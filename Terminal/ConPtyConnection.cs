using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Terminal.Wpf;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// BROWSE-08: one PowerShell session behind a pseudoconsole, feeding the Windows Terminal WPF control.
// conpty.dll (Microsoft.Windows.Console.ConPTY, MIT) is preferred: it starts the packaged OpenConsole.exe,
// which passes OSC sequences through. Without it the inbox kernel32 pseudoconsole (conhost) is used.
// The shell's OSC 9;9 (folder) and OSC 133 (prompt/command marks) are read on the way to the control and
// passed through unchanged. The process lives in a kill-on-close job: closing the session ends its tree.
internal sealed class ConPtyConnection : ITerminalConnection, IDisposable
{
    public event EventHandler<TerminalOutputEventArgs> TerminalOutput;
    /// The shell reported its current folder (OSC 9;9), raised on the reader thread.
    internal event Action<string> FolderReported;
    /// True: the shell sits at a prompt with nothing typed yet (OSC 133 B, before any input or C). Reader or UI thread.
    internal event Action<bool> EmptyPromptChanged;
    /// The shell process ended; the exit code when known. Raised on the reader thread.
    internal event Action<int?> Exited;

    readonly string folder;
    short rows=30,columns=120;
    IntPtr console,process,job;
    SafeFileHandle inputWrite,outputRead;
    FileStream input;
    Thread reader;
    readonly object gate=new();
    bool closed,started,atPrompt,typed,shellIntegration;
    internal string Shell {get;private set;}
    internal bool ShellIntegrationSeen=>shellIntegration;
    internal bool EmptyPrompt{get{lock(gate)return atPrompt&&!typed;}}

    internal ConPtyConnection(string folder){this.folder=folder;}

    public void Start()
    {
        lock(gate){if(started||closed)return;started=true;}
        Shell=FindShell();
        if(!CreatePipe(out var inputRead,out inputWrite,IntPtr.Zero,0)||!CreatePipe(out outputRead,out var outputWrite,IntPtr.Zero,0))
            throw new IOException("Could not create the terminal pipes",Marshal.GetLastWin32Error());
        int hr=Pty.Create(new Coord{X=columns,Y=rows},inputRead,outputWrite,0,out console);
        if(hr!=0)throw new IOException("Could not create the pseudoconsole",hr);
        try{StartShell();}
        finally{inputRead.Dispose();outputWrite.Dispose();} // the pseudoconsole holds its own copies
        input=new FileStream(inputWrite,FileAccess.Write,1,false);
        reader=new Thread(Read){IsBackground=true,Name="Pathweft terminal output"};reader.Start();
    }

