#if DEBUG
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifySizes18(string root, Action<bool,string> check)
    {
        string parent=Directory.CreateDirectory(Path.Combine(root,"size-view")).FullName;
        string package=Directory.CreateDirectory(Path.Combine(parent,"DeckCraft")).FullName;
        string sibling=Directory.CreateDirectory(Path.Combine(parent,"unchanged")).FullName;
        File.WriteAllBytes(Path.Combine(sibling,"keep.bin"),new byte[37]);
        // Do not reactivate the pane after listing: that used to hide the stale
        // toolbar state seen on a normal asynchronous folder navigation.
        SetActive(Left);
        await S1Verification.Navigate(Left,parent);
        check(Left.CanAnalyzeSpace&&AnalyzeSpaceCommand.IsEnabled,"Analyze space becomes enabled when the active asynchronous folder listing is ready");
        await Sizes(Left,()=>Left.Items.All(e=>e.Size>=0));
        var row=Left.Items.Single(e=>e.Name=="DeckCraft");
        Left.ListForVerification.SelectedItem=row;
        check(row.Size==0,"Empty folder initially has a measured zero");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var watcher=(FileSystemWatcher)typeof(PaneView).GetField("watcher",flags).GetValue(Left);
        check(watcher.IncludeSubdirectories,"The actual visible-folder observer covers nested descendants");
        string release=Path.Combine(package,"releases","1.0","portable");
        string file=Path.Combine(release,"renderer.bin");
        var created=Sizes(Left,()=>row.Size==213_000_000);
        await Task.Run(()=>{Directory.CreateDirectory(release);using var stream=File.Create(file);stream.SetLength(213_000_000);});
        await created;
        check(ReferenceEquals(Left.Selected.Single(),row)&&Left.FolderTotal==213_000_037,"Nested external installation updates the same selected ancestor row and folder total without refresh");
        await S1Verification.Navigate(Right,package);
        await Sizes(Right,()=>Right.Items.Single().Size==213_000_000);
        bool blinked=false,totalDropped=false;
        long retainedTotal=Left.FolderTotal;
        void ObserveRefresh()
        {
            var current=Left.Items.SingleOrDefault(e=>e.Name=="DeckCraft");
            if(current!=null)blinked|=current.SizeText=="…";
            totalDropped|=Left.FolderTotal<retainedTotal;
        }
        System.ComponentModel.PropertyChangedEventHandler observeRow=(_,_)=>ObserveRefresh();
        row.PropertyChanged+=observeRow;Left.StatsChanged+=ObserveRefresh;
        var grown=Sizes(Left,()=>row.Size==214_000_000);
        var rightGrown=Sizes(Right,()=>Right.Items.Single().Size==214_000_000);
        try
        {
            await Task.Run(()=>{using var stream=File.OpenWrite(file);stream.SetLength(214_000_000);});
            await Task.WhenAll(grown,rightGrown);
            check(!blinked&&!totalDropped,"A real nested rewrite keeps the last displayed folder size and summary total until the valid replacement arrives");
        }
        finally{row.PropertyChanged-=observeRow;Left.StatsChanged-=ObserveRefresh;}
        check(Left.FolderTotal==214_000_037&&Right.FolderTotal==214_000_000,"A rewrite with unchanged ancestor timestamps updates both open views and totals");
        Left.EditPath();
        var address=(TextBox)Left.FindName("FilterBox");
        string draft=Path.Combine(parent,"not yet submitted é 子");
        address.Text=draft;address.Select(9,4);
        var focused=System.Windows.Input.FocusManager.GetFocusedElement(this);
        bool noisy=false,editLost=false;
        void ObserveEditing()
        {
            noisy|=Left.Footer.Text.Contains("measuring")||StatFolder.Text.EndsWith('+')||StatFolderSub.Text.Contains("measuring");
            editLost|=address.Text!=draft||address.Visibility!=System.Windows.Visibility.Visible||address.SelectionStart!=9||address.SelectionLength!=4||System.Windows.Input.FocusManager.GetFocusedElement(this)!=focused;
        }
        Left.ListForVerification.SelectedItems.Clear();
        Left.StatsChanged+=ObserveEditing;
        try
        {
            for(int rewrite=0;rewrite<3;rewrite++)
            {
                var pending=Sizes(Left,()=>row.Size==-1);
                await Task.Run(()=>{using var stream=File.OpenWrite(file);stream.WriteByte((byte)rewrite);});
                await pending;await Sizes(Left,()=>row.Size==214_000_000);
            }
            var replaced=Sizes(Left,()=>Left.Items.Any(e=>e.Name=="row replacement.txt"));
            await Task.Run(()=>File.WriteAllText(Path.Combine(parent,"row replacement.txt"),""));await replaced;
            check(!noisy,"Repeated same-length nested writes silently refresh known totals without measuring labels or plus suffixes");
            check(!editLost,"Actual watcher refresh preserves draft address text, selection, visibility and logical focus");
        }
        finally{Left.StatsChanged-=ObserveEditing;}
        address.Text=sibling;
        var submitted=Left.SubmitAddressAsync();
        Left.Refresh();
        await submitted;
        check(Left.Dir==sibling,"Enter submission survives a concurrent same-folder metadata refresh");
        await S1Verification.Navigate(Left,parent);
        await Sizes(Left,()=>Left.Items.All(e=>e.Size>=0));
        row=Left.Items.Single(e=>e.Name=="DeckCraft");
        Left.EditPath();address.Text=draft;
        typeof(PaneView).GetMethod("PathBox_PreviewKeyDown",flags).Invoke(Left,new object[]{address,new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(address),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent}});
        check(address.Visibility==System.Windows.Visibility.Collapsed&&Left.Dir==parent,"Escape cancels the draft without navigating");
        string renamed=Path.Combine(release,"renamed.bin");
        await Task.Run(()=>File.Move(file,renamed));
        var shrunk=Sizes(Left,()=>row.Size==19);
        await Task.Run(()=>{using var stream=File.OpenWrite(renamed);stream.SetLength(19);});
        await shrunk;
        check(Left.Items.Single(e=>e.Name=="unchanged").Size==37,"Unchanged sibling sizes survive targeted invalidation");
        var removed=Sizes(Left,()=>row.Size==0);
        await Task.Run(()=>File.Move(renamed,Path.Combine(root,"removed.bin")));
        await removed;
        await S1Verification.Navigate(Left,package);
        await S1Verification.Navigate(Left,parent);
        await Sizes(Left,()=>Left.Items.Single(e=>e.Name=="DeckCraft").Size==0);
        check(Left.FolderTotal==37,"Returning to an ancestor uses corrected totals after nested removal");
        string unchangedStamp=Path.Combine(package,"releases");
        using(var obsolete=new SizeCache.Measurement(package))
        {
            SizeCache.Invalidate(unchangedStamp);
            check(!obsolete.Complete(Directory.GetLastWriteTimeUtc(package),999_999),"A scan invalidated while enumerating cannot publish its obsolete total");
        }
        await Sizes(Left,()=>Left.Items.Single(e=>e.Name=="DeckCraft").Size==0);
        // Overflow reports an unknown change: exercise the real Error event path, without
        // flooding the OS buffer or touching another application's watcher.
        watcher=(FileSystemWatcher)typeof(PaneView).GetField("watcher",flags).GetValue(Left);
        var overflow=typeof(FileSystemWatcher).GetMethod("OnError",flags);
        check(overflow!=null,"The native observer exposes its overflow recovery event");
        SizeCache.Set(package,Directory.GetLastWriteTimeUtc(package),17);
        overflow.Invoke(watcher,new object[]{new ErrorEventArgs(new InternalBufferOverflowException("Owned diagnostic notification loss"))});
        // The displayed zero intentionally survives refresh. It cannot signal
        // completion of the replacement measurement; observe the actual
        // invalidated database record being republished as well.
        await Sizes(Left,()=>Left.Items.Single(e=>e.Name=="DeckCraft").Size==0&&SizeCache.Get(package,Directory.GetLastWriteTimeUtc(package))==0);
        check(SizeCache.Get(package,Directory.GetLastWriteTimeUtc(package))==0,"Notification overflow discards unknown stale descendants and rebuilds actual totals");
        Left.ListForVerification.SelectedItem=Left.Items.Single(e=>e.Name=="DeckCraft");
        bool selectionLost=false;
        void ObserveSelection(){selectionLost|=Left.Selected.Count==0;}
        Left.StatsChanged+=ObserveSelection;
        try
        {
            var added=Sizes(Left,()=>Left.Items.Any(e=>e.Name=="unrelated.txt"));
            await Task.Run(()=>File.WriteAllText(Path.Combine(parent,"unrelated.txt"),"other"));await added;
            check(!selectionLost&&Left.Selected.Single().Name=="DeckCraft","Replacing rows after an external create publishes the restored selection atomically to preview/stat consumers");
        }
        finally{Left.StatsChanged-=ObserveSelection;}
        check(HiddenTrialPresentation.ForegroundViolations==0,"Live folder-size qualification caused no foreground activation");
    }

    static async Task Sizes(PaneView pane, Func<bool> predicate)
    {
        if(predicate())return;
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(){if(predicate())completed.TrySetResult();}
        pane.StatsChanged+=Changed;
        try{Changed();await completed.Task;await pane.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
        finally{pane.StatsChanged-=Changed;}
    }
}
#endif
