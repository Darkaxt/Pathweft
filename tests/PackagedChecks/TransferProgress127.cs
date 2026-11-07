using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    async Task VerifyTransferProgress127()
    {
        var job=new Job{Sources=new(){"fixture"},Destination=@"D:\Destination",TotalFiles=1635,DoneFiles=480,
            TotalBytes=400_000_000,DoneBytes=12_300_000,CopiedFiles=480,VerifiedFiles=480};
        var phase=typeof(Job).GetProperty("Phase",Flags);phase.SetValue(job,Phase.Copying);
        // Instantiate the delivered compiled template without showing a window
        // or touching the user's desktop, clipboard or current transfers.
        var card=(Border)((DataTemplate)owner.FindResource("JobCard")).LoadContent();card.DataContext=job;
        foreach(int width in new[]{580,400})
        {
            card.Measure(new Size(width,double.PositiveInfinity));card.Arrange(new Rect(0,0,width,card.DesiredSize.Height));
            await owner.Dispatcher.InvokeAsync(card.UpdateLayout,DispatcherPriority.ApplicationIdle);
            var bar=Descendants<ProgressBar>(card).Single();
            var left=Descendants<TextBlock>(card).Single(t=>t.Name=="TransferCompleted");
            var right=Descendants<TextBlock>(card).Single(t=>t.Name=="TransferTotal");
            Check(bar.Value==job.Fraction&&left.Text.Contains("480")&&right.Text.Contains(job.TotalFiles.ToString("N0"))&&left.TranslatePoint(new Point(),card).Y<bar.TranslatePoint(new Point(),card).Y,
                "Delivered single progress bar shows completed/total counts above it at width "+width);
            foreach(var button in Descendants<Button>(card).Where(b=>b.Visibility==Visibility.Visible&&b.Name.StartsWith("Transfer")))
            {
                var parts=((StackPanel)button.Content).Children.Cast<TextBlock>().ToArray();
                double Center(TextBlock text)=>text.TranslatePoint(new Point(0,text.ActualHeight/2),button).Y;
                Check(parts.Length==2&&Math.Abs(Center(parts[0])-Center(parts[1]))<.1&&AutomationProperties.GetName(button).Length>0,"Delivered action icons/labels have aligned centers and accessible names");
            }
        }
        var pause=Descendants<Button>(card).Single(b=>b.Name=="TransferPause");
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(job.Paused&&job.PauseText=="Resume","Delivered Pause command retains actual state behavior");
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!job.Paused&&job.Stats==""&&job.VerifiedFiles==480,"Delivered running presentation omits debug statistics and preserves verification accounting");
        phase.SetValue(job,Phase.Done);
        Check(!job.Title.Contains("verified")&&!job.Stats.Contains("verified")&&job.Stats.Contains("12.3 MB processed"),"Delivered completion omits verification counts and retains processed bytes");
    }
}
