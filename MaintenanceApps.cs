using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FileExplorer;

// Windows apps inventory and uninstall (MAINT-32..35). Sources, quiet switches and
// bulk order follow Bulk Crap Uninstaller 30da609 (Apache-2.0), re-implemented here;
// the cited originals are pinned under vendor/BulkCrapUninstaller. No donor code runs.
internal sealed record InstalledApp(string Id,string Name,string Publisher,string Version,string Source,string Scope,long? Bytes,string Installed,
    string Uninstall,string Quiet,string Location,string Key,string Kind,string Package="",string ProductCode="",string Protection="")
{
    internal bool Silent(bool quiet)=>Source is not MaintenanceApps.Desktop||quiet&&Quiet.Length>0;
    internal string Summary=>Protection.Length>0?"Protected · "+Protection:Kind+(Quiet.Length>0&&Source==MaintenanceApps.Desktop?" · quiet uninstall available":"");
    internal MaintenanceRow Row(string state="Installed")=>new(Id,Name,Scope,state,Details(),Bytes,Source,CanChange:Protection.Length==0&&state=="Installed",
        Publisher:Publisher,Version:Version,Installed:Installed,Package:Package);
    string Details()=>string.Join("\n",new[]{Summary,"Uninstall: "+(Uninstall.Length>0?Uninstall:"none registered"),Quiet.Length>0&&Source==MaintenanceApps.Desktop?"Quiet uninstall: "+Quiet:"",
        Location.Length>0?"Location: "+Location:"",(Source==MaintenanceApps.Store?"Package: ":Source==MaintenanceApps.Desktop?"Registry: ":"Manager entry: ")+Key}.Where(x=>x.Length>0));
}
internal static class MaintenanceApps
{
    internal const string Desktop="Desktop program",Store="Store app",Scoop="Scoop",Chocolatey="Chocolatey";
    const string UninstallPath=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
#if DEBUG
    // Owned HKCU fixture root: when set, only its Uninstall key is read (no Store/Scoop/Chocolatey).
    // Inherited by a non-elevated worker so its re-scan sees the same fixture.
    internal static string FixtureRootForVerification=Environment.GetEnvironmentVariable("PATHWEFT_APPS_FIXTURE") is {Length:>0} root?root:null;
#endif
    static IEnumerable<(RegistryHive Hive,RegistryView View,string Path)> Roots()
    {
#if DEBUG
        if(FixtureRootForVerification!=null){yield return(RegistryHive.CurrentUser,RegistryView.Registry64,FixtureRootForVerification+@"\Uninstall");yield break;}
#endif
        yield return(RegistryHive.LocalMachine,RegistryView.Registry64,UninstallPath);
        yield return(RegistryHive.LocalMachine,RegistryView.Registry32,UninstallPath);
        yield return(RegistryHive.CurrentUser,RegistryView.Registry64,UninstallPath);
        yield return(RegistryHive.CurrentUser,RegistryView.Registry64,@"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
    }
    // Read-only and unelevated. A failing source is reported, not fatal to the others.
    internal static InstalledApp[] Read(CancellationToken cancel,string source=null)
    {
        var apps=new List<InstalledApp>();
        if(source is null or Desktop)apps.AddRange(Registry(cancel));
#if DEBUG
        if(FixtureRootForVerification!=null)return apps.ToArray();
#endif
        if(source is null or Store)apps.AddRange(StorePackages(cancel));
        if(source is null or Scoop)apps.AddRange(ScoopApps());
        if(source is null or Chocolatey)apps.AddRange(ChocolateyPackages());
        return apps.ToArray();
    }
    static IEnumerable<InstalledApp> Registry(CancellationToken cancel)
    {
        foreach(var (hive,view,path) in Roots())
        {
            using var root=RegistryKey.OpenBaseKey(hive,view);using var key=root.OpenSubKey(path);if(key==null)continue;
            foreach(string name in key.GetSubKeyNames())
            {
                cancel.ThrowIfCancellationRequested();
                InstalledApp app;
                try{using var entry=key.OpenSubKey(name);app=entry==null?null:Entry(entry,hive,view,path,name);}
                catch(Exception error)when(error is System.Security.SecurityException or UnauthorizedAccessException or IOException){continue;}
                if(app!=null)yield return app;
            }
        }
    }
    // Folding follows BCU RegistryFactory: no name, SystemComponent, ParentKeyName or update release types are not apps.
    internal static InstalledApp Entry(RegistryKey entry,RegistryHive hive,RegistryView view,string path,string name)
    {
        string display=Text(entry,"DisplayName");
        if(display.Length==0||Number(entry,"SystemComponent")==1||Text(entry,"ParentKeyName").Length>0||
           Text(entry,"ReleaseType") is "Update" or "Hotfix" or "Security Update" or "Update Rollup")return null;
        string uninstall=Text(entry,"UninstallString"),quiet=Text(entry,"QuietUninstallString"),location=Text(entry,"InstallLocation").Trim().Trim('"');
        bool msi=Number(entry,"WindowsInstaller")==1||uninstall.Contains("msiexec",StringComparison.OrdinalIgnoreCase);
        string code=!msi?"":Guid.TryParse(name,out var guid)?guid.ToString("B").ToUpperInvariant():Regex.Match(uninstall,@"\{[0-9A-Fa-f-]{36}\}").Value.ToUpperInvariant();
        string kind="Other installer";
        if(msi&&code.Length>0)
        {
            // BCU MsiUninstallStringGenerator: /X, never the /I (modify) form some entries register.
            kind="Windows Installer";uninstall="MsiExec.exe /X"+code;if(quiet.Length==0)quiet="MsiExec.exe /X"+code+" /qb REBOOT=ReallySuppress /norestart";
        }
        else if(name.EndsWith("_is1",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(uninstall,@"unins\d{3}\.exe",RegexOptions.IgnoreCase))
        {
            // BCU InnoSetupQuietUninstallStringGenerator.
            kind="Inno Setup";if(quiet.Length==0&&uninstall.Length>0)quiet=Quote(Split(uninstall).File)+" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        }
        long kilobytes=Number(entry,"EstimatedSize");string date=Text(entry,"InstallDate");
        string installed=DateTime.TryParseExact(date,"yyyyMMdd",null,System.Globalization.DateTimeStyles.None,out var day)?day.ToString("yyyy-MM-dd"):"";
        string protection=Number(entry,"NoRemove")==1?"Windows marks this entry as not removable":
            uninstall.Length==0?"No uninstall command is registered":
            Self(display,location)?"This is Pathweft; uninstall it from Windows Settings":"";
        string hiveName=hive==RegistryHive.LocalMachine?"HKEY_LOCAL_MACHINE":"HKEY_CURRENT_USER";
        return new("app:reg:"+Maintenance.Id(hiveName+"/"+view+"/"+path,name),display.Trim(),Text(entry,"Publisher").Trim(),Text(entry,"DisplayVersion").Trim(),Desktop,
            hive==RegistryHive.LocalMachine?"This computer":"Current user",kilobytes>0?kilobytes*1024:null,installed,uninstall,quiet,location,
            hiveName+"\\"+path+"\\"+name+(view==RegistryView.Registry32&&hive==RegistryHive.LocalMachine?" (32-bit view)":""),kind,ProductCode:code,Protection:protection);
    }
    static bool Self(string name,string location)=>name.StartsWith("Pathweft",StringComparison.OrdinalIgnoreCase)||
        location.Length>0&&Path.IsPathFullyQualified(location)&&Inside(AppContext.BaseDirectory,location);
    internal static bool Inside(string path,string folder)
    {
        string a=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),b=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(b+"\\",StringComparison.OrdinalIgnoreCase);
    }
    static string Text(RegistryKey key,string name)=>key.GetValue(name) switch{string s=>s,_=>""};
    static long Number(RegistryKey key,string name)=>key.GetValue(name) switch{int i=>(uint)i,long l=>l,string s when long.TryParse(s,out long v)=>v,_=>0};
    // BCU StoreAppHelper: main packages for the current user, Disabled/NotAvailable skipped, System-signed protected.
    static IEnumerable<InstalledApp> StorePackages(CancellationToken cancel)
    {
        var found=new List<InstalledApp>();
        foreach(var package in new Windows.Management.Deployment.PackageManager().FindPackagesForUserWithPackageTypes("",Windows.Management.Deployment.PackageTypes.Main))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if(package.Status.Disabled||package.Status.NotAvailable)continue;
                var id=package.Id;var v=id.Version;string family=id.FamilyName;
                string display=Try(()=>package.DisplayName);if(display.Length==0||display.StartsWith("ms-resource:",StringComparison.OrdinalIgnoreCase))display=id.Name;
                string location=Try(()=>package.InstalledLocation.Path);
                string installed=Try(()=>package.InstalledDate.ToLocalTime().ToString("yyyy-MM-dd"));
                string protection=package.SignatureKind==Windows.ApplicationModel.PackageSignatureKind.System?"Windows system package":"";
                found.Add(new("app:store:"+family,display,Try(()=>package.PublisherDisplayName),$"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",Store,"Current user",null,installed,
                    "Remove the package for the current user","",location,id.FullName,"Store package",Package:id.Name,Protection:protection));
            }
            catch(Exception error)when(error is COMException or UnauthorizedAccessException or FileNotFoundException){}
        }
        return found.DistinctBy(a=>a.Id);
    }
    static string Try(Func<string> read){try{return read()??"";}catch(Exception error)when(error is COMException or UnauthorizedAccessException or FileNotFoundException or ArgumentException){return "";}}
    // BCU ScoopFactory: <root>\apps\<name>\current; Scoop itself is managed by Scoop's own uninstaller.
    static IEnumerable<InstalledApp> ScoopApps()
    {
        string apps=Path.Combine(ScoopCleanup.Locations().Root,"apps");if(!Directory.Exists(apps))yield break;
        foreach(var folder in new DirectoryInfo(apps).EnumerateDirectories())
        {
            string current=Path.Combine(folder.FullName,"current"),version="";
            if(!Directory.Exists(current))continue;
            try{using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(current,"manifest.json")));if(manifest.RootElement.TryGetProperty("version",out var value))version=value.GetString()??"";}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException){}
            string name=folder.Name;
            yield return new("app:scoop:"+name.ToLowerInvariant(),name,"",version,Scoop,"Current user",null,folder.CreationTime.ToString("yyyy-MM-dd"),"scoop uninstall "+name,"",
                folder.FullName,name,"Scoop app",Protection:name.Equals("scoop",StringComparison.OrdinalIgnoreCase)?"Scoop itself; remove it with Scoop's own uninstall":"");
        }
    }
    internal static string ChocolateyRoot=>Environment.GetEnvironmentVariable("ChocolateyInstall") is {Length:>0} root?root:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"chocolatey");
    // BCU ChocolateyFactory lists through choco; reading lib\<id>\<id>.nuspec gives the same identities without a process.
    static IEnumerable<InstalledApp> ChocolateyPackages()
    {
        string lib=Path.Combine(ChocolateyRoot,"lib");if(!Directory.Exists(lib))yield break;
        foreach(var folder in new DirectoryInfo(lib).EnumerateDirectories())
        {
            string nuspec=Path.Combine(folder.FullName,folder.Name+".nuspec");if(!File.Exists(nuspec))continue;
            string title=folder.Name,version="",authors="";
            try
            {
                var metadata=XDocument.Load(nuspec).Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="metadata");
                string Value(string name)=>metadata?.Elements().FirstOrDefault(e=>e.Name.LocalName==name)?.Value.Trim()??"";
                if(Value("title").Length>0)title=Value("title");version=Value("version");authors=Value("authors");
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Xml.XmlException){}
            string id=folder.Name;
            yield return new("app:choco:"+id.ToLowerInvariant(),title,authors,version,Chocolatey,"This computer",null,folder.CreationTime.ToString("yyyy-MM-dd"),"choco uninstall "+id+" -y","",
                folder.FullName,id,"Chocolatey package",Protection:id.Equals("chocolatey",StringComparison.OrdinalIgnoreCase)?"Chocolatey itself":"");
        }
    }
    // BCU SortIntelligently: loud before silent, size bucket, Windows Installer first, then larger first.
    internal static InstalledApp[] Order(IEnumerable<InstalledApp> apps,bool quiet)=>apps
        .OrderBy(a=>a.Silent(quiet))
        .ThenByDescending(a=>Math.Round(Math.Floor(Math.Log10(Math.Max(1,(a.Bytes??0)/1024d))+1)/4))
        .ThenByDescending(a=>a.Kind=="Windows Installer")
        .ThenByDescending(a=>a.Bytes??0).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    // A quoted executable, else the shortest existing ".exe" prefix (unquoted paths with spaces), else the first token.
    internal static (string File,string Arguments) Split(string command)
    {
        command=Environment.ExpandEnvironmentVariables(command.Trim());
        if(command.StartsWith('"')){int end=command.IndexOf('"',1);if(end>1)return(command[1..end],command[(end+1)..].Trim());}
        for(int i=command.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);i>=0;i=command.IndexOf(".exe",i+4,StringComparison.OrdinalIgnoreCase))
        {
            string file=command[..(i+4)];bool boundary=i+4==command.Length||command[i+4]==' ';
            if(boundary&&Path.IsPathFullyQualified(file)&&File.Exists(file))return(file,command[(i+4)..].Trim());
        }
        int space=command.IndexOf(' ');return space<0?(command,""):(command[..space],command[(space+1)..].Trim());
    }
    static string Quote(string file)=>"\""+file+"\"";
    static string Resolve(string file)
    {
        if(Path.IsPathFullyQualified(file))return file;
        string system=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),file);
        return File.Exists(system)?system:file;
    }
    // MAINT-35: a Windows Installer uninstall never starts while another installation holds this mutex.
    internal static bool InstallerBusy()
    {
        try{if(Mutex.TryOpenExisting(@"Global\_MSIExecute",out var mutex)){mutex.Dispose();return true;}return false;}
        catch(UnauthorizedAccessException){return true;}
    }
    internal sealed record Outcome(string State,string Message);
    // Re-reads the exact entry by identity before and after; the caller's copy is never trusted for the command.
    internal static async Task<Outcome> Uninstall(string id,bool quiet,CancellationToken cancel)
    {
        string source=id.StartsWith("app:store:")?Store:id.StartsWith("app:scoop:")?Scoop:id.StartsWith("app:choco:")?Chocolatey:Desktop;
        var app=await Task.Run(()=>Read(cancel,source).SingleOrDefault(a=>a.Id==id),cancel);
        if(app==null)return new("Removed","It was no longer installed.");
        if(app.Protection.Length>0)throw new InvalidOperationException(app.Name+" is protected: "+app.Protection+".");
        int exit=0;
        try
        {
            switch(source)
            {
                case Store:
                    // Current user only; no all-users removal or deprovisioning (MAINT-34).
                    var result=await new Windows.Management.Deployment.PackageManager().RemovePackageAsync(app.Key).AsTask();
                    if(result.ExtendedErrorCode!=null&&result.ExtendedErrorCode.HResult!=0)return Check("Still installed",result.ErrorText is {Length:>0} text?text:result.ExtendedErrorCode.Message);
                    break;
                case Scoop:
                    await MaintenanceWorker.WindowsAction("ScoopUninstall",app.Key,cancel);break;
                case Chocolatey:
                    exit=await Launch(Find("choco.exe",Path.Combine(ChocolateyRoot,"bin","choco.exe")),"uninstall "+app.Key+" -y",true,cancel);break;
                default:
                    if(app.Kind=="Windows Installer"&&InstallerBusy())return new("Not started","Another Windows Installer operation is running. Try this app again when it finishes.");
                    var (file,arguments)=Split(quiet&&app.Quiet.Length>0?app.Quiet:app.Uninstall);
                    exit=await Launch(Resolve(file),arguments,false,cancel);break;
            }
        }
        catch(Win32Exception error)when(error.NativeErrorCode==1223){return new("Cancelled","Administrator permission was declined. Nothing changed.");}
        catch(OperationCanceledException){return new("Not waited","Pathweft stopped waiting; the uninstaller may still be open.");}
        if(exit is 1602 or 1223)return Check("Cancelled","The uninstaller was cancelled.");
        if(exit==1618)return Check("Not started","Another installation is in progress. Try again when it finishes.");
        return Check("Still installed",exit is 0 or 3010 or 1641?"The uninstaller finished but the app is still registered.":$"The uninstaller ended with code {exit}.");
        Outcome Check(string otherwise,string message)
        {
            bool present=Read(CancellationToken.None,source).Any(a=>a.Id==id);
            return present?new(otherwise,message):new("Removed",exit is 3010 or 1641?"Removed. Windows needs a restart to finish.":"Removed.");
        }
    }
    static string Find(string name,string fallback)
    {
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';',StringSplitOptions.RemoveEmptyEntries))
        {try{string path=Path.Combine(folder.Trim('"'),name);if(Path.IsPathFullyQualified(path)&&File.Exists(path))return path;}catch(ArgumentException){}}
        return File.Exists(fallback)?fallback:throw new FileNotFoundException(name+" was not found");
    }
    // ShellExecute lets the uninstaller request elevation itself, as Windows Settings does.
    // Waits for the process and descendants it starts (Inno/NSIS copy themselves to Temp and relaunch).
    static async Task<int> Launch(string file,string arguments,bool elevate,CancellationToken cancel)
    {
        var start=new ProcessStartInfo(file,arguments){UseShellExecute=true,Verb=elevate?"runas":"",WorkingDirectory=Path.GetDirectoryName(file) is {Length:>0} folder&&Directory.Exists(folder)?folder:Environment.SystemDirectory};
        using var process=Process.Start(start)??throw new IOException("The uninstaller did not start");
        int exit;
        await process.WaitForExitAsync(cancel);exit=process.ExitCode;
        await Descendants(process.Id,Created(process.Id),cancel);
        return exit;
    }
    // No Windows event reports descendants of an elevated process we cannot place in a job, so the
    // process table is re-read at a fixed observation cadence until every tracked descendant has exited.
    static async Task Descendants(int root,long created,CancellationToken cancel)
    {
        var known=new Dictionary<int,long>{[root]=created};var alive=new HashSet<int>();
        while(true)
        {
            alive.Clear();
            foreach(var (pid,parent) in Snapshot())
            {
                if(known.TryGetValue(pid,out long seen)){long now=Created(pid);if(now==seen||now==0)alive.Add(pid);else known.Remove(pid);continue;}
                if(known.TryGetValue(parent,out long parentCreated)){long time=Created(pid);if(time==0||time>=parentCreated){known[pid]=time;alive.Add(pid);}}
            }
            alive.Remove(root);
            if(alive.Count==0)return;
            await Task.Delay(500,cancel);
        }
    }
    static long Created(int pid)
    {
        IntPtr handle=OpenProcess(0x1000,false,pid);if(handle==IntPtr.Zero)return 0;
        try{return GetProcessTimes(handle,out long creation,out _,out _,out _)?creation:0;}finally{CloseHandle(handle);}
    }
    static List<(int Pid,int Parent)> Snapshot()
    {
        var list=new List<(int,int)>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==new IntPtr(-1))throw new Win32Exception();
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
            for(bool more=Process32FirstW(snapshot,ref entry);more;more=Process32NextW(snapshot,ref entry))list.Add(((int)entry.Pid,(int)entry.Parent));
        }
        finally{CloseHandle(snapshot);}
        return list;
    }
    // Uninstaller signature: Authenticode (WinVerifyTrust, no network revocation) plus the signer's name.
    internal static string Signature(InstalledApp app)
    {
        if(app.Source!=Desktop)return app.Source==Store?"Signed Store package":"Package manager command";
        if(app.Kind=="Windows Installer")return "Windows Installer (msiexec, signed by Microsoft)";
        string file=Resolve(Split(app.Uninstall).File);
        if(!File.Exists(file))return "Uninstaller not found: "+file;
        int trust=Verify(file);
        if(trust==unchecked((int)0x800B0100))return "Uninstaller is not signed";
        string signer="";
        try{var subject=System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file).Subject;signer=Regex.Match(subject,@"CN=(""[^""]+""|[^,]+)").Groups[1].Value.Trim('"');}
        catch(System.Security.Cryptography.CryptographicException){}
        return trust==0?"Uninstaller signed by "+(signer.Length>0?signer:"a trusted publisher"):"Uninstaller signature is not valid"+(signer.Length>0?" ("+signer+")":"");
    }
    static int Verify(string file)
    {
        var fileInfo=new TrustFile{Size=(uint)Marshal.SizeOf<TrustFile>(),Path=file};
        IntPtr info=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(fileInfo,info,false);
            var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),UiChoice=2,RevocationChecks=0,UnionChoice=1,File=info,StateAction=0,ProviderFlags=0x1000|0x10};
            var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            return WinVerifyTrust(new IntPtr(-1),ref action,ref data);
        }
        finally{Marshal.DestroyStructure<TrustFile>(info);Marshal.FreeHGlobal(info);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct TrustFile{internal uint Size;[MarshalAs(UnmanagedType.LPWStr)]internal string Path;internal IntPtr Handle,Subject;}
    [StructLayout(LayoutKind.Sequential)]struct TrustData{internal uint Size;internal IntPtr Policy,Sip;internal uint UiChoice,RevocationChecks,UnionChoice;internal IntPtr File;internal uint StateAction;internal IntPtr State,Url;internal uint ProviderFlags,UiContext;internal IntPtr Signature;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessEntry{internal uint Size,Usage,Pid;internal IntPtr Heap;internal uint Module,Threads,Parent;internal int Priority;internal uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]internal string Exe;}
    [DllImport("wintrust.dll",CharSet=CharSet.Unicode)]static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
}
