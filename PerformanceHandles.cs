using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// MONITOR-23: parent-ended identity, on-demand handle types and explicit End process.
// Every action is bound to PID plus creation time; a reused PID is never adopted.
internal static class ProcessParents
{
    // One Toolhelp snapshot: PID -> parent PID. Null when Windows refuses the snapshot.
    internal static Dictionary<int,int> Read()
    {
        var result=new Dictionary<int,int>();
        var snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==new IntPtr(-1))return null;
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
            for(bool more=Process32First(snapshot,ref entry);more;more=Process32Next(snapshot,ref entry))result[(int)entry.Pid]=(int)entry.Parent;
        }
        finally{CloseHandle(snapshot);}
        return result;
    }
    // True when the recorded parent PID is gone, or now belongs to a process created after the child
    // (PID reuse: the real parent ended). Null when unknown; parentless system processes are false.
    internal static bool? Ended(int pid,long started,Dictionary<int,int> parents,Func<int,long?> startOf)
    {
        if(parents==null||!parents.TryGetValue(pid,out int parent))return null;
        if(parent==0||parent==pid)return false;
        if(!parents.ContainsKey(parent))return true;
        return startOf(parent) is {} start?start>started:null;
    }
    internal static long? StartOf(int pid)
    {
        try{using var process=Process.GetProcessById(pid);return process.StartTime.ToUniversalTime().Ticks;}
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){return null;}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessEntry
    {
        public uint Size,Usage,Pid;public UIntPtr Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",EntryPoint="Process32FirstW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",EntryPoint="Process32NextW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
}

internal sealed record PerformanceHandleTypes(int Pid,long Started,(string Type,int Count)[] Types,int Total,string Issue)
{
    internal string Dominant=>Types.Length==0?null:Types[0].Type;
    public override string ToString()=>Issue??(Types.Length==0?"No handles attributed to this process":$"{Total:N0} handles · "+string.Join(" · ",Types.Take(8).Select(x=>$"{x.Type} {x.Count:N0}"))+(Types.Length>8?$" · {Types.Length-8} more types":""));
}
internal static class PerformanceProcessControl
{
    const int Mismatch=unchecked((int)0xC0000004);
    const int HandleTableLimit=256<<20;
    // On demand only. One bounded system handle-table snapshot filtered to this PID, with type
    // names from the kernel type table. No handle is duplicated and no object name is queried,
    // so pipes and other blocking objects cannot hang the read.
    internal static PerformanceHandleTypes HandleTypes(int pid,long started,CancellationToken cancel=default)
    {
        PerformanceHandleTypes Issue(string text)=>new(pid,started,[],0,text);
        if(!Matches(pid,started))return Issue("Process exited or its PID now belongs to another process");
        var names=TypeNames();IntPtr buffer=IntPtr.Zero;int size=8<<20;
        try
        {
            while(true)
            {
                cancel.ThrowIfCancellationRequested();buffer=Marshal.AllocHGlobal(size);
                int status=NtQuerySystemInformation(0x40,buffer,size,out int needed);
                if(status==0)break;
                Marshal.FreeHGlobal(buffer);buffer=IntPtr.Zero;
                if(status!=Mismatch)return Issue($"Windows handle table unavailable (0x{status:X8})");
                size=(int)Math.Min(int.MaxValue,Math.Max(size*2L,needed+(1L<<20)));
                if(size>HandleTableLimit)return Issue("The system handle table exceeds the 256 MiB on-demand bound; types were not read");
            }
            int pointer=IntPtr.Size,stride=IntPtr.Size==8?40:28,header=2*pointer;
            long count=Math.Min((long)Marshal.ReadIntPtr(buffer),(size-header)/stride);
            var counts=new Dictionary<int,int>();
            for(long i=0;i<count;i++)
            {
                if((i&0xFFFF)==0)cancel.ThrowIfCancellationRequested();
                var entry=buffer+(nint)(header+i*stride);
                if((long)Marshal.ReadIntPtr(entry,pointer)!=pid)continue;
                int type=(ushort)Marshal.ReadInt16(entry,3*pointer+6);
                counts[type]=counts.GetValueOrDefault(type)+1;
            }
            // The PID could have been reused during the snapshot; never attribute that to this identity.
            if(!Matches(pid,started))return Issue("Process exited while its handles were being read");
            var types=counts.GroupBy(x=>names.GetValueOrDefault(x.Key,"Type "+x.Key)).Select(g=>(Type:g.Key,Count:g.Sum(x=>x.Value)))
                .OrderByDescending(x=>x.Count).ThenBy(x=>x.Type,StringComparer.Ordinal).ToArray();
            return new(pid,started,types,types.Sum(x=>x.Count),null);
        }
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
    }
    static Dictionary<int,string> TypeNames()
    {
        var names=new Dictionary<int,string>();IntPtr buffer=IntPtr.Zero;int size=64<<10;
        try
        {
            while(true)
            {
                buffer=Marshal.AllocHGlobal(size);
                int status=NtQueryObject(IntPtr.Zero,3,buffer,size,out _);
                if(status==0)break;
                Marshal.FreeHGlobal(buffer);buffer=IntPtr.Zero;
                if(status!=Mismatch||size>=4<<20)return names;
                size*=2;
            }
            int pointer=IntPtr.Size,structure=2*pointer+0x58,number=Marshal.ReadInt32(buffer),offset=pointer;
            for(int i=0;i<number&&offset+structure<=size;i++)
            {
                int length=(ushort)Marshal.ReadInt16(buffer,offset),maximum=(ushort)Marshal.ReadInt16(buffer,offset+2);
                var text=Marshal.ReadIntPtr(buffer,offset+pointer);int index=Marshal.ReadByte(buffer,offset+2*pointer+0x4A);
                if(text!=IntPtr.Zero)names[index==0?i+2:index]=Marshal.PtrToStringUni(text,length/2);
                offset+=structure+maximum;offset=(offset+pointer-1)&~(pointer-1);
            }
            return names;
        }
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
    }
    // Explicit user action only. The PID/start identity is rechecked through the same handle that
    // terminates, so the PID cannot be reused between the check and the action. Null means ended.
    internal static string End(int pid,long started)
    {
        if(pid==Environment.ProcessId)return "Pathweft does not end its own process.";
        using var process=OpenProcess(0x0001|0x1000,false,pid);
        if(process.IsInvalid)return $"Process {pid} cannot be opened (error {Marshal.GetLastWin32Error()}); it may have exited or need administrator access. Nothing was ended.";
        if(Creation(process)!=started)return $"PID {pid} now belongs to a different process; nothing was ended.";
        return TerminateProcess(process,1)?null:$"Windows refused to end process {pid} (error {Marshal.GetLastWin32Error()}).";
    }
    internal static bool Matches(int pid,long started)
    {
        using var process=OpenProcess(0x1000,false,pid);
        return !process.IsInvalid&&Creation(process)==started;
    }
    static long? Creation(SafeProcessHandle process)=>GetProcessTimes(process,out long created,out _,out _,out _)?DateTime.FromFileTimeUtc(created).Ticks:null;
    [DllImport("ntdll.dll")]static extern int NtQuerySystemInformation(int type,IntPtr buffer,int length,out int needed);
    [DllImport("ntdll.dll")]static extern int NtQueryObject(IntPtr handle,int type,IntPtr buffer,int length,out int needed);
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(SafeProcessHandle process,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateProcess(SafeProcessHandle process,uint code);
}
