#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FileExplorer;
internal static class PerformanceBudgetVerification
{
    internal static async Task<int> Run(string root)
    {
        root=Path.GetFullPath(root);
        if(!root.StartsWith(@"D:\Temp\pathweft-s18\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned resource fixture required");
        Directory.CreateDirectory(root);
        using var process=Process.GetCurrentProcess();
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();process.Refresh();
        long initialPrivate=process.PrivateMemorySize64;
        using var monitor=new PerformanceMonitor();
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>ready.TrySetResult();monitor.Changed+=Updated;
            try{await ready.Task;}finally{monitor.Changed-=Updated;}
        }
        for(int i=0;i<4;i++)await Next();
        process.Refresh();var cpu=process.TotalProcessorTime;var watch=Stopwatch.StartNew();
        for(int i=0;i<30;i++)await Next();
        process.Refresh();double cpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100;
        var real=monitor.Latest;
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();process.Refresh();
        long steadyPrivate=process.PrivateMemorySize64-initialPrivate,heap=GC.GetTotalMemory(true);
        var time=DateTimeOffset.UtcNow.AddHours(2);
        // Independent copies model actual native-read payload allocation, not
        // thousands of references to the same sample's arrays.
        for(int i=0;i<3600;i++)monitor.Record(new(time.AddSeconds(i),real.Cpu,
            real.Gpus.Select(x=>x with{}).ToArray(),real.Disks.Select(x=>x with{}).ToArray(),real.Issue));
        long retained=GC.GetTotalMemory(true)-heap;
        IoReport permission;
        var client=new PerformanceIoClient();
        await using(client)
        {
            await client.Enable(false);await client.SetScope(root,true);permission=client.Latest;
        }
        // Closing while launch/connection is pending must still own and retire
        // the child; it must not wait forever for a pipe whose server vanished.
        var pending=new PerformanceIoClient();var connection=pending.Enable(false);
        await pending.DisposeAsync();await connection;
        bool pass=cpuPercent<=0.5&&steadyPrivate<=32*1024*1024&&retained<=8*1024*1024&&!pending.Connected;
        File.WriteAllText(Path.Combine(root,"budget.json"),JsonSerializer.Serialize(new{pass,pid=Environment.ProcessId,seconds=watch.Elapsed.TotalSeconds,
            cpuPercent,steadyPrivate,retained,samples=monitor.History().Length,gpus=real.Gpus,disks=real.Disks,permission,nativeDiagnostics=client.NativeDiagnostics},new JsonSerializerOptions{WriteIndented=true}));
        return pass?0:1;
    }
}
#endif
