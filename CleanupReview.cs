using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

internal sealed record CleanupTarget(string Id,string Name,string Description,string Root,string Pattern="*");
internal sealed record CleanupMember(string Relative,long Size,long Modified,uint Volume,ulong Identity,int Group=-1);
internal sealed record CleanupOutcome(long Deleted,long Skipped,long Bytes,bool Cancelled,string Details="");
internal sealed record CleanupProgress(string Phase,string Path,long Completed,long Total,long Removed,long Skipped,long Bytes);

// Curated targets and analyze-first flow from SysOptimizer CleanupService (MIT).
// Temporary files/cache use a streamed exact-file review. Obsolete Scoop
// versions use indexed estimates and explicit whole-folder deletion instead.
internal sealed class CleanupReview : IDisposable
{
    internal static CleanupTarget[] Targets
    {
        get
        {
            string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows),local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var scoop=ScoopCleanup.Locations();
            CleanupTarget[] basic=[
                new("user-temp","User temporary files","Unused temporary files; files in use are skipped",Path.GetTempPath()),
                new("windows-temp","Windows temporary files","Windows temporary files; administrator access required",Path.Combine(windows,"Temp")),
                new("error-reports","Error reports","Saved Windows Error Reporting files",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Microsoft","Windows","WER")),
                new("thumbnail-cache","Thumbnail cache","Explorer thumbnail cache only; thumbnails will be rebuilt",Path.Combine(local,"Microsoft","Windows","Explorer"),"thumbcache_*.db"),
                new("scoop-versions","Scoop obsolete versions","Old installed versions; current, persistent data and packages in use are preserved",Path.Combine(scoop.Root,"apps")),
                new("scoop-cache","Scoop download cache","Completed downloads for older installed versions; current and partial downloads are preserved",scoop.Cache)
            ];
            return basic.Concat(ManagerCleanup.AvailableActions().Select(m=>new CleanupTarget("manager-"+m.Id,m.Name,m.Description,""))).ToArray();
        }
    }
    internal string Token {get;}
    internal CleanupTarget Target {get;}
    internal long Count {get;private set;}
    internal long Bytes {get;private set;}
    internal long Unreadable {get;private set;}
    readonly string manifest;
    readonly List<ScoopCleanup.Group> groups=[];
    readonly List<long> versionBytes=[];
    internal bool IndexedVersions {get;private set;}
    internal int UnknownVersionSizes=>versionBytes.Count(value=>value<0);
    readonly List<string> examples=[];
    readonly HashSet<int> excludedGroups=[];
    ManagerCleanup.Review managerReview;
    bool disposed;
    internal CleanupReview(CleanupTarget target,string directory=null)
    {
        Target=target;Token=Guid.NewGuid().ToString("N");directory??=Path.Combine(DataFolder.Cache,"Maintenance");
        Directory.CreateDirectory(directory);manifest=Path.Combine(directory,Token+".jsonl");
    }
    internal void Analyze(CancellationToken cancel)
    {
        if(ManagerCleanup.IsManager(Target))
        {
            managerReview=ManagerCleanup.Inspect(Target,cancel);Count=managerReview.Count;Bytes=managerReview.Bytes??0;return;
        }
        if(Target.Id=="scoop-versions"){AnalyzeVersions(cancel);return;}
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
                    foreach(string file in ScoopCleanup.CacheFiles(group))Record(file,index);
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
        void Record(string path,int group)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                using var handle=Open(path,false);var item=Info(handle,path);
                if(item.Links!=1){Unreadable++;return;}
                bool directory=(item.Attributes&(uint)FileAttributes.Directory)!=0,link=(item.Attributes&(uint)FileAttributes.ReparsePoint)!=0;
                if(directory||link)return;
                long size=((long)item.HighSize<<32)|item.LowSize;
                output.WriteLine(JsonSerializer.Serialize(new CleanupMember(Path.GetRelativePath(Target.Root,path),size,item.Modified,item.Volume,item.Identity,group)));
                Count++;Bytes+=size;
            }
            catch(Exception ex)when(ScoopCleanup.Expected(ex)){Unreadable++;}
        }
    }
    void AnalyzeVersions(CancellationToken cancel)
    {
        groups.AddRange(ScoopCleanup.Candidates(Target,cancel));
        var sizes=ScoopCleanup.VersionSizes(groups,cancel);
        IndexedVersions=sizes.Indexed;
        foreach(var group in groups)
        {
            cancel.ThrowIfCancellationRequested();
            bool known=sizes.Bytes.TryGetValue(group.Folder,out long size);
            versionBytes.Add(known?size:-1);Count++;if(known)Bytes+=size;
            if(examples.Count<12)examples.Add(group.Folder);
        }
    }
    CleanupOutcome CleanVersions(CancellationToken cancel,Action<CleanupProgress> progress)
    {
        long deleted=0,skipped=0,bytes=0;var failures=new List<string>();
        try
        {
            progress?.Invoke(new("Deleting obsolete versions","",0,groups.Count,0,0,0));
            for(int i=0;i<groups.Count;i++)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke(new("Deleting obsolete versions",groups[i].Folder,i,groups.Count,deleted,skipped,bytes));
                try
                {
                    using var guard=ScoopCleanup.Guard(groups[i],cancel);
                    ScoopCleanup.DeleteVersion(groups[i],cancel);
                    deleted++;bytes+=Math.Max(0,versionBytes[i]);
                }
                catch(Exception error)when(ScoopCleanup.Expected(error)){
#if DEBUG
                    ScoopCleanup.DeleteFailureForVerification?.Invoke(error.ToString());
#endif
                    skipped++;
                    if(failures.Count<50)failures.Add(groups[i].Folder+"\n"+error.Message+
                        (Directory.Exists(groups[i].Folder)?"\nRemoval was incomplete; this obsolete version may be partially removed.":""));
                }
                progress?.Invoke(new("Deleting obsolete versions",groups[i].Folder,i+1,groups.Count,deleted,skipped,bytes));
            }
            return new(deleted,skipped,bytes,false,string.Join("\n\n",failures));
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){return new(deleted,skipped,bytes,true,string.Join("\n\n",failures));}
    }
    internal CleanupOutcome Clean(CancellationToken cancel,Action<CleanupProgress> progress=null)
    {
        if(ManagerCleanup.IsManager(Target))return ManagerCleanup.Clean(managerReview??throw new InvalidOperationException("Analyze this manager first"),Target,cancel,progress);
        if(Target.Id=="scoop-versions")return CleanVersions(cancel,progress);
        long deleted=0,skipped=0,bytes=0;
        long completed=0;progress?.Invoke(new("Deleting reviewed files","",0,Count,0,0,0));
        using var source=new StreamReader(manifest);
        IDisposable guard=null;int guarded=-1;bool allowed=true;
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
                    if(item.Group>=0)
                        try{if(excludedGroups.Contains(item.Group))throw new IOException("Version contains unavailable members");guard=ScoopCleanup.Guard(groups[item.Group],cancel);}
                        catch(Exception ex)when(ScoopCleanup.Expected(ex)){allowed=false;}
                }
                if(!allowed){skipped++;continue;}
                string path=Path.GetFullPath(Path.Combine(Target.Root,item.Relative)),root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Target.Root));
                progress?.Invoke(new("Deleting reviewed files",path,completed,Count,deleted,skipped,bytes));
                if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Cleanup member is outside its reviewed category");
                string parent=Path.GetDirectoryName(path);bool linked=false;
                using var parents=new ParentHandles();
                while(parent!=null&&(item.Group>=0||parent.Length>=root.Length))
                {
                    if(item.Group>=0)parents.Add(Open(parent,false,3));
                    if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0){linked=true;break;}parent=Path.GetDirectoryName(parent);
                }
                if(linked){skipped++;continue;}
                using var handle=Open(path,true);var actual=Info(handle,path);
                long length=((long)actual.HighSize<<32)|actual.LowSize;
                bool directory=(actual.Attributes&(uint)FileAttributes.Directory)!=0,reparse=(actual.Attributes&(uint)FileAttributes.ReparsePoint)!=0;
                if(actual.Volume!=item.Volume||actual.Identity!=item.Identity||actual.Links!=1||actual.Modified!=item.Modified||length!=item.Size||directory||reparse)
                {skipped++;continue;}
                bool disposition=true;if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw new System.ComponentModel.Win32Exception();
                deleted++;bytes+=length;
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){skipped++;}
            finally{completed++;progress?.Invoke(new("Deleting reviewed files","",completed,Count,deleted,skipped,bytes));}
        }
        return new(deleted,skipped,bytes,false);
        }
        catch(OperationCanceledException)when(cancel.IsCancellationRequested){return new(deleted,skipped,bytes,true);}
        finally{guard?.Dispose();}
    }
    internal MaintenanceRow Row=>managerReview is {} manager
        ?new(Target.Id,Target.Name,"Current user",manager.Id=="dotnet-tool"?$"{Count:N0} reviewed files":$"{Count:N0} cache entries",manager.Details,manager.Bytes,CanChange:manager.Supported&&Count>0)
        :new(Target.Id,Target.Name,"This computer",Target.Id=="scoop-versions"?$"{Count:N0} obsolete versions":$"{Count:N0} reviewed files",Target.Description+(UnknownVersionSizes>0?$"\n{UnknownVersionSizes:N0} sizes could not be measured.":"")+(Unreadable>0?$"\n{Unreadable:N0} entries could not be reviewed.":"")+(examples.Count>0?"\n"+string.Join("\n",examples):""),UnknownVersionSizes==groups.Count&&groups.Count>0?null:Bytes);
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
