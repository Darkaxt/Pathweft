using System.IO;

namespace FileExplorer;

internal sealed record PerformanceAppSeries(PerformancePoint[] Cpu,PerformancePoint[] Ram,PerformancePoint[] Gpu);
internal sealed partial class PerformanceHistory
{
    // Donor AppHistory's CPU/RAM/GPU small multiples over the existing bounded
    // query store. Never merge process starts or interpret missing snapshots as 0.
    internal static PerformanceAppSeries AppSeries(PerformanceQuery query,int pid,long started,CancellationToken cancel,string source=null,PerformanceProcessSnapshot[] live=null)
    {
        var from=query.From??DateTimeOffset.UtcNow.AddMinutes(-query.LastMinutes);var to=query.To??DateTimeOffset.UtcNow;
        var values=new Dictionary<(int Metric,long Bucket),(DateTimeOffset Time,double Sum,double Peak,int Count)>();
        var path=source??FilePath;using var db=File.Exists(path)?Open(path,true):null;
        double bucketSeconds=Math.Max(1,(to-from).TotalSeconds/600);
        void Add(int metric,DateTimeOffset time,double value)
        {
            long bucket=(long)((time-from).TotalSeconds/bucketSeconds);var key=(metric,bucket);var old=values.GetValueOrDefault(key);
            values[key]=(time,old.Sum+value,old.Count==0?value:Math.Max(old.Peak,value),old.Count+1);
        }
        long liveStart=live is {Length:>0}?live[0].Time.ToUnixTimeMilliseconds():long.MaxValue;
        if(db!=null)
        {
            using var command=db.CreateCommand();
            command.Parameters.AddWithValue("$pid",pid);command.Parameters.AddWithValue("$start",started);command.Parameters.AddWithValue("$from",from.ToUnixTimeMilliseconds());command.Parameters.AddWithValue("$to",to.ToUnixTimeMilliseconds());command.Parameters.AddWithValue("$live",liveStart);command.Parameters.AddWithValue("$device",query.Device??"");
            command.CommandText="SELECT t,cpu,memory FROM apps WHERE pid=$pid AND start=$start AND t BETWEEN $from AND $to AND t<$live ORDER BY t";
            using(var reader=command.ExecuteReader())while(reader.Read())
            {cancel.ThrowIfCancellationRequested();var time=DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));if(!reader.IsDBNull(1))Add(0,time,reader.GetDouble(1));if(!reader.IsDBNull(2))Add(1,time,reader.GetInt64(2));}
            command.CommandText="SELECT t,MAX(usage) FROM gpu_apps WHERE pid=$pid AND start=$start AND t BETWEEN $from AND $to AND t<$live AND ($device='' OR adapter=$device) GROUP BY t ORDER BY t";
            using(var reader=command.ExecuteReader())while(reader.Read()){cancel.ThrowIfCancellationRequested();if(!reader.IsDBNull(1))Add(2,DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),reader.GetDouble(1));}
        }
        foreach(var snapshot in live??[])
        {
            cancel.ThrowIfCancellationRequested();if(snapshot.Time<from||snapshot.Time>to)continue;
            var app=snapshot.Apps.FirstOrDefault(x=>x.Pid==pid&&x.Started==started);
            if(app!=null){Add(0,snapshot.Time,app.Usage);Add(1,snapshot.Time,app.Memory);}
            var gpus=snapshot.GpuApps.Where(x=>x.Pid==pid&&x.Started==started&&(query.Device==null||x.Adapter==query.Device)).ToArray();
            if(gpus.Length>0)Add(2,snapshot.Time,gpus.Max(x=>x.Usage));
        }
        PerformancePoint[] Series(int metric)
        {
            var result=new List<PerformancePoint>();DateTimeOffset previous=default;
            foreach(var item in values.Where(x=>x.Key.Metric==metric).OrderBy(x=>x.Key.Bucket))
            {
                var sample=item.Value;if(previous!=default&&(sample.Time-previous).TotalSeconds>Math.Max(20,bucketSeconds*2))result.Add(new(sample.Time.AddMilliseconds(-1),null));
                result.Add(new(sample.Time,sample.Sum/sample.Count,sample.Peak));previous=sample.Time;
            }
            return result.ToArray();
        }
        return new(Series(0),Series(1),Series(2));
    }
}
