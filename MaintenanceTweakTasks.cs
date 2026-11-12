using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

// Scheduled tasks named by exact path in a typed Windows tweak (MAINT-27). Only the
// task's Enabled flag changes; its definition and triggers stay as they are, and the
// journal keeps the exact original state for Restore.
internal static class MaintenanceTweakTasks
{
    internal static StartupTaskState Read(string path)
    {
        using var session=new MaintenanceStartup.TaskSession();
        dynamic task=Find(session,path);
        return task==null?null:new StartupTaskState((string)task.Path,MaintenanceStartup.TaskDefinition(task),(bool)task.Enabled);
    }
    internal static StartupTaskState[] ReadAll(IEnumerable<string> paths)=>paths.Select(Read).Where(state=>state!=null).ToArray();
    internal static void Set(StartupTaskState expected,bool enabled,bool missingAllowed=false)
    {
        using var session=new MaintenanceStartup.TaskSession();
        dynamic task=Find(session,expected.Path);
        // A task Windows has since removed has no state left to restore.
        if(task==null){if(missingAllowed)return;throw new IOException("Scheduled task "+expected.Path+" was removed; its saved state is retained");}
        if(MaintenanceStartup.TaskDefinition(task)!=expected.Definition)throw new IOException("Scheduled task "+expected.Path+" changed outside Pathweft. Stopped to preserve it.");
        task.Enabled=enabled;
        if((bool)task.Enabled!=enabled)throw new IOException("Windows did not retain the state of scheduled task "+expected.Path);
    }
    static object Find(MaintenanceStartup.TaskSession session,string path)
    {
        int split=path.LastIndexOf('\\');if(split<0)throw new ArgumentException("Scheduled task path must be absolute");
        try
        {
            dynamic folder=session.Own(session.Root.GetFolder(split==0?"\\":path[..split]));
            return session.Own(folder.GetTask(path[(split+1)..]));
        }
        catch(Exception error)when(error is COMException or FileNotFoundException or DirectoryNotFoundException){return null;}
    }
}
