#if DEBUG
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyLiveIndexedSizes15(string root,Action<bool,string> check)
    {
        if(!root.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned indexed-size fixture required");
        string neutral=Directory.CreateDirectory(Path.Combine(root,"neutral")).FullName;
        await S1Verification.Navigate(Left,neutral);await S1Verification.Navigate(Right,neutral);await Task.WhenAll(Left.StopSizingAsync(),Right.StopSizingAsync());
        string fixture=Directory.CreateDirectory(Path.Combine(root,"live indexed fixture é 子")).FullName;
        string folder=Directory.CreateDirectory(Path.Combine(fixture,"child")).FullName;
        File.WriteAllBytes(Path.Combine(folder,"one.bin"),new byte[123]);File.WriteAllBytes(Path.Combine(folder,"two.bin"),new byte[456]);
        var client=Search.EverythingSearchClient.Shared;
        Search.IndexedPage page;
        // The external index reports readiness and asynchronous filesystem
        // updates through query results. Poll that protocol state without a
        // cancellation deadline, relaunch, settings change or whole-index copy.
        using(var monitor=new PeriodicTimer(TimeSpan.FromMilliseconds(100)))
        {
            do{page=await client.QueryAsync(Search.EverythingSearchClient.FolderExpression(new[]{folder}),"",0,2,"Name",false,CancellationToken.None);
                if(page.FolderSizesIndexed&&page.Hits.Count==1&&page.Hits[0].Size==579)break;
            }while(await monitor.WaitForNextTickAsync());
        }
        check(page.FolderSizesIndexed&&page.Hits.Single().Size==579,"The user's normally restarted Everything engine reports enabled folder sizing and the exact real recursive total");
        var totals=await client.FolderSizesAsync(new[]{folder,Path.Combine(fixture,"absent")},"",CancellationToken.None);
        check(totals.Count==1&&totals[folder]==579,"Production indexed lookup distinguishes the real covered folder from an absent path");
        await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;
        File.WriteAllText(Path.Combine(root,"live-indexed-row.json"),System.Text.Json.JsonSerializer.Serialize(new{Left.Items.Single().Size,Left.Items.Single().SizeSource}));
        check(Left.Items.Single().Size==579&&Left.Items.Single().SizeSource.StartsWith("Everything"),"The ordinary pane uses actual indexed totals with an explicit coverage label");
        // Finish a notification refresh already queued while the fixture was
        // created. A genuine changed snapshot correctly requests traversal;
        // this separate check exercises an idle indexed analysis entry.
        if(typeof(PaneView).GetField("folderRefresh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(Left) is System.Windows.Threading.DispatcherOperation pending)await pending.Task;
        await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;
        SpaceScan.IndexObserved=(path,result)=>File.AppendAllText(Path.Combine(root,"live-indexed-scan.txt"),path+"\n"+result+"\n");
        await Left.ToggleSpaceAsync();await Left.SpaceCompletion;SpaceScan.IndexObserved=null;
        File.WriteAllText(Path.Combine(root,"live-indexed-analysis.json"),System.Text.Json.JsonSerializer.Serialize(new{Left.SpaceAnalyzing,Left.Dir,summary=Left.SpaceAnalyzing?Left.SpaceSummary:default,rows=Left.Items.Select(entry=>new{entry.Path,entry.Size,entry.SizeSource,state=(entry as SpaceEntry)?.State.ToString()})}));
        check(Left.Items.Single().Size==579&&Left.Items.Single() is SpaceEntry{State:SpaceState.Indexed},"The analysis pane uses the same actual indexed folder size");
        await Left.EndSpaceAsync();
        var changed=Sizes(Left,()=>Left.Items.Single().Size==580&&Left.Items.Single().SizeSource=="Filesystem measurement");
        await Task.Run(()=>File.AppendAllText(Path.Combine(folder,"one.bin"),"x"));await changed;
        check(Left.Items.Single().Size==580,"A real write refreshes the row from fresh filesystem metadata rather than accepting a lagging indexed size");
        await Left.StopSizingAsync();
    }
    [DllImport("Everything64.dll",EntryPoint="Everything_IsDBLoaded")]static extern bool IndexLoaded15();
    [DllImport("Everything64.dll",EntryPoint="Everything_Exit")]static extern bool ExitIndex15();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowW(string name,string title);
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]static extern uint IndexProcessId15(IntPtr window,out uint pid);
    internal async Task VerifyIndexedSizes15(string root,Action<bool,string> check)
    {
        // The existing native harness creates a unique, never-switched desktop.
        // Thus the SDK default-instance lookup cannot reach the user's engine.
        bool guest=Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_SANDBOX")=="1"&&root.StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase);
        if(!guest&&Environment.GetEnvironmentVariable("PATHWEFT_PRIVATE_MENU_DESKTOP")!="WinSta0")throw new InvalidOperationException("Private desktop or owned Sandbox required");
        string engineRoot=Directory.CreateDirectory(Path.Combine(root,"index-engine")).FullName;
        string fixture=Directory.CreateDirectory(Path.Combine(root,"indexed fixture é 子")).FullName;
        string folder=Directory.CreateDirectory(Path.Combine(fixture,"child")).FullName;
        File.WriteAllBytes(Path.Combine(folder,"one.bin"),new byte[123]);File.WriteAllBytes(Path.Combine(folder,"two.bin"),new byte[456]);
        string original=Path.Combine(AppContext.BaseDirectory,"Everything","Everything.exe"),executable=Path.Combine(engineRoot,"Everything.exe");
        File.Copy(original,executable);check(SHA256.HashData(File.ReadAllBytes(original)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(executable))),"Private size qualification uses the exact bundled engine bytes");
        string config=Path.Combine(engineRoot,"Everything.ini");
        File.WriteAllText(config,"[Everything]\r\napp_data=0\r\nrun_as_admin=0\r\nrun_in_background=1\r\nshow_tray_icon=0\r\nauto_include_fixed_volumes=0\r\nauto_include_removable_volumes=0\r\nauto_include_fixed_refs_volumes=0\r\nauto_include_removable_refs_volumes=0\r\nindex_size=1\r\nindex_folder_size=1\r\nfolders=\""+fixture+"\"\r\nfolder_monitor_changes=1\r\n",new UTF8Encoding(true));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=engineRoot};
        foreach(string argument in new[]{"-config",config,"-db",Path.Combine(engineRoot,"Everything.db"),"-startup"})start.ArgumentList.Add(argument);
        start.Environment["APPDATA"]=engineRoot;start.Environment["LOCALAPPDATA"]=engineRoot;start.Environment["TEMP"]=Path.Combine(root,"temp");start.Environment["TMP"]=start.Environment["TEMP"];
        using var engine=Process.Start(start);
        try
        {
            await Task.Run(()=>engine.WaitForInputIdle());
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var monitor=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(100)};
            // SDK1.4 exposes readiness by query, not a load-completed event.
            // This protocol-bound diagnostic monitor has no failure deadline
            // and never cancels or relaunches the original indexing process.
            void CheckReady()
            {
                if(engine.HasExited){ready.TrySetException(new IOException("Private index exited before readiness"));return;}
                var window=FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION",null);
                if(window==IntPtr.Zero)return;IndexProcessId15(window,out uint pid);
                if(pid!=engine.Id){ready.TrySetException(new IOException("SDK lookup escaped the private engine"));return;}
                if(IndexLoaded15())ready.TrySetResult();
            }
            monitor.Tick+=(_,_)=>CheckReady();monitor.Start();try{CheckReady();await ready.Task;}finally{monitor.Stop();}
            var client=Search.EverythingSearchClient.Shared;
            var page=await client.QueryAsync(Search.EverythingSearchClient.FolderExpression(new[]{folder}),"",0,2,"Name",false,CancellationToken.None);
            check(page.FolderSizesIndexed&&page.Total==1&&page.Hits.Single().Size==579,"Actual enabled Everything1.4 folder indexing returns the exact recursive logical size through the existing SDK");
            var totals=await client.FolderSizesAsync(new[]{folder,Path.Combine(fixture,"absent")},"",CancellationToken.None);
            check(totals.Count==1&&totals[folder]==579,"Production batched folder-size lookup distinguishes indexed data from absent paths");
        await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;
            check(Left.Items.Single().Size==579&&Left.Items.Single().SizeSource.StartsWith("Everything"),"Ordinary pane sizing uses the real index and labels its coverage");
            await Left.ToggleSpaceAsync();await Left.SpaceCompletion;
            check(Left.Items.Single().Size==579&&Left.Items.Single() is SpaceEntry{State:SpaceState.Indexed},"Analysis uses the same real indexed totals without traversing the covered child");
            await Left.EndSpaceAsync();
            var changed=Sizes(Left,()=>Left.Items.Single().Size==580&&Left.Items.Single().SizeSource=="Filesystem measurement");
            await Task.Run(()=>File.AppendAllText(Path.Combine(folder,"one.bin"),"x"));await changed;
            check(Left.Items.Single().Size==580,"A real filesystem notification uses a fresh measurement rather than accepting an older indexed size");
            await Left.StopSizingAsync();
        }
        finally
        {
            var window=FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION",null);if(window!=IntPtr.Zero){IndexProcessId15(window,out uint pid);if(pid!=engine.Id)throw new InvalidOperationException("Refuse to exit an unowned index");if(!ExitIndex15())throw new Win32Exception();}
            await engine.WaitForExitAsync();check(engine.ExitCode==0,"The original private engine exits normally before native job/desktop release");
        }
    }
}
#endif
