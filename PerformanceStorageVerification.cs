#if DEBUG
using System.IO;
using Microsoft.Data.Sqlite;

namespace FileExplorer;
internal static class PerformanceStorageVerification
{
    internal static void Run(string root,Action<bool,string> check)
    {
        const long limit=2*1024*1024;
        var start=DateTimeOffset.UtcNow.AddHours(-4);
        string path=Path.Combine(root,"capacity-history.sqlite");
        ProcessMetric[] Apps(int tick)=>Enumerable.Range(0,45).Select(n=>new ProcessMetric(n,start.UtcTicks,"app-"+tick+"-"+n+new string('x',160),n<15?100-n:0,n is >=15 and <30?100000-n:1)).ToArray();
        ProcessMetric[] Gpus(ProcessMetric[] apps)=>apps.Where(x=>x.Pid>=30).Select(x=>x with{Adapter="gpu-a",Usage=50}).ToArray();
        using(var store=new PerformanceHistory(path,limit))
        {
            for(int i=0;i<700;i++)
            {var apps=Apps(i);store.Record(new(start.AddSeconds(i*10),20,[],[],null),apps,Gpus(apps));}
            check(store.Issue==null,"Capacity pruning keeps automatic history writable");
            var query=new PerformanceQuery(From:start.AddSeconds(-1),To:start.AddHours(3));
            var result=PerformanceHistory.Query(query,source:path);
            check(result.Rows.Single().Observations is >0 and <700&&result.Points.Last().Time==start.AddSeconds(6990),"Storage pressure removes oldest observations and retains the newest");
            check(new FileInfo(path).Length<=limit,"SQLite file stays within its configured page ceiling");
            using var reader=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());reader.Open();
            using(var transaction=reader.BeginTransaction(deferred:true))
            {
                using var command=reader.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT COUNT(*) FROM samples";command.ExecuteScalar();
                for(int i=700;i<2000&&store.Issue==null;i++)
                {var apps=Apps(i);store.Record(new(start.AddSeconds(i*10),20,[],[],null),apps,Gpus(apps));}
                check(store.Issue?.Contains("waiting for an active reader")==true,"Long-lived reader applies WAL backpressure instead of unbounded writes");
                long heldLength=new FileInfo(path+"-wal").Length;
                for(int i=2000;i<2010;i++)store.Record(new(start.AddSeconds(i*10),20,[],[],null),Apps(i),null);
                check(new FileInfo(path+"-wal").Length==heldLength&&heldLength<PerformanceHistory.WalLimit+limit,"Waiting reader does not grow the journal; overshoot is bounded to one small pruning/write transaction");
            }
            reader.Close();
            store.Record(new(start.AddSeconds(20100),20,[],[],null),Apps(2010),null);
            check(store.Issue==null,"Saving resumes automatically when the actual reader releases its snapshot");
        }
        using(var db=new SqliteConnection("Data Source="+path+";Mode=ReadOnly;Pooling=False"))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA integrity_check";
            check((string)command.ExecuteScalar()=="ok","Capacity pruning/backpressure leave a valid reopenable database");
        }
        check(PerformanceHistory.Query(new("get_top_processes",From:start,To:start.AddHours(6)),source:path).Rows.Length>0,"Automatically saved process identities survive store close and reopen");
        string upgrade=Path.Combine(root,"capacity-upgrade.sqlite");
        using(var previous=new PerformanceHistory(upgrade,limit*2))
        {for(int i=0;i<600;i++){var apps=Apps(i);previous.Record(new(start.AddSeconds(i*10),20,[],[],null),apps,Gpus(apps));}}
        check(new FileInfo(upgrade).Length>limit,"Upgrade fixture contains actual history above the new smaller capacity");
        using(var upgraded=new PerformanceHistory(upgrade,limit))
        {
            upgraded.Record(new(start.AddSeconds(6010),20,[],[],null),Apps(601),null);
            check(upgraded.Issue==null&&new FileInfo(upgrade).Length<=limit,"Existing larger history shrinks incrementally into the ceiling without a replacement database");
        }
        check(PerformanceHistory.Query(new("get_top_processes",Name:"app-601",From:start.AddSeconds(6000),To:start.AddSeconds(6020)),source:upgrade).Rows.Any(x=>x.Name.StartsWith("app-601-")),"Capacity migration preserves the newly saved process observations");
    }
}
#endif
