using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

internal sealed class PerformancePanel : StackPanel,IAsyncDisposable
{
    readonly PerformanceMonitor monitor;
    readonly UniformGrid cards=new(){Columns=4};
    readonly TextBlock[] values=new TextBlock[4],subtitles=new TextBlock[4];
    readonly TextBlock[] headings=new TextBlock[4];
    readonly PerformanceGraph[] graphs=new PerformanceGraph[4];
    readonly Button[] buttons=new Button[4];
    readonly Border details=new(){Margin=new(0,8,0,0),Visibility=Visibility.Collapsed};
    readonly ListView table=new(){Height=175,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    readonly TextBlock detailTitle=new(){FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock detailState=new(){FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new(0,4,0,4)};
    readonly ComboBox gpuPicker=new(){MinWidth=180,MaxWidth=310,DisplayMemberPath=nameof(GpuReading.Name)};
    readonly Button enable=new(){Content="Enable detailed I/O",Visibility=Visibility.Collapsed};
    PerformanceIoClient io;
    string folder,gpu,traceFolder,volumeRoot;
    uint? diskNumber;Task mapping=Task.CompletedTask;
    int selected=-1;bool scheduled,disposed,enabling;volatile bool presented;string traceScope;bool traceActive;Task retiring=Task.CompletedTask;
    internal bool Showing=>Visibility==Visibility.Visible&&IsVisible&&Window.GetWindow(this)?.WindowState!=WindowState.Minimized;
    bool FolderFocused=>buttons[3].IsKeyboardFocusWithin||selected==3&&details.IsKeyboardFocusWithin;
    internal void OwnerFocusChanged()=>_ = UpdateScope();
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
        for(int i=0;i<4;i++)
        {
            int index=i;
            var body=new StackPanel();
            var title=headings[i]=new TextBlock{Text=new[]{"CPU","GPU","Drive I/O","Folder I/O"}[i],FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};title.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            values[i]=new TextBlock{Text="—",FontSize=20,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
            subtitles[i]=new TextBlock{FontSize=11,Text="Waiting for a sample",TextTrimming=TextTrimming.CharacterEllipsis};subtitles[i].SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            graphs[i]=new PerformanceGraph{Height=24,Margin=new(0,5,0,0)};
            var header=new Grid();header.RowDefinitions.Add(new());header.RowDefinitions.Add(new());header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            header.Children.Add(title);header.Children.Add(values[i]);Grid.SetRow(values[i],1);
            body.Children.Add(header);body.Children.Add(subtitles[i]);body.Children.Add(graphs[i]);
            var button=buttons[i]=new Button{Content=body,Padding=new Thickness(0),Margin=new Thickness(0,0,i==3?0:8,0),Focusable=true,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
            button.SetResourceReference(StyleProperty,"PerformanceCard");AutomationProperties.SetName(button,title.Text+" performance details");
            button.Click+=(_,_)=>Select(index);cards.Children.Add(button);
        }
        Children.Add(cards);
        details.SetResourceReference(StyleProperty,"StatCard");
        var content=new DockPanel();var heading=new DockPanel();
        var back=new Button{Content="Back to cards",Margin=new Thickness(8,0,0,0)};back.SetResourceReference(StyleProperty,"AuxiliaryAction");back.Click+=(_,_)=>Select(-1);DockPanel.SetDock(back,Dock.Right);heading.Children.Add(back);
        enable.SetResourceReference(StyleProperty,"AuxiliaryAction");enable.Margin=new Thickness(8,0,0,0);enable.Click+=async(_,_)=>await EnableIo();DockPanel.SetDock(enable,Dock.Right);heading.Children.Add(enable);
        gpuPicker.Visibility=Visibility.Collapsed;gpuPicker.SelectionChanged+=(_,_)=>{gpu=(gpuPicker.SelectedItem as GpuReading)?.Id;Refresh();};DockPanel.SetDock(gpuPicker,Dock.Right);heading.Children.Add(gpuPicker);
        heading.Children.Add(detailTitle);DockPanel.SetDock(heading,Dock.Top);content.Children.Add(heading);
        detailState.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(detailState,Dock.Top);content.Children.Add(detailState);
        table.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");AutomationProperties.SetName(table,"Performance activity");
        VirtualizingPanel.SetIsVirtualizing(table,true);VirtualizingPanel.SetVirtualizationMode(table,VirtualizationMode.Recycling);
        content.Children.Add(table);details.Child=content;Children.Add(details);
        SizeChanged+=(_,_)=>
        {
            cards.Columns=ActualWidth<850?2:4;bool compact=ActualWidth<650;
            for(int i=0;i<4;i++)
            {
                buttons[i].Margin=new Thickness(0,0,(i+1)%cards.Columns==0?0:10,cards.Columns==2&&i<2?10:0);
                Grid.SetRow(values[i],1);Grid.SetColumn(values[i],0);Grid.SetColumnSpan(values[i],2);
                headings[i].VerticalAlignment=VerticalAlignment.Center;headings[i].Margin=new Thickness(0);
                graphs[i].Height=compact?12:24;
            }
        };
        IsVisibleChanged+=async(_,_)=>{presented=Showing;if(!Showing){monitor.DetailKind=0;await ReleaseIo();}else{monitor.DetailKind=selected is 0 or 1?selected+1:0;Refresh();await UpdateScope();}};
        IsKeyboardFocusWithinChanged+=async(_,_)=>await UpdateScope();
        monitor.Changed+=Updated;
    }
    internal void SetFolder(string path)
    {
        if(folder==path)return;folder=path;diskNumber=null;traceFolder=null;volumeRoot=null;mapping=MapFolder(path,mapping);Refresh();_ = UpdateScope();
    }
    async Task MapFolder(string path,Task previous)
    {
        await previous.ConfigureAwait(false);
        if(disposed||folder!=path)return;
        var resolved=await Task.Run(()=>WindowsPerformanceCounters.MapPath(path)).ConfigureAwait(false);
        if(!disposed&&!Dispatcher.HasShutdownStarted)_ = Dispatcher.BeginInvoke(()=>
        {
            if(disposed||folder!=path)return;
            diskNumber=resolved.Disk;traceFolder=resolved.Folder;volumeRoot=resolved.Volume;Refresh();_ = UpdateScope();
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
        selected=index;details.Visibility=index<0?Visibility.Collapsed:Visibility.Visible;
        cards.Visibility=index<0?Visibility.Visible:Visibility.Collapsed;
        monitor.DetailKind=Showing&&index is 0 or 1?index+1:0;
        for(int i=0;i<4;i++)buttons[i].Tag=i==index?"on":null;
        ConfigureTable();Refresh();_ = UpdateScope();
        if(hadFocus){if(index>=0)table.Focus();else if(previous>=0)buttons[previous].Focus();}
    }
    void ConfigureTable()
    {
        var view=new GridView{AllowsColumnReorder=false};
        void Column(string title,string property,double width)=>view.Columns.Add(new GridViewColumn{Header=title,DisplayMemberBinding=new Binding(property),Width=width});
        Column("Process","Process",145);Column("PID","Pid",70);
        if(selected is 0 or 1){Column(selected==0?"CPU":"GPU","Percent",100);if(selected==0)Column("Working set","MemoryText",120);}
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
        subtitles[0].Text=sample?.Issue??(history.Length<3600?$"{history.Length}s observed · last hour":"System · last hour");
        values[1].Text=adapter?.Usage is {} busy?$"{busy:0}%":"—";
        headings[1].Text=adapter==null?"GPU":"GPU · "+adapter.Name;headings[1].ToolTip=adapter?.Name;
        subtitles[1].Text=adapter==null?"GPU counters unavailable":adapter.Memory is {} bytes?$"{MainWindow.Fmt(bytes)}"+(adapter.Capacity>0?$" of {MainWindow.Fmt(adapter.Capacity)}":"")+" dedicated":"Memory counters unavailable";
        values[2].Text=disk is {Read:{} read,Write:{} write}?Rate(read+write):"—";
        subtitles[2].Text=disk==null?"No physical-drive measurement":disk.Busy is {} activity?$"{disk.Id} · {activity:0}% active":$"{disk.Id} · waiting for a sample";
        buttons[2].ToolTip="Physical device I/O; other partitions on this unit may contribute. File details show logical requests.";
        values[3].Text=io?.Latest is {Running:true} report&&FolderFocused?(report.Lost>0?"≥ ":"")+Rate(report.Rows.Sum(x=>x.Total)):"—";
        subtitles[3].Text=traceFolder==null?"File I/O unavailable for this path":FolderFocused?io?.Latest.State??"Enable detailed I/O":"Focus this widget to collect";
        buttons[3].ToolTip=folder;
        graphs[0].Set(history.Select(x=>(x.Time,x.Cpu)).ToArray(),100);
        graphs[1].Set(history.Select(x=>(x.Time,x.Gpus.FirstOrDefault(g=>g.Id==gpu)?.Usage)).ToArray(),100);
        graphs[2].Set(history.Select(x=>{var d=WindowsPerformanceCounters.ForDisk(x,diskNumber);return(x.Time,d?.Read+d?.Write);}).ToArray());
        graphs[3].Set(Array.Empty<(DateTimeOffset,double?)>());
        if(selected<0)return;
        detailTitle.Text=new[]{"CPU activity","GPU activity",$"Drive activity · {volumeRoot??PathRoot(folder)}",$"Folder activity · {folder}"}[selected];
        gpuPicker.Visibility=selected==1?Visibility.Visible:Visibility.Collapsed;
        if(selected==1&&sample!=null)
        {
            if(!gpuPicker.Items.Cast<GpuReading>().Select(g=>g.Id).SequenceEqual(sample.Gpus.Select(g=>g.Id)))gpuPicker.ItemsSource=sample.Gpus;
            if(adapter!=null)gpuPicker.SelectedItem=gpuPicker.Items.Cast<GpuReading>().FirstOrDefault(g=>g.Id==gpu);
        }
        enable.Visibility=selected>=2&&io?.Connected!=true?Visibility.Visible:Visibility.Collapsed;
        enable.IsEnabled=!enabling&&traceFolder!=null;
        if(selected<2)
        {
            detailState.Text=selected==0?"CPU usage across all logical processors · inaccessible processes omitted":"Busiest GPU engine · "+adapter?.Name;
            table.ItemsSource=selected==1?monitor.Details.Where(x=>x.Adapter==gpu).ToArray():monitor.Details;
        }
        else
        {
            detailState.Text=traceFolder==null?"File I/O measurement unavailable for this path":selected==3&&!FolderFocused?"Folder collection paused — focus this widget":io?.Latest.State??"Enable detailed I/O — Windows will ask for administrator permission";
            if(io?.Latest.Lost>0)detailState.Text+=$" · {io.Latest.Lost:N0} observations lost";
            table.ItemsSource=io?.Latest.Rows.Select(x=>new{ x.Process,x.Pid,x.File,ReadText=Rate(x.Read),WriteText=Rate(x.Write),TotalText=Rate(x.Total)}).ToArray();
        }
    }
    static string PathRoot(string path)=>string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)?null:Path.GetPathRoot(path);
    static string Rate(double bytes)=>MainWindow.Fmt((long)Math.Max(0,bytes))+"/s";
    async Task EnableIo()
    {
        if(enabling)return;enabling=true;enable.IsEnabled=false;
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
        string target=FolderFocused?traceFolder:selected==2?volumeRoot:null;
        bool collect=Showing&&(selected==2||FolderFocused&&Window.GetWindow(this)?.IsActive==true)&&!string.IsNullOrWhiteSpace(target)&&Path.IsPathFullyQualified(target);
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
    public async ValueTask DisposeAsync(){disposed=true;monitor.Changed-=Updated;monitor.DetailKind=0;await ReleaseIo().ConfigureAwait(false);await mapping.ConfigureAwait(false);}
}

internal sealed class PerformanceGraph : FrameworkElement
{
    (DateTimeOffset Time,double? Value)[] points=Array.Empty<(DateTimeOffset,double?)>();double ceiling;
    internal void Set((DateTimeOffset,double?)[] values,double maximum=0){points=values;ceiling=maximum;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);if(ActualWidth<=0||ActualHeight<=0)return;
        var ink=TryFindResource("Accent") as Brush??Brushes.DodgerBlue;
        var muted=TryFindResource("Palette.12FFFFFF") as Brush??Brushes.Gray;
        drawing.DrawLine(new Pen(muted,1),new Point(0,ActualHeight-1),new Point(ActualWidth,ActualHeight-1));
        if(points.Length<2)return;
        var end=points[^1].Time;double max=ceiling>0?ceiling:Math.Max(1,points.Max(x=>x.Value??0));
        var geometry=new StreamGeometry();using(var context=geometry.Open())
        {
            DateTimeOffset previous=default;bool segment=false;int pixel=-1;
            foreach(var sample in points)
            {
                if(sample.Value is not {} value){segment=false;continue;}
                double x=ActualWidth*(1-(end-sample.Time).TotalSeconds/3600);
                if(x<0)continue;
                var next=new Point(x,ActualHeight-1-Math.Clamp(value/max,0,1)*(ActualHeight-2));
                if(!segment||sample.Time-previous>TimeSpan.FromSeconds(5)){context.BeginFigure(next,false,false);segment=true;pixel=-1;}
                else if((int)x!=pixel)context.LineTo(next,true,false);
                pixel=(int)x;previous=sample.Time;
            }
        }
        geometry.Freeze();drawing.DrawGeometry(null,new Pen(ink,1.4),geometry);
    }
}
