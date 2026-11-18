using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FileExplorer;

// Which processes hold a file or anything under a folder. Port of PowerToys File Locksmith
// (v0.103.3151, 2d817b0a, MIT): FileLocksmithLibInterop\NtdllBase.h, NtdllExtensions.cpp/.h,
// FileLocksmith.cpp (find_processes_recursive, pid_to_full_path) and NativeMethods.cpp
// (IsProcessElevated). Read-only: never terminates processes or closes their handles, only our duplicates.
// Like the donor, paths are compared as kernel names (\Device\HarddiskVolumeN\...) obtained by opening
// the query path and asking NtQueryObject, so no QueryDosDevice mapping is needed.
internal sealed record LockingProcess(string Name, int Pid, string User, string ExecutablePath, IReadOnlyList<string> Files);
internal sealed record LockScan(IReadOnlyList<LockingProcess> Processes, bool Elevated, int ProcessesNotOpened, TimeSpan Took);

internal static class FileLocksmith
{
    const int SystemProcessInformation=5,SystemExtendedHandleInformation=64,ObjectNameInformation=1,ObjectTypeInformation=2;
    const int StatusInfoLengthMismatch=unchecked((int)0xC0000004);
    const int DefaultBuffer=64*1024,MaxBuffer=1024*1024*1024,StallTimeoutMs=200;
    const uint ProcessDupHandle=0x40,ProcessQueryInformation=0x400,ProcessVmRead=0x10,ProcessQueryLimited=0x1000;
    const uint DuplicateSameAccess=2,FileTypeDisk=1,TokenQuery=8,OpenExisting=3,FileFlagBackupSemantics=0x02000000;
    static readonly IntPtr Invalid=new(-1);

