#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// MAINT-39: Windows apps checks with owned fixtures. Host runs read-only parsing, ordering, guidance and
// leftover scanning; uninstall, leftover removal and Restore run only in the disposable guest.
internal static class MaintenanceAppsVerification
{
    internal const string MsiCode="{5A1C0DE5-0000-4000-8000-0000000F1C7E}";
    // An owned HKCU fixture: Uninstall entries for folding, installer types and protection, plus leftover sources.
    internal sealed class Fixture:IDisposable
    {
        internal readonly string Key=@"Software\Pathweft-Verification-"+Guid.NewGuid().ToString("N"),Root,Programs,Shortcuts;
        internal Fixture(string root)
        {
            Root=Path.Combine(root,"apps-"+Guid.NewGuid().ToString("N")[..8]);Programs=Path.Combine(Root,"Programs");Shortcuts=Path.Combine(Root,"Shortcuts");
            Directory.CreateDirectory(Programs);Directory.CreateDirectory(Shortcuts);
            MaintenanceApps.FixtureRootForVerification=Key;
            MaintenanceLeftovers.FixturesForVerification=([Programs],[Shortcuts],Key+@"\Software",Key+@"\Run");
            Environment.SetEnvironmentVariable("PATHWEFT_APPS_FIXTURE",Key);
            Environment.SetEnvironmentVariable("PATHWEFT_LEFTOVERS_FIXTURE",string.Join("|",Programs,Shortcuts,Key+@"\Software",Key+@"\Run"));
        }
        internal string Uninstall=>Key+@"\Uninstall";
        internal void Entry(string name,params (string Name,object Value)[] values)
        {
            using var key=Registry.CurrentUser.CreateSubKey(Uninstall+"\\"+name);
            foreach(var (value,data) in values)key.SetValue(value,data);
        }
        internal string Id(string name)=>"app:reg:"+Maintenance.Id("HKEY_CURRENT_USER/"+RegistryView.Registry64+"/"+Uninstall,name);
        public void Dispose()
        {
            MaintenanceApps.FixtureRootForVerification=null;MaintenanceLeftovers.FixturesForVerification=null;
            Environment.SetEnvironmentVariable("PATHWEFT_APPS_FIXTURE",null);Environment.SetEnvironmentVariable("PATHWEFT_LEFTOVERS_FIXTURE",null);
            Registry.CurrentUser.DeleteSubKeyTree(Key,false);
        }
    }
    static void Shortcut(string link,string target)
    {
        var shell=(MainWindow.IShellLinkW)new MainWindow.ShellLink();
        try{shell.SetPath(target);((System.Runtime.InteropServices.ComTypes.IPersistFile)shell).Save(link,true);}
        finally{Marshal.ReleaseComObject(shell);}
    }
    // Leftover sources of "Fixture Plain App" by "Fixture Labs", returned as identity → expected confidence.
    static Dictionary<string,string> Leftovers(Fixture fixture,string install)
    {
        string P(string name)=>Path.Combine(fixture.Programs,name);
        foreach(string folder in new[]{install,P("Fixture Plain App Helper"),P(@"Fixture Labs\Fixture Plain App"),P("Fixture Unrelated"),P("Unrelated Tool")})
        {Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"settings.ini"),"owned fixture");}
        string link=Path.Combine(fixture.Shortcuts,"Fixture Plain App.lnk");Shortcut(link,Path.Combine(install,"plain.exe"));
        Shortcut(Path.Combine(fixture.Shortcuts,"Other.lnk"),Path.Combine(fixture.Programs,"Unrelated Tool","tool.exe"));
        using(Registry.CurrentUser.CreateSubKey(fixture.Key+@"\Software\Fixture Plain App")){}
        using(Registry.CurrentUser.CreateSubKey(fixture.Key+@"\Software\Fixture Labs\Fixture Plain App"))using(var settings=Registry.CurrentUser.CreateSubKey(fixture.Key+@"\Software\Fixture Labs\Fixture Plain App\Settings"))
        {settings.SetValue("Theme","dark");settings.SetValue("Count",7,RegistryValueKind.DWord);settings.SetValue("Paths",new[]{"a","b"},RegistryValueKind.MultiString);settings.SetValue("Home","%USERPROFILE%\\x",RegistryValueKind.ExpandString);}
        using(Registry.CurrentUser.CreateSubKey(fixture.Key+@"\Software\Unrelated Vendor")){}
        using(var run=Registry.CurrentUser.CreateSubKey(fixture.Key+@"\Run")){run.SetValue("FixturePlain","\""+Path.Combine(install,"tray.exe")+"\" -min");run.SetValue("Other","\""+Path.Combine(fixture.Programs,"Unrelated Tool","tool.exe")+"\"");}
        string user=@"HKEY_CURRENT_USER\"+fixture.Key;
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["Folder|"+install+"|"]=MaintenanceLeftovers.Likely,
            ["Folder|"+P("Fixture Plain App Helper")+"|"]=MaintenanceLeftovers.Possible,
            ["Folder|"+P(@"Fixture Labs\Fixture Plain App")+"|"]=MaintenanceLeftovers.Likely,
            ["Folder|"+P("Fixture Labs")+"|"]=MaintenanceLeftovers.Possible,
            ["Folder|"+P("Fixture Unrelated")+"|"]=MaintenanceLeftovers.Unlikely,
            ["Shortcut|"+link+"|"]=MaintenanceLeftovers.Likely,
            ["Registry key|"+user+@"\Software\Fixture Plain App|"]=MaintenanceLeftovers.Likely,
            ["Registry key|"+user+@"\Software\Fixture Labs\Fixture Plain App|"]=MaintenanceLeftovers.Likely,
            ["Registry key|"+user+@"\Software\Fixture Labs|"]=MaintenanceLeftovers.Possible,
            ["Startup value|"+user+@"\Run|FixturePlain"]=MaintenanceLeftovers.Likely,
        };
    }
    static InstalledApp App(string name,string kind,long? bytes,string quiet="",string source=MaintenanceApps.Desktop)=>
        new("app:test:"+name,name,"","",source,"Current user",bytes,"","x.exe",quiet,"","",kind);
    internal static void Host(string root,Action<bool,string> check)
    {
        using var fixture=new Fixture(root);
        string install=Path.Combine(fixture.Programs,"Fixture Plain App"),inno=Path.Combine(fixture.Programs,"Fixture Inno","unins000.exe");
        fixture.Entry("Plain",("DisplayName","Fixture Plain App"),("Publisher","Fixture Labs"),("DisplayVersion","1.2.3"),("UninstallString","\""+Path.Combine(install,"uninstall.exe")+"\" /x"),
            ("InstallLocation",install),("EstimatedSize",2048),("InstallDate","20260102"));
        fixture.Entry(MsiCode,("DisplayName","Fixture Installer App"),("WindowsInstaller",1),("UninstallString","MsiExec.exe /I"+MsiCode));
        fixture.Entry("Fixture Inno_is1",("DisplayName","Fixture Inno App"),("UninstallString","\""+inno+"\""));
        fixture.Entry("Hidden",("DisplayName","Fixture Component"),("SystemComponent",1),("UninstallString","x.exe"));
        fixture.Entry("Patch",("DisplayName","Fixture Patch"),("ParentKeyName","Plain"),("UninstallString","x.exe"));
        fixture.Entry("Hotfix",("DisplayName","Fixture Hotfix"),("ReleaseType","Security Update"),("UninstallString","x.exe"));
        fixture.Entry("Nameless",("UninstallString","x.exe"));
        fixture.Entry("Locked",("DisplayName","Fixture Locked App"),("NoRemove",1),("UninstallString","x.exe"));
        fixture.Entry("Commandless",("DisplayName","Fixture Commandless App"));
        var apps=MaintenanceApps.Read(default);
        check(apps.Select(a=>a.Name).Order().SequenceEqual(["Fixture Commandless App","Fixture Inno App","Fixture Installer App","Fixture Locked App","Fixture Plain App"]),
            "Inventory folds nameless, system-component, child-patch and update entries out of an owned uninstall fixture");
        var plain=apps.Single(a=>a.Name=="Fixture Plain App");var msi=apps.Single(a=>a.Kind=="Windows Installer");var innoApp=apps.Single(a=>a.Kind=="Inno Setup");
        check(plain.Id==fixture.Id("Plain")&&plain.Publisher=="Fixture Labs"&&plain.Version=="1.2.3"&&plain.Bytes==2048*1024&&plain.Installed=="2026-01-02"&&plain.Scope=="Current user"&&plain.Quiet.Length==0&&plain.Protection.Length==0,
            "A registry entry yields its identity, publisher, version, size, install date and scope");
        check(msi.Uninstall=="MsiExec.exe /X"+MsiCode&&msi.Quiet=="MsiExec.exe /X"+MsiCode+" /qb REBOOT=ReallySuppress /norestart"&&msi.ProductCode==MsiCode,
            "Windows Installer entries uninstall by product code with /X (never the registered /I modify form) and offer a quiet form");
        check(innoApp.Quiet=="\""+inno+"\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART","Inno Setup entries get the documented silent switches");
        check(apps.Single(a=>a.Name=="Fixture Locked App").Protection.Length>0&&apps.Single(a=>a.Name=="Fixture Commandless App").Protection.Length>0&&
            !apps.Single(a=>a.Name=="Fixture Locked App").Row().CanChange&&plain.Row().CanChange&&!plain.Row("Removed").CanChange,
            "NoRemove and command-less entries are protected rows; a removed row cannot be uninstalled again");
        check(plain.Row().Details.Contains("Registry: HKEY_CURRENT_USER\\"+fixture.Uninstall+"\\Plain")&&plain.Row().Details.Contains("Location: "+install)&&
            plain.Row().Summary.IndexOfAny(['\r','\n'])<0,"Details carry the uninstall command, install location and registry key; the table shows a one-line summary");
        // BCU order: interactive before quiet, then size bucket, Windows Installer first, larger first.
        var big=App("Big",  "Other installer",1L<<30);var small=App("Small","Other installer",1L<<20);
        var quietBig=App("Quiet big","Other installer",1L<<30,"x.exe /S");var store=App("Store","Store package",null,"",MaintenanceApps.Store);
        var msiSmall=App("Installer small","Windows Installer",1L<<20,"msiexec /X /qb");
        check(MaintenanceApps.Order([store,small,quietBig,big],true).Select(a=>a.Name).SequenceEqual(["Big","Small","Quiet big","Store"])&&
            MaintenanceApps.Order([quietBig,big],false).First().Name=="Big"&&MaintenanceApps.Order([small,msiSmall],false).First().Name=="Installer small",
            "Bulk order runs interactive uninstallers first, then larger, with Windows Installer first within a size bucket");
        string spaced=Path.Combine(root,"apps tools","un install.exe");Directory.CreateDirectory(Path.GetDirectoryName(spaced));File.WriteAllText(spaced,"");
        check(MaintenanceApps.Split("\"C:\\A B\\u.exe\" /S")==("C:\\A B\\u.exe","/S")&&MaintenanceApps.Split(spaced+" /S --x")==(spaced,"/S --x")&&
            MaintenanceApps.Split("C:\\Missing Folder\\u.exe /S")==("C:\\Missing","Folder\\u.exe /S")&&MaintenanceApps.Split("MsiExec.exe /X"+MsiCode)==("MsiExec.exe","/X"+MsiCode),
            "Command split honours quotes, finds an existing unquoted path with spaces and otherwise takes the first token");
        // Guidance (MAINT-33): Store packages by exact name, desktop programs never rated, protected rows explained.
        var solitaire=new MaintenanceRow("app:store:fixture","Solitaire","Current user","Installed","Store package",Type:MaintenanceApps.Store,Package:"Microsoft.MicrosoftSolitaireCollection");
        var described=MaintenanceGuidance.Describe("Windows apps",solitaire,26100);
        check(described.Recommendation=="Optional"&&described.Group==MaintenanceApps.Store&&described.Guidance.Contains("Rating: Optional")&&described.Source.StartsWith("https://"),
            "A Store package gets its reviewed donor rating by exact package name, grouped by source");
        check(MaintenanceGuidance.Describe("Windows apps",solitaire with{Package="Microsoft.MicrosoftSolitaireCollectionX"},26100).Recommendation=="Not assessed"&&
            MaintenanceGuidance.Describe("Windows apps",plain.Row() with{Name="Microsoft Solitaire Collection"},26100).Recommendation=="Not assessed",
            "A similar package name or a desktop program named like a rated app gains no rating");
        var locked=MaintenanceGuidance.Describe("Windows apps",apps.Single(a=>a.Name=="Fixture Locked App").Row(),26100);
        check(locked.Recommendation=="Not assessed"&&locked.Guidance.Contains("cannot be selected for removal"),"A protected row explains why it cannot be removed");
        check(MaintenanceGuidance.Data.Rules.Where(r=>r.Kind=="app").All(r=>r.Recommendation is "Optional" or "Keep enabled"&&r.Risk is "Low" or "Caution" or "High"&&
            (r.Source.StartsWith("https://github.com/Raphire/Win11Debloat/")||r.Source.StartsWith("https://github.com/builtbybel/Bloatynosy"))),
            "Every app rating is Optional or Keep enabled with a risk, from the pinned MIT donors");
        // Leftovers (MAINT-36), scan only on the host.
        var expected=Leftovers(fixture,install);
        var found=MaintenanceLeftovers.Scan(plain,default).ToDictionary(x=>x.Identity,x=>x.Confidence,StringComparer.OrdinalIgnoreCase);
        var missing=expected.Where(e=>found.GetValueOrDefault(e.Key)!=e.Value).Select(e=>e.Key+"="+found.GetValueOrDefault(e.Key)).ToArray();
        check(missing.Length==0,"Leftover scan finds the install folder, name and publisher folders, a dead shortcut, settings keys and a startup value with their confidence"+(missing.Length>0?": "+string.Join("; ",missing):""));
        check(found.Count==expected.Count,"Leftover scan offers nothing unrelated (other folders, shortcuts, keys and startup values)"+(found.Count!=expected.Count?": "+string.Join("; ",found.Keys.Except(expected.Keys)):""));
        File.WriteAllText(Path.Combine(install,"plain.exe"),"");
        check(!MaintenanceLeftovers.Scan(plain,default).Any(x=>x.Kind=="Shortcut"),"A shortcut whose target still exists is not a leftover");
        fixture.Entry("Neighbour",("DisplayName","Neighbour"),("InstallLocation",Path.Combine(fixture.Programs,"Fixture Labs")),("UninstallString","x.exe"));
        var shared=MaintenanceLeftovers.Scan(plain,default);
        check(!shared.Any(x=>x.Path.StartsWith(Path.Combine(fixture.Programs,"Fixture Labs"),StringComparison.OrdinalIgnoreCase)),"Folders holding another registered app's location are never offered");
        check(MaintenanceLeftovers.Match("Fixture Plain App 2.1 (x64)","Fixture Plain App")==0&&MaintenanceLeftovers.Match("Fix","Fixture")==-1,"Product matching ignores versions and architecture and refuses very short names");
    }
    // Windows apps tab against the owned fixture: columns, multiple selection, protected rows and the confirmation.
    internal static async Task Ui(MaintenancePanel panel,string root,Action<bool,string> check)
    {
        using var fixture=new Fixture(root);
        fixture.Entry("Plain",("DisplayName","Fixture Plain App"),("Publisher","Fixture Labs"),("UninstallString","x.exe"),("EstimatedSize",4096));
        fixture.Entry("Second",("DisplayName","Fixture Second App"),("UninstallString","\"C:\\Fixture\\unins000.exe\""));
        fixture.Entry("Locked",("DisplayName","Fixture Locked App"),("NoRemove",1),("UninstallString","x.exe"));
        await panel.CategoryForVerification("Windows apps");
        var headers=((System.Windows.Controls.GridView)panel.Rows.View).Columns.Select(c=>System.Windows.Automation.AutomationProperties.GetName((System.Windows.Controls.GridViewColumnHeader)c.Header)).ToArray();
        check(headers.SequenceEqual(["Name","Publisher","Version","Source","State","Size","Installed","Details"])&&panel.Rows.SelectionMode==System.Windows.Controls.SelectionMode.Extended,
            "Windows apps shows Name, Publisher, Version, Source, State, Size, Installed and Details with multiple selection, and no Recommendation column");
        var rows=panel.Rows.Items.Cast<MaintenanceRow>().ToArray();
        check(rows.Length==3&&rows.All(r=>r.Group==MaintenanceApps.Desktop&&r.Recommendation=="Not assessed"),"The tab lists the fixture's three programs grouped by source, unrated");
        panel.Rows.SelectedItem=rows.Single(r=>r.Name=="Fixture Locked App");
        check(!panel.CleanEnabledForVerification&&panel.ExplanationForVerification.Contains("cannot be selected for removal")&&!panel.ExplanationForVerification.Contains("Protected · "),"A protected row disables Uninstall and says why once");
        panel.Rows.SelectedItems.Clear();foreach(var row in rows.Where(r=>r.CanChange))panel.Rows.SelectedItems.Add(row);
        check(panel.CleanEnabledForVerification&&panel.ActionLabelsForVerification.Contains("Uninstall selected…")&&!panel.ActionLabelsForVerification.Contains("Remove"),"Selecting two programs offers Uninstall selected…");
        IReadOnlyList<InstalledApp> asked=null;
        UninstallConfirmWindow.AnswerForVerification=apps=>{asked=apps;return(false,false,false);};
        try{await panel.ClickForVerification("Uninstall selected…");}finally{UninstallConfirmWindow.AnswerForVerification=null;}
        check(asked?.Count==2&&asked[0].Name=="Fixture Plain App"&&panel.Rows.Items.Count==3,"The confirmation names every selected app in bulk order; declining changes nothing");
        panel.Rows.SelectedItem=rows.Single(r=>r.Name=="Fixture Second App");
        check(panel.ActionLabelsForVerification.Contains("Uninstall…")&&panel.ActionLabelsForVerification.Contains("Find leftovers…"),"One program offers Uninstall… and Find leftovers… (enabled only after removal)");
        fixture.Dispose();await panel.CategoryForVerification("Windows apps");
    }
    // Guest only (MAINT-39): real uninstalls of owned fixture entries, the installer-busy guard, a Store package,
    // the restore point, leftover removal to the Recycle Bin with a .reg backup and Restore.
    internal static async Task Guest(string root,Action<bool,string> check,Action<string,object> note)
    {
        // Guest-local disk: the shared C:\Coordinator\Work folder is redirected from the host and has no Recycle Bin.
        using var fixture=new Fixture(@"C:\PathweftAppsFixture");
        string install=Path.Combine(fixture.Programs,"Fixture Plain App");Directory.CreateDirectory(install);
        string Script(string name,string body){string file=Path.Combine(fixture.Root,name+".cmd");File.WriteAllText(file,"@echo off\r\n"+body+"\r\n");return file;}
        string delete(string entry)=>"reg delete \"HKCU\\"+fixture.Uninstall+"\\"+entry+"\" /f >nul";
        // The uninstaller exits at once and a detached child removes the entry three seconds later (Inno/NSIS style).
        // cmd /c makes the child end with its script; start on a .cmd alone keeps its window open (cmd /K).
        string child=Script("child",$"ping -n 4 127.0.0.1 >nul\r\n{delete("Plain")}");
        fixture.Entry("Plain",("DisplayName","Fixture Plain App"),("Publisher","Fixture Labs"),("InstallLocation",install),("UninstallString","\""+Script("plain",$"start \"\" /min cmd.exe /c \"{child}\"")+"\""));
        fixture.Entry("Quiet",("DisplayName","Fixture Quiet App"),("UninstallString","\""+Script("loud","exit /b 0")+"\""),("QuietUninstallString","\""+Script("quiet",delete("Quiet"))+"\" /S"));
        fixture.Entry("Refuses",("DisplayName","Fixture Refusing App"),("UninstallString","\""+Script("refuses","exit /b 1602")+"\""));
        fixture.Entry("Worker",("DisplayName","Fixture Worker App"),("UninstallString","\""+Script("worker",delete("Worker"))+"\""));
        fixture.Entry(MsiCode,("DisplayName","Fixture Installer App"),("WindowsInstaller",1),("UninstallString","MsiExec.exe /X"+MsiCode));
        var watch=Stopwatch.StartNew();var outcome=await Bounded(note,"uninstall plain",()=>MaintenanceApps.Uninstall(fixture.Id("Plain"),false,default));
        check(outcome.State=="Removed"&&watch.Elapsed>=TimeSpan.FromSeconds(2),"Uninstall waits for the uninstaller's descendants and confirms removal by re-reading the entry: "+outcome.State+" after "+watch.Elapsed.TotalSeconds.ToString("0.0")+" s");
        outcome=await Bounded(note,"uninstall loud",()=>MaintenanceApps.Uninstall(fixture.Id("Quiet"),false,default));
        check(outcome.State=="Still installed","An interactive uninstaller that leaves the entry is reported as Still installed: "+outcome.State);
        outcome=await Bounded(note,"uninstall quiet",()=>MaintenanceApps.Uninstall(fixture.Id("Quiet"),true,default));
        check(outcome.State=="Removed","Quiet uninstall runs the declared quiet command: "+outcome.State);
        outcome=await Bounded(note,"uninstall refusing",()=>MaintenanceApps.Uninstall(fixture.Id("Refuses"),false,default));
        check(outcome.State=="Cancelled","Exit code 1602 reports Cancelled with the entry kept: "+outcome.State);
        // ACCESS-07: uninstalls run through the administrator session's worker, which re-reads the entry itself.
        var viaWorker=await Bounded(note,"worker uninstall",async()=>{await using var client=new MaintenanceClient();return await client.Start(new("uninstall-app",fixture.Id("Worker"),root,""),false);});
        var workerOutcome=viaWorker.Ok?JsonSerializer.Deserialize<MaintenanceApps.Outcome>(viaWorker.Message):null;
        check(workerOutcome?.State=="Removed","The administrator-session worker runs a desktop program's own uninstaller and confirms removal: "+viaWorker.Message);
        await using(var client=new MaintenanceClient())
            check(!(await client.Start(new("uninstall-app","app:store:fixture",root,""),false)).Ok,"The worker refuses to uninstall a Store package");
        // The named mutex existing is the busy signal; it is not owned, so no thread affinity crosses the await.
        using(new Mutex(false,@"Global\_MSIExecute"))
        {
            outcome=await Bounded(note,"uninstall installer busy",()=>MaintenanceApps.Uninstall(fixture.Id(MsiCode),true,default));
            check(outcome.State=="Not started"&&MaintenanceApps.InstallerBusy(),"A Windows Installer uninstall does not start while another installation holds the installer mutex: "+outcome.State);
        }
        // Restore point: Windows Sandbox may not support System Restore; the result is recorded, not required.
        try
        {
            var point=await Bounded(note,"restore point",async()=>{await using var client=new MaintenanceClient();return await client.Start(new("restore-point","",Path.Combine(root,"restore-point.json"),null),false);});
            note("restorePoint",new{point.Ok,point.Message});
        }
        catch(TimeoutException error){note("restorePoint",error.Message);}
        // Leftovers: user registry items in-process; folders outside the profile through the authenticated worker.
        var plain=new InstalledApp(fixture.Id("Plain"),"Fixture Plain App","Fixture Labs","",MaintenanceApps.Desktop,"Current user",null,"","x.exe","",install,"","Other installer");
        var expected=Leftovers(fixture,install);string backups=Path.Combine(root,"leftover-backups");
        var found=await Bounded(note,"leftover scan",()=>Task.FromResult(MaintenanceLeftovers.Scan(plain,default)));
        check(expected.All(e=>found.Any(f=>f.Identity.Equals(e.Key,StringComparison.OrdinalIgnoreCase)&&f.Confidence==e.Value)),"Guest leftover scan finds every fixture leftover with its confidence");
        var likely=found.Where(f=>f.Confidence==MaintenanceLeftovers.Likely).ToArray();
        var user=await Bounded(note,"user leftovers",()=>Task.FromResult(MaintenanceLeftovers.Remove(plain,likely.Where(f=>!f.Administrator).Select(f=>f.Identity),false,backups,default)));
        check(user.Skipped==0&&user.Backup!=null&&File.Exists(user.Backup)&&Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Software\Fixture Plain App")==null&&
            Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Software\Fixture Labs\Fixture Plain App")==null&&Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Run")?.GetValue("FixturePlain")==null&&
            Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Software\Fixture Labs")!=null,"Likely registry leftovers are removed after a .reg backup; Possible ones stay: "+user.Details);
        MaintenanceReply reply;
        reply=await Bounded(note,"worker leftovers",async()=>{await using var client=new MaintenanceClient();return await client.Start(new("remove-leftovers",MaintenanceLeftovers.Serialize(plain,likely.Where(f=>f.Administrator).Select(f=>f.Identity)),backups,null),false);});
        var machine=reply.Ok?JsonSerializer.Deserialize<LeftoverOutcome>(reply.Message):null;
        check(machine?.Skipped==0&&!Directory.Exists(install)&&!Directory.Exists(Path.Combine(fixture.Programs,@"Fixture Labs\Fixture Plain App"))&&!File.Exists(Path.Combine(fixture.Shortcuts,"Fixture Plain App.lnk"))&&
            Directory.Exists(Path.Combine(fixture.Programs,"Fixture Plain App Helper"))&&Directory.Exists(Path.Combine(fixture.Programs,"Unrelated Tool")),
            "The worker removes Likely folders and the dead shortcut and leaves Possible and unrelated folders: "+reply.Message);
        check(RecycleBinHolds("settings.ini"),"Removed leftover folders are in the Recycle Bin, not deleted");
        await Bounded(note,"restore leftovers",async()=>{await MaintenanceLeftovers.Restore(user.Backup,default);return true;});
        using(var restored=Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Software\Fixture Labs\Fixture Plain App\Settings"))
            check(restored!=null&&(string)restored.GetValue("Theme")=="dark"&&(int)restored.GetValue("Count")==7&&((string[])restored.GetValue("Paths")).SequenceEqual(["a","b"])&&
                restored.GetValueKind("Home")==RegistryValueKind.ExpandString&&(string)restored.GetValue("Home",null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%USERPROFILE%\\x"&&
                (Registry.CurrentUser.OpenSubKey(fixture.Key+@"\Run")?.GetValue("FixturePlain") as string)?.Contains("tray.exe")==true,
                "Restore imports the .reg backup with exact value kinds, including the startup value");
        await using(var client=new MaintenanceClient())
            check(!(await client.Start(new("remove-leftovers","{}",backups,null),false)).Ok,"The worker refuses a malformed leftovers request");
        // Store: remove one removable main package for the current user when the guest has one.
        MaintenanceApps.FixtureRootForVerification=null;
        var package=(await Bounded(note,"store inventory",()=>Task.FromResult(MaintenanceApps.Read(default,MaintenanceApps.Store)))).FirstOrDefault(a=>a.Protection.Length==0);
        if(package==null)note("storePackage","No removable Store package in this guest");
        else
        {
            outcome=await Bounded(note,"store removal",()=>MaintenanceApps.Uninstall(package.Id,false,default));note("storePackage",new{package.Package,outcome.State,outcome.Message});
            check(outcome.State=="Removed"&&!MaintenanceApps.Read(default,MaintenanceApps.Store).Any(a=>a.Id==package.Id),"A Store package is removed for the current user and no longer listed: "+package.Package+" "+outcome.State);
        }
    }
    // Each guest step is bounded so a step that never returns is named in the results instead of stalling the guest.
    static async Task<T> Bounded<T>(Action<string,object> note,string step,Func<Task<T>> work)
    {
        note("step",step);
        try{return await Task.Run(work).WaitAsync(TimeSpan.FromMinutes(3));}
        catch(TimeoutException){throw new TimeoutException(step+" did not finish within 3 minutes");}
    }
    static bool RecycleBinHolds(string name)
    {
        string bin=Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)??@"C:\","$Recycle.Bin");
        try{return Directory.EnumerateFiles(bin,name,new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=0}).Any();}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return false;}
    }
}
#endif
