using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

// ACTIVITY-05: read-only queries over the recorder's store. Never writes,
// never starts the recorder, never elevates.
internal static class ActivityQuery
{
    internal static readonly string[] Tools=["get_run_history","get_workloads","get_top_consumers","get_app_usage","get_activity_intervals"];
    internal sealed record Request(string Tool,DateTimeOffset From,DateTimeOffset To,string Name,string Metric,int Limit);

    internal static Request Parse(string tool,JsonElement arguments)
    {
        if(!Tools.Contains(tool)||arguments.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Unknown tool or invalid arguments");
        var allowed=new[]{"last_minutes","from","to","name","metric","limit"};
        if(arguments.EnumerateObject().Any(x=>!allowed.Contains(x.Name)))throw new ArgumentException("Unknown argument");
        string Text(string key)=>arguments.TryGetProperty(key,out var value)?value.GetString():null;
        int Integer(string key,int fallback)=>arguments.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        DateTimeOffset? Date(string key)
        {
            string value=Text(key);if(value==null)return null;
            if(!Regex.IsMatch(value,@"(Z|[+-]\d\d:\d\d)$")||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new ArgumentException("Use ISO timestamps with UTC Z or an explicit offset");return date;
        }
        int minutes=Integer("last_minutes",1440),limit=Integer("limit",50);string name=Text("name"),metric=Text("metric")??"cpu";
        var from=Date("from");var to=Date("to");
        if(minutes<1||minutes>43200||limit<1||limit>500||name?.Length>260)throw new ArgumentException("Invalid range, limit or name");
        if(!new[]{"cpu","io","memory","starts"}.Contains(metric))throw new ArgumentException("Metric must be cpu, io, memory or starts");
        if(from.HasValue!=to.HasValue||from>=to||to-from>TimeSpan.FromDays(30))throw new ArgumentException("Use an ordered from/to range of at most 30 days");
        if(tool=="get_app_usage"&&string.IsNullOrWhiteSpace(name))throw new ArgumentException("Executable name or path is required");
        var end=to??DateTimeOffset.UtcNow;
        return new(tool,from??end.AddMinutes(-minutes),end,string.IsNullOrWhiteSpace(name)?null:name.Trim(),metric,limit);
    }

    internal static object Query(Request request,string source=null)
    {
        string path=source??ActivityRecorder.FilePath;
        if(!File.Exists(path))return new{status="App activity recording is off: no recorder store exists. Turn on Settings > Startup > Record app activity (Program Files installation).",rows=Array.Empty<object>(),truncated=false};
        using var db=ActivityRecorder.Open(path,true);
        var meta=new Dictionary<string,object>();
        using(var command=db.CreateCommand()){command.CommandText="SELECT key,value FROM meta";using var reader=command.ExecuteReader();while(reader.Read())meta[reader.GetString(0)]=reader.GetValue(1);}
        double mhz=meta.TryGetValue("tsc_mhz",out var m)?Convert.ToDouble(m,CultureInfo.InvariantCulture):1000;
        long heartbeat=meta.TryGetValue("heartbeat",out var h)?Convert.ToInt64(h):0;
        long from=request.From.ToUnixTimeMilliseconds(),to=request.To.ToUnixTimeMilliseconds();
        double Seconds(long cycles)=>Math.Round(cycles/(mhz*1e6),3);
        static string Time(long ms)=>DateTimeOffset.FromUnixTimeMilliseconds(ms).ToString("O",CultureInfo.InvariantCulture);
        string like=request.Name==null?null:"%"+request.Name.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        var rows=new List<object>();bool truncated=false;
        void Read(string sql,Func<SqliteDataReader,object> map)
        {
            using var command=db.CreateCommand();command.CommandText=sql;
            command.Parameters.AddWithValue("$from",from);command.Parameters.AddWithValue("$to",to);
            command.Parameters.AddWithValue("$name",(object)like??DBNull.Value);command.Parameters.AddWithValue("$limit",request.Limit+1);
            using var reader=command.ExecuteReader();
            while(reader.Read()){if(rows.Count==request.Limit){truncated=true;break;}rows.Add(map(reader));}
        }
        const string NameFilter="($name IS NULL OR path LIKE $name ESCAPE '\\')";
        switch(request.Tool)
        {
            case "get_run_history":
                Read($"SELECT p.path,p.pid,p.parent,p.start,p.exit,p.cycles,p.read,p.write,p.peak,p.code,p.workload,w.root FROM processes p LEFT JOIN workloads w ON w.id=p.workload WHERE p.exit>=$from AND p.start<=$to AND {NameFilter.Replace("path","p.path")} ORDER BY p.start DESC LIMIT $limit",
                    r=>new{path=r.GetString(0),pid=r.GetInt32(1),parent_pid=r.GetInt32(2),start=Time(r.GetInt64(3)),exit=Time(r.GetInt64(4)),duration_seconds=(r.GetInt64(4)-r.GetInt64(3))/1000.0,
                        cpu_seconds=Seconds(r.GetInt64(5)),read_bytes=r.GetInt64(6),write_bytes=r.GetInt64(7),peak_private_bytes=r.GetInt64(8),exit_code=r.IsDBNull(9)?(int?)null:r.GetInt32(9),workload=r.GetInt64(10),workload_root=r.IsDBNull(11)?null:r.GetString(11)});
                break;
            case "get_workloads":
                Read($"SELECT id,root,start,end,processes,tiny,cycles,read,write,peak FROM workloads WHERE IFNULL(end,$to)>=$from AND start<=$to AND {NameFilter.Replace("path","root")} ORDER BY "+Order("cycles","read+write","peak","processes")+" DESC LIMIT $limit",
                    r=>new{workload=r.GetInt64(0),root=r.GetString(1),start=Time(r.GetInt64(2)),end=r.IsDBNull(3)?null:Time(r.GetInt64(3)),running=r.IsDBNull(3),processes=r.GetInt32(4),short_processes_not_listed=r.GetInt32(5),
                        cpu_seconds=Seconds(r.GetInt64(6)),read_bytes=r.GetInt64(7),write_bytes=r.GetInt64(8),largest_process_peak_private_bytes=r.GetInt64(9)});
                break;
            case "get_top_consumers":
                Read($"SELECT path,SUM(starts),SUM(cycles),SUM(read),SUM(write),MAX(peak),MIN(t),MAX(t) FROM app_usage WHERE t>=$from-{ActivityRecorder.BucketMs} AND t<=$to AND {NameFilter} GROUP BY path ORDER BY "+Order("SUM(cycles)","SUM(read)+SUM(write)","MAX(peak)","SUM(starts)")+" DESC LIMIT $limit",
                    r=>new{path=r.GetString(0),starts=r.GetInt64(1),cpu_seconds=Seconds(r.GetInt64(2)),read_bytes=r.GetInt64(3),write_bytes=r.GetInt64(4),peak_private_bytes=r.GetInt64(5),first_bucket=Time(r.GetInt64(6)),last_bucket=Time(r.GetInt64(7))});
                break;
            case "get_app_usage":
                Read($"SELECT t,path,starts,cycles,read,write,peak FROM app_usage WHERE t>=$from-{ActivityRecorder.BucketMs} AND t<=$to AND {NameFilter} ORDER BY t DESC,path LIMIT $limit",
                    r=>new{bucket_start=Time(r.GetInt64(0)),path=r.GetString(1),starts=r.GetInt32(2),cpu_seconds=Seconds(r.GetInt64(3)),read_bytes=r.GetInt64(4),write_bytes=r.GetInt64(5),peak_private_bytes=r.GetInt64(6)});
                break;
            default:
                long previous=0;
                Read("SELECT start,end,state,lost FROM intervals WHERE end>=$from AND start<=$to ORDER BY start LIMIT $limit",r=>
                {
                    long start=r.GetInt64(0),end=r.GetInt64(1);
                    // A gap of more than 90 s between recorded intervals means the recorder or the PC was off.
                    var gap=previous>0&&start-previous>90_000?new{off_from=Time(previous),off_to=Time(start)}:null;previous=end;
                    return new{start=Time(start),end=Time(end),state=r.GetString(2),lost_events=r.GetInt64(3),off_before=gap};
                });
                break;
        }
        long age=heartbeat==0?-1:(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-heartbeat)/1000;
        string status=heartbeat==0?"The recorder has not written yet.":age>120?$"Recorder not running: last heartbeat {Time(heartbeat)}; later activity is absent.":"Recording.";
        return new
        {
            status,last_heartbeat=heartbeat==0?null:Time(heartbeat),range_from=Time(from),range_to=Time(to),rows,truncated,
            lost_events=meta.GetValueOrDefault("lost_events"),missed_starts=meta.GetValueOrDefault("missed_starts"),unmatched_stops=meta.GetValueOrDefault("unmatched_stops"),dropped_batches=meta.GetValueOrDefault("dropped_batches"),
            notes="cpu_seconds is derived from CPU cycle counts at the nominal processor frequency. Usage buckets are 5 minutes. Processes shorter than 1 s with little CPU, I/O and memory are counted in their workload, not listed. Peak memory is peak private commit. Executable path is the only program identity."
        };
        string Order(string cpu,string io,string memory,string starts)=>request.Metric switch{"io"=>io,"memory"=>memory,"starts"=>starts,_=>cpu};
    }

    internal static object Definition(string name)=>new
    {
        name,description=name switch
        {
            "get_run_history"=>"Recorded program runs (exited processes) overlapping a range: executable path, PID, parent, start/exit, CPU, I/O, peak memory, exit code and launching workload. Short light processes are only counted in their workload. Requires the app activity recorder.",
            "get_workloads"=>"Launching workloads (process trees such as one build command) with process count, CPU, I/O, largest peak memory and duration; ordered by metric.",
            "get_top_consumers"=>"Per-executable totals over a range (starts, CPU, I/O, peak memory) ordered by metric, from 5-minute usage buckets including long-running programs.",
            "get_app_usage"=>"5-minute usage buckets for executables whose path contains name. Requires name.",
            _=>"Recorder intervals with active, idle (no input for 5 minutes) and locked state, lost-event counts and off gaps."
        },
        inputSchema=new{type="object",properties=new Dictionary<string,object>
        {
            ["last_minutes"]=new{type="integer",minimum=1,maximum=43200,description="Default 1440"},["from"]=new{type="string",format="date-time"},["to"]=new{type="string",format="date-time"},
            ["name"]=new{type="string",maxLength=260,description="Case-insensitive part of the executable path"},
            ["metric"]=new{type="string",@enum=new[]{"cpu","io","memory","starts"}},["limit"]=new{type="integer",minimum=1,maximum=500,description="Default 50"}
        },required=name=="get_app_usage"?new[]{"name"}:Array.Empty<string>(),additionalProperties=false},
        annotations=new{readOnlyHint=true,destructiveHint=false,idempotentHint=true,openWorldHint=false}
    };
}
