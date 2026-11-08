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
    public string When=>Time==default?"":Time.ToLocalTime().ToString("G");
    public string Value=>Average?.ToString("0.##",CultureInfo.CurrentCulture)??"—";
    public string Maximum=>Peak?.ToString("0.##",CultureInfo.CurrentCulture)??"—";
}
internal sealed record PerformancePoint(DateTimeOffset Time,double? Value,double? Peak=null);
internal sealed record PerformanceResult(string Status,PerformanceFact[] Rows,PerformancePoint[] Points,long Total,bool Truncated);
internal sealed record PerformanceQuery(string Tool="get_usage_summary",int LastMinutes=60,string Metric="cpu",string Device=null,string Name=null,double Threshold=80,int Limit=100,DateTimeOffset? From=null,DateTimeOffset? To=null);

internal sealed class PerformanceHistory : IDisposable
{
    internal static string FilePath=>Path.Combine(DataFolder.Bulk,"Performance","history.sqlite");
    readonly string file;
    internal PerformanceHistory(string file=null){this.file=file;}
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
    }
    internal void Record(PerformanceSample sample,ProcessMetric[] apps,ProcessMetric[] gpuApps)
    {
        try
        {
            if(!Settings.PerformanceRecording)
            {
                if(recording&&writer!=null)Execute(writer,"INSERT INTO events VALUES($0,'Recording stopped','New observations disabled')",sample.Time.ToUnixTimeMilliseconds());
                recording=false;writer?.Dispose();writer=null;return;
            }
            EnsureWriter();
            if(!recording){Execute(writer,"INSERT INTO events VALUES($0,'Recording started','Aggregate samples every 5s; heavy apps every 10s; inaccessible and short-lived processes may be absent')",sample.Time.ToUnixTimeMilliseconds());recording=true;lastWrite=default;}
            if(sample.Time-lastWrite<TimeSpan.FromSeconds(5)&&apps==null)return;
            using var transaction=writer.BeginTransaction();
            activeTransaction=transaction;
            if(lastWrite!=default&&sample.Time-lastWrite>TimeSpan.FromSeconds(15))Execute(writer,"INSERT INTO events VALUES($0,'Collection gap',$1)",sample.Time.ToUnixTimeMilliseconds(),$"No observations since {lastWrite:O}");
            if(sample.Issue!=previousIssue){Execute(writer,"INSERT INTO events VALUES($0,$1,$2)",sample.Time.ToUnixTimeMilliseconds(),sample.Issue==null?"Counters restored":"Counters unavailable",sample.Issue??"Aggregate counters available");previousIssue=sample.Issue;}
            Execute(writer,"INSERT OR REPLACE INTO samples VALUES($0,$1)",sample.Time.ToUnixTimeMilliseconds(),JsonSerializer.Serialize(sample));
            if(apps!=null)
            {
                var gpuByPid=(gpuApps??Array.Empty<ProcessMetric>()).GroupBy(x=>(x.Pid,x.Started)).ToDictionary(g=>g.Key,g=>g.Max(x=>x.Usage));
                var selected=apps.OrderByDescending(x=>x.Usage).Take(15).Concat(apps.OrderByDescending(x=>x.Memory).Take(15))
                    .Concat(apps.OrderByDescending(x=>gpuByPid.GetValueOrDefault((x.Pid,x.Started))).Take(15)).DistinctBy(x=>(x.Pid,x.Started)).ToArray();
                foreach(var app in selected)Execute(writer,"INSERT OR REPLACE INTO apps VALUES($0,$1,$2,$3,$4,$5)",sample.Time.ToUnixTimeMilliseconds(),app.Pid,app.Started,app.Process,app.Usage,app.Memory);
                var identities=selected.Select(x=>(x.Pid,x.Started)).ToHashSet();
                foreach(var app in gpuApps??[])if(app.Adapter!=null&&identities.Contains((app.Pid,app.Started)))Execute(writer,"INSERT OR REPLACE INTO gpu_apps VALUES($0,$1,$2,$3,$4,$5)",sample.Time.ToUnixTimeMilliseconds(),app.Pid,app.Started,app.Process,app.Adapter,app.Usage);
            }
            int days=Settings.PerformanceRetention;
            if(sample.Time-lastPrune>=TimeSpan.FromHours(1)||retainedDays!=days)
            {
                long cutoff=sample.Time.AddDays(-days).ToUnixTimeMilliseconds();
                Execute(writer,"DELETE FROM samples WHERE t<$0; DELETE FROM apps WHERE t<$0; DELETE FROM gpu_apps WHERE t<$0; DELETE FROM events WHERE t<$0;",cutoff);
                lastPrune=sample.Time;retainedDays=days;
            }
            transaction.Commit();activeTransaction=null;lastWrite=sample.Time;Issue=null;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or SqliteException)
        {Issue="History recording unavailable: "+ex.Message;activeTransaction=null;writer?.Dispose();writer=null;recording=false;}
    }
    internal static void Validate(PerformanceQuery query)
    {
        if(!new[]{"get_current_usage","get_usage_summary","get_top_processes","find_spikes","get_app_history","get_events"}.Contains(query.Tool))throw new ArgumentException("Unknown read-only performance tool");
        if(query.LastMinutes<1||query.LastMinutes>43200||query.Limit<1||query.Limit>500||!double.IsFinite(query.Threshold)||query.Name?.Length>260||query.Device?.Length>260)throw new ArgumentException("Invalid range, limit, threshold or name");
        if(!new[]{"cpu","ram","gpu","disk"}.Contains(query.Metric))throw new ArgumentException("Metric must be cpu, ram, gpu or disk");
        if(query.Tool=="get_app_history"&&string.IsNullOrWhiteSpace(query.Name))throw new ArgumentException("App name is required");
        if(query.From.HasValue!=query.To.HasValue||query.From>=query.To||query.To-query.From>TimeSpan.FromDays(30))throw new ArgumentException("Use an ordered from/to range of at most 30 days");
    }
    internal static PerformanceResult Query(PerformanceQuery query,CancellationToken cancel=default,string source=null,PerformanceSample[] live=null)
    {
        Validate(query);string path=source??FilePath;
        bool recorded=File.Exists(path);
        if(!recorded&&live==null)return new("No recorded history. Enable recording in System analysis; the live rolling hour is separate.",[],[],0,false);
        using var db=recorded?Open(path,true):null;using var command=db?.CreateCommand();
        var to=query.To??DateTimeOffset.UtcNow;var from=query.From??to.AddMinutes(-query.LastMinutes);
        command?.Parameters.AddWithValue("$from",from.ToUnixTimeMilliseconds());command?.Parameters.AddWithValue("$to",to.ToUnixTimeMilliseconds());
        var rows=new List<PerformanceFact>();var points=new List<PerformancePoint>();long total=0;
        string note=live==null?"Recorded observations only; missing intervals are not zero. ":"Available live and recorded observations; missing intervals are not zero. ";
        if(command==null&&query.Tool is "get_top_processes" or "get_app_history")return new("No retained process snapshots for this interval. Aggregate observations remain available; live process details show current activity.",[],[],0,false);
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
            string grouping=$" FROM {(gpu?"gpu_apps":"apps")} INDEXED BY {(gpu?"gpu_apps_identity_time":"apps_identity_time")} WHERE t BETWEEN $from AND $to AND ($name='' OR instr(lower(name),lower($name))>0)"+(gpu?" AND ($device='' OR adapter=$device)":"")+$" GROUP BY name,pid,start{(gpu?",adapter":"")}";
            command.Parameters.AddWithValue("$name",query.Tool=="get_app_history"?query.Name:"");
            if(gpu)command.Parameters.AddWithValue("$device",query.Device??"");
            command.CommandText="SELECT COUNT(*) FROM (SELECT 1"+grouping+")";
            total=Convert.ToInt64(command.ExecuteScalar());cancel.ThrowIfCancellationRequested();
            command.Parameters.AddWithValue("$limit",query.Limit);
            command.CommandText=$"SELECT name,pid,start,AVG({column}),MAX({column}),COUNT({column}),MIN(t),MAX(t)"+(gpu?",adapter":"")+grouping+$" ORDER BY AVG({column}) DESC LIMIT $limit";
            using var reader=command.ExecuteReader();while(reader.Read())
            {
                cancel.ThrowIfCancellationRequested();
                rows.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),reader.GetString(0),reader.GetInt32(1),reader.IsDBNull(3)?null:reader.GetDouble(3),reader.IsDBNull(4)?null:reader.GetDouble(4),reader.GetInt64(5),$"Started {new DateTime(reader.GetInt64(2),DateTimeKind.Utc):O}; observed from {DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)):O}; {column} {(column=="memory"?"bytes":"%")}"+(gpu?" · "+reader.GetString(8):"")));
            }
            note+="Selected heavy apps every 10s; inaccessible, small and short-lived processes may be absent. PID/start distinguishes reuse; observations are not exact launch/exit sessions.";
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
        return new(total==0?"No matching recorded observations. "+note:note,rows.ToArray(),points.ToArray(),total,total>rows.Count);
    }
    public void Dispose()
    {
        try{if(recording&&writer!=null)Execute(writer,"INSERT INTO events VALUES($0,'Collector stopped','Pathweft exited; no observations while stopped')",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());}
        catch(Exception ex)when(ex is SqliteException or IOException){Issue=ex.Message;}
        finally{writer?.Dispose();writer=null;}
    }
}
