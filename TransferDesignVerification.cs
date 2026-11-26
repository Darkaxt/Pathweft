#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    Button TransferCancel(Job job)=>PreviewChildren<Button>(transfersWindow).Single(button=>ReferenceEquals(button.DataContext,job)&&button.Name=="TransferCancel");
    internal async Task VerifyTransferDesign15(string root,Action<bool,string> check)
    {
        string source=Directory.CreateDirectory(Path.Combine(root,"table incoming é 子")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"table existing")).FullName;
        var files=new List<string>();
        for(int i=0;i<120;i++){string name=$"File {i:000} — lengthy relative name é 子.txt";string path=Path.Combine(source,name);File.WriteAllText(path,"incoming"+i);File.WriteAllText(Path.Combine(target,name),"existing");files.Add(path);}
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Ferry.VerificationBoundary=(phase,_,_,_)=>{if(phase=="conflicts"){reached.TrySetResult();release.Task.GetAwaiter().GetResult();}};
        Job job=null;
        try
        {
            Settings.Animations=true;
            job=Submit(files,target,false);await reached.Task;
            foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
            {
                Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();
                job.Refresh();UpdateTransfers();transfersWindow.SizeToContent=SizeToContent.Height;
                transfersWindow.Width=640*scale;
                ((FrameworkElement)transfersWindow.Content).LayoutTransform=scale>1?new System.Windows.Media.ScaleTransform(scale,scale):null;
                await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
                var table=PreviewChildren<ListView>(transfersWindow).Single(view=>view.Name=="ConflictTable");
                var card=PreviewChildren<Border>(transfersWindow).Single(border=>border.Name=="TransferCard");
                check(table.Items.Count==120&&((GridView)table.View).Columns.Select(column=>column.Header.ToString()).SequenceEqual(new[]{"File","Incoming","Existing"}),"Actual conflicts use the complete comparison table: "+theme+scale);
                check(card.Opacity==1&&!card.HasAnimatedProperties&&table.ActualHeight<=240.1&&PreviewChildren<ListViewItem>(table).Count()<120,"First layout contains opaque content and a bounded virtualized table: "+theme+scale);
                check(transfersWindow.ActualHeight<620*scale&&PreviewChildren<Button>(transfersWindow).Where(button=>button.IsVisible).All(button=>button.ActualWidth>0&&button.ActualHeight>0),"Compact content-sized window contains reachable real actions: "+theme+scale);
                var scroll=PreviewChildren<ScrollViewer>(table).First();
                check(scroll.ScrollableHeight>0&&scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled,"Conflict table scrolls vertically without a redundant horizontal scrollbar: "+theme+scale);
                await S1Verification.Snapshot(transfersWindow,root,$"transfers-table-{theme}-{scale*100:0}");
            }
            TransferCancel(job).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!job.Completion.IsCompleted&&jobs.Contains(job),"Cancel does not dismiss a worker held before actual exit");
            release.TrySetResult();await job.Completion;
            await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(!jobs.Contains(job)&&!transfersWindow.IsVisible,"Explicit clean cancellation dismisses once the actual worker completes");
            var failure=new Job{Sources=new(),Destination=target,Phase=Phase.Failed};failure.Errors.Enqueue("File 003.txt: access denied");failure.Notes.Enqueue("Some stored timestamps differ from the source.");
            check(failure.ErrorSummary=="File 003.txt: access denied"&&failure.ErrorDetail.Contains("timestamp"),"Actual file-specific errors lead the summary; informational metadata remains in Details");
            check(!new Job{Sources=new(),Destination=target,Phase=Phase.Copying}.Stats.Contains("/s"),"Per-job statistics do not repeat aggregate throughput");
        }
        finally{release.TrySetResult();Ferry.VerificationBoundary=null;if(job?.Active==true){job.Cancel();await job.Completion;}Settings.Animations=false;Settings.TextScale=1;ApplyAppearance();}
    }
    internal async Task VerifyTransferClipboard15(string root,Action<bool,string> check)
    {
        check(Environment.GetEnvironmentVariable("PATHWEFT_PRIVATE_MENU_DESKTOP") is {} station&&station.StartsWith("PathweftMenu-")&&station!="WinSta0","Native clipboard verification requires the exact private station, never the host clipboard");
        string source=Directory.CreateDirectory(Path.Combine(root,"clipboard source é 子")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"clipboard target")).FullName;
        string file=Path.Combine(source,"copied.txt");File.WriteAllText(file,"source");File.WriteAllText(Path.Combine(target,"copied.txt"),"existing");
        await S1Verification.Navigate(Left,source);SetActive(Left);Left.SetSelection(entry=>entry.Path==file);
        await PutClipboard(false);
        var before=S4NativeClipboard.Read();uint sequence=GetClipboardSequenceNumber();
        for(int attempt=0;attempt<2;attempt++)
        {
            var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Ferry.VerificationBoundary=(phase,_,_,_)=>{if(phase=="conflicts"){reached.TrySetResult();release.Task.GetAwaiter().GetResult();}};
            Job job=null;
            try
            {
                ClipboardPaste(target);job=jobs[0];await reached.Task;
                job.Refresh();UpdateTransfers();await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
                TransferCancel(job).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(jobs.Contains(job)&&!job.Completion.IsCompleted,"Native paste cancellation waits for its actual held worker: "+attempt);
                release.TrySetResult();await job.Completion;await clipboardOperation;
                await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
                var after=S4NativeClipboard.Read();var read=ReadFileClipboard(out uint readSequence);
                check(sequence==GetClipboardSequenceNumber()&&readSequence==sequence&&read.Paths.SequenceEqual(new[]{file})&&!read.Move&&before.SequenceEqual(after),"Actual copy/cancel preserves all clipboard formats and another Paste uses the same selection: "+attempt);
                check(!jobs.Contains(job),"Cancelled paste needs no second dismissal: "+attempt);
            }
            finally{release.TrySetResult();Ferry.VerificationBoundary=null;if(job?.Active==true){job.Cancel();await job.Completion;}}
        }
        // CORE-34, once the clipboard is free: only on the private window station, never the owner's clipboard.
        await VerifyBusyClipboard34(source,file,check);
        string quoted=Entry.QuotedPaths(Left.Selected);
        CopyPathFromMenu();
        check(Status.Text=="Path copied"&&ClipboardText.TryGet()==quoted,"Copy path puts the quoted path on the clipboard once it is free");
    }
    void CopyPathFromMenu(){var item=MenuCommands(ItemMenu(Left,Left.Selected)).Single(item=>item.Header as string=="Copy path");item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));}
    /// CORE-34: another program holding the clipboard open (here a window-less thread of this process) refuses
    /// the write. The item menu's Copy path reports it instead of ending Pathweft. The clipboard is only held
    /// open, never written, so this may run where the clipboard is the owner's (shell-only private desktop).
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    internal async Task VerifyBusyClipboard34(string folder,string file,Action<bool,string> check)
    {
        await S1Verification.Navigate(Left,folder);SetActive(Left);Left.SetSelection(entry=>entry.Path==file);
        var held=new ManualResetEventSlim();var free=new ManualResetEventSlim();bool opened=false;
        var holder=new Thread(()=>{opened=OpenClipboard(IntPtr.Zero);held.Set();free.Wait();if(opened)CloseClipboard();}){IsBackground=true};
        string quoted=Entry.QuotedPaths(Left.Selected);uint busySequence=GetClipboardSequenceNumber();
        holder.Start();held.Wait();
        Exception thrown=null;
        try{try{CopyPathFromMenu();}catch(Exception error){thrown=error;}}
        finally{free.Set();holder.Join();}
        // "changes nothing": nothing was written, or what was written since is another program's (on the shared desktop the owner's
        // copies and clipboard sync move the sequence, and can put back the same path an earlier run copied), never Pathweft's
        bool moved=GetClipboardSequenceNumber()!=busySequence;
        GetWindowThreadProcessId(GetClipboardOwner(),out uint ownerProcess);
        bool ours=moved&&ownerProcess==Environment.ProcessId;
        check(opened&&thrown==null&&Status.Text=="Nothing copied: another program is using the clipboard. Try again."&&!ours,
            "Copy path while another program holds the clipboard reports it, changes nothing and Pathweft keeps running: "+(thrown?.GetType().Name??Status.Text)+(moved?" (clipboard since written by "+(ours?"Pathweft":"another program")+(ClipboardText.TryGet()==quoted?", holding the same path":"")+")":""));
    }
}
#endif
