#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace FileExplorer;
internal static class PerformanceBridgeVerification
{
    internal static async Task<int> Run(string root)
    {
        root=Path.GetFullPath(root);
        if(!System.Text.RegularExpressions.Regex.IsMatch(root,@"^D:\\Temp\\pathweft-s18\\bridge-[a-f0-9]{32}$"))throw new ArgumentException("Exact owned bridge fixture required");
        using var self=Process.GetCurrentProcess();
        using var identity=WindowsIdentity.GetCurrent();
        if(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("Boundary client must be unelevated");
        string handoff="Pathweft-Bridge-"+Guid.NewGuid().ToString("N");
        using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,handoff);
        using var cancel=new EventWaitHandle(false,EventResetMode.ManualReset,handoff+"-cancel");
        var client=new PerformanceIoClient();
        Process worker=null;
        client.LaunchForVerification=async launch=>
        {
            File.WriteAllText(Path.Combine(root,"request.json"),JsonSerializer.Serialize(new{pid=self.Id,started=self.StartTime.ToUniversalTime().Ticks,executable=launch.FileName,args=launch.ArgumentList.ToArray(),handoff}));
            int signaled=await Task.Run(()=>WaitHandle.WaitAny(new WaitHandle[]{ready,cancel}));
            if(signaled==1)throw new IOException("Bridge qualification cancelled before elevated launch");
            int pid=int.Parse(File.ReadAllText(Path.Combine(root,"worker.pid")));
            // ShellExecute returns an owned process handle. The broker supplies
            // the equivalent duplicated handle; reopening a high process by PID
            // from medium integrity requests different .NET query rights.
            long handle=long.Parse(File.ReadAllText(Path.Combine(root,"worker.handle")));
            worker=new Process{StartInfo=launch};
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Process).GetMethod("SetProcessId",flags).Invoke(worker,new object[]{pid});
            typeof(Process).GetMethod("SetProcessHandle",flags).Invoke(worker,new object[]{new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(handle),true)});
            return worker;
        };
        var observations=new List<object>();bool observedRead=false,observedWrite=false,withinBudget=true;
        try
        {
            await client.Enable();await client.SetScope(root,true);
            if(!client.Connected||!client.Latest.Running)throw new IOException(client.Latest.State);
            async Task Next()
            {
                var next=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed()=>next.TrySetResult();client.Changed+=Changed;
                try{await next.Task;}finally{client.Changed-=Changed;}
                if(!client.Latest.Running)throw new IOException(client.Latest.State);
            }
            foreach(bool active in new[]{true,false})
            {
                self.Refresh();worker.Refresh();var appCpu=self.TotalProcessorTime;var workerCpu=worker.TotalProcessorTime;var watch=Stopwatch.StartNew();
                long appPrivate=self.PrivateMemorySize64,workerPrivate=worker.PrivateMemorySize64;
                string file=Path.Combine(root,"known.bin");var bytes=new byte[64*1024];
                for(int i=0;i<30;i++)
                {
                    // Actual cached file operations, not fabricated ETW observations.
                    if(active){await File.WriteAllBytesAsync(file,bytes);await File.ReadAllBytesAsync(file);}
                    await Next();
                    observedRead|=client.Latest.Rows.Any(x=>x.Pid==self.Id&&x.ProcessStarted==self.StartTime.ToUniversalTime().Ticks&&x.File.Equals(file,StringComparison.OrdinalIgnoreCase)&&x.Read>0);
                    observedWrite|=client.Latest.Rows.Any(x=>x.Pid==self.Id&&x.ProcessStarted==self.StartTime.ToUniversalTime().Ticks&&x.File.Equals(file,StringComparison.OrdinalIgnoreCase)&&x.Write>0);
                }
                self.Refresh();worker.Refresh();
                withinBudget&=(worker.TotalProcessorTime-workerCpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100<=1&&worker.PrivateMemorySize64<=96*1024*1024;
                observations.Add(new{active,seconds=watch.Elapsed.TotalSeconds,appCpuPercent=(self.TotalProcessorTime-appCpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                    workerCpuPercent=(worker.TotalProcessorTime-workerCpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                    appPrivate=self.PrivateMemorySize64,appPrivateChange=self.PrivateMemorySize64-appPrivate,workerPrivate=worker.PrivateMemorySize64,workerPrivateChange=worker.PrivateMemorySize64-workerPrivate,
                    state=client.Latest.State,lost=client.Latest.Lost});
                File.WriteAllText(Path.Combine(root,"cost.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
            }
            int workerPid=worker.Id;
            await client.SetScope(root,false);
            await client.DisposeAsync();
            File.WriteAllText(Path.Combine(root,"bridge.json"),JsonSerializer.Serialize(new{pass=observedRead&&observedWrite&&withinBudget,pid=self.Id,workerPid,observedRead,observedWrite,withinBudget,observations},new JsonSerializerOptions{WriteIndented=true}));
            return observedRead&&observedWrite&&withinBudget?0:1;
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(root,"bridge-error.txt"),error.ToString());return 1;
        }
        finally{await client.DisposeAsync();}
    }
}
#endif
