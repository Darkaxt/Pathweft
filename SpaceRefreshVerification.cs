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
        // An expanded row under a folder F5 takes from the index (below) must be refreshed too.
        Left.SpaceExpandForVerification((SpaceEntry)Left.Items.Single(entry=>entry.Name=="busy"));await Left.SpaceCompletion;
        var visits=new ConcurrentDictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        SpaceScan.BeforeEnumeration=(path,token)=>visits.AddOrUpdate(path,1,(_,count)=>count+1);
        var unrelated=(SpaceEntry)Left.Items.Single(entry=>entry.Name=="stable");
        bool unrelatedReset=false,knownFlashed=false;
        void Observed()
        {
            unrelatedReset|=unrelated.State is SpaceState.Pending or SpaceState.Scanning;
            knownFlashed|=Left.Items.OfType<SpaceEntry>().Any(entry=>entry.DisplaySize>=0&&entry.SpaceStatus is "Pending" or "Measuring…");
        }
        // Waits until the watcher reported a change of this file, then long enough for the index service to read it.
        async Task Watched(string path,Action change)
        {
            var invalidated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Invalidated(string changed){if(changed==path)invalidated.TrySetResult();}
            SizeCache.Invalidated+=Invalidated;
            try{await Task.Run(change);await invalidated.Task;}finally{SizeCache.Invalidated-=Invalidated;}
            await Task.Delay(PaneView.IndexCatchUp+TimeSpan.FromMilliseconds(400));
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        }
        Left.StatsChanged+=Observed;
        try
        {
            // F5 takes the index's totals, but a folder written in the last Settle is measured on disk by design
            // (SEARCH-10), so the fixture's folders must be older than that.
            var age=DateTime.UtcNow-Directory.GetLastWriteTimeUtc(busy);
            if(age<FileExplorer.Search.IndexedSearch.Settle)await Task.Delay(FileExplorer.Search.IndexedSearch.Settle-age+TimeSpan.FromMilliseconds(250));
            Left.ListForVerification.SelectedItem=unrelated;
            // CORE-30 (owner: "who cares about the disk analysis be desynced by a couple of minutes"): a change on disk
            // leaves the analysis as it was and only marks it changed.
            await Watched(payload,()=>{using var stream=File.OpenWrite(payload);stream.SetLength(127);});
            await Left.SpaceCompletion;
            var busyRow=Left.Items.OfType<SpaceEntry>().Single(entry=>entry.Name=="busy");
            File.WriteAllText(Path.Combine(root,"refresh-observed.json"),JsonSerializer.Serialize(new{initialSelected,unclipped,unrelatedReset,knownFlashed,footer=Left.Footer.Text,visits},new JsonSerializerOptions{WriteIndented=true}));
            check(initialObserved&&initialSelected,"Entering analysis selects the first available top row exactly once");
            check(unclipped,"Compact18-DIP expansion button contains the complete rendered chevron ink");
            check(visits.IsEmpty&&Left.FolderTotal==300&&busyRow.Size==100&&!Left.SizesPending&&Left.SpaceStale&&Left.Footer.Text.Contains("changed since this snapshot, F5 to refresh"),
                "A change on disk reads no folder and keeps the snapshot (total "+Left.FolderTotal+", "+visits.Count+" folders read); the footer says it changed and F5 refreshes: "+Left.Footer.Text);
            // CORE-30 ("If I want a refresh, allow me to press F5"): F5 takes the index service's totals.
            Left.Refresh(force:true);await Left.SpaceCompletion;
            busyRow=Left.Items.OfType<SpaceEntry>().Single(entry=>entry.Name=="busy");
            check(!Left.SpaceStale&&Left.FolderTotal==327&&busyRow.State==SpaceState.Indexed&&busyRow.Size==127&&visits.GetValueOrDefault(busy)==0&&!visits.Keys.Any(path=>SizeCache.Within(path,Path.GetDirectoryName(stable))),
                "F5 refreshes the analysis from the index service's totals (busy: "+busyRow.SpaceStatus+", "+busyRow.Size+" bytes, nested read "+visits.GetValueOrDefault(busy)+" times; total "+Left.FolderTotal+")");
            var nestedRow=Left.Items.OfType<SpaceEntry>().Single(entry=>entry.Path==busy);
            check(nestedRow.State==SpaceState.Indexed&&nestedRow.Size==127,"F5 also refreshes an expanded row inside a folder taken from the index (nested: "+nestedRow.SpaceStatus+", "+nestedRow.Size+" bytes)");
            check(!knownFlashed&&!unrelatedReset&&ReferenceEquals(Left.Items.Single(entry=>entry.Name=="stable"),unrelated)&&ReferenceEquals(Left.Selected.Single(),unrelated),
                "F5 keeps the rows, their values and the selection on screen until each is replaced");
            // Changes during a refresh do not start another one; F5 pressed again during it adds exactly one more.
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int held=0,passes=0;
            SpaceScan.IndexObserved=(_,message)=>{if(message.StartsWith("Run:",StringComparison.Ordinal))Interlocked.Increment(ref passes);};
            SpaceScan.BeforeEnumeration=(path,token)=>
            {
                visits.AddOrUpdate(path,1,(_,count)=>count+1);
                if(path==folder&&Interlocked.Increment(ref held)==1){entered.TrySetResult();release.Task.WaitAsync(token).GetAwaiter().GetResult();}
            };
            try
            {
                Left.Refresh(force:true);await entered.Task;
                await Watched(payload,()=>{using var stream=File.OpenWrite(payload);stream.SetLength(130);});
                Left.Refresh(force:true);Left.Refresh(force:true);
            }
            finally{release.TrySetResult();}
            await Left.SpaceCompletion;
            check(passes==2&&Left.FolderTotal==330&&!Left.SpaceStale&&ReferenceEquals(Left.Selected.Single(),unrelated)&&!knownFlashed&&!unrelatedReset,
                "A change during a refresh starts nothing, and F5 pressed twice meanwhile adds one pass ("+passes+" passes, total "+Left.FolderTotal+")");
            SpaceScan.IndexObserved=null;
            SpaceScan.BeforeEnumeration=(path,token)=>visits.AddOrUpdate(path,1,(_,count)=>count+1);
            // Shrink, rename and move out; F5 then shows exactly what is on disk.
            await Watched(payload,()=>{using var stream=File.OpenWrite(payload);stream.SetLength(11);});
            string moved=Path.Combine(folder,"renamed");
            await Task.Run(()=>Directory.Move(Path.GetDirectoryName(busy),moved));
            await Watched(moved,()=>Directory.Move(moved,Path.Combine(root,"removed branch")));
            check(Left.SpaceStale&&Left.FolderTotal==330&&Left.Items.Any(entry=>entry.Name=="busy"),"Changes on disk leave the snapshot as it was until F5");
            Left.Refresh(force:true);await Left.SpaceCompletion;
            check(Left.FolderTotal==200&&Left.Items.All(entry=>entry.Name is not ("busy" or "renamed"))&&ReferenceEquals(Left.Items.Single(entry=>entry.Name=="stable"),unrelated)&&ReferenceEquals(Left.Selected.Single(),unrelated),
                "F5 after a shrink, rename and removal reaches the exact total and keeps the unaffected row and the selection");
            // Ctrl+R measures everything on disk.
            Left.Refresh(force:true,measure:true);await Left.SpaceCompletion;
            check(Left.FolderTotal==200&&Left.SpaceSummary.Files==1&&Left.SpaceSummary.Folders==2&&unrelated.State==SpaceState.Complete&&visits.GetValueOrDefault(stable)>0,
                "Ctrl+R still measures everything on disk (stable: "+unrelated.SpaceStatus+")");
            await VerifySpaceTabsAndDelete(root,folder,unrelated,visits,check);
            // CORE-31 (owner: switching views made the analysis walk D:\ for minutes while F5 was instant): a view switch
            // takes the index service's totals too.
            visits.Clear();
            await Left.RefreshAccessMode();await Left.SpaceCompletion;
            var switched=Left.Items.OfType<SpaceEntry>().Single(entry=>entry.Name=="stable");
            check(Left.FolderTotal==200&&switched.State==SpaceState.Indexed&&switched.Size==200&&!visits.Keys.Any(path=>SizeCache.Within(path,Path.GetDirectoryName(stable))),
                "Switching between standard and administrator view takes the index's totals instead of measuring on disk (stable: "+switched.SpaceStatus+", "+visits.Count+" folders read, total "+Left.FolderTotal+")");
        }
        finally{Left.StatsChanged-=Observed;SpaceScan.BeforeEnumeration=null;SpaceScan.IndexObserved=null;}
        await Left.EndSpaceAsync();await Left.StopSizingAsync();
        await VerifyCommandStates21(root,check);
        check(HiddenTrialPresentation.ForegroundViolations==0,"Analysis refresh verification does not activate the user's desktop: "+JsonSerializer.Serialize(HiddenTrialPresentation.ForegroundEvents));
    }

    // Owner, 2026-11-17: clicking the analysis tab's own tab reset it, other tabs could not be visited without losing
    // it, and Delete (key or menu) on a tree row seemed to do nothing because the deleted row stayed in the tree.
    async Task VerifySpaceTabsAndDelete(string root,string folder,SpaceEntry stable,ConcurrentDictionary<string,int> visits,Action<bool,string> check)
    {
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        void ClickTab(int index)=>((Button)Left.TabStrip.Children[index]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        int analysisTab=Left.ActiveTab;
        HiddenTrialPresentation.Phase="space-tabs";
        ClickTab(analysisTab);await Idle();
        check(Left.SpaceAnalyzing&&ReferenceEquals(Left.Items.Single(entry=>entry.Name=="stable"),stable)&&ReferenceEquals(Left.Selected.Single(),stable),"Clicking the analysis tab's own tab changes nothing: the analysis, its rows and its selection stay");
        int measured=visits.Values.Sum();
        Left.OpenInBackgroundTab(root);await Idle();
        ClickTab(analysisTab+1);await Idle();
        bool otherShown=!Left.SpaceAnalyzing&&Left.Dir.Equals(root,StringComparison.OrdinalIgnoreCase)&&Left.Items.Any(entry=>entry.Name=="watched tree")&&!(Left.ListForVerification.View is GridView grid&&grid.Columns.Any(column=>column.Header as string=="Space used"));
        bool otherCommand=AnalyzeSpaceCommand.Tag==null;
        ClickTab(analysisTab);await Idle();
        check(otherShown&&otherCommand&&Left.SpaceAnalyzing&&AnalyzeSpaceCommand.Tag as string=="on"&&ReferenceEquals(Left.Items.Single(entry=>entry.Name=="stable"),stable)&&ReferenceEquals(Left.Selected.Single(),stable)&&Left.FolderTotal==200&&visits.Values.Sum()==measured,
            "Another tab shows its own folder listing, and switching back restores the same analysis rows, selection and totals without measuring again");
        Left.CloseTab(analysisTab+1);await Idle();
        check(Left.SpaceAnalyzing&&ReferenceEquals(Left.Selected.Single(),stable)&&Left.OpenTabs.Count==analysisTab+1,"Closing another tab keeps the analysis on screen");
        // A real recycle and its undelete are shell operations that may activate
        // a sheet of this process. Run them only on the owned never-switched
        // private desktop (space-ui), never on the user's desktop.
        if(!HiddenTrialPresentation.PrivateSpaceTrial)return;
        HiddenTrialPresentation.Phase="space-delete";

        string doomed=Path.Combine(folder,"doomed.bin");
        var appeared=Sizes(Left,()=>Left.Items.Any(entry=>entry.Path==doomed)&&Left.FolderTotal==250&&!Left.SizesPending);
        await Task.Run(()=>File.WriteAllBytes(doomed,new byte[50]));Left.Refresh(force:true);await appeared;await Left.SpaceCompletion;
        byte[] hash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(doomed));
        SetActive(Left);Left.ListForVerification.SelectedItem=Left.Items.Single(entry=>entry.Path==doomed);
        var recycled=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);bool removedAtOnce=false;
        RecycleFinishedForVerification=(paths,error)=>
        {
            if(!paths.SequenceEqual(new[]{doomed}))return;
            removedAtOnce=Left.Items.All(entry=>entry.Path!=doomed)&&Left.FolderTotal==200&&ReferenceEquals(Left.Selected.SingleOrDefault(),stable);
            recycled.TrySetResult(error);
        };
        try
        {
            ContextMenu menu;captureS4Menu=true;try{Left.S4ContextAction();menu=archiveMenuForVerification;}finally{captureS4Menu=false;}
            var command=MenuCommands(menu).Single(item=>item.Header as string=="Delete");
            check(command.IsEnabled,"An analysis row's context menu offers Delete");
            command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            string error=await recycled.Task;
            check(error==null&&!File.Exists(doomed)&&removedAtOnce,"Delete on an analysis row recycles it, removes the row and its space at once, and selects the row that took its place");
        }
        finally{RecycleFinishedForVerification=null;}
        await Left.SpaceCompletion;await Idle();
        check(Left.SpaceAnalyzing&&Left.FolderTotal==200&&Left.Items.All(entry=>entry.Path!=doomed),"After a delete the analysis keeps the total shown at once without measuring again");
        var restored=Sizes(Left,()=>Left.Items.Any(entry=>entry.Path==doomed)&&Left.FolderTotal==250&&!Left.SizesPending);
        await RestoreRecycledMenuFixture(doomed,hash);Left.Refresh(force:true);await restored;await Left.SpaceCompletion;
        var removed=Sizes(Left,()=>Left.Items.All(entry=>entry.Path!=doomed)&&Left.FolderTotal==200&&!Left.SizesPending);
        await Task.Run(()=>File.Delete(doomed));Left.Refresh(force:true);await removed;await Left.SpaceCompletion;
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
