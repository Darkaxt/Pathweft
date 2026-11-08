using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

internal sealed record MaintenanceRow(string Id,string Name,string Scope,string State,string Details,long? Bytes=null)
{public string Size=>Bytes is {} size?MainWindow.Fmt(size):"";}
internal sealed record RegistryEdit(string Hive,string Path,string Name,RegistryValueKind Kind,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]JsonElement Value,bool Exists,RegistryView View=RegistryView.Registry64)
{
    internal string Identity=>Hive+"\\"+View+"\\"+Path+"\\"+Name;
    internal RegistryKey Root()=>RegistryKey.OpenBaseKey(Hive switch{"HKEY_CURRENT_USER"=>RegistryHive.CurrentUser,"HKEY_LOCAL_MACHINE"=>RegistryHive.LocalMachine,_=>throw new ArgumentException("Unsupported registry hive")},View is RegistryView.Registry32 or RegistryView.Registry64?View:throw new ArgumentException("Unsupported registry view"));
    internal RegistryEdit Read()
    {
        using var root=Root();using var key=root.OpenSubKey(Path);
        if(key==null||!key.GetValueNames().Contains(Name,StringComparer.OrdinalIgnoreCase))return this with{Exists=false,Value=default};
        return this with{Exists=true,Kind=key.GetValueKind(Name),Value=JsonSerializer.SerializeToElement(key.GetValue(Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames))};
    }
    internal void Write()
    {
        using var root=Root();using var key=root.CreateSubKey(Path);
        if(!Exists){key.DeleteValue(Name,false);return;}
        object value=Kind switch
        {
            RegistryValueKind.DWord=>Value.GetInt32(),RegistryValueKind.QWord=>Value.GetInt64(),
            RegistryValueKind.Binary or RegistryValueKind.None=>Value.Deserialize<byte[]>(),RegistryValueKind.MultiString=>Value.Deserialize<string[]>(),
            RegistryValueKind.String or RegistryValueKind.ExpandString=>Value.GetString(),_=>throw new InvalidDataException("Unsupported registry value type")
        };
        key.SetValue(Name,value,Kind);key.Flush();
    }
    internal bool Same(RegistryEdit other)=>Exists==other.Exists&&(!Exists||Kind==other.Kind&&Value.GetRawText()==other.Value.GetRawText());
}
internal sealed record MaintenanceUndo(string Id,RegistryEdit[] Before,RegistryEdit[] After,string Service=null,uint ServiceStart=0,bool ServiceDelayed=false,string Phase="prepared");

