using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

internal sealed record StartupFileState(string Path,string Hash,uint Volume,ulong Identity,long Length,long Modified);
internal sealed record StartupTaskState(string Path,string Definition,bool Enabled);

// Common Windows startup sources. Reading never resolves/executes a shortcut or
// runs/registers a task. Each mutation reopens its actual source in the worker.
internal static class MaintenanceStartup
{
#if DEBUG
    internal static int ReadCountForVerification;
#endif
    internal static readonly string[] RegistryPaths=[Maintenance.RunPath,Maintenance.RunPath+"Once",@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run"];
    const string DisabledFolder="Pathweft-disabled";
    internal static string RegistryId(string hive,RegistryView view,string path,string name)=>path==Maintenance.RunPath?Maintenance.StartupId(hive,view,name):"startup:"+Maintenance.Id(hive+"/"+view+"/"+path,name);
    internal static string FileId(string path)=>"startup-file:"+Maintenance.Id("file",Path.GetFullPath(path).ToUpperInvariant());
    internal static string TaskId(string path)=>"startup-task:"+Maintenance.Id("task",path.ToUpperInvariant());
    static IEnumerable<(string Path,string Scope)> Folders()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Current user");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),"All users");
    }
    static bool ReadError(Exception error)=>error is IOException or UnauthorizedAccessException or System.Security.SecurityException or Win32Exception or COMException;
    internal static MaintenanceRow[] Read(Dictionary<string,MaintenanceUndo> undo,CancellationToken cancel)
    {
#if DEBUG
        Interlocked.Increment(ref ReadCountForVerification);
#endif
        var rows=new List<MaintenanceRow>();
        foreach(var source in Maintenance.StartupRoots)
        foreach(string path in RegistryPaths)
        {
            cancel.ThrowIfCancellationRequested();string scope=source.Hive==RegistryHive.CurrentUser?"Current user":"All users";
            try
            {
                using var root=RegistryKey.OpenBaseKey(source.Hive,source.View);using var key=root.OpenSubKey(path);if(key==null)continue;
                foreach(string name in key.GetValueNames())
                {
                    cancel.ThrowIfCancellationRequested();string state=path==Maintenance.RunPath?Approval(root,source.View==RegistryView.Registry32?"Run32":"Run",name):"Enabled";
                    rows.Add(new(RegistryId(root.Name,source.View,path,name),name,scope,state,root.Name+"\\"+path+" · "+source.View+" · "+key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames),Type:path.EndsWith("RunOnce")?"Registry RunOnce":path.Contains("Policies")?"Registry policy Run":"Registry Run",CanChange:state=="Enabled"));
                }
            }
            catch(Exception error)when(ReadError(error)){rows.Add(new("source:"+source.Hive+source.View+path,path,scope,"Unavailable",error.Message,Type:"Registry",CanChange:false));}
        }
        foreach(var folder in Folders())
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if(!Directory.Exists(folder.Path))continue;
                using var root=RegistryKey.OpenBaseKey(folder.Scope=="Current user"?RegistryHive.CurrentUser:RegistryHive.LocalMachine,RegistryView.Registry64);
                foreach(string file in Directory.EnumerateFiles(folder.Path))
                {
                    cancel.ThrowIfCancellationRequested();string state=Approval(root,"StartupFolder",Path.GetFileName(file));
                    rows.Add(new(FileId(file),Path.GetFileName(file),folder.Scope,state,file,Type:Path.GetExtension(file).Equals(".lnk",StringComparison.OrdinalIgnoreCase)?"Startup shortcut":"Startup file",CanChange:state=="Enabled"));
                }
            }
            catch(Exception error)when(ReadError(error)){rows.Add(new("source:"+folder.Path,"Startup folder",folder.Scope,"Unavailable",error.Message,Type:"Startup folder",CanChange:false));}
        }
        try
        {
            using var tasks=new TaskSession();tasks.Each((task,com)=>
            {
                if(!Relevant(task,com,out string triggers))return;
                string path=task.Path;bool enabled=task.Enabled;
                rows.Add(new(TaskId(path),(string)task.Name,"All users",enabled?"Enabled":"Disabled",path+" · "+triggers+" · "+Actions(task,com),Type:"Scheduled task",CanChange:enabled));
            },cancel,(path,error)=>rows.Add(new("source:task:"+path,path,"This computer","Unavailable",error.Message,Type:"Scheduled tasks",CanChange:false)));
        }
        catch(Exception error)when(ReadError(error)){rows.Add(new("source:tasks","Scheduled tasks","This computer","Unavailable",error.Message,Type:"Scheduled tasks",CanChange:false));}
        foreach(var saved in undo.Values.Where(s=>s.Id.StartsWith("startup")))
        {
            if(rows.Any(r=>r.Id==saved.Id))continue;
            if(saved.StartupFile is {} file)rows.Add(new(saved.Id,Path.GetFileName(file.Path),FolderScope(file.Path),saved.Phase=="applied"?"Disabled":"Needs review",file.Path,Type:"Startup file",CanChange:false));
            else if(saved.StartupTask is {} task)rows.Add(new(saved.Id,task.Path,"All users","Needs review","Saved task is no longer present",Type:"Scheduled task",CanChange:false));
            else if(saved.Before.Length==1)
            {var item=saved.Before[0];rows.Add(new(saved.Id,item.Name,item.Hive=="HKEY_CURRENT_USER"?"Current user":"All users",saved.Phase=="applied"?"Disabled":"Needs review",item.Hive+"\\"+item.Path+" · "+item.View+" · "+item.Value.ToString(),Type:item.Path.EndsWith("RunOnce")?"Registry RunOnce":item.Path.Contains("Policies")?"Registry policy Run":"Registry Run",CanChange:false));}
        }
        return rows.OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(r=>r.Type).ToArray();
    }
    static string Approval(RegistryKey root,string group,string name)
    {
        using var approved=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\"+group);
        if(approved?.GetValue(name) is not byte[] bytes)return "Enabled";
        // StartupApproved is Windows-owned. Read its known states, never invent
        // an approval blob or overwrite one to enable an item.
        if(bytes.Length<12)return "Approval unknown";
        return BitConverter.ToUInt32(bytes) switch{2 or 6=>"Enabled",3 or 7=>"Disabled",_=>"Approval unknown"};
    }
    static string FolderScope(string path)=>Folders().FirstOrDefault(f=>string.Equals(Path.GetDirectoryName(path),f.Path,StringComparison.OrdinalIgnoreCase)).Scope??throw new InvalidDataException("Startup file is outside the Windows startup folders");
    static string DisabledPath(string path)
    {
        FolderScope(path);if(Path.GetFileName(path) is "" or "." or "..")throw new InvalidDataException("Invalid startup file");
        return Path.Combine(Path.GetDirectoryName(path),DisabledFolder,Path.GetFileName(path));
    }
    internal static string ChangeFile(string id,bool restore,string journal)
    {
        using var lease=new Maintenance.JournalLease(journal);var records=Maintenance.Undo(journal);
        StartupFileState saved;string source,destination;
        if(restore)
        {
            saved=records.GetValueOrDefault(id)?.StartupFile??throw new IOException("No startup file is saved for Restore");
            if(FileId(saved.Path)!=id)throw new InvalidDataException("Saved startup file identity differs");
            source=DisabledPath(saved.Path);destination=saved.Path;
            if(!File.Exists(source)&&File.Exists(destination))
            {
                using var original=OpenFile(destination);if(Snapshot(destination,original)!=saved)throw new IOException("The startup file changed outside Pathweft");
                records.Remove(id);Maintenance.Save(records,journal);return "Original startup file is already present.";
            }
        }
        else
        {
            if(records.ContainsKey(id))throw new IOException("Original startup file is already saved");
            source=Folders().Where(f=>Directory.Exists(f.Path)).SelectMany(f=>Directory.EnumerateFiles(f.Path)).SingleOrDefault(f=>FileId(f)==id)??throw new IOException("The startup file changed or was removed; refresh first");
            destination=DisabledPath(source);saved=null;
        }
        // Pin the source and both parents against replacement while journaling
        // and renaming. No copies, shortcut execution, overwrite or cross-volume move.
        using var sourceParent=OpenDirectory(Path.GetDirectoryName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));using var destinationParent=OpenDirectory(Path.GetDirectoryName(destination));
        using var handle=OpenFile(source);var actual=Snapshot(restore?saved.Path:source,handle);
        if(restore&&actual!=saved)throw new IOException("The disabled startup file changed outside Pathweft. Restore stopped.");
        if(!restore){records[id]=new(id,[],[],StartupFile:actual);Maintenance.Save(records,journal);}
        Rename(handle,destination);
        if(restore)records.Remove(id);else records[id]=records[id] with{Phase="applied"};Maintenance.Save(records,journal);
        return restore?"Original startup file restored.":"Startup file disabled. Its original file is retained for Restore.";
    }
    static SafeFileHandle OpenDirectory(string path)
    {
        // Check the entire parent chain; a junction must not redirect privileged writes.
        for(var dir=new DirectoryInfo(path);dir!=null;dir=dir.Parent)if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Startup path contains a link; no changes made");
        var handle=CreateFile(path,0x80,1|2,IntPtr.Zero,3,0x02000000|0x00200000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception();}return handle;
    }
    static SafeFileHandle OpenFile(string path)
    {
        var handle=CreateFile(path,0x80000000|0x10000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new Win32Exception();}return handle;
    }
    static StartupFileState Snapshot(string original,SafeFileHandle handle)
    {
        if(!GetFileInformationByHandle(handle,out var info))throw new Win32Exception();
        if((info.Attributes&(0x400u|0x10u))!=0||info.Links!=1)throw new IOException("Linked startup files cannot be changed");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[65536];long offset=0;int count;
        while((count=RandomAccess.Read(handle,buffer,offset))>0){hash.AppendData(buffer,0,count);offset+=count;}
        return new(original,Convert.ToHexString(hash.GetHashAndReset()),info.Volume,((ulong)info.IndexHigh<<32)|info.IndexLow,((long)info.SizeHigh<<32)|info.SizeLow,((long)info.WriteHigh<<32)|info.WriteLow);
    }
    static void Rename(SafeFileHandle handle,string destination)
    {
        byte[] name=Encoding.Unicode.GetBytes(Path.GetFullPath(destination));int lengthOffset=IntPtr.Size==8?16:8,nameOffset=lengthOffset+4;
        // Kernel32 resolves an absolute DOS path before the native rename. Its
        // FileName string must be terminated, even though FileNameLength does
        // not include that terminator. An unterminated allocation can append
        // unrelated heap characters to the actual destination name.
        IntPtr data=Marshal.AllocHGlobal(nameOffset+name.Length+2);
        try
        {
            for(int i=0;i<nameOffset;i++)Marshal.WriteByte(data,i,0);
            Marshal.WriteInt32(data,lengthOffset,name.Length);Marshal.Copy(name,0,data+nameOffset,name.Length);
            Marshal.WriteInt16(data,nameOffset+name.Length,0);
            if(!SetFileInformationByHandle(handle,3,data,(uint)(nameOffset+name.Length+2)))throw new Win32Exception();
        }
        finally{Marshal.FreeHGlobal(data);}
    }
    internal static string ChangeTask(string id,bool restore,string journal)
    {
        using var lease=new Maintenance.JournalLease(journal);var records=Maintenance.Undo(journal);
        using var session=new TaskSession();object found=null;
        session.Each((task,com)=>{if(TaskId((string)task.Path)==id&&Relevant(task,com,out string triggers))found=task;},CancellationToken.None);
        if(found==null)throw new IOException("The startup task changed or was removed; refresh first");
        dynamic target=found;string path=target.Path,definition=TaskDefinition(target);bool enabled=target.Enabled;
        StartupTaskState before;
        if(restore)
        {
            before=records.GetValueOrDefault(id)?.StartupTask??throw new IOException("No original task state is saved");
            if(TaskId(before.Path)!=id||before.Path!=path||before.Definition!=definition)throw new IOException("The task definition changed outside Pathweft. Restore stopped.");
        }
        else
        {
            if(records.ContainsKey(id))throw new IOException("Original task state is already saved");
            before=new(path,definition,enabled);records[id]=new(id,[],[],StartupTask:before);Maintenance.Save(records,journal);
        }
        target.Enabled=restore?before.Enabled:false;
        if((bool)target.Enabled!=(restore?before.Enabled:false)||TaskDefinition(target)!=before.Definition)throw new IOException("Windows did not retain the requested task state; its original state remains saved");
        if(restore)records.Remove(id);else records[id]=records[id] with{Phase="applied"};Maintenance.Save(records,journal);
        return restore?"Original task state restored.":"Startup task disabled. Its definition and triggers are unchanged.";
    }
    static string TaskDefinition(dynamic task)
    {
        var xml=XDocument.Parse((string)task.Xml);XNamespace ns=xml.Root.Name.Namespace;
        xml.Root.Element(ns+"Settings")?.Element(ns+"Enabled")?.Remove();
        return Maintenance.Id("task-definition",xml.ToString(SaveOptions.DisableFormatting));
    }
    static bool Relevant(dynamic task,TaskSession com,out string label)
    {
        dynamic definition=com.Own(task.Definition),triggers=com.Own(definition.Triggers);var kinds=new HashSet<string>();
        for(int i=1;i<=(int)triggers.Count;i++){dynamic trigger=com.Own(triggers[i]);int type=trigger.Type;if(type==8)kinds.Add("At startup");if(type==9)kinds.Add("At logon");}
        label=string.Join(", ",kinds);return kinds.Count>0;
    }
    static string Actions(dynamic task,TaskSession com)
    {
        dynamic definition=com.Own(task.Definition),actions=com.Own(definition.Actions);var values=new List<string>();
        for(int i=1;i<=(int)actions.Count;i++){dynamic action=com.Own(actions[i]);values.Add((int)action.Type==0?(string)action.Path+" "+(string)action.Arguments:"Windows task action "+action.Type);}
        return string.Join("; ",values);
    }
    sealed class TaskSession : IDisposable
    {
        readonly List<object> owned=new();readonly dynamic scheduler;
        internal TaskSession(){scheduler=Own(Activator.CreateInstance(System.Type.GetTypeFromProgID("Schedule.Service",true)));try{scheduler.Connect();}catch{Dispose();throw;}}
        internal dynamic Own(object value){if(value!=null)owned.Add(value);return value;}
        internal void Each(Action<dynamic,TaskSession> visit,CancellationToken cancel,Action<string,Exception> unavailable=null)
        {
            var pending=new Stack<object>();pending.Push(Own(scheduler.GetFolder("\\")));
            while(pending.TryPop(out var item))
            {
                cancel.ThrowIfCancellationRequested();dynamic folder=item;string path=folder.Path;
                try
                {
                    dynamic tasks=Own(folder.GetTasks(1));
                    for(int i=1;i<=(int)tasks.Count;i++)
                    {
                        cancel.ThrowIfCancellationRequested();string taskPath=path+" / entry "+i;
                        try{dynamic task=Own(tasks[i]);taskPath=task.Path;visit(task,this);}
                        catch(Exception error)when(unavailable!=null&&ReadError(error)){unavailable(taskPath,error);}
                    }
                }
                catch(Exception error)when(unavailable!=null&&ReadError(error)){unavailable(path,error);}
                try{dynamic children=Own(folder.GetFolders(0));for(int i=1;i<=(int)children.Count;i++)pending.Push(Own(children[i]));}
                catch(Exception error)when(unavailable!=null&&ReadError(error)){unavailable(path+" / subfolders",error);}
            }
        }
        public void Dispose(){for(int i=owned.Count-1;i>=0;i--)if(Marshal.IsComObject(owned[i]))Marshal.ReleaseComObject(owned[i]);owned.Clear();}
    }
    [StructLayout(LayoutKind.Sequential)]struct Information{public uint Attributes,CreateLow,CreateHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle file,out Information info);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetFileInformationByHandle(SafeFileHandle file,int kind,IntPtr data,uint size);
}
