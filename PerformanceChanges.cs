using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

// SysOptimizer ChangeService's installed/updated/removed comparison, using
// hive/view/key identity and an atomic local baseline. Checks are on demand;
// successful changes enter the existing sampler-owned, capacity-limited history.
internal static class PerformanceChanges
{
    internal sealed record App(string Name,string Version);
    internal sealed record Baseline(DateTimeOffset Seen,Dictionary<string,App> Apps);
    static readonly SemaphoreSlim serial=new(1,1);
    internal static Dictionary<string,App> Installed(CancellationToken cancel)
    {
        var apps=new Dictionary<string,App>(StringComparer.OrdinalIgnoreCase);
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var root=RegistryKey.OpenBaseKey(hive,view);using var uninstall=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");if(uninstall==null)continue;
            foreach(string id in uninstall.GetSubKeyNames())
            {
                cancel.ThrowIfCancellationRequested();using var key=uninstall.OpenSubKey(id);if(key==null)continue;
                if(key.GetValue("DisplayName") is not string {Length:>0} name)continue;
                apps[hive+"/"+view+"/"+id]=new(name,key.GetValue("DisplayVersion") as string??"");
            }
        }
        return apps;
    }
    internal static PerformanceFact[] Diff(Baseline before,Baseline now)
    {
        var facts=new List<PerformanceFact>();
        void Add(string kind,App app,string detail)=>facts.Add(new(now.Seen,"App "+kind,null,null,null,1,app.Name+" "+detail+$" · observed since {before.Seen:O}; actual installation/removal time is unknown"));
        foreach(var (id,app) in now.Apps.OrderBy(x=>x.Value.Name,StringComparer.OrdinalIgnoreCase))
            if(!before.Apps.TryGetValue(id,out var old))Add("installed",app,app.Version);
            else if(old.Version!=app.Version&&app.Version.Length>0)Add("updated",app,old.Version+" → "+app.Version);
        foreach(var (id,app) in before.Apps.OrderBy(x=>x.Value.Name,StringComparer.OrdinalIgnoreCase))if(!now.Apps.ContainsKey(id))Add("removed",app,app.Version);
        return facts.ToArray();
    }
    internal static async Task<string> Check(PerformanceMonitor monitor,CancellationToken cancel)
    {
        await serial.WaitAsync(cancel);
        try
        {
            string file=Path.Combine(DataFolder.Bulk,"Performance","apps-baseline.json");
            Baseline before=null;
            if(File.Exists(file))before=JsonSerializer.Deserialize<Baseline>(await File.ReadAllTextAsync(file,cancel))??throw new InvalidDataException("App baseline is invalid");
            var now=new Baseline(DateTimeOffset.UtcNow,await Task.Run(()=>Installed(cancel),cancel));
            var changes=before==null?[]:Diff(before,now);cancel.ThrowIfCancellationRequested();
            // Once submitted, complete the save/baseline pair even if the view
            // changes. Never acknowledge a baseline whose changes were not saved.
            await monitor.SaveChanges(changes);
            Directory.CreateDirectory(Path.GetDirectoryName(file));string next=file+".writing";
            using(var stream=new FileStream(next,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,now);stream.Flush(true);}
            File.Move(next,file,true);
            return before==null?"Installed-app baseline established; future checks detect changes.":$"{changes.Length:N0} installed-app changes saved; checked {now.Seen.ToLocalTime():g}.";
        }
        finally{serial.Release();}
    }
}
