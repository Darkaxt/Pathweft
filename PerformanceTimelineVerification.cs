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
        await VerifyLiveContinuity31(root,check);
    }
    async Task VerifyLiveContinuity31(string root,Action<bool,string> check)
    {
        Settings.PerformanceRecording=false;
        performance.SetHistorySourceForVerification(Path.Combine(root,"unrecorded.sqlite"),true);
        performance.LiveForVerification();performance.Select(PerformancePanel.Cpu);
        var monitor=PerformanceMonitor.Current;var at=DateTimeOffset.UtcNow;
        for(int i=0;i<35;i++)monitor.Record(new(at.AddTicks(i),i<5?95:20,[],[],null,new(40,100)));
        performance.RefreshForVerification();UpdateLayout();
        performance.TimelineForVerification.SelectRangeForVerification(at.AddSeconds(-40),at.AddSeconds(1));await performance.InvestigationReadyForVerification;
        check(performance.HistoryRowsForVerification>0,"Recording-off range inspects already-visible aggregate observations without a database");
        performance.ResetInvestigationForVerification();await performance.InvestigationReadyForVerification;
        check(performance.TimelineForVerification.PointCountForVerification>0&&performance.TimelineForVerification.EventCountForVerification>0,"Now recovers the live graph and its sampled events after range selection with recording off");
        await performance.HistoryForVerification(0);
        check(performance.TimelineForVerification.PointCountForVerification>0,"Last hour retains live observations without recording");
        string mixed=Path.Combine(root,"mixed-analysis.sqlite");Settings.PerformanceRecording=true;
        PerformanceSample Sample(DateTimeOffset time,double cpu)=>new(time,cpu,[],[new("disk-a",100,200,10),new("disk-b",900,1000,80)],null,new(40,100));
        using(var store=new PerformanceHistory(mixed)){store.Record(Sample(at.AddSeconds(-90),10),null,null);store.Record(Sample(at.AddSeconds(-30),99),null,null);}
        Settings.PerformanceRecording=false;
        PerformanceSample[] recent=[Sample(at.AddSeconds(-30),20),Sample(at.AddSeconds(-10),40)];
        var q=new PerformanceQuery(From:at.AddMinutes(-2),To:at);
        var combined=PerformanceHistory.Query(q,source:mixed,live:recent);
        check(combined.Rows.Single().Observations==3&&combined.Rows.Single().Peak==40&&combined.Points.Any(x=>x.Value==null),"Automatic live/recorded overlay retains older data, avoids overlap double counting and preserves real gaps");
        var disk=PerformanceHistory.Query(q with{Metric="disk",Device="disk-a"},source:mixed,live:recent);
        check(disk.Rows.Single().Peak==300,"Combined analysis respects physical device identity");
        using(var cancelled=new CancellationTokenSource())
        {cancelled.Cancel();bool stopped=false;try{PerformanceHistory.Query(q,cancelled.Token,mixed,recent);}catch(OperationCanceledException){stopped=true;}check(stopped,"Combined queries preserve caller cancellation");}
        performance.SetHistorySourceForVerification(null);performance.LiveForVerification();performance.Select(-1);
        performance.Select(PerformancePanel.Cpu);await performance.VerifyProcessInspection31(root,check);
        await S1Verification.Snapshot(this,root,"cpu-columns-pinned");
        foreach(var (theme,scale,width) in new[]{("dark",1d,1400d),("light",1.5,820d)})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=width;Height=850;
            await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            performance.ShowColumnsForVerification();
            await S1Verification.Snapshot(this,root,$"cpu-columns-menu-{theme}-{scale}");
            CaptureMenu(performance.ColumnsMenuForVerification,root,$"cpu-menu-popup-{theme}-{scale}");
            check(performance.ColumnsFitForVerification,"Column chooser and process table remain reachable at "+theme+scale+width);
            performance.HideColumnsForVerification();
        }
        Settings.NativeMode="dark";Settings.TextScale=1;Width=1400;ApplyAppearance();
        performance.Select(-1);
    }
}

