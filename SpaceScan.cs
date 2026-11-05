using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using FileExplorer.Search;
using FILETIME=System.Runtime.InteropServices.ComTypes.FILETIME;

namespace FileExplorer;

internal enum SpaceState{Pending,Scanning,Complete,Indexed,Incomplete,Denied,Cancelled,Failed,Reparse}
internal sealed record SpaceUpdate(string Path,string Parent,long Bytes,long Files,long Folders,SpaceState State,string Error="");
internal sealed record SpaceMember(string Path,string Name,bool Directory,bool Reparse,long Bytes,DateTime Modified);

// Selected metadata enumeration/traversal and value-publication ideas borrowed
// from Diskweft, MIT. Pin, hashes and adaptation boundary: vendor/Diskweft/NOTICES.txt.
// All mutable traversal/accounting state belongs to this invocation, not a donor
// application singleton. File contents and raw volumes are never opened.
internal static class SpaceScan
{
#if DEBUG
    internal static Action<string,CancellationToken> BeforeEnumeration;
    internal static Action<string,string> IndexObserved;
#endif
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct FindData
    {
        public uint Attributes;public FILETIME Created,Accessed,Modified;
        public uint SizeHigh,SizeLow,Reserved0,Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=14)]public string Alternate;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr FindFirstFileExW(string name,int level,out FindData data,int operation,IntPtr filter,int flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool FindNextFileW(IntPtr handle,out FindData data);
    [DllImport("kernel32.dll")]static extern bool FindClose(IntPtr handle);
    static string LongPath(string path)=>path.StartsWith(@"\\?\",StringComparison.Ordinal)?path:path.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+path[2..]:@"\\?\"+path;
    internal static void Enumerate(string path,Action<SpaceMember> found,CancellationToken token)
    {
#if DEBUG
        BeforeEnumeration?.Invoke(path,token);
#endif
        token.ThrowIfCancellationRequested();
        var handle=FindFirstFileExW(LongPath(path).TrimEnd('\\')+@"\*",1,out var data,0,IntPtr.Zero,2);
        if(handle==new IntPtr(-1)){int error=Marshal.GetLastWin32Error();if(error==2)return;throw new Win32Exception(error);}
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();if(data.Name is "." or "..")continue;
                long modified=((long)data.Modified.dwHighDateTime<<32)|(uint)data.Modified.dwLowDateTime;
                found(new(System.IO.Path.Combine(path,data.Name),data.Name,(data.Attributes&0x10)!=0,(data.Attributes&0x400)!=0,((long)data.SizeHigh<<32)|data.SizeLow,modified>0?DateTime.FromFileTimeUtc(modified):default));
            }while(FindNextFileW(handle,out data));
            int error=Marshal.GetLastWin32Error();if(error!=18)throw new Win32Exception(error);
        }
        finally{FindClose(handle);}
    }
    internal static List<SpaceMember> Children(string path,CancellationToken token)
    {var rows=new List<SpaceMember>();Enumerate(path,rows.Add,token);return rows;}
    sealed class Frame(string path,string parent)
    {
        internal string Path=path,Parent=parent;
        internal List<SpaceMember> Children=new();internal int Next;internal bool Opened,Valid=true,Indexed,FailedSelf;
        internal long Bytes,Files,Folders;
        internal SizeCache.Measurement Measurement;
    }
    internal static Task RunAsync(string root,bool preferIndex,string instance,Action<SpaceUpdate> publish,CancellationToken token)
        =>Task.Run(()=>Walk(root,preferIndex,instance,publish,token),token);
    static void Walk(string root,bool preferIndex,string instance,Action<SpaceUpdate> publish,CancellationToken token)
    {
        var stack=new Stack<Frame>();stack.Push(new(root,Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))));
        void Send(Frame frame,SpaceState state,string error="")=>publish(new(frame.Path,frame.Parent,frame.Bytes,frame.Files,frame.Folders,state,error));
        try
        {
            while(stack.Count>0)
            {
                token.ThrowIfCancellationRequested();var frame=stack.Peek();
                if(!frame.Opened)
                {
                    frame.Opened=true;frame.Measurement=new(frame.Path);Send(frame,SpaceState.Scanning);
                    try
                    {
                        int batch=0;long publishedBytes=0,publishedFiles=0;
                        Enumerate(frame.Path,entry=>
                        {
                            if(entry.Reparse)
                            {if(entry.Directory){frame.Folders++;publish(new(entry.Path,frame.Path,0,0,0,SpaceState.Reparse,"Linked directory; not traversed"));}return;}
                            if(entry.Directory){frame.Children.Add(entry);frame.Folders++;publish(new(entry.Path,frame.Path,0,0,0,SpaceState.Pending));}
                            else{frame.Bytes=checked(frame.Bytes+entry.Bytes);frame.Files++;}
                            if(++batch%64==0)
                            {
                                long delta=frame.Bytes-publishedBytes,files=frame.Files-publishedFiles;
                                foreach(var ancestor in stack.Skip(1)){ancestor.Bytes+=delta;ancestor.Files+=files;Send(ancestor,SpaceState.Scanning);}
                                publishedBytes=frame.Bytes;publishedFiles=frame.Files;Send(frame,SpaceState.Scanning);
                            }
                        },token);
                        // Publish the final direct-file delta to ancestors once.
                        foreach(var ancestor in stack.Skip(1)){ancestor.Bytes+=frame.Bytes-publishedBytes;ancestor.Files+=frame.Files-publishedFiles;Send(ancestor,SpaceState.Scanning);}
                        if(preferIndex&&frame.Children.Count>0)
                        {
                            Dictionary<string,long> sizes=null;
                            try{sizes=EverythingSearchClient.Shared.FolderSizesAsync(frame.Children.Select(child=>child.Path),instance,token).GetAwaiter().GetResult();}
                            catch(Exception ex)when(ex is InvalidOperationException or Win32Exception or DllNotFoundException or EntryPointNotFoundException){
#if DEBUG
                                IndexObserved?.Invoke(frame.Path,ex.ToString());
#endif
                            }
#if DEBUG
                            IndexObserved?.Invoke(frame.Path,"Returned: "+(sizes==null?"unavailable":string.Join(";",sizes.Select(pair=>pair.Key+"="+pair.Value))));
#endif
                            if(sizes!=null)foreach(var child in frame.Children.ToArray())
                                if(sizes.TryGetValue(Path.TrimEndingDirectorySeparator(child.Path),out long size))
                                {
                                    frame.Children.Remove(child);frame.Indexed=true;
                                    foreach(var ancestor in stack){ancestor.Bytes+=size;ancestor.Indexed=true;}
                                    publish(new(child.Path,frame.Path,size,0,0,SpaceState.Indexed));
                                }
                        }
                    }
                    catch(Exception ex)when(ex is Win32Exception or IOException or UnauthorizedAccessException)
                    {frame.Valid=false;frame.FailedSelf=true;frame.Children.Clear();Send(frame,ex is Win32Exception{NativeErrorCode:5} or UnauthorizedAccessException?SpaceState.Denied:SpaceState.Failed,ex.Message);}
                }
                if(frame.Next<frame.Children.Count)
                {var child=frame.Children[frame.Next++];stack.Push(new(child.Path,frame.Path));continue;}
                token.ThrowIfCancellationRequested();
                bool valid=frame.Valid&&!frame.Measurement.Invalid;
                if(valid&&!frame.Indexed)valid=frame.Measurement.Complete(Directory.GetLastWriteTimeUtc(frame.Path),frame.Bytes);
                if(!valid)frame.Measurement.Discard();
                if(!frame.FailedSelf)Send(frame,valid?(frame.Indexed?SpaceState.Indexed:SpaceState.Complete):SpaceState.Incomplete,valid?"":"Changed or incomplete while measuring");
                stack.Pop();frame.Measurement.Dispose();
                if(stack.TryPeek(out var parent))
                {parent.Valid&=valid;parent.Indexed|=frame.Indexed;parent.Folders+=frame.Folders;}
                // Direct-file and indexed-byte deltas were already propagated.
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested)
        {foreach(var frame in stack)Send(frame,SpaceState.Cancelled);throw;}
        finally{foreach(var frame in stack)frame.Measurement?.Dispose();}
    }
}
