using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace FileExplorer;

internal sealed record IoRow(int Pid,long ProcessStarted,string Process,string File,double Read,double Write)
{public double Total=>Read+Write;}
internal sealed record IoReport(string State,IoRow[] Rows,long Lost,bool Running,long Generation=0);
internal sealed record IoRequest(string Scope,bool Start,long Generation);

// A privilege boundary, not a service: an explicitly requested worker owns only
// its unique real-time session and exits on owner/pipe loss. No ETL file is made.
internal static class PerformanceIoWorker
{
    internal static async Task<int> Run(string pipeName,int ownerPid,long ownerStarted,CancellationToken session=default)
    {
        using var owner=Process.GetProcessById(ownerPid);
        if(owner.StartTime.ToUniversalTime().Ticks!=ownerStarted||!string.Equals(owner.MainModule.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))return 2;
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var lostOwner=CancellationTokenSource.CreateLinkedTokenSource(session);
        owner.EnableRaisingEvents=true;owner.Exited+=(_,_)=>lostOwner.Cancel();
        if(owner.HasExited)return 2;
        using var release=EventWaitHandle.OpenExisting(pipeName+"-stop");
        var registration=ThreadPool.RegisterWaitForSingleObject(release,(_,_)=>lostOwner.Cancel(),null,Timeout.Infinite,true);
        try
        {
        try{await pipe.ConnectAsync(lostOwner.Token);}
        catch(OperationCanceledException)when(lostOwner.IsCancellationRequested){return 0;}
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint server)||server!=ownerPid)return 2;
        using var reader=new StreamReader(pipe,leaveOpen:true);
        using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
        using var collection=new IoCollection();
        var writeGate=new SemaphoreSlim(1);
        var collectionGate=new SemaphoreSlim(1);
        async Task Send(IoReport report)
        {
            await writeGate.WaitAsync(lostOwner.Token);
            try{await writer.WriteLineAsync(JsonSerializer.Serialize(report));}
            finally{writeGate.Release();}
        }
        await Send(new("Detailed I/O ready",Array.Empty<IoRow>(),0,false));
        var reporting=Task.Run(async()=>
        {
            using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
            try{while(await timer.WaitForNextTickAsync(lostOwner.Token))
            {
                await collectionGate.WaitAsync(lostOwner.Token);
                try{if(collection.Running)await Send(collection.Take());}finally{collectionGate.Release();}
            }}
            catch(Exception ex)when(ex is IOException or OperationCanceledException or ObjectDisposedException){lostOwner.Cancel();}
        });
        try
        {
            while(await reader.ReadLineAsync(lostOwner.Token) is {} line)
            {
                if(line.Length>32768)throw new InvalidDataException("I/O request is too large");
                var request=JsonSerializer.Deserialize<IoRequest>(line)??throw new InvalidDataException("Missing I/O request");
                await collectionGate.WaitAsync(lostOwner.Token);
                try
                {
                    collection.Stop();
                    collection.Generation=request.Generation;
                    if(request.Start)collection.Start(Path.GetFullPath(request.Scope));
                    await Send(collection.Take());
                }
                finally{collectionGate.Release();}
            }
        }
        catch(Exception ex)when(ex is IOException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ApplicationException or InvalidOperationException)
        {
            if(!lostOwner.IsCancellationRequested&&pipe.IsConnected)
                try{await Send(new(ex.Message,Array.Empty<IoRow>(),0,false,collection.Generation));}catch(IOException){}
        }
        finally{lostOwner.Cancel();await reporting;collection.Stop();writeGate.Dispose();collectionGate.Dispose();}
        return 0;
        }
        finally
        {
            using var callbacksDone=new ManualResetEvent(false);
            // Cancellation can complete this async method inline inside the
            // registered callback itself. Never synchronously join that same
            // callback: yield to an event-owned wait so it can return first.
            if(registration.Unregister(callbacksDone))await Task.Run(()=>callbacksDone.WaitOne()).ConfigureAwait(false);
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
}

