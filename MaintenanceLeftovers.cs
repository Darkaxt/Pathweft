using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FileExplorer;

// What a removed app left behind (MAINT-36). Scanners and confidence follow Bulk Crap Uninstaller's
// junk finders (InstallLocation, UninstallerLocation, CommonDrive, Shortcut, Startup, registry keys) and
// ConfidenceGenerators, re-implemented and narrowed; nothing inside Windows or another app is offered.
internal sealed record Leftover(string Kind,string Path,string Value,string Confidence,string Reason,bool Administrator)
{
    internal string Identity=>Kind+"|"+Path+"|"+Value;
    internal string Display=>Value is {Length:>0}?Path+" · "+Value:Path;
}
internal sealed record LeftoverOutcome(int Removed,int Skipped,string Backup,string Details);
internal static class MaintenanceLeftovers
{
    internal const string Likely="Likely",Possible="Possible",Unlikely="Unlikely";
    internal static string BackupRoot=>Path.Combine(DataFolder.Bulk,"Maintenance","Leftovers");
    const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
#if DEBUG
    // Owned fixture roots: folders, shortcut folders and an HKCU key standing in for HKCU/HKLM\Software.
    internal static (string[] Folders,string[] Shortcuts,string Software,string Run)? FixturesForVerification=
        Environment.GetEnvironmentVariable("PATHWEFT_LEFTOVERS_FIXTURE")?.Split('|') is [var folders,var shortcuts,var software,var run]?(new[]{folders},new[]{shortcuts},software,run):null;
#endif
    static string Local=>Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string[] Folders()
    {
#if DEBUG
        if(FixturesForVerification is {} fixture)return fixture.Folders;
#endif
        return new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Local,Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Path.GetDirectoryName(Local)??Local,"LocalLow"),Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)}
            .Where(x=>x.Length>0&&Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static string[] ShortcutFolders()
    {
#if DEBUG
        if(FixturesForVerification is {} fixture)return fixture.Shortcuts;
#endif
        return new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}
            .Where(x=>x.Length>0&&Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // (hive name, software path) pairs searched for product and publisher keys.
    static (RegistryHive Hive,string Path)[] SoftwareRoots()
    {
#if DEBUG
        if(FixturesForVerification is {} fixture)return[(RegistryHive.CurrentUser,fixture.Software)];
#endif
        return[(RegistryHive.CurrentUser,"Software"),(RegistryHive.LocalMachine,"SOFTWARE"),(RegistryHive.LocalMachine,@"SOFTWARE\WOW6432Node")];
    }
    static (RegistryHive Hive,string Path)[] RunRoots()
    {
#if DEBUG
        if(FixturesForVerification is {} fixture)return[(RegistryHive.CurrentUser,fixture.Run)];
#endif
        return[(RegistryHive.CurrentUser,RunPath),(RegistryHive.LocalMachine,RunPath),(RegistryHive.LocalMachine,@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")];
    }
    static readonly HashSet<string> Generic=new(StringComparer.OrdinalIgnoreCase){"microsoft","windows","common","common files","update","updater","program","programs","system","tools","google","apps","app","software","data","temp","cache","packages","installer","setup","x64","x86"};
    // BCU-style trimming: versions, architecture and company suffixes do not identify a product.
    internal static string Clean(string text)
    {
        text=Regex.Replace(text.ToLowerInvariant(),@"\(.*?\)|\bv?\d+(\.\d+)+\b|\b(x64|x86|64-bit|32-bit|amd64|version|inc|ltd|llc|gmbh|corp|corporation|co|limited|s\.?a)\b\.?|[_,]"," ");
        return Regex.Replace(text,@"\s+"," ").Trim(' ','.','-');
    }
    // 0 perfect, 1 dodgy (one name begins with the other), -1 no match (BCU MatchStringToProductName, narrowed).
    internal static int Match(string candidate,string product)
    {
        string a=Clean(candidate),b=Clean(product);if(a.Length<3||b.Length<3)return -1;
        if(a==b||a.Replace(" ","")==b.Replace(" ",""))return 0;
        if(a.Length>=4&&b.Length>=4&&(b.StartsWith(a+" ")||a.StartsWith(b+" ")))return 1;
        return -1;
    }
    internal static Leftover[] Scan(InstalledApp app,CancellationToken cancel)
    {
        var found=new Dictionary<string,Leftover>(StringComparer.OrdinalIgnoreCase);
        var installed=MaintenanceApps.Read(cancel).Where(a=>a.Id!=app.Id).Select(a=>a.Location).Where(l=>l.Length>3&&Path.IsPathFullyQualified(l)).Select(Path.GetFullPath).ToArray();
        var running=RunningImages();
        string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string publisher=Clean(app.Publisher);bool microsoft=publisher.StartsWith("microsoft");
        var removed=new List<string>();
        void Folder(string path,string confidence,string reason)
        {
            cancel.ThrowIfCancellationRequested();
            if(!Directory.Exists(path)||Excluded(path,installed,running))return;
            removed.Add(path);Add(new("Folder",Path.GetFullPath(path),null,confidence,reason,!MaintenanceApps.Inside(path,profile)));
        }
        void Add(Leftover item)
        {
            if(!found.TryGetValue(item.Identity,out var prior)||Rank(item.Confidence)>Rank(prior.Confidence))found[item.Identity]=item;
        }
        if(app.Source==MaintenanceApps.Desktop)
        {
            if(app.Location.Length>3&&Path.IsPathFullyQualified(app.Location))Folder(app.Location,Likely,"Its registered install folder");
            string uninstaller=MaintenanceApps.Split(app.Uninstall).File;
            if(app.Kind!="Windows Installer"&&Path.IsPathFullyQualified(uninstaller)&&Path.GetDirectoryName(uninstaller) is {} folder)Folder(folder,Likely,"The folder of its uninstaller");
            foreach(string root in Folders())
                foreach(var child in Children(root))
                {
                    string name=Path.GetFileName(child);int product=Match(name,app.Name);
                    if(product==0)Folder(child,Likely,"Named after the product directly in "+Path.GetFileName(root));
                    else if(product==1)Folder(child,Possible,"Name resembles the product in "+Path.GetFileName(root));
                    else if(publisher.Length>=3&&!microsoft&&Match(name,app.Publisher)==0)
                    {
                        var inner=Children(child).ToArray();
                        foreach(string sub in inner)if(Match(Path.GetFileName(sub),app.Name) is int m&&m>=0)Folder(sub,m==0?Likely:Possible,"Named after the product inside its publisher's folder");
                        if(inner.All(sub=>Match(Path.GetFileName(sub),app.Name)>=0)&&NoFiles(child))Folder(child,Possible,"Publisher folder holding nothing else");
                    }
                    else if(Clean(app.Name).Split(' ')[0] is {Length:>=5} word&&!Generic.Contains(word)&&Clean(name).Split(' ')[0]==word)Folder(child,Unlikely,"Shares the first word of the product name");
                }
            foreach(string root in ShortcutFolders())
                foreach(string link in Safe(()=>Directory.EnumerateFiles(root,"*.lnk",SearchOption.AllDirectories)))
                {
                    cancel.ThrowIfCancellationRequested();
                    string target=Target(link);
                    if(target.Length>3&&Path.IsPathFullyQualified(target)&&!File.Exists(target)&&!Directory.Exists(target)&&removed.Concat(new[]{app.Location}).Any(r=>r.Length>3&&MaintenanceApps.Inside(target,r)))
                        Add(new("Shortcut",link,null,Likely,"Points into the removed app's folder",!MaintenanceApps.Inside(link,profile)));
                }
            foreach(var (hive,path) in SoftwareRoots())
            {
                using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var software=Try(()=>root.OpenSubKey(path));if(software==null)continue;
                string hiveName=hive==RegistryHive.LocalMachine?"HKEY_LOCAL_MACHINE":"HKEY_CURRENT_USER";
                foreach(string name in Try(()=>software.GetSubKeyNames())??[])
                {
                    if(Match(name,app.Name)==0)Add(new("Registry key",hiveName+"\\"+path+"\\"+name,null,Likely,"Settings key named after the product",hive==RegistryHive.LocalMachine));
                    else if(publisher.Length>=3&&!microsoft&&Match(name,app.Publisher)==0)
                    {
                        using var company=Try(()=>software.OpenSubKey(name));var subs=Try(()=>company?.GetSubKeyNames())??[];
                        foreach(string sub in subs)if(Match(sub,app.Name) is int m&&m>=0)Add(new("Registry key",hiveName+"\\"+path+"\\"+name+"\\"+sub,null,m==0?Likely:Possible,"Settings key of the product under its publisher",hive==RegistryHive.LocalMachine));
                        if(subs.Length>0&&subs.All(sub=>Match(sub,app.Name)>=0)&&company?.ValueCount==0)Add(new("Registry key",hiveName+"\\"+path+"\\"+name,null,Possible,"Publisher key holding nothing else",hive==RegistryHive.LocalMachine));
                    }
                }
            }
            foreach(var (hive,path) in RunRoots())
            {
                using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var run=Try(()=>root.OpenSubKey(path));if(run==null)continue;
                foreach(string value in run.GetValueNames())
                {
                    string command=run.GetValue(value,"",RegistryValueOptions.DoNotExpandEnvironmentNames) as string??"";
                    string file=MaintenanceApps.Split(command).File;
                    if(Path.IsPathFullyQualified(file)&&!File.Exists(file)&&removed.Concat(new[]{app.Location}).Any(r=>r.Length>3&&MaintenanceApps.Inside(file,r)))
                        Add(new("Startup value",(hive==RegistryHive.LocalMachine?"HKEY_LOCAL_MACHINE":"HKEY_CURRENT_USER")+"\\"+path,value,Likely,"Starts a program from the removed app's folder",hive==RegistryHive.LocalMachine));
                }
            }
        }
        else if(app.Source==MaintenanceApps.Store)
        {
            string family=app.Id["app:store:".Length..];
            Folder(Path.Combine(Local,"Packages",family),Likely,"The package's data folder");
        }
        else if(app.Source==MaintenanceApps.Scoop)
        {
            string persist=Path.Combine(ScoopCleanup.Locations().Root,"persist",app.Key);
            Folder(persist,Likely,"Scoop's persisted data for this app");
        }
        return found.Values.OrderByDescending(x=>Rank(x.Confidence)).ThenBy(x=>x.Kind).ThenBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    static bool NoFiles(string folder){try{return !Directory.EnumerateFiles(folder).Any();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}}
    static int Rank(string confidence)=>confidence switch{Likely=>3,Possible=>2,_=>1};
    static IEnumerable<string> Children(string root)=>Safe(()=>Directory.EnumerateDirectories(root));
    static IEnumerable<string> Safe(Func<IEnumerable<string>> list)
    {
        try{return list().ToArray();}catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){return [];}
    }
    static T Try<T>(Func<T> read)where T:class{try{return read();}catch(Exception error)when(error is System.Security.SecurityException or UnauthorizedAccessException or IOException){return null;}}
    // Never Windows, a known root itself, a folder holding (or inside) another registered app, or one with a running program.
    static bool Excluded(string path,string[] installed,string[] running)
    {
        string full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if(Path.GetPathRoot(full)?.TrimEnd('\\').Equals(full,StringComparison.OrdinalIgnoreCase)!=false)return true;
        if(MaintenanceApps.Inside(full,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))return true;
        string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(Folders().Concat(ShortcutFolders()).Append(profile).Append(Path.GetDirectoryName(profile)??profile).Any(r=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)).Equals(full,StringComparison.OrdinalIgnoreCase)))return true;
        if(MaintenanceApps.Inside(full,AppContext.BaseDirectory)||MaintenanceApps.Inside(AppContext.BaseDirectory,full)||MaintenanceApps.Inside(full,DataFolder.Bulk)||MaintenanceApps.Inside(DataFolder.Bulk,full))return true;
        if(installed.Any(l=>MaintenanceApps.Inside(l,full)||MaintenanceApps.Inside(full,l)))return true;
        return running.Any(image=>MaintenanceApps.Inside(image,full));
    }
    static string[] RunningImages()
    {
        var images=new List<string>();
        foreach(var process in Process.GetProcesses())
            using(process)
            {
                IntPtr handle=OpenProcess(0x1000,false,process.Id);if(handle==IntPtr.Zero)continue;
                try{var text=new StringBuilder(1024);int size=text.Capacity;if(QueryFullProcessImageName(handle,0,text,ref size))images.Add(text.ToString());}
                finally{CloseHandle(handle);}
            }
        return images.ToArray();
    }
    // The stored target path, without resolving or searching for a moved target.
    internal static string Target(string link)
    {
        var shell=(MainWindow.IShellLinkW)new MainWindow.ShellLink();IntPtr buffer=Marshal.AllocHGlobal(2048);
        try
        {
            Marshal.WriteInt16(buffer,0);((System.Runtime.InteropServices.ComTypes.IPersistFile)shell).Load(link,0);
            shell.GetPath(buffer,1024,IntPtr.Zero,0x4);return Marshal.PtrToStringUni(buffer)??"";
        }
        catch(Exception error)when(error is COMException or UnauthorizedAccessException or IOException or ArgumentException){return "";}
        finally{Marshal.FreeHGlobal(buffer);Marshal.ReleaseComObject(shell);}
    }
    // Re-scans with the same identity and removes only requested items it finds again. Registry keys and
    // values are exported to a .reg backup first; files and folders go to the Recycle Bin.
    internal static LeftoverOutcome Remove(InstalledApp app,IEnumerable<string> requested,bool administrator,string backupFolder,CancellationToken cancel)
    {
        RequireBackupFolder(backupFolder);
        var wanted=requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items=Scan(app,cancel).Where(x=>wanted.Contains(x.Identity)&&x.Administrator==administrator).ToArray();
        int removed=0,skipped=wanted.Count-items.Length;var details=new StringBuilder();string backup=null;
        var registry=items.Where(x=>x.Kind is "Registry key" or "Startup value").ToArray();
        if(registry.Length>0)
        {
            Directory.CreateDirectory(backupFolder);
            backup=Path.Combine(backupFolder,(administrator?"machine-":"user-")+DateTime.UtcNow.Ticks+".reg");
            var text=new StringBuilder("Windows Registry Editor Version 5.00\r\n");
            foreach(var item in registry)text.Append("\r\n").Append(item.Kind=="Registry key"?Export(item.Path):ExportValue(item.Path,item.Value));
            File.WriteAllText(backup,text.ToString(),Encoding.Unicode);
        }
        foreach(var item in registry)
        {
            try
            {
                var (hive,path)=Parse(item.Path);using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);
                if(item.Kind=="Registry key")root.DeleteSubKeyTree(path,false);
                else{using var key=root.OpenSubKey(path,true);key?.DeleteValue(item.Value,false);}
                removed++;
            }
            catch(Exception error)when(error is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
            {skipped++;details.AppendLine(item.Display+": "+error.Message);}
        }
        var files=items.Where(x=>x.Kind is "Folder" or "Shortcut").Select(x=>x.Path).Where(p=>Directory.Exists(p)||File.Exists(p)).ToList();
        if(files.Count>0)
        {
            cancel.ThrowIfCancellationRequested();
            // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI: the Recycle Bin, no shell prompts.
            string error=MainWindow.ShellDelete(IntPtr.Zero,files,0x40|0x10|0x4|0x400);
            foreach(string path in files)
                if(Directory.Exists(path)||File.Exists(path)){skipped++;details.AppendLine(path+": still present"+(error!=null?" ("+error+")":""));}else removed++;
        }
        return new(removed,skipped,backup,details.ToString().Trim());
    }
    internal static void RequireBackupFolder(string folder)
    {
#if !DEBUG
        if(!MaintenanceApps.Inside(folder,BackupRoot)||MaintenanceApps.Inside(BackupRoot,folder))throw new ArgumentException("Leftover backup is outside this profile's Maintenance folder");
#endif
    }
    // Restore imports a backup written by Remove (reg.exe import, the documented .reg merge).
    internal static async Task Restore(string file,CancellationToken cancel)
    {
        RequireBackupFolder(Path.GetDirectoryName(file));
        if(!File.Exists(file))throw new FileNotFoundException("The registry backup is missing",file);
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"reg.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        start.ArgumentList.Add("import");start.ArgumentList.Add(file);
        using var process=Process.Start(start)??throw new IOException("reg.exe did not start");
        string error=await process.StandardError.ReadToEndAsync(cancel);await process.WaitForExitAsync(cancel);
        if(process.ExitCode!=0)throw new IOException("Registry import failed: "+error.Trim());
    }
    static (RegistryHive Hive,string Path) Parse(string path)
    {
        int slash=path.IndexOf('\\');string hive=path[..slash],rest=path[(slash+1)..];
        return(hive switch{"HKEY_CURRENT_USER"=>RegistryHive.CurrentUser,"HKEY_LOCAL_MACHINE"=>RegistryHive.LocalMachine,_=>throw new ArgumentException("Unsupported registry hive")},rest);
    }
    // Writes .reg text directly (no reg.exe export) so values keep their exact types.
    static string Export(string path)
    {
        var (hive,sub)=Parse(path);using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var key=root.OpenSubKey(sub);
        var text=new StringBuilder();if(key!=null)Write(key,path,text);return text.ToString();
        static void Write(RegistryKey key,string name,StringBuilder text)
        {
            text.Append('[').Append(name).Append("]\r\n");
            foreach(string value in key.GetValueNames())text.Append(Line(key,value));
            text.Append("\r\n");
            foreach(string child in key.GetSubKeyNames()){using var sub=key.OpenSubKey(child);if(sub!=null)Write(sub,name+"\\"+child,text);}
        }
    }
    static string ExportValue(string path,string value)
    {
        var (hive,sub)=Parse(path);using var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64);using var key=root.OpenSubKey(sub);
        return key==null?"":"["+path+"]\r\n"+Line(key,value)+"\r\n";
    }
    static string Line(RegistryKey key,string value)
    {
        string name=value.Length==0?"@":"\""+Escape(value)+"\"";var kind=key.GetValueKind(value);object data=key.GetValue(value,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
        string Hex(byte[] bytes)=>string.Join(",",bytes.Select(b=>b.ToString("x2")));
        return name+"="+kind switch
        {
            RegistryValueKind.String=>"\""+Escape((string)data)+"\"",
            RegistryValueKind.DWord=>"dword:"+unchecked((uint)(int)data).ToString("x8"),
            RegistryValueKind.QWord=>"hex(b):"+Hex(BitConverter.GetBytes((long)data)),
            RegistryValueKind.ExpandString=>"hex(2):"+Hex(Encoding.Unicode.GetBytes((string)data+"\0")),
            RegistryValueKind.MultiString=>"hex(7):"+Hex(Encoding.Unicode.GetBytes(string.Join("\0",(string[])data)+"\0\0")),
            RegistryValueKind.Binary=>"hex:"+Hex((byte[])data),
            _=>"hex(0):"+Hex(data as byte[]??[])
        }+"\r\n";
        static string Escape(string text)=>text.Replace("\\","\\\\").Replace("\"","\\\"");
    }
    internal static string Serialize(InstalledApp app,IEnumerable<string> items)=>JsonSerializer.Serialize(new Request(app,items.ToArray()));
    internal sealed record Request(InstalledApp App,string[] Items);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref int size);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
}
