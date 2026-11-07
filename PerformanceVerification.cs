#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPerformance18(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false; // Native qualification owns its separate explicit permission boundary.
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Next()
        {
            var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Updated()=>completed.TrySetResult();
            PerformanceMonitor.Current.Changed+=Updated;
            try{await completed.Task;}finally{PerformanceMonitor.Current.Changed-=Updated;}
            await Settle();
        }
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var monitor=PerformanceMonitor.Current;
        Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);
        await S1Verification.Navigate(Left,root);await Settle();
        await VerifyPerformancePresentation18(root,check);
        Click(PerformanceCommand);await Settle();
        check(PerformanceMode&&performance.Visibility==Visibility.Visible&&StatsRow.Visibility==Visibility.Collapsed,"Performance command swaps the existing summary region and shows active state");
        check(PerformanceCommand.Tag as string=="on"&&AutomationProperties.GetName(PerformanceCommand)=="Show folder statistics","Performance command exposes its active state and return action");
        await Next();await Next();
        var native=monitor.Latest;
        check(native?.Cpu is >=0 and <=100,"Actual Windows system CPU deltas supply a valid live sample");
        check(native.Ram is {Total:>0,Used:>=0}&&native.Ram.Used<=native.Ram.Total,"Actual Windows physical RAM is independent of GPU dedicated memory");
        check(native.Disks.Any()&&WindowsPerformanceCounters.ForPath(native,root)!=null,"Physical-drive counters map the current task-owned D: folder to its real device");
        check(native.Gpus.All(g=>g.Name.Length>0&&g.Usage is null or >=0 and <=100),"Actual GPU samples retain adapter identities and bounded busiest-engine usage");
        check(WindowsPerformanceCounters.ForPath(native,@"\\unavailable\share\file")==null&&WindowsPerformanceCounters.ForPath(native,"not a filesystem path")==null,"Unavailable/network paths never inherit a physical host disk measurement");
        check(CloudVolumes.GoogleRoots().All(path=>WindowsPerformanceCounters.PhysicalDisk(path)==null),"Registered virtual cloud drives do not borrow their cache disk's physical throughput");
        File.WriteAllText(Path.Combine(root,"native-performance.json"),JsonSerializer.Serialize(native,new JsonSerializerOptions{WriteIndented=true}));
        performance.Select(0);await Next();await Next();
        check(monitor.DetailKind==1&&monitor.Details.Any(x=>x.Pid==Environment.ProcessId),"CPU card opens actual current process detail sampling");
        performance.Select(PerformancePanel.Ram);await Next();await Next();check(monitor.DetailKind==3&&monitor.Details.Any(x=>x.Memory>0),"RAM card directly exposes actual process working sets");
        performance.Select(PerformancePanel.Gpu);await Next();check(monitor.DetailKind==2,"GPU card directly switches to GPU engine process details");
        performance.Select(PerformancePanel.Folder);await Settle();check(monitor.DetailKind==0,"Folder card does not enumerate process working sets or bypass Windows permission");
        check(IoCollection.Contains(@"D:\test",@"D:\test\one.txt")&&!IoCollection.Contains(@"D:\test",@"D:\test-extra\one.txt")&&IoCollection.Contains(@"D:\",@"D:\test\one.txt"),"Folder attribution respects descendant boundaries and drive roots");
        performance.Select(-1);
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})foreach(double width in new[]{1400d,820d})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=width;Height=850;await Settle();
            check(performance.ActualHeight>50&&PaneArea.ActualHeight>250,"Performance cards reserve usable file-pane space: "+theme+scale+width);
            check(CommandButtons.Children.OfType<Button>().Where(x=>x.Visibility==Visibility.Visible).All(x=>x.TransformToAncestor(CommandBar).TransformBounds(new Rect(x.RenderSize)).Right<=CommandBar.ActualWidth+1),"Every compact toolbar command remains reachable at "+theme+scale+width);
            await S1Verification.Snapshot(this,root,$"performance-{theme}-{scale*100:0}-{width:0}");
            double cardHeight=performance.ActualHeight;Click(PerformanceCommand);await Settle();
            check(Math.Abs(cardHeight-StatsRow.ActualHeight)<=4,"Performance/statistics card heights match: "+theme+scale+width+" ("+cardHeight+"/"+StatsRow.ActualHeight+")");
            Click(PerformanceCommand);await Settle();
        }
        Width=1400;Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();performance.Select(0);await Next();
        await S1Verification.Snapshot(this,root,"performance-cpu-details");
        performance.Select(-1);await Settle();double performanceHeight=performance.ActualHeight;Click(PerformanceCommand);await Settle();
        File.WriteAllText(Path.Combine(root,"card-heights.json"),JsonSerializer.Serialize(new{performanceHeight,statisticsHeight=StatsRow.ActualHeight}));
        check(Math.Abs(performanceHeight-StatsRow.ActualHeight)<=4,"Five single-row performance cards match the ordinary statistics vertical footprint");
        check(!PerformanceMode&&StatsRow.Visibility==Visibility.Visible&&monitor.DetailKind==0,"Returning to statistics stops detailed enumeration and restores the original useful cards");
        var costs=new List<object>();
        async Task Measure(string state)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var process=Process.GetCurrentProcess();process.Refresh();
            long memory=process.PrivateMemorySize64;var cpu=process.TotalProcessorTime;var watch=Stopwatch.StartNew();
            for(int i=0;i<30;i++)await Next();
            process.Refresh();
            costs.Add(new{state,seconds=watch.Elapsed.TotalSeconds,cpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                privateBytes=process.PrivateMemorySize64,privateChange=process.PrivateMemorySize64-memory});
            File.WriteAllText(Path.Combine(root,"ui-resource-observations.json"),JsonSerializer.Serialize(costs,new JsonSerializerOptions{WriteIndented=true}));
        }
        await Measure("statistics-presented");
        Click(PerformanceCommand);await Settle();await Measure("performance-cards-presented");
        performance.Select(0);await Settle();await Measure("cpu-detail-presented");performance.Select(-1);
        long before=monitor.History().Length;Hide();await Measure("tray-hidden");
        check(monitor.History().Length>before,"Shared aggregate history continues while the browser is hidden in the tray");
        Show();await Settle();
        check(performance.Visibility==Visibility.Visible&&monitor.History().Length>before,"Reopening performance retains observed history rather than starting over");
        SetSinglePane(false);SetActive(Right);await Settle();
        check(!PerformanceMode,"Performance/statistics mode belongs to its pane");
        SetActive(Left);await Settle();check(PerformanceMode,"Switching back restores the original pane's performance mode");
        performance.Select(-1);SetSinglePane(true);
        var hashes=new HashSet<string>();
        var changelog=new WhatsNewWindow(this);
        try
        {
            changelog.Show();
            foreach(int month in Enumerable.Range(1,12))
            {
                using var first=WhatsNewWindow.Picture(new("test",new DateTime(2026,month,1),"test",new()));
                using var next=WhatsNewWindow.Picture(new("test",new DateTime(2027,month,1),"test",new()));
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first));hashes.Add(hash);
                check(hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(next)),"Changelog release month remains available across year rollover: "+month);
                changelog.ShowMonthForVerification(month,2027);
                await S1Verification.Snapshot(changelog,root,$"changelog-month-{month:00}");
            }
            check(hashes.Count==12,"Changelog actually decodes and presents twelve distinct ImageGen monthly assets");
        }
        finally{changelog.Close();}
        // Complete retention/gap contract using the same ring with a future sequence;
        // this isolated app's synthetic observations never enter installed history.
        var time=DateTimeOffset.UtcNow.AddHours(2);
        for(int i=0;i<3700;i++)monitor.Record(new(time.AddSeconds(i),0,Array.Empty<GpuReading>(),Array.Empty<DiskReading>(),null));
        var retained=monitor.History();
        check(retained.Length==3600&&retained[^1].Time-retained[0].Time<TimeSpan.FromHours(1),"Shared history retains at most the most recent hour and 3600 samples");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Source performance diagnostics never take over the user's desktop");
    }
}
#endif
