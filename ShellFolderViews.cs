using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileExplorer;

// The native Shell sends selection to the view for a registered folder. Each
// open filesystem tab has a cookie; no Explorer windows are watched or closed.
internal sealed class ShellFolderViews : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Select([MarshalAs(UnmanagedType.LPWStr)] string path,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int Register(IntPtr hwnd,[MarshalAs(UnmanagedType.LPWStr)]string path,Select selection,out IntPtr registration);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Revoke(IntPtr registration);
    sealed record View(IntPtr Registration,Select Selection);
    readonly MainWindow owner;
    readonly Dictionary<string,View> views=new(StringComparer.OrdinalIgnoreCase);
    readonly IntPtr library;
    readonly Register register;
    readonly Revoke revoke;
    bool enabled,disposed;
    internal ShellFolderViews(MainWindow window)
    {
        owner=window;
        string version=typeof(App).Assembly.GetName().Version.ToString(3);
        library=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,$"PathweftShell-{version}-x64.dll"));
        register=Marshal.GetDelegateForFunctionPointer<Register>(NativeLibrary.GetExport(library,"PathweftRegisterFolder"));
        revoke=Marshal.GetDelegateForFunctionPointer<Revoke>(NativeLibrary.GetExport(library,"PathweftRevokeFolder"));
        WindowsIntegration.Changed+=Refresh;
        owner.Activated+=Activated;
        foreach(var pane in new[]{owner.Left,owner.Right}){pane.NavigationChanged+=Sync;pane.IsVisibleChanged+=VisibilityChanged;}
        Refresh();
    }
    void Activated(object sender,EventArgs args)=>Refresh();
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)=>Sync();
    void Refresh(){enabled=Settings.Profile==null&&WindowsIntegration.Enabled;Sync();}
#if DEBUG
    internal void EnableForVerification(bool value){enabled=value;Sync();}
    internal int CountForVerification=>views.Count;
#endif
    internal void Sync()
    {
        if(disposed)return;
        var paths=enabled?new[]{owner.Left,owner.Right}.Where(pane=>pane.Visibility==Visibility.Visible).SelectMany(pane=>pane.OpenTabs)
            .Where(path=>Path.IsPathFullyQualified(path)&&PaneView.ArchiveOf(path)==null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in views.Keys.Where(path=>!paths.Contains(path)).ToArray())
        {revoke(views[path].Registration);views.Remove(path);}
        foreach(string path in paths.Where(path=>!views.ContainsKey(path)))
        {
            Select select=(item,flags)=>
            {
                try{return owner.SelectFromShell(path,item,flags);}
                catch(Exception error){return Marshal.GetHRForException(error);}
            };
            int result=register(new WindowInteropHelper(owner).Handle,path,select,out var registration);
            if(result>=0)views.Add(path,new(registration,select));
            else System.Diagnostics.Trace.WriteLine($"Shell folder registration: {path}: 0x{result:X8}");
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        WindowsIntegration.Changed-=Refresh;owner.Activated-=Activated;
        foreach(var pane in new[]{owner.Left,owner.Right}){pane.NavigationChanged-=Sync;pane.IsVisibleChanged-=VisibilityChanged;}
        foreach(var view in views.Values)revoke(view.Registration);
        // COM may retain an already marshalled proxy after Revoke. The native
        // callbacks are disabled synchronously, but its vtable must stay mapped
        // until the process exits. Keep this small shared module loaded.
        views.Clear();
    }
}

public partial class MainWindow
{
    ShellFolderViews shellFolderViews;
    internal int SelectFromShell(string folder,string path,uint flags)
    {
        Dispatcher.VerifyAccess();
        if(TabShowing(folder) is not { } target)return unchecked((int)0x80004004);
        var (pane,index)=target;
        if(path!=null&&!string.Equals(Path.GetDirectoryName(path.TrimEnd('\\'))?.TrimEnd('\\'),folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))return unchecked((int)0x80070057);
        if(!IsVisible)Show();
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        // Selecting an already visible tab avoids a fresh folder enumeration.
        if(pane.ActiveTab!=index||pane.InSearch)pane.ShowTab(index);
        SetActive(pane);pane.SelectFromShell(path==null?null:Path.GetFileName(path),flags);
        return 0;
    }
}
