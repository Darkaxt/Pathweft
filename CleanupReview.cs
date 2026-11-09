using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

internal sealed record CleanupTarget(string Id,string Name,string Description,string Root,string Pattern="*");
internal sealed record CleanupMember(string Relative,long Size,long Modified,uint Volume,ulong Identity,int Kind=0,int Group=-1);
internal sealed record CleanupOutcome(long Deleted,long Skipped,long Bytes,bool Cancelled);

// Curated targets and analyze-first flow from SysOptimizer CleanupService (MIT).
// A disk-streamed review and exact-handle deletion replace the donor's rescan /
// recursive directory delete. New/changed members are never part of the review.
internal sealed class CleanupReview : IDisposable
{
    internal static CleanupTarget[] Targets
    {
        get
        {
            string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows),local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var scoop=ScoopCleanup.Locations();
            return [
                new("user-temp","User temporary files","Unused temporary files; files in use are skipped",Path.GetTempPath()),
                new("windows-temp","Windows temporary files","Windows temporary files; administrator access required",Path.Combine(windows,"Temp")),
                new("error-reports","Error reports","Saved Windows Error Reporting files",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Microsoft","Windows","WER")),
                new("thumbnail-cache","Thumbnail cache","Explorer thumbnail cache only; thumbnails will be rebuilt",Path.Combine(local,"Microsoft","Windows","Explorer"),"thumbcache_*.db"),
                new("scoop-versions","Scoop obsolete versions","Old installed versions; current, persistent data and packages in use are preserved",Path.Combine(scoop.Root,"apps")),
                new("scoop-cache","Scoop download cache","Completed downloads for older installed versions; current and partial downloads are preserved",scoop.Cache)
            ];
        }
    }
    internal string Token {get;}
    internal CleanupTarget Target {get;}
    internal long Count {get;private set;}
    internal long Bytes {get;private set;}
    internal long Unreadable {get;private set;}
    readonly string manifest;
    readonly List<ScoopCleanup.Group> groups=[];
    readonly List<string> examples=[];
    readonly HashSet<int> excludedGroups=[];
    bool disposed;
    internal CleanupReview(CleanupTarget target,string directory=null)
    {
        Target=target;Token=Guid.NewGuid().ToString("N");directory??=Path.Combine(DataFolder.Cache,"Maintenance");
        Directory.CreateDirectory(directory);manifest=Path.Combine(directory,Token+".jsonl");
    }
    internal void Analyze(CancellationToken cancel)
    {
        using var stream=new FileStream(manifest,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var output=new StreamWriter(stream);
        if(ScoopCleanup.IsScoop(Target))
        {
            foreach(var group in ScoopCleanup.Candidates(Target,cancel))
            {
                cancel.ThrowIfCancellationRequested();
                int index=groups.Count;long before=Count,beforeBytes=Bytes,unreadable=Unreadable;groups.Add(group);
                try
                {
                    using var guard=ScoopCleanup.Guard(group,cancel);
                    if(group.Cache){foreach(string file in ScoopCleanup.CacheFiles(group))Record(file,0,index);}
                    else Scan(group.Folder,index);
                    if(Unreadable>unreadable){excludedGroups.Add(index);Count=before;Bytes=beforeBytes;}
                    if(Count>before&&examples.Count<12)examples.Add(group.Cache?Path.GetFileName(Path.GetDirectoryName(group.Current))+" · old downloads":group.Folder);
                }
                catch(Exception ex)when(ScoopCleanup.Expected(ex)){Unreadable++;excludedGroups.Add(index);Count=before;Bytes=beforeBytes;}
            }
            return;
        }
        var stack=new Stack<string>();stack.Push(Target.Root);
        while(stack.TryPop(out var folder))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0){Unreadable++;continue;}
                foreach(string file in Directory.EnumerateFiles(folder,Target.Pattern))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        using var handle=Open(file,false);var item=Info(handle,file);
                        if(item.Links!=1||(item.Attributes&(uint)(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0){Unreadable++;continue;}
                        long length=((long)item.HighSize<<32)|item.LowSize;
                        output.WriteLine(JsonSerializer.Serialize(new CleanupMember(Path.GetRelativePath(Target.Root,file),length,item.Modified,item.Volume,item.Identity)));
                        Count++;Bytes+=length;
                    }
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){Unreadable++;}
                }
                foreach(string child in Directory.EnumerateDirectories(folder))
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)stack.Push(child);else Unreadable++;
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){Unreadable++;}
        }
        void Record(string path,int kind,int group)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var handle=Open(path,false);var item=Info(handle,path);
                if(item.Links!=1&&kind==0){Unreadable++;return;}
                bool directory=(item.Attributes&(uint)FileAttributes.Directory)!=0,link=(item.Attributes&(uint)FileAttributes.ReparsePoint)!=0;
                if(kind==0&&(directory||link)||kind==1&&(!directory||link)||kind==2&&(!directory||!link))return;
                long size=kind==0?((long)item.HighSize<<32)|item.LowSize:0;
                output.WriteLine(JsonSerializer.Serialize(new CleanupMember(Path.GetRelativePath(Target.Root,path),size,item.Modified,item.Volume,item.Identity,kind,group)));
                if(kind==0){Count++;Bytes+=size;}
            }
            catch(Exception ex)when(ScoopCleanup.Expected(ex)){Unreadable++;}
        }
        void Scan(string folder,int group)
        {
            var pending=new Stack<(string Folder,bool Visited)>();pending.Push((folder,false));
            while(pending.TryPop(out var entry))
            {
                cancel.ThrowIfCancellationRequested();
                if(entry.Visited){Record(entry.Folder,1,group);continue;}
                if((File.GetAttributes(entry.Folder)&FileAttributes.ReparsePoint)!=0){Unreadable++;continue;}
                foreach(string file in Directory.EnumerateFiles(entry.Folder))Record(file,0,group);
                pending.Push((entry.Folder,true));
                foreach(string child in Directory.EnumerateDirectories(entry.Folder))
                {
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push((child,false));
                    else if(ScoopCleanup.PersistentLink(child,groups[group]))Record(child,2,group);
                    else Unreadable++;
                }
            }
        }
    }
    internal CleanupOutcome Clean(CancellationToken cancel)
    {
        long deleted=0,skipped=0,bytes=0;
        using var source=new StreamReader(manifest);
        IDisposable guard=null;int guarded=-1;bool allowed=true;
        bool groupSkipped=false;var metadata=new List<CleanupMember>(4);
        try
        {
        while(source.ReadLine() is {} line)
        {
            if(cancel.IsCancellationRequested)return new(deleted,skipped,bytes,true);
            try
            {
                var item=JsonSerializer.Deserialize<CleanupMember>(line)??throw new InvalidDataException("Invalid cleanup member");
                if(item.Group!=guarded)
                {
                    guard?.Dispose();guard=null;guarded=item.Group;allowed=true;
                    groupSkipped=false;metadata.Clear();
                    if(item.Group>=0)
                        try{if(excludedGroups.Contains(item.Group))throw new IOException("Version contains unavailable members");guard=ScoopCleanup.Guard(groups[item.Group],cancel);}
                        catch(Exception ex)when(ScoopCleanup.Expected(ex)){allowed=false;}
                }
                if(!allowed){skipped++;continue;}
                string path=Path.GetFullPath(Path.Combine(Target.Root,item.Relative)),root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Target.Root));
                if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Cleanup member is outside its reviewed category");
                if(item.Group>=0&&!groups[item.Group].Cache&&item.Kind==0&&ScoopCleanup.Same(Path.GetDirectoryName(path),groups[item.Group].Folder)&&
                   Path.GetFileName(path) is "install.json" or "manifest.json" or "scoop-install.json" or "scoop-manifest.json")
                {metadata.Add(item);continue;}
                string parent=Path.GetDirectoryName(path);bool linked=false;
                using var parents=new ParentHandles();
                while(parent!=null&&(item.Group>=0||parent.Length>=root.Length))
                {
                    if(item.Group>=0)parents.Add(Open(parent,false,3));
                    if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0){linked=true;break;}parent=Path.GetDirectoryName(parent);
                }
                if(linked){skipped++;groupSkipped=true;continue;}
                // Keep the reviewed object itself open before touching package
                // metadata; a replaced directory must never lose its identity.
                using var handle=Open(path,true);var actual=Info(handle,path);
                long length=((long)actual.HighSize<<32)|actual.LowSize;
                bool directory=(actual.Attributes&(uint)FileAttributes.Directory)!=0,reparse=(actual.Attributes&(uint)FileAttributes.ReparsePoint)!=0;
                if(actual.Volume!=item.Volume||actual.Identity!=item.Identity||
                   item.Kind==0&&(actual.Links!=1||actual.Modified!=item.Modified||length!=item.Size||directory||reparse)||
                   item.Kind==1&&(!directory||reparse)||
                   item.Kind==2&&(!directory||!reparse||!ScoopCleanup.PersistentLink(path,groups[item.Group]))) {skipped++;groupSkipped=true;continue;}
                if(item.Group>=0&&item.Kind==1&&ScoopCleanup.Same(path,groups[item.Group].Folder))
                {
                    // Keep installation identity until every other reviewed
                    // member is gone. New/changed/busy leftovers remain eligible
                    // for a future review instead of losing their metadata.
                    if(groupSkipped||Directory.EnumerateFileSystemEntries(path).Any(p=>!metadata.Any(m=>ScoopCleanup.Same(p,Path.Combine(Target.Root,m.Relative)))))
                    {skipped++;continue;}
                    var metadataHandles=new List<SafeFileHandle>();
                    try
                    {
                        foreach(var m in metadata)
                        {
                            var h=Open(Path.Combine(Target.Root,m.Relative),true);metadataHandles.Add(h);var metadataInfo=Info(h,m.Relative);
                            if(metadataInfo.Links!=1||metadataInfo.Identity!=m.Identity||metadataInfo.Volume!=m.Volume||metadataInfo.Modified!=m.Modified||((long)metadataInfo.HighSize<<32|metadataInfo.LowSize)!=m.Size||
                               (metadataInfo.Attributes&(uint)(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0)throw new IOException("Package metadata changed");
                        }
                        foreach(var h in metadataHandles){bool remove=true;if(!SetFileInformationByHandle(h,4,ref remove,1))throw new System.ComponentModel.Win32Exception();}
                        deleted+=metadata.Count;bytes+=metadata.Sum(m=>m.Size);
                    }
                    finally{foreach(var h in metadataHandles)h.Dispose();}
                }
                bool disposition=true;if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw new System.ComponentModel.Win32Exception();
                if(item.Kind==0){deleted++;bytes+=length;}
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){skipped++;groupSkipped=true;}
        }
        return new(deleted,skipped,bytes,false);
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){return new(deleted,skipped,bytes,true);}
        finally{guard?.Dispose();}
    }
    internal MaintenanceRow Row=>new(Target.Id,Target.Name,"This computer",$"{Count:N0} reviewed files",Target.Description+(Unreadable>0?$" · {Unreadable:N0} unavailable entries":"")+(examples.Count>0?"\n"+string.Join("\n",examples):""),Bytes);
    public void Dispose(){if(disposed)return;disposed=true;File.Delete(manifest);}
    sealed class ParentHandles:IDisposable
    {readonly List<SafeFileHandle> handles=[];internal void Add(SafeFileHandle h)=>handles.Add(h);public void Dispose(){foreach(var h in handles)h.Dispose();}}
    static SafeFileHandle Open(string path,bool delete,uint? share=null)
    {
        var handle=CreateFile(path,0x80u|(delete?0x10000u:0),share??(delete?1u:7u),IntPtr.Zero,3,0x02200000,IntPtr.Zero);
        if(handle.IsInvalid){handle.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),path);}return handle;
    }
    static FileInformation Info(SafeFileHandle handle,string path)
    {if(!GetFileInformationByHandle(handle,out var info))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),path);return info;}
    [StructLayout(LayoutKind.Sequential)]struct FileInformation
    {
        public uint Attributes,CreatedLow,CreatedHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,HighSize,LowSize,Links,IndexHigh,IndexLow;
        public long Modified=>((long)WriteHigh<<32)|WriteLow;
        public ulong Identity=>((ulong)IndexHigh<<32)|IndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation information);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetFileInformationByHandle(SafeFileHandle handle,int information,[MarshalAs(UnmanagedType.U1)]ref bool disposition,uint size);
}
