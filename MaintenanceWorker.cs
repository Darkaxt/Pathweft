using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileExplorer;

internal sealed record MaintenanceRequest(string Action,string Id,string Journal,string Cache);
internal sealed record MaintenanceReply(bool Ok,string Message,MaintenanceRow Row=null);

// Authenticated, task-scoped privilege boundary following PerformanceIoWorker's
// existing owner/pipe contract. No listener/service, arbitrary shell or driver.
internal sealed class MaintenanceClient : IAsyncDisposable
{
    NamedPipeServerStream pipe;Process process;StreamReader reader;StreamWriter writer;
    readonly CancellationTokenSource stopped=new();
    internal async Task<MaintenanceReply> Start(MaintenanceRequest request,bool elevated,CancellationToken cancel=default)
    {
        string name="Pathweft-Maintenance-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
        pipe=new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var owner=Process.GetCurrentProcess();
        var launch=new ProcessStartInfo(Program.Executable){UseShellExecute=elevated,Verb=elevated?"runas":"",CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"--maintenance-worker",name,Environment.ProcessId.ToString(),owner.StartTime.ToUniversalTime().Ticks.ToString()})launch.ArgumentList.Add(arg);
        try
        {
            process=await Task.Run(()=>Process.Start(launch))??throw new IOException("Maintenance worker did not start");
            process.EnableRaisingEvents=true;process.Exited+=(_,_)=>{if(reader==null)stopped.Cancel();};if(process.HasExited)stopped.Cancel();
            using var waiting=CancellationTokenSource.CreateLinkedTokenSource(stopped.Token,cancel);
            await pipe.WaitForConnectionAsync(waiting.Token);
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint client)||client!=process.Id)throw new IOException("Maintenance worker identity differs");
            reader=new(pipe,leaveOpen:true);writer=new(pipe,leaveOpen:true){AutoFlush=true};
            return await Send(request,cancel);
        }
        catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){return new(false,"Administrator permission cancelled. Nothing changed.");}
    }
    internal async Task<MaintenanceReply> Send(MaintenanceRequest request,CancellationToken cancel=default)
    {
        using var cancellation=cancel.Register(()=>pipe.Dispose());
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            string line=await reader.ReadLineAsync(stopped.Token)??throw new IOException("Maintenance worker exited before returning its result");
            if(line.Length>256*1024)throw new InvalidDataException("Maintenance response is too large");
            return JsonSerializer.Deserialize<MaintenanceReply>(line)??throw new InvalidDataException("Invalid maintenance response");
        }
        catch(Exception)when(cancel.IsCancellationRequested){throw new OperationCanceledException(cancel);}
    }
    public async ValueTask DisposeAsync()
    {
        stopped.Cancel();
        // Cancellation may already have closed the pipe. Retiring the native
        // owner must still run when a buffered writer cannot flush at disposal.
        try{writer?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){}
        try{reader?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){}
        pipe?.Dispose();
        try{if(process!=null){if(reader==null&&!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
        finally{process?.Dispose();stopped.Dispose();}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
}
internal static class MaintenanceWorker
{
    internal static async Task<int> Run(string name,int pid,long started)
    {
        using var owner=Process.GetProcessById(pid);
        if(owner.StartTime.ToUniversalTime().Ticks!=started||!string.Equals(owner.MainModule.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return 2;
        using var lost=new CancellationTokenSource();owner.EnableRaisingEvents=true;owner.Exited+=(_,_)=>lost.Cancel();if(owner.HasExited)return 2;
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(lost.Token);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint server)||server!=pid)return 2;
            using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
            var request=await Read();
            MaintenanceReply failure=null;
            try
            {
            if(request.Action=="analyze")
            {
                var target=CleanupReview.Targets.SingleOrDefault(t=>t.Id==request.Id)??throw new ArgumentException("Unknown cleanup category");
                // Keep the exact review with its owner until Clean or disconnect.
                using var review=new CleanupReview(target,request.Cache);
                var next=Read();
                _=next.ContinueWith(t=>{if(!t.IsCompletedSuccessfully)lost.Cancel();},TaskScheduler.Default);
                await Task.Run(()=>review.Analyze(lost.Token),lost.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,"Review ready",review.Row)));
                var action=await next;if(action.Action!="clean"||action.Id!=request.Id)throw new ArgumentException("Cleanup action differs from its review");
                // Closing the authenticated pipe is cancellation. Observe it even
                // while the file loop owns the worker thread.
                var disconnected=Read();
                _=disconnected.ContinueWith(_=>lost.Cancel(),TaskScheduler.Default);
                var result=await Task.Run(()=>review.Clean(lost.Token),lost.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,$"{result.Deleted:N0} removed · {MainWindow.Fmt(result.Bytes)} · {result.Skipped:N0} skipped")));
            }
            else
            {
                var disconnected=Read();_=disconnected.ContinueWith(_=>lost.Cancel(),TaskScheduler.Default);
                MaintenanceReply result;
                try{result=new(true,await Apply(request,lost.Token));}
                catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
                {result=new(false,error.Message);}
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            }
            catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {failure=new(false,error.Message);}
            if(failure!=null)await writer.WriteLineAsync(JsonSerializer.Serialize(failure));
            lost.Cancel();
            return 0;
            async Task<MaintenanceRequest> Read()
            {
                string line=await reader.ReadLineAsync(lost.Token)??throw new IOException("Maintenance owner disconnected");
                if(line.Length>32768)throw new InvalidDataException("Maintenance request is too large");
                return JsonSerializer.Deserialize<MaintenanceRequest>(line)??throw new InvalidDataException("Missing maintenance request");
            }
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException){return 1;}
    }
    internal static async Task<string> Apply(MaintenanceRequest request,CancellationToken cancel=default)
    {
        if(request.Action=="restore-point")return await WindowsAction("RestorePoint",cancel:cancel);
        if(request.Action=="remove-app")
        {
            if(!Maintenance.Apps.Any(a=>a.Package==request.Id))throw new ArgumentException("Unknown built-in app");
            return await WindowsAction("RemoveApp",request.Id,cancel);
        }
        if(request.Action is not ("apply" or "restore"))throw new ArgumentException("Unknown maintenance action");
        bool restore=request.Action=="restore";
        if(request.Id.StartsWith("service:"))return MaintenanceServices.Change(request.Id[8..],restore,request.Journal);
        if(request.Id.StartsWith("startup-file:"))return MaintenanceStartup.ChangeFile(request.Id,restore,request.Journal);
        if(request.Id.StartsWith("startup-task:"))
        {try{return MaintenanceStartup.ChangeTask(request.Id,restore,request.Journal);}catch(COMException error){throw new IOException("Windows Task Scheduler: "+error.Message,error);}}
        if(restore)Maintenance.RestoreRegistry(request.Id,request.Journal);
        else Maintenance.ApplyRegistry(request.Id,request.Id.StartsWith("startup:")?Maintenance.StartupEdits(request.Id):Maintenance.TweakEdits(request.Id),request.Journal);
        return restore?"Original settings restored.":"Change verified. Original settings saved for Restore.";
    }
    internal static async Task<string> WindowsAction(string action,string package=null,CancellationToken cancel=default)
    {
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.System);
        var launch=new ProcessStartInfo(Path.Combine(windows,"WindowsPowerShell","v1.0","powershell.exe"))
        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-File",Path.Combine(AppContext.BaseDirectory,"Maintenance-Windows.ps1"),"-Action",action})launch.ArgumentList.Add(arg);
        if(package!=null){launch.ArgumentList.Add("-Package");launch.ArgumentList.Add(package);}
        var result=await MaintenanceProcess.Run(launch,cancel);string text=result.Output,errors=result.Error;
        if(result.ExitCode!=0)throw new IOException(errors.Trim().Length>0?errors.Trim():"Windows maintenance failed");
        return action=="ListApps"?text:JsonDocument.Parse(text).RootElement.GetProperty("message").GetString();
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
}
