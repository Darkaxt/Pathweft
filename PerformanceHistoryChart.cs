using System.Windows;
using System.Windows.Media;

namespace FileExplorer;

// Selected chart code from SysOptimizer MainWindow.History.cs, MIT,
// 6ddd104fe4fc3f990bc2a9b376e5795943541d49, blob f7d7bb61782bbf5e029f6252c5cd07d6633a3c90.
// Original retained under vendor/SysOptimizer/upstream. Adapted sample/time types;
// graph shape, absolute column grid and non-overshooting rounded steps are retained.
internal static class PerformanceHistoryChart
{
    internal readonly record struct Columns(DateTime Origin,double Seconds,double Step,double Offset,int Count)
    {
        internal double Center(int k)=>Offset+(k+0.5)*Step;
        internal double Left(int k)=>Offset+k*Step;
    }
    static readonly int[] ColumnSeconds={1,2,3,5,10,15,20,30,60,120,180,300,600,900,1200,1800,3600,7200,10800,21600,43200,86400};
    internal static Columns MakeColumns(DateTime fromUtc,double span,double w,int buckets)
    {
        double raw=span/buckets;
        double seconds=raw<1?raw:ColumnSeconds.FirstOrDefault(c=>c>=raw,(int)Math.Ceiling(raw));
        buckets=(int)Math.Ceiling(span/seconds);
        long ticks=Math.Max(1,(long)(seconds*TimeSpan.TicksPerSecond));
        var origin=new DateTime(fromUtc.Ticks-fromUtc.Ticks%ticks,DateTimeKind.Utc);
        return new(origin,seconds,seconds/span*w,(origin-fromUtc).TotalSeconds/span*w,buckets+2);
    }
    internal static StreamGeometry SmoothRuns(float[] values,Columns cols,Func<double,double> y,double? baseline)
    {
        var geometry=new StreamGeometry();int n=values.Length;
        double r=Math.Min(cols.Step*0.35,4);
        using(var ctx=geometry.Open())
        {
            int i=0;
            while(i<n)
            {
                if(float.IsNaN(values[i])){i++;continue;}
                int j=i;while(j<n&&!float.IsNaN(values[j]))j++;
                var start=new Point(cols.Left(i),y(values[i]));
                if(baseline is double b){ctx.BeginFigure(new Point(start.X,b),true,true);ctx.LineTo(start,false,true);}
                else ctx.BeginFigure(start,false,false);
                for(int k=i;k<j-1;k++)
                {
                    double edge=cols.Left(k+1),y0=y(values[k]),y1=y(values[k+1]);
                    ctx.LineTo(new Point(edge-r,y0),true,true);
                    ctx.BezierTo(new Point(edge,y0),new Point(edge,y1),new Point(edge+r,y1),true,true);
                }
                ctx.LineTo(new Point(cols.Left(j),y(values[j-1])),true,true);
                if(baseline is double b2)ctx.LineTo(new Point(cols.Left(j),b2),false,true);
                i=j;
            }
        }
        geometry.Freeze();return geometry;
    }
    internal static void BridgeShortGaps(float[] values,int maxGap)
    {
        int i=0;
        while(i<values.Length)
        {
            if(!float.IsNaN(values[i])){i++;continue;}
            int j=i;while(j<values.Length&&float.IsNaN(values[j]))j++;
            if(i>0&&j<values.Length&&j-i<=maxGap)
                for(int k=i;k<j;k++)values[k]=values[i-1]+(values[j]-values[i-1])*(k-i+1)/(j-i+1);
            i=j;
        }
    }
    internal static float[] Bucket((DateTimeOffset Time,double? Value)[] samples,Columns cols)
    {
        var peaks=new float[cols.Count];Array.Fill(peaks,float.NaN);
        long origin=cols.Origin.Ticks;double ticksPerColumn=cols.Seconds*TimeSpan.TicksPerSecond;
        foreach(var s in samples)
        {
            long offset=s.Time.UtcTicks-origin;if(offset<0)continue;
            int b=(int)(offset/ticksPerColumn);if(b>=cols.Count)continue;
            float v=(float)(s.Value??double.NaN);if(float.IsNaN(v))continue;
            peaks[b]=float.IsNaN(peaks[b])?v:Math.Max(peaks[b],v);
        }
        return peaks;
    }
}
