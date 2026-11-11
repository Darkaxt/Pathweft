using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

internal sealed record GpuReading(string Id,string Name,double? Usage,long? Memory,long Capacity)
{public override string ToString()=>Name;}
internal sealed record DiskReading(string Id,double? Read,double? Write,double? Busy,double? LatencyMs=null);
internal sealed record RamReading(long Used,long Total)
{internal double Usage=>100d*Used/Total;}
internal sealed record PerformanceSample(DateTimeOffset Time,double? Cpu,GpuReading[] Gpus,DiskReading[] Disks,string Issue,RamReading Ram=null);
internal sealed record PerformanceProcessSnapshot(DateTimeOffset Time,ProcessMetric[] Apps,ProcessMetric[] GpuApps);

// One process-wide sampler. The recent hour includes automatic app snapshots;
// automatic bounded disk persistence extends that history. File ETW remains separately
// scoped and is never persisted. A time gap remains a displayed history gap.
internal sealed class PerformanceMonitor : IDisposable
{
    internal const int Capacity=3600;
    internal const int ProcessCapacity=360;
    internal static readonly TimeSpan ProcessCadence=TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Cadence=TimeSpan.FromSeconds(1);
    static PerformanceMonitor current;
    internal static PerformanceMonitor Current=>current??=new();
    internal static void Stop(){current?.Dispose();current=null;}
    readonly object gate=new();
    readonly Queue<PerformanceSample> history=new(Capacity);
    readonly Queue<PerformanceProcessSnapshot> processHistory=new(ProcessCapacity);
    readonly CancellationTokenSource stopped=new();
    readonly Task sampling;
    internal event Action Changed;
    int detailKind;
    internal int DetailKind {get=>Volatile.Read(ref detailKind);set=>Volatile.Write(ref detailKind,value);}
    internal ProcessMetric[] Details {get;private set;}=Array.Empty<ProcessMetric>();
    readonly ProcessMetrics processMetrics=new();
    readonly PerformanceHistory persisted=new();
    readonly System.Collections.Concurrent.ConcurrentQueue<(PerformanceFact[] Facts,TaskCompletionSource Saved)> pendingChanges=new();
    internal Task SaveChanges(PerformanceFact[] facts)
    {
        if(facts.Length==0)return Task.CompletedTask;
        var saved=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);pendingChanges.Enqueue((facts,saved));return saved.Task;
    }
    internal string HistoryIssue=>persisted.Issue;
    internal PerformanceMonitor(){sampling=Task.Run(Run);}
    internal PerformanceSample Latest {get{lock(gate)return history.LastOrDefault();}}
    internal PerformanceSample[] History(){lock(gate)return history.ToArray();}
    internal PerformanceProcessSnapshot[] ProcessHistory(){lock(gate)return processHistory.ToArray();}
    internal void RecordProcesses(DateTimeOffset time,ProcessMetric[] apps,ProcessMetric[] gpus)
    {
        var selected=PerformanceHistory.SelectApps(apps,gpus);
        var identities=selected.Select(x=>(x.Pid,x.Started)).ToHashSet();
        var snapshot=new PerformanceProcessSnapshot(time,selected,(gpus??[]).Where(x=>x.Adapter!=null&&identities.Contains((x.Pid,x.Started))).ToArray());
        lock(gate)
        {
            while(processHistory.Count>0&&(processHistory.Count>=ProcessCapacity||time-processHistory.Peek().Time>=TimeSpan.FromHours(1)))processHistory.Dequeue();
            processHistory.Enqueue(snapshot);
        }
    }
    internal void Record(PerformanceSample sample)
    {
        lock(gate)
        {
            while(history.Count>0&&(history.Count>=Capacity||sample.Time-history.Peek().Time>=TimeSpan.FromHours(1)))history.Dequeue();
            history.Enqueue(sample);
        }
        Changed?.Invoke();
    }
    async Task Run()
    {
        using var source=new WindowsPerformanceCounters();
        using var clock=new PeriodicTimer(Cadence);
        DateTimeOffset lastApps=default;
        while(!stopped.IsCancellationRequested)
        {
            // One failed native or history read skips this sample; it must not end
            // collection for the rest of the session or rethrow at shutdown.
            try
            {
                var sample=source.Read();
                // The UI may switch cards during a native read. One selection
                // owns this entire tick, including the array that is published.
                int kind=DetailKind;
                bool recordApps=sample.Time-lastApps>=ProcessCadence;
                var apps=kind is 1 or 3||recordApps?processMetrics.Read():null;
                var gpuApps=kind==2||recordApps?source.GpuProcesses():null;
                Details=kind is 1 or 3?apps:kind==2?gpuApps:Array.Empty<ProcessMetric>();
                if(recordApps)RecordProcesses(sample.Time,apps,gpuApps);
                var changes=pendingChanges.ToArray();
                if(persisted.Record(sample,recordApps?apps:null,recordApps?gpuApps:null,changes.SelectMany(x=>x.Facts).ToArray()))
                    foreach(var change in changes){pendingChanges.TryDequeue(out _);change.Saved.TrySetResult();}
                else if(persisted.Issue!=null)foreach(var change in changes){pendingChanges.TryDequeue(out _);change.Saved.TrySetException(new System.IO.IOException(persisted.Issue));}
                if(recordApps)lastApps=sample.Time;
                Record(sample);
            }
            catch(Exception error)when(error is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException){}
            try{if(!await clock.WaitForNextTickAsync(stopped.Token))break;}
            catch(OperationCanceledException)when(stopped.IsCancellationRequested){break;}
        }
    }
    public void Dispose(){stopped.Cancel();sampling.GetAwaiter().GetResult();while(pendingChanges.TryDequeue(out var pending))pending.Saved.TrySetException(new OperationCanceledException("Collector stopped before saving changes"));persisted.Dispose();stopped.Dispose();}
}

