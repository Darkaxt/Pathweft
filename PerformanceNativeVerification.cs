#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing.Session;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPerformanceNative18(string root,Action<bool,string> check)
    {
        check(HiddenTrialPresentation.PrivateSpaceTrial,"Native performance workflow owns a never-switched private desktop");
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>ready.TrySetResult();
            PerformanceMonitor.Current.Changed+=Updated;
            try{await ready.Task;}finally{PerformanceMonitor.Current.Changed-=Updated;}
            await Settle();
        }
        Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);
        string folder=Path.Combine(root,"activity");Directory.CreateDirectory(folder);
        await S1Verification.Navigate(Left,folder);Activate();
        PerformanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        performance.FocusForVerification(3);await Settle();
        check(IsActive&&performance.IsKeyboardFocusWithin,"Actual native focus reaches the folder widget");
        var existing=TraceEventSession.GetActiveSessionNames().Where(x=>x.StartsWith("Pathweft-Io-")).ToArray();
        await performance.EnableForVerification();await performance.SyncForVerification();
        check(performance.IoForVerification is {Running:true},"App-owned worker acknowledges actual focused folder tracing");
        var eventName="PathweftPerformance-"+Guid.NewGuid().ToString("N");
        using var stop=new EventWaitHandle(false,EventResetMode.ManualReset,eventName);
        var executable=Environment.GetEnvironmentVariable("PATHWEFT_PERFORMANCE_LOAD");
        Process StartLoad(params string[] arguments)
        {
            var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add(eventName);foreach(string argument in arguments)start.ArgumentList.Add(argument);
            return Process.Start(start);
        }
        string childFile=Path.Combine(folder,"other-process.bin");
        using(var child=StartLoad(childFile))
        {
            bool observed=false;
            try{for(int i=0;i<4;i++){await Next();observed|=performance.IoForVerification?.Rows.Any(x=>x.Pid==child.Id&&x.ProcessStarted>0&&x.File.Equals(childFile,StringComparison.OrdinalIgnoreCase)&&x.Write>0)==true;}}
            finally{stop.Set();await child.WaitForExitAsync();}
            check(observed&&child.ExitCode==0,"Known native other-process file writes retain correct PID/start/file attribution");
        }
        stop.Reset();
        PerformanceMonitor.Current.DetailKind=1;
        using(var load=StartLoad("--cpu"))
        {
            bool observed=false;
            try{for(int i=0;i<4;i++){await Next();observed|=PerformanceMonitor.Current.Details.Any(x=>x.Pid==load.Id&&x.Usage>0&&x.Started>0);}}
            finally{stop.Set();await load.WaitForExitAsync();}
            check(load.ExitCode==0&&observed,"Known headless CPU workload is attributed to its actual process and start identity");
        }
        stop.Reset();
        PerformanceMonitor.Current.DetailKind=2;
        using(var load=StartLoad())
        {
            bool observed=false,attributed=false;
            try{for(int i=0;i<4;i++){await Next();observed|=PerformanceMonitor.Current.Latest.Gpus.Any(x=>x.Usage>0);attributed|=PerformanceMonitor.Current.Details.Any(x=>x.Pid==load.Id&&x.Usage>0&&x.Started>0);}}
            finally{stop.Set();await load.WaitForExitAsync();}
            check(load.ExitCode==0&&observed&&attributed,"Headless hardware D3D11 workload produces positive adapter utilization and actual GPU process attribution");
        }
        PerformanceMonitor.Current.DetailKind=0;
        string file=Path.Combine(folder,"known.bin");var payload=new byte[1024*1024];new Random(18).NextBytes(payload);
        var observations=new List<IoRow>();
        // Event-driven sample windows bound this diagnostic, not collection lifetime.
        for(int i=0;i<4;i++)
        {
            await Task.Run(()=>{File.WriteAllBytes(file,payload);File.ReadAllBytes(file);});
            await Next();observations.AddRange(performance.IoForVerification?.Rows??Array.Empty<IoRow>());
        }
        check(observations.Any(x=>x.File.Equals(file,StringComparison.OrdinalIgnoreCase)&&x.Pid==Environment.ProcessId&&x.ProcessStarted>0&&x.Write>0),"Known writes are attributed to the actual file and PID/start identity");
        check(observations.Any(x=>x.File.Equals(file,StringComparison.OrdinalIgnoreCase)&&x.Read>0),"Cached file reads are represented as logical requests");
        performance.Select(-1);await Settle();await performance.SyncForVerification();
        check(performance.IoForVerification is {Running:true},"Returning to the compact Folder card retains collection only while that card has native focus");
        File.WriteAllBytes(file,payload);await Next();
        await S1Verification.Snapshot(this,root,"performance-folder-card");
        Left.ListForVerification.Focus();await Settle();await performance.SyncForVerification();
        check(!performance.IsKeyboardFocusWithin&&performance.IoForVerification is {Running:false},"Losing actual widget focus acknowledges trace shutdown");
        File.WriteAllBytes(file,payload);await Next();
        check(performance.IoForVerification is {Running:false,Rows.Length:0},"Unfocused folder writes never restart file tracing");
        performance.FocusForVerification(3);await Settle();await performance.SyncForVerification();
        check(performance.IoForVerification is {Running:true},"Refocusing resumes the same owned worker with a fresh trace scope");
        performance.SetFolder(Path.Combine(root,"other"));await performance.SyncForVerification();
        check(performance.IoForVerification?.Rows.Length==0,"Scope replacement clears old attributed rows after acknowledgement");
        await S1Verification.Snapshot(this,root,"performance-folder-details");
        PerformanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle();
        await performance.DisposeAsync();
        var remaining=TraceEventSession.GetActiveSessionNames().Where(x=>x.StartsWith("Pathweft-Io-")).ToArray();
        check(remaining.Order().SequenceEqual(existing.Order()),"Closing performance retires its exact native worker/session and preserves unrelated traces");
        File.WriteAllText(Path.Combine(root,"io-observations.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
