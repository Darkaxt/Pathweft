using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

// Pathweft host for SysOptimizer's selected History graph/navigation logic.
// Donor provenance and original source are recorded in PerformanceHistoryChart.
internal sealed class PerformanceTimeline : FrameworkElement
{
    (DateTimeOffset Time,double? Value)[] points=[];
    DrivePoint[] drive;
    PerformanceFact[] events=[];
    readonly List<(Rect Bounds,PerformanceFact[] Events)> groups=[];
    double ceiling,gapSeconds=15;DateTimeOffset? cursor,hover,selectionFrom,selectionTo,viewFrom,viewTo;
    Point? pressed;bool overviewDrag;PerformanceMoment moment;string momentMetric="cpu";
    enum NavDrag{None,Move,Left,Right}
    NavDrag navDrag;double navGrab;
    (double X,DateTimeOffset From,DateTimeOffset To)? pan;
    const double DragThreshold=5,EdgeGrip=7,MinSelection=8;
    internal event Action<DateTimeOffset> MomentSelected;
    internal event Action<DateTimeOffset,DateTimeOffset> RangeSelected;
    internal event Action<PerformanceFact[]> EventsSelected;
    internal event Action<DateTimeOffset> MomentHovered;
    internal event Action SelectionCleared;
    internal DateTimeOffset? From=>viewFrom??(points.Length==0?null:points[0].Time);
    internal DateTimeOffset? To=>viewTo??(points.Length==0?null:points[^1].Time);
    internal PerformanceTimeline()
    {
        Focusable=true;Cursor=Cursors.Cross;
        AutomationProperties.SetName(this,"Performance timeline. Left and right select moments; up and down select events; drag the overview to select a range.");
        ToolTip="Click a moment to investigate its recorded apps. Drag the overview below the graph to select a time range.";
    }
    internal void SetHistory((DateTimeOffset,double?)[] values,double maximum,double gap)
    {points=values;ceiling=maximum;gapSeconds=gap;InvalidateVisual();}
    internal void Set((DateTimeOffset,double?)[] values)=>SetHistory(values,0,15);
    internal void SetDrive(DrivePoint[] values){drive=values;InvalidateVisual();}
    internal void SetLive(PerformanceGraph source)
    {
        SetHistory(source.Points,source.Ceiling,source.GapSeconds);
        var markers=new List<PerformanceFact>();bool above=false;DateTimeOffset previous=default;
        foreach(var point in points)
        {
            if(previous!=default&&point.Time-previous>TimeSpan.FromSeconds(gapSeconds))markers.Add(new(point.Time,"Collection gap",null,null,null,1,"No observations in the preceding interval"));
            bool high=ceiling==100&&point.Value>=80;
            if(high&&!above)markers.Add(new(point.Time,"Usage spike",null,null,point.Value,1,"Live sampled usage crossed 80%; contemporaneous apps are not proof of cause"));
            above=high;previous=point.Time;
        }
        events=markers.ToArray();
    }
    internal void SetEvents(PerformanceFact[] values){events=values;InvalidateVisual();}
    internal void ClearSelection(){cursor=hover=selectionFrom=selectionTo=viewFrom=viewTo=null;moment=null;InvalidateVisual();}
    internal void SetMoment(PerformanceMoment value,string metric)
    {if((hover??cursor) is {} at&&value.Requested.ToUnixTimeSeconds()/10==at.ToUnixTimeSeconds()/10){moment=value;momentMetric=metric;InvalidateVisual();}}
#if DEBUG
    internal int DriveCountForVerification=>drive?.Length??0;
    internal void SelectMomentForVerification(DateTimeOffset time){ChooseMoment(time);}
    internal void SelectRangeForVerification(DateTimeOffset from,DateTimeOffset to){ChooseRange(from,to);}
    internal void SelectEventsForVerification(){var item=events.FirstOrDefault(e=>e.Time>=From&&e.Time<=To);if(item!=null){cursor=item.Time;EventsSelected?.Invoke([item]);InvalidateVisual();}}
    internal int EventCountForVerification=>events.Length;
    internal int PointCountForVerification=>points.Length;
    internal void HoverForVerification(DateTimeOffset time){hover=time;MomentHovered?.Invoke(time);}
    internal int MomentAppsForVerification=>moment?.Apps.Length??0;
#endif
    void ChooseMoment(DateTimeOffset time)
    {
        if(cursor.HasValue&&Math.Abs(X(cursor.Value)-X(time))<=6){cursor=null;SelectionCleared?.Invoke();}
        else{cursor=time;hover=null;MomentSelected?.Invoke(time);}InvalidateVisual();
    }
    void ChooseRange(DateTimeOffset from,DateTimeOffset to)
    {
        if(to<=from)return;
        if(to-from<TimeSpan.FromMinutes(1))to=from.AddMinutes(1);
        if(to>DateTimeOffset.UtcNow){var shift=to-DateTimeOffset.UtcNow;from-=shift;to-=shift;}
        selectionFrom=viewFrom=from;selectionTo=viewTo=to;cursor=hover=null;moment=null;
        RangeSelected?.Invoke(from,to);InvalidateVisual();
    }
    Rect Plot=>new(0,drive==null?34:58,Math.Max(0,ActualWidth-66),Math.Max(0,ActualHeight-(drive==null?119:143)));
    Rect Overview=>new(0,Math.Max(0,ActualHeight-36),Math.Max(0,ActualWidth-66),20);
    double Fraction(DateTimeOffset value)=>From is {} from&&To is {} to?Math.Clamp((value-from).TotalSeconds/Math.Max(1,(to-from).TotalSeconds),0,1):0;
    double X(DateTimeOffset time)=>Plot.Width*Fraction(time);
    double NavX(DateTimeOffset time)=>points.Length<2?0:Overview.Width*Math.Clamp((time-points[0].Time).TotalSeconds/Math.Max(1,(points[^1].Time-points[0].Time).TotalSeconds),0,1);
    DateTimeOffset NavTime(double x)=>points.Length<2?default:points[0].Time+TimeSpan.FromSeconds(Math.Clamp(x/Math.Max(1,Overview.Width),0,1)*(points[^1].Time-points[0].Time).TotalSeconds);
    DateTimeOffset TimeAt(double x)=>From is {} from&&To is {} to?from+TimeSpan.FromSeconds(Math.Clamp(x/Math.Max(1,Plot.Width),0,1)*(to-from).TotalSeconds):default;
    DateTimeOffset Nearest(double x)
    {
        // Donor History.Nearest: binary search on time-sorted observations.
        if(points.Length==0)return default;var time=TimeAt(x);int lo=0,hi=points.Length-1;
        while(lo<hi){int mid=(lo+hi)/2;if(points[mid].Time<time)lo=mid+1;else hi=mid;}
        return lo>0&&time-points[lo-1].Time<points[lo].Time-time?points[lo-1].Time:points[lo].Time;
    }
    Brush Ink=>TryFindResource("Palette.E8EEF6") as Brush??Brushes.White;
    Brush Muted=>TryFindResource("Muted") as Brush??Brushes.Gray;
    Brush Accent=>TryFindResource("Accent") as Brush??Brushes.DodgerBlue;
    void Text(DrawingContext dc,string text,Point where,Brush brush,double size=11,double? width=null)
    {
        var formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(1,Math.Min(width??double.MaxValue,ActualWidth-where.X)),MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis};
        dc.DrawText(formatted,where);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(ActualWidth<80||ActualHeight<115)return;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));var plot=Plot;groups.Clear();
        var throughput=drive==null?plot:new Rect(plot.X,plot.Y,plot.Width,plot.Height*.49);
        double max=ceiling>0?ceiling:points.Length==0?1:Math.Max(1,points.Max(p=>p.Value??0));
        for(int i=0;i<3;i++)
        {
            double y=throughput.Top+throughput.Height*i/2;dc.PushOpacity(.18);dc.DrawLine(new Pen(Muted,1),new(0,y),new(plot.Right,y));dc.Pop();
            Text(dc,ceiling==100?$"{max*(1-i/2d):0}%":MainWindow.Fmt((long)(max*(1-i/2d)))+"/s",new(plot.Right+6,y-8),Muted,10);
        }
        if(points.Length==0){Text(dc,"No observations in this range",new(8,plot.Top+12),Muted);return;}
        if(drive==null)DrawSeries(dc,plot,max,1.6);
        else DrawDrive(dc,plot,throughput,max);
        dc.PushOpacity(.55);DrawSeries(dc,Overview,max,1);dc.Pop();
        // Donor DrawLaunchBadges groups relative to the first badge so two
        // events straddling a pixel bucket cannot create overlapping capsules.
        var eventGroups=new List<List<PerformanceFact>>();
        foreach(var item in events.Where(e=>e.Time>=From&&e.Time<=To).OrderBy(e=>e.Time))
        {
            double BadgeX(DateTimeOffset time)=>Math.Clamp(X(time),14,Math.Max(14,plot.Right-14));
            if(eventGroups.Count==0||BadgeX(item.Time)-BadgeX(eventGroups[^1][0].Time)>32)eventGroups.Add(new());
            eventGroups[^1].Add(item);
        }
        foreach(var group in eventGroups)
        {
            var items=group.ToArray();double x=Math.Clamp(X(items[0].Time),14,Math.Max(14,plot.Right-14));
            var bounds=new Rect(x-13,1,26,24);groups.Add((bounds,items));
            dc.PushOpacity(.13);dc.DrawLine(new Pen(Accent,1),new(x,29),new(x,plot.Bottom));dc.DrawRoundedRectangle(Accent,null,bounds,10,10);dc.Pop();
            dc.DrawRoundedRectangle(null,new Pen(Accent,1),bounds,10,10);
            string count=items.Length.ToString();Text(dc,count,new(x-(count.Length>1?7:3.5),4),Ink,11);
        }
        DrawSelection(dc,plot,false);DrawSelection(dc,Overview,true);
        var time=hover??cursor;
        if(time is {} at)
        {
            double x=X(at);dc.DrawLine(new Pen(Accent,1),new(x,plot.Top),new(x,plot.Bottom));
            var sample=points.MinBy(p=>Math.Abs((p.Time-at).TotalSeconds));
            string value=Math.Abs((sample.Time-at).TotalSeconds)>gapSeconds?"No observation":sample.Value is {} number?(ceiling==100?$"{number:0.#}%":MainWindow.Fmt((long)number)+"/s"):"Unavailable";
            string label=$"{at.ToLocalTime():G} · {value}";
            Text(dc,label,new(Math.Min(Math.Max(0,x-100),Math.Max(0,plot.Width-245)),plot.Bottom+3),Ink);
            DrawCursorCard(dc,at,x,plot);
        }
        else Text(dc,plot.Width<400?"Click to pin · drag to zoom":"Click to pin · drag or scroll to zoom · right-drag to move",new(0,plot.Bottom+3),Muted,10);
        DrawAxis(dc,plot);
        string format=plot.Width<400?"HH:mm":"g";
        Text(dc,From.Value.ToLocalTime().ToString(format),new(0,ActualHeight-13),Muted,10);
        string end=To.Value.ToLocalTime().ToString(format);var endLabel=new FormattedText(end,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Muted,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Text(dc,end,new(Math.Max(0,plot.Right-endLabel.Width),ActualHeight-13),Muted,10);
        if(IsKeyboardFocused)dc.DrawRectangle(null,new Pen(Accent,1){DashStyle=DashStyles.Dot},new Rect(1,0,Math.Max(0,ActualWidth-2),Math.Max(0,ActualHeight-1)));
    }
    void DrawDrive(DrawingContext dc,Rect plot,Rect throughput,double max)
    {
        // Donor Disks: throughput and latency together. Both use the same
        // timeline, cursor, range/overview and peak-preserving bucket geometry.
        var write=TryFindResource("TransferWarn") as Brush??Brushes.DarkOrange;
        var latency=new Rect(plot.X,plot.Top+plot.Height*.67,plot.Width,plot.Height*.33);
        var shown=drive.Where(p=>p.Time>=From&&p.Time<=To).ToArray();
        double delay=Math.Max(1,shown.Select(p=>p.LatencyMs??0).DefaultIfEmpty().Max());
        var at=hover??cursor??To;
        var current=drive.Length==0?null:drive.MinBy(p=>Math.Abs((p.Time-at.Value).TotalSeconds));
        if(current!=null&&Math.Abs((current.Time-at.Value).TotalSeconds)>gapSeconds)current=null;
        string Rate(double? value)=>value.HasValue?MainWindow.Fmt((long)value.Value)+"/s":"—";
        Series(drive.Select(p=>(p.Time,p.Read)).ToArray(),throughput,max,Accent);
        Series(drive.Select(p=>(p.Time,p.Write)).ToArray(),throughput,max,write);
        double second=Math.Min(175,throughput.Width*.5),labelWidth=Math.Max(1,second-8);bool compact=throughput.Width<300;
        Text(dc,(compact?"R ":"Read ")+Rate(current?.Read),new(4,throughput.Top-20),Accent,11,labelWidth);
        Text(dc,(compact?"W ":"Write ")+Rate(current?.Write),new(second,throughput.Top-20),write,11,throughput.Width-second);
        Text(dc,(compact?"":"Latency ")+(current?.LatencyMs is {} ms?$"{ms:0.##} ms":"—"),new(4,latency.Top-22),Ink,11,labelWidth);
        Text(dc,current?.Busy is {} busy?$"{busy:0.#}% active":"Active time —",new(second,latency.Top-22),Muted,11,latency.Width-second);
        for(int i=0;i<3;i++)
        {
            double y=latency.Top+latency.Height*i/2;dc.PushOpacity(.18);dc.DrawLine(new Pen(Muted,1),new(0,y),new(latency.Right,y));dc.Pop();
            Text(dc,$"{delay*(1-i/2d):0.##} ms",new(latency.Right+6,y-8),Muted,10);
        }
        Series(drive.Select(p=>(p.Time,p.LatencyMs)).ToArray(),latency,delay,Accent);
        void Series((DateTimeOffset Time,double? Value)[] values,Rect rect,double maximum,Brush ink)
        {
            var cols=PerformanceHistoryChart.MakeColumns(From.Value.UtcDateTime,Math.Max(1,(To.Value-From.Value).TotalSeconds),rect.Width,Math.Max(1,(int)Math.Min(rect.Width/4,Math.Max(1,(To.Value-From.Value).TotalSeconds/5))));
            var peaks=PerformanceHistoryChart.Bucket(values,cols);
            // Only bridge observed cadence; explicit missing fields/collection
            // gaps remain blank (old history does not contain latency).
            PerformanceHistoryChart.BridgeShortGaps(peaks,(int)(gapSeconds/cols.Seconds));
            DateTimeOffset previous=default;
            foreach(var p in values)
            {
                int b=(int)((p.Time.UtcDateTime-cols.Origin).TotalSeconds/cols.Seconds);
                if(p.Value==null&&b>=0&&b<peaks.Length)peaks[b]=float.NaN;
                if(previous!=default&&(p.Time-previous).TotalSeconds>gapSeconds)
                {
                    int start=Math.Max(0,(int)((previous.UtcDateTime-cols.Origin).TotalSeconds/cols.Seconds)+1);
                    for(int n=start;n<Math.Min(b,peaks.Length);n++)peaks[n]=float.NaN;
                }
                previous=p.Time;
            }
            double Y(double number)=>rect.Bottom-Math.Clamp(number/maximum,0,1)*rect.Height;
            dc.PushClip(new RectangleGeometry(rect));dc.PushOpacity(.1);dc.DrawGeometry(ink,null,PerformanceHistoryChart.SmoothRuns(peaks,cols,Y,rect.Bottom));dc.Pop();
            dc.DrawGeometry(null,new Pen(ink,1.6),PerformanceHistoryChart.SmoothRuns(peaks,cols,Y,null));dc.Pop();
        }
    }
    void DrawAxis(DrawingContext dc,Rect plot)
    {
        // Donor round-duration time axis, with density fitted to Pathweft's pane.
        double span=Math.Max(1,(To.Value-From.Value).TotalSeconds);
        int[] steps={10,15,30,60,120,300,600,900,1800,3600,7200,10800,21600,43200,86400,172800};
        int step=steps.FirstOrDefault(s=>span/s<=Math.Max(2,plot.Width/100),604800);
        var from=From.Value.ToLocalTime();var to=To.Value.ToLocalTime();
        var tick=from.Date.AddSeconds(Math.Ceiling((from-from.Date).TotalSeconds/step)*step);
        for(;tick<=to.DateTime;tick=tick.AddSeconds(step))
        {
            double x=plot.Width*(tick-from.DateTime).TotalSeconds/span;
            string text=step>=86400||tick.TimeOfDay==TimeSpan.Zero?tick.ToString("ddd d"):tick.ToString(step<60?"HH:mm:ss":"HH:mm");
            Text(dc,text,new(Math.Clamp(x-23,0,Math.Max(0,plot.Width-55)),plot.Bottom+22),Muted,10);
        }
    }
    void DrawSelection(DrawingContext dc,Rect rect,bool handles)
    {
        if(selectionFrom is not {} from||selectionTo is not {} to)return;
        double left=handles?NavX(from):X(from),right=handles?NavX(to):X(to);var region=new Rect(left,rect.Top,Math.Max(1,right-left),rect.Height);
        dc.PushOpacity(.12);dc.DrawRectangle(Accent,null,region);dc.Pop();
        dc.DrawRectangle(null,new Pen(Accent,1),region);
        if(handles){dc.DrawRectangle(Accent,null,new Rect(left,rect.Top,3,rect.Height));dc.DrawRectangle(Accent,null,new Rect(right-3,rect.Top,3,rect.Height));}
    }
    void DrawSeries(DrawingContext dc,Rect rect,double max,double thickness)
    {
        if(points.Length==0)return;
        bool navigator=rect==Overview;
        var from=navigator?points[0].Time:From.Value;var to=navigator?points[^1].Time:To.Value;
        double span=Math.Max(1,(to-from).TotalSeconds);
        var cols=PerformanceHistoryChart.MakeColumns(from.UtcDateTime,span,rect.Width,Math.Max(1,(int)Math.Min(rect.Width/4,span/5)));
        var peaks=PerformanceHistoryChart.Bucket(points,cols);
        PerformanceHistoryChart.BridgeShortGaps(peaks,(int)(gapSeconds/cols.Seconds));
        // The donor interpolates short missing intervals. Explicit unavailable
        // samples in Pathweft stay breaks even when they share a wide time column.
        foreach(var point in points.Where(p=>p.Value==null))
        {
            int bucket=(int)((point.Time.UtcDateTime-cols.Origin).TotalSeconds/cols.Seconds);
            if(bucket>=0&&bucket<peaks.Length)peaks[bucket]=float.NaN;
        }
        DateTimeOffset previous=default;
        foreach(var point in points)
        {
            if(previous!=default&&point.Time>from&&previous<to&&(point.Time-previous).TotalSeconds>gapSeconds)
            {
                int first=Math.Clamp((int)((previous.UtcDateTime-cols.Origin).TotalSeconds/cols.Seconds)+1,0,peaks.Length-1);
                int last=Math.Clamp((int)((point.Time.UtcDateTime-cols.Origin).TotalSeconds/cols.Seconds),first,peaks.Length-1);
                for(int b=first;b<=last;b++)peaks[b]=float.NaN;
            }
            previous=point.Time;
        }
        Color accent=(Accent as SolidColorBrush)?.Color??Colors.DodgerBlue;
        Color warm=ceiling==100?Color.FromRgb(230,155,45):accent,hot=ceiling==100?Color.FromRgb(225,78,69):accent;
        LinearGradientBrush Heat(byte alpha)=>new(new GradientStopCollection{
            new(Color.FromArgb(alpha,hot.R,hot.G,hot.B),0),new(Color.FromArgb(alpha,warm.R,warm.G,warm.B),.3),
            new(Color.FromArgb(alpha,accent.R,accent.G,accent.B),.5),new(Color.FromArgb(alpha,accent.R,accent.G,accent.B),1)},new(0,rect.Top),new(0,rect.Bottom)){MappingMode=BrushMappingMode.Absolute};
        double Y(double value)=>rect.Bottom-Math.Clamp(value/max,0,1)*rect.Height;
        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawGeometry(Heat(35),null,PerformanceHistoryChart.SmoothRuns(peaks,cols,Y,rect.Bottom));
        dc.DrawGeometry(null,new Pen(Heat(255),thickness),PerformanceHistoryChart.SmoothRuns(peaks,cols,Y,null));
        dc.Pop();
    }
    // Donor's cursor card: top five observed apps beside the curve, flipped at
    // the right edge. Pathweft leaves unrecorded temperature and residual shares out.
    void DrawCursorCard(DrawingContext dc,DateTimeOffset at,double x,Rect plot)
    {
        if(moment==null||moment.Requested.ToUnixTimeSeconds()/10!=at.ToUnixTimeSeconds()/10)return;
        double width=Math.Min(265,plot.Width-16);if(width<145||plot.Height<70)return;
        var lines=moment.Apps.Take(5).Select(a=>$"{a.Name}    "+(a.Average is not {} value?"—":momentMetric=="ram"?MainWindow.Fmt((long)value):$"{value:0.#}%")).ToArray();
        if(lines.Length==0)lines=["No app observations near this moment"];
        double height=Math.Min(plot.Height-8,38+lines.Length*18);
        double left=x+14+width>plot.Right?Math.Max(4,x-14-width):x+14;
        var bounds=new Rect(left,plot.Top+5,width,height);
        var back=TryFindResource("Palette.20242A") as Brush??Brushes.Black;
        dc.DrawRoundedRectangle(back,new Pen(Muted,.7),bounds,7,7);
        dc.PushClip(new RectangleGeometry(new Rect(left+9,bounds.Top+7,width-18,height-14)));
        Text(dc,moment.Observed is {} seen?"Observed "+seen.ToLocalTime().ToString("T"):"No process snapshot",new(left+10,bounds.Top+8),Ink);
        for(int i=0;i<lines.Length;i++)Text(dc,lines[i],new(left+10,bounds.Top+29+i*18),Muted);
        dc.Pop();
    }
    NavDrag HitTestSelection(double x)
    {
        if(selectionFrom==null||selectionTo==null)return NavDrag.None;
        double left=NavX(selectionFrom.Value),right=NavX(selectionTo.Value),grip=Math.Min(EdgeGrip,(right-left)/4);
        if(Math.Abs(x-left)<=grip)return NavDrag.Left;
        if(Math.Abs(x-right)<=grip)return NavDrag.Right;
        return x>left&&x<right?NavDrag.Move:NavDrag.None;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(points.Length==0)return;var position=e.GetPosition(this);
        if(pan is {} p&&e.RightButton==MouseButtonState.Pressed)
        {
            var shift=TimeSpan.FromSeconds(-(position.X-p.X)/Math.Max(1,Plot.Width)*(p.To-p.From).TotalSeconds);
            if(p.To+shift>DateTimeOffset.UtcNow)shift=DateTimeOffset.UtcNow-p.To;
            viewFrom=p.From+shift;viewTo=p.To+shift;InvalidateVisual();return;
        }
        if(pressed is {} start&&e.LeftButton==MouseButtonState.Pressed)
        {
            if(overviewDrag)
            {
                double left=selectionFrom.HasValue?NavX(selectionFrom.Value):start.X,right=selectionTo.HasValue?NavX(selectionTo.Value):start.X;
                switch(navDrag)
                {
                    case NavDrag.Move: double width=right-left;left=Math.Clamp(position.X-navGrab,0,Math.Max(0,Overview.Width-width));right=left+width;break;
                    case NavDrag.Left:left=Math.Clamp(position.X,0,Math.Max(0,right-MinSelection));break;
                    case NavDrag.Right:right=Math.Clamp(position.X,Math.Min(Overview.Width,left+MinSelection),Overview.Width);break;
                    default:left=Math.Min(start.X,position.X);right=Math.Max(start.X,position.X);break;
                }
                selectionFrom=NavTime(left);selectionTo=NavTime(right);
            }
            else if(Math.Abs(start.X-position.X)>DragThreshold){selectionFrom=TimeAt(Math.Min(start.X,position.X));selectionTo=TimeAt(Math.Max(start.X,position.X));}
            InvalidateVisual();return;
        }
        if(Plot.Contains(position)&&cursor==null)
        {
            var time=TimeAt(position.X);
            // Donor cursor-card column reuse: only query a newly reached 10s
            // snapshot slot, while the cursor itself follows the exact pointer.
            var previous=hover;hover=time;
            if(previous==null||previous.Value.ToUnixTimeSeconds()/10!=time.ToUnixTimeSeconds()/10)MomentHovered?.Invoke(time);
            InvalidateVisual();
        }
        else if(hover!=null){hover=null;InvalidateVisual();}
        var group=groups.FirstOrDefault(g=>g.Bounds.Contains(position));
        ToolTip=group.Events==null?"Click a moment to investigate recorded apps. Drag the overview to select a range.":string.Join("\n",group.Events.Take(8).Select(x=>$"{x.Time.ToLocalTime():T} · {x.Name}"))+(group.Events.Length>8?$"\n{group.Events.Length:N0} events in this group":"");
    }
    protected override void OnMouseLeave(MouseEventArgs e){base.OnMouseLeave(e);hover=null;InvalidateVisual();}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);Focus();if(points.Length==0)return;
        var position=e.GetPosition(this);var group=groups.FirstOrDefault(g=>g.Bounds.Contains(position));
        if(group.Events!=null){cursor=group.Events[0].Time;EventsSelected?.Invoke(group.Events);InvalidateVisual();e.Handled=true;return;}
        if(e.ClickCount>=2&&Plot.Contains(position))
        {
            double seconds=Math.Min(3600,Math.Max(60,(To.Value-From.Value).TotalSeconds/2));
            var at=TimeAt(position.X);ChooseRange(at.AddSeconds(-seconds/2),at.AddSeconds(seconds/2));e.Handled=true;return;
        }
        if(!Plot.Contains(position)&&!Overview.Contains(position))return;
        pressed=position;overviewDrag=Overview.Contains(position);navDrag=overviewDrag?HitTestSelection(position.X):NavDrag.None;
        if(navDrag==NavDrag.Move)navGrab=position.X-NavX(selectionFrom.Value);
        CaptureMouse();e.Handled=true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);if(pressed is not {} start)return;
        var end=e.GetPosition(this);pressed=null;ReleaseMouseCapture();
        if(Math.Abs(start.X-end.X)>DragThreshold&&selectionFrom is {} from&&selectionTo is {} to&&to>from)ChooseRange(from,to);
        else{selectionFrom=selectionTo=null;ChooseMoment(TimeAt(end.X));}
        InvalidateVisual();e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if(pan is {} previous){viewFrom=previous.From;viewTo=previous.To;}
        if(pressed!=null){selectionFrom=viewFrom;selectionTo=viewTo;}
        pressed=null;pan=null;navDrag=NavDrag.None;InvalidateVisual();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {base.OnMouseRightButtonDown(e);if(From.HasValue&&To.HasValue){pan=(e.GetPosition(this).X,From.Value,To.Value);CaptureMouse();e.Handled=true;}}
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);if(pan==null)return;
        bool moved=Math.Abs(e.GetPosition(this).X-pan.Value.X)>DragThreshold;pan=null;ReleaseMouseCapture();
        if(moved)ChooseRange(From.Value,To.Value);else SelectionCleared?.Invoke();e.Handled=true;
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);if(points.Length<2||pressed!=null)return;
        double ratio=Math.Clamp(e.GetPosition(this).X/Math.Max(1,Plot.Width),0,1);
        double seconds=Math.Clamp((To.Value-From.Value).TotalSeconds*Math.Pow(.8,e.Delta/120d),60,TimeSpan.FromDays(30).TotalSeconds);
        var at=TimeAt(e.GetPosition(this).X);ChooseRange(at.AddSeconds(-seconds*ratio),at.AddSeconds(seconds*(1-ratio)));e.Handled=true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(points.Length==0)return;
        if(e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            var visible=points.Where(p=>p.Time>=From&&p.Time<=To).ToArray();if(visible.Length==0)return;
            int index=Array.FindIndex(visible,p=>p.Time>=cursor.GetValueOrDefault(visible[0].Time));if(index<0)index=visible.Length-1;
            index=e.Key switch{Key.Home=>0,Key.End=>visible.Length-1,Key.Left=>Math.Max(0,index-1),_=>Math.Min(visible.Length-1,index+1)};
            hover=null;ChooseMoment(visible[index].Time);e.Handled=true;
        }
        if(e.Key is Key.Up or Key.Down&&events.Length>0)
        {
            var current=cursor??From.Value;var visible=events.Where(x=>x.Time>=From&&x.Time<=To);var candidates=e.Key==Key.Down?visible.Where(x=>x.Time>current):visible.Reverse().Where(x=>x.Time<current);
            var item=candidates.FirstOrDefault();if(item!=null){cursor=item.Time;EventsSelected?.Invoke([item]);InvalidateVisual();}e.Handled=true;
        }
        if(e.Key==Key.Escape){SelectionCleared?.Invoke();e.Handled=true;}
    }
}
