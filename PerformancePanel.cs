using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

internal sealed partial class PerformancePanel : StackPanel,IAsyncDisposable
{
    readonly PerformanceMonitor monitor;
    internal const int Cpu=0,Ram=1,Gpu=2,Drive=3,Folder=4;
    readonly UniformGrid cards=new(){Columns=5};
    readonly TextBlock[] values=new TextBlock[5],subtitles=new TextBlock[5];
    readonly TextBlock[] headings=new TextBlock[5];
    readonly PerformanceGraph[] graphs=new PerformanceGraph[5];
    readonly Button[] buttons=new Button[5];
    readonly Queue<(DateTimeOffset Time,double? Value)> folderHistory=new();
    IoReport lastReport;
    readonly Border details=new(){Visibility=Visibility.Collapsed};
    readonly ListView table=new(){MinHeight=90,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    readonly TextBlock detailTitle=new(){FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock detailState=new(){FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,4,0,4)};
    readonly ComboBox gpuPicker=new(){MinWidth=180,MaxWidth=310,DisplayMemberPath=nameof(GpuReading.Name)};
    readonly Button enable=new(){Content="Enable detailed I/O",Visibility=Visibility.Collapsed};
    PerformanceIoClient io;
    string folder,gpu,traceFolder,volumeRoot;
    uint? diskNumber;Task mapping=Task.CompletedTask;
    int selected=-1;bool scheduled,disposed,enabling;volatile bool presented;string traceScope;bool traceActive;Task retiring=Task.CompletedTask;
    internal bool Showing=>Visibility==Visibility.Visible&&IsVisible&&Window.GetWindow(this)?.WindowState!=WindowState.Minimized;
    internal bool AutomaticIo {get;set;}=true;
    bool permissionAttempted;
    internal void OwnerFocusChanged()=>_ = UpdateScope();
    internal async Task EndAdministratorIo(){permissionAttempted=true;await ReleaseIo();Refresh();}
    internal void AdministratorReady(){if(AutomaticIo&&Showing&&!disposed){permissionAttempted=false;MaybeEnableIo();}}
    internal int Selected=>selected;
    internal string Scope=>folder;
#if DEBUG
    internal IoReport IoForVerification=>io?.Latest;
    internal Task SyncForVerification()=>UpdateScope();
    internal void FocusForVerification(int index){buttons[index].Focus();Select(index);}
    internal async Task EnableForVerification(){await ReleaseIo();io=new PerformanceIoClient();io.Changed+=Updated;await io.Enable(false);await UpdateScope();}
#endif
    internal PerformancePanel()
    {
        monitor=PerformanceMonitor.Current;Orientation=Orientation.Vertical;
        for(int i=0;i<5;i++)
        {
            int index=i;
            var body=new StackPanel();
            var title=headings[i]=new TextBlock{Text=new[]{"CPU","RAM","GPU","Drive I/O","Folder I/O"}[i],FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};title.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            values[i]=new TextBlock{Text="—",FontSize=20,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
            subtitles[i]=new TextBlock{FontSize=11,Text="Waiting for a sample",TextTrimming=TextTrimming.CharacterEllipsis};subtitles[i].SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            graphs[i]=new PerformanceGraph{Height=18,Margin=new(0,5,0,0)};
            var header=new Grid{MinHeight=27};header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            values[i].Margin=new Thickness(5,0,0,0);
            header.Children.Add(title);header.Children.Add(values[i]);Grid.SetColumn(values[i],1);
            body.Children.Add(header);body.Children.Add(subtitles[i]);body.Children.Add(graphs[i]);
            var button=buttons[i]=new Button{Content=body,Padding=new Thickness(12,9,14,9),Margin=new Thickness(0,0,i==4?0:10,0),Focusable=true,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
            button.SetResourceReference(StyleProperty,"PerformanceCard");AutomationProperties.SetName(button,title.Text+" performance details");
            button.Click+=(_,_)=>Select(index);cards.Children.Add(button);
        }
        Children.Add(cards);
        BuildAnalysis();
        SizeChanged+=(_,_)=>
        {
            for(int i=0;i<5;i++)
            {
                bool compact=ActualWidth<900,tight=ActualWidth<600;
                var header=(Grid)values[i].Parent;header.MinHeight=compact?42:27;
                Grid.SetRow(values[i],compact?1:0);Grid.SetColumn(values[i],compact?0:1);Grid.SetColumnSpan(values[i],compact?2:1);
                values[i].FontSize=tight?11:compact?16:20;values[i].Margin=new Thickness(compact?0:5,0,0,0);
                subtitles[i].Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                buttons[i].Padding=tight?new Thickness(6,9,6,9):new Thickness(12,9,14,9);
                buttons[i].Margin=new Thickness(0,0,i==4?0:tight?4:10,0);
            }
            Refresh();
        };
        IsVisibleChanged+=async(_,_)=>{presented=Showing;SetDetailKind();Refresh();await UpdateScope();MaybeEnableIo();};
        monitor.Changed+=Updated;
    }
    internal void SetFolder(string path)
    {
        if(folder==path)return;folder=path;folderHistory.Clear();lastReport=null;diskNumber=null;traceFolder=null;volumeRoot=null;mapping=MapFolder(path,mapping);Refresh();_ = UpdateScope();
    }
    async Task MapFolder(string path,Task previous)
    {
        await previous.ConfigureAwait(false);
        if(disposed||folder!=path)return;
        var resolved=await Task.Run(()=>WindowsPerformanceCounters.MapPath(path)).ConfigureAwait(false);
        if(!disposed&&!Dispatcher.HasShutdownStarted)_ = Dispatcher.BeginInvoke(()=>
        {
            if(disposed||folder!=path)return;
            diskNumber=resolved.Disk;traceFolder=resolved.Folder;volumeRoot=resolved.Volume;Refresh();_ = UpdateScope();MaybeEnableIo();
        });
    }
    void Updated()
    {
        if(disposed||!presented||Dispatcher.HasShutdownStarted)return;
        lock(graphs){if(scheduled)return;scheduled=true;}
        Dispatcher.BeginInvoke(()=>{lock(graphs)scheduled=false;if(Showing)Refresh();});
    }
    internal void Select(int index)
    {
        bool hadFocus=IsKeyboardFocusWithin;int previous=selected;
        if(index!=previous){hoverCancel?.Cancel();ClearInvestigation();}
        selected=index;details.Visibility=index<0?Visibility.Collapsed:Visibility.Visible;
        sortProperty=null;
        cards.Visibility=Visibility.Visible;
        SetDetailKind();
        for(int i=0;i<5;i++)buttons[i].Tag=i==index?"on":null;
        ConfigureTable();Refresh();_ = UpdateScope();
        AnalysisChanged?.Invoke();
        if(index>=0&&Historical)_ = LoadHistory();
        if(index<0)historyCancel?.Cancel();
        if(hadFocus){if(index>=0)table.Focus();else if(previous>=0)buttons[previous].Focus();}
    }
    void ConfigureTable()
    {
        var view=new GridView{AllowsColumnReorder=false};
        if(selected==Cpu&&!Historical){ConfigureProcessTable(view);table.View=view;return;}
        void Column(string title,string property,double width)=>view.Columns.Add(new GridViewColumn{Header=title,DisplayMemberBinding=new Binding(property),Width=width});
        Column("Process","Process",145);Column("PID","Pid",70);
        if(selected is Cpu or Ram or Gpu){if(selected!=Ram)Column(selected==Cpu?"CPU":"GPU","Percent",100);if(selected!=Gpu)Column("Working set","MemoryText",120);}
        else
        {
            Column("File","File",360);Column("Read/s","ReadText",110);Column("Write/s","WriteText",110);Column("Total/s","TotalText",110);
        }
        table.View=view;
    }
    void Refresh()
    {
        if(disposed)return;
        var sample=monitor.Latest;var history=monitor.History();
        var adapter=sample?.Gpus.FirstOrDefault(g=>g.Id==gpu)??sample?.Gpus.FirstOrDefault();gpu=adapter?.Id;
        var disk=WindowsPerformanceCounters.ForDisk(sample,diskNumber);
        values[0].Text=sample?.Cpu is {} cpu?$"{cpu:0}%":"—";
        subtitles[Cpu].Text=sample?.Issue??Observed(history);
        values[Ram].Text=sample?.Ram is {} ram?$"{ram.Usage:0}%":"—";
        subtitles[Ram].Text=sample?.Ram is {} memory?$"{MainWindow.Fmt(memory.Used)} of {MainWindow.Fmt(memory.Total)}":"Memory counters unavailable";
        values[Gpu].Text=adapter?.Usage is {} busy?$"{busy:0}%":"—";
        headings[Gpu].Text="GPU";
        subtitles[Gpu].Text=adapter==null?"GPU counters unavailable":adapter.Memory is {} bytes?$"{MainWindow.Fmt(bytes)}"+(adapter.Capacity>0?$" of {MainWindow.Fmt(adapter.Capacity)}":"")+" VRAM":"Memory counters unavailable";
        values[Drive].Text=disk is {Read:{} read,Write:{} write}?CardRate(read+write):"—";
        headings[Drive].Text=ActualWidth<900?"Drive":"Drive I/O";
        subtitles[Drive].Text=disk==null?"No physical-drive measurement":disk.Busy is {} activity?$"{disk.Id} · {activity:0}% active":$"{disk.Id} · waiting for a sample";
        bool folderCollecting=traceActive&&traceScope==traceFolder&&traceFolder!=null;
        var report=folderCollecting?io?.Latest:null;
        values[Folder].Text=report is {Running:true}?(report.Lost>0?"≥ ":"")+CardRate(report.Rows.Sum(x=>x.Total)):"—";
        headings[Folder].Text=ActualWidth<900?"Folder":"Folder I/O";
        subtitles[Folder].Text=report is {Running:true}?$"Read {CardRate(report.Rows.Sum(x=>x.Read))} · Write {CardRate(report.Rows.Sum(x=>x.Write))}":traceFolder==null?"File I/O unavailable for this path":enabling?"Waiting for permission":io?.Latest.State??"Windows permission required";
        if(Showing&&report!=null&&!ReferenceEquals(lastReport,report))
        {
            lastReport=report;var now=DateTimeOffset.UtcNow;
            while(folderHistory.Count>0&&(folderHistory.Count>=3600||now-folderHistory.Peek().Time>=TimeSpan.FromHours(1)))folderHistory.Dequeue();
            folderHistory.Enqueue((now,report.Running?report.Rows.Sum(x=>x.Total):null));
        }
        buttons[Cpu].ToolTip="CPU · "+values[Cpu].Text+" · "+subtitles[Cpu].Text;
        buttons[Ram].ToolTip="Physical system RAM · "+values[Ram].Text+" · "+subtitles[Ram].Text;
        buttons[Gpu].ToolTip=(adapter?.Name??"GPU unavailable")+" · "+subtitles[Gpu].Text;
        buttons[Drive].ToolTip=(disk is {Read:{} rd,Write:{} wr}?Rate(rd+wr)+" · ":"")+"Physical device I/O; other partitions on this unit may contribute. File details show logical requests. "+subtitles[Drive].Text;
        buttons[Folder].ToolTip=folder+" · "+(report is {Running:true}?Rate(report.Rows.Sum(x=>x.Total))+" · ":"")+subtitles[Folder].Text+(report is {Lost:>0}?" · Displayed throughput is a lower bound: some Windows file events could not be attributed. Open analysis for details.":"");
        graphs[0].Set(history.Select(x=>(x.Time,x.Cpu)).ToArray(),100);
        graphs[Ram].Set(history.Select(x=>(x.Time,x.Ram?.Usage)).ToArray(),100);
        graphs[Gpu].Set(history.Select(x=>(x.Time,x.Gpus.FirstOrDefault(g=>g.Id==gpu)?.Usage)).ToArray(),100);
        graphs[Drive].Set(history.Select(x=>{var d=WindowsPerformanceCounters.ForDisk(x,diskNumber);return(x.Time,d?.Read+d?.Write);}).ToArray());
        graphs[Folder].Set(folderHistory.ToArray());
        if(selected<0)return;
        columnsButton.Visibility=selected==Cpu&&!Historical?Visibility.Visible:Visibility.Collapsed;
        processFilter.Visibility=columnsButton.Visibility;
        UpdateAnalysisGraph();
        detailTitle.Text=new[]{"CPU","Physical RAM","GPU",$"Drive · {volumeRoot??PathRoot(folder)}",$"Folder · {folder}"}[selected];
        gpuPicker.Visibility=selected==Gpu?Visibility.Visible:Visibility.Collapsed;
        if(selected==Gpu&&sample!=null)
        {
            if(!gpuPicker.Items.Cast<GpuReading>().Select(g=>g.Id).SequenceEqual(sample.Gpus.Select(g=>g.Id)))gpuPicker.ItemsSource=sample.Gpus;
            if(adapter!=null)gpuPicker.SelectedItem=gpuPicker.Items.Cast<GpuReading>().FirstOrDefault(g=>g.Id==gpu);
        }
        enable.Visibility=!Historical&&selected>=Drive&&io?.Connected!=true?Visibility.Visible:Visibility.Collapsed;
        enable.IsEnabled=!enabling&&traceFolder!=null;
        if(Historical)return;
        if(selected<Drive)
        {
            detailState.Text=selected==Cpu?"CPU usage across all logical processors · inaccessible processes omitted":selected==Ram?"Process working sets may share pages; their sum is not system RAM usage":"Busiest GPU engine · "+adapter?.Name;
            if(selected==Cpu)RefreshProcessRows(monitor.Details);
            else table.ItemsSource=selected==Gpu?monitor.Details.Where(x=>x.Adapter==gpu).ToArray():monitor.Details.OrderByDescending(x=>x.Memory).ToArray();
        }
        else
        {
            detailState.Text=traceFolder==null?"File I/O measurement unavailable for this path":io?.Latest.State??"Enable detailed I/O — Windows will ask for administrator permission";
            if(io?.Latest.Lost>0)detailState.Text+=$" · {io.Latest.Lost:N0} observations lost";
            table.ItemsSource=io?.Latest.Rows.Select(x=>new{ x.Process,x.Pid,x.File,ReadText=Rate(x.Read),WriteText=Rate(x.Write),TotalText=Rate(x.Total)}).ToArray();
        }
        SortRows();
    }
    static string PathRoot(string path)=>string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)?null:Path.GetPathRoot(path);
    static string Rate(double bytes)=>MainWindow.Fmt((long)Math.Max(0,bytes))+"/s";
    string CardRate(double bytes)
    {
        if(ActualWidth>=600)return Rate(bytes);
        var units=new[]{" B/s"," KB/s"," MB/s"," GB/s"," TB/s"};int unit=0;
        while(bytes>=1000&&unit<units.Length-1){bytes/=1000;unit++;}
        return bytes.ToString("0")+units[unit];
    }
    static string Observed(PerformanceSample[] history)
    {
        if(history.Length<2)return "Waiting for history";
        var span=history[^1].Time-history[0].Time;
        return span.TotalMinutes<1?$"{span.TotalSeconds:0}s observed":span.TotalMinutes<59?$"{span.TotalMinutes:0.#} min observed":"Last hour";
    }
    void SetDetailKind()=>monitor.DetailKind=Showing&&!Historical?selected switch{Cpu=>1,Gpu=>2,Ram=>3,_=>0}:0;
    async void MaybeEnableIo()
    {
        if(!AutomaticIo||permissionAttempted||!Showing||traceFolder==null||disposed)return;
        permissionAttempted=true;await EnableIo();
    }
    async Task EnableIo()
    {
        if(enabling||disposed)return;enabling=true;permissionAttempted=true;enable.IsEnabled=false;
        try
        {
            if(io?.Connected!=true){await ReleaseIo();io=new PerformanceIoClient();io.Changed+=Updated;}
            await io.Enable();await UpdateScope();Refresh();
        }
        finally{enabling=false;Refresh();}
    }
    Task scopeChanging=Task.CompletedTask;
    Task UpdateScope()
    {
        if(disposed||io?.Connected!=true)return scopeChanging;
        string target=selected==Drive?volumeRoot:traceFolder;
        bool collect=Showing&&!string.IsNullOrWhiteSpace(target)&&Path.IsPathFullyQualified(target);
        if(traceActive==collect&&traceScope==target)return scopeChanging;
        traceActive=collect;traceScope=target;
        return scopeChanging=ChangeScope(io,target,collect);
    }
    async Task ChangeScope(PerformanceIoClient client,string target,bool collect)
    {await client.SetScope(target,collect);if(!disposed&&ReferenceEquals(io,client))Refresh();}
    Task ReleaseIo()
    {
        var old=io;io=null;traceActive=false;traceScope=null;
        if(old!=null){old.Changed-=Updated;retiring=Retire(old,retiring);}
        return retiring;
    }
    static async Task Retire(PerformanceIoClient client,Task previous){await previous.ConfigureAwait(false);await client.DisposeAsync().ConfigureAwait(false);}
    public async ValueTask DisposeAsync()
    {
        disposed=true;historyCancel?.Cancel();hoverCancel?.Cancel();metadataStopped.Cancel();appChartCancel?.Cancel();
        // Closed can synchronously drain us on the UI thread. Wait only for
        // native/background reads, never their UI presentation continuation.
        try{await Task.WhenAll(historyWork,hoverWork,metadataRead,appChartRead).ConfigureAwait(false);}
        // These query failures already have a visible unavailable state. They
        // must not prevent retirement of an independent native I/O worker.
        catch(Exception error)when(error is OperationCanceledException or ArgumentException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException){}
        monitor.Changed-=Updated;monitor.DetailKind=0;appChartCancel?.Dispose();
        await ReleaseIo().ConfigureAwait(false);await mapping.ConfigureAwait(false);historyCancel?.Dispose();hoverCancel?.Dispose();metadataStopped.Dispose();
    }
}

internal sealed class PerformanceGraph : FrameworkElement
{
    (DateTimeOffset Time,double? Value)[] points=Array.Empty<(DateTimeOffset,double?)>();double ceiling,gapSeconds=5;
    internal (DateTimeOffset Time,double? Value)[] Points=>points;
    internal double Ceiling=>ceiling;
    internal double GapSeconds=>gapSeconds;
    internal void Set((DateTimeOffset,double?)[] values,double maximum=0){points=values;ceiling=maximum;InvalidateVisual();}
    internal void CopyTo(PerformanceGraph graph){graph.gapSeconds=gapSeconds;graph.Set(points,ceiling);}
    internal void SetHistory((DateTimeOffset,double?)[] values,double maximum,double gap){gapSeconds=gap;Set(values,maximum);}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);if(ActualWidth<=0||ActualHeight<=0)return;
        var ink=TryFindResource("Accent") as Brush??Brushes.DodgerBlue;
        var muted=TryFindResource("Palette.12FFFFFF") as Brush??Brushes.Gray;
        drawing.DrawLine(new Pen(muted,1),new Point(0,ActualHeight-1),new Point(ActualWidth,ActualHeight-1));
        if(points.Length<2)return;
        var end=points[^1].Time;var start=points[0].Time;
        double seconds=Math.Max(1,(end-start).TotalSeconds);
        double max=ceiling>0?ceiling:Math.Max(1,points.Max(x=>x.Value??0));
        var geometry=new StreamGeometry();using(var context=geometry.Open())
        {
            DateTimeOffset previous=default;bool segment=false;int pixel=-1;
            foreach(var sample in points)
            {
                if(sample.Value is not {} value){segment=false;continue;}
                double x=ActualWidth*(sample.Time-start).TotalSeconds/seconds;
                if(x<0)continue;
                var next=new Point(x,ActualHeight-1-Math.Clamp(value/max,0,1)*(ActualHeight-2));
                if(!segment||sample.Time-previous>TimeSpan.FromSeconds(gapSeconds)){context.BeginFigure(next,false,false);segment=true;pixel=-1;}
                else if((int)x!=pixel)context.LineTo(next,true,false);
                pixel=(int)x;previous=sample.Time;
            }
        }
        geometry.Freeze();drawing.DrawGeometry(null,new Pen(ink,1.4),geometry);
    }
}
