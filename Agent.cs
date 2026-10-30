using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using System.Security.Principal;
using Microsoft.Win32;

namespace FileExplorer;

// Optional, windowless Win+E ownership only. Folder requests use Shell verbs;
// explicit Explorer launches, virtual locations and Open/Save dialogs stay native.
public static class Agent
{
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run",RunValue="Pathweft Agent";
    public const string StateKey=@"Software\Darkaxt\Pathweft";
    static string Scope=>WindowsIdentity.GetCurrent().User.Value+TrialSuffix;
    static string StopEventName=>"Local\\Pathweft-AgentStop-"+Scope;
    static string ReadyEventName=>"Local\\Pathweft-AgentReady-"+Scope;
    static string MutexName=>"Local\\Pathweft-Agent-"+Scope;
    static string OwnerPath=>Path.Combine(Settings.Dir,"agent-owner.json");
    static string TrialSuffix=>Trial?"-"+System.IO.Hashing.XxHash32.HashToUInt32(System.Text.Encoding.UTF8.GetBytes(Settings.Profile)).ToString("x8"):"";
    static bool Trial
    {
        get
        {
#if DEBUG
            return Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s8-delivery\trial-",StringComparison.OrdinalIgnoreCase)==true && Environment.GetEnvironmentVariable("PATHWEFT_S8_AGENT_TRIAL")=="1";
#else
            return false;
#endif
        }
    }
    static string StartupKey=>Trial?@"Software\Darkaxt\Pathweft\S8Verification\Agent"+TrialSuffix:RunKey;
    sealed record Owner(int Pid,long Started,string Exe);
    static string Command(string exe)=>$"\"{exe}\" --agent";
    static Mutex single;
    static EventWaitHandle stop;
    static EventWaitHandle ready;
    static RegisteredWaitHandle waiting;
    static IntPtr hook;
    static LowLevelKeyboardProc callback;
    static bool swallowUp;