internal sealed class IoCollection : IDisposable
{
    readonly object gate=new();
    readonly Dictionary<(int Pid,long Started,string Name,string File),(long Read,long Write)> interval=new();
    readonly Dictionary<int,(long Started,string Name)> processes=new();
    TraceEventSession session;Task processing;string scope;long omitted;long previousLoss;int observedCharacters;Stopwatch elapsed=new();
    string failure;
    bool stopping;
    internal long Generation {get;set;}
    internal bool Running=>session!=null;
    internal void Start(string path)
    {
        scope=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));failure=null;stopping=false;omitted=0;previousLoss=0;elapsed.Restart();
        // A unique system-logger session never adopts or stops another tool's trace.
        session=new TraceEventSession("Pathweft-Io-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N"),TraceEventSessionOptions.Create|TraceEventSessionOptions.NoRestartOnCreate)
        {StopOnDispose=true,BufferSizeMB=2};
        try
        {
            // TraceEvent4 starts a normal session when Source is first read.
            // Configure the kernel provider before obtaining that source.
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process|KernelTraceEventParser.Keywords.Thread|
                KernelTraceEventParser.Keywords.FileIO|KernelTraceEventParser.Keywords.FileIOInit|KernelTraceEventParser.Keywords.DiskFileIO);
            session.Source.Kernel.FileIORead+=data=>Add(data.ProcessID,data.FileName,data.IoSize,false);
            session.Source.Kernel.FileIOWrite+=data=>Add(data.ProcessID,data.FileName,data.IoSize,true);
            session.Source.Kernel.ProcessStart+=data=>processes.Remove(data.ProcessID);
            session.Source.Kernel.ProcessStop+=data=>processes.Remove(data.ProcessID);
            var source=session.Source;
            processing=Task.Run(()=>
            {
                try{source.Process();}
                catch(ObjectDisposedException)when(stopping){}
                catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){lock(gate)failure=ex.Message;}
            });
        }
        catch{session.Dispose();session=null;throw;}
    }
    internal static bool Contains(string root,string file)=>!string.IsNullOrEmpty(file)&&
        (file.Equals(root,StringComparison.OrdinalIgnoreCase)||file.StartsWith(Path.EndsInDirectorySeparator(root)?root:root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
    void Add(int pid,string file,int bytes,bool write)
    {
        if(bytes<0||pid<0)return;
        if(string.IsNullOrEmpty(file)){lock(gate)omitted++;return;}
        if(!Contains(scope,file))return;
        if(!processes.TryGetValue(pid,out var identity))
        {
            identity=(0,"Unavailable");
            try{using var process=Process.GetProcessById(pid);identity=(process.StartTime.ToUniversalTime().Ticks,process.ProcessName);}
            catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
            if(processes.Count<8192)processes[pid]=identity;
        }
        lock(gate)
        {
            var key=(pid,identity.Started,identity.Name,file);
            if(!interval.TryGetValue(key,out var value))
            {
                int characters=file.Length+identity.Name.Length;
                if(interval.Count>=4096||observedCharacters+characters>1024*1024){omitted++;return;}
                observedCharacters+=characters;
            }
            interval[key]=write?(value.Read,value.Write+bytes):(value.Read+bytes,value.Write);
        }
    }
    internal IoReport Take()
    {
        lock(gate)
        {
            double seconds=Math.Max(elapsed.Elapsed.TotalSeconds,0.001);elapsed.Restart();
            var rows=interval.Select(pair=>
            {
                // Identity is captured at observation, never resolved to a later
                // process which happens to reuse the same numeric PID.
                return new IoRow(pair.Key.Pid,pair.Key.Started,pair.Key.Name,pair.Key.File,pair.Value.Read/seconds,pair.Value.Write/seconds);
            }).OrderByDescending(row=>row.Total).ToArray();
            interval.Clear();observedCharacters=0;
            long lost=session?.Source.EventsLost??0;long total=omitted+Math.Max(0,lost-previousLoss);previousLoss=lost;omitted=0;
            return new(failure??(Running?(total>0?"Incomplete observations":"Logical file I/O · cached requests included"):"Not collecting"),rows,total,Running&&failure==null,Generation);
        }
    }
    internal void Stop()
    {
        if(session==null)return;
        stopping=true;session.Dispose();processing?.GetAwaiter().GetResult();session=null;processing=null;
        lock(gate){interval.Clear();processes.Clear();}elapsed.Reset();
    }
    public void Dispose()=>Stop();
}

internal sealed class PerformanceIoClient : IAsyncDisposable
{
    NamedPipeServerStream pipe;Process process;StreamWriter writer;Task receiving,starting,disposal;CancellationTokenSource stop=new();
    AdministratorSession.Operation administrator;CancellationTokenRegistration sessionStop;
    EventWaitHandle release;int closing;
    internal IoReport Latest {get;private set;}=new("Enable detailed I/O to collect activity",Array.Empty<IoRow>(),0,false);
    internal event Action Changed;
#if DEBUG
    internal string NativeDiagnostics {get;private set;}
    internal Func<ProcessStartInfo,Task<Process>> LaunchForVerification {get;set;}
#endif
    readonly SemaphoreSlim commands=new(1);
    long generation;TaskCompletionSource acknowledgement;
    internal bool Connected=>pipe?.IsConnected==true;
    internal Task Enable(bool elevation=true)=>starting??=Start(elevation);
    async Task Start(bool elevation)
    {
        if(Connected||closing!=0)return;
        string name="Pathweft-Io-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
        pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        release=new EventWaitHandle(false,EventResetMode.ManualReset,name+"-stop");
        using var self=Process.GetCurrentProcess();
        var launch=new ProcessStartInfo(Program.Executable)
        {UseShellExecute=elevation,Verb=elevation?"runas":"",CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
#if DEBUG
        launch.RedirectStandardError=!elevation;
#endif
        foreach(var arg in new[]{"--performance-io-worker",name,Environment.ProcessId.ToString(),self.StartTime.ToUniversalTime().Ticks.ToString()})launch.ArgumentList.Add(arg);
        try
        {
            if(elevation
#if DEBUG
                &&LaunchForVerification==null
#endif
            )
            {
                administrator=await AdministratorSession.Open(name,"io",stop.Token).ConfigureAwait(false);
                sessionStop=administrator.Stopped.Register(()=>{release.Set();stop.Cancel();});
            }
            else
            {
#if DEBUG
            process=LaunchForVerification!=null?await LaunchForVerification(launch).ConfigureAwait(false):Process.Start(launch);
#else
            process=Process.Start(launch);
#endif
            if(process==null)throw new IOException("Detailed I/O worker did not start");
            // Once connected, drain the pipe through EOF. Cancelling its read
            // on child exit can discard the final permission/failure response.
            process.EnableRaisingEvents=true;process.Exited+=(_,_)=>{if(writer==null)stop.Cancel();};
            if(process.HasExited)stop.Cancel();
            }
            await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint client)||client!=(administrator?.Pid??process.Id))throw new IOException("Detailed I/O worker identity differs");
            writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
            receiving=Receive();
        }
        catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException or ObjectDisposedException)
        {
            Latest=new(ex is System.ComponentModel.Win32Exception{NativeErrorCode:1223}?"Detailed I/O permission was cancelled":ex.Message,Array.Empty<IoRow>(),0,false);
            release.Set();pipe.Dispose();pipe=null;Changed?.Invoke();
            if(process!=null){await process.WaitForExitAsync().ConfigureAwait(false);process.Dispose();process=null;}
        }
    }
    async Task Receive()
    {
        using var reader=new StreamReader(pipe,leaveOpen:true);
        try
        {
            while(await reader.ReadLineAsync(stop.Token).ConfigureAwait(false) is {} line)
            {
                if(line.Length>8*1024*1024)throw new InvalidDataException("Detailed I/O report is too large");
                var report=JsonSerializer.Deserialize<IoReport>(line)??throw new InvalidDataException("Invalid detailed I/O report");
                if(report.Generation==generation){Latest=report;acknowledgement?.TrySetResult();Changed?.Invoke();}
            }
            if(Latest.Running){Latest=new("Detailed I/O worker exited",Array.Empty<IoRow>(),0,false);Changed?.Invoke();}
        }
        catch(Exception ex)when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {Latest=new("Detailed I/O stopped",Array.Empty<IoRow>(),0,false);Changed?.Invoke();}
        finally{acknowledgement?.TrySetException(new IOException("Detailed I/O worker exited before acknowledging its scope"));}
    }
    internal async Task SetScope(string scope,bool start)
    {
        await commands.WaitAsync().ConfigureAwait(false);
        try
        {
            if(closing==0&&writer!=null&&Connected)
            {
                var request=new IoRequest(scope,start,++generation);
                acknowledgement=new(TaskCreationOptions.RunContinuationsAsynchronously);
                Latest=new(start?"Starting collection":"Stopping collection",Array.Empty<IoRow>(),0,false,generation);Changed?.Invoke();
                await writer.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
                await acknowledgement.Task.ConfigureAwait(false);
            }
        }
        catch(IOException){Latest=new("Detailed I/O stopped",Array.Empty<IoRow>(),0,false);Changed?.Invoke();}
        finally{commands.Release();}
    }
    public ValueTask DisposeAsync()=>new(disposal??=Close());
    async Task Close()
    {
        Interlocked.Exchange(ref closing,1);release?.Set();stop.Cancel();
        if(starting!=null)await starting.ConfigureAwait(false);
        await commands.WaitAsync().ConfigureAwait(false);
        try{writer?.Dispose();pipe?.Dispose();stop.Cancel();}
        finally{commands.Release();}
        if(receiving!=null)await receiving.ConfigureAwait(false);
        if(process!=null)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
#if DEBUG
            if(process.StartInfo.RedirectStandardError)NativeDiagnostics=await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
#endif
            process.Dispose();
        }
        sessionStop.Dispose();if(administrator!=null)await administrator.DisposeAsync().ConfigureAwait(false);
        release?.Dispose();stop.Dispose();
        // Late focus notifications may still enter the managed semaphore. They
        // observe closing under the gate and never touch retired native handles.
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
}
