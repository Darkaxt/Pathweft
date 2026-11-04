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
        bool cleared=false;
        try
        {
            pinned.Clear();pinned.Add(folder);SaveQuickAccess();
            Settings.RecentFolders=new(){folder,Path.GetPathRoot(folder),@"\\pathweft-offline.invalid\share\folder é 子",PaneView.ThisPC,Path.Combine(root,"virtual.zip","inner")};
            folderJumpList.Publish();
            var published=folderJumpList.Published;
            File.WriteAllText(Path.Combine(root,"jump-published.json"),JsonSerializer.Serialize(new{appId=ShellIdentity.Current,historyAllowed=folderJumpList.HistoryEnabled,folderJumpList.Issue,folderJumpList.Rejections,items=published.Select(item=>new{item.Title,item.ApplicationPath,item.Arguments,item.CustomCategory,item.Description,item.IconResourcePath,item.IconResourceIndex})}));
            check(folderJumpList.HistoryEnabled,"The existing Windows privacy policy permits this native destination qualification; no policy setting was changed");
            check(folderJumpList.Rejections.Length==0&&published.Count(item=>!string.IsNullOrEmpty(item.CustomCategory))==3,"The actual Shell accepts Quick access and Recent folders, including drive root and offline UNC destinations");
            check(published.All(item=>item.ApplicationPath==Environment.ProcessPath&&item.Arguments.Length>0)&&published.Count(item=>item.Arguments==FolderJumpList.Quote(folder))==1,"Destinations launch this executable with arguments and deduplicate across categories; they are not folder-default-handler links");
            check(published.Where(item=>item.CustomCategory==null).Select(item=>item.Title).SequenceEqual(new[]{"Home","Downloads"}),"Useful tasks reuse the existing Home and Downloads navigation");
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

            var task=folderJumpList.Published.Single(item=>item.Arguments==FolderJumpList.Quote(folder));
            folderJumpList.RemoveForVerification(task);folderJumpList.Publish();
            check(folderJumpList.Published.All(item=>item.Arguments!=task.Arguments),"Shell-removal event handling suppresses the exact destination on reconstruction (diagnostic event input, not a simulated user pin/remove claim)");
            folderJumpList.Dispose();
            using(var restarted=FolderJumpList.Create(()=>pinned))
            {
                restarted.Publish();check(restarted.Published.All(item=>item.Arguments!=task.Arguments),"Recorded destination removal survives publisher reconstruction using the actual saved profile");
                restarted.Visit(folder);restarted.Publish();
                check(restarted.Published.Any(item=>item.Arguments==task.Arguments),"A genuine folder visit can reintroduce its own previously removed destination");
                restarted.HistoryForVerification=false;restarted.Publish();
                check(restarted.Published.Count==2&&restarted.Published.All(item=>item.CustomCategory==null),"Privacy-disabled publication retains tasks and omits all folder categories (isolated policy input, no Windows setting change)");
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

    static (string path,string arguments)[] ReadNativeRecent(string id)
    {
        object native=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86bec222-30f2-47e0-9f25-60d11cd75c28")));object raw=null;
        try
        {
            var lists=(IApplicationDocumentLists11)native;lists.SetAppID(id);var arrayId=typeof(IObjectArray11).GUID;lists.GetList(0,Settings.RecentMax,ref arrayId,out raw);
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
