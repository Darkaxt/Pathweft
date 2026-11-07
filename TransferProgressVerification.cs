#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyTransferProgress24(string root,Action<bool,string> check)
    {
        // Deterministic displayed states; real worker completion/cancellation and
        // conflict decisions are exercised by VerifyTransferDismiss14 afterwards.
        var job=new Job{Sources=new(){"source"},Destination=@"D:\Tools\Modern VLC",Phase=Phase.Copying,
            TotalFiles=1635,DoneFiles=480,TotalBytes=400_000_000,DoneBytes=12_300_000,
            CopiedFiles=480,VerifiedFiles=480,Concurrent=true,Current="4 active files"};
        var other=new Job{Sources=new(){"other"},Destination=@"D:\Other",Phase=Phase.Copying,
            TotalFiles=2,TotalBytes=8_000_000,DoneBytes=2_000_000,Current="other.bin"};
        string mode=Settings.NativeMode;double originalScale=Settings.TextScale;
        async Task Layout()=>await Dispatcher.InvokeAsync(transfersWindow.UpdateLayout,DispatcherPriority.ApplicationIdle);
        try
        {
            jobs.Add(job);ShowTransfers(false);
            foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})foreach(int width in new[]{640,460})
            {
                Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();
                transfersWindow.SizeToContent=SizeToContent.Height;transfersWindow.Width=width*scale;
                ((FrameworkElement)transfersWindow.Content).LayoutTransform=scale>1?new ScaleTransform(scale,scale):null;
                job.Refresh();UpdateTransfers();await Layout();
                var card=PreviewChildren<Border>(transfersWindow).Single(b=>b.Name=="TransferCard");
                var completed=PreviewChildren<TextBlock>(card).Single(t=>t.Name=="TransferCompleted");
                var total=PreviewChildren<TextBlock>(card).Single(t=>t.Name=="TransferTotal");
                var bar=PreviewChildren<ProgressBar>(card).Single();
                Point At(FrameworkElement e)=>e.TranslatePoint(new Point(),card);
                check(completed.Text==job.CompletedFilesText&&total.Text==job.TotalFilesText&&At(completed).Y<At(bar).Y&&At(total).X>At(completed).X+completed.ActualWidth,
                    $"Actual completed/total counters occupy opposite ends above the single bar: {theme}/{scale}/{width}");
                check(bar.Value==job.Fraction&&totalProcessed.Text==Fmt(job.DoneBytes)+" processed"&&totalLegend.Visibility==Visibility.Collapsed,
                    "Header shows processed bytes once beside speed, with no redundant one-job legend");
                check(!PreviewChildren<TextBlock>(card).Any(t=>t.IsVisible&&t.Text.Contains("verified",StringComparison.OrdinalIgnoreCase)),
                    "Running card exposes no verification counter");
                foreach(var button in PreviewChildren<Button>(card).Where(b=>b.IsVisible))
                {
                    var parts=((StackPanel)button.Content).Children.Cast<TextBlock>().ToArray();
                    double Center(TextBlock t)=>t.TranslatePoint(new Point(0,t.ActualHeight/2),button).Y;
                    check(parts.Length==2&&Math.Abs(Center(parts[0])-Center(parts[1]))<.1&&AutomationProperties.GetName(button).Length>0&&At(button).X+button.ActualWidth<=card.ActualWidth,
                        "Actual button glyph and label share a center, fit the card and have accessible names");
                }
                check(transfersWindow.ActualHeight<420*scale,"One running transfer remains compact with its speed chart");
                await S1Verification.Snapshot(transfersWindow,root,$"transfer-progress-{theme}-{scale*100:0}-{width}");
            }
            var pause=PreviewChildren<Button>(transfersWindow).Single(b=>b.Name=="TransferPause");
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Layout();
            check(job.Paused&&AutomationProperties.GetName(pause)=="Resume","Actual Pause changes state and accessible Resume label");
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(!job.Paused,"Actual Resume preserves the transfer");
            jobs.Add(other);UpdateTransfers();await Layout();
            check(totalProcessed.Text==Fmt(job.DoneBytes+other.DoneBytes)+" processed"&&totalLegend.Visibility==Visibility.Visible,"Multiple active transfers aggregate bytes while retaining their legend");
            jobs.Remove(other);job.Phase=Phase.Failed;job.Errors.Enqueue("blocked.txt: Access denied");job.Refresh();UpdateTransfers();await Layout();
            check(PreviewChildren<TextBlock>(transfersWindow).Any(t=>t.IsVisible&&t.Text==job.ErrorSummary)&&job.Stats==Fmt(job.DoneBytes)+" processed","Genuine failure retains its error and processed amount without verification counters");
            var dismiss=PreviewChildren<Button>(transfersWindow).Single(b=>b.Name=="TransferDismiss");
            check(dismiss.IsVisible&&((StackPanel)dismiss.Content).Children.Count==2,"Finished action retains the aligned icon/label layout");
            job.Phase=Phase.Done;job.Errors.TryDequeue(out _);job.SkippedFiles=1;job.ExcludedItems=1;job.Refresh();
            check(job.Stats.Contains("1 skipped")&&job.Stats.Contains("1 unsupported")&&!job.Title.Contains("verified")&&job.VerifiedFiles==480,"Outcome details remain truthful and internal verification accounting is unchanged");
        }
        finally
        {
            jobs.Remove(job);jobs.Remove(other);Settings.NativeMode=mode;Settings.TextScale=originalScale;ApplyAppearance();
            ((FrameworkElement)transfersWindow.Content).LayoutTransform=null;
        }
        await VerifyTransferDismiss14(root,check);
    }
}
#endif
