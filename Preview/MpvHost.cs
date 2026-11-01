using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FileExplorer.Preview;

internal sealed class MpvHost : HwndHost
{
    static long sequence;
    readonly IntPtr identity=new(Interlocked.Increment(ref sequence));
    const string Ownership="Pathweft.MediaPreview";
    internal Func<IntPtr,MpvPlayer> Start;
    internal MpvPlayer Player {get;private set;}
    internal Task Closed {get;private set;}=Task.CompletedTask;
    internal IntPtr Child {get;private set;}
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        Child=CreateWindowExW(0,"STATIC","",0x56000000,0,0,1,1,parent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(Child==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if(!SetPropW(Child,Ownership,identity)){DestroyWindow(Child);throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        Player=Start(Child);return new HandleRef(this,Child);
    }
    protected override void DestroyWindowCore(HandleRef window)
    {
        ShowWindow(window.Handle,0);
        // Keep the HWND alive until the renderer has actually exited. Awaiting
        // off the dispatcher lets the native window threads finish normally.
        Closed=Release(window.Handle);
    }
    async Task Release(IntPtr window)
    {
        if(Player!=null)await Player.StopAsync();
        await Dispatcher.InvokeAsync(()=>
        {
            if(GetPropW(window,Ownership)==identity){RemovePropW(window,Ownership);DestroyWindow(window);}
            Child=IntPtr.Zero;
        });
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowExW(uint extended,string kind,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
    [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int show);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetPropW(IntPtr window,string name,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetPropW(IntPtr window,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr RemovePropW(IntPtr window,string name);
}
