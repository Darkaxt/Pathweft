using System.IO;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

internal sealed record PerformanceTimelineData(PerformanceResult Activity,PerformanceFact[] Events,long EventCount);
internal sealed record PerformanceMoment(DateTimeOffset Requested,DateTimeOffset? Observed,PerformanceFact[] Apps,string Status);

// Read-only investigation over the automatic recent hour and saved history.
internal static class PerformanceInvestigation
{
    internal static PerformanceTimelineData Read(PerformanceQuery query,CancellationToken cancel=default,string source=null,PerformanceSample[] live=null,PerformanceProcessSnapshot[] liveApps=null)
    {
        var activity=PerformanceHistory.Query(query with{Tool="get_usage_summary"},cancel,source,live,liveApps);
        var events=PerformanceHistory.Query(query with{Tool="get_events"},cancel,source,live,liveApps);
        var spikes=PerformanceHistory.Query(query with{Tool="find_spikes"},cancel,source,live,liveApps);
        // Markers sit at a spike's peak and name the apps recorded there (MONITOR-22).
        var spikeRows=spikes.Rows.Select((x,i)=>x with{Time=x.PeakAt==default?x.Time:x.PeakAt,Name="Usage spike",
            Details=i<20?Attribute(query,x,cancel,source,liveApps):x.Name+" · "+x.Details}).ToArray();
        var growth=query.Metric=="ram"?Growth(query.From??DateTimeOffset.UtcNow.AddMinutes(-query.LastMinutes),query.To??DateTimeOffset.UtcNow,InstalledMemory,cancel,source,liveApps):[];
        var markers=events.Rows.Concat(spikeRows).Concat(growth).OrderBy(x=>x.Time).Take(query.Limit).ToArray();
        return new(activity,markers,events.Total+spikes.Total+growth.Length);
    }
    internal static long InstalledMemory=>GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    // Names the app with the largest share at the peak and, for RAM, the one that grew most since the spike began.
    static string Attribute(PerformanceQuery query,PerformanceFact spike,CancellationToken cancel,string source,PerformanceProcessSnapshot[] liveApps)
    {
        if(query.Metric=="disk"||spike.PeakAt==default)return spike.Name+" · "+spike.Details;
        string head=$"{spike.Name.Split(" · ")[0].TrimEnd(' ','%')} {spike.Peak:0.#}%";
        var peak=Moment(query,spike.PeakAt,cancel,source,liveApps).Apps;
        if(peak.Length==0)return head+" · no app snapshot near the peak";
        var top=peak[0];string text=head+" · largest "+top.Name+" "+(query.Metric=="ram"?MainWindow.Fmt((long)(top.Average??0)):$"{top.Average:0.#}%");
        if(query.Metric=="ram"&&spike.Time<spike.PeakAt)
        {
            var before=Moment(query,spike.Time,cancel,source,liveApps).Apps.ToDictionary(x=>(x.Name,x.Pid),x=>x.Average??0);
            var grower=peak.Select(x=>(x.Name,Gain:(x.Average??0)-before.GetValueOrDefault((x.Name,x.Pid),0))).MaxBy(x=>x.Gain);
            if(grower.Gain>=256L<<20)text+=$" · {grower.Name} +{MainWindow.Fmt((long)grower.Gain)} since {spike.Time.ToLocalTime():t}";
        }
        return text+" · sampled; not proof of cause";
    }
    // Sustained working-set growth of one process identity: at least 15% of installed memory (min 2 GB)
    // gained over at least 8 minutes at 0.25 GB/min or more. Working-set trims of up to a quarter of the
    // gain and stalls under 5 minutes stay inside one interval; plateaus before growth are excluded.
    internal static PerformanceFact[] Growth(DateTimeOffset from,DateTimeOffset to,long installed,CancellationToken cancel=default,string source=null,PerformanceProcessSnapshot[] liveApps=null)
    {
        const long Gb=1L<<30;long minGain=Math.Max(2*Gb,installed*15/100);
        var series=new Dictionary<(string Name,int Pid,long Start),List<(long T,long M)>>();
        long live=liveApps is {Length:>0}?liveApps[0].Time.ToUnixTimeMilliseconds():long.MaxValue;
        string path=source??PerformanceHistory.FilePath;
        if(File.Exists(path))
        {
            SqliteRuntime.Ensure();using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
            using var command=db.CreateCommand();command.CommandText="PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";command.ExecuteNonQuery();
            command.Parameters.AddWithValue("$from",from.ToUnixTimeMilliseconds());command.Parameters.AddWithValue("$to",to.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$live",live);command.Parameters.AddWithValue("$gain",minGain);
            command.CommandText="SELECT a.name,a.pid,a.start,a.t,a.memory FROM apps a JOIN (SELECT name,pid,start FROM apps WHERE t BETWEEN $from AND $to AND t<$live GROUP BY name,pid,start HAVING MAX(memory)-MIN(memory)>=$gain) c "+
                "ON a.name=c.name AND a.pid=c.pid AND a.start=c.start WHERE a.t BETWEEN $from AND $to AND a.t<$live AND a.memory IS NOT NULL ORDER BY a.name,a.pid,a.start,a.t";
            using var reader=command.ExecuteReader();
            while(reader.Read())
            {
                cancel.ThrowIfCancellationRequested();var key=(reader.GetString(0),reader.GetInt32(1),reader.GetInt64(2));
                if(!series.TryGetValue(key,out var list))series[key]=list=[];list.Add((reader.GetInt64(3),reader.GetInt64(4)));
            }
        }
        foreach(var snapshot in liveApps??[])
        {
            cancel.ThrowIfCancellationRequested();if(snapshot.Time<from||snapshot.Time>to)continue;
            foreach(var app in snapshot.Apps)
            {
                var key=(app.Process,app.Pid,app.Started);
                if(!series.TryGetValue(key,out var list))series[key]=list=[];list.Add((snapshot.Time.ToUnixTimeMilliseconds(),app.Memory));
            }
        }
        var found=new List<PerformanceFact>();
        foreach(var (key,list) in series)
        {
            if(list.Count<2||list.Max(x=>x.M)-list.Min(x=>x.M)<minGain)continue;
            list.Sort();(long T,long M) start=default,begin=default,high=default;long previous=0;int count=0;bool open=false;
            void Close()
            {
                if(!open)return;open=false;long span=high.T-begin.T,gain=high.M-begin.M;
                if(span<8*60_000||gain<minGain||gain/(span/60_000d)<Gb/4)return;
                double rate=gain/(span/60_000d);
                found.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(begin.T),"Memory growth",key.Pid,null,null,count,
                    $"{key.Name} · growing steadily ~{MainWindow.Fmt((long)rate)}/min for {span/60_000d:0} min · {MainWindow.Fmt(begin.M)} → {MainWindow.Fmt(high.M)} working set"){Started=key.Start,PeakAt=DateTimeOffset.FromUnixTimeMilliseconds(high.T)});
            }
            foreach(var (t,m) in list)
            {
                if(open&&(t-previous>30_000||m<high.M-Math.Max(256L<<20,(high.M-begin.M)/4)||t-high.T>5*60_000))Close();
                if(!open||m<start.M&&high.M<=start.M+(256L<<20)){start=begin=high=(t,m);count=0;open=true;}
                if(m>high.M)high=(t,m);
                // Growth begins at the last sample still level with the run's low point.
                if(m<=start.M+(256L<<20)){begin=(t,m);count=0;}
                previous=t;count++;
            }
            Close();
        }
        return found.ToArray();
    }
    internal static PerformanceMoment Moment(PerformanceQuery query,DateTimeOffset time,CancellationToken cancel=default,string source=null,PerformanceProcessSnapshot[] liveApps=null)
    {
        PerformanceHistory.Validate(query);
        cancel.ThrowIfCancellationRequested();
        if(query.Metric=="disk")return new(time,null,[],"Per-process drive I/O is available in live drive details; it is not retained in the background.");
        PerformanceMoment recent=null;
        foreach(var snapshot in liveApps??[])
        {
            cancel.ThrowIfCancellationRequested();
            if(Math.Abs((snapshot.Time-time).TotalSeconds)>10)continue;
            var apps=(query.Metric=="gpu"?snapshot.GpuApps.Where(x=>query.Device==null||x.Adapter==query.Device):snapshot.Apps).ToArray();
            if(apps.Length==0||recent!=null&&Math.Abs((recent.Observed.Value-time).Ticks)<=Math.Abs((snapshot.Time-time).Ticks))continue;
            recent=new(time,snapshot.Time,apps.OrderByDescending(x=>query.Metric=="ram"?x.Memory:x.Usage).Select(x=>new PerformanceFact(snapshot.Time,x.Process,x.Pid,query.Metric=="ram"?x.Memory:x.Usage,null,1,$"Started {new DateTime(x.Started,DateTimeKind.Utc):O}"+(query.Metric=="gpu"?" · "+x.Adapter:" · "+MainWindow.Fmt(x.Memory)+" working set"))).ToArray(),$"Observed {snapshot.Time.ToLocalTime():G} · automatic recent snapshot; selected heavy apps, not proof of cause.");
        }
        string path=source??PerformanceHistory.FilePath;
        if(!File.Exists(path))return recent??new(time,null,[],"No process observations near this moment. Recent snapshots are collected automatically while Pathweft runs; missing intervals are not zero activity.");
        SqliteRuntime.Ensure();using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";command.ExecuteNonQuery();
        long at=time.ToUnixTimeMilliseconds();
        command.Parameters.AddWithValue("$from",at-10000);command.Parameters.AddWithValue("$to",at+10000);command.Parameters.AddWithValue("$at",at);
        command.Parameters.AddWithValue("$live",liveApps is {Length:>0}?liveApps[0].Time.ToUnixTimeMilliseconds():long.MaxValue);
        string table=query.Metric=="gpu"?"gpu_apps":"apps";
        command.Parameters.AddWithValue("$device",query.Device??"");
        string device=query.Metric=="gpu"?" AND ($device='' OR adapter=$device)":"";
        command.CommandText=$"SELECT t FROM {table} WHERE t BETWEEN $from AND $to AND t<$live{device} ORDER BY abs(t-$at),t LIMIT 1";
        cancel.ThrowIfCancellationRequested();var nearest=command.ExecuteScalar();
        if(nearest==null)return recent??new(time,null,[],"No process snapshot within 10 seconds of this moment. Missing observations are not zero activity.");
        var observed=DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(nearest));
        if(recent!=null&&Math.Abs((recent.Observed.Value-time).Ticks)<=Math.Abs((observed-time).Ticks))return recent;
        command.Parameters.AddWithValue("$sample",observed.ToUnixTimeMilliseconds());
        string value=query.Metric switch{"ram"=>"memory","gpu"=>"usage",_=>"cpu"};
        command.CommandText=$"SELECT name,pid,start,{value}"+(query.Metric=="gpu"?",NULL":",memory")+$" FROM {table} WHERE t=$sample{device} ORDER BY {value} DESC LIMIT 45";
        using var reader=command.ExecuteReader();var rows=new List<PerformanceFact>();
        while(reader.Read())
        {
            cancel.ThrowIfCancellationRequested();
            rows.Add(new(observed,reader.GetString(0),reader.GetInt32(1),reader.IsDBNull(3)?null:reader.GetDouble(3),null,1,
                $"Started {new DateTime(reader.GetInt64(2),DateTimeKind.Utc):O}"+(reader.IsDBNull(4)?"":" · "+MainWindow.Fmt(reader.GetInt64(4))+" working set")));
        }
        return new(time,observed,rows.ToArray(),$"Observed {observed.ToLocalTime():G} · selected heavy apps; incomplete coverage, not proof of cause.");
    }
}
