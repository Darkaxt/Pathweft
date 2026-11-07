#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyTransferDismiss14(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        string source=Directory.CreateDirectory(Path.Combine(root,"transfer source é 子")).FullName;
        string target=Directory.CreateDirectory(Path.Combine(root,"transfer target")).FullName;
        string Write(string name){string path=Path.Combine(source,name);File.WriteAllText(path,"Real transfer fixture: "+name);return path;}
        await S1Verification.Navigate(Right,target);
        int previousUndo=undo.Count;
        var copy=Submit(new(){Write("copy.txt")},target,false);await copy.Completion;await Settle();
        check(copy.Successful&&File.ReadAllText(Path.Combine(target,"copy.txt"))==File.ReadAllText(Path.Combine(source,"copy.txt")),"Real copy completes with matching destination bytes");
        check(!jobs.Contains(copy)&&!transfersWindow.IsVisible&&TransfersButton.Visibility==Visibility.Collapsed,"Successful completion dismisses its card and hides the now-empty Transfers window automatically");
        check(undo.Count==previousUndo+1,"Automatic dismissal preserves the copy's Undo record");

        var move=Submit(new(){Write("move.txt")},target,true);await move.Completion;await Settle();
        check(move.Successful&&!File.Exists(Path.Combine(source,"move.txt"))&&File.Exists(Path.Combine(target,"move.txt"))&&!jobs.Contains(move),"Real successful move also auto-dismisses after verified source removal");

        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string first=Write("first.txt"),second=Write("second.txt");
        Ferry.VerificationBoundary=(phase,job,path,_)=>
        {
            if(phase!="before-copy")return;
            if(path==first){entered.TrySetResult();release.Task.GetAwaiter().GetResult();}
            if(path==second){secondEntered.TrySetResult();secondRelease.Task.GetAwaiter().GetResult();}
        };
        Job one=null,two=null;
        try
        {
            one=Submit(new(){first},target,false);await entered.Task;
            two=Submit(new(){second},target,false);await Settle();
            check(jobs.Contains(one)&&jobs.Contains(two)&&transfersWindow.IsVisible,"Running and queued real jobs remain displayed");
            release.TrySetResult();await one.Completion;await secondEntered.Task;await Settle();
            check(!jobs.Contains(one)&&jobs.Contains(two)&&transfersWindow.IsVisible,"One success cannot hide another outstanding transfer");
            transfersWindow.Close();check(!transfersWindow.IsVisible&&!two.Completion.IsCompleted,"Manual close still hides the surface without cancelling its operation");
            ShowTransfers(false);secondRelease.TrySetResult();await two.Completion;await Settle();
            check(two.Successful&&!jobs.Any()&&!transfersWindow.IsVisible,"The final real success automatically hides the reused window");
        }
        finally
        {
            release.TrySetResult();secondRelease.TrySetResult();Ferry.VerificationBoundary=null;
            if(one!=null)await one.Completion;if(two!=null)await two.Completion;
        }

        var failed=Submit(new(){Path.Combine(source,"missing.txt")},target,false);await failed.Completion;await Settle();
        check(!failed.Successful&&jobs.Contains(failed)&&transfersWindow.IsVisible,"A genuine missing-source transfer remains visible for review");
        failed.Notes.Enqueue("Some stored timestamps differ from the source.");failed.Refresh();await Settle();
        check(PreviewChildren<TextBlock>(transfersWindow).Any(text=>text.IsVisible&&text.Text==failed.Errors.First())&&failed.ErrorSummary==failed.Errors.First(),"The actual failed file is visible in the card while metadata notes stay in collapsed Details");
        async Task<Job> Conflict(string name,bool cancel)
        {
            string path=Write(name);File.WriteAllText(Path.Combine(target,name),"Existing different bytes");
            var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Ferry.VerificationBoundary=(phase,_,_,_)=>{if(phase=="conflicts")reached.TrySetResult();};
            Job job=null;
            try
            {
                job=Submit(new(){path},target,false);await reached.Task;await Settle();
                check(jobs.Contains(job)&&job.HasConflicts&&transfersWindow.IsVisible,"Unresolved real conflicts remain displayed");
                if(cancel)
                {
                    var button=TransferCancel(job);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else job.Resolve(Choice.Skip);
                await job.Completion;await Settle();return job;
            }
            finally{Ferry.VerificationBoundary=null;if(job?.Active==true){job.Cancel();await job.Completion;}}
        }
        var partial=await Conflict("skip.txt",false);var cancelled=await Conflict("cancel.txt",true);
        check(partial.Partial&&!partial.Successful&&jobs.Contains(partial)&&cancelled.Phase==Phase.Cancelled&&!jobs.Contains(cancelled),"Skipped/partial outcomes remain available; explicit clean cancellation dismisses after actual completion");
        var last=Submit(new(){Write("last.txt")},target,false);await last.Completion;await Settle();
        check(last.Successful&&!jobs.Contains(last)&&jobs.Contains(failed)&&jobs.Contains(partial)&&transfersWindow.IsVisible,"Another success preserves all outstanding unsuccessful cards and their window");
        await S1Verification.Snapshot(transfersWindow,root,"transfers-needs-review");
        foreach(var job in jobs.ToArray())
        {
            transfersWindow.UpdateLayout();
            var dismiss=PreviewChildren<Button>(transfersWindow).Single(button=>ReferenceEquals(button.DataContext,job)&&button.Name=="TransferDismiss");
            dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        check(jobs.Count==0&&!transfersWindow.IsVisible,"The retained outcomes still dismiss through the actual existing command");
        await VerifyTransferDesign15(root,check);
        check(HiddenTrialPresentation.ForegroundViolations==0,"Transfer completion and review workflows cause no host-desktop activation");
    }
}
#endif
