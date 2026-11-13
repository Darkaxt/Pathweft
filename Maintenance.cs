using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

internal sealed record MaintenanceRow(string Id,string Name,string Scope,string State,string Details,long? Bytes=null,string Type="",string StartupMode="",bool CanChange=true,
    string Group="",string Recommendation="Not assessed",string Guidance="",string Source="",string RuleId="",string Goal="",string Risk="",
    string Publisher="",string Version="",string Installed="",string Package="")
{
    public string RiskLabel=>Risk switch{"Caution"=>"▲ Caution","High"=>"◆ High",_=>""};
    public string Size=>Bytes is {} size?MainWindow.Fmt(size):"";
    public string Summary=>Details?.Split(['\r','\n'],2)[0]??"";
    public string RecommendationLabel=>(Recommendation switch{"Keep enabled"=>"● ","Optional"=>"◇ ","Advanced"=>"△ ",_=>"○ "})+Recommendation;
}
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
internal sealed record MaintenanceUndo(string Id,RegistryEdit[] Before,RegistryEdit[] After,string Service=null,uint ServiceStart=0,bool ServiceDelayed=false,string Phase="prepared",StartupFileState StartupFile=null,StartupTaskState StartupTask=null,StartupTaskState[] Tasks=null);

// Catalogue/Run enumeration adapted from SysOptimizer StartupService, ServiceManager
// and TweakService (6ddd104, MIT). No donor automatic optimization is imported.
internal static class Maintenance
{
    internal const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static readonly (RegistryHive Hive,RegistryView View)[] StartupRoots=[(RegistryHive.CurrentUser,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry64),(RegistryHive.LocalMachine,RegistryView.Registry32)];
    internal static string StartupId(string hive,RegistryView view,string name)=>"startup:"+Id(hive+"/"+view,name);
    internal static string Journal=>Path.Combine(DataFolder.Bulk,"Maintenance","changes.json");
    // Serialize the complete read/write/verify transaction across windows and
    // elevated workers. A lock around Save alone would lose another change.
    internal sealed class JournalLease : IDisposable
    {
        readonly Mutex mutex;
        internal JournalLease(string file)
        {
            // An elevated creator's default DACL would lock the normal window out
            // (and any account could open the Global name); grant this user,
            // unchanged by UAC, and SYSTEM only.
            var security=new System.Security.AccessControl.MutexSecurity();
            using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent())
                security.AddAccessRule(new(identity.User,System.Security.AccessControl.MutexRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
            security.AddAccessRule(new(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid,null),System.Security.AccessControl.MutexRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
            mutex=MutexAcl.Create(false,@"Global\Pathweft-Maintenance-"+Id("journal",Path.GetFullPath(file??Journal).ToUpperInvariant()),out _,security);
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
        if(category=="Startup")return MaintenanceStartup.Read(undo,cancel);
        if(category=="Services")return MaintenanceServices.List(undo,cancel);
        if(category=="Windows tweaks")return Tweaks.Select(t=>
        {
            cancel.ThrowIfCancellationRequested();string id="tweak:"+Id("tweak",t.Name);
            try
            {
                var edits=TweakEdits(id);bool machine=edits.Any(x=>x.Hive=="HKEY_LOCAL_MACHINE")||t.TaskPaths.Length>0;
                string scope=!machine?"Current user":edits.All(x=>x.Hive=="HKEY_LOCAL_MACHINE")?"This computer":"User and computer";
                string effect=MaintenanceGuidance.Find("tweak",t.Name)?.FeatureLoss??t.Description;
                var tasks=MaintenanceTweakTasks.ReadAll(t.TaskPaths);
                bool applied=edits.All(x=>x.Same(x.Read()))&&(t.TaskPaths.Length==0||tasks.Length>0&&tasks.All(task=>!task.Enabled));
                bool saved=undo.ContainsKey(id);
                string state=!saved&&SetOutside(edits)?OutsideState:applied?"Applied":"Not applied";
                if(t.TaskPaths.Length>0&&tasks.Length==0&&!saved)state="Unavailable";
                return new MaintenanceRow(id,t.Name,scope,state,effect+(saved?" · original settings saved":"")+OtherPolicies(edits),CanChange:state!="Unavailable");
            }
            catch(UnauthorizedAccessException ex){return new MaintenanceRow(id,t.Name,"Windows","Access denied",ex.Message);}
        }).ToArray();
        throw new ArgumentException("Unknown maintenance category");
    }
    internal const string OutsideState="Set outside Pathweft";
    // Policy values have no Windows default other than "absent" (MAINT-29).
    internal static bool IsPolicy(RegistryEdit edit)=>edit.Path.StartsWith(@"SOFTWARE\Policies\",StringComparison.OrdinalIgnoreCase)||
        edit.Path.Contains(@"\CurrentVersion\Policies\",StringComparison.OrdinalIgnoreCase);
    internal static bool PolicyOnly(RegistryEdit[] edits)=>edits.Length>0&&edits.All(IsPolicy);
    // A managed policy present without a Pathweft journal was set by another tool or Group Policy.
    static bool SetOutside(RegistryEdit[] edits)=>PolicyOnly(edits)&&edits.Any(x=>x.Read().Exists);
    // Other values under the same policy keys are context only; Pathweft never removes them.
    static string OtherPolicies(RegistryEdit[] edits)
    {
        var known=edits.Select(x=>x.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);var found=new List<string>();
        foreach(var key in edits.Where(IsPolicy).DistinctBy(x=>(x.Hive,x.Path,x.View)))
        {
            using var root=key.Root();using var open=root.OpenSubKey(key.Path);if(open==null)continue;
            foreach(string name in open.GetValueNames())
                if(!known.Contains((key with{Name=name}).Identity))found.Add((name.Length==0?"(Default)":name)+"="+open.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        return found.Count==0?"":"\nOther policies at this key, not managed by this rule (read-only): "+string.Join(", ",found.Take(8))+(found.Count>8?$" and {found.Count-8} more":"");
    }
    internal static RegistryEdit[] StartupEdits(string id)
    {
        foreach(var source in StartupRoots)
        foreach(string path in MaintenanceStartup.RegistryPaths)
        {
            using var root=RegistryKey.OpenBaseKey(source.Hive,source.View);
            using var key=root.OpenSubKey(path);if(key==null)continue;
            foreach(string name in key.GetValueNames())if(id==MaintenanceStartup.RegistryId(root.Name,source.View,path,name))
                return [new RegistryEdit(root.Name,path,name,RegistryValueKind.String,default,false,source.View)];
        }
        throw new IOException("The startup entry changed or was removed; refresh before changing it");
    }
    internal static MaintenanceTweak Tweak(string id)=>Tweaks.SingleOrDefault(t=>id=="tweak:"+Id("tweak",t.Name))??throw new ArgumentException("Unknown Windows tweak");
    internal static RegistryEdit[] TweakEdits(string id)
    {
        var edits=new List<RegistryEdit>();Tweak(id).Apply(edits);return edits.ToArray();
    }
    internal static void ApplyRegistry(string id,RegistryEdit[] after,string journal=null,bool resetDefault=false)
    {
        bool tweak=id.StartsWith("tweak:",StringComparison.Ordinal);
        if(tweak&&!resetDefault)MaintenanceGuidance.RequireSupported(id);
        using var lease=new JournalLease(journal);
        var records=Undo(journal);
        if(records.ContainsKey(id))throw new IOException("An earlier change is saved for this item. Restore or review it before applying again.");
        string[] paths=tweak&&!resetDefault?Tweak(id).TaskPaths:[];
        var tasks=paths.Length==0?null:MaintenanceTweakTasks.ReadAll(paths);
        if(tasks?.Length==0)throw new IOException("None of this setting's scheduled tasks exist on this PC");
        var before=after.Select(x=>x.Read()).ToArray();var record=new MaintenanceUndo(id,before,after,Tasks:tasks);
        records.Add(id,record);Save(records,journal);
        foreach(var edit in after)edit.Write();
        foreach(var task in tasks??[])MaintenanceTweakTasks.Set(task,false);
        if(after.Any(x=>!x.Same(x.Read())))throw new IOException("Windows did not retain the requested settings; the original state remains saved");
        records[id]=record with{Phase="applied"};Save(records,journal);
    }
    // MAINT-29: remove the policy values a rule manages so Windows uses its default; journaled like any change.
    internal static void ResetPolicies(string id,string journal=null)
    {
        var edits=TweakEdits(id);
        if(!PolicyOnly(edits))throw new InvalidOperationException("Only policy settings can be returned to the Windows default");
        ApplyRegistry(id,edits.Select(x=>x with{Exists=false,Value=default}).ToArray(),journal,resetDefault:true);
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
        foreach(var task in saved.Tasks??[])MaintenanceTweakTasks.Set(task,task.Enabled,missingAllowed:true);
        foreach(var before in saved.Before)before.Write();
        if(saved.Before.Any(x=>!x.Same(x.Read())))throw new IOException("Windows did not restore all original values; the saved state is retained");
        records.Remove(id);Save(records,journal);
    }
    static void ValidateRestore(MaintenanceUndo saved)
    {
        RegistryEdit[] allowed;
        if(saved.Id.StartsWith("startup:")&&saved.Before.Length==1)
        {
            var item=saved.Before[0];if(!MaintenanceStartup.RegistryPaths.Contains(item.Path)||saved.Id!=MaintenanceStartup.RegistryId(item.Hive,item.View,item.Path,item.Name)||!StartupRoots.Any(s=>s.Hive.ToString()==(item.Hive=="HKEY_CURRENT_USER"?"CurrentUser":item.Hive=="HKEY_LOCAL_MACHINE"?"LocalMachine":"")&&s.View==item.View))throw new InvalidDataException("Invalid startup restore target");
            allowed=[item];
        }
        else
        {
            allowed=TweakEdits(saved.Id);var paths=Tweak(saved.Id).TaskPaths;
            if(saved.Tasks?.Any(t=>!paths.Contains(t.Path,StringComparer.OrdinalIgnoreCase))==true)throw new InvalidDataException("Saved tasks differ from this maintenance action");
        }
        if(saved.Before.Length!=allowed.Length||saved.After.Length!=allowed.Length||allowed.Where((x,i)=>x.Identity!=saved.Before[i].Identity||x.Identity!=saved.After[i].Identity).Any())throw new InvalidDataException("Saved targets differ from this maintenance action");
    }
}
