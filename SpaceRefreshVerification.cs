#if DEBUG
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifySpaceRefresh21(string root,Action<bool,string> check)
    {
        string folder=Directory.CreateDirectory(Path.Combine(root,"watched tree")).FullName;
        string busy=Directory.CreateDirectory(Path.Combine(folder,"busy","nested")).FullName;
        string stable=Directory.CreateDirectory(Path.Combine(folder,"stable","nested")).FullName;
        string payload=Path.Combine(busy,"payload.bin");File.WriteAllBytes(payload,new byte[100]);File.WriteAllBytes(Path.Combine(stable,"stable.bin"),new byte[200]);
        Settings.Preview=false;ApplyPreview();SetActive(Left);
        await S1Verification.Navigate(Left,folder);await Left.SizingCompletion;
        bool native=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_SCENARIO")=="space-ui";
        if(native){check(HiddenTrialPresentation.PrivateSpaceTrial,"Keyboard focus checks run only on the owned never-switched private desktop");Activate();Left.ListForVerification.Focus();}
        bool initialObserved=false,initialSelected=false;
        void Entered(){if(Left.SpaceAnalyzing&&Left.Items.FirstOrDefault() is SpaceEntry&&!initialObserved){initialObserved=true;initialSelected=ReferenceEquals(Left.Selected.SingleOrDefault(),Left.Items.First());}}
        Left.StatsChanged+=Entered;
        try{AnalyzeSpaceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Left.SpaceCompletion;}
        finally{Left.StatsChanged-=Entered;}
        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        File.WriteAllText(Path.Combine(root,"entry-state.json"),JsonSerializer.Serialize(new{status=Status.Text,analyzing=Left.SpaceAnalyzing,columns=(Left.ListForVerification.View as GridView)?.Columns.Select(c=>c.Header?.ToString()).ToArray(),rows=Left.Items.Count(),entries=Left.Items.OfType<SpaceEntry>().Select(row=>new{row.Path,row.ParentPath,state=row.State.ToString()}).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        if(native)check(Left.ListForVerification.IsKeyboardFocusWithin&&Keyboard.FocusedElement is ListViewItem,"The first analysis row receives actual keyboard focus on entry");
        var expander=PreviewChildren<Button>(Left.ListForVerification).First(button=>AutomationProperties.GetName(button)=="Expand or collapse folder");
        var glyph=PreviewChildren<TextBlock>(expander).Single();
        var ink=new FormattedText(glyph.Text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(glyph.FontFamily,glyph.FontStyle,glyph.FontWeight,glyph.FontStretch),glyph.FontSize,Brushes.White,1);
        var presenter=PreviewChildren<ContentPresenter>(expander).First();
        bool unclipped=presenter.ActualWidth>=ink.WidthIncludingTrailingWhitespace&&presenter.ActualHeight>=ink.Height&&expander.ActualWidth==18;
        var visits=new ConcurrentDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        SpaceScan.BeforeEnumeration=(path,token)=>visits.AddOrUpdate(path,1,(_,count)=>count+1);
        var unrelated=(SpaceEntry)Left.Items.Single(entry=>entry.Name=="stable");
        bool unrelatedReset=false,knownFlashed=false;
        var secondStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPresented=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observed()
        {
            unrelatedReset|=unrelated.State is SpaceState.Pending or SpaceState.Scanning;
            knownFlashed|=Left.Items.OfType<SpaceEntry>().Any(entry=>entry.DisplaySize>=0&&entry.SpaceStatus is "Pending" or "Measuring…");
            if(secondStarted.Task.IsCompleted)secondPresented.TrySetResult();
        }
        Left.StatsChanged+=Observed;
        try
        {
            var grown=Sizes(Left,()=>Left.FolderTotal==327&&!Left.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(payload);stream.SetLength(127);});await grown;
            await Left.SpaceCompletion;
            File.WriteAllText(Path.Combine(root,"refresh-observed.json"),JsonSerializer.Serialize(new{initialSelected,unclipped,unrelatedReset,knownFlashed,rootVisits=visits.GetValueOrDefault(folder),stableVisits=visits.GetValueOrDefault(Path.GetDirectoryName(stable)),visits},new JsonSerializerOptions{WriteIndented=true}));
            check(initialObserved&&initialSelected,"Entering analysis selects the first available top row exactly once");
            check(unclipped,"Compact18-DIP expansion button contains the complete rendered chevron ink");
            check(visits.GetValueOrDefault(folder)<=1&&!visits.Keys.Any(path=>SizeCache.Within(path,Path.GetDirectoryName(stable)))&&!unrelatedReset,"A watched branch rewrite does not rewalk or reset unrelated completed branches");
            check(!knownFlashed,"Known analysis rows retain their completed presentation during background replacement");
            Left.ListForVerification.SelectedItem=unrelated;
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int held=0;
            SpaceScan.BeforeEnumeration=(path,token)=>
            {
                visits.AddOrUpdate(path,1,(_,count)=>count+1);
                if(path==Path.GetDirectoryName(busy))
                {
                    int pass=Interlocked.Increment(ref held);
                    if(pass==1){entered.TrySetResult();release.Task.WaitAsync(token).GetAwaiter().GetResult();}
                    if(pass==2){secondStarted.TrySetResult();secondPresented.Task.WaitAsync(token).GetAwaiter().GetResult();}
                }
            };
            var replaced=Sizes(Left,()=>Left.FolderTotal==340&&!Left.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(payload);stream.SetLength(130);});await entered.Task;
            var invalidated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Invalidated(string path){if(path==payload)invalidated.TrySetResult();}
            SizeCache.Invalidated+=Invalidated;
            try{await Task.Run(()=>{using var stream=File.OpenWrite(payload);stream.SetLength(140);});await invalidated.Task;}
            finally{SizeCache.Invalidated-=Invalidated;release.TrySetResult();}
            await replaced;await Left.SpaceCompletion;
            check(secondStarted.Task.IsCompleted&&ReferenceEquals(Left.Selected.Single(),unrelated)&&!knownFlashed&&!unrelatedReset,"An actual invalidation during a held pass coalesces its replacement without losing selection or flashing pending states");
            SpaceScan.BeforeEnumeration=(path,token)=>visits.AddOrUpdate(path,1,(_,count)=>count+1);
            var shrunk=Sizes(Left,()=>Left.FolderTotal==211&&!Left.SizesPending);
            await Task.Run(()=>{using var stream=File.OpenWrite(payload);stream.SetLength(11);});await shrunk;
            string moved=Path.Combine(folder,"renamed");
            var renamed=Sizes(Left,()=>Left.Items.Any(entry=>entry.Name=="renamed")&&Left.Items.All(entry=>entry.Name!="busy")&&!Left.SizesPending);
            await Task.Run(()=>Directory.Move(Path.GetDirectoryName(busy),moved));await renamed;
            var removed=Sizes(Left,()=>Left.FolderTotal==200&&!Left.SizesPending);
            await Task.Run(()=>Directory.Move(moved,Path.Combine(root,"removed branch")));await removed;
            check(Left.FolderTotal==200&&ReferenceEquals(Left.Items.Single(entry=>entry.Name=="stable"),unrelated)&&ReferenceEquals(Left.Selected.Single(),unrelated),"Incremental shrink/rename/removal reaches exact totals and preserves unaffected selection/row identity");
            Left.Refresh(force:true);await Left.SpaceCompletion;
            check(Left.FolderTotal==200&&Left.SpaceSummary.Files==1&&Left.SpaceSummary.Folders==2,"Explicit refresh still performs a complete exact metadata measurement");
        }
        finally{Left.StatsChanged-=Observed;SpaceScan.BeforeEnumeration=null;}
        await Left.EndSpaceAsync();await Left.StopSizingAsync();
        await VerifyCommandStates21(root,check);
        check(HiddenTrialPresentation.ForegroundViolations==0,"Analysis refresh verification does not activate the user's desktop");
    }

    async Task VerifyCommandStates21(string root,Action<bool,string> check)
    {
        string hidden=Path.Combine(Left.Dir,".command-hidden");File.WriteAllText(hidden,"");
        static byte[] Pixels(ImageSource image)
        {
            var visual=new DrawingVisual();using(var context=visual.RenderOpen())context.DrawImage(image,new Rect(0,0,16,16));
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(16,16,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var bytes=new byte[16*16*4];bitmap.CopyPixels(bytes,16*4,0);return bytes;
        }
        foreach(bool light in new[]{false,true})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=light?"light":"dark";Settings.TextScale=scale;ApplyAppearance();
            Settings.ShowHidden=false;Settings.SinglePane=true;Settings.Preview=false;ApplyPanes();ApplyPreview();UpdateLayout();
            var hiddenOff=Pixels(((Image)HiddenToggle.Content).Source);var onePane=Pixels(((Image)PanesCommand.Content).Source);
            foreach(var button in new[]{HiddenToggle,PanesCommand,PreviewCommand})button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Sizes(Left,()=>Left.Items.Any(entry=>entry.Path==hidden));UpdateLayout();
            check(!hiddenOff.SequenceEqual(Pixels(((Image)HiddenToggle.Content).Source)),"The hidden-files eye visibly changes between crossed/off and open/on: "+light+scale);
            check(!onePane.SequenceEqual(Pixels(((Image)PanesCommand.Content).Source))&&PanesCommand.ToolTip.ToString().StartsWith("Show one pane"),"The current two-pane icon differs from one pane and its tooltip offers the opposite action: "+light+scale);
            await Left.ToggleSpaceAsync();await Left.SpaceCompletion;UpdateCommandStates();UpdateLayout();
            foreach(var button in new[]{HiddenToggle,PanesCommand,PreviewCommand,AnalyzeSpaceCommand})
            {
                var border=(Border)button.Template.FindName("Bd",button);
                check(button.Tag as string=="on"&&border.BorderBrush!=Brushes.Transparent&&border.BorderBrush.ToString()==((Brush)FindResource("Accent")).ToString()&&button.ActualWidth<=30,
                    "Active toggle has a persistent compact accent outline and accessible state: "+button.Name+light+scale);
            }
            check(AutomationProperties.GetItemStatus(HiddenToggle)=="On"&&AutomationProperties.GetItemStatus(PanesCommand)=="Two panes"&&AutomationProperties.GetItemStatus(PreviewCommand)=="On"&&AutomationProperties.GetItemStatus(AnalyzeSpaceCommand)=="On","Every bottom toggle exposes its current state to accessibility: "+light+scale);
            string path=Path.Combine(root,$"command-states-{(light?"light":"dark")}-{scale*100:0}.png");
            var body=(FrameworkElement)Content;var bounds=body.TransformToAncestor(this).TransformBounds(new Rect(body.RenderSize));
            var capture=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(bounds.Width),(int)Math.Ceiling(bounds.Height),96,96,PixelFormats.Pbgra32);capture.Render(body);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(capture));using(var output=File.Create(path))png.Save(output);
            await Left.EndSpaceAsync();
            foreach(var button in new[]{HiddenToggle,PanesCommand,PreviewCommand})button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Sizes(Left,()=>Left.Items.All(entry=>entry.Path!=hidden));UpdateLayout();
            foreach(var button in new[]{HiddenToggle,PanesCommand,PreviewCommand,AnalyzeSpaceCommand})check(button.Tag==null,"Inactive toggle clears its active state: "+button.Name+light+scale);
        }
        Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();await Left.StopSizingAsync();
    }
}
#endif
