using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

// Query vocabulary and heavy-app policy adapted from SysOptimizer 6ddd104,
// Services/HistoryStore.cs and Sysoptimizer.Mcp/Tools.cs (MIT). Pathweft owns
// storage, counter/device identities and lifecycle; no donor app is required.
internal sealed record PerformanceFact(DateTimeOffset Time,string Name,int? Pid,double? Average,double? Peak,long Observations,string Details)
{
    public long Started {get;init;}
    public string When=>Time==default?"":Time.ToLocalTime().ToString("G");
    public string Value=>Average?.ToString("0.##",CultureInfo.CurrentCulture)??"—";
    public string Maximum=>Peak?.ToString("0.##",CultureInfo.CurrentCulture)??"—";
}
internal sealed record PerformancePoint(DateTimeOffset Time,double? Value,double? Peak=null);
internal sealed record PerformanceResult(string Status,PerformanceFact[] Rows,PerformancePoint[] Points,long Total,bool Truncated);
internal sealed record PerformanceQuery(string Tool="get_usage_summary",int LastMinutes=60,string Metric="cpu",string Device=null,string Name=null,double Threshold=80,int Limit=100,DateTimeOffset? From=null,DateTimeOffset? To=null);

internal sealed partial class PerformanceHistory : IDisposable
{
    internal static string FilePath=>Path.Combine(DataFolder.Bulk,"Performance","history.sqlite");
    internal const long StorageLimit=256L*1024*1024;
    internal const long WalLimit=8L*1024*1024;
    readonly string file;
    readonly long storageLimit;
    internal PerformanceHistory(string file=null,long storageLimit=StorageLimit){this.file=file;this.storageLimit=storageLimit;}
    string StoragePath=>file??FilePath;
    SqliteConnection writer;SqliteTransaction activeTransaction;DateTimeOffset lastWrite,lastPrune;int retainedDays;bool recording;string previousIssue;
    internal string Issue {get;private set;}
    static SqliteConnection Open(string path,bool readOnly)
    {
        SqliteRuntime.Ensure();
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());
        connection.Open();
        using(var setup=connection.CreateCommand()){setup.CommandText=readOnly?"PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;":"PRAGMA temp_store=MEMORY;";setup.ExecuteNonQuery();}
        return connection;
    }
    void Execute(SqliteConnection db,string sql,params object[] parameters)
    {
        using var command=db.CreateCommand();command.CommandText=sql;command.Transaction=activeTransaction;
        for(int i=0;i<parameters.Length;i++)command.Parameters.AddWithValue("$"+i,parameters[i]??DBNull.Value);
        command.ExecuteNonQuery();
    }
    void EnsureWriter()
    {
        if(writer!=null)return;Directory.CreateDirectory(Path.GetDirectoryName(StoragePath));writer=Open(StoragePath,false);
        Execute(writer,"PRAGMA auto_vacuum=INCREMENTAL; PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS samples(t INTEGER PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS apps(t INTEGER NOT NULL,pid INTEGER NOT NULL,start INTEGER NOT NULL,name TEXT NOT NULL,cpu REAL,memory INTEGER,PRIMARY KEY(t,pid,start)); CREATE INDEX IF NOT EXISTS apps_name_time ON apps(name,t); CREATE TABLE IF NOT EXISTS gpu_apps(t INTEGER NOT NULL,pid INTEGER NOT NULL,start INTEGER NOT NULL,name TEXT NOT NULL,adapter TEXT NOT NULL,usage REAL,PRIMARY KEY(t,pid,start,adapter)); CREATE INDEX IF NOT EXISTS gpu_apps_name_time ON gpu_apps(name,t); CREATE TABLE IF NOT EXISTS events(t INTEGER NOT NULL,kind TEXT NOT NULL,detail TEXT NOT NULL); CREATE INDEX IF NOT EXISTS events_time ON events(t);");
        Execute(writer,"CREATE INDEX IF NOT EXISTS apps_identity_time ON apps(name,pid,start,t); CREATE INDEX IF NOT EXISTS gpu_apps_identity_time ON gpu_apps(name,pid,start,adapter,t);");
        long pageSize=Scalar("PRAGMA page_size");
        Execute(writer,$"PRAGMA max_page_count={storageLimit/pageSize}; PRAGMA journal_size_limit=1048576; PRAGMA wal_autocheckpoint=256; PRAGMA busy_timeout=0;");
    }
    long Scalar(string sql)
    {using var command=writer.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
    bool Checkpoint()
    {
        // Do not grow an unbounded WAL behind a long-lived reader. PASSIVE is
        // non-blocking; TRUNCATE below uses SQLite's explicit busy response.
        Execute(writer,"PRAGMA wal_checkpoint(PASSIVE);");
        if(new FileInfo(StoragePath+"-wal") is {Exists:true,Length:>=WalLimit})
        {
            Execute(writer,"PRAGMA wal_checkpoint(TRUNCATE);");
            if(new FileInfo(StoragePath+"-wal") is {Exists:true,Length:>=WalLimit})
            {Issue="History saving is waiting for an active reader; recent observations remain available.";return false;}
        }
        return true;
    }
    bool Prune(DateTimeOffset time)
    {
        if(!Checkpoint())return false;
        long pageSize=Scalar("PRAGMA page_size");
        int days=Math.Clamp(Settings.PerformanceRetention,1,30);
        bool expire=time-lastPrune>=TimeSpan.FromHours(1)||retainedDays!=days;
        long cutoff=time.AddDays(-days).ToUnixTimeMilliseconds();
        while(true)
        {
            bool pressure=(Scalar("PRAGMA page_count")-Scalar("PRAGMA freelist_count"))*pageSize>=storageLimit*3/4;
            if(!expire&&!pressure)break;
            // Small chronological batches bound individual transactions and
            // their WAL growth. Retention and capacity remove the oldest facts
            // together; no full database rewrite or vacuum copy is required.
            using var next=writer.CreateCommand();
            next.CommandText="SELECT MAX(t) FROM (SELECT t FROM samples"+(pressure?"":" WHERE t<$cutoff")+" ORDER BY t LIMIT 60)";
            next.Parameters.AddWithValue("$cutoff",cutoff);
            var value=next.ExecuteScalar();if(value is null or DBNull)break;
            using(var transaction=writer.BeginTransaction())
            {
                activeTransaction=transaction;
                Execute(writer,"DELETE FROM samples WHERE t<=$0; DELETE FROM apps WHERE t<=$0; DELETE FROM gpu_apps WHERE t<=$0; DELETE FROM events WHERE t<=$0;",value);
                transaction.Commit();activeTransaction=null;
            }
            if(!Checkpoint())return false;
        }
        if(expire)
        {
            // Lifecycle events can occur between sample timestamps.
            Execute(writer,"DELETE FROM events WHERE t<$0",cutoff);
            lastPrune=time;retainedDays=days;
        }
        // Existing history may predate the capacity ceiling. It was created
        // with incremental auto-vacuum; reclaim free tail pages in small steps
        // rather than copying/replacing the database or dropping recent facts.
        long pages=Scalar("PRAGMA page_count"),maximum=storageLimit/pageSize;
        while(pages>maximum&&Scalar("PRAGMA freelist_count")>0)
        {
            Execute(writer,"PRAGMA incremental_vacuum(128);");
            long reduced=Scalar("PRAGMA page_count");
            if(reduced>=pages){Issue="History cannot reclaim its existing free pages.";return false;}
            pages=reduced;if(!Checkpoint())return false;
        }
        Scalar($"PRAGMA max_page_count={maximum}");
        return true;
    }
    internal bool Record(PerformanceSample sample,ProcessMetric[] apps,ProcessMetric[] gpuApps,PerformanceFact[] changes=null)
    {
        try
        {
            EnsureWriter();
            if(sample.Time-lastWrite<TimeSpan.FromSeconds(5)&&apps==null&&changes is not {Length:>0})return false;
            if(!Prune(sample.Time))return false;
            if(!recording){Execute(writer,"INSERT INTO events VALUES($0,'Recording started','Aggregate samples every 5s; heavy apps every 10s; inaccessible and short-lived processes may be absent')",sample.Time.ToUnixTimeMilliseconds());recording=true;lastWrite=default;}
            using var transaction=writer.BeginTransaction();
            activeTransaction=transaction;
            foreach(var change in changes??[])Execute(writer,"INSERT INTO events VALUES($0,$1,$2)",change.Time.ToUnixTimeMilliseconds(),change.Name,change.Details);
            if(lastWrite!=default&&sample.Time-lastWrite>TimeSpan.FromSeconds(15))Execute(writer,"INSERT INTO events VALUES($0,'Collection gap',$1)",sample.Time.ToUnixTimeMilliseconds(),$"No observations since {lastWrite:O}");
            if(sample.Issue!=previousIssue){Execute(writer,"INSERT INTO events VALUES($0,$1,$2)",sample.Time.ToUnixTimeMilliseconds(),sample.Issue==null?"Counters restored":"Counters unavailable",sample.Issue??"Aggregate counters available");previousIssue=sample.Issue;}
            Execute(writer,"INSERT OR REPLACE INTO samples VALUES($0,$1)",sample.Time.ToUnixTimeMilliseconds(),JsonSerializer.Serialize(sample));
            if(apps!=null)
            {
                var selected=SelectApps(apps,gpuApps);
                foreach(var app in selected)Execute(writer,"INSERT OR REPLACE INTO apps VALUES($0,$1,$2,$3,$4,$5)",sample.Time.ToUnixTimeMilliseconds(),app.Pid,app.Started,app.Process,app.Usage,app.Memory);
                var identities=selected.Select(x=>(x.Pid,x.Started)).ToHashSet();
                foreach(var app in gpuApps??[])if(app.Adapter!=null&&identities.Contains((app.Pid,app.Started)))Execute(writer,"INSERT OR REPLACE INTO gpu_apps VALUES($0,$1,$2,$3,$4,$5)",sample.Time.ToUnixTimeMilliseconds(),app.Pid,app.Started,app.Process,app.Adapter,app.Usage);
            }
            transaction.Commit();activeTransaction=null;lastWrite=sample.Time;Issue=null;return true;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or SqliteException)
        {Issue="History recording unavailable: "+ex.Message;activeTransaction=null;writer?.Dispose();writer=null;recording=false;return false;}
    }
    internal static ProcessMetric[] SelectApps(ProcessMetric[] apps,ProcessMetric[] gpuApps)
    {
        apps??=[];
        var gpuByPid=(gpuApps??[]).GroupBy(x=>(x.Pid,x.Started)).ToDictionary(g=>g.Key,g=>g.Max(x=>x.Usage));
        return apps.OrderByDescending(x=>x.Usage).Take(15).Concat(apps.OrderByDescending(x=>x.Memory).Take(15))
            .Concat(apps.OrderByDescending(x=>gpuByPid.GetValueOrDefault((x.Pid,x.Started))).Take(15)).DistinctBy(x=>(x.Pid,x.Started)).ToArray();
    }
    internal static void Validate(PerformanceQuery query)
    {
        if(!new[]{"get_current_usage","get_usage_summary","get_top_processes","find_spikes","get_app_history","get_events"}.Contains(query.Tool))throw new ArgumentException("Unknown read-only performance tool");
        if(query.LastMinutes<1||query.LastMinutes>43200||query.Limit<1||query.Limit>500||!double.IsFinite(query.Threshold)||query.Name?.Length>260||query.Device?.Length>260)throw new ArgumentException("Invalid range, limit, threshold or name");
        if(!new[]{"cpu","ram","gpu","disk"}.Contains(query.Metric))throw new ArgumentException("Metric must be cpu, ram, gpu or disk");
        if(query.Tool=="get_app_history"&&string.IsNullOrWhiteSpace(query.Name))throw new ArgumentException("App name is required");
        if(query.From.HasValue!=query.To.HasValue||query.From>=query.To||query.To-query.From>TimeSpan.FromDays(30))throw new ArgumentException("Use an ordered from/to range of at most 30 days");
    }
    internal static PerformanceResult Query(PerformanceQuery query,CancellationToken cancel=default,string source=null,PerformanceSample[] live=null,PerformanceProcessSnapshot[] liveApps=null)
    {
        Validate(query);cancel.ThrowIfCancellationRequested();string path=source??FilePath;
        bool recorded=File.Exists(path);
        if(!recorded&&live==null&&liveApps==null)return new("No saved observations yet. Pathweft collects and saves history automatically while running.",[],[],0,false);
        using var db=recorded?Open(path,true):null;using var command=db?.CreateCommand();
        var to=query.To??DateTimeOffset.UtcNow;var from=query.From??to.AddMinutes(-query.LastMinutes);
        command?.Parameters.AddWithValue("$from",from.ToUnixTimeMilliseconds());command?.Parameters.AddWithValue("$to",to.ToUnixTimeMilliseconds());
        var rows=new List<PerformanceFact>();var points=new List<PerformancePoint>();long total=0;
        string note=live==null&&liveApps==null?"Saved observations only; missing intervals are not zero. ":"Available recent and saved observations; missing intervals are not zero. ";
        IEnumerable<PerformanceSample> Samples()
        {
            var liveStart=live is {Length:>0}?live[0].Time:DateTimeOffset.MaxValue;
            if(command!=null)
            {
                command.CommandText=query.Tool=="get_current_usage"?"SELECT data FROM samples ORDER BY t DESC LIMIT 1":"SELECT data FROM samples WHERE t BETWEEN $from AND $to ORDER BY t";
                using var reader=command.ExecuteReader();
                while(reader.Read())
                {
                    cancel.ThrowIfCancellationRequested();var sample=JsonSerializer.Deserialize<PerformanceSample>(reader.GetString(0));
                    if(sample.Time<liveStart)yield return sample;
                }
            }
            foreach(var sample in live??[]){cancel.ThrowIfCancellationRequested();if(sample.Time>=from&&sample.Time<=to)yield return sample;}
        }
        if(query.Tool is "get_top_processes" or "get_app_history")
        {
            string column=query.Metric switch{"ram"=>"memory","gpu"=>"usage",_=>"cpu"};bool gpu=query.Metric=="gpu";
            if(query.Metric=="disk")throw new ArgumentException("Historical process metrics are cpu, ram or gpu; no background file I/O log is recorded");
            // Stream identity groups from the matching index. Only the bounded
            // top results are sorted, while a separate count covers all matches.
            string name=query.Tool=="get_app_history"?query.Name:"";
            var recent=new Dictionary<(string,int,long,string),(double Sum,double Peak,long Count,DateTimeOffset First,DateTimeOffset Last)>();
            foreach(var snapshot in liveApps??[])
            {
                cancel.ThrowIfCancellationRequested();
                if(snapshot.Time<from||snapshot.Time>to)continue;
                foreach(var app in gpu?snapshot.GpuApps:snapshot.Apps)
                {
                    if(!app.Process.Contains(name,StringComparison.OrdinalIgnoreCase)||(gpu&&query.Device!=null&&app.Adapter!=query.Device))continue;
                    var key=(app.Process,app.Pid,app.Started,gpu?app.Adapter:"");
                    double value=query.Metric=="ram"?app.Memory:app.Usage;
                    if(recent.TryGetValue(key,out var old))recent[key]=(old.Sum+value,Math.Max(old.Peak,value),old.Count+1,old.First,snapshot.Time);
                    else recent[key]=(value,value,1,snapshot.Time,snapshot.Time);
                }
            }
            void Add((string Name,int Pid,long Start,string Adapter) key,double sum,double peak,long count,DateTimeOffset first,DateTimeOffset last)
            {
                total++;
                rows.Add(new(last,key.Name,key.Pid,count==0?null:sum/count,count==0?null:peak,count,$"Started {new DateTime(key.Start,DateTimeKind.Utc):O}; observed from {first:O}; {column} {(column=="memory"?"bytes":"%")}"+(gpu?" · "+key.Adapter:"")){Started=key.Start});
                // Stream all saved groups, retaining only the requested top
                // results plus bounded identities present in the recent hour.
                rows.Sort((a,b)=>{int order=Nullable.Compare(b.Average,a.Average);if(order!=0)return order;order=string.CompareOrdinal(a.Name,b.Name);return order!=0?order:Nullable.Compare(a.Pid,b.Pid);});
                if(rows.Count>query.Limit)rows.RemoveAt(rows.Count-1);
            }
            if(command!=null)
            {
                command.Parameters.AddWithValue("$name",name);
                command.Parameters.AddWithValue("$live",liveApps is {Length:>0}?liveApps[0].Time.ToUnixTimeMilliseconds():long.MaxValue);
                if(gpu)command.Parameters.AddWithValue("$device",query.Device??"");
                string grouping=$" FROM {(gpu?"gpu_apps":"apps")} INDEXED BY {(gpu?"gpu_apps_identity_time":"apps_identity_time")} WHERE t BETWEEN $from AND $to AND t<$live AND ($name='' OR instr(lower(name),lower($name))>0)"+(gpu?" AND ($device='' OR adapter=$device)":"")+$" GROUP BY name,pid,start{(gpu?",adapter":"")}";
                command.CommandText=$"SELECT name,pid,start,SUM({column}),MAX({column}),COUNT({column}),MIN(t),MAX(t)"+(gpu?",adapter":"")+grouping;
                using var reader=command.ExecuteReader();while(reader.Read())
                {
                    cancel.ThrowIfCancellationRequested();
                    var key=(reader.GetString(0),reader.GetInt32(1),reader.GetInt64(2),gpu?reader.GetString(8):"");
                    double sum=reader.IsDBNull(3)?0:reader.GetDouble(3),peak=reader.IsDBNull(4)?0:reader.GetDouble(4);long count=reader.GetInt64(5);
                    var first=DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6));var last=DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7));
                    if(recent.Remove(key,out var item)){sum+=item.Sum;peak=Math.Max(peak,item.Peak);count+=item.Count;last=item.Last;}
                    Add(key,sum,peak,count,first,last);
                }
            }
            foreach(var (key,item) in recent){cancel.ThrowIfCancellationRequested();Add(key,item.Sum,item.Peak,item.Count,item.First,item.Last);}
            note+="Automatic recent app snapshots every 10s; selected heavy apps only, so inaccessible, small and short-lived processes may be absent. PID/start distinguishes reuse; observations are not exact launch/exit sessions.";
        }
        else if(query.Tool=="get_events")
        {
            if(command!=null)
            {
                command.CommandText="SELECT t,kind,detail FROM events WHERE t BETWEEN $from AND $to ORDER BY t DESC";
                using var reader=command.ExecuteReader();while(reader.Read()){cancel.ThrowIfCancellationRequested();total++;if(rows.Count<query.Limit)rows.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),reader.GetString(1),null,null,null,1,reader.GetString(2)));}
            }
            PerformanceSample previous=null;
            foreach(var sample in live??[])
            {
                cancel.ThrowIfCancellationRequested();
                if(sample.Time>=from&&sample.Time<=to)
                {
                    void Event(string name,string detail){if(rows.Any(x=>x.Time==sample.Time&&x.Name==name))return;total++;if(rows.Count<query.Limit)rows.Add(new(sample.Time,name,null,null,null,1,detail));}
                    if(previous!=null&&sample.Time-previous.Time>TimeSpan.FromSeconds(5))Event("Collection gap","No live observations in the preceding interval");
                    if(previous!=null&&previous.Issue!=sample.Issue)Event(sample.Issue==null?"Counters restored":"Counters unavailable",sample.Issue??"Live counters available");
                }
                previous=sample;
            }
        }
        else
        {
            var sums=new Dictionary<string,(double Sum,double Weight,double Peak,DateTimeOffset PeakAt,long Count)>();
            var buckets=new Dictionary<long,(DateTimeOffset Time,double Sum,int Count,double Peak)>();var breaks=new HashSet<long>();
            DateTimeOffset first=default,latest=default,previous=default,spikeStart=default;double spikeMax=0;long spikeCount=0;string spikeDevice=null;
            void EndSpike(){if(spikeCount==0)return;total++;if(rows.Count<query.Limit)rows.Add(new(spikeStart,spikeDevice,null,null,spikeMax,spikeCount,"Threshold exceedances at sampled times; coincident apps are not proof of cause"));spikeCount=0;}
            foreach(var sample in Samples())
            {
                cancel.ThrowIfCancellationRequested();
                if(first==default)first=sample.Time;latest=sample.Time;
                bool liveSample=live is {Length:>0}&&previous>=live[0].Time;
                bool gap=previous!=default&&sample.Time-previous>TimeSpan.FromSeconds(liveSample?5:15);
                double weight=previous==default||gap?1:Math.Min(5,Math.Max(0,(sample.Time-previous).TotalSeconds));
                IEnumerable<(string Name,double? Value)> metrics=query.Tool=="get_current_usage"?
                    new[]{("CPU %",sample.Cpu),("Physical RAM %",sample.Ram?.Usage)}.Concat(sample.Gpus.Select(x=>("GPU % · "+x.Name+" · "+x.Id,x.Usage))).Concat(sample.Disks.Select(x=>("Drive bytes/s · "+x.Id,x.Read+x.Write))):
                    query.Metric switch
                    {
                        "ram"=>[("Physical RAM %",sample.Ram?.Usage)],
                        "gpu"=>sample.Gpus.Where(x=>query.Device==null||x.Id==query.Device).Select(x=>("GPU % · "+x.Name+" · "+x.Id,x.Usage)),
                        "disk"=>sample.Disks.Where(x=>query.Device==null||x.Id==query.Device).Select(x=>("Drive bytes/s · "+x.Id,x.Read+x.Write)),
                        _=>[("CPU %",sample.Cpu)]
                    };
                var values=metrics.ToArray();foreach(var (name,value) in values)
                {
                    var old=sums.GetValueOrDefault(name,(0d,0d,double.MinValue,default,0L));
                    if(value is not {} number){sums.TryAdd(name,old);continue;}
                    sums[name]=(old.Item1+number*weight,old.Item2+weight,Math.Max(old.Item3,number),number>old.Item3?sample.Time:old.Item4,old.Item5+1);
                }
                // Never sum adapters; the plot is the first named selected device.
                var plotted=values.FirstOrDefault();long bucket=(long)((sample.Time-from).TotalSeconds/Math.Max(1,(to-from).TotalSeconds/600));
                if(gap){EndSpike();breaks.Add(bucket);}
                if(plotted.Value is {} v)
                {
                    var old=buckets.GetValueOrDefault(bucket);buckets[bucket]=(sample.Time,old.Sum+v,old.Count+1,old.Count==0?v:Math.Max(old.Peak,v));
                    if(query.Tool=="find_spikes"){if(v>=query.Threshold){if(spikeCount==0){spikeStart=sample.Time;spikeDevice=plotted.Name;spikeMax=v;}spikeCount++;spikeMax=Math.Max(spikeMax,v);}else EndSpike();}
                }
                else {EndSpike();breaks.Add(bucket);buckets.TryAdd(bucket,(sample.Time,0,0,0));}
                previous=sample.Time;
            }
            EndSpike();foreach(var bucket in buckets.OrderBy(x=>x.Key))
            {
                if(breaks.Contains(bucket.Key))points.Add(new(bucket.Value.Time.AddMilliseconds(-1),null));
                points.Add(new(bucket.Value.Time,bucket.Value.Count==0?null:bucket.Value.Sum/bucket.Value.Count,bucket.Value.Count==0?null:bucket.Value.Peak));
            }
            if(query.Tool!="find_spikes")foreach(var entry in sums){total++;if(rows.Count<query.Limit)rows.Add(new(entry.Value.PeakAt,entry.Key,null,entry.Value.Weight==0?null:entry.Value.Sum/entry.Value.Weight,entry.Value.Count==0?null:entry.Value.Peak,entry.Value.Count,entry.Value.Count==0?"Counter unavailable":"Average over observed intervals; peak time shown"));}
            note+=first==default?"No samples in range.":$"Observed {first:O} to {latest:O}. Latest age {(DateTimeOffset.UtcNow-latest).TotalSeconds:0}s. Interval averages and original-sample peaks; live cadence 1s, recorded cadence 5s.";
        }
        return new(total==0?"No matching observations. "+note:note,rows.ToArray(),points.ToArray(),total,total>rows.Count);
    }
    public void Dispose()
    {
        try{if(recording&&writer!=null)Execute(writer,"INSERT INTO events VALUES($0,'Collector stopped','Pathweft exited; no observations while stopped')",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());}
        catch(Exception ex)when(ex is SqliteException or IOException){Issue=ex.Message;}
        finally{writer?.Dispose();writer=null;}
    }
}
