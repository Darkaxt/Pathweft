#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPreviewModes13(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        Settings.DisabledPreviewModules.Remove("f3d"); // Explicit opt-in in this isolated qualification profile only.
        foreach(string name in new[]{"document-pages.docx","sdr.mp4","audio-playback.wav"})File.Copy(Path.Combine(inputs,name),Path.Combine(root,name));
        File.WriteAllText(Path.Combine(root,"table.csv"),"Name,Value\nOne,1\nTwo,2");
        File.WriteAllText(Path.Combine(root,"other.txt"),"Another selected item");
        File.WriteAllText(Path.Combine(root,"static.html"),"<html><body><h1>Preserved HTML preview</h1>"+string.Concat(Enumerable.Range(0,100).Select(i=>$"<p>Line {i}</p>"))+"</body></html>");
        Directory.CreateDirectory(Path.Combine(root,"folder"));File.WriteAllText(Path.Combine(root,"folder","inside.txt"),"Folder child");
        string archive=Path.Combine(root,"preview.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"other.txt"),"note.txt");
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();
        async Task Layout(){await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);if(previewWindow!=null)await previewWindow.Dispatcher.InvokeAsync(previewWindow.UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);}
        async Task Select(string path)
        {
            if(previewMode!=PreviewMode.Sidebar)MovePreviewHost(PreviewMode.Sidebar);
            previewPinned=false;previewShown=null;Settings.Preview=true;
            Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));ApplyPreview();PreviewFollow();
            await sidePreview.Completion;await Layout();check(sidePreview.State=="ready","Mode workflow loads actual preview: "+Path.GetFileName(path));
            await Left.IconsCompletionForVerification;await Layout();
            check(PreviewTitle.Text==sidePreview.Entry.Name&&PreviewHeader.DataContext==sidePreview.Entry,"Preview heading shows the filename without a renderer label");
            if(PreviewAppIcon.Source!=null)check(PreviewAppIcon.TranslatePoint(new Point(),PreviewHeader).X<PreviewTitle.TranslatePoint(new Point(),PreviewHeader).X,"The actual app icon appears to the left of the filename");
        }
        async Task Moves(string label,Action state)
        {
            var viewer=sidePreview.ViewerForVerification;var content=PreviewBody.Content;long generation=sidePreview.Generation;var entry=sidePreview.Entry;
            int windows=Application.Current.Windows.Count;
            PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            check(previewMode==PreviewMode.PictureInPicture&&previewWindow==null&&Application.Current.Windows.Count==windows&&PreviewColumn.Width.Value==0&&PreviewGap.Width.Value==0&&PreviewSplitter.Visibility==Visibility.Collapsed,"PiP is an in-app corner overlay and releases the full sidebar column: "+label);
            check(PreviewPane.ActualWidth<=400.1&&PreviewPane.ActualHeight<=320.1&&PreviewPane.ActualWidth>200&&PreviewPane.ActualHeight>100,"PiP has compact measured client bounds: "+label);
            if(!previewPinned)PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Left.List.SelectedItem=Left.Items.Single(item=>item.Path==Path.Combine(root,"other.txt"));PreviewFollow();
            check(previewPinned&&sidePreview.Entry==entry&&sidePreview.Generation==generation,"Explicit pin keeps the detached item while browsing: "+label);
            state();await S1Verification.Snapshot(this,root,label+"-pip");
            PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            check(previewMode==PreviewMode.Windowed&&previewWindow!=null&&previewWindow.Content==PreviewPane&&PreviewColumn.Width.Value==0,"Windowed uses an independent resizable floating window: "+label);
            check(ReferenceEquals(viewer,sidePreview.ViewerForVerification)&&ReferenceEquals(content,PreviewBody.Content)&&sidePreview.Generation==generation,"Switching hosts retains the same renderer, content and request: "+label);state();
            check(PreviewHeader.Children.OfType<Button>().All(button=>button.IsVisible&&AutomationProperties.GetName(button).Length>0),"Every actual mode/close button has an accessible name: "+label);
            await S1Verification.Snapshot(previewWindow,root,label+"-windowed");
            previewWindow.Close();await Layout();
            check(previewMode==PreviewMode.Sidebar&&previewWindow==null&&PreviewColumn.Width.Value==Settings.PreviewWidth&&PreviewPane.Parent==PaneArea&&sidePreview.Entry==entry,"Closing the floating window restores the same preview to its saved sidebar: "+label);
            PreviewFollow();check(sidePreview.Entry==entry&&sidePreview.Generation==generation,"Restoration is stable until the user changes selection again: "+label);state();
            if(Left.Items.FirstOrDefault(item=>item.Path==entry.Path) is {} selected)
            {Left.List.SelectedItem=selected;PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await sidePreview.Completion;}
        }
        await Select(Path.Combine(root,"document-pages.docx"));var office=(OfficeViewer)sidePreview.ViewerForVerification;
        var number=PreviewChildren<TextBox>(PreviewBody).Single(box=>AutomationProperties.GetName(box)=="Page number");number.Text="64";
        number.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(number),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});await office.Completion;
        int worker=office.WorkerForVerification;
        await Moves("office",()=>check(office.WorkerForVerification==worker&&PreviewChildren<TextBlock>(PreviewBody).Any(text=>text.Text=="Page 64 of 64"),"Host changes preserve the native document and final page"));
        foreach(var theme in new[]{("native-dark",true,"dark"),("native-light",true,"light"),("original",false,"dark")})
        {
            Settings.NativeTheme=theme.Item2;Settings.NativeMode=theme.Item3;Settings.TextScale=1.5;Width=1500;Height=1000;ApplyAppearance();await Layout();
            await Moves("office-scaled-"+theme.Item1,()=>check(office.WorkerForVerification==worker&&PreviewChildren<TextBlock>(PreviewBody).Any(text=>text.Text=="Page 64 of 64"),"Scaled/themed hosts retain the same actual document and requested page"));
        }
        Settings.NativeTheme=true;Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();await Layout();
        check(PreviewAppIcon.Source==PreviewControls.RendererIcon("wordcraft"),"Word preview identifies its actual primary renderer by icon");
        using(var process=System.Diagnostics.Process.GetProcessById(worker)){process.Kill();await process.WaitForExitAsync();}
        PreviewChildren<Button>(PreviewBody).Single(button=>AutomationProperties.GetName(button)=="Previous page").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await office.Completion;
        check(sidePreview.RendererId=="LibreOffice"&&PreviewAppIcon.Source==PreviewControls.RendererIcon("libreoffice")&&((string)PreviewAppIcon.ToolTip).Contains("wordcraft failed"),"A failed primary switches the live header to LibreOffice's icon with its recovery reason in the tooltip");
        var warning=PreviewChildren<TextBlock>(PreviewBody).Single(text=>AutomationProperties.GetName(text)=="Preview warning");
        check(warning.Text=="Error loading: document-pages.docx"&&warning.TextWrapping==TextWrapping.NoWrap&&warning.HorizontalAlignment==HorizontalAlignment.Right&&warning.ToolTip is string detail&&detail.Contains("wordcraft failed"),"Office recovery shows only a compact bottom-right filename warning with full details on hover");
        check(warning.Foreground is SolidColorBrush {Color:var warningColor}&&warningColor==Color.FromRgb(255,180,84),"Office recovery warning uses the dark-theme orange status color");
        await Moves("office-fallback",()=>check(PreviewAppIcon.Source==PreviewControls.RendererIcon("libreoffice"),"Fallback identification follows the same preview through all modes"));
        await Select(Path.Combine(root,"table.csv"));var table=PreviewChildren<CheckBox>(PreviewBody).Single();table.IsChecked=false;
        await Moves("table",()=>check(!table.IsChecked.Value,"Host changes retain table navigation controls and header state"));
        await Select(Path.Combine(root,"static.html"));var html=(HtmlViewer)sidePreview.ViewerForVerification;var core=html.CoreForVerification;uint browser=core.BrowserProcessId;
        await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"window.scrollTo(0,300)\"}");
        await Moves("html",()=>check(ReferenceEquals(core,html.CoreForVerification)&&core.BrowserProcessId==browser,"Host changes retain the same isolated HTML browser"));
        string scroll=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"window.scrollY\",\"returnByValue\":true}");using(var json=JsonDocument.Parse(scroll))check(json.RootElement.GetProperty("result").GetProperty("value").GetDouble()==300,"HTML scroll position survives sidebar/PiP/Windowed restoration");
        await Select(Path.Combine(root,"sdr.mp4"));var media=((BuiltinViewer)sidePreview.ViewerForVerification).MediaForVerification;var player=media.Player;IntPtr child=media.HostForVerification.Child;
        await Moves("video-paused",()=>check(ReferenceEquals(player,media.Player)&&media.HostForVerification.Child==child&&player.Property("pause")=="yes","Paused video retains its actual native player and HWND across hosts"));
        await player.CommandAsync("set","loop-file","inf");await player.CommandAsync("set","pause","no");
        await Moves("video-playing",()=>check(media.HostForVerification.Child==child&&player.Property("pause")=="no"&&!player.Completion.IsCompleted,"Playing video continues through host changes without a new renderer"));
        string model=@"D:\Downloads\SNES_PAL_-_Super_Mario_World-31f4914a.glb";
        // Existing retained inert model is read-only, as in the established model checks.
        await S1Verification.Navigate(Left,Path.GetDirectoryName(model));await Select(model);
        var renderer=(F3dViewer)sidePreview.ViewerForVerification;renderer.HandleKey(System.Windows.Input.Key.Left);await renderer.RenderCompletion;renderer.Seek(3);await renderer.RenderCompletion;
        var timeline=PreviewChildren<Slider>(PreviewBody).Single();double position=timeline.Value;
        var up=renderer.LastFrame.Up.ToArray();var focal=renderer.LastFrame.Focal.ToArray();
        // For the navigation-away check use a real sibling selection in this folder.
        PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
        PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await S1Verification.Navigate(Left,root);MovePreviewHost(PreviewMode.Sidebar);await Layout();
        await Moves("model",()=>check(renderer.WorkerAlive&&timeline.Value==position,"Model host changes keep the native worker and animation position"));
        await renderer.RenderCompletion;check(up.Zip(renderer.LastFrame.Up).All(pair=>Math.Abs(pair.First-pair.Second)<1e-6)&&focal.Zip(renderer.LastFrame.Focal).All(pair=>Math.Abs(pair.First-pair.Second)<1e-6),"Model camera orientation and focal point survive host-size changes");
        await Select(Path.Combine(root,"folder"));var tree=PreviewChildren<TreeView>(PreviewBody).Single();
        await Moves("folder",()=>check(ReferenceEquals(tree,PreviewChildren<TreeView>(PreviewBody).Single()),"Native folder tree remains the same expanded-view instance"));
        await S1Verification.Navigate(Left,archive);await Select(archive+"\\note.txt");
        string lease=sidePreview.ArchiveDirectoryForVerification;long archiveGeneration=sidePreview.Generation;
        PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();await S1Verification.Navigate(Left,root);PreviewFollow();
        check(lease!=null&&Directory.Exists(lease)&&sidePreview.Generation==archiveGeneration,"PiP browsing preserves its archive extraction lease");
        PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();previewWindow.Close();await Layout();
        check(Directory.Exists(lease)&&sidePreview.Generation==archiveGeneration,"Windowed restoration retains the same archive input lease");
        PreviewCloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PreviewSession.DrainAsync();
        check(!Directory.Exists(lease)&&!Settings.Preview&&previewMode==PreviewMode.Sidebar&&previewWindow==null&&PreviewColumn.Width.Value==0,"Explicit preview close releases its lease and disables the sidebar preference");
        await Select(Path.Combine(root,"sdr.mp4"));media=((BuiltinViewer)sidePreview.ViewerForVerification).MediaForVerification;player=media.Player;
        PreviewPipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
        double oldWidth=Width;Width=860;await Layout();
        check(PreviewPane.ActualWidth<=PaneArea.ActualWidth-24&&PreviewPane.ActualHeight<=PaneArea.ActualHeight-24,"PiP stays within the actual narrow app client");Width=oldWidth;
        Settings.DisabledPreviewModules.Add("media");RefreshPreviewModules();await sidePreview.Completion;await PreviewSession.DrainAsync();
        check(player.Completion.IsCompletedSuccessfully&&sidePreview.ModuleId!="media","Disabling the active PiP module tears down its actual native player");Settings.DisabledPreviewModules.Remove("media");
        MovePreviewHost(PreviewMode.Sidebar);await Select(Path.Combine(root,"sdr.mp4"));player=((BuiltinViewer)sidePreview.ViewerForVerification).MediaForVerification.Player;
        PreviewWindowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();var finalWindow=previewWindow;
        Closed+=(_,_)=>check(player.Completion.IsCompletedSuccessfully&&!finalWindow.IsVisible,"App exit drains the detached renderer and closes its floating window");
        check(HiddenTrialPresentation.ForegroundViolations==0,"All preview-mode workflows avoid foreground takeover");
    }
}
#endif