    internal static LockScan Find(IReadOnlyList<string> paths,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw=System.Diagnostics.Stopwatch.StartNew();
        // Kernel names of the queried files and folders, mapped to their normal paths.
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); // donor: case-sensitive std::map
        var dirs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in paths)
        {
            var k=PathToKernelName(p);
            if(k.Length>0) (Directory.Exists(p)?dirs:files)[k]=p;
        }
        string Match(string kernel)
        {
            if(kernel.Length==0) return null;
            if(files.TryGetValue(kernel,out var f)||dirs.TryGetValue(kernel,out f)) return f;
            foreach(var (dk,dp) in dirs)
            {
                var prefix=dk.EndsWith('\\')?dk:dk+"\\";
                if(kernel.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                {
                    var rest=kernel[dk.Length..];
                    // Donor concatenates as-is; avoid a doubled separator when the caller's folder ends in '\'.
                    return dp.EndsWith('\\')&&rest.StartsWith('\\')?dp+rest[1..]:dp+rest;
                }
            }
            return null;
        }

        var pidFiles=new Dictionary<int,SortedSet<string>>();
        void Add(int pid,string path)
        {
            if(!pidFiles.TryGetValue(pid,out var s)) pidFiles[pid]=s=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            s.Add(path);
        }
        int notOpened=0;
        foreach(var (pid,kernel) in Handles(ct,ref notOpened))
            if(Match(kernel) is string m) Add(pid,m);

        // Loaded modules (DLLs, the image) lock files too.
        var procs=Processes();
        var moduleKernel=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); // cache; donor reopens each module per process
        foreach(var (pid,_) in procs)
        {
            ct.ThrowIfCancellationRequested();
            foreach(var mod in Modules(pid))
            {
                if(!moduleKernel.TryGetValue(mod,out var k)) moduleKernel[mod]=k=PathToKernelName(mod);
                if(Match(k) is string m) Add(pid,m);
            }
        }

        var result=new List<LockingProcess>();
        foreach(var (pid,name) in procs)
            if(pidFiles.TryGetValue(pid,out var set))
                result.Add(new LockingProcess(name,pid,PidToUser(pid),PidToFullPath(pid),set.ToList()));
        return new LockScan(result,IsElevated(),notOpened,sw.Elapsed);
    }

    internal static bool IsElevated()
    {
        if(!OpenProcessToken(GetCurrentProcess(),TokenQuery,out var token)) return false;
        try
        {
            var buf=Marshal.AllocHGlobal(4);
            try { return GetTokenInformation(token,20 /*TokenElevation*/,buf,4,out _)&&Marshal.ReadInt32(buf)!=0; }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(token); }
    }

    // NtQuerySystemInformation with a doubling buffer, as the donor's MemoryLoop. Caller frees.
    static IntPtr QuerySystem(int cls)
    {
        for(long size=DefaultBuffer;size<=MaxBuffer;size*=2)
        {
            var buf=Marshal.AllocHGlobal((IntPtr)size);
            int st=NtQuerySystemInformation(cls,buf,(uint)size,out _);
            if(st==StatusInfoLengthMismatch) { Marshal.FreeHGlobal(buf); continue; }
            if(IsError(st)) { Marshal.FreeHGlobal(buf); return IntPtr.Zero; }
            return buf;
        }
        return IntPtr.Zero;
    }
    static bool IsError(int st)=>((uint)st>>30)==3;

    static string ReadUnicode(IntPtr us)
    {
        int len=(ushort)Marshal.ReadInt16(us);
        var p=Marshal.ReadIntPtr(us,IntPtr.Size);
        return len==0||p==IntPtr.Zero?"":Marshal.PtrToStringUni(p,len/2);
    }

    // Every open "File" handle in the system as (pid, kernel name). A handle whose name query stalls is skipped.
    static List<(int,string)> Handles(CancellationToken ct,ref int notOpened)
    {
        var result=new List<(int,string)>();
        var info=QuerySystem(SystemExtendedHandleInformation);
        if(info==IntPtr.Zero) return result;
        var opened=new Dictionary<long,IntPtr>();
        var typeBuf=Marshal.AllocHGlobal(DefaultBuffer);
        var worker=new NameWorker();
        try
        {
            int ptr=IntPtr.Size,entry=3*ptr+16; // Object, UniqueProcessId, HandleValue, then 4+2+2+4+4
            long count=Marshal.ReadIntPtr(info).ToInt64();
            var self=GetCurrentProcess();
            for(long i=0;i<count;i++)
            {
                ct.ThrowIfCancellationRequested();
                var e=info+(int)(2*ptr+i*entry);
                long pid=Marshal.ReadIntPtr(e,ptr).ToInt64();
                var value=Marshal.ReadIntPtr(e,2*ptr);
                if(!opened.TryGetValue(pid,out var proc))
                {
                    // Donor caches failures implicitly by retrying; we remember them so each pid is counted once.
                    proc=OpenProcess(ProcessDupHandle,false,(uint)pid);
                    opened[pid]=proc;
                    if(proc==IntPtr.Zero) notOpened++;
                }
                if(proc==IntPtr.Zero) continue;
                if(!DuplicateHandle(proc,value,self,out var copy,0,false,DuplicateSameAccess)) continue;
                if(IsError(NtQueryObject(copy,ObjectTypeInformation,typeBuf,DefaultBuffer,out _))||ReadUnicode(typeBuf)!="File")
                {
                    CloseHandle(copy);
                    continue;
                }
                if(worker.Query(copy) is string name) result.Add(((int)pid,name)); // Query owns copy
            }
        }
        finally
        {
            worker.Dispose();
            Marshal.FreeHGlobal(typeBuf);
            Marshal.FreeHGlobal(info);
            foreach(var h in opened.Values) if(h!=IntPtr.Zero) CloseHandle(h);
        }
        return result;
    }

    // GetFileType and NtQueryObject(ObjectNameInformation) can block forever on some handles (synchronous
    // pipes with pending I/O) and take no timeout. They run on one reused helper thread; when it stalls
    // past the timeout it is abandoned (never terminated) and takes ownership of the handle, its buffer
    // and events, and a fresh helper is started for the next handle. The donor does the same.
    sealed class NameWorker:IDisposable
    {
        const int Busy=0,Done=1,Abandoned=2;
        sealed class Block
        {
            public readonly AutoResetEvent Request=new(false),Finished=new(false);
            public readonly IntPtr Buffer=Marshal.AllocHGlobal(DefaultBuffer);
            public IntPtr Handle; public bool IsDisk; public int Status; public int State;
        }
        Block block;

        static void Run(object o)
        {
            var b=(Block)o;
            for(;;)
            {
                b.Request.WaitOne();
                if(b.Handle==IntPtr.Zero) break;
                b.IsDisk=GetFileType(b.Handle)==FileTypeDisk;
                if(b.IsDisk) b.Status=NtQueryObject(b.Handle,ObjectNameInformation,b.Buffer,DefaultBuffer,out _);
                if(Interlocked.CompareExchange(ref b.State,Done,Busy)==Abandoned)
                {
                    CloseHandle(b.Handle); // the caller gave up and handed the handle to us
                    break;
                }
                b.Finished.Set();
            }
            // The caller has dropped the block by now (exit request or abandonment); we own cleanup.
            b.Request.Dispose(); b.Finished.Dispose(); Marshal.FreeHGlobal(b.Buffer);
        }

        // Takes ownership of handle. Returns the kernel name ("" if not a disk file or unqueryable), or null if it stalled.
        public string Query(IntPtr handle)
        {
            if(block==null)
            {
                block=new Block();
                new Thread(Run,256*1024){IsBackground=true,Name="FileLocksmith name query"}.Start(block);
            }
            var b=block;
            b.Handle=handle; b.IsDisk=false;
            Interlocked.Exchange(ref b.State,Busy);
            b.Request.Set();
            if(!b.Finished.WaitOne(StallTimeoutMs))
            {
                if(Interlocked.CompareExchange(ref b.State,Abandoned,Busy)==Busy) { block=null; return null; }
                b.Finished.WaitOne(); // completed just as we timed out; consume its signal
            }
            CloseHandle(handle);
            return b.IsDisk&&!IsError(b.Status)?ReadUnicode(b.Buffer):"";
        }

        public void Dispose()
        {
            if(block==null) return;
            block.Handle=IntPtr.Zero;
            block.Request.Set();
            block=null;
        }
    }

    // Kernel name of a path we can open ourselves (no hang guard, as donor: our own fresh handle cannot stall).
    static string PathToKernelName(string path)
    {
        var h=CreateFileW(path,0,7 /*share read|write|delete*/,IntPtr.Zero,OpenExisting,FileFlagBackupSemantics,IntPtr.Zero);
        if(h==Invalid||h==IntPtr.Zero) return "";
        var buf=Marshal.AllocHGlobal(DefaultBuffer);
        try { return GetFileType(h)==FileTypeDisk&&!IsError(NtQueryObject(h,ObjectNameInformation,buf,DefaultBuffer,out _))?ReadUnicode(buf):""; }
        finally { Marshal.FreeHGlobal(buf); CloseHandle(h); }
    }

    // (pid, image name) from SystemProcessInformation. Like the donor, the first entry (Idle) is skipped.
    static List<(int,string)> Processes()
    {
        var result=new List<(int,string)>();
        var info=QuerySystem(SystemProcessInformation);
        if(info==IntPtr.Zero) return result;
        try
        {
            const int imageName=0x38;
            int pidOffset=imageName+3*IntPtr.Size; // UNICODE_STRING, BasePriority (+pad on x64)
            var p=info;
            for(int next;(next=Marshal.ReadInt32(p))!=0;)
            {
                p+=next;
                result.Add(((int)Marshal.ReadIntPtr(p,pidOffset).ToInt64(),ReadUnicode(p+imageName)));
            }
        }
        finally { Marshal.FreeHGlobal(info); }
        return result;
    }

    static List<string> Modules(int pid)
    {
        var result=new List<string>();
        var proc=OpenProcess(ProcessQueryInformation|ProcessVmRead,false,(uint)pid);
        if(proc==IntPtr.Zero) return result;
        try
        {
            var mods=new IntPtr[512];
            for(;;)
            {
                if(!K32EnumProcessModules(proc,mods,(uint)(mods.Length*IntPtr.Size),out uint needed)) return new List<string>();
                int n=(int)(needed/IntPtr.Size);
                if(n>mods.Length) { mods=new IntPtr[n]; continue; }
                var sb=new StringBuilder(32768);
                for(int i=0;i<n;i++)
                {
                    sb.Clear();
                    if(K32GetModuleFileNameExW(proc,mods[i],sb,(uint)sb.Capacity)>0) result.Add(sb.ToString());
                }
                return result;
            }
        }
        finally { CloseHandle(proc); }
    }

    static string PidToFullPath(int pid)
    {
        var proc=OpenProcess(ProcessQueryInformation|ProcessVmRead,false,(uint)pid);
        if(proc==IntPtr.Zero) return "";
        try
        {
            var sb=new StringBuilder(65536);
            return K32GetModuleFileNameExW(proc,IntPtr.Zero,sb,(uint)sb.Capacity)>0?sb.ToString():"";
        }
        finally { CloseHandle(proc); }
    }

    // Account name (no domain) running pid, as donor's pid_to_user. Donor leaks handles on early exits; we don't.
    static string PidToUser(int pid)
    {
        var proc=OpenProcess(ProcessQueryLimited,false,(uint)pid);
        if(proc==IntPtr.Zero) return "";
        IntPtr token=IntPtr.Zero,buf=IntPtr.Zero;
        try
        {
            if(!OpenProcessToken(proc,TokenQuery,out token)) return "";
            GetTokenInformation(token,1 /*TokenUser*/,IntPtr.Zero,0,out uint size);
            if(size==0) return "";
            buf=Marshal.AllocHGlobal((int)size);
            if(!GetTokenInformation(token,1,buf,size,out size)) return "";
            var sid=Marshal.ReadIntPtr(buf); // TOKEN_USER.User.Sid
            uint u=256,d=256;
            var user=new StringBuilder(256); var domain=new StringBuilder(256);
            return LookupAccountSidW(null,sid,user,ref u,domain,ref d,out _)?user.ToString():"";
        }
        finally
        {
            if(buf!=IntPtr.Zero) Marshal.FreeHGlobal(buf);
            if(token!=IntPtr.Zero) CloseHandle(token);
            CloseHandle(proc);
        }
    }

