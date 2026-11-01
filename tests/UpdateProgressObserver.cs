using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Native installer verification only. Owns its two event handles and the exact
// Setup process; no desktop input, global hooks or elapsed-time synchronization.
public sealed class UpdateProgressObserver : IDisposable
{
    public readonly EventWaitHandle Ready,Continue;
    readonly ManualResetEvent exited=new ManualResetEvent(false);
    public readonly Process Process=new Process();
    public UpdateProgressObserver(string name)
    {
        Ready=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-ready");
        Continue=new EventWaitHandle(false,EventResetMode.AutoReset,name+"-continue");
    }
    public void Start(ProcessStartInfo start)
    {
        Process.StartInfo=start;Process.EnableRaisingEvents=true;
        Process.Exited+=(sender,args)=>exited.Set();
        if(!Process.Start())throw new IOException("The owned installer did not start.");
    }
    public int Wait()=>WaitHandle.WaitAny(new WaitHandle[]{Ready,exited});
    public void Dispose(){Continue.Set();Process.Dispose();Ready.Dispose();Continue.Dispose();exited.Dispose();}
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    public static uint Owner(long window){uint pid;GetWindowThreadProcessId(new IntPtr(window),out pid);return pid;}
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Header{public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,Image;public int X,Y;public uint Colors,Important;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect value);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr source);
    [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr dc,ref Header header,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    public static void Capture(long handle,string path)
    {
        var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try{CapturePixels(handle,path);}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
    static void CapturePixels(long handle,string path)
    {
        var window=new IntPtr(handle);Rect rect;if(!GetWindowRect(window,out rect))throw new IOException("Progress window is absent.");
        int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
        if(width<=0||height<=0||width>5000||height>5000)throw new IOException("Unexpected window bounds.");
        var header=new Header{Size=40,Width=width,Height=-height,Planes=1,Bits=32};
        var dc=CreateCompatibleDC(IntPtr.Zero);IntPtr bits;var bitmap=CreateDIBSection(dc,ref header,0,out bits,IntPtr.Zero,0);var old=SelectObject(dc,bitmap);
        try{
            if(!PrintWindow(window,dc,2))throw new IOException("Progress window capture failed.");
            var pixels=new byte[width*height*4];Marshal.Copy(bits,pixels,0,pixels.Length);
            using(var file=new BinaryWriter(File.Create(path))){file.Write((ushort)0x4D42);file.Write(54+pixels.Length);file.Write(0);file.Write(54);file.Write(40);file.Write(width);file.Write(-height);file.Write((ushort)1);file.Write((ushort)32);file.Write(0);file.Write(pixels.Length);file.Write(0);file.Write(0);file.Write(0);file.Write(0);file.Write(pixels);}
        }finally{SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);}
    }
}
