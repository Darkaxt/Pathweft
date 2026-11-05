#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyJumpLists11(string root,Action<bool,string> check)
    {
        check(ShellIdentity.Current!=ShellIdentity.Installed&&Settings.Profile.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase),"Native Jump Lists are bound to this exact diagnostic profile, not the installed AppID");
        Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out IntPtr appId));
        try{check(Marshal.PtrToStringUni(appId)==ShellIdentity.Current,"Windows reports the actual stable explicit process identity");}finally{Marshal.FreeCoTaskMem(appId);}
        check(folderJumpList!=null,"The real main window owns its native folder Jump List");
        string folder=Directory.CreateDirectory(Path.Combine(root,"folder spaced é 子")).FullName;
        string warm=Directory.CreateDirectory(Path.Combine(root,"running instance é 子")).FullName;
        File.WriteAllText(Path.Combine(folder,"cold-result.txt"),"Actual cold launch target");
        File.WriteAllText(Path.Combine(warm,"warm-result.txt"),"Actual running-instance target");
        var before=ReadNativeRecent(ShellIdentity.Installed);
        var beforeFrequent=ReadNativeRecent(ShellIdentity.Installed,1);
        bool cleared=false;
        try
        {
            pinned.Clear();pinned.Add(folder);SaveQuickAccess();
            Settings.RecentFolders=new(){folder,Path.GetPathRoot(folder),@"\\pathweft-offline.invalid\share\folder é 子",PaneView.ThisPC,Path.Combine(root,"virtual.zip","inner")};
            folderJumpList.Publish();
            var published=folderJumpList.Published;
            File.WriteAllText(Path.Combine(root,"jump-published.json"),JsonSerializer.Serialize(new{appId=ShellIdentity.Current,historyAllowed=folderJumpList.HistoryEnabled,folderJumpList.Issue,folderJumpList.Rejections,items=published.Select(item=>new{item.Title,item.ApplicationPath,item.Arguments,item.CustomCategory,item.Description,item.IconResourcePath,item.IconResourceIndex})}));
            check(folderJumpList.HistoryEnabled,"The existing Windows privacy policy permits this native destination qualification; no policy setting was changed");
            var shellList=(JumpList)typeof(FolderJumpList).GetField("list",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(folderJumpList);
            check(folderJumpList.Rejections.Length==0&&shellList.ShowFrequentCategory&&!shellList.ShowRecentCategory&&published.Count==0,"The publisher requests native Frequent with Windows-owned Pinned, without custom groups or Home/Downloads tasks");
            check(!FolderJumpList.TryFolder(PaneView.ThisPC,out _)&&!FolderJumpList.TryFolder(Path.Combine(root,"virtual.zip","inner"),out _),"Shell namespace and compressed virtual locations are explicitly omitted");
            check(FolderJumpList.TryFolder(Directory.CreateDirectory(Path.Combine(root,"real folder.zip")).FullName,out _),"A physical local folder remains eligible even when its name has an archive extension");

            var argumentResults=new List<object>();
            foreach(string path in new[]{folder,Path.GetPathRoot(folder),@"\\pathweft-offline.invalid\share\folder é 子"})
            {
                string receipt=Path.Combine(Settings.Profile,"argument-"+argumentResults.Count+".json");
                var start=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,Arguments=FolderJumpList.Quote(path)+" --verify-jump-args="+FolderJumpList.Quote(receipt)};
                using var process=Process.Start(start);await process.WaitForExitAsync();
                using var result=JsonDocument.Parse(File.ReadAllText(receipt));
                check(process.ExitCode==0&&result.RootElement.GetProperty("path").GetString()==path&&result.RootElement.GetProperty("pid").GetInt32()==process.Id,"Actual Windows command-line parsing preserves "+path);
                argumentResults.Add(new{path,pid=process.Id});
            }
            File.WriteAllText(Path.Combine(root,"jump-arguments.json"),JsonSerializer.Serialize(argumentResults));

            await S1Verification.Navigate(Left,folder);await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            int visits=0;void CountVisit(string path)=>visits++;
            Left.FolderVisited+=CountVisit;
            try{await S1Verification.Navigate(Left,folder);check(visits==0,"A same-folder refresh does not record another native visit");}finally{Left.FolderVisited-=CountVisit;}
            var nativeRecent=ReadNativeRecent(ShellIdentity.Current);
            File.WriteAllText(Path.Combine(root,"jump-native-recent.json"),JsonSerializer.Serialize(nativeRecent.Select(item=>new{item.path,item.arguments})));
            check(nativeRecent.Any(item=>item.path.Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)&&item.arguments==FolderJumpList.Quote(folder)),"Independent IApplicationDocumentLists readback finds the real visited folder link under the exact test AppID");
            var frequent=ReadNativeRecent(ShellIdentity.Current,1);
            File.WriteAllText(Path.Combine(root,"jump-native-frequent.json"),JsonSerializer.Serialize(frequent.Select(item=>new{item.path,item.arguments})));
            check(frequent.Any(item=>item.path.Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)&&item.arguments==FolderJumpList.Quote(folder)),"Independent native Frequent readback contains the actual visited argument-bearing folder link targeting this executable");

            RemoveNativeVisit11(folder);folderJumpList.Publish();
            check(!ReadNativeRecent(ShellIdentity.Current).Any(item=>item.arguments==FolderJumpList.Quote(folder))&&!ReadNativeRecent(ShellIdentity.Current,1).Any(item=>item.arguments==FolderJumpList.Quote(folder)),"Actual Windows removal clears Recent/Frequent and publisher refresh does not reintroduce it from local history or Quick access");
            folderJumpList.Dispose();
            using(var restarted=FolderJumpList.Create())
            {
                restarted.Publish();check(!ReadNativeRecent(ShellIdentity.Current,1).Any(item=>item.arguments==FolderJumpList.Quote(folder)),"Windows-owned removal survives publisher reconstruction without a second local removal database");
                restarted.Visit(folder);restarted.Publish();
                check(ReadNativeRecent(ShellIdentity.Current,1).Any(item=>item.arguments==FolderJumpList.Quote(folder)),"A genuine folder visit records new native usage for its previously removed destination");
                restarted.HistoryForVerification=false;restarted.Publish();
                var privateList=(JumpList)typeof(FolderJumpList).GetField("list",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(restarted);
                var usageBefore=ReadNativeRecent(ShellIdentity.Current,1);
                restarted.Visit(warm);
                check(!privateList.ShowFrequentCategory&&!privateList.ShowRecentCategory&&restarted.Published.Count==0&&usageBefore.SequenceEqual(ReadNativeRecent(ShellIdentity.Current,1)),"Privacy-disabled publication hides native usage and records no visits (isolated policy input; Windows preferences and pins unchanged)");
            }
            var navigated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready(){if(new[]{Left,Right}.Any(pane=>pane.Dir==warm&&pane.Items.Any(entry=>entry.Name=="warm-result.txt")))navigated.TrySetResult();}
            Left.StatsChanged+=Ready;Right.StatsChanged+=Ready;
            try
            {
                using var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,Arguments=FolderJumpList.Quote(warm)});
                await process.WaitForExitAsync();Ready();await navigated.Task;
                check(process.ExitCode==0&&new[]{Left,Right}.Any(pane=>pane.Dir==warm),"A real argument-only second process hands the folder to the existing window through the normal pipe, independent of default-folder integration");
            }finally{Left.StatsChanged-=Ready;Right.StatsChanged-=Ready;}

            string coldProfile=Directory.CreateDirectory(Path.Combine(root,"cold-profile")).FullName;
            string coldReceipt=Path.Combine(coldProfile,"cold.json");
            var cold=new ProcessStartInfo(Environment.ProcessPath){UseShellExecute=false,CreateNoWindow=true,Arguments=FolderJumpList.Quote(folder)+" --verify-jump-cold="+FolderJumpList.Quote(coldReceipt)};
            cold.Environment["PATHWEFT_PROFILE"]=coldProfile;cold.Environment["PATHWEFT_SPECIALIST_SCENARIO"]="jump-cold";
            File.WriteAllLines(Path.Combine(coldProfile,"settings.txt"),new[]{"tray=0","wine=0","taskbarsearch=0","lastseen=1.1.17","tabs.Left="+root,"tabs.Right="+root});
            using(var process=Process.Start(cold))
            {
                await process.WaitForExitAsync();using var receipt=JsonDocument.Parse(File.ReadAllText(coldReceipt));
                check(process.ExitCode==0&&receipt.RootElement.GetProperty("pid").GetInt32()==process.Id&&receipt.RootElement.GetProperty("path").GetString()==folder&&receipt.RootElement.GetProperty("listed").GetBoolean(),"A real cold process opens and lists the correct Unicode folder through normal startup and exits with its owned views drained");
            }
            File.WriteAllText(Path.Combine(root,"jump-cold.json"),File.ReadAllText(coldReceipt));
            OpenFromOutside(Path.Combine(root,"missing drive folder"));
            check(Status.Text.Contains("Location unavailable:"),"An unavailable local destination reports the actual navigation failure instead of silently doing nothing");

            NativeJumpList.RemoveOwned();cleared=true;
            check(ReadNativeRecent(ShellIdentity.Current).Length==0,"Uninstall's supported Shell cleanup clears only this test AppID's recorded usage");
            check(before.SequenceEqual(ReadNativeRecent(ShellIdentity.Installed)),"The installed application's native recent destinations remain unchanged");
            check(beforeFrequent.SequenceEqual(ReadNativeRecent(ShellIdentity.Installed,1)),"The installed application's Frequent data remains unchanged throughout the isolated native correction checks");
            check(HiddenTrialPresentation.ForegroundObserverActive&&HiddenTrialPresentation.ForegroundViolations==0,"Native list publication and real cold/warm launches never activate a host-desktop window");
        }
        finally{folderJumpList.Dispose();if(!cleared)NativeJumpList.RemoveOwned();}
    }

    internal async Task VerifyColdJump11(string receipt)
    {
        if(!HiddenTrialPresentation.Enabled||Settings.Profile==null||!Path.GetFullPath(receipt).StartsWith(Settings.Profile+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Exact hidden cold-launch receipt required");
        string requested=Program.PathArg(Environment.GetCommandLineArgs().Skip(1));
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Listed(){if(new[]{Left,Right}.Any(pane=>pane.Dir==requested&&pane.Items.Any(entry=>entry.Name=="cold-result.txt")))ready.TrySetResult();}
        Left.StatsChanged+=Listed;Right.StatsChanged+=Listed;
        try{Listed();await ready.Task;File.WriteAllText(receipt,JsonSerializer.Serialize(new{path=requested,pid=Environment.ProcessId,listed=true}));}
        finally{Left.StatsChanged-=Listed;Right.StatsChanged-=Listed;Quit();}
    }

    static void RemoveNativeVisit11(string folder)
    {
        if(ShellIdentity.Current==ShellIdentity.Installed)throw new InvalidOperationException("Native test removal requires the isolated application identity.");
        object native=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86c14003-4d6b-4ef3-a7b4-0506663b2e68")));
        object listsNative=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86bec222-30f2-47e0-9f25-60d11cd75c28")));object raw=null;
        try
        {
            var destinations=(IApplicationDestinations11)native;destinations.SetAppID(ShellIdentity.Current);
            var lists=(IApplicationDocumentLists11)listsNative;lists.SetAppID(ShellIdentity.Current);
            var arrayId=typeof(IObjectArray11).GUID;lists.GetList(0,0,ref arrayId,out raw);
            var array=(IObjectArray11)raw;array.GetCount(out uint count);var linkId=typeof(IShellLinkW).GUID;
            for(uint index=0;index<count;index++)
            {
                array.GetAt(index,ref linkId,out object item);var arguments=Marshal.AllocHGlobal(32768*2);
                try
                {
                    ((IShellLinkW)item).GetArguments(arguments,32768);
                    if(Marshal.PtrToStringUni(arguments)==FolderJumpList.Quote(folder)){destinations.RemoveDestination(item);return;}
                }finally{Marshal.FreeHGlobal(arguments);Marshal.ReleaseComObject(item);}
            }
            throw new InvalidOperationException("The actual native destination to remove was not found.");
        }finally{if(raw!=null)Marshal.ReleaseComObject(raw);Marshal.ReleaseComObject(listsNative);Marshal.ReleaseComObject(native);}
    }
    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("12337d35-94c6-48a0-bce7-6a9c69d4d600")]
    interface IApplicationDestinations11
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)]string id);
        void RemoveDestination([MarshalAs(UnmanagedType.IUnknown)]object destination);
        void RemoveAllDestinations();
    }
    static (string path,string arguments)[] ReadNativeRecent(string id,int kind=0)
    {
        object native=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86bec222-30f2-47e0-9f25-60d11cd75c28")));object raw=null;
        try
        {
            var lists=(IApplicationDocumentLists11)native;lists.SetAppID(id);var arrayId=typeof(IObjectArray11).GUID;lists.GetList(kind,Settings.RecentMax,ref arrayId,out raw);
            var array=(IObjectArray11)raw;array.GetCount(out uint count);var result=new List<(string,string)>();var linkId=typeof(IShellLinkW).GUID;
            for(uint index=0;index<count;index++)
            {
                array.GetAt(index,ref linkId,out object item);
                var path=Marshal.AllocHGlobal(32768*2);var arguments=Marshal.AllocHGlobal(32768*2);
                try{var link=(IShellLinkW)item;link.GetPath(path,32768,IntPtr.Zero,4);link.GetArguments(arguments,32768);result.Add((Marshal.PtrToStringUni(path),Marshal.PtrToStringUni(arguments)));}
                finally{Marshal.FreeHGlobal(path);Marshal.FreeHGlobal(arguments);Marshal.ReleaseComObject(item);}
            }
            return result.ToArray();
        }finally{if(raw!=null)Marshal.ReleaseComObject(raw);Marshal.ReleaseComObject(native);}
    }
    [ComImport,Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationDocumentLists11
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)]string id);
        void GetList(int type,uint count,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object items);
    }
    [ComImport,Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectArray11
    {
        void GetCount(out uint count);
        void GetAt(uint index,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object item);
    }
    [DllImport("shell32.dll")]static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);
}
#endif