#if DEBUG
    // Cancellation: a token cancelled before the call throws OperationCanceledException (never returns empty).
    internal static void Checks(string scratch,Action<bool,string> check)
    {
        Directory.CreateDirectory(scratch);
        var file=Path.Combine(scratch,"locked.txt");
        int me=Environment.ProcessId;
        bool Holds(LockScan s,string path)=>s.Processes.Any(p=>p.Pid==me&&p.Files.Any(f=>string.Equals(Path.GetFullPath(f),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)));
        using(var fs=new FileStream(file,FileMode.Create,FileAccess.ReadWrite,FileShare.ReadWrite))
        {
            fs.WriteByte(1); fs.Flush();
            var s=Find(new[]{file},CancellationToken.None);
            check(Holds(s,file),"File Locksmith reports this process holding a file it has open, as the donor's handle scan does");
            var me1=s.Processes.FirstOrDefault(p=>p.Pid==me);
            check(me1!=null&&me1.ExecutablePath.Length>0&&me1.Name.Length>0,"File Locksmith reports the locking process's image name and full executable path");
            check(Holds(Find(new[]{scratch},CancellationToken.None),file),"File Locksmith folder query finds open files anywhere under the folder");
        }
        check(!Holds(Find(new[]{file},CancellationToken.None),file),"File Locksmith no longer reports this process once the file is closed");
        using var cts=new CancellationTokenSource(); cts.Cancel();
        var t=System.Diagnostics.Stopwatch.StartNew(); bool threw=false;
        try { Find(new[]{file},cts.Token); } catch(OperationCanceledException) { threw=true; }
        check(threw&&t.ElapsedMilliseconds<1000,"File Locksmith cancelled before start throws OperationCanceledException promptly");
        try { File.Delete(file); } catch(IOException) { }
    }
#endif

    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls,IntPtr info,uint len,out uint ret);
    [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr h,int cls,IntPtr info,int len,out int ret);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr srcProc,IntPtr src,IntPtr dstProc,out IntPtr dst,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll")] static extern uint GetFileType(IntPtr h);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CreateFileW(string name,uint access,uint share,IntPtr sec,uint disp,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool K32EnumProcessModules(IntPtr proc,[Out] IntPtr[] mods,uint cb,out uint needed);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint K32GetModuleFileNameExW(IntPtr proc,IntPtr mod,StringBuilder name,uint size);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr proc,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int cls,IntPtr info,uint len,out uint ret);
    [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool LookupAccountSidW(string system,IntPtr sid,StringBuilder name,ref uint nameLen,StringBuilder domain,ref uint domainLen,out int use);
}
