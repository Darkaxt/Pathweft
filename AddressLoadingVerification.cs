#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPerformancePresentation18(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false;
        Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);
        await S1Verification.Navigate(Left,root);
        await VerifyAddressLoading18(root,check);
        check(Left.ReadOnlyWatermark.Visibility==Visibility.Collapsed&&Left.FindName("FolderStrip")==null&&Left.FindName("UsedBar")==null&&Left.FindName("DriveInfoText")==null,
            "Ordinary folders reclaim the duplicate address capacity strip");
        string archivePath=Path.Combine(root,"performance-context.zip");
        using(var archive=System.IO.Compression.ZipFile.Open(archivePath,System.IO.Compression.ZipArchiveMode.Create))
        using(var text=new StreamWriter(archive.CreateEntry("context.txt").Open()))text.Write("Archive context");
        await S1Verification.Navigate(Left,archivePath);
        check(Left.ReadOnlyWatermark.Visibility==Visibility.Collapsed,"Editable archives carry no unnecessary warning watermark");
        var attributes=File.GetAttributes(archivePath);File.SetAttributes(archivePath,attributes|FileAttributes.ReadOnly);
        try
        {
            foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
            {
                Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1400;Height=850;
                await S1Verification.Navigate(Left,root);await S1Verification.Navigate(Left,archivePath);
                check(Left.ReadOnlyWatermark.Visibility==Visibility.Visible&&!Left.ReadOnlyWatermark.IsHitTestVisible&&Left.ReadOnlyWatermark.ClipToBounds,
                    "Read-only ribbon reaches the pane edges without intercepting input: "+theme+scale);
                Left.UpdateLayout();
                var corner=Left.ReadOnlyWatermark.TranslatePoint(new Point(Left.ReadOnlyWatermark.ActualWidth,Left.ReadOnlyWatermark.ActualHeight),Left.Card);
                check(Math.Abs(corner.X-(Left.Card.ActualWidth-1))<1&&Math.Abs(corner.Y-(Left.Card.ActualHeight-1))<1,
                    "Ribbon ends at the outer pane border instead of above the status footer: "+theme+scale);
                await S1Verification.Snapshot(this,root,$"readonly-ribbon-{theme}-{scale*100:0}");
            }
        }
        finally{File.SetAttributes(archivePath,attributes);Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();}
        await S1Verification.Navigate(Left,root);
        check(Left.ReadOnlyWatermark.Visibility==Visibility.Collapsed,"Leaving a read-only archive clears its ribbon");
    }
    internal async Task VerifyAddressLoading18(string root,Action<bool,string> check)
    {
        string folder=Path.Combine(root,"loading-folder");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"arrived.txt"),"A real completed listing");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim(false);
        void Listed(){if(Left.Dir==folder&&Left.AddressActivity.Visibility==Visibility.Collapsed)completed.TrySetResult();}
        Left.BeforeListingForVerification=path=>{if(path==folder){entered.TrySetResult();release.Wait();}};
        Left.StatsChanged+=Listed;
        try
        {
            bool motion=Settings.Animations;Settings.Animations=true;Left.RefreshAppearance();
            Left.Navigate(folder);await entered.Task;
            Left.EditPath();Left.FilterBox.Text=@"D:\Unsubmitted draft";Left.FilterBox.Select(3,5);
            check(Left.AddressActivity.Visibility==Visibility.Visible&&AutomationProperties.GetItemStatus(Left.AddressField)=="Loading",
                "Actual pending folder read owns an honest address-background loading state");
            await S1Verification.Snapshot(this,root,"address-loading");
            check(!Motion.On||Left.AddressActivity.OpacityMask is System.Windows.Media.LinearGradientBrush mask&&mask.GradientStops.All(x=>x.HasAnimatedProperties),
                "Unknown-duration work uses an animated band rather than a fabricated percentage");
            Settings.Animations=motion;
            Left.BeforeListingForVerification=null;release.Set();await completed.Task;
            check(Left.Items.Any(x=>x.Name=="arrived.txt")&&Left.FilterBox.Text==@"D:\Unsubmitted draft"&&Left.FilterBox.SelectionStart==3&&Left.FilterBox.SelectionLength==5&&Left.FilterBox.Visibility==Visibility.Visible,
                "Listing completion clears loading without replacing the address draft or caret selection");
            check(AutomationProperties.GetItemStatus(Left.AddressField)=="Ready","Completed listing exposes Ready instead of a stale progress state");
        }
        finally{Left.BeforeListingForVerification=null;release.Set();Left.StatsChanged-=Listed;}
        await S1Verification.Navigate(Left,root);
        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        Left.Refresh();
        check(Left.AddressActivity.Visibility==Visibility.Collapsed,"Routine same-folder refresh does not flash the foreground loading background");
        // The real indexed query's task owns its spinner through failure or completion.
        Left.Search("pathweft-loading-nonexistent-"+Guid.NewGuid().ToString("N"));
        await Left.SearchCompletion;
        check(Left.AddressActivity.Visibility==Visibility.Collapsed,"Actual search completion/error retires its address loading indication");
        await S1Verification.Navigate(Left,root);
    }
}
#endif
