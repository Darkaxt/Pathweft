#if DEBUG
using FileExplorer.Preview;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyPreviewCorrection(string root, Action<bool,string> check)
    {
        var body = new ContentControl();
        Settings.Preview=true;ApplyPreview();
        PreviewBody.Content = body;
        using var session = new PreviewSession(this, body, _ => { });
        async Task Show(string path, bool directory = false)
        {
            session.Show(new Entry { Path = path, Name = Path.GetFileName(path), IsDir = directory });
            await session.Completion;
            check(session.State == "ready", "Preview loads through the actual session: " + Path.GetExtension(path));
        }
        async Task Settle() => await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        TreeViewItem[] Rows(ItemsControl tree) => tree.Items.OfType<TreeViewItem>().ToArray();
        var zip = Path.Combine(root,"tree.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Empty/"); archive.CreateEntry("Top/Nested/readme.txt").Open().Dispose();
            archive.CreateEntry("Top/Other.bin").Open().Dispose(); archive.CreateEntry("root.txt").Open().Dispose();
            for(int i=0;i<1200;i++) archive.CreateEntry($"Wide/item-{i:0000}.txt");
        }
        await Show(zip);
        var tree = body.Content as TreeView;
        check(tree!=null && Rows(tree).Length==4, "Archive is a tree with implicit directories and root files, not a flat text listing");
        var top=Rows(tree).Single(r=>((ArchiveTree.Node)r.Tag).Name=="Top");
        check(top.Items[0] is string, "Collapsed branches defer child WPF rows");
        ((IExpandCollapseProvider)new TreeViewItemAutomationPeer(top).GetPattern(PatternInterface.ExpandCollapse)).Expand();
        check(Rows(top).Length==2 && ((ArchiveTree.Node)Rows(top)[0].Tag).Directory, "Accessible expansion lists folders before files using concise names");
        var nested=Rows(top)[0]; nested.IsExpanded=true;
        check(Rows(nested).Single().ToolTip.ToString()=="Top/Nested/readme.txt" && Rows(tree).Single(r=>((ArchiveTree.Node)r.Tag).Name=="Empty").Items.Count==0, "Nested paths and explicit empty directories survive metadata-only tree construction");
        var wide=Rows(tree).Single(r=>((ArchiveTree.Node)r.Tag).Name=="Wide"); wide.IsExpanded=true;
        check(Rows(wide).Length==1200 && VirtualizingPanel.GetIsVirtualizing(tree), "Large branches retain every entry with tree virtualization");
        check(!Directory.Exists(Path.Combine(root,"Top"))&&!Directory.Exists(Path.Combine(root,"Wide")), "Tree inspection does not extract entries");
        await Show(Path.Combine(zip,"Top"),true);
        check(Rows((TreeView)body.Content).Length==2,"Archive-subfolder preview uses the same relative tree");

        string actualArchive=Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_ARCHIVE");
        string model=Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_MODEL");
        static string Hash(string path) {using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));}
        if(!string.IsNullOrEmpty(actualArchive))
        {
            actualArchive=Path.GetFullPath(actualArchive);
            string hash=Hash(actualArchive);
            await S1Verification.Navigate(Left,Path.GetDirectoryName(actualArchive));SetActive(Left);
            Left.List.SelectedItem=Left.Items.Single(e=>e.Path==actualArchive);PreviewFollow();await sidePreview.Completion;
            tree=(TreeView)PreviewBody.Content; var first=Rows(tree).First(); first.IsExpanded=true;
            check(Rows(first).Length>0 && Hash(actualArchive)==hash,"Reported real ZIP expands without changing its source");
            foreach(string mode in new[]{"dark","light"})
            {
                Settings.NativeMode=mode;ApplyAppearance();await sidePreview.Completion;
                tree=(TreeView)PreviewBody.Content;Rows(tree).First().IsExpanded=true;await Settle();
                check(tree.IsVisible&&tree.ActualHeight>100&&PreviewTitle.Text.Contains(Path.GetFileName(actualArchive)),"Actual side-pane archive tree remains current after "+mode+" theme refresh");
                var label=((StackPanel)Rows(tree).First().Header).Children.OfType<TextBlock>().First();
                check(((SolidColorBrush)label.Foreground).Color==((SolidColorBrush)FindResource("Palette.E8EEF6")).Color,"Tree row text uses the actual "+mode+" theme foreground");
                await S1Verification.Snapshot(this,root,"archive-tree-"+mode);
            }
            Settings.NativeMode="dark";ApplyAppearance();await sidePreview.Completion;sidePreview.Clear();
            Settings.Preview=false;PreviewBody.Content=body;
        }
        var settings = new SettingsWindow(this);
        try
        {
            check(settings.FindName("ProfileText")==null && settings.FindName("StartupText")==null,"About removes the Diagnostics expander and startup telemetry");
            var button=(Button)settings.FindName("OpenSettingsFolder");
            var navigated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Arrived(){if(active.Dir==Settings.Dir&&active.Listed)navigated.TrySetResult();}
            active.StatsChanged+=Arrived;
            try{button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Arrived();await navigated.Task;}
            finally{active.StatsChanged-=Arrived;}
            check(active.Dir==Settings.Dir && button.Content.ToString()=="Open settings folder","About action opens the real profile in Pathweft");
        }
        finally {settings.Close();}

        check(Settings.DisabledPreviewModules.Contains("f3d"),"3D module remains opt-in by default");
        Settings.DisabledPreviewModules.Remove("f3d");
        var supported=F3dViewer.SupportedExtensions;
        File.WriteAllLines(Path.Combine(root,"f3d-readers.txt"),supported);
        check(new[]{".glb",".gltf",".obj",".stl",".ply"}.All(supported.Contains),"Formats come from the actual bundled readers");
        check(PreviewModules.Candidates(new Entry{Path="image.png"}).First().Id=="image","Enabling F3D preserves the normal image reader for overlapping image extensions");
        string obj=Path.Combine(root,"triangle.obj");File.WriteAllText(obj,"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        await Show(obj);check(session.ModuleId=="f3d","OBJ dispatch reaches the embedded F3D reader");
        string gltf=Path.Combine(root,"external.gltf");
        var coordinates=new[]{0f,0f,0f,1f,0f,0f,0f,1f,0f}.SelectMany(BitConverter.GetBytes).ToArray();
        File.WriteAllBytes(Path.Combine(root,"mesh.bin"),coordinates);
        File.WriteAllText(gltf,"""{"asset":{"version":"2.0"},"buffers":[{"uri":"mesh.bin","byteLength":36}],"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}""");
        await Show(gltf);check(session.ModuleId=="f3d","glTF resolves its external relative buffer using the native scene reader");
        string animated=Path.Combine(root,"animated.gltf");
        var document=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(gltf));
        var animatedBytes=coordinates.Concat(new[]{0f,1f,0f,0f,0f,2f,0f,0f}.SelectMany(BitConverter.GetBytes)).ToArray();
        File.WriteAllBytes(Path.Combine(root,"animation.bin"),animatedBytes);
        document["buffers"][0]["uri"]="animation.bin";document["buffers"][0]["byteLength"]=animatedBytes.Length;
        ((System.Text.Json.Nodes.JsonArray)document["bufferViews"]).Add(System.Text.Json.Nodes.JsonNode.Parse("""{"buffer":0,"byteOffset":36,"byteLength":8}"""));
        ((System.Text.Json.Nodes.JsonArray)document["bufferViews"]).Add(System.Text.Json.Nodes.JsonNode.Parse("""{"buffer":0,"byteOffset":44,"byteLength":24}"""));
        ((System.Text.Json.Nodes.JsonArray)document["accessors"]).Add(System.Text.Json.Nodes.JsonNode.Parse("""{"bufferView":1,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]}"""));
        ((System.Text.Json.Nodes.JsonArray)document["accessors"]).Add(System.Text.Json.Nodes.JsonNode.Parse("""{"bufferView":2,"componentType":5126,"count":2,"type":"VEC3"}"""));
        document["animations"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"samplers":[{"input":1,"output":2,"interpolation":"LINEAR"}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]""");
        File.WriteAllText(animated,document.ToJsonString());await Show(animated);await PreviewSession.DrainAsync();
        var animatedRenderer=(F3dViewer)session.ViewerForVerification;
        check(animatedRenderer.LastFrame.Animations==1&&animatedRenderer.LastFrame.End>0,"Native scene reports an animation clip and exposes its time range");
        var animationFrame=animatedRenderer.LastFrame;animatedRenderer.Seek(.5);await animatedRenderer.RenderCompletion;
        check(!animationFrame.Png.SequenceEqual(animatedRenderer.LastFrame.Png),"Animation timeline evaluates and renders the actual animated scene");
        var playButton=S3VerificationButtons(body).Single(b=>b.Content?.ToString()=="Play");
        playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(playButton.Content.ToString()=="Pause","Actual animation control starts frame-driven playback");
        playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(playButton.Content.ToString()=="Play","Actual animation control pauses and releases its frame subscription");
        var previous=(F3dViewer)session.ViewerForVerification;
        if(!string.IsNullOrEmpty(model))
        {
            string hash=Hash(model);check(new FileInfo(model).Length>64L<<20,"Real input exceeds the old 64-MiB limit");
            await Show(model);await PreviewSession.DrainAsync();
            check(!previous.WorkerAlive,"Selection change destroys the old native engine and joins its thread");
            var renderer=(F3dViewer)session.ViewerForVerification;
            var first=renderer.LastFrame;File.WriteAllBytes(Path.Combine(root,"large-model.png"),first.Png);
            check(first is { Offscreen:true, Bitmap.PixelWidth:960 },"Large user model renders through the actual internal offscreen engine");
            // Use the same application-owned drag handler as WPF pointer movement;
            // no physical mouse or desktop focus is synthesized.
            body.Measure(new Size(500,600));body.Arrange(new Rect(0,0,500,600));
            renderer.ApplyDrag(new Vector(80,25),false);await renderer.RenderCompletion;
            check(!first.Camera.SequenceEqual(renderer.LastFrame.Camera)&&!first.Png.SequenceEqual(renderer.LastFrame.Png),"Drag orbit changes both native camera and rendered model");
            var orbit=renderer.LastFrame;renderer.ApplyDrag(new Vector(20,15),true);await renderer.RenderCompletion;
            check(!orbit.Camera.SequenceEqual(renderer.LastFrame.Camera)&&!orbit.Png.SequenceEqual(renderer.LastFrame.Png),"Drag pan changes the native camera and image");
            renderer.HandleKey(Key.Add);await renderer.RenderCompletion;
            renderer.HandleKey(Key.Home);await renderer.RenderCompletion;
            check(first.Camera.Zip(renderer.LastFrame.Camera).All(p=>Math.Abs(p.First-p.Second)<1e-6),"Keyboard zoom/reset restores the fitted camera");
            check(Hash(model)==hash,"Large user model remains byte-identical");
            Settings.DisabledPreviewModules.Add("f3d");session.Refresh();await session.Completion;await PreviewSession.DrainAsync();
            check(session.ModuleId!="f3d"&&!renderer.WorkerAlive,"Disabling the active module releases its native engine/worker");
            Settings.DisabledPreviewModules.Remove("f3d");
        }
        string corrupt=Path.Combine(root,"corrupt.glb");File.WriteAllText(corrupt,"not a model");
        session.Show(new Entry{Path=corrupt,Name="corrupt.glb"});await session.Completion;
        check(session.State=="error","Malformed model reports reader failure rather than claiming a render");
        session.Clear();await PreviewSession.DrainAsync();
        await S1Verification.Navigate(Left,root);SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==obj);
        ToggleQuickView(Left,this);await quickPreview.Completion;
        var spaceRenderer=(F3dViewer)quickPreview.ViewerForVerification;
        check(quickPreview.State=="ready"&&quickPreview.ModuleId=="f3d","Space preview uses the same expanded embedded model reader");
        quick.Close();await PreviewSession.DrainAsync();check(!spaceRenderer.WorkerAlive,"Closing Space preview joins the actual native worker");
        var widget=new SearchWidgetWindow(this);
        try
        {
            widget.ShowSearch();await S1Verification.Navigate(widget.Pane,root);
            widget.Pane.List.SelectedItem=widget.Pane.Items.Single(e=>e.Path==obj);
            widget.Preview();await quickPreview.Completion;
            check(quickPreview.State=="ready"&&quickPreview.ModuleId=="f3d","Widget preview uses the same model reader and native lifecycle");
            quick.Close();widget.Dismiss();await PreviewSession.DrainAsync();
        }
        finally{widget.Shutdown();}
        check(HiddenTrialPresentation.ForegroundViolations==0,"Preview correction checks never activate a desktop window");
    }
    static IEnumerable<Button> S3VerificationButtons(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is Button button)yield return button;foreach(var nested in S3VerificationButtons(child))yield return nested;}
    }
}
#endif
