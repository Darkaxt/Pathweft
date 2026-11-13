using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace FileExplorer;

internal sealed record AdministratorRequest(string Kind,string Pipe);
internal sealed record AdministratorReply(bool Ok,string Message,int Pid=0);

// One UAC-approved process per run, requested at launch (ACCESS-04) and shared by
// Explorer browsing, Performance and Maintenance (ACCESS-07). It hosts only the existing scoped Maintenance / I/O protocols; no request
// supplies an executable, command line or script. Authenticated operation pipes
// retain cancellation and exact-review lifetimes without serializing long scans.
internal sealed class AdministratorSession : IAsyncDisposable
{
    static readonly SemaphoreSlim admission=new(1);
    static AdministratorSession current;
    internal static bool Browsing {get;private set;}
    internal static bool Available=>current is { } session&&!session.stopped.IsCancellationRequested;
    internal static event Action Changed;
    internal static void SetBrowsing(bool enabled){Browsing=enabled&&Available;Changed?.Invoke();}
    readonly SemaphoreSlim commands=new(1);
    readonly SemaphoreSlim operations=new(8);
    readonly CancellationTokenSource stopped=new();
    NamedPipeServerStream pipe;StreamReader reader;StreamWriter writer;Process process;int pid;bool disposed;
#if DEBUG
    internal static Func<ProcessStartInfo,Task<Process>> LaunchForVerification;
#endif
    internal static async Task<bool> Enable(CancellationToken cancel=default)
    {
        await admission.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if(Available)return true;
            if(current!=null){await current.DisposeAsync().ConfigureAwait(false);current=null;}
            var session=new AdministratorSession();
            try
            {
                await session.Start(cancel).ConfigureAwait(false);current=session;Changed?.Invoke();return true;
            }
            catch(Win32Exception error)when(error.NativeErrorCode==1223)
            {await session.DisposeAsync().ConfigureAwait(false);return false;}
            catch{await session.DisposeAsync().ConfigureAwait(false);throw;}
        }
        finally{admission.Release();}
    }
    async Task Start(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();using var owner=Process.GetCurrentProcess();
        string name="Pathweft-Administrator-"+owner.Id+"-"+Guid.NewGuid().ToString("N");
        pipe=new(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var launch=new ProcessStartInfo(Program.Executable){UseShellExecute=true,Verb="runas",CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        foreach(string arg in new[]{"--administrator-session",name,owner.Id.ToString(),owner.StartTime.ToUniversalTime().Ticks.ToString(),Settings.Profile??""})launch.ArgumentList.Add(arg);
#if DEBUG
        process=LaunchForVerification!=null?await LaunchForVerification(launch).ConfigureAwait(false):await Task.Run(()=>Process.Start(launch)).ConfigureAwait(false);
#else
        process=await Task.Run(()=>Process.Start(launch)).ConfigureAwait(false);
#endif
        if(process==null)throw new IOException("Administrator session did not start");
        pid=process.Id;process.EnableRaisingEvents=true;process.Exited+=(_,_)=>{stopped.Cancel();if(ReferenceEquals(current,this)){Browsing=false;Changed?.Invoke();}};
        if(process.HasExited)stopped.Cancel();
        // After Windows has launched the helper, finish this same handshake.
        // Closing a not-yet-connected server would strand its ConnectAsync.
        await pipe.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false);
        if(!ClientPid(pipe,out uint client)||client!=pid)throw new IOException("Administrator session identity differs");
        reader=new(pipe,leaveOpen:true);writer=new(pipe,leaveOpen:true){AutoFlush=true};
        var ready=await ReadReply(stopped.Token).ConfigureAwait(false);
        if(!ready.Ok||ready.Pid!=pid)throw new IOException(ready.Message);
        cancel.ThrowIfCancellationRequested();
    }
    async Task<AdministratorReply> ReadReply(CancellationToken token)
    {
        string line=await reader.ReadLineAsync(token).ConfigureAwait(false)??throw new IOException("Administrator session disconnected");
        if(line.Length>32768)throw new InvalidDataException("Administrator response is too large");
        return JsonSerializer.Deserialize<AdministratorReply>(line)??throw new InvalidDataException("Invalid administrator response");
    }
    async Task Call(string kind,string name,CancellationToken cancel=default)
    {
        await commands.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            stopped.Token.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(new AdministratorRequest(kind,name))).ConfigureAwait(false);
            // Once dispatched retain the original operation. Cancellation of a
            // launch must not abandon a broker-created operation or replay it.
            var reply=await ReadReply(stopped.Token).ConfigureAwait(false);
            if(!reply.Ok)throw new IOException(reply.Message);
        }
        finally{commands.Release();}
    }
    internal sealed class Operation(AdministratorSession session,string pipeName) : IAsyncDisposable
    {
        Task disposal;
        internal int Pid=>session.pid;
        internal CancellationToken Stopped=>session.stopped.Token;
        public ValueTask DisposeAsync()=>new(disposal??=Close());
        async Task Close()
        {
            try{if(!session.stopped.IsCancellationRequested)await session.Call("wait",pipeName).ConfigureAwait(false);}
            catch(Exception error)when(session.stopped.IsCancellationRequested&&error is IOException or OperationCanceledException or ObjectDisposedException){}
            finally{session.operations.Release();}
        }
    }
    internal static async Task<Operation> Open(string name,string kind,CancellationToken cancel,bool enable=true)
    {
        cancel.ThrowIfCancellationRequested();
        if(enable&&!await Enable(cancel).ConfigureAwait(false))throw new Win32Exception(1223,"Administrator permission cancelled. Nothing changed.");
        if(!Available)throw new IOException("Administrator session ended");
        var session=current??throw new IOException("Administrator session ended");
        // Queue at the owner before sending admission. Never hold the control
        // pipe while waiting for capacity: existing operations need it to retire.
        using var waiting=CancellationTokenSource.CreateLinkedTokenSource(cancel,session.stopped.Token);
        await session.operations.WaitAsync(waiting.Token).ConfigureAwait(false);
        try{await session.Call(kind,name,cancel).ConfigureAwait(false);return new(session,name);}
        catch{session.operations.Release();throw;}
    }
    internal static async Task SetActivityRecorder(bool on,CancellationToken cancel=default)
    {
        if(!await Enable(cancel).ConfigureAwait(false))throw new Win32Exception(1223,"Administrator permission cancelled. Nothing changed.");
        var session=current??throw new IOException("Administrator session ended");
        await session.Call(on?"activity-on":"activity-off","",cancel).ConfigureAwait(false);
    }
    internal static async Task End()
    {
        await admission.WaitAsync().ConfigureAwait(false);
        try{var old=current;current=null;Browsing=false;Changed?.Invoke();if(old!=null)await old.DisposeAsync().ConfigureAwait(false);}
        finally{admission.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;stopped.Cancel();
        // Closing the control pipe cancels every hosted operation. Each one
        // drains through its existing cancellation/finally path before exit.
        pipe?.Dispose();
        if(process!=null){await process.WaitForExitAsync().ConfigureAwait(false);process.Dispose();}
        try{writer?.Dispose();}catch(Exception error)when(error is IOException or ObjectDisposedException){}
        reader?.Dispose();
    }
    internal static async Task<int> Run(string name,int ownerPid,long ownerStarted,string profile)
    {
        using var owner=Process.GetProcessById(ownerPid);using var identity=WindowsIdentity.GetCurrent();
        if(owner.StartTime.ToUniversalTime().Ticks!=ownerStarted||!string.Equals(owner.MainModule.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return 2;
        if(!ValidPipe(name,"Pathweft-Administrator-",ownerPid))return 2;
        using var stop=new CancellationTokenSource();owner.EnableRaisingEvents=true;owner.Exited+=(_,_)=>stop.Cancel();if(owner.HasExited)return 2;
        using var connection=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        var operations=new Dictionary<string,Task>(StringComparer.Ordinal);
        try
        {
            await connection.ConnectAsync(stop.Token).ConfigureAwait(false);
            if(!ServerPid(connection,out uint server)||server!=ownerPid)return 2;
            using var read=new StreamReader(connection,leaveOpen:true);using var write=new StreamWriter(connection,leaveOpen:true){AutoFlush=true};
            bool elevated=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            await write.WriteLineAsync(JsonSerializer.Serialize(new AdministratorReply(elevated,elevated?"Administrator session ready":"Windows did not grant administrator access",Environment.ProcessId))).ConfigureAwait(false);
            if(!elevated)return 2;
            Environment.SetEnvironmentVariable("PATHWEFT_PROFILE",string.IsNullOrEmpty(profile)?null:Path.GetFullPath(profile));
            while(await read.ReadLineAsync(stop.Token).ConfigureAwait(false) is {} line)
            {
                if(line.Length>32768)throw new InvalidDataException("Administrator request is too large");
                var request=JsonSerializer.Deserialize<AdministratorRequest>(line)??throw new InvalidDataException("Missing administrator request");
                AdministratorReply reply;
                if(request.Kind=="wait"&&operations.Remove(request.Pipe,out var completing))
                {await completing.ConfigureAwait(false);reply=new(true,"Operation retired");}
                else if(request.Kind is "maintenance" or "io"&&ValidPipe(request.Pipe,request.Kind=="io"?"Pathweft-Io-":"Pathweft-Maintenance-",ownerPid))
                {
                    foreach(string finished in operations.Where(x=>x.Value.IsCompleted).Select(x=>x.Key).ToArray()){await operations[finished].ConfigureAwait(false);operations.Remove(finished);}
                    if(operations.Count>=8||operations.ContainsKey(request.Pipe))reply=new(false,"Administrator session is busy; finish an existing operation first");
                    else
                    {
                        operations.Add(request.Pipe,Task.Run(async()=>
                        {
                            try{if(request.Kind=="io")await PerformanceIoWorker.Run(request.Pipe,ownerPid,ownerStarted,stop.Token).ConfigureAwait(false);else await MaintenanceWorker.Run(request.Pipe,ownerPid,ownerStarted,stop.Token).ConfigureAwait(false);}
                            catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException or Win32Exception or InvalidOperationException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or System.Security.SecurityException){}
                        }));reply=new(true,"Operation admitted",Environment.ProcessId);
                    }
                }
                else if(request.Kind=="wait"&&ValidPipe(request.Pipe,"Pathweft-Maintenance-",ownerPid)||request.Kind=="wait"&&ValidPipe(request.Pipe,"Pathweft-Io-",ownerPid))reply=new(true,"Operation already retired");
                else if(request.Kind is "activity-on" or "activity-off"&&request.Pipe=="")
                {
                    // ACTIVITY-01: the approved session registers or removes the elevated recorder task; no second prompt.
                    try{StartupTasks.SetRecorder(request.Kind=="activity-on");reply=new(true,request.Kind=="activity-on"?"App activity recording is on":"App activity recording is off");}
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or System.Security.SecurityException or ArgumentException){reply=new(false,error.Message);}
                }
                else reply=new(false,"Unsupported administrator request");
                await write.WriteLineAsync(JsonSerializer.Serialize(reply)).ConfigureAwait(false);
            }
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException or UnauthorizedAccessException or JsonException){}
        finally{stop.Cancel();await Task.WhenAll(operations.Values).ConfigureAwait(false);}
        return 0;
    }
    static bool ValidPipe(string name,string prefix,int pid)=>name!=null&&name.StartsWith(prefix+pid+"-",StringComparison.Ordinal)&&Guid.TryParseExact(name[(prefix+pid+"-").Length..],"N",out _);
    internal static bool ClientPid(NamedPipeServerStream pipe,out uint pid)=>GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid);
    internal static bool ServerPid(NamedPipeClientStream pipe,out uint pid)=>GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
}
