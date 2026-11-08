using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace FileExplorer;

internal sealed partial class PerformancePanel
{
    internal Border Analysis=>details;
    readonly ScrollViewer analysisScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    internal event Action AnalysisChanged;
    readonly PerformanceTimeline analysisGraph=new(){MinHeight=180,Margin=new(0,12,0,12)};
    readonly ComboBox range=new(){MinWidth=130},analysisKind=new(){MinWidth=145};
    readonly TextBox appFilter=new(){MinWidth=130,MaxWidth=200,ToolTip="Observed process name"},threshold=new(){Width=65,Text="80",ToolTip="Spike threshold: percent, or bytes/s for drive I/O"};
    readonly TextBlock historyStatus=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,8,0,0)};
    CancellationTokenSource historyCancel;Task historyQuery=Task.CompletedTask,historyWork=Task.CompletedTask;
    CancellationTokenSource hoverCancel;Task hoverQuery=Task.CompletedTask,hoverWork=Task.CompletedTask;
    bool ready;string sortProperty;ListSortDirection sortDirection;
    string historySource;bool includeLiveHistory=true;
    DateTimeOffset? investigationMoment,investigationFrom,investigationTo,historyEnd;
    TimeSpan? historyWindow;bool changingRange;
    PerformanceFact[] selectedEvents;
    internal bool Historical=>range.SelectedIndex>0||analysisKind.SelectedIndex>0||investigationMoment.HasValue||investigationFrom.HasValue||selectedEvents!=null||historyEnd.HasValue;
#if DEBUG
    internal void RefreshForVerification()=>Refresh();
    internal async Task HistoryForVerification(int kind){range.SelectedIndex=2;analysisKind.SelectedIndex=kind;await historyQuery;}
    internal string HistoryTextForVerification=>detailState.Text;
    internal int HistoryRowsForVerification=>table.Items.Count;
    internal void LiveForVerification(){range.SelectedIndex=0;analysisKind.SelectedIndex=0;}
    internal bool AnalysisLayoutForVerification=>analysisGraph.ActualHeight>=60&&table.ActualHeight>=80&&table.ActualHeight<details.ActualHeight;
    internal PerformanceTimeline TimelineForVerification=>analysisGraph;
    internal void SetHistorySourceForVerification(string source,bool live=false){historySource=source;includeLiveHistory=source==null||live;}
    internal Task InvestigationReadyForVerification=>historyQuery;
    internal Task HoverReadyForVerification=>hoverQuery;
    internal void ResetInvestigationForVerification()=>ResetInvestigation();
    internal void MoveHistoryForVerification(int direction)=>MoveHistory(direction);
    internal bool RevealTimelineForVerification()
    {
        if(analysisScroll.Content is not FrameworkElement content)return false;
        analysisScroll.ScrollToVerticalOffset(analysisGraph.TranslatePoint(new Point(),content).Y);UpdateLayout();
        var bounds=analysisGraph.TransformToAncestor(analysisScroll).TransformBounds(new Rect(analysisGraph.RenderSize));
        return bounds.Top>=-1&&bounds.Bottom<=analysisScroll.ActualHeight+1;
    }