// Catalogue/Run enumeration adapted from SysOptimizer StartupService, ServiceManager
// and TweakService (6ddd104, MIT). No donor automatic optimization is imported.
internal static class Maintenance
{
    internal const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly (RegistryHive Hive,RegistryView View)[] StartupRoots=[(RegistryHive.CurrentUser,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry32)];
    internal static string StartupId(string hive,RegistryView view,string name)=>"startup:"+Id(hive+"/"+view,name);
    internal static string Journal=>Path.Combine(DataFolder.Bulk,"Maintenance","changes.json");
    // Serialize the complete read/write/verify transaction across windows and
    // elevated workers. A lock around Save alone would lose another change.
    internal sealed class JournalLease : IDisposable
    {
        readonly Mutex mutex;
        internal JournalLease(string file)
        {
            mutex=new Mutex(false,@"Global\Pathweft-Maintenance-"+Id("journal",Path.GetFullPath(file??Journal).ToUpperInvariant()));
            try{mutex.WaitOne();}catch(AbandonedMutexException){/* Prepared entries remain recoverable. */}
        }
        public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
    }
    internal static readonly (string Key,string Name,string Description)[] Services=
    [
        ("DiagTrack","Connected User Experiences and Telemetry","Windows diagnostic and usage data"),
        ("dmwappushservice","WAP Push Message Routing","Device-management push messages"),
        ("SysMain","SysMain","Preloads frequently used applications"),("WSearch","Windows Search","Windows and Outlook indexing"),
        ("Fax","Fax","Fax support"),("RemoteRegistry","Remote Registry","Remote registry access"),
        ("MapsBroker","Downloaded Maps Manager","Offline map updates"),("PrintNotify","Printer notifications","Printer extension notifications"),
        ("RetailDemo","Retail Demo","Retail demonstration mode"),("WerSvc","Windows Error Reporting","Windows crash reports")
    ];
    internal static readonly (string Name,string Package)[] Apps=
    [
        ("3D Builder","Microsoft.3DBuilder"),("Weather","Microsoft.BingWeather"),("News","Microsoft.BingNews"),
        ("Get Help","Microsoft.GetHelp"),("Tips","Microsoft.Getstarted"),("Office Hub","Microsoft.MicrosoftOfficeHub"),
        ("Solitaire Collection","Microsoft.MicrosoftSolitaireCollection"),("Mixed Reality Portal","Microsoft.MixedReality.Portal"),
        ("People","Microsoft.People"),("Skype","Microsoft.SkypeApp"),("Feedback Hub","Microsoft.WindowsFeedbackHub"),
        ("Xbox App","Microsoft.XboxApp"),("Xbox Game Overlay","Microsoft.XboxGameOverlay"),("Xbox Gaming Overlay","Microsoft.XboxGamingOverlay"),
        ("Xbox Identity Provider","Microsoft.XboxIdentityProvider"),("Xbox Speech To Text Overlay","Microsoft.XboxSpeechToTextOverlay"),
        ("Phone Link","Microsoft.YourPhone"),("Groove Music","Microsoft.ZuneMusic"),("Movies & TV","Microsoft.ZuneVideo"),
        ("To Do","Microsoft.Todos"),("Teams (classic)","MicrosoftTeams"),("Teams","MSTeams"),("Clipchamp","Clipchamp.Clipchamp"),("Cortana","Microsoft.549981C3F5F10")
    ];
    internal static MaintenanceTweak[] Tweaks=>MaintenanceTweaks.All().Where(t=>t.Name!="Hibernation").ToArray();
    internal static string Id(string scope,string name)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope+"\0"+name)));
    internal static Dictionary<string,MaintenanceUndo> Undo(string file=null)
    {
        file??=Journal;if(!File.Exists(file))return new(StringComparer.Ordinal);
        if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("Maintenance change journal exceeds its safety limit");
        return JsonSerializer.Deserialize<Dictionary<string,MaintenanceUndo>>(File.ReadAllText(file))??throw new InvalidDataException("Maintenance journal is invalid");
    }
    internal static void Save(Dictionary<string,MaintenanceUndo> records,string file=null)
    {
        file??=Journal;Directory.CreateDirectory(Path.GetDirectoryName(file));string temporary=file+".writing";
        using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,records);stream.Flush(true);}
        File.Move(temporary,file,true);
    }
    internal static MaintenanceRow[] Read(string category,CancellationToken cancel)
    {
        var undo=Undo();cancel.ThrowIfCancellationRequested();
        if(category=="Cleanup")return CleanupReview.Targets.Select(t=>new MaintenanceRow(t.Id,t.Name,"This computer","Not analyzed",t.Description)).ToArray();
        if(category=="Startup")
        {
            var rows=new List<MaintenanceRow>();
            foreach(var source in StartupRoots)
            {
                using var root=RegistryKey.OpenBaseKey(source.Hive,source.View);
                using var key=root.OpenSubKey(RunPath);if(key==null)continue;
                foreach(string name in key.GetValueNames())
                {
                    cancel.ThrowIfCancellationRequested();string id=StartupId(root.Name,source.View,name);
                    rows.Add(new(id,name,source.Hive==RegistryHive.CurrentUser?"Current user":"All users","Enabled",source.View+" · "+(key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString()??"")));
                }
            }
            foreach(var item in undo.Values.Where(x=>x.Id.StartsWith("startup:")))
                if(!rows.Any(r=>r.Id==item.Id)&&item.Before.Length==1)
                    rows.Add(new(item.Id,item.Before[0].Name,item.Before[0].Hive=="HKEY_CURRENT_USER"?"Current user":"All users",item.Phase=="applied"?"Disabled":"Needs review",item.Before[0].Value.ToString()));
            return rows.OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        if(category=="Services")return Services.Select(s=>
        {
            cancel.ThrowIfCancellationRequested();try{var state=MaintenanceServices.Read(s.Key);return new MaintenanceRow("service:"+s.Key,s.Name,"This computer",state.Label,s.Description+" · "+state.StartLabel+(undo.ContainsKey("service:"+s.Key)?" · original configuration saved":""));}
            catch(System.ComponentModel.Win32Exception ex){return new MaintenanceRow("service:"+s.Key,s.Name,"This computer",ex.NativeErrorCode==1060?"Not installed":"Unavailable",ex.Message);}
        }).ToArray();
        if(category=="Windows tweaks")return Tweaks.Select(t=>
        {
            cancel.ThrowIfCancellationRequested();string id="tweak:"+Id("tweak",t.Name);
            try{return new MaintenanceRow(id,t.Name,"Windows",TweakEdits(id).All(x=>x.Same(x.Read()))?"Applied":"Not applied",t.Description+(undo.ContainsKey(id)?" · original settings saved":""));}
            catch(UnauthorizedAccessException ex){return new MaintenanceRow(id,t.Name,"Windows","Access denied",ex.Message);}
        }).ToArray();
        throw new ArgumentException("Unknown maintenance category");
    }
    internal static RegistryEdit[] StartupEdits(string id)
    {
        foreach(var source in StartupRoots)
        {
            using var root=RegistryKey.OpenBaseKey(source.Hive,source.View);
            using var key=root.OpenSubKey(RunPath);if(key==null)continue;
            foreach(string name in key.GetValueNames())if(id==StartupId(root.Name,source.View,name))
                return [new RegistryEdit(root.Name,RunPath,name,RegistryValueKind.String,default,false,source.View)];
        }
        throw new IOException("The startup entry changed or was removed; refresh before changing it");
    }
    internal static RegistryEdit[] TweakEdits(string id)
    {
        var tweak=Tweaks.SingleOrDefault(t=>id=="tweak:"+Id("tweak",t.Name))??throw new ArgumentException("Unknown Windows tweak");
        var edits=new List<RegistryEdit>();tweak.Apply(edits);return edits.ToArray();
    }
    internal static void ApplyRegistry(string id,RegistryEdit[] after,string journal=null)
    {
        using var lease=new JournalLease(journal);
        var records=Undo(journal);
        if(records.ContainsKey(id))throw new IOException("An earlier change is saved for this item. Restore or review it before applying again.");
        var before=after.Select(x=>x.Read()).ToArray();var record=new MaintenanceUndo(id,before,after);
        records.Add(id,record);Save(records,journal);
        foreach(var edit in after)edit.Write();
        if(after.Any(x=>!x.Same(x.Read())))throw new IOException("Windows did not retain the requested settings; the original state remains saved");
        records[id]=record with{Phase="applied"};Save(records,journal);
    }
    internal static void RestoreRegistry(string id,string journal=null)
    {
        using var lease=new JournalLease(journal);
        var records=Undo(journal);var saved=records.GetValueOrDefault(id)??throw new IOException("No original state is saved for this item");
        ValidateRestore(saved);
        for(int i=0;i<saved.After.Length;i++)
        {
            var current=saved.After[i].Read();if(!current.Same(saved.After[i])&&!current.Same(saved.Before[i]))
                throw new IOException("A setting changed outside Pathweft. Restore stopped to preserve the newer value.");
        }
        foreach(var before in saved.Before)before.Write();
        if(saved.Before.Any(x=>!x.Same(x.Read())))throw new IOException("Windows did not restore all original values; the saved state is retained");
        records.Remove(id);Save(records,journal);
    }
    static void ValidateRestore(MaintenanceUndo saved)
    {
        RegistryEdit[] allowed;
        if(saved.Id.StartsWith("startup:")&&saved.Before.Length==1)
        {
            var item=saved.Before[0];if(item.Path!=RunPath||saved.Id!=StartupId(item.Hive,item.View,item.Name))throw new InvalidDataException("Invalid startup restore target");
            allowed=[item];
        }
        else allowed=TweakEdits(saved.Id);
        if(saved.Before.Length!=allowed.Length||saved.After.Length!=allowed.Length||allowed.Where((x,i)=>x.Identity!=saved.Before[i].Identity||x.Identity!=saved.After[i].Identity).Any())throw new InvalidDataException("Saved targets differ from this maintenance action");
    }
}
