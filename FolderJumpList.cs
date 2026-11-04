using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FileExplorer;

internal static class ShellIdentity
{
    internal const string Installed = "Darkaxt.Pathweft";
    internal static string Current => (Settings.Profile ?? Program.PortableDir) is {} own
        ? Installed+".Copy."+System.IO.Hashing.XxHash64.HashToUInt64(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(own).ToLowerInvariant())).ToString("x16") : Installed;
    internal static void Apply()=>Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(Current));
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

// One native list for the existing main-window history and selected Quick list.
// Windows alone owns pinned destinations, removed-item decisions and placement.
internal sealed class FolderJumpList : IDisposable
{
    readonly JumpList list=new();
    readonly Func<IEnumerable<string>> quick;
    readonly HashSet<string> removed=new(StringComparer.OrdinalIgnoreCase);
    readonly string removalFile=Path.Combine(Settings.Dir,"jumplist-removed.txt");
    DispatcherOperation pending;
    bool disposed;
    bool storageFailed;
    internal string Issue {get;private set;}="";
#if DEBUG
    internal bool? HistoryForVerification;
    internal void RemoveForVerification(JumpTask item)=>RememberRemoved(new[]{item});
#endif
    internal string[] Rejections {get;private set;}=Array.Empty<string>();
    internal bool HistoryEnabled=>!storageFailed&&
#if DEBUG
        (HistoryForVerification??HistoryAllowed());
#else
        HistoryAllowed();
#endif
    internal IReadOnlyList<JumpTask> Published=>list.JumpItems.OfType<JumpTask>().ToArray();

    internal static FolderJumpList Create(Func<IEnumerable<string>> quick)
    {
        // Other diagnostics must never modify any Shell destination list.
        if(Settings.Profile!=null
#if DEBUG
            &&Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")!="jump-lists"
#endif
            )return null;
        return new FolderJumpList(quick);
    }
    FolderJumpList(Func<IEnumerable<string>> quickFolders)
    {
        quick=quickFolders;
        try{if(File.Exists(removalFile))foreach(string line in File.ReadAllLines(removalFile))if(!string.IsNullOrWhiteSpace(line))removed.Add(line);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){storageFailed=true;Issue=error.Message;}
        list.JumpItemsRemovedByUser+=(_,args)=>RememberRemoved(args.RemovedItems);
        list.JumpItemsRejected+=(_,args)=>
        {
            Rejections=args.RejectionReasons.Select(reason=>reason.ToString()).ToArray();
            RememberRemoved(args.RejectedItems.Where((_,index)=>args.RejectionReasons[index]==JumpItemRejectionReason.RemovedByUser));
        };
        Schedule();
    }
    void RememberRemoved(IEnumerable<JumpItem> entries)
    {
        bool changed=false;
        foreach(var entry in entries.OfType<JumpTask>())
            if(string.Equals(entry.ApplicationPath,Program.Executable,StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrEmpty(entry.Arguments))changed|=removed.Add(entry.Arguments);
        if(changed)SaveRemoved();
    }
    void SaveRemoved()
    {
        try{Directory.CreateDirectory(Settings.Dir);Settings.WriteAllLines(removalFile,removed.Order(StringComparer.OrdinalIgnoreCase));}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){storageFailed=true;Issue=error.Message;}
    }
    internal void Visit(string path)
    {
        if(!TryFolder(path,out string folder))return;
        if(HistoryEnabled)
        {
            var destination=Destination(folder,null);
            if(removed.Remove(destination.Arguments))SaveRemoved();
            try{NativeJumpList.RecordVisit(destination);}
            catch(COMException error){Issue=error.Message;}
        }
        Schedule();
    }
    internal void Schedule()
    {
        if(disposed||pending?.Status==DispatcherOperationStatus.Pending)return;
        pending=Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{pending=null;Publish();});
    }
    internal void Publish()
    {
        if(disposed)return;
        Rejections=Array.Empty<string>();list.JumpItems.Clear();
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddFolders(IEnumerable<string> paths,string category)
        {
            foreach(string path in paths)
                if(TryFolder(path,out string folder)&&used.Add(folder))
                {
                    var item=Destination(folder,category);
                    if(!removed.Contains(item.Arguments))list.JumpItems.Add(item);
                }
        }
        if(HistoryEnabled){AddFolders(quick(),"Quick access");AddFolders(Settings.RecentFolders,"Recent folders");}
        var home=Destination(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),null);home.Title="Home";list.JumpItems.Add(home);
        var downloads=Destination(MainWindow.DownloadsFolder(),null);downloads.Title="Downloads";list.JumpItems.Add(downloads);
        list.Apply();
    }
    internal static JumpTask Destination(string folder,string category)=>new()
    {
        ApplicationPath=Program.Executable,Arguments=Quote(folder),CustomCategory=category,
        Title=Path.GetFileName(folder.TrimEnd('\\')) is {Length:>0} name?name:folder,
        Description=folder,WorkingDirectory=AppContext.BaseDirectory,
        IconResourcePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shell32.dll"),IconResourceIndex=3
    };
    internal static string Quote(string folder)=>"\""+folder+(folder.EndsWith('\\')?"\\":"")+"\"";
    internal static bool TryFolder(string value,out string folder)
    {
        folder=null;
        if(string.IsNullOrWhiteSpace(value)||value.Contains('"')||!Path.IsPathFullyQualified(value)||value.StartsWith("::",StringComparison.Ordinal))return false;
        // Archive namespaces contain an archive filename as a path segment. Do
        // not probe remote paths: offline servers must not stall menu refreshes.
        if(value.Split('\\','/').Any(Archive.IsArchiveName)&&(PaneView.IsRemote(value)||!Directory.Exists(value)))return false;
        try{folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));return true;}
        catch(Exception error)when(error is ArgumentException or NotSupportedException or PathTooLongException){return false;}
    }
    internal static bool HistoryAllowed()
    {
        try
        {
            using var userPolicy=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            using var machinePolicy=Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            using var advanced=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return (userPolicy?.GetValue("NoRecentDocsHistory") is not int up||up==0)&&
                (machinePolicy?.GetValue("NoRecentDocsHistory") is not int mp||mp==0)&&
                (advanced?.GetValue("Start_TrackDocs") is not int track||track!=0);
        }
        catch(Exception error)when(error is UnauthorizedAccessException or System.Security.SecurityException){return false;}
    }
    public void Dispose(){disposed=true;pending?.Abort();}
}

