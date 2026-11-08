using System.IO;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

internal sealed record PerformanceTimelineData(PerformanceResult Activity,PerformanceFact[] Events,long EventCount);
internal sealed record PerformanceMoment(DateTimeOffset Requested,DateTimeOffset? Observed,PerformanceFact[] Apps,string Status);

// Read-only investigation over the existing recording. No second sampler or log.
internal static class PerformanceInvestigation
{
    internal static PerformanceTimelineData Read(PerformanceQuery query,CancellationToken cancel=default,string source=null,PerformanceSample[] live=null)
    {
        var activity=PerformanceHistory.Query(query with{Tool="get_usage_summary"},cancel,source,live);
        var events=PerformanceHistory.Query(query with{Tool="get_events"},cancel,source,live);
        var spikes=PerformanceHistory.Query(query with{Tool="find_spikes"},cancel,source,live);
        var markers=events.Rows.Concat(spikes.Rows.Select(x=>x with{Name="Usage spike",Details=x.Name+" · "+x.Details}))
            .OrderBy(x=>x.Time).Take(query.Limit).ToArray();
        return new(activity,markers,events.Total+spikes.Total);
    }
    internal static PerformanceMoment Moment(PerformanceQuery query,DateTimeOffset time,CancellationToken cancel=default,string source=null)
    {
        PerformanceHistory.Validate(query);
        string path=source??PerformanceHistory.FilePath;
        if(!File.Exists(path))return new(time,null,[],"No recorded app observations at this time. Enable history recording to investigate future activity.");
        SqliteRuntime.Ensure();using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";command.ExecuteNonQuery();
        long at=time.ToUnixTimeMilliseconds();
        command.Parameters.AddWithValue("$from",at-10000);command.Parameters.AddWithValue("$to",at+10000);command.Parameters.AddWithValue("$at",at);
        string table=query.Metric=="gpu"?"gpu_apps":"apps";
        command.Parameters.AddWithValue("$device",query.Device??"");
        string device=query.Metric=="gpu"?" AND ($device='' OR adapter=$device)":"";
        command.CommandText=$"SELECT t FROM {table} WHERE t BETWEEN $from AND $to{device} ORDER BY abs(t-$at),t LIMIT 1";
        cancel.ThrowIfCancellationRequested();var nearest=command.ExecuteScalar();
        if(nearest==null)return new(time,null,[],"No recorded app snapshot within 10 seconds of this moment. Missing observations are not zero activity.");
        var observed=DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(nearest));
        if(query.Metric=="disk")return new(time,observed,[],"Historical per-process drive I/O was not recorded. Use live drive details for file attribution.");
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