    void StartShell()
    {
        string arguments=$"-NoLogo -NoExit -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(ShellIntegrationScript))}";
        string command=$"\"{Shell}\" {arguments}";
        var size=IntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);
        var list=Marshal.AllocHGlobal(size);
        try
        {
            if(!InitializeProcThreadAttributeList(list,1,0,ref size))throw new IOException("Could not prepare the shell start",Marshal.GetLastWin32Error());
            if(!UpdateProcThreadAttribute(list,0,(IntPtr)ProcThreadAttributePseudoConsole,console,(IntPtr)IntPtr.Size,IntPtr.Zero,IntPtr.Zero))
                throw new IOException("Could not attach the pseudoconsole",Marshal.GetLastWin32Error());
            var start=new StartupInfoEx{Attributes=list};start.Startup.Size=Marshal.SizeOf<StartupInfoEx>();
            string directory=Directory.Exists(folder)?folder:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(!CreateProcessW(null,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,ExtendedStartupInfoPresent|CreateUnicodeEnvironment|CreateSuspended,IntPtr.Zero,directory,ref start,out var info))
                throw new IOException("Could not start "+Path.GetFileName(Shell),Marshal.GetLastWin32Error());
            process=info.Process;
            job=CreateJobObjectW(IntPtr.Zero,null);
            var limits=new ExtendedLimitInformation();limits.Basic.LimitFlags=JobObjectLimitKillOnJobClose;
            int length=Marshal.SizeOf<ExtendedLimitInformation>();var buffer=Marshal.AllocHGlobal(length);
            try{Marshal.StructureToPtr(limits,buffer,false);SetInformationJobObject(job,JobObjectExtendedLimitInformationClass,buffer,(uint)length);}
            finally{Marshal.FreeHGlobal(buffer);}
            // Owned before its first instruction: a shell that starts children cannot outlive the session.
            if(job==IntPtr.Zero||!AssignProcessToJobObject(job,process)){TerminateProcess(process,1);CloseHandle(info.Thread);throw new IOException("Could not own the shell process",Marshal.GetLastWin32Error());}
            ResumeThread(info.Thread);CloseHandle(info.Thread);
        }
        finally{DeleteProcThreadAttributeList(list);Marshal.FreeHGlobal(list);}
    }

    void Read()
    {
        var decoder=new UTF8Encoding(false).GetDecoder();var bytes=new byte[16384];var chars=new char[16385];
        try
        {
            using var output=new FileStream(outputRead,FileAccess.Read,1,false);
            int count;
            while((count=output.Read(bytes,0,bytes.Length))>0)
            {
                int n=decoder.GetChars(bytes,0,count,chars,0);if(n==0)continue;
                var text=new string(chars,0,n);
                Scan(text);
                TerminalOutput?.Invoke(this,new TerminalOutputEventArgs(text));
            }
        }
        catch(Exception ex)when(ex is IOException or ObjectDisposedException){}
        int? code=null;
        if(process!=IntPtr.Zero&&WaitForSingleObject(process,2000)==0&&GetExitCodeProcess(process,out uint exit))code=(int)exit;
        Exited?.Invoke(code);
    }

    // OSC state across reads: ESC ] body (BEL | ESC \). Bodies are capped; other sequences are ignored.
    readonly StringBuilder osc=new();int oscState; // 0 text, 1 after ESC, 2 in OSC, 3 ESC inside OSC
    void Scan(string text)
    {
        foreach(char c in text)
        {
            switch(oscState)
            {
                case 0: if(c=='\x1b')oscState=1;break;
                case 1: if(c==']'){osc.Clear();oscState=2;}else oscState=c=='\x1b'?1:0;break;
                case 2:
                    if(c=='\a'){Osc(osc.ToString());oscState=0;}
                    else if(c=='\x1b')oscState=3;
                    else if(osc.Length<4096)osc.Append(c);
                    else oscState=0;
                    break;
                case 3: if(c=='\\')Osc(osc.ToString());oscState=c=='\x1b'?1:0;break;
            }
        }
    }
    void Osc(string body)
    {
        if(body.StartsWith("9;9;",StringComparison.Ordinal))
        {
            string path=body[4..].Trim();if(path.Length>=2&&path[0]=='"'&&path[^1]=='"')path=path[1..^1];
            shellIntegration=true;if(path.Length>0)FolderReported?.Invoke(path);
            return;
        }
        if(!body.StartsWith("133;",StringComparison.Ordinal)||body.Length<5)return;
        shellIntegration=true;
        bool before=EmptyPrompt;
        lock(gate)
        {
            switch(body[4])
            {
                case 'B': atPrompt=true;typed=false;break;   // prompt drawn: input starts here
                case 'A' or 'C' or 'D': atPrompt=false;break; // prompt starting, command running or finished
            }
        }
        if(EmptyPrompt!=before)EmptyPromptChanged?.Invoke(!before);
    }

    /// The user's keys, from the control. Anything typed at the prompt makes it non-empty until the next prompt.
    public void WriteInput(string data)
    {
        if(string.IsNullOrEmpty(data))return;
        bool before=EmptyPrompt;lock(gate)if(atPrompt)typed=true;
        Write(data);
        if(before&&!EmptyPrompt)EmptyPromptChanged?.Invoke(false);
    }

    /// A command line from Pathweft itself (folder follow). Sent only at an empty prompt; false when it was not.
    internal bool SendCommand(string line)
    {
        lock(gate){if(!atPrompt||typed||closed)return false;atPrompt=false;}
        Write(line+"\r");EmptyPromptChanged?.Invoke(false);return true;
    }

    void Write(string data)
    {
        try{lock(gate){if(closed||input==null)return;var bytes=Encoding.UTF8.GetBytes(data);input.Write(bytes,0,bytes.Length);input.Flush();}}
        catch(Exception ex)when(ex is IOException or ObjectDisposedException){}
    }

    public void Resize(uint rows,uint columns)
    {
        if(rows==0||columns==0)return;
        lock(gate)
        {
            this.rows=(short)Math.Min(rows,short.MaxValue);this.columns=(short)Math.Min(columns,short.MaxValue);
            if(console!=IntPtr.Zero&&!closed)Pty.Resize(console,new Coord{X=this.columns,Y=this.rows});
        }
    }

    public void Close()=>Dispose();
    public void Dispose()
    {
        lock(gate){if(closed)return;closed=true;}
        // The job ends the shell and every child; closing the pseudoconsole then lets the reader drain to EOF.
        if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
        try{input?.Dispose();}catch(IOException){}
        if(console!=IntPtr.Zero){Pty.Close(console);console=IntPtr.Zero;}
        if(reader!=null&&reader.ManagedThreadId!=Environment.CurrentManagedThreadId)reader.Join(3000);
        outputRead?.Dispose();
        if(process!=IntPtr.Zero){CloseHandle(process);process=IntPtr.Zero;}
    }

    /// PowerShell 7 when installed, else Windows PowerShell.
    internal static string FindShell()
    {
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            try{var candidate=Path.Combine(Environment.ExpandEnvironmentVariables(dir),"pwsh.exe");if(File.Exists(candidate))return candidate;}
            catch(ArgumentException){}
        }
        var programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach(var version in new[]{"7","7-preview"})
        {var candidate=Path.Combine(programFiles,"PowerShell",version,"pwsh.exe");if(File.Exists(candidate))return candidate;}
        return Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
    }

    /// Shell integration installed after the user's profile: the existing prompt is wrapped, not replaced.
    /// Prompt: 133;D (previous command's end) 133;A, 9;9 with the FileSystem folder, the user's prompt, 133;B.
    /// Enter (PSReadLine): 133;C before output. Pathweft's own folder-follow lines stay out of history.
    internal const string FollowMarker="# pathweft-follow";
    internal const string ShellIntegrationScript = """
        $global:__pathweftPrompt = $function:prompt
        $global:__pathweftStarted = $false
        function global:prompt {
            $ok = $?
            $e = [char]27
            $out = ''
            if ($global:__pathweftStarted) { $out += "$e]133;D;$(if ($ok) { 0 } else { 1 })`a" }
            $global:__pathweftStarted = $true
            $out += "$e]133;A`a"
            $location = $ExecutionContext.SessionState.Path.CurrentLocation
            if ($location.Provider.Name -eq 'FileSystem') { $out += "$e]9;9;`"$($location.ProviderPath)`"`a" }
            $text = if ($global:__pathweftPrompt) { & $global:__pathweftPrompt } else { "PS $($location)> " }
            "$out$text$e]133;B`a"
        }
        if (Get-Module PSReadLine) {
            Set-PSReadLineKeyHandler -Key Enter -BriefDescription 'Pathweft accept line' -ScriptBlock {
                [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
                [Console]::Write("$([char]27)]133;C`a")
            }
            $global:__pathweftHistory = (Get-PSReadLineOption).AddToHistoryHandler
            Set-PSReadLineOption -AddToHistoryHandler {
                param($line)
                if ($line.EndsWith('# pathweft-follow')) { return $false }
                if ($global:__pathweftHistory) { return & $global:__pathweftHistory $line }
                return $true
            }
        }
        """;

    // conpty.dll when present beside the app (packaged OpenConsole), else the inbox kernel32 pseudoconsole.
    static class Pty
    {
        delegate int CreateFn(Coord size,SafeFileHandle input,SafeFileHandle output,uint flags,out IntPtr console);
        delegate int ResizeFn(IntPtr console,Coord size);
        delegate void CloseFn(IntPtr console);
        static readonly CreateFn create;static readonly ResizeFn resize;static readonly CloseFn close;
        internal static readonly bool Packaged;
        static Pty()
        {
            if(NativeLibrary.TryLoad("conpty.dll",typeof(ConPtyConnection).Assembly,DllImportSearchPath.AssemblyDirectory|DllImportSearchPath.ApplicationDirectory,out var library)
               &&NativeLibrary.TryGetExport(library,"ConptyCreatePseudoConsole",out var c)&&NativeLibrary.TryGetExport(library,"ConptyResizePseudoConsole",out var r)&&NativeLibrary.TryGetExport(library,"ConptyClosePseudoConsole",out var x))
            {create=Marshal.GetDelegateForFunctionPointer<CreateFn>(c);resize=Marshal.GetDelegateForFunctionPointer<ResizeFn>(r);close=Marshal.GetDelegateForFunctionPointer<CloseFn>(x);Packaged=true;}
            else{create=CreatePseudoConsole;resize=ResizePseudoConsole;close=ClosePseudoConsole;}
        }
        internal static int Create(Coord size,SafeFileHandle input,SafeFileHandle output,uint flags,out IntPtr console)=>create(size,input,output,flags,out console);
        internal static int Resize(IntPtr console,Coord size)=>resize(console,size);
        internal static void Close(IntPtr console)=>close(console);
    }

    const uint ExtendedStartupInfoPresent=0x80000,CreateUnicodeEnvironment=0x400,CreateSuspended=0x4;
    const int ProcThreadAttributePseudoConsole=0x20016;
    const uint JobObjectLimitKillOnJobClose=0x2000;const int JobObjectExtendedLimitInformationClass=9;
    [StructLayout(LayoutKind.Sequential)] struct Coord{public short X,Y;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct StartupInfo{public int Size;public string Reserved,Desktop,Title;public int X,Y,XSize,YSize,XCountChars,YCountChars,FillAttribute,Flags;public short ShowWindow,Reserved2;public IntPtr Reserved2Pointer,StdInput,StdOutput,StdError;}
    [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx{public StartupInfo Startup;public IntPtr Attributes;}
    [StructLayout(LayoutKind.Sequential)] struct ProcessInformation{public IntPtr Process,Thread;public int ProcessId,ThreadId;}
    [StructLayout(LayoutKind.Sequential)] struct BasicLimitInformation{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)] struct IoCounters{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimitInformation{public BasicLimitInformation Basic;public IoCounters Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}

    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,IntPtr attributes,int size);
    [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(Coord size,SafeFileHandle input,SafeFileHandle output,uint flags,out IntPtr console);
    [DllImport("kernel32.dll")] static extern int ResizePseudoConsole(IntPtr console,Coord size);
    [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    static extern bool CreateProcessW(string application,StringBuilder commandLine,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfoEx startup,out ProcessInformation information);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObjectW(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int infoClass,IntPtr info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