internal sealed class WindowsPerformanceCounters : IDisposable
{
    readonly IntPtr query;
    readonly IntPtr engines,memory,reads,writes,idle,latency;
    Dictionary<string,double> lastEngines=new();
    readonly Dictionary<string,(string Name,long Capacity)> adapters=GpuAdapters.Read();
    ulong previousIdle,previousKernel,previousUser;
    DateTimeOffset previousTime;
    static readonly Regex gpuId=new(@"luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)_phys_(\d+)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
    internal WindowsPerformanceCounters()
    {
        if(PdhOpenQuery(null,UIntPtr.Zero,out query)!=0)return;
        engines=Add(@"\GPU Engine(*)\Utilization Percentage");
        memory=Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
        reads=Add(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        writes=Add(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        idle=Add(@"\PhysicalDisk(*)\% Idle Time");
        // SysOptimizer ResourceMonitor: Avg. Disk sec/Transfer is seconds,
        // unlike the millisecond label used by its disk detail chart.
        latency=Add(@"\PhysicalDisk(*)\Avg. Disk sec/Transfer");
    }
    IntPtr Add(string path)=>PdhAddEnglishCounter(query,path,UIntPtr.Zero,out var counter)==0?counter:IntPtr.Zero;
    internal PerformanceSample Read()
    {
        var now=DateTimeOffset.UtcNow;
        bool continuous=previousTime!=default&&now-previousTime<TimeSpan.FromSeconds(5);
        double? cpu=null;
        if(GetSystemTimes(out ulong i,out ulong k,out ulong u))
        {
            if(continuous&&k>=previousKernel&&u>=previousUser&&i>=previousIdle)
            {
                var total=(k-previousKernel)+(u-previousUser);
                if(total>0)cpu=Math.Clamp(100d*(1-(double)(i-previousIdle)/total),0,100);
            }
            previousIdle=i;previousKernel=k;previousUser=u;
        }
        previousTime=now;
        uint status=query==IntPtr.Zero?0xC0000BBD:PdhCollectQueryData(query);
        var gpuValues=lastEngines=Values(engines);var mem=Values(memory);
        // Counter instances may outlive a removed/software adapter. DXGI is the
        // actual hardware inventory; never promote a stale LUID to a GPU name.
        var names=gpuValues.Keys.Concat(mem.Keys).Select(Id).Where(x=>x!=null&&(adapters.Count==0||adapters.ContainsKey(x))).Distinct().ToArray();
        var gpus=names.Select(id=>
        {
            var busy=gpuValues.Where(x=>Id(x.Key)==id).GroupBy(x=>x.Key[(x.Key.IndexOf("_eng_",StringComparison.Ordinal)+5)..])
                .Select(group=>group.Sum(x=>x.Value)).ToArray();
            var identity=adapters.GetValueOrDefault(id,("GPU adapter unavailable",0));
            return new GpuReading(id,identity.Name,continuous&&busy.Length>0?Math.Clamp(busy.Max(),0,100):null,
                mem.Where(x=>Id(x.Key)==id).Select(x=>(long?)x.Value).FirstOrDefault(),identity.Capacity);
        }).ToArray();
        var r=Values(reads);var w=Values(writes);var quiet=Values(idle);var delays=Values(latency);
        var disks=r.Keys.Concat(w.Keys).Distinct().Where(x=>x!="_Total").Select(id=>new DiskReading(id,
            continuous&&r.TryGetValue(id,out var rv)?rv:null,continuous&&w.TryGetValue(id,out var wv)?wv:null,
            continuous&&quiet.TryGetValue(id,out var qv)?Math.Clamp(100-qv,0,100):null,
            continuous&&delays.TryGetValue(id,out var seconds)?seconds*1000:null)).ToArray();
        var physical=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        RamReading ram=GlobalMemoryStatusEx(ref physical)&&physical.Total>0&&physical.Available<=physical.Total?
            new((long)(physical.Total-physical.Available),(long)physical.Total):null;
        return new(now,cpu,gpus,disks,status==0?null:$"Windows counters unavailable (0x{status:X8})",ram);
    }
    static string Id(string instance){var match=gpuId.Match(instance);return match.Success?match.Value.ToLowerInvariant():null;}
    internal ProcessMetric[] GpuProcesses()=>lastEngines.Where(x=>x.Key.StartsWith("pid_",StringComparison.Ordinal))
        .GroupBy(x=>(Pid:x.Key.Split('_')[1],Adapter:Id(x.Key))).Select(group=>
        {
            int.TryParse(group.Key.Pid,out int pid);string name="Unavailable";long started=0;
            try{using var process=Process.GetProcessById(pid);name=process.ProcessName;started=process.StartTime.ToUniversalTime().Ticks;}
            catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
            return new ProcessMetric(pid,started,name,Math.Clamp(group.GroupBy(x=>Id(x.Key)+x.Key[(x.Key.IndexOf("_eng_",StringComparison.Ordinal)+5)..]).Max(g=>g.Sum(x=>x.Value)),0,100),0,group.Key.Adapter);
        }).OrderByDescending(x=>x.Usage).ToArray();
    internal Dictionary<string,double> Values(IntPtr counter)
    {
        var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        if(counter==IntPtr.Zero)return result;
        uint bytes=0,count=0;uint status=PdhGetFormattedCounterArray(counter,0x200|0x8000,ref bytes,ref count,IntPtr.Zero);
        if(status!=0x800007D2||bytes==0||bytes>8*1024*1024)return result;
        var buffer=Marshal.AllocHGlobal((int)bytes);
        try
        {
            if(PdhGetFormattedCounterArray(counter,0x200|0x8000,ref bytes,ref count,buffer)!=0)return result;
            int stride=Marshal.SizeOf<CounterItem>();
            for(int n=0;n<count;n++)
            {
                var item=Marshal.PtrToStructure<CounterItem>(buffer+n*stride);
                if(item.Value.Status<=1&&double.IsFinite(item.Value.Number))result[Marshal.PtrToStringUni(item.Name)]=Math.Max(0,item.Value.Number);
            }
        }
        finally{Marshal.FreeHGlobal(buffer);}
        return result;
    }
    internal static DiskReading ForPath(PerformanceSample sample,string path)
    {
        return ForDisk(sample,PhysicalDisk(path));
    }
    internal static DiskReading ForDisk(PerformanceSample sample,uint? number)=>number is {} disk?
        sample?.Disks.FirstOrDefault(d=>uint.TryParse(d.Id.Split(' ')[0],out uint id)&&id==disk):null;
    internal static uint? PhysicalDisk(string path)=>MapPath(path).Disk;
    internal static (string Folder,string Volume,uint? Disk) MapPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathFullyQualified(path)||path.StartsWith(@"\\"))return default;
        string root=System.IO.Path.GetPathRoot(path);
        if(CloudVolumes.GoogleRoots().Contains(root)||GetDriveType(root) is not (2 or 3))return default;
        using var directory=CreateFile(path,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        var final=new StringBuilder(32768);
        if(directory.IsInvalid||GetFinalPathNameByHandle(directory,final,(uint)final.Capacity,0) is not (>0 and <32768))return default;
        string canonical=final.ToString();if(canonical.StartsWith(@"\\?\"))canonical=canonical[4..];
        if(canonical.StartsWith("UNC\\",StringComparison.OrdinalIgnoreCase))return default;
        var mount=new StringBuilder(32768);var name=new StringBuilder(128);
        if(!GetVolumePathName(canonical,mount,(uint)mount.Capacity)||GetDriveType(mount.ToString()) is not (2 or 3))return default;
        var location=(Folder:canonical,Volume:mount.ToString(),Disk:(uint?)null);
        if(!GetVolumeNameForVolumeMountPoint(mount.ToString(),name,(uint)name.Capacity))return location;
        using var volume=CreateFile(name.ToString().TrimEnd('\\'),0,7,IntPtr.Zero,3,0,IntPtr.Zero);
        if(volume.IsInvalid)return location;
        // A single extent is unambiguous. Spanned/virtual volumes are explicitly
        // unavailable rather than borrowing one backing device's throughput.
        var extent=new byte[32];
        if(DeviceIoControl(volume,0x00560000,IntPtr.Zero,0,extent,(uint)extent.Length,out uint bytes,IntPtr.Zero)&&bytes>=32&&
            BitConverter.ToUInt32(extent,0)==1)location.Disk=BitConverter.ToUInt32(extent,8);
        return location;
    }
    [StructLayout(LayoutKind.Explicit,Size=16)]struct CounterValue{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Number;}
    [StructLayout(LayoutKind.Sequential)]struct CounterItem{public IntPtr Name;public CounterValue Value;}
    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus
    {
        public uint Length,Load;
        public ulong Total,Available,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtended;
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhOpenQueryW")]static extern uint PdhOpenQuery(string source,UIntPtr data,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")]static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhGetFormattedCounterArrayW")]static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,ref uint count,IntPtr buffer);
    [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetDriveTypeW")]static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetVolumePathNameW")]static extern bool GetVolumePathName(string path,StringBuilder mount,uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetFinalPathNameByHandleW")]static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder name,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetVolumeNameForVolumeMountPointW")]static extern bool GetVolumeNameForVolumeMountPoint(string mount,StringBuilder name,uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFileW",SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,uint inputLength,byte[] output,uint outputLength,out uint returned,IntPtr overlapped);
    public void Dispose(){if(query!=IntPtr.Zero)PdhCloseQuery(query);}
}

internal sealed record ProcessMetric(int Pid,long Started,string Process,double Usage,long Memory,string Adapter=null)
{public string Percent=>$"{Usage:0.0}%";public string MemoryText=>MainWindow.Fmt(Memory);}
internal sealed class ProcessMetrics
{
    Dictionary<(int,long),TimeSpan> previous=new();long timestamp;
    internal ProcessMetric[] Read()
    {
        long now=Stopwatch.GetTimestamp();double seconds=timestamp==0?0:Stopwatch.GetElapsedTime(timestamp,now).TotalSeconds;timestamp=now;
        var next=new Dictionary<(int,long),TimeSpan>();var result=new List<ProcessMetric>();
        foreach(var process in Process.GetProcesses())
        {
            using(process)try
            {
                var key=(process.Id,process.StartTime.ToUniversalTime().Ticks);var total=process.TotalProcessorTime;next[key]=total;
                if(seconds>0&&previous.TryGetValue(key,out var old))result.Add(new(key.Id,key.Ticks,process.ProcessName,
                    Math.Clamp((total-old).TotalSeconds/seconds/Environment.ProcessorCount*100,0,100),process.WorkingSet64));
            }
            catch(Exception ex)when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException){}
        }
        previous=next;return result.OrderByDescending(x=>x.Usage).ToArray();
    }
}

internal static class GpuAdapters
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Description
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Name;
        public uint Vendor,Device,SubSystem,Revision;
        public UIntPtr Video,System,Shared;
        public uint Low;public int High;public uint Flags;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Enumerate(IntPtr self,uint index,out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Describe(IntPtr self,out Description description);
    [DllImport("dxgi.dll")]static extern int CreateDXGIFactory1(ref Guid id,out IntPtr factory);
    internal static Dictionary<string,(string Name,long Capacity)> Read()
    {
        var result=new Dictionary<string,(string,long)>();
        var iid=new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if(CreateDXGIFactory1(ref iid,out var factory)<0)return result;
        try
        {
            var enumerate=Marshal.GetDelegateForFunctionPointer<Enumerate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory),12*IntPtr.Size));
            for(uint i=0;enumerate(factory,i,out var adapter)==0;i++)
            {
                try
                {
                    var describe=Marshal.GetDelegateForFunctionPointer<Describe>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter),10*IntPtr.Size));
                    if(describe(adapter,out var desc)==0&&(desc.Flags&2)==0)
                        result[$"luid_0x{desc.High:x8}_0x{desc.Low:x8}_phys_0"]=(desc.Name,(long)desc.Video.ToUInt64());
                }
                finally{Marshal.Release(adapter);}
            }
        }
        finally{Marshal.Release(factory);}
        return result;
    }
}