// Documented Shell teardown used only for this copy's uninstall and isolated
// test identities. Updating a list never calls DeleteList or clears OS pins.
internal static class NativeJumpList
{
    internal static void RecordVisit(JumpTask item)
    {
        var link=(MainWindow.IShellLinkW)new MainWindow.ShellLink();
        try
        {
            link.SetPath(item.ApplicationPath);link.SetArguments(item.Arguments);link.SetDescription(item.Description);
            link.SetWorkingDirectory(item.WorkingDirectory);link.SetIconLocation(item.IconResourcePath,item.IconResourceIndex);
            var titleKey=new PropertyKey{Format=new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"),Id=2};
            // InitPropVariantFromString is an inline SDK helper, not a DLL
            // export. Match its VT_LPWSTR/CoTaskMem ownership directly.
            var title=new PropVariant{Type=31,Value=Marshal.StringToCoTaskMemUni(item.Title)};
            try{var properties=(IPropertyStore)link;properties.SetValue(ref titleKey,ref title);properties.Commit();}
            finally{PropVariantClear(ref title);}
            // SHARD_LINK lets Shell infer an executable's association. An
            // argument-bearing folder destination needs this exact explicit ID.
            var information=new AppIdLink{Link=link,AppId=ShellIdentity.Current};SHAddToRecentDocs(7,ref information);
        }finally{Marshal.ReleaseComObject(link);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct AppIdLink
    {
        [MarshalAs(UnmanagedType.Interface)]internal MainWindow.IShellLinkW Link;
        [MarshalAs(UnmanagedType.LPWStr)]internal string AppId;
    }
    [DllImport("shell32.dll")]static extern void SHAddToRecentDocs(uint kind,ref AppIdLink item);
    [StructLayout(LayoutKind.Sequential)]struct PropertyKey{internal Guid Format;internal uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]internal ushort Type;[FieldOffset(8)]internal IntPtr Value;}
    [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropVariant value);
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index,out PropertyKey key);
        void GetValue(ref PropertyKey key,out PropVariant value);
        void SetValue(ref PropertyKey key,ref PropVariant value);
        void Commit();
    }
    internal static void RemoveOwned()
    {
        object recent=null,custom=null;
        try
        {
            recent=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86c14003-4d6b-4ef3-a7b4-0506663b2e68")));
            var destinations=(IApplicationDestinations)recent;destinations.SetAppID(ShellIdentity.Current);destinations.RemoveAllDestinations();
            custom=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")));
            ((ICustomDestinationList)custom).DeleteList(ShellIdentity.Current);
        }
        finally{if(custom!=null)Marshal.ReleaseComObject(custom);if(recent!=null)Marshal.ReleaseComObject(recent);}
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("12337d35-94c6-48a0-bce7-6a9c69d4d600")]
    interface IApplicationDestinations
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)]string appId);
        void RemoveDestination([MarshalAs(UnmanagedType.IUnknown)]object destination);
        void RemoveAllDestinations();
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)]string appId);
        void BeginList(out uint slots,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)]string category,[MarshalAs(UnmanagedType.Interface)]object items);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)]object items);
        void CommitList();
        void GetRemovedDestinations(ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)]string appId);
        void AbortList();
    }
}