    static Process OwnedProcess()
    {
        if(!File.Exists(OwnerPath))return null;
        Owner owner;
        try{owner=JsonSerializer.Deserialize<Owner>(File.ReadAllText(OwnerPath));}
        catch(JsonException ex){throw new InvalidDataException("The Win+E owner record is invalid.",ex);}
        Process process;
        try{process=Process.GetProcessById(owner.Pid);}
        catch(ArgumentException){return null;}
        try
        {
            // Retain the native process handle before requesting shutdown. A
            // GetProcessById wrapper cannot recover an exit code after the PID
            // has disappeared unless it already owns that handle.
            _=process.Handle;
            if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=owner.Started){process.Dispose();return null;}
            if(!string.Equals(process.MainModule.FileName,owner.Exe,StringComparison.OrdinalIgnoreCase)||!string.Equals(owner.Exe,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A different Pathweft copy owns the Win+E helper.");
            return process;
        }
        catch{process.Dispose();throw;}
    }
    internal static bool IsReady
    {
        get
        {
            using var process=OwnedProcess();
            if(process==null||!EventWaitHandle.TryOpenExisting(ReadyEventName,out var signal))return false;
            using(signal)return signal.WaitOne(0)&&!process.HasExited;
        }
    }
    public static Task EnableAsync(string exe)=>Task.Run(()=>Enable(exe));
    static void Enable(string exe)
    {
        if(Settings.Profile!=null&&!Trial)throw new InvalidOperationException("A test profile cannot register Win+E.");
        using var run=Registry.CurrentUser.CreateSubKey(StartupKey);
        var previous=run.GetValue(RunValue) as string;
        if(previous!=null && previous!=Command(exe))
            throw new InvalidOperationException("Another Pathweft copy owns Win+E. Disable its integration before choosing this copy.");
        bool added=previous==null;
        Process launched=null;
        try
        {
            // Establish actual hook readiness before creating a sign-in entry.
            if(!IsReady)
            {
                using var signal=new EventWaitHandle(false,EventResetMode.ManualReset,ReadyEventName);
                var launch=new ProcessStartInfo(exe,"--agent")
                {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(exe)};
                // Do not bind a continuing sign-in helper to a build/test's
                // disposable TEMP. Normal startup inherits the user's runtime.
                if(!Trial)
                    foreach(var name in new[]{"TEMP","TMP"})
                    {
                        var value=Environment.GetEnvironmentVariable(name,EnvironmentVariableTarget.User)??Environment.GetEnvironmentVariable(name,EnvironmentVariableTarget.Machine);
                        if(value!=null)launch.Environment[name]=Environment.ExpandEnvironmentVariables(value);else launch.Environment.Remove(name);
                    }
                var process=launched=Process.Start(launch)??throw new IOException("Win+E helper did not start.");
                IntPtr[] handles={signal.SafeWaitHandle.DangerousGetHandle(),process.Handle};
                uint result=WaitForMultipleObjects(2,handles,false,uint.MaxValue);
                if(result==uint.MaxValue)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if(result!=0||!IsReady)
                {
                    process.WaitForExit();
                    throw new IOException($"Win+E helper exited before hook readiness (exit {process.ExitCode}).");
                }
            }
            run.SetValue(RunValue,Command(exe));
        }
        catch
        {
            if(launched!=null&&!launched.HasExited&&EventWaitHandle.TryOpenExisting(StopEventName,out var signal))
            {using(signal)signal.Set();launched.WaitForExit();}
            if(added && run.GetValue(RunValue) as string==Command(exe))run.DeleteValue(RunValue,false);
            throw;
        }
        finally{launched?.Dispose();}
    }

    public static Task DisableAsync()=>Task.Run(Disable);
    internal static bool OwnsStartup
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(StartupKey);return key?.GetValue(RunValue) as string==Command(Environment.ProcessPath);}
    }
    internal static void StopForUpdate(){if(OwnsStartup)StopOwned();}
    static void StopOwned()
    {
        using var process=OwnedProcess();
        if(process==null)return;
        if(!EventWaitHandle.TryOpenExisting(StopEventName,out var signal))throw new IOException("The Win+E helper has no shutdown signal.");
        using(signal)signal.Set();
        process.WaitForExit();
        if(process.ExitCode!=0)throw new IOException($"The Win+E helper stopped with exit {process.ExitCode}.");
    }
    public static void Disable()
    {
        if(Settings.Profile!=null&&!Trial)throw new InvalidOperationException("A test profile cannot change Win+E.");
        using var run=Registry.CurrentUser.OpenSubKey(StartupKey,true);
        var current=run?.GetValue(RunValue) as string;
        if(current!=null && current!=Command(Environment.ProcessPath))
            throw new InvalidOperationException("Another Pathweft copy owns Win+E; its settings were left unchanged.");
        StopOwned();
        run?.DeleteValue(RunValue,false);
    }

    public static int Run(System.Windows.Application app)
    {
        Settings.Load();
        if(Settings.Profile!=null&&!Trial || !Settings.TakeWinE)return 0;
        single=new Mutex(true,MutexName,out bool first);
        if(!first){single.Dispose();single=null;return 0;}
        stop=new EventWaitHandle(false,EventResetMode.AutoReset,StopEventName);
        ready=new EventWaitHandle(false,EventResetMode.ManualReset,ReadyEventName);
        callback=Hook;
        hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero){ready.Dispose();stop.Dispose();single.ReleaseMutex();single.Dispose();return 1;}
        waiting=ThreadPool.RegisterWaitForSingleObject(stop,(_,_)=>app.Dispatcher.BeginInvoke(()=>app.Shutdown()),null,-1,true);
        app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
        app.Exit+=(_,_)=>
        {
            ready.Reset();UnhookWindowsHookEx(hook);waiting.Unregister(null);stop.Dispose();ready.Dispose();
            File.Delete(OwnerPath);
            single.ReleaseMutex();single.Dispose();Log("stopped");
        };
        Directory.CreateDirectory(Settings.Dir);
        using(var self=Process.GetCurrentProcess())Settings.WriteAllLines(OwnerPath,new[]{JsonSerializer.Serialize(new Owner(self.Id,self.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath))});
        Log("started");ready.Set();
        return -1; // ownership lasts until app.Exit, not until this method returns
    }

    internal static bool Matches(bool windows,bool control,bool alt,bool shift)=>windows&&!control&&!alt&&!shift;
    static IntPtr Hook(int code,IntPtr message,IntPtr data)
    {
        if(code>=0 && Marshal.ReadInt32(data)==0x45)
        {
            bool down=message==(IntPtr)0x100||message==(IntPtr)0x104;
            if(down && Matches(Held(0x5b)||Held(0x5c),Held(0x11),Held(0x12),Held(0x10)))
            {
                if(!swallowUp)
                {
                    swallowUp=true;TapUnassigned();AllowSetForegroundWindow(-1);
                    ThreadPool.QueueUserWorkItem(_=>
                    {
                        if(Program.Running&&Program.HandOff(""))return;
                        try{using var p=Process.Start(new ProcessStartInfo(Environment.ProcessPath,"--front"){UseShellExecute=false});}
                        catch(System.ComponentModel.Win32Exception ex){Log("launch failed: "+ex.NativeErrorCode);}
                    });
                }
                return (IntPtr)1;
            }
            if(!down&&swallowUp){swallowUp=false;return (IntPtr)1;}
        }
        return CallNextHookEx(hook,code,message,data);
    }
    static bool Held(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
    static void TapUnassigned()
    {
        var input=new INPUT[2];input[0].type=input[1].type=1;
        input[0].key=input[1].key=0xe8;input[1].flags=2;
        SendInput(2,input,Marshal.SizeOf<INPUT>());
    }
    static void Log(string state)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);var path=Path.Combine(Settings.Dir,"agent.log");
            if(File.Exists(path)&&new FileInfo(path).Length>65536)File.WriteAllText(path,"");
            File.AppendAllText(path,$"{DateTime.UtcNow:O} pid={Environment.ProcessId} {state}\n");
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ }
    }
    [StructLayout(LayoutKind.Explicit,Size=40)]struct INPUT
    {
        [FieldOffset(0)]public uint type;[FieldOffset(8)]public ushort key;
        [FieldOffset(12)]public uint flags;
    }
    delegate IntPtr LowLevelKeyboardProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,LowLevelKeyboardProc fn,IntPtr module,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")]static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,bool all,uint milliseconds);
}
