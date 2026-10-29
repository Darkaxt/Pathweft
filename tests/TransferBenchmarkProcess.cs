using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

// Measurement only. REA owns spawning/presentation; retain the exact process
// handle before resume and wait for its real exit without a cancellation timer.
public static class TransferBenchmarkProcess
{
    [StructLayout(LayoutKind.Sequential)] struct MemoryInfo
    {
        public uint size,faults; public UIntPtr peakWorkingSet,workingSet,peakPaged,paged,peakNonPaged,nonPaged,pagefile,peakPagefile,privateBytes;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
    [DllImport("psapi.dll",SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr process,ref MemoryInfo info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint exitCode);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetCompressedFileSizeW(string path,out uint high);
    public static long ReportedStorageBytes(string path)
    {
        uint low=GetCompressedFileSizeW(path,out uint high);
        if(low==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
        return ((long)high<<32)|low;
    }
    public static object InspectNativeProcess(uint pid)
    {
        IntPtr process=OpenProcess(0x101000,false,pid);
        if(process==IntPtr.Zero)return new{pid,opened=false,error=Marshal.GetLastWin32Error()};
        try
        {
            if(!GetExitCodeProcess(process,out uint code)||!GetProcessTimes(process,out long created,out long exited,out long kernel,out long user))throw new Win32Exception(Marshal.GetLastWin32Error());
            return new{pid,opened=true,exitCode=code,created,exited,kernel,user,waitState=WaitForSingleObject(process,0)};
        }
        finally{CloseHandle(process);}
    }
    public static void StopExactFailedProbe(uint pid,long expectedCreated)
    {
        IntPtr process=OpenProcess(0x101001,false,pid);
        if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if(!GetProcessTimes(process,out long created,out _,out _,out _))throw new Win32Exception(Marshal.GetLastWin32Error());
            if(created!=expectedCreated)throw new InvalidOperationException("The failed probe's native creation identity changed.");
            if(WaitForSingleObject(process,0)==0)return;
            if(!TerminateProcess(process,90))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally{CloseHandle(process);}
    }
    public sealed class Result
    {
        public uint Pid,ExitCode;
        public ulong PeakWorkingSetBytes,PeakPagefileBytes,PrivateBytesAtExit;
        public bool ProcessExitObserved;
        public long CreatedFileTime,ExitedFileTime,Kernel100ns,User100ns;
    }
    public static Result Observe(uint pid,string readyReceipt)
    {
        IntPtr process=OpenProcess(0x100410,false,pid);
        if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            File.WriteAllText(readyReceipt,"pid="+pid+"\nhandle-open=true\n");
            if(WaitForSingleObject(process,0xffffffff)!=0)throw new Win32Exception(Marshal.GetLastWin32Error());
            var result=new Result{Pid=pid,ProcessExitObserved=true};
            if(!GetExitCodeProcess(process,out result.ExitCode)||!GetProcessTimes(process,out result.CreatedFileTime,out result.ExitedFileTime,out result.Kernel100ns,out result.User100ns))throw new Win32Exception(Marshal.GetLastWin32Error());
            var memory=new MemoryInfo{size=(uint)Marshal.SizeOf<MemoryInfo>()};
            if(!GetProcessMemoryInfo(process,ref memory,memory.size))throw new Win32Exception(Marshal.GetLastWin32Error());
            result.PeakWorkingSetBytes=memory.peakWorkingSet.ToUInt64();result.PeakPagefileBytes=memory.peakPagefile.ToUInt64();result.PrivateBytesAtExit=memory.privateBytes.ToUInt64();return result;
        }
        finally{CloseHandle(process);}
    }
}