internal sealed partial class PerformancePanel
{
    internal System.Windows.Controls.ContextMenu ColumnsMenuForVerification=>processColumnsMenu;
    internal void ShowColumnsForVerification(){analysisScroll.ScrollToTop();UpdateLayout();columnsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
    internal void HideColumnsForVerification()=>processColumnsMenu.IsOpen=false;
    internal bool ColumnsFitForVerification=>columnsButton.IsVisible&&columnsButton.ActualHeight>=32&&processColumnsMenu.IsOpen&&processColumnsMenu.Items.Cast<System.Windows.Controls.MenuItem>().Count(x=>x.IsCheckable)==7&&table.ActualHeight>=90;
    internal async Task VerifyProcessInspection31(string root,Action<bool,string> check)
    {
        using var owner=System.Diagnostics.Process.GetCurrentProcess();
        var identity=new ProcessMetric(owner.Id,owner.StartTime.ToUniversalTime().Ticks,owner.ProcessName,2,owner.WorkingSet64);
        var other=new ProcessMetric(int.MaxValue,identity.Started,"busier-fixture",90,1000);
        RefreshProcessRows([other,identity]);table.SelectedItem=processRows[(identity.Pid,identity.Started)];
        var row=(PerformanceProcessRow)table.SelectedItem;
        void Space()=>table.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(table),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Space();
        check(row.Pinned&&ReferenceEquals(table.Items[0],row),"Keyboard pin moves the selected process above busier entries");
        RefreshProcessRows([other,identity with{Usage=5}]);
        check(ReferenceEquals(table.Items[0],row)&&row.Usage==5&&ReferenceEquals(table.SelectedItem,row),"Pinned identity and selection survive live value refresh");
        sortProperty="Usage";sortDirection=System.ComponentModel.ListSortDirection.Descending;SortRows();
        check(ReferenceEquals(table.Items[0],row),"User sorting keeps pinned processes at the top");
        RefreshProcessRows([identity with{Started=identity.Started+TimeSpan.TicksPerSecond,Process="replacement-fixture"},other]);
        check(!row.Observed&&row.Percent=="—"&&row.Process==identity.Process&&processRows.Count==3,"Exited pinned identity remains explicit and never adopts a reused PID");
        RefreshProcessRows([identity,other]);
        foreach(var item in processColumnsMenu.Items.Cast<System.Windows.Controls.MenuItem>().Skip(4).Take(2))
        {item.IsChecked=true;item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));}
        RefreshProcessRows([identity,other]);
        await metadataWork;
        // The real sampler may have refreshed the list while WMI was working;
        // the pinned row object is the same exact process/start throughout.
        bool pathMatches=string.Equals(row.ExecutablePath,Program.Executable,StringComparison.OrdinalIgnoreCase),commandMatches=row.CommandLine.Contains("--verify-s8-appearance=");
        File.WriteAllText(Path.Combine(root,"process-metadata-readiness.json"),System.Text.Json.JsonSerializer.Serialize(new{row.MetadataRequested,row.Observed,pathMatches,commandMatches,row.MetadataIssue,pathState=pathMatches?"matched":row.ExecutablePath,commandState=commandMatches?"matched":row.CommandLine}));
        check(pathMatches&&commandMatches,"Optional columns resolve actual executable and command line for the exact running process without elevation");
        check(ReferenceEquals(table.Items[0],row)&&processColumns["CommandLine"]&&processColumns["ExecutablePath"],"Optional column choices and pin remain after asynchronous metadata and live refresh");
        table.SelectedItem=row;Space();check(!row.Pinned,"Space unpins the selected process");
        var mismatch=await Task.Run(()=>PerformanceProcessMetadataReader.Read([(identity.Pid,identity.Started+TimeSpan.TicksPerSecond)],CancellationToken.None));
        check(mismatch.Count==0,"Metadata cannot be attached to a mismatched process creation identity");
        table.SelectedItem=row;table.UpdateLayout();
        var container=(System.Windows.Controls.ListViewItem)table.ItemContainerGenerator.ContainerFromItem(row);
        check(container!=null,"Pinned process has a rendered row for the mouse action");
        container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.MouseUpEvent});
        check(row.Pinned,"Actual row mouse-up pins the process through the normal table handler");
        processFilter.Text="no matching process";
        check(table.Items.Count==1&&ReferenceEquals(table.Items[0],row),"Local process filter keeps the explicitly pinned identity available");
        ToggleProcessPin(row);processFilter.Text=identity.Process;
        check(table.Items.Cast<PerformanceProcessRow>().All(x=>x.Process.Contains(identity.Process,StringComparison.OrdinalIgnoreCase)),"Donor-style process filter uses existing observations without collecting more data");
        processFilter.Text="";ToggleProcessPin(row);
        var visibility=Visibility;Visibility=Visibility.Collapsed;
        var hiddenIdentity=identity with{Started=identity.Started+2*TimeSpan.TicksPerSecond};
        RefreshProcessRows([hiddenIdentity]);
        check(!processRows[(hiddenIdentity.Pid,hiddenIdentity.Started)].MetadataRequested&&monitor.DetailKind==0,"Hidden inspection schedules no process metadata or detailed sampling");
        Visibility=visibility;RefreshProcessRows([identity,other]);
    }
}
#endif
