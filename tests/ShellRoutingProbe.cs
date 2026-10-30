// Windows PowerShell 5.1 and PowerShell 7 load this same probe, qualifying the
// 32-bit and 64-bit caller boundaries. This is test code, not an injected module.
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public static class ShellRoutingProbe
{
    public static int Bits { get { return IntPtr.Size*8; } }
    public static void Open(string target,string verb)
    {
        var info=new ShellExecuteInfo();info.Size=Marshal.SizeOf(info);
        info.Mask=0x00000100|0x00000400; // wait for DDE and report errors here
        info.File=target;info.Verb=verb;info.Show=0;
        if(!ShellExecuteEx(ref info))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static int SelectFile(string folder,string name)
    {
        IntPtr parent=IntPtr.Zero,child=IntPtr.Zero,absolute=IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(folder,IntPtr.Zero,out parent,0,IntPtr.Zero));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(System.IO.Path.Combine(folder,name),IntPtr.Zero,out absolute,0,IntPtr.Zero));
            child=ILFindLastID(absolute);
            return SHOpenFolderAndSelectItems(parent,1,new[]{child},0);
        }
        finally{if(parent!=IntPtr.Zero)Marshal.FreeCoTaskMem(parent);if(absolute!=IntPtr.Zero)Marshal.FreeCoTaskMem(absolute);}
    }
    public static void WinE()
    {
        Input[] keys=new Input[4];for(int i=0;i<4;i++)keys[i].Type=1;
        keys[0].Key=0x5b;keys[1].Key=0x45;keys[2].Key=0x45;keys[2].Flags=2;keys[3].Key=0x5b;keys[3].Flags=2;
        if(SendInput(4,keys,Marshal.SizeOf(typeof(Input)))!=4)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public sealed class Observer:IDisposable
    {
        readonly ManualResetEvent ready=new ManualResetEvent(false);
        readonly ConcurrentQueue<string> windows=new ConcurrentQueue<string>();
        readonly Thread thread;
        public string TargetTitle;
        public readonly ManualResetEvent ExplorerReady=new ManualResetEvent(false);
        public long TargetWindow;
        uint nativeThread;IntPtr hook;WinEvent callback;
        public Observer()
        {
            thread=new Thread(Run);thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne();
            if(hook==IntPtr.Zero)throw new InvalidOperationException("Window observation was not installed.");
        }
        void Run()
        {
            nativeThread=GetCurrentThreadId();
            // Create this thread's native queue before advertising readiness.
            Message ignored;PeekMessage(out ignored,IntPtr.Zero,0,0,0);
            callback=Shown;hook=SetWinEventHook(0x8002,0x800C,IntPtr.Zero,callback,0,0,0);ready.Set();
            if(hook!=IntPtr.Zero){Application.Run();UnhookWinEvent(hook);}
        }
        void Shown(IntPtr handle,uint kind,IntPtr window,int obj,int child,uint tid,uint time)
        {
            if(obj!=0||child!=0)return;
            var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);
            if(name.ToString()=="CabinetWClass")
            {
                if(kind==0x8002)windows.Enqueue(window.ToInt64()+":"+time);
                if(TargetTitle!=null)
                {
                    var title=new StringBuilder(512);GetWindowText(window,title,title.Capacity);
                    if(title.ToString().IndexOf(TargetTitle,StringComparison.OrdinalIgnoreCase)>=0)
                    {TargetWindow=window.ToInt64();ExplorerReady.Set();}
                }
            }
        }
        public string[] ExplorerWindows { get { return windows.ToArray(); } }
        public void Dispose(){PostThreadMessage(nativeThread,0x12,IntPtr.Zero,IntPtr.Zero);thread.Join();ready.Dispose();ExplorerReady.Dispose();}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ShellExecuteInfo
    {
        public int Size;public uint Mask;public IntPtr Window;
        public string Verb,File,Parameters,Directory;public int Show;public IntPtr Instance,IdList;
        public string Class;public IntPtr Key;public uint Hotkey;public IntPtr Icon,Process;
    }
    [StructLayout(LayoutKind.Explicit,Size=40)]struct Input
    { [FieldOffset(0)]public uint Type;[FieldOffset(8)]public ushort Key;[FieldOffset(12)]public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]struct Message
    {public IntPtr Window;public uint Id;public IntPtr W,L;public uint Time;public int X,Y;}
    delegate void WinEvent(IntPtr hook,uint kind,IntPtr window,int obj,int child,uint tid,uint time);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHParseDisplayName(string name,IntPtr bind,out IntPtr pidl,uint attributes,IntPtr found);
    [DllImport("shell32.dll")]static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")]static extern int SHOpenFolderAndSelectItems(IntPtr pidl,uint count,IntPtr[] children,uint flags);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder title,int maximum);
    [DllImport("user32.dll")]static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")]static extern bool PeekMessage(out Message message,IntPtr window,uint first,uint last,uint flags);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
}
