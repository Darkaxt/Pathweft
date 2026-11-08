using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// The existing OfficeWorkerProcess job/exit contract, scoped to the PowerShell
// maintenance boundary. The script waits on stdin until native job assignment.
internal static class MaintenanceProcess
{
    internal sealed record Result(int ExitCode,string Output,string Error);
    internal static async Task<Result> Run(ProcessStartInfo launch,CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var job=CreateJobObject(IntPtr.Zero,null);
        using var port=CreateIoCompletionPort(new IntPtr(-1),IntPtr.Zero,UIntPtr.Zero,1);
        if(job.IsInvalid||port.IsInvalid)throw new Win32Exception();
        var limits=new ExtendedLimits{Basic=new(){Flags=0x2000}};
        var association=new CompletionAssociation{Port=port.DangerousGetHandle()};
        if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssociateCompletionPort(job,7,ref association,(uint)Marshal.SizeOf<CompletionAssociation>()))throw new Win32Exception();
        using var process=Process.Start(launch)??throw new IOException("Windows maintenance did not start");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!AssignProcessToJobObject(job,process.Handle))
        {int code=Marshal.GetLastWin32Error();process.StandardInput.Close();await process.WaitForExitAsync();throw new Win32Exception(code,"Maintenance process ownership failed");}
        using var cancellation=cancel.Register(()=>TerminateJobObject(job,0xC000013A));
        try
        {
            await process.StandardInput.WriteLineAsync("owned");process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
        finally
        {
            // Retire descendants even if the shell failed after spawning one.
            TerminateJobObject(job,0xC000013A);
            await Task.Run(()=>
            {
                for(;;)
                {
                    if(!QueryInformationJobObject(job,1,out Accounting state,(uint)Marshal.SizeOf<Accounting>(),IntPtr.Zero))throw new Win32Exception();
                    if(state.ActiveProcesses==0)break;
                    // Diagnostic recheck for a dropped Windows job notification;
                    // expiry never cancels or classifies the workload as complete.
                    if(!GetQueuedCompletionStatus(port,out _,out _,out _,1000)&&Marshal.GetLastWin32Error()!=258)throw new Win32Exception();
                }
            });
        }
        cancel.ThrowIfCancellationRequested();return new(process.ExitCode,await output,await error);
    }
    [StructLayout(LayoutKind.Sequential)]struct BasicLimits{internal long ProcessTime,JobTime;internal uint Flags;internal nuint MinimumWorkingSet,MaximumWorkingSet;internal uint ActiveProcessLimit;internal nuint Affinity;internal uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]struct IoCounters{internal ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]struct ExtendedLimits{internal BasicLimits Basic;internal IoCounters Io;internal nuint ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [StructLayout(LayoutKind.Sequential)]struct CompletionAssociation{internal IntPtr Key,Port;}
    [StructLayout(LayoutKind.Sequential)]struct Accounting{internal long TotalUserTime,TotalKernelTime,PeriodUserTime,PeriodKernelTime;internal uint PageFaults,TotalProcesses,ActiveProcesses,TerminatedProcesses;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeFileHandle CreateIoCompletionPort(IntPtr file,IntPtr existing,UIntPtr key,uint concurrency);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref ExtendedLimits limits,uint length);
    [DllImport("kernel32.dll",EntryPoint="SetInformationJobObject",SetLastError=true)]static extern bool AssociateCompletionPort(SafeFileHandle job,int info,ref CompletionAssociation association,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(SafeFileHandle job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryInformationJobObject(SafeFileHandle job,int info,out Accounting state,uint length,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetQueuedCompletionStatus(SafeFileHandle port,out uint bytes,out UIntPtr key,out IntPtr overlapped,uint milliseconds);
}
