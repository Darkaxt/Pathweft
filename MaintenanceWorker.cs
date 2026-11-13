using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileExplorer;

internal sealed record MaintenanceRequest(string Action,string Id,string Journal,string Cache);
internal sealed record AccessEntry(string Name,string Path,FileAttributes Attributes,long Bytes,DateTime Modified);
internal sealed record MaintenanceReply(bool Ok,string Message,MaintenanceRow Row=null,AccessEntry[] Entries=null,SpaceUpdate[] Updates=null,bool More=false,int ErrorCode=0,DriveHealth Health=null,CleanupProgress Progress=null,CleanupOutcome Outcome=null);

// Authenticated, task-scoped privilege boundary following PerformanceIoWorker's
// existing owner/pipe contract. No listener/service, arbitrary shell or driver.
internal sealed class MaintenanceClient : IAsyncDisposable
{
    readonly bool maintenanceScope;
    internal MaintenanceClient(bool maintenanceScope=false)=>this.maintenanceScope=maintenanceScope;
    NamedPipeServerStream pipe;Process process;StreamReader reader;StreamWriter writer;
    AdministratorSession.Operation session;CancellationTokenRegistration sessionStop;
    Task disposal;
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
            if(elevated)
            {
                session=await AdministratorSession.Open(name,"maintenance",cancel,enable:request.Action is not ("list" or "space" or "disk-health"),maintenanceScope:maintenanceScope).ConfigureAwait(false);
                sessionStop=session.Stopped.Register(()=>{stopped.Cancel();pipe.Dispose();});
            }
            else
            {
                process=await Task.Run(()=>Process.Start(launch))??throw new IOException("Maintenance worker did not start");
                process.EnableRaisingEvents=true;process.Exited+=(_,_)=>{if(reader==null)stopped.Cancel();};if(process.HasExited)stopped.Cancel();
            }
            // Admission has already started this exact operation; connect it
            // before honoring cancellation so the hosted reader can retire.
            await pipe.WaitForConnectionAsync(stopped.Token);
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint client)||client!=(session?.Pid??process.Id))throw new IOException("Maintenance worker identity differs");
            reader=new(pipe,leaveOpen:true);writer=new(pipe,leaveOpen:true){AutoFlush=true};
            return await Send(request,cancel);
        }
        catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){return new(false,"Administrator permission cancelled. Nothing changed.");}
    }
    internal async Task<MaintenanceReply> Send(MaintenanceRequest request,CancellationToken cancel=default,Action<CleanupProgress> progress=null)
    {
        using var cancellation=cancel.Register(()=>pipe.Dispose());
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            while(true)
            {
                string line=await reader.ReadLineAsync(stopped.Token)??throw new IOException("Maintenance worker exited before returning its result");
                if(line.Length>8*1024*1024)throw new InvalidDataException("Maintenance response is too large");
                var reply=JsonSerializer.Deserialize<MaintenanceReply>(line)??throw new InvalidDataException("Invalid maintenance response");
                if(reply.Progress!=null)progress?.Invoke(reply.Progress);
                if(!reply.More||reply.Progress==null)return reply;
            }
        }
        catch(Exception)when(cancel.IsCancellationRequested){throw new OperationCanceledException(cancel);}
    }
    internal async Task Stream(MaintenanceRequest request,Action<MaintenanceReply> publish,CancellationToken cancel)
    {
        try
        {
            var reply=await Start(request,true,cancel).ConfigureAwait(false);
            using var cancellation=cancel.Register(()=>pipe?.Dispose());
            while(true)
            {
                if(!reply.Ok){if(reply.ErrorCode==5)throw new UnauthorizedAccessException(reply.Message);throw new IOException(reply.Message);}publish(reply);if(!reply.More)break;
                string line=await reader.ReadLineAsync(stopped.Token).ConfigureAwait(false)??throw new IOException("Administrator read ended before completion");
                if(line.Length>8*1024*1024)throw new InvalidDataException("Administrator read response is too large");
                reply=JsonSerializer.Deserialize<MaintenanceReply>(line)??throw new InvalidDataException("Invalid administrator read");
            }
        }
        catch(Exception)when(cancel.IsCancellationRequested){throw new OperationCanceledException(cancel);}
    }
    internal static async Task<List<AccessEntry>> ReadFolder(string path,CancellationToken cancel)
    {
        var rows=new List<AccessEntry>();await using var client=new MaintenanceClient();
        await client.Stream(new("list",path,null,null),reply=>{if(reply.Entries!=null)rows.AddRange(reply.Entries);},cancel).ConfigureAwait(false);return rows;
    }
    internal static async Task ScanSpace(string path,Action<SpaceUpdate> publish,CancellationToken cancel)
    {
        await using var client=new MaintenanceClient();
        await client.Stream(new("space",path,null,null),reply=>{if(reply.Updates!=null)foreach(var update in reply.Updates)publish(update);},cancel).ConfigureAwait(false);
    }
    internal static async Task SaveEditor(string path,byte[] bytes)
    {
        string staging=Path.Combine(DataFolder.Cache,"EditorSaves");Directory.CreateDirectory(staging);
        string source=Path.Combine(staging,Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(source,bytes).ConfigureAwait(false);
            string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            await using var client=new MaintenanceClient();
            var result=await client.Start(new("save-editor",Path.GetFullPath(path),hash,source),true).ConfigureAwait(false);
            if(!result.Ok)throw new IOException(result.Message);
        }
        finally{File.Delete(source);}
    }
    public ValueTask DisposeAsync()=>new(disposal??=Close());
    async Task Close()
    {
        stopped.Cancel();
        // Cancellation may already have closed the pipe. Retiring the native
        // owner must still run when a buffered writer cannot flush at disposal.
        try{writer?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){}
        try{reader?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){}
        pipe?.Dispose();
        sessionStop.Dispose();if(session!=null)await session.DisposeAsync().ConfigureAwait(false);
        try{if(process!=null){if(reader==null&&!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
        finally{process?.Dispose();stopped.Dispose();}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
}
internal static class MaintenanceWorker
{
    static MaintenanceReply Failure(Exception error)=>new(false,error.Message,ErrorCode:error is UnauthorizedAccessException or System.Security.SecurityException||error is System.ComponentModel.Win32Exception{NativeErrorCode:5}?5:0);
    internal static async Task<int> Run(string name,int pid,long started,CancellationToken session=default)
    {
        using var owner=Process.GetProcessById(pid);
        if(owner.StartTime.ToUniversalTime().Ticks!=started||!string.Equals(owner.MainModule.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return 2;
        using var lost=CancellationTokenSource.CreateLinkedTokenSource(session);owner.EnableRaisingEvents=true;owner.Exited+=(_,_)=>lost.Cancel();if(owner.HasExited)return 2;
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
            if(request.Action is "list" or "space")
            {
                if(!Path.IsPathFullyQualified(request.Id))throw new ArgumentException("An absolute filesystem folder is required");
                string folder=Path.GetFullPath(request.Id);
                var disconnected=Read();_=disconnected.ContinueWith(_=>lost.Cancel(),TaskScheduler.Default);
                if(request.Action=="list")
                {
                    await Task.Run(()=>
                    {
                        var page=new List<AccessEntry>(16);
                        foreach(var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                        {
                            lost.Token.ThrowIfCancellationRequested();
                            page.Add(new(entry.Name,entry.FullName,entry.Attributes,entry is FileInfo file?file.Length:-1,entry.LastWriteTime));
                            if(page.Count==16){writer.WriteLine(JsonSerializer.Serialize(new MaintenanceReply(true,"Reading folder",Entries:page.ToArray(),More:true)));page.Clear();}
                        }
                        writer.WriteLine(JsonSerializer.Serialize(new MaintenanceReply(true,"Folder read",Entries:page.ToArray())));
                    },lost.Token).ConfigureAwait(false);
                }
                else
                {
                    var channel=System.Threading.Channels.Channel.CreateBounded<SpaceUpdate>(512);
                    var scan=Task.Run(async()=>
                    {
                        try{await SpaceScan.RunAsync(folder,false,null,update=>channel.Writer.WriteAsync(update,lost.Token).AsTask().GetAwaiter().GetResult(),lost.Token).ConfigureAwait(false);channel.Writer.TryComplete();}
                        catch(Exception error){channel.Writer.TryComplete(error);throw;}
                    });
                    try
                    {
                        var page=new List<SpaceUpdate>(8);
                        await foreach(var update in channel.Reader.ReadAllAsync(lost.Token).ConfigureAwait(false))
                        {
                            page.Add(update);
                            if(page.Count==8||update.Path.Equals(folder,StringComparison.OrdinalIgnoreCase)&&update.State is not (SpaceState.Pending or SpaceState.Scanning))
                            {await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,"Measuring folder",Updates:page.ToArray(),More:true))).ConfigureAwait(false);page.Clear();}
                        }
                        await scan.ConfigureAwait(false);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,"Folder measured",Updates:page.ToArray()))).ConfigureAwait(false);
                    }
                    finally{lost.Cancel();await scan.ConfigureAwait(false);}
                }
            }
            else if(request.Action=="disk-health")
            {
                if(!uint.TryParse(request.Id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out uint number))throw new ArgumentException("A physical drive number is required");
                var health=await Task.Run(()=>WindowsDriveHealth.Read(number)).ConfigureAwait(false);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,"Drive health read",Health:health))).ConfigureAwait(false);
            }
            else if(request.Action=="analyze")
            {
                var target=CleanupReview.Targets.SingleOrDefault(t=>t.Id==request.Id)??throw new ArgumentException("Unknown cleanup category");
                // Keep the exact review with its owner until Clean or disconnect.
                RequireOwnedPaths(request);
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
                long shown=0;
                var result=await Task.Run(()=>review.Clean(lost.Token,progress=>
                {
                    long now=Environment.TickCount64;
                    // Rendering/IPC cadence only, never operation synchronization.
                    if(now-shown<100&&progress.Completed!=progress.Total)return;shown=now;
                    writer.WriteLine(JsonSerializer.Serialize(new MaintenanceReply(true,progress.Phase,More:true,Progress:progress)));
                }),lost.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new MaintenanceReply(true,$"{result.Deleted:N0} removed · {MainWindow.Fmt(result.Bytes)} · {result.Skipped:N0} skipped",Outcome:result)));
            }
            else
            {
                var disconnected=Read();_=disconnected.ContinueWith(_=>lost.Cancel(),TaskScheduler.Default);
                MaintenanceReply result;
                try{result=new(true,await Apply(request,lost.Token));}
                catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
                {result=Failure(error);}
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            }
            catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {failure=Failure(error);}
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
        if(request.Action=="save-editor")return SaveEditor(request,cancel);
        if(request.Action=="restore-point")return await WindowsAction("RestorePoint",cancel:cancel);
        // Administrator leftovers of a removed app (MAINT-36): the worker re-scans with the same identity and
        // removes only requested items it finds again, after its own .reg backup inside the profile's folder.
        if(request.Action=="remove-leftovers")
        {
            var data=JsonSerializer.Deserialize<MaintenanceLeftovers.Request>(request.Id)??throw new InvalidDataException("Missing leftovers request");
            return JsonSerializer.Serialize(await Task.Run(()=>MaintenanceLeftovers.Remove(data.App,data.Items,true,request.Journal,cancel),cancel));
        }
        if(request.Action=="restore-leftovers"){await MaintenanceLeftovers.Restore(request.Journal,cancel);return "Registry backup restored.";}
        if(request.Action is not ("apply" or "restore" or "reset-default"))throw new ArgumentException("Unknown maintenance action");
        bool restore=request.Action=="restore";
        RequireOwnedPaths(request);
        if(request.Action=="reset-default")
        {
            if(!request.Id.StartsWith("tweak:",StringComparison.Ordinal))throw new ArgumentException("Only Windows tweaks can return to the Windows default");
            Maintenance.ResetPolicies(request.Id,request.Journal);return "Windows default restored. Previous policy values saved for Restore.";
        }
        if(request.Id.StartsWith("service:"))return MaintenanceServices.Change(request.Id[8..],restore,request.Journal);
        if(request.Id.StartsWith("startup-file:"))return MaintenanceStartup.ChangeFile(request.Id,restore,request.Journal);
        if(request.Id.StartsWith("startup-task:"))
        {try{return MaintenanceStartup.ChangeTask(request.Id,restore,request.Journal);}catch(COMException error){throw new IOException("Windows Task Scheduler: "+error.Message,error);}}
        try
        {
            if(restore)Maintenance.RestoreRegistry(request.Id,request.Journal);
            else Maintenance.ApplyRegistry(request.Id,request.Id.StartsWith("startup:")?Maintenance.StartupEdits(request.Id):Maintenance.TweakEdits(request.Id),request.Journal);
        }
        catch(COMException error){throw new IOException("Windows Task Scheduler: "+error.Message,error);}
        return restore?"Original settings restored.":"Change verified. Original settings saved for Restore.";
    }
    // The elevated worker writes journals and review manifests only to this
    // profile's Maintenance locations (DEBUG verification uses owned fixture roots).
    static void RequireOwnedPaths(MaintenanceRequest request)
    {
#if !DEBUG
        static bool Same(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
        if(request.Journal!=null&&!Same(request.Journal,Maintenance.Journal))throw new ArgumentException("Maintenance journal is outside this profile");
        if(request.Cache!=null&&!Same(request.Cache,Path.Combine(DataFolder.Cache,"Maintenance")))throw new ArgumentException("Maintenance review folder is outside this profile");
#endif
    }
    static string SaveEditor(MaintenanceRequest request,CancellationToken cancel)
    {
        if(!Path.IsPathFullyQualified(request.Id)||!Path.IsPathFullyQualified(request.Cache))throw new ArgumentException("An absolute editor destination and staged source are required");
        string source=Path.GetFullPath(request.Cache),destination=Path.GetFullPath(request.Id);
        string staging=Path.Combine(DataFolder.Cache,"EditorSaves");
        if(!string.Equals(Path.GetDirectoryName(source),staging,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(source),"N",out _))throw new ArgumentException("Editor source is outside its staging folder");
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Editor source is a link");
        using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(request.Journal,StringComparison.Ordinal))throw new IOException("Editor contents changed before saving");
        input.Position=0;cancel.ThrowIfCancellationRequested();
        string temporary=destination+".pathweft-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {input.CopyTo(output);output.Flush(true);}
            cancel.ThrowIfCancellationRequested();
            if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
            return "File saved";
        }
        finally{File.Delete(temporary);}
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
        return JsonDocument.Parse(text).RootElement.GetProperty("message").GetString();
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
}
