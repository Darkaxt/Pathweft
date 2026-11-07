#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    async Task VerifyTimeline29(string root,Action<bool,string> check)
    {
        string path=Path.Combine(root,"investigation.sqlite");var start=DateTimeOffset.UtcNow.AddMinutes(-50);
        var first=new ProcessMetric(42,start.UtcTicks,"observed-first",10,1024);
        var second=new ProcessMetric(42,start.AddSeconds(1).UtcTicks,"observed-second",75,4096);
        Settings.PerformanceRecording=true;Settings.PerformanceRetention=1;
        using(var store=new PerformanceHistory(path))
        {
            for(int i=0;i<240;i++)
            {
                if(i is >=100 and <110)continue;
                var at=start.AddSeconds(i*10);
                var app=i<120?first:second;
                var sample=new PerformanceSample(at,i%25<3?95:10+i%20,[new("gpu-a","Adapter A",35,100,1000),new("gpu-b","Adapter B",60,200,2000)],[new("0 D:",100,300,10)],null,new(50,100));
                store.Record(sample,[app],[app with{Adapter="gpu-a",Usage=35},app with{Adapter="gpu-b",Usage=60}]);
            }
            check(store.Issue==null,"Investigation fixture uses actual history persistence and identities");
        }
        Settings.PerformanceRecording=false;
        var query=new PerformanceQuery(LastMinutes:60,From:start.AddSeconds(-5),To:start.AddMinutes(41));
        var timeline=PerformanceInvestigation.Read(query,source:path);
        check(timeline.Events.Any(x=>x.Name=="Usage spike")&&timeline.Events.Any(x=>x.Name=="Collection gap")&&timeline.Activity.Points.Any(x=>x.Value==null),"Timeline combines factual lifecycle/gap events and sampled spikes without filling gaps");
        var a=PerformanceInvestigation.Moment(query,start.AddSeconds(20),source:path);
        var b=PerformanceInvestigation.Moment(query,start.AddSeconds(1300),source:path);
        check(a.Apps.Single().Name=="observed-first"&&b.Apps.Single().Name=="observed-second"&&a.Apps[0].Details!=b.Apps[0].Details,"Moment selection uses neighbouring observed snapshots and preserves reused PID/start identities");
        check(PerformanceInvestigation.Moment(query,start.AddSeconds(1050),source:path).Observed==null,"A gap has no invented process snapshot");
        var gpu=PerformanceInvestigation.Moment(query with{Metric="gpu",Device="gpu-b"},start.AddSeconds(20),source:path);
        check(gpu.Apps.Single().Average==60,"Moment attribution respects selected GPU identity");
        check(PerformanceInvestigation.Moment(query with{Metric="ram"},start.AddSeconds(20),source:path).Apps.Single().Average==1024,"RAM investigation returns recorded working sets");
        check(PerformanceInvestigation.Moment(query with{Metric="disk"},start.AddSeconds(20),source:path).Apps.Length==0,"Drive history never claims recorded per-process I/O attribution");
        var range=PerformanceHistory.Query(query with{Tool="get_top_processes",From=start.AddSeconds(1250),To=start.AddSeconds(1350)},source:path);
        check(range.Rows.Length==1&&range.Rows[0].Name=="observed-second","Selected-range app queries exclude observations outside the interval");
        performance.SetHistorySourceForVerification(path);performance.Select(PerformancePanel.Cpu);await performance.HistoryForVerification(1);UpdateLayout();
        check(performance.TimelineForVerification.EventCountForVerification>0,"Recorded analysis exposes interactive factual markers");
        performance.TimelineForVerification.SelectMomentForVerification(start.AddSeconds(1300));await performance.InvestigationReadyForVerification;
        check(performance.HistoryRowsForVerification==1&&performance.HistoryTextForVerification.Contains("Observed"),"Timeline moment semantic action updates the integrated app table");
        await S1Verification.Snapshot(this,root,"investigation-pinned-moment");
        performance.TimelineForVerification.SelectRangeForVerification(start.AddSeconds(1250),start.AddSeconds(1350));await performance.InvestigationReadyForVerification;
        check(performance.HistoryTextForVerification.StartsWith("Selected")&&performance.HistoryRowsForVerification>0,"Overview selection scopes the integrated facts to its actual range");
        performance.MoveHistoryForVerification(-1);await performance.InvestigationReadyForVerification;
        check(performance.TimelineForVerification.To<start.AddSeconds(1350)&&performance.TimelineForVerification.To-performance.TimelineForVerification.From<=TimeSpan.FromSeconds(100),"Previous range preserves the selected interval duration and loads its neighbouring timeline");
        performance.ResetInvestigationForVerification();await performance.InvestigationReadyForVerification;
        performance.TimelineForVerification.SelectEventsForVerification();await performance.InvestigationReadyForVerification;
        check(performance.HistoryTextForVerification.StartsWith("Recorded events")&&performance.HistoryRowsForVerification>0,"Grouped marker semantic action exposes its type/time/details");
        performance.ResetInvestigationForVerification();await performance.InvestigationReadyForVerification;
        var chart=performance.TimelineForVerification;var source=PresentationSource.FromVisual(chart);
        chart.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Home){RoutedEvent=Keyboard.KeyDownEvent});await performance.InvestigationReadyForVerification;
        check(performance.HistoryTextForVerification.Contains("Observed"),"Actual routed keyboard Home action investigates the first visible moment");
        var before=chart.To-chart.From;
        chart.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.MouseWheelEvent});await performance.InvestigationReadyForVerification;
        check(chart.To-chart.From<before,"Actual routed mouse-wheel action zooms the selected timeline");
        chart.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});await performance.InvestigationReadyForVerification;
        check(!performance.HistoryTextForVerification.StartsWith("Selected"),"Keyboard Escape clears the timeline investigation");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})foreach(double width in new[]{1400d,820d})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=width;Height=850;
            await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(performance.RevealTimelineForVerification(),"Whole timeline including axes/overview is reachable without clipping at "+theme+scale+width);
            await S1Verification.Snapshot(this,root,$"investigation-{theme}-{scale:0.0}-{width:0}");
            check(performance.AnalysisLayoutForVerification,"Investigation timeline and table fit "+theme+scale+width);
        }
        performance.SetHistorySourceForVerification(null);Settings.TextScale=1;Settings.NativeMode="dark";Width=1400;ApplyAppearance();
        performance.Select(-1);performance.LiveForVerification();
    }
}
#endif