#endif
    void BuildAnalysis()
    {
        details.SetResourceReference(StyleProperty,"StatCard");details.Padding=new Thickness(16,48,16,14);
        var grid=new Grid();
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(3,GridUnitType.Star),GridLength.Auto,new GridLength(2,GridUnitType.Star),GridLength.Auto})grid.RowDefinitions.Add(new(){Height=height});
        var heading=new DockPanel();
        var back=Action("Back to files",()=>Select(-1));DockPanel.SetDock(back,Dock.Right);heading.Children.Add(back);
        detailTitle.FontSize=18;detailTitle.VerticalAlignment=VerticalAlignment.Center;
        var title=new StackPanel();var category=new TextBlock{Text="System analysis",FontSize=11};category.SetResourceReference(TextBlock.ForegroundProperty,"Muted");title.Children.Add(category);title.Children.Add(detailTitle);heading.Children.Add(title);grid.Children.Add(heading);
        var controls=new WrapPanel{Margin=new(0,10,0,0)};Grid.SetRow(controls,1);grid.Children.Add(controls);
        void Add(Control control,string name)
        {
            control.Margin=new Thickness(0,0,10,6);control.MinHeight=32;AutomationProperties.SetName(control,name);control.ToolTip??=name;
            if(control is ComboBox)control.SetResourceReference(StyleProperty,"PreviewClipCombo");controls.Children.Add(control);
            if(control is TextBox)control.SetResourceReference(StyleProperty,"PerformanceFilter");
        }
        range.ItemsSource=new[]{"Rolling hour","Last 15 minutes","Last hour","Last 24 hours","Last 7 days","Last 30 days","Selected interval"};range.SelectedIndex=0;
        analysisKind.ItemsSource=new[]{"Activity","Summary","Top apps","Spikes","App history","Events","Boot diagnostics","System changes","Insights"};analysisKind.SelectedIndex=0;
        Add(range,"Performance time range");Add(analysisKind,"Analysis section");
        BuildProcessControls();Add(columnsButton,"Choose process columns");Add(processFilter,"Filter CPU processes");
        gpuPicker.Visibility=Visibility.Collapsed;Add(gpuPicker,"GPU adapter");
        gpuPicker.SelectionChanged+=(_,_)=>{gpu=(gpuPicker.SelectedItem as GpuReading)?.Id;Refresh();if(ready&&Historical)_ = LoadHistory();};
        Add(appFilter,"App history name");Add(threshold,"Spike threshold");
        Button Glyph(string glyph,Action action){var button=Action(glyph,action);button.FontFamily=new("Segoe Fluent Icons");button.FontSize=13;button.Width=button.MinWidth=32;button.Padding=new Thickness(0);return button;}
        Add(Glyph("\uE72C",()=>{if(Historical)_ = LoadHistory();else Refresh();}),"Refresh analysis");
        Add(Glyph("\uE76B",()=>MoveHistory(-1)),"Previous time range");
        Add(Glyph("\uE76C",()=>MoveHistory(1)),"Next time range");
        Add(Action("Now",ResetInvestigation),"Clear selection and return to now");
        BuildAdoptionControls(controls);
        enable.SetResourceReference(StyleProperty,"AuxiliaryAction");enable.Click+=async(_,_)=>await EnableIo();Add(enable,"Enable detailed I/O");
        var historyMenu=new ContextMenu();
        var keep=new MenuItem{Header="Saved history retention"};historyMenu.Items.Add(keep);
        foreach(int days in new[]{1,7,30})
        {
            var item=new MenuItem{Header=days==1?"1 day":days+" days",IsCheckable=true,IsChecked=Settings.PerformanceRetention==days};keep.Items.Add(item);
            item.Click+=(_,_)=>{Settings.PerformanceRetention=days;foreach(MenuItem sibling in keep.Items)sibling.IsChecked=ReferenceEquals(sibling,item);Settings.Save();UpdateHistoryStatus();};
        }
        var settings=Glyph("\uE713",()=>{});settings.ToolTip="History settings";
        settings.Click+=(_,_)=>{historyMenu.PlacementTarget=settings;historyMenu.IsOpen=true;};Add(settings,"History retention; collection and saving are automatic");
        controls.SizeChanged+=(_,_)=>{bool narrow=controls.ActualWidth<420;range.MinWidth=narrow?100:130;analysisKind.MinWidth=narrow?100:145;range.FontSize=analysisKind.FontSize=narrow?11:12;};
        range.SelectionChanged+=(_,_)=>{if(changingRange)return;ClearInvestigation();historyEnd=null;historyWindow=null;ChangedAnalysis();};
        analysisKind.SelectionChanged+=(_,_)=>{investigationMoment=null;selectedEvents=null;ChangedAnalysis();};
        appFilter.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){_ = LoadHistory();e.Handled=true;}};
        threshold.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){_ = LoadHistory();e.Handled=true;}};
        analysisGraph.MomentSelected+=time=>{investigationFrom=investigationTo=null;investigationMoment=time;selectedEvents=null;SetDetailKind();_ = LoadHistory();};
        analysisGraph.RangeSelected+=(from,to)=>{investigationMoment=null;selectedEvents=null;investigationFrom=from;investigationTo=to;historyWindow=to-from;historyEnd=to;changingRange=true;try{range.SelectedIndex=6;}finally{changingRange=false;}SetDetailKind();_ = LoadHistory();};
        analysisGraph.EventsSelected+=events=>{investigationFrom=investigationTo=null;selectedEvents=events;investigationMoment=null;SetDetailKind();_ = LoadHistory();};
        analysisGraph.MomentHovered+=time=>{hoverCancel?.Cancel();var previous=hoverQuery;var cancellation=new CancellationTokenSource();hoverCancel=cancellation;hoverQuery=ReadHover(previous,time,cancellation);};
        analysisGraph.SelectionCleared+=ResetInvestigation;
        var plots=new Grid();plots.Children.Add(analysisGraph);plots.Children.Add(appCharts);Grid.SetRow(plots,2);grid.Children.Add(plots);
        detailState.TextWrapping=TextWrapping.NoWrap;detailState.TextTrimming=TextTrimming.CharacterEllipsis;detailState.SetBinding(ToolTipProperty,new Binding(nameof(TextBlock.Text)){Source=detailState});detailState.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(detailState,3);grid.Children.Add(detailState);
        table.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");AutomationProperties.SetName(table,"Performance activity");
        VirtualizingPanel.SetIsVirtualizing(table,true);VirtualizingPanel.SetVirtualizationMode(table,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(table,true);
        table.AddHandler(GridViewColumnHeader.ClickEvent,new RoutedEventHandler((sender,e)=>
        {
            if(e.OriginalSource is not GridViewColumnHeader header)return;
            string property=header.Tag as string??(header.Column?.DisplayMemberBinding as Binding)?.Path.Path;
            if(property==null)return;
            if(ReferenceEquals(table.ItemsSource,processItems))property=property switch{"Percent"=>"SortUsage","MemoryText"=>"Memory",_=>property};
            sortDirection=sortProperty==property&&sortDirection==ListSortDirection.Descending?ListSortDirection.Ascending:ListSortDirection.Descending;sortProperty=property;SortRows();
        }));
        Grid.SetRow(table,4);grid.Children.Add(table);
        historyStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetRow(historyStatus,5);grid.Children.Add(historyStatus);
        historyStatus.TextWrapping=TextWrapping.NoWrap;historyStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        void FitAnalysis()
        {
            // Star rows need a finite height even inside the small-window
            // scroller, preserving table virtualization and the whole timeline.
            grid.Height=Math.Max(analysisScroll.ActualHeight,(appCharts.IsVisible?290:180)+110+heading.ActualHeight+controls.ActualHeight+detailState.ActualHeight+historyStatus.ActualHeight+48);
        }
        analysisScroll.Content=grid;analysisScroll.SizeChanged+=(_,_)=>FitAnalysis();controls.SizeChanged+=(_,_)=>FitAnalysis();
        details.Child=analysisScroll;ready=true;ChangedAnalysis();
    }
    static Button Action(string label,Action click)
    {
        var button=new Button{Content=label,MinHeight=32};button.SetResourceReference(StyleProperty,"AuxiliaryAction");button.Click+=(_,_)=>click();return button;
    }
    void SortRows()
    {
        if(table.ItemsSource==null)return;
        var view=CollectionViewSource.GetDefaultView(table.ItemsSource);using var defer=view.DeferRefresh();view.SortDescriptions.Clear();
        bool processes=ReferenceEquals(table.ItemsSource,processItems);
        if(processes)view.SortDescriptions.Add(new("Pinned",ListSortDirection.Descending));
        string filter=processFilter.Text.Trim();
        view.Filter=processes&&filter.Length>0?item=>item is PerformanceProcessRow row&&(row.Pinned||row.Process.Contains(filter,StringComparison.OrdinalIgnoreCase)||row.Pid.ToString().Contains(filter,StringComparison.OrdinalIgnoreCase)||(processColumns["ExecutablePath"]&&row.ExecutablePath.Contains(filter,StringComparison.OrdinalIgnoreCase))||(processColumns["CommandLine"]&&row.CommandLine.Contains(filter,StringComparison.OrdinalIgnoreCase))):null;
        if(sortProperty!=null)view.SortDescriptions.Add(new(sortProperty,sortDirection));
        else if(processes)view.SortDescriptions.Add(new("SortUsage",ListSortDirection.Descending));
        if(processes){view.SortDescriptions.Add(new("Process",ListSortDirection.Ascending));view.SortDescriptions.Add(new("Pid",ListSortDirection.Ascending));}
    }
    void ChangedAnalysis()
    {
        appChartCancel?.Cancel();appCharts.Visibility=Visibility.Collapsed;analysisGraph.Visibility=Visibility.Visible;displayedApp=null;displayedResult=null;
        if(!ready)return;historyCancel?.Cancel();SetDetailKind();appFilter.Visibility=analysisKind.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;
        threshold.Visibility=analysisKind.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;
        sortProperty=null;if(Historical&&selected>=0)_ = LoadHistory();else {ConfigureTable();Refresh();}
        UpdateHistoryStatus();
    }
    void UpdateHistoryStatus()
    {
        historyStatus.Text=monitor.HistoryIssue??$"History automatic · up to {Settings.PerformanceRetention} {(Settings.PerformanceRetention==1?"day":"days")} · 256 MiB limit";
        historyStatus.ToolTip=monitor.HistoryIssue??"Aggregate observations are saved every 5s and selected CPU/RAM/GPU app snapshots every 10s, including in the tray. Oldest observations expire by retention or storage capacity. Command lines and file I/O are not retained.";
    }
    void UpdateAnalysisGraph()
    {
        UpdateHistoryStatus();if(!Historical&&selected>=0){analysisGraph.SetLive(graphs[selected]);SortRows();}
    }
    void ClearInvestigation(){investigationMoment=investigationFrom=investigationTo=null;selectedEvents=null;analysisGraph.ClearSelection();}
    async Task ReadHover(Task previous,DateTimeOffset time,CancellationTokenSource cancellation)
    {
        await previous;
        try
        {
            if(cancellation.IsCancellationRequested||disposed||selected<0||selected==Folder)return;
            string metric=selected switch{Ram=>"ram",Gpu=>"gpu",Drive=>"disk",_=>"cpu"};
            var query=new PerformanceQuery(Metric:metric,Device:selected==Gpu?gpu:null);
            var apps=includeLiveHistory?monitor.ProcessHistory():null;
            var work=Task.Run(()=>PerformanceInvestigation.Moment(query,time,cancellation.Token,historySource,apps),cancellation.Token);hoverWork=work;
            var moment=await work;if(!cancellation.IsCancellationRequested&&!disposed)analysisGraph.SetMoment(moment,metric);
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
        catch(Exception ex)when(ex is ArgumentException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {if(!cancellation.IsCancellationRequested&&!disposed)analysisGraph.SetMoment(new(time,null,[],"History unavailable: "+ex.Message),"cpu");}
        finally{if(!ReferenceEquals(hoverCancel,cancellation))cancellation.Dispose();}
    }
    void ResetInvestigation()
    {
        ClearInvestigation();historyEnd=null;historyWindow=null;
        changingRange=true;try{range.SelectedIndex=0;}finally{changingRange=false;}
        SetDetailKind();if(Historical)_ = LoadHistory();else{ConfigureTable();Refresh();}
    }
    void MoveHistory(int direction)
    {
        var span=historyWindow??TimeSpan.FromMinutes(RangeMinutes());
        var end=historyEnd??analysisGraph.To??DateTimeOffset.UtcNow;
        historyEnd=end+span*direction;if(historyEnd>DateTimeOffset.UtcNow)historyEnd=DateTimeOffset.UtcNow;
        ClearInvestigation();SetDetailKind();_ = LoadHistory();
    }
    int RangeMinutes()=>range.SelectedIndex==6?Math.Clamp((int)Math.Ceiling((historyWindow??TimeSpan.FromHours(1)).TotalMinutes),1,43200):new[]{60,15,60,1440,10080,43200}[Math.Max(0,range.SelectedIndex)];
    Task LoadHistory()
    {
        historyCancel?.Cancel();var previous=historyQuery;var cancellation=new CancellationTokenSource();historyCancel=cancellation;
        return historyQuery=ReadHistory(previous,cancellation);
    }
    async Task ReadHistory(Task previous,CancellationTokenSource cancellation)
    {
        await previous;
        try
        {
            if(cancellation.IsCancellationRequested||disposed||selected<0)return;
            if(selected==Folder){detailState.Text="Folder/file activity is live only; no background folder I/O history is retained.";table.ItemsSource=null;analysisGraph.Set([]);return;}
            string metric=selected switch{Ram=>"ram",Gpu=>"gpu",Drive=>"disk",_=>"cpu"};
            string tool=analysisKind.SelectedIndex switch{2=>"get_top_processes",3=>"find_spikes",4=>"get_app_history",5=>"get_events",_=>"get_usage_summary"};
            if(!double.TryParse(threshold.Text,out double limit))throw new ArgumentException("Enter a numeric spike threshold");
            int minutes=RangeMinutes();
            var to=historyEnd??DateTimeOffset.UtcNow;
            var query=new PerformanceQuery(tool,minutes,metric,selected==Gpu?gpu:selected==Drive?WindowsPerformanceCounters.ForDisk(monitor.Latest,diskNumber)?.Id:null,appFilter.Text,limit,500,investigationFrom??to-(historyWindow??TimeSpan.FromMinutes(minutes)),investigationTo??to);
            var moment=investigationMoment;var events=selectedEvents;bool selectedRange=investigationFrom.HasValue;
            if(selectedRange&&analysisKind.SelectedIndex==0&&selected!=Drive)query=query with{Tool="get_top_processes"};
            // Freeze one shared-sampler snapshot for this request. No second
            // collector, recording setting, or live/recorded source switch.
            var live=includeLiveHistory?monitor.History():null;
            var apps=includeLiveHistory?monitor.ProcessHistory():null;
            detailState.Text="Reading available history…";
            int kind=analysisKind.SelectedIndex;
            var work=Task.Run(async()=>
            {
                if(kind>=6)return(Result:await ReadAdoption(kind,query,cancellation.Token,live,apps),Timeline:(PerformanceTimelineData)null,Moment:(PerformanceMoment)null);
                if(moment is {} time)
                {
                    var snapshot=PerformanceInvestigation.Moment(query,time,cancellation.Token,historySource,apps);
                    return (Result:new PerformanceResult(snapshot.Status,snapshot.Apps,[],snapshot.Apps.Length,false),Timeline:(PerformanceTimelineData)null,Moment:snapshot);
                }
                if(events!=null)return(Result:new PerformanceResult("Recorded events / sampled threshold spikes · select another graph moment to inspect contemporaneous apps.",events,[],events.Length,false),Timeline:(PerformanceTimelineData)null,Moment:(PerformanceMoment)null);
                var result=PerformanceHistory.Query(query,cancellation.Token,historySource,live,apps);
                var timeline=selectedRange?null:PerformanceInvestigation.Read(query,cancellation.Token,historySource,live,apps);
                return(Result:result,Timeline:timeline,Moment:(PerformanceMoment)null);
            },cancellation.Token);historyWork=work;var loaded=await work;var result=loaded.Result;
            if(cancellation.IsCancellationRequested||disposed)return;
            displayedQuery=query;displayedResult=result;displayedApp=null;
            var view=new GridView{AllowsColumnReorder=false};
            foreach(var column in new[]{("Observed / peak time","When",135d),("Metric / app / event","Name",210d),("PID","Pid",65d),(moment.HasValue?metric=="ram"?"Working set (bytes)":"Usage (%)":"Average","Average",115d),("Peak","Peak",95d),("Samples","Observations",80d),("Details","Details",460d)})
                view.Columns.Add(new(){Header=column.Item1,Width=column.Item3,DisplayMemberBinding=new Binding(column.Item2){StringFormat=column.Item2 is "Average" or "Peak"?"{0:0.##}":null}});
            table.View=view;table.ItemsSource=result.Rows;sortProperty=null;
            if(kind==4&&result.Rows.Length>0)table.SelectedIndex=0;
            if(loaded.Moment is {} snapshot)analysisGraph.SetMoment(snapshot,metric);
            if(loaded.Timeline is {} timeline)
            {
                analysisGraph.SetHistory(timeline.Activity.Points.Select(x=>(x.Time,x.Peak??x.Value)).ToArray(),metric=="disk"?0:100,Math.Max(15,(query.To.Value-query.From.Value).TotalSeconds/600*2));
                analysisGraph.SetEvents(timeline.Events);
            }
            string status=loaded.Timeline is {}?$"{query.From.Value.ToLocalTime():g} – {query.To.Value.ToLocalTime():g} · observed peaks; gaps retained":result.Status;
            detailState.Text=(selectedRange?$"Selected {query.From.Value.ToLocalTime():g} – {query.To.Value.ToLocalTime():g} · ":"")+status+(result.Truncated?$" Showing {result.Rows.Length:N0} of {result.Total:N0} rows; refine the range.":"")+(loaded.Timeline is {EventCount:>0} rail?$" · {rail.Events.Length:N0} of {rail.EventCount:N0} markers":"");
            detailState.ToolTip=result.Status;
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
        catch(Exception ex)when(ex is ArgumentException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {if(!cancellation.IsCancellationRequested&&!disposed){detailState.Text="History unavailable: "+ex.Message;table.ItemsSource=null;analysisGraph.Set([]);}}
        finally {if(!ReferenceEquals(historyCancel,cancellation))cancellation.Dispose();}
    }
}
