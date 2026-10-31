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
using System.Windows.Automation;

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
            PreviewBody.Content=body;
            session.Show(new Entry { Path = path, Name = Path.GetFileName(path), IsDir = directory });
            await session.Completion;
            body.Measure(new Size(380,640));body.Arrange(new Rect(0,0,380,640));body.UpdateLayout();
            check(session.State == "ready", "Preview loads through the actual session: " + Path.GetExtension(path));
        }
        async Task Settle() => await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        string folder=Path.Combine(root,"folder-tree");Directory.CreateDirectory(Path.Combine(folder,"Nested","Deep"));Directory.CreateDirectory(Path.Combine(folder,"Empty"));
        File.WriteAllText(Path.Combine(folder,"Nested","Deep","leaf.txt"),"leaf");File.WriteAllText(Path.Combine(folder,".hidden"),"hidden");
        await Show(folder,true);var folderTree=(TreeView)body.Content;
        check(folderTree.Items.OfType<TreeViewItem>().Count()==2,"Native folder preview uses concise folder-first tree rows and hides dotfiles");
        var branch=folderTree.Items.OfType<TreeViewItem>().Single(r=>((ArchiveTree.Node)r.Tag).Name=="Nested");
        check(branch.Items[0] is string,"Native tree does not enumerate grandchildren before expansion");
        ((IExpandCollapseProvider)new TreeViewItemAutomationPeer(branch).GetPattern(PatternInterface.ExpandCollapse)).Expand();await ((ArchiveTree.Node)branch.Tag).Expansion;
        var deep=branch.Items.OfType<TreeViewItem>().Single();deep.IsExpanded=true;await ((ArchiveTree.Node)deep.Tag).Expansion;
        check(deep.Items.OfType<TreeViewItem>().Single().ToolTip.ToString()==Path.Combine(folder,"Nested","Deep","leaf.txt"),"Accessible native branch expansion asynchronously resolves real nested files");
        var emptyBranch=folderTree.Items.OfType<TreeViewItem>().Single(r=>((ArchiveTree.Node)r.Tag).Name=="Empty");emptyBranch.IsExpanded=true;await ((ArchiveTree.Node)emptyBranch.Tag).Expansion;
        check(emptyBranch.Items.OfType<TextBlock>().Single().Text=="Empty folder","Empty native folders have an explicit empty state");
        PaneView.ShowHidden=true;await Show(folder,true);check(((TreeView)body.Content).Items.OfType<TreeViewItem>().Count()==3,"Native folder tree honors enabled hidden-file visibility");PaneView.ShowHidden=false;
        Settings.Preview=true;await S1Verification.Navigate(Left,root);SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==folder);PreviewFollow();await sidePreview.Completion;await Settle();
        double previewWidth=Settings.PreviewWidth;
        foreach(bool single in new[]{true,false})
        {
            SetSinglePane(single);Left.List.UnselectAll();PreviewFollow();await PreviewSession.DrainAsync();await Settle();
            check(Settings.Preview&&PreviewPane.Visibility==Visibility.Collapsed&&PreviewSplitter.Visibility==Visibility.Collapsed&&PreviewColumn.Width.Value==0&&PreviewGap.Width.Value==0,"Empty selection collapses the complete preview without changing its enabled preference: "+single);
            Left.List.SelectedItem=Left.Items.Single(e=>e.Path==folder);PreviewFollow();await sidePreview.Completion;await Settle();
            check(PreviewPane.Visibility==Visibility.Visible&&PreviewColumn.Width.Value==previewWidth&&PreviewBody.Content is TreeView,"New selection restores the saved preview width and native folder tree: "+single);
        }
        Settings.Preview=false;ApplyPreview();Left.List.UnselectAll();Left.List.SelectedItem=Left.Items.Single(e=>e.Path==folder);PreviewFollow();
        check(PreviewPane.Visibility==Visibility.Collapsed,"Selection does not re-enable a deliberately disabled preview");
        Settings.Preview=true;ApplyPreview();await sidePreview.Completion;
        ToggleHidden();await sidePreview.Completion;await Settle();
        check(((TreeView)PreviewBody.Content).Items.OfType<TreeViewItem>().Any(r=>((ArchiveTree.Node)r.Tag).Name==".hidden"),"Actual Ctrl+H visibility action refreshes the selected native folder preview");
        ToggleHidden();await sidePreview.Completion;
        string executable=Path.Combine(AppContext.BaseDirectory,"Pathweft.exe");
        string executableHash=HashFile(executable);await Show(executable);
        check(session.ModuleId=="executable"&&ExecutableDetails.Read(executable,default).Any(p=>p.Name=="Product version"&&p.Value.StartsWith(typeof(MainWindow).Assembly.GetName().Version.ToString(3))),"Executable selection renders actual version resources without launching the file");
        check(HashFile(executable)==executableHash,"Executable metadata preview preserves its source");
        var signed=ExecutableDetails.Read(Path.Combine(Environment.SystemDirectory,"notepad.exe"),default);
        check(signed.Any(p=>p.Name=="Company")&&signed.Any(p=>p.Name=="File version"),"System executable exposes actual company/version metadata without being loaded or executed");
        string noVersion=Path.Combine(root,"no-version.exe");File.WriteAllText(noVersion,"no PE version resource");await Show(noVersion);
        check(ExecutableDetails.Read(noVersion,default).Any(p=>p.Name=="Executable header"),"Missing resources and malformed executable headers remain a readable details view");
        Settings.DisabledPreviewModules.Add("executable");session.Refresh();await session.Completion;
        check(session.ModuleId!="executable","Executable-details module respects its actual Settings disable state");
        Settings.DisabledPreviewModules.Remove("executable");
        async Task LayoutPreview(string file,string name,bool directory=false)
        {
            SetSinglePane(true);SetActive(Left);
            await S1Verification.Navigate(Left,Path.GetDirectoryName(file));
            Left.List.SelectedItem=Left.Items.Single(e=>e.Path==file);PreviewFollow();await sidePreview.Completion;
            foreach(string mode in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
            {
                Settings.NativeMode=mode;Settings.TextScale=scale;ApplyAppearance();await sidePreview.Completion;await Settle();
                Width=1200;Height=820;await Settle();
                check(sidePreview.Entry.Path==file&&PreviewBody.Content is not TextBox&&PreviewBody.ActualWidth>150&&PreviewBody.ActualHeight>200,"Actual selected-file preview remains current with a usable themed viewport: "+name+mode+scale);
                if(name=="animation")
                {var current=PreviewChildren<ComboBox>(PreviewBody).Single();current.ApplyTemplate();var face=PreviewChildren<System.Windows.Controls.Primitives.ToggleButton>(current).First();check(((SolidColorBrush)face.Foreground).Color==((SolidColorBrush)FindResource("Palette.E8EEF6")).Color&&current.ActualHeight>=32&&current.SelectedIndex==0,"Animation selector has a readable selected clip and the actual "+mode+" theme foreground");}
                await S1Verification.Snapshot(this,root,$"preview-{name}-{mode}-{scale*100:0}");
            }
            Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();await sidePreview.Completion;await Settle();await Show(file,directory);
        }
        await LayoutPreview(folder,"folder",true);
        await LayoutPreview(executable,"executable");
        string pdf=Path.Combine(root,"pages.pdf");WritePdf(pdf);await Show(pdf);await Settle();
        var pdfButtons=S3VerificationButtons(body).ToArray();
        var nextPage=pdfButtons.Single(b=>AutomationProperties.GetName(b)=="Next page");
        var previousPage=pdfButtons.Single(b=>AutomationProperties.GetName(b)=="Previous page");
        check(nextPage.Content is TextBlock&&nextPage.IsEnabled&&!previousPage.IsEnabled,"PDF navigation uses named icons with correct first-page state");
        nextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PreviewSession.DrainAsync();
        check(!nextPage.IsEnabled&&previousPage.IsEnabled,"PDF next-page icon renders the actual second page");
        previousPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PreviewSession.DrainAsync();
        check(nextPage.IsEnabled&&!previousPage.IsEnabled,"PDF previous-page icon returns to the first page");
        await LayoutPreview(pdf,"pdf");
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
        var playButton=S3VerificationButtons(body).Single(b=>AutomationProperties.GetName(b)=="Play");
        check(S3VerificationButtons(body).All(b=>b.Content is TextBlock&&AutomationProperties.GetName(b).Length>0&&b.ToolTip!=null),"Model actions use icons with tooltips and accessible names");
        playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(AutomationProperties.GetName(playButton)=="Pause","Actual animation control starts frame-driven playback");
        playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(AutomationProperties.GetName(playButton)=="Play","Actual animation control pauses and releases its frame subscription");
        await LayoutPreview(animated,"animation");
        var clip=PreviewChildren<ComboBox>(body).Single();clip.ApplyTemplate();
        check(clip.Style==FindResource("PreviewClipCombo")&&clip.SelectedIndex==0,"Animation selector uses the actual themed template and selected clip");
        var popup=(System.Windows.Controls.Primitives.Popup)clip.Template.FindName("PART_Popup",clip);
        check(((SolidColorBrush)((Border)popup.Child).Background).Color==((SolidColorBrush)FindResource("Palette.20242A")).Color,"Animation popup uses the current themed surface without opening a desktop popup");
        var previous=(F3dViewer)session.ViewerForVerification;
        if(!string.IsNullOrEmpty(model))
        {
            string hash=Hash(model);check(new FileInfo(model).Length>64L<<20,"Real input exceeds the old 64-MiB limit");
            await Show(model);await PreviewSession.DrainAsync();
            check(!previous.WorkerAlive,"Selection change destroys the old native engine and joins its thread");
            var renderer=(F3dViewer)session.ViewerForVerification;
            var first=renderer.LastFrame;File.WriteAllBytes(Path.Combine(root,"large-model.png"),first.Png);
            var bgra=new System.Windows.Media.Imaging.FormatConvertedBitmap(first.Bitmap,PixelFormats.Bgra32,null,0);var corner=new byte[4];bgra.CopyPixels(new Int32Rect(0,0,1,1),corner,4,0);
            check(corner[3]==0,"Native model background is transparent, eliminating the opaque rectangular cutoff");
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
        Settings.Preview=true;ApplyPreview();await sidePreview.Completion;
        var collapseRenderer=(F3dViewer)sidePreview.ViewerForVerification;
        Left.List.UnselectAll();PreviewFollow();await PreviewSession.DrainAsync();
        check(PreviewPane.Visibility==Visibility.Collapsed&&!collapseRenderer.WorkerAlive,"Clearing model selection collapses the side pane and joins its native render worker");
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==obj);PreviewFollow();await sidePreview.Completion;
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
    static string HashFile(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    static IEnumerable<T> PreviewChildren<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var next in PreviewChildren<T>(child))yield return next;}}
    static void WritePdf(string path)
    {
        var text=new System.Text.StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};
        foreach(string value in new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>"})
        {offsets.Add(text.Length);text.Append($"{offsets.Count-1} 0 obj\n{value}\nendobj\n");}
        int xref=text.Length;text.Append("xref\n0 5\n0000000000 65535 f \n");foreach(int offset in offsets.Skip(1))text.Append($"{offset:0000000000} 00000 n \n");
        text.Append($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");File.WriteAllText(path,text.ToString(),System.Text.Encoding.ASCII);
    }
    static IEnumerable<Button> S3VerificationButtons(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is Button button)yield return button;foreach(var nested in S3VerificationButtons(child))yield return nested;}
    }
}
#endif
