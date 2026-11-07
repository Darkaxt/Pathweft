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
            Check(bar.Value==job.Fraction&&left.Text=="480 completed"&&right.Text==job.TotalFiles.ToString("N0")+" total"&&left.TranslatePoint(new Point(),card).Y<bar.TranslatePoint(new Point(),card).Y,
                "Delivered single progress bar shows completed/total counts above it at width "+width);
            foreach(var button in Descendants<Button>(card).Where(b=>b.Visibility==Visibility.Visible&&b.Name.StartsWith("Transfer")))
            {
                var label=(FrameworkElement)button.Content;
                var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(label);
                int w=(int)Math.Ceiling(label.ActualWidth*dpi.DpiScaleX),h=(int)Math.Ceiling(label.ActualHeight*dpi.DpiScaleY),split=(int)Math.Floor((double)Get(label,"TextOffset")*dpi.DpiScaleX);
                var raster=new System.Windows.Media.Imaging.RenderTargetBitmap(w,h,dpi.PixelsPerInchX,dpi.PixelsPerInchY,System.Windows.Media.PixelFormats.Pbgra32);raster.Render(label);
                var pixels=new byte[w*h*4];raster.CopyPixels(pixels,w*4,0);
                double Center(bool icon){var rows=new List<int>();for(int y=0;y<h;y++)for(int x=icon?0:split;x<(icon?split:w);x++)if(pixels[(y*w+x)*4+3]>32){rows.Add(y);break;}return(rows.Min()+rows.Max())/2.0;}
                Check(Math.Abs(Center(true)-Center(false))<=1&&AutomationProperties.GetName(button).Length>0,"Delivered rasterized action icons and labels have aligned visible ink centers and accessible names");
            }
        }
        var pause=Descendants<Button>(card).Single(b=>b.Name=="TransferPause");
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(job.Paused&&job.PauseText=="Resume","Delivered Pause command retains actual state behavior");
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!job.Paused&&job.Stats==""&&job.VerifiedFiles==480,"Delivered running presentation omits debug statistics and preserves verification accounting");
        phase.SetValue(job,Phase.Done);
        Check(!job.Title.Contains("verified")&&!job.Stats.Contains("verified")&&job.Stats=="12.3 MB","Delivered completion omits verification counts and redundant byte labels");
    }
}
