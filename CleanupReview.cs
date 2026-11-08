using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

internal sealed record CleanupTarget(string Id,string Name,string Description,string Root,string Pattern="*");
internal sealed record CleanupMember(string Relative,long Size,long Modified,uint Volume,ulong Identity);
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
            return [
                new("user-temp","User temporary files","Unused temporary files; files in use are skipped",Path.GetTempPath()),
                new("windows-temp","Windows temporary files","Windows temporary files; administrator access required",Path.Combine(windows,"Temp")),
                new("error-reports","Error reports","Saved Windows Error Reporting files",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Microsoft","Windows","WER")),
                new("thumbnail-cache","Thumbnail cache","Explorer thumbnail cache only; thumbnails will be rebuilt",Path.Combine(local,"Microsoft","Windows","Explorer"),"thumbcache_*.db")
            ];
        }
    }
    internal string Token {get;}
    internal CleanupTarget Target {get;}
    internal long Count {get;private set;}
    internal long Bytes {get;private set;}
    internal long Unreadable {get;private set;}
    readonly string manifest;
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
    }
    internal CleanupOutcome Clean(CancellationToken cancel)
    {
        long deleted=0,skipped=0,bytes=0;
        using var source=new StreamReader(manifest);
        while(source.ReadLine() is {} line)
        {
            if(cancel.IsCancellationRequested)return new(deleted,skipped,bytes,true);
            try
            {
                var item=JsonSerializer.Deserialize<CleanupMember>(line)??throw new InvalidDataException("Invalid cleanup member");
                string path=Path.GetFullPath(Path.Combine(Target.Root,item.Relative)),root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Target.Root));
                if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Cleanup member is outside its reviewed category");
                string parent=Path.GetDirectoryName(path);bool linked=false;
                while(parent!=null&&parent.Length>=root.Length)
                {if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0){linked=true;break;}parent=Path.GetDirectoryName(parent);}
                if(linked){skipped++;continue;}
                // No shared write/delete: an active writer or replacement cannot
                // race the identity check and exact-handle disposition below.
                using var handle=Open(path,true);var actual=Info(handle,path);
                long length=((long)actual.HighSize<<32)|actual.LowSize;
                if(actual.Links!=1||actual.Volume!=item.Volume||actual.Identity!=item.Identity||actual.Modified!=item.Modified||length!=item.Size||
                   (actual.Attributes&(uint)(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0){skipped++;continue;}
                bool disposition=true;if(!SetFileInformationByHandle(handle,4,ref disposition,1))throw new System.ComponentModel.Win32Exception();
                deleted++;bytes+=length;
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){skipped++;}
        }
        return new(deleted,skipped,bytes,false);
    }
    internal MaintenanceRow Row=>new(Target.Id,Target.Name,"This computer",$"{Count:N0} reviewed files",Target.Description+(Unreadable>0?$" · {Unreadable:N0} unavailable entries":""),Bytes);
    public void Dispose(){if(disposed)return;disposed=true;File.Delete(manifest);}
    static SafeFileHandle Open(string path,bool delete)
    {
        var handle=CreateFile(path,0x80u|(delete?0x10000u:0),delete?1u:7u,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
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
