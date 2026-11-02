#if DEBUG
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHParseDisplayName(string path,IntPtr context,out IntPtr pidl,uint flags,IntPtr attributes);
    [DllImport("shell32.dll")]static extern uint ILGetSize(IntPtr pidl);
    [DllImport("shell32.dll")]static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")]static extern int SHOpenFolderAndSelectItems(IntPtr folder,uint count,IntPtr[] children,uint flags);
    [DllImport("ole32.dll")]static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")]static extern void CoUninitialize();
    internal async Task VerifyMedia113(string root,Action<bool,string> check)
    {
        async Task Settled()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var fixture=Directory.CreateDirectory(Path.Combine(root,"selection é 子")).FullName;
        foreach(string name in new[]{"selected.txt","second.txt"})File.WriteAllText(Path.Combine(fixture,name),name);
        await S1Verification.Navigate(Left,fixture);await S1Verification.Navigate(Right,PaneView.ThisPC);SetActive(Left);
        shellFolderViews.EnableForVerification(true);
        try
        {
            await NativeSelection(fixture,new[]{"selected.txt","second.txt"});await Settled();
            check(Left.Selected.Select(item=>item.Name).ToHashSet().SetEquals(new[]{"selected.txt","second.txt"}),"The real Windows selection API selects both requested rows through the registered native view");
            if(Environment.GetEnvironmentVariable("PATHWEFT_MEDIA113_CALLER") is {Length:>0} callerReceipt)
            {
                Left.List.SelectedItems.Clear();
                var received=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Received(){if(Left.Selected.Any(item=>item.Name=="selected.txt"))received.TrySetResult();}
                Left.StatsChanged+=Received;
                try
                {
                    File.WriteAllText(callerReceipt,JsonSerializer.Serialize(new{pid=Environment.ProcessId,folder=fixture,file=Path.Combine(fixture,"selected.txt")}));
                    await received.Task;
                    check(Left.Selected.Single().Name=="selected.txt","The actual Helium process native request reaches Pathweft and highlights its supplied file");
                }
                finally{Left.StatsChanged-=Received;}
            }
            Left.NewTab();await S1Verification.Navigate(Left,root);
            await NativeSelection(fixture,new[]{"selected.txt"});
            var selected=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Done(){if(Left.Dir==fixture&&Left.Selected.Any(item=>item.Name=="selected.txt"))selected.TrySetResult();}
            Left.StatsChanged+=Done;try{Done();await selected.Task;}finally{Left.StatsChanged-=Done;}
            check(Left.Dir==fixture&&Left.Selected.Single().Name=="selected.txt","Show in folder activates an existing inactive tab and preserves selection across its asynchronous listing");
            shellFolderViews.EnableForVerification(false);
            check(shellFolderViews.CountForVerification==0,"Opting out revokes every native folder-view registration without changing Windows defaults");
        }
        finally{shellFolderViews.EnableForVerification(false);}
        await driveRefreshWork;
        foreach(var drive in shownDrives.Where(drive=>drive.Name is "C:\\" or "D:\\"))
        {
            var node=folderTree.Items.OfType<TreeViewItem>().Single(item=>(string)item.Tag==drive.Name);
            check(((StackPanel)node.Header).Children.OfType<TextBlock>().Single().Text==drive.Name.TrimEnd('\\')+"  "+drive.Label,"Folders and Drives share the real volume-label fallback: "+drive.Name);
        }

        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_MEDIA113_INPUTS")??throw new ArgumentException("Media fixture inventory required.");
        using var inventory=JsonDocument.Parse(File.ReadAllText(inputs));var metadata=new List<object>();
        async Task<MpvPlayer> Show(string path)
        {
            path=Path.GetFullPath(path);
            await S1Verification.Navigate(Left,Path.GetDirectoryName(path));SetActive(Left);
            Settings.Preview=true;ApplyPreview();Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));PreviewFollow();
            await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="media","Real pane selection loads the bundled media preview: "+Path.GetFileName(path)+"; "+sidePreview.State+" "+sidePreview.ModuleId+" "+(PreviewBody.Content as TextBlock)?.Text);
            return ((BuiltinViewer)sidePreview.ViewerForVerification).MediaForVerification.Player;
        }
        foreach(var input in inventory.RootElement.EnumerateArray())
        {
            string name=input.GetProperty("name").GetString(),path=input.GetProperty("path").GetString();
            var player=await Show(path);
            check(player.Property("pause")=="yes","Side-pane media remains paused until Play: "+name);
            check(player.Property("current-vo")=="gpu-next","Media uses the native gpu-next output: "+name);
            metadata.Add(new{name,path,parameters=player.Property("video-params"),output=player.Property("video-out-params"),target=player.Property("video-target-params"),hardware=player.Property("hwdec-current"),duration=player.Property("duration")});
            if(name!="audio")
            {
                await player.CommandAsync("screenshot-to-file",Path.Combine(root,name+"-native.png"),"window");
                check(new FileInfo(Path.Combine(root,name+"-native.png")).Length>1000,"The actual GPU video output produces a nonempty verification frame: "+name);
            }
            if(name=="sdr"||name=="audio")
            {
                if(name=="sdr"&&Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_DEPS117")=="1")
                    await Dependency117Checks.Subtitles(player,root,check);
                var advanced=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var reset=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);bool eof=false;
                void Changed(string property,object value){if(property=="time-pos"&&value is double seconds&&seconds>0.1)advanced.TrySetResult();if(property=="eof-reached"&&value is true)eof=true;if(eof&&property=="time-pos"&&value is double at&&at<=0.01)reset.TrySetResult();}
                player.Changed+=Changed;
                try{check(sidePreview.HandleKey(System.Windows.Input.Key.Enter),"The preview's Enter action toggles real native playback: "+name);await advanced.Task;check(sidePreview.HandleKey(System.Windows.Input.Key.Right),"The preview's seek key reaches native playback: "+name);await reset.Task;}
                finally{player.Changed-=Changed;}
                check(player.Property("pause")=="yes","Native EOF resets the preview to its beginning and pauses: "+name);
            }
            if(name.StartsWith("p")&&Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_DEPS117")=="1")
                await Dependency117Checks.DolbySeeks(player,name,check);
            sidePreview.Clear();await PreviewSession.DrainAsync();
            check(player.Completion.IsCompletedSuccessfully,"Selection cleanup joins the actual native media worker: "+name);
        }
        File.WriteAllText(Path.Combine(root,"media-metadata.json"),JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true}));
        string video=inventory.RootElement.EnumerateArray().First(input=>input.GetProperty("name").GetString()=="sdr").GetProperty("path").GetString();
        var pausedPlayer=await Show(video);ToggleQuickView(Left,this);await quickPreview.Completion;
        check(quickPreview.State=="ready"&&quickPreview.ModuleId=="media","Space opens the same bundled native media engine");
        var popup=((BuiltinViewer)quickPreview.ViewerForVerification).MediaForVerification.Player;
        check(popup.Property("pause")=="no","Space preview autoplays independently of the paused side pane");
        quick.Close();await PreviewSession.DrainAsync();check(popup.Completion.IsCompletedSuccessfully,"Closing Space waits for native media teardown");
        Settings.DisabledPreviewModules.Add("media");RefreshPreviewModules();await sidePreview.Completion;await PreviewSession.DrainAsync();
        check(sidePreview.ModuleId!="media"&&pausedPlayer.Completion.IsCompletedSuccessfully,"Disabling Video and audio releases the active native player through the existing module setting");
        Settings.DisabledPreviewModules.Remove("media");
        string broken=Path.Combine(root,"broken.mp4");File.WriteAllText(broken,"not a media file");
        sidePreview.Show(new Entry{Path=broken,Name="broken.mp4"});await sidePreview.Completion;
        check(sidePreview.State=="error","An invalid media file reports a preview error instead of a silent empty view");
        sidePreview.Clear();await PreviewSession.DrainAsync();
        check(HiddenTrialPresentation.ForegroundViolations==0,"Native selection and media qualification performed no foreground takeover");
    }
    static Task NativeSelection(string folder,string[] names)=>Task.Run(()=>
    {
        int initialized=CoInitializeEx(IntPtr.Zero,0);Marshal.ThrowExceptionForHR(initialized);
        IntPtr parent=IntPtr.Zero;var items=new List<IntPtr>();
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(folder,IntPtr.Zero,out parent,0,IntPtr.Zero));
            foreach(string name in names){Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.Combine(folder,name),IntPtr.Zero,out var item,0,IntPtr.Zero));items.Add(item);}
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(parent,(uint)items.Count,items.Select(ILFindLastID).ToArray(),0));
        }
        finally{foreach(var item in items)Marshal.FreeCoTaskMem(item);Marshal.FreeCoTaskMem(parent);CoUninitialize();}
    });
}
#endif
