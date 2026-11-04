using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer.Preview;

// One preview owns one worker and one kill-on-close job. The child waits for its
// first request, so it cannot load a renderer before job assignment succeeds.
internal sealed class OfficeWorkerProcess:IAsyncDisposable
{
    readonly Process process;
    readonly SafeFileHandle job;
    readonly SafeFileHandle completionPort;
    readonly Task empty;
    readonly SemaphoreSlim serial=new(1,1);
    readonly Task<string> errors;
    readonly CancellationToken lifetime;
    readonly bool pdfPayload;
    readonly CancellationTokenRegistration cancellation;
    bool disposed;
    internal int ProcessId=>process.Id;

    internal OfficeWorkerProcess(string directory,CancellationToken lifetime,bool libreOffice=false)
    {
        this.lifetime=lifetime;pdfPayload=libreOffice;lifetime.ThrowIfCancellationRequested();
        job=CreateJobObject(IntPtr.Zero,null);
        if(job.IsInvalid)throw new System.ComponentModel.Win32Exception();
        completionPort=CreateIoCompletionPort(new IntPtr(-1),IntPtr.Zero,UIntPtr.Zero,1);
        if(completionPort.IsInvalid){job.Dispose();throw new System.ComponentModel.Win32Exception();}
        try
        {
        var limits=new ExtendedLimits {Basic=new BasicLimits {Flags=0x2000}}; // KILL_ON_JOB_CLOSE
        var association=new CompletionAssociation{Key=IntPtr.Zero,Port=completionPort.DangerousGetHandle()};
        if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssociateCompletionPort(job,7,ref association,(uint)Marshal.SizeOf<CompletionAssociation>()))
        {job.Dispose();completionPort.Dispose();throw new System.ComponentModel.Win32Exception();}
        string program=Path.Combine(AppContext.BaseDirectory,"Office","LibreOffice","program");
        var start=new ProcessStartInfo(libreOffice?Path.Combine(program,"python.exe"):Program.Executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            WorkingDirectory=directory,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8};
        if(libreOffice)
        {
            start.ArgumentList.Add("-B");start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Office","libreoffice-worker.py"));
            start.Environment["PYTHONHOME"]=Directory.GetDirectories(program,"python-core-*").Single();
            start.Environment["PYTHONPATH"]=program;start.Environment["PYTHONNOUSERSITE"]="1";start.Environment["PYTHONDONTWRITEBYTECODE"]="1";
            start.Environment["UNO_PATH"]=program;start.Environment["URE_BOOTSTRAP"]="vnd.sun.star.pathname:"+Path.Combine(program,"fundamental.ini");
        }
        else start.ArgumentList.Add("--office-preview-worker");
        start.Environment["TEMP"]=start.Environment["TMP"]=directory;
        // No donor preference/password state or application-controlled native services.
        foreach(string name in new[]{"WORDCRAFT_PASSWORD","GRIDCRAFT_PASSWORD","DECKCRAFT_PASSWORD"})start.Environment.Remove(name);
            process=Process.Start(start)??throw new IOException("Office preview worker did not start.");
            errors=DrainErrors(process.StandardError);
            if(!AssignProcessToJobObject(job,process.Handle))
            {
                int code=Marshal.GetLastWin32Error();process.StandardInput.Close();process.WaitForExit();process.Dispose();
                throw new System.ComponentModel.Win32Exception(code,"Office preview worker ownership failed.");
            }
            empty=Task.Run(ObserveEmpty);
            cancellation=lifetime.Register(Stop);
        }
        catch{job.Dispose();completionPort.Dispose();throw;}
    }

    internal async Task<(OfficeWorker.Reply Reply,byte[] Pixels)> Request(OfficeWorker.Request request,CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();lifetime.ThrowIfCancellationRequested();
            if(disposed)throw new ObjectDisposedException(nameof(OfficeWorkerProcess));
            await OfficeWorker.Write(process.StandardInput.BaseStream,request);
            string header=await OfficeWorker.ReadHeader(process.StandardOutput.BaseStream);
            if(header==null)
            {
                await process.WaitForExitAsync();lifetime.ThrowIfCancellationRequested();
                string details=await errors;
                throw new IOException($"Office preview worker exited (0x{process.ExitCode:X8}). "+details);
            }
            var reply=JsonSerializer.Deserialize<OfficeWorker.Reply>(header,OfficeWorker.Json)??throw new IOException("Invalid Office worker response.");
            if(!reply.Ok)
            {
                if(reply.Code=="policy")throw new OfficePolicyException(reply.Error??"Office input exceeds preview policy.");
                throw new IOException(reply.Error??"Office rendering failed.");
            }
            byte[] pixels=Array.Empty<byte>();
            if(request.Action=="render")
            {
                if(pdfPayload)
                {if(reply.Bytes is <1 or >64*1024*1024)throw new IOException("Invalid fallback display list response.");}
                else if(reply.Width is <1 or >4096||reply.Height is <1 or >4096||reply.Bytes!=(long)reply.Width*reply.Height*4)throw new IOException("Invalid Office raster response.");
                pixels=new byte[reply.Bytes];await process.StandardOutput.BaseStream.ReadExactlyAsync(pixels);
            }
            // Navigation cancellation discards this complete response; it cannot
            // leave unread pixels that would corrupt the next request's framing.
            token.ThrowIfCancellationRequested();lifetime.ThrowIfCancellationRequested();
            return(reply,pixels);
        }
        catch(Exception)when(lifetime.IsCancellationRequested)
        {throw new OperationCanceledException(lifetime);}
        finally{serial.Release();}
    }

    static async Task<string> DrainErrors(StreamReader reader)
    {
        var text=new StringBuilder();var buffer=new char[4096];int read;
        while((read=await reader.ReadAsync(buffer))>0){int keep=Math.Min(read,(64<<10)-text.Length);if(keep>0)text.Append(buffer,0,keep);}
        return text.ToString();
    }

    void Stop()
    {
        if(!job.IsClosed&&!TerminateJobObject(job,0xC000013A))throw new System.ComponentModel.Win32Exception();
    }
    void ObserveEmpty()
    {
        for(;;)
        {
            if(!QueryInformationJobObject(job,1,out Accounting accounting,(uint)Marshal.SizeOf<Accounting>(),IntPtr.Zero))throw new System.ComponentModel.Win32Exception();
            if(accounting.ActiveProcesses==0)return;
            // Job completion notifications are not guaranteed by Windows, and a
            // nested child's ZERO notification does not imply the parent is empty.
            // This observation window only rechecks actual state if a wakeup is
            // lost; its expiry never cancels work or classifies it as finished.
            if(!GetQueuedCompletionStatus(completionPort,out _,out _,out _,1000)&&Marshal.GetLastWin32Error()!=258)throw new System.ComponentModel.Win32Exception();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;
        // Also handles a worker fault while holding the request. Closing the exact
        // job signals cancellation; the observed native exit precedes fallback.
        cancellation.Dispose();Stop();
        await process.WaitForExitAsync();
        await empty;
        await serial.WaitAsync();
        try{await errors;process.Dispose();}
        finally{job.Dispose();completionPort.Dispose();serial.Release();serial.Dispose();}
    }

    [StructLayout(LayoutKind.Sequential)]struct BasicLimits
    {
        internal long ProcessTime,JobTime;internal uint Flags;internal nuint MinimumWorkingSet,MaximumWorkingSet;
        internal uint ActiveProcessLimit;internal nuint Affinity;internal uint PriorityClass,SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]struct IoCounters{internal ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct ExtendedLimits
    {
        internal BasicLimits Basic;internal IoCounters Io;internal nuint ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]struct CompletionAssociation{internal IntPtr Key,Port;}
    [StructLayout(LayoutKind.Sequential)]struct Accounting
    {internal long TotalUserTime,TotalKernelTime,PeriodUserTime,PeriodKernelTime;internal uint PageFaults,TotalProcesses,ActiveProcesses,TerminatedProcesses;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref ExtendedLimits limits,uint length);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool TerminateJobObject(SafeFileHandle job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeFileHandle CreateIoCompletionPort(IntPtr file,IntPtr existing,UIntPtr key,uint threads);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetQueuedCompletionStatus(SafeFileHandle port,out uint bytes,out UIntPtr key,out IntPtr overlapped,uint milliseconds);
    [DllImport("kernel32.dll",EntryPoint="SetInformationJobObject",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AssociateCompletionPort(SafeFileHandle job,int info,ref CompletionAssociation association,uint length);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool QueryInformationJobObject(SafeFileHandle job,int info,out Accounting accounting,uint length,IntPtr returnedLength);
}
