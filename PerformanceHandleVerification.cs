#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace FileExplorer;
public partial class MainWindow
{
    // MONITOR-23: handle counts, Handle growth, parent-ended marker, handle types and End process.
    async Task VerifyHandles23(string root,Action<bool,string> check)
    {
        VerifyHandleGrowth23(root,check);
        VerifyHandleHistoryCompatibility23(root,check);
        performance.SetHistorySourceForVerification(null);performance.LiveForVerification();performance.Select(PerformancePanel.Ram);
        await performance.VerifyHandleColumns23(check);
        performance.Select(-1);
        await VerifyHandleFixture23(check);
    }
    static void VerifyHandleGrowth23(string root,Action<bool,string> check)
    {
        string path=Path.Combine(root,"handle-growth.sqlite");var start=DateTimeOffset.UtcNow.AddMinutes(-40);
        var snapshots=new List<PerformanceProcessSnapshot>();
        using(var store=new PerformanceHistory(path))
        {
            for(int i=0;i<180;i++)
            {
                double minute=i/6d;var at=start.AddSeconds(i*10);
                // Level 250k+; 20k for 5 min then +30k/min to 140k; stable 200k; slow +3k/min; a reused PID that stays small.
                ProcessMetric[] apps=[
                    new(21,start.UtcTicks,"handle-level",1,100<<20){Handles=260_000,ParentEnded=true},
                    new(22,start.UtcTicks,"handle-grower",1,100<<20){Handles=20_000+(int)(Math.Clamp(minute-5,0,4)*30_000),ParentEnded=false},
                    new(23,start.UtcTicks,"handle-stable",1,100<<20){Handles=200_000},
                    new(24,start.UtcTicks,"handle-slow",1,100<<20){Handles=100_000+(int)(minute*3_000)},
                    new(22,start.AddSeconds(1).UtcTicks,"handle-reused",1,100<<20){Handles=50_000}];
                store.Record(new(at,10,[],[],null,new(8L<<30,16L<<30){KernelPaged=1L<<30,KernelNonpaged=1L<<29,Handles=900_000}),apps,[]);
                snapshots.Add(new(at,apps,[]));
            }
            check(store.Issue==null,"Handle growth fixture uses actual history persistence");
        }
        var found=PerformanceInvestigation.HandleGrowth(start,start.AddMinutes(31),source:path);
        var level=found.FirstOrDefault(x=>x.Pid==21);var grower=found.FirstOrDefault(x=>x.Pid==22);
        check(found.Length==2&&level!=null&&grower!=null&&found.All(x=>x.Name=="Handle growth"&&x.Started==start.UtcTicks),"Handle growth fires once for the 250,000 level and once for the +100,000/5 min grower: "+string.Join(" | ",found.Select(x=>x.Details)));
        check(found.All(x=>x.Pid is 21 or 22),"A high but stable count below 250,000, a slow grower and a reused PID with another start are not flagged");
        check(level!=null&&level.Details.Contains("reached "+PerformanceInvestigation.HandleLevel.ToString("N0"),StringComparison.Ordinal)&&level.Handles==260_000,"The level event names the reached threshold and the peak count: "+level?.Details);
        check(grower!=null&&Math.Abs((grower.Time-start.AddMinutes(5)).TotalSeconds)<=20&&grower.PeakAt>=start.AddMinutes(8.9)&&grower.Details.Contains("while growing",StringComparison.Ordinal),"The grower event starts where growth began and peaks at its highest count: "+grower?.Details);
        var live=PerformanceInvestigation.HandleGrowth(start,start.AddMinutes(31),source:Path.Combine(root,"absent-handles.sqlite"),liveApps:snapshots.ToArray());
        check(live.Select(x=>x.Pid).Order().SequenceEqual(new int?[]{21,22}),"Handle growth is detected from the automatic recent snapshots before they are saved");
        var query=new PerformanceQuery(LastMinutes:60,Metric:"ram",From:start.AddSeconds(-5),To:start.AddMinutes(31));
        var rail=PerformanceInvestigation.Read(query,source:path).Events;
        check(rail.Count(x=>x.Name=="Handle growth")==2&&rail.Where(x=>x.Name=="Handle growth").All(x=>PerformanceTimeline.Alerting(x)&&x.PeakAt!=default),"The RAM timeline carries Handle growth with the caution presentation and a peak to investigate");
        check(PerformanceInvestigation.Read(query with{Metric="cpu"},source:path).Events.All(x=>x.Name!="Handle growth"),"Handle growth appears only in the RAM view");
        var events=PerformanceHistory.Query(PerformanceMcp.Parse("get_events",JsonSerializer.SerializeToElement(new{@from=query.From.Value.ToString("O"),to=query.To.Value.ToString("O")})),source:path);
        check(events.Rows.Count(x=>x.Name=="Handle growth")==2,"MCP get_events reports the Handle growth events");
        var top=PerformanceHistory.Query(PerformanceMcp.Parse("get_top_processes",JsonSerializer.SerializeToElement(new{metric="ram",@from=query.From.Value.ToString("O"),to=query.To.Value.ToString("O")})),source:path);
        var levelRow=top.Rows.FirstOrDefault(x=>x.Pid==21);var growerRow=top.Rows.FirstOrDefault(x=>x.Pid==22&&x.Started==start.UtcTicks);
        string json=JsonSerializer.Serialize(top);
        check(levelRow is {Handles:260_000,ParentEnded:true}&&growerRow is {Handles:140_000,ParentEnded:false}&&json.Contains("\"Handles\":260000",StringComparison.Ordinal)&&levelRow.Details.Contains("parent ended",StringComparison.Ordinal),"MCP get_top_processes reports peak handle counts and parent-ended state");
        check(!JsonSerializer.Serialize(new PerformanceFact(start,"CPU %",null,1,1,1,"")).Contains("Handles",StringComparison.Ordinal),"Rows without handle data omit the field instead of reporting zero");
        var moment=PerformanceInvestigation.Moment(query,start.AddMinutes(10),source:path);
        check(moment.Apps.Any(x=>x.Pid==21&&x.Handles==260_000&&x.Details.Contains("parent ended",StringComparison.Ordinal)),"Investigated moments list handle counts and the parent-ended marker");
        var current=PerformanceHistory.Query(new("get_current_usage"),source:path);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="SELECT data FROM samples ORDER BY t DESC LIMIT 1";
            var sample=JsonSerializer.Deserialize<PerformanceSample>((string)command.ExecuteScalar());
            check(current.Rows.Length>0&&sample.Ram is {KernelPaged:1L<<30,KernelNonpaged:1L<<29,Handles:900_000},"Saved samples keep the system handle total and kernel paged/non-paged pool");
        }
    }
    static void VerifyHandleHistoryCompatibility23(string root,Action<bool,string> check)
    {
        // History saved before MONITOR-23: no handle columns and no kernel-pool sample fields.
        string path=Path.Combine(root,"handle-legacy.sqlite");var at=DateTimeOffset.UtcNow.AddMinutes(-10);
        SqliteRuntime.Ensure();
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
        {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="CREATE TABLE samples(t INTEGER PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE apps(t INTEGER NOT NULL,pid INTEGER NOT NULL,start INTEGER NOT NULL,name TEXT NOT NULL,cpu REAL,memory INTEGER,PRIMARY KEY(t,pid,start)); CREATE INDEX apps_name_time ON apps(name,t); CREATE INDEX apps_identity_time ON apps(name,pid,start,t); "+
                "CREATE TABLE gpu_apps(t INTEGER NOT NULL,pid INTEGER NOT NULL,start INTEGER NOT NULL,name TEXT NOT NULL,adapter TEXT NOT NULL,usage REAL,PRIMARY KEY(t,pid,start,adapter)); CREATE INDEX gpu_apps_identity_time ON gpu_apps(name,pid,start,adapter,t); CREATE TABLE events(t INTEGER NOT NULL,kind TEXT NOT NULL,detail TEXT NOT NULL);";
            command.ExecuteNonQuery();
            command.CommandText="INSERT INTO samples VALUES($t,$data); INSERT INTO apps VALUES($t,31,$start,'legacy-app',5,1000000);";
            command.Parameters.AddWithValue("$t",at.ToUnixTimeMilliseconds());command.Parameters.AddWithValue("$start",at.UtcTicks);
            command.Parameters.AddWithValue("$data","{\"Time\":\""+at.ToString("O")+"\",\"Cpu\":10,\"Gpus\":[],\"Disks\":[],\"Issue\":null,\"Ram\":{\"Used\":1,\"Total\":2}}");
            command.ExecuteNonQuery();
        }
        var q=new PerformanceQuery("get_top_processes",Metric:"ram",From:at.AddMinutes(-1),To:at.AddMinutes(1));
        var legacy=PerformanceHistory.Query(q,source:path);
        var sample=PerformanceHistory.Query(new("get_current_usage"),source:path);
        check(legacy.Rows.SingleOrDefault() is {Handles:null,ParentEnded:null}&&!legacy.Rows[0].Details.Contains("handles",StringComparison.Ordinal)&&sample.Rows.Length>0,"Older history without handle columns reads as unknown, not zero");
        check(PerformanceInvestigation.HandleGrowth(at.AddMinutes(-1),at.AddMinutes(1),source:path).Length==0&&PerformanceInvestigation.Moment(q with{Tool="get_usage_summary"},at,source:path).Apps.Length==1,"Older history has no derived Handle growth and investigation still works");
        using(var store=new PerformanceHistory(path))
            store.Record(new(at.AddSeconds(10),10,[],[],null,new(1,2)),[new ProcessMetric(31,at.UtcTicks,"legacy-app",5,1000000){Handles=1234}],[]);
        var migrated=PerformanceHistory.Query(q with{To=at.AddMinutes(2)},source:path);
        check(migrated.Rows.SingleOrDefault()?.Handles==1234,"The writer adds the handle columns in place; older rows stay unknown and new rows are counted");
    }
    async Task VerifyHandleFixture23(Action<bool,string> check)
    {
        // Parent identity accounting for PID reuse, without any real process.
        Dictionary<int,int> parents=new(){[10]=20,[20]=4,[30]=99};
        check(ProcessParents.Ended(10,100,parents,pid=>pid==20?(long?)200:null)==true&&ProcessParents.Ended(10,300,parents,pid=>pid==20?(long?)200:null)==false
            &&ProcessParents.Ended(30,100,parents,_=>null)==true&&ProcessParents.Ended(10,100,parents,_=>null)==null,"A parent PID that is gone, or reused by a later process, means the parent ended; an unreadable parent is unknown");
        using var own=Process.GetCurrentProcess();long ownStart=own.StartTime.ToUniversalTime().Ticks;
        var types=await Task.Run(()=>PerformanceProcessControl.HandleTypes(own.Id,ownStart));
        check(types.Issue==null&&types.Types.Length>=3&&types.Types.Any(x=>x.Type=="File")&&types.Total>0,"On-demand handle types return named types for the current process: "+types);
        var wrong=await Task.Run(()=>PerformanceProcessControl.HandleTypes(own.Id,ownStart+1));
        check(wrong.Issue!=null&&wrong.Types.Length==0,"Handle types refuse a mismatched process start");
        check(PerformanceProcessControl.End(own.Id,ownStart)!=null,"End process refuses Pathweft's own process");
        // Real fixtures: a leaking PowerShell whose cmd launcher exits, and a ping whose parent stays alive.
        string system=Environment.SystemDirectory,script="$h=foreach($i in 1..3000){[Threading.EventWaitHandle]::new($false,'ManualReset')};Start-Sleep 90";
        var launcher=Process.Start(new ProcessStartInfo(Path.Combine(system,"cmd.exe"),"/d /c start \"\" /b \""+Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe")+"\" -NoProfile -NonInteractive -Command \""+script+"\""){UseShellExecute=false,CreateNoWindow=true});
        var child=Process.Start(new ProcessStartInfo(Path.Combine(system,"ping.exe"),"-n 60 127.0.0.1"){UseShellExecute=false,CreateNoWindow=true});
        Process leak=null;long leakStart=0;
        try
        {
            using(var exited=new CancellationTokenSource(TimeSpan.FromSeconds(30)))await launcher.WaitForExitAsync(exited.Token);
            long launched=launcher.StartTime.ToUniversalTime().Ticks;
            var map=ProcessParents.Read();
            foreach(var candidate in Process.GetProcessesByName("powershell"))
            {
                if(leak==null&&map!=null&&map.TryGetValue(candidate.Id,out int parent)&&parent==launcher.Id&&ProcessParents.StartOf(candidate.Id) is {} begun&&begun>=launched){leak=candidate;leakStart=begun;}
                else candidate.Dispose();
            }
            check(leak!=null,"The leaking fixture starts from a launcher that has exited");
            if(leak==null)return;
            // Lifecycle condition with a deadline: the fixture has opened its handles.
            var deadline=DateTime.UtcNow.AddSeconds(60);
            while(!leak.HasExited&&DateTime.UtcNow<deadline){leak.Refresh();if(leak.HandleCount>=3000)break;await Task.Delay(250);}
            var metrics=new ProcessMetrics();metrics.Read();var read=metrics.Read();
            var leaked=read.FirstOrDefault(x=>x.Pid==leak.Id&&x.Started==leakStart);var alive=read.FirstOrDefault(x=>x.Pid==child.Id);
            check(leaked is {Handles:>=3000,ParentEnded:true},"The sampler's own enumeration reports the fixture's handle count and parent ended after its launcher exited: "+leaked?.Handles+" "+leaked?.ParentEnded);
            check(alive is {ParentEnded:false}&&alive.Handles>0,"A child whose parent (Pathweft) is running is not marked parent ended");
            var row=new PerformanceProcessRow(leaked??new ProcessMetric(leak.Id,leakStart,"fixture",0,0));
            check(row.ParentMark=="parent ended"&&row.State.Contains("parent ended",StringComparison.Ordinal)&&(leaked??row.Sample).ProcessLabel.EndsWith("parent ended",StringComparison.Ordinal),"CPU, RAM and GPU tables mark the parent-ended fixture");
            var leakTypes=await Task.Run(()=>PerformanceProcessControl.HandleTypes(leak.Id,leakStart));
            check(leakTypes.Issue==null&&leakTypes.Dominant=="Event"&&leakTypes.Types[0].Count>=3000,"On-demand handle types attribute the fixture's leaked handles to Event: "+leakTypes);
            check(PerformanceProcessControl.End(leak.Id,leakStart+TimeSpan.TicksPerSecond)!=null&&!leak.HasExited,"End process refuses a reused/mismatched start time and leaves the process running");
            check(PerformanceProcessControl.End(leak.Id,leakStart)==null,"End process ends the exact rechecked fixture identity");
            bool ended;try{using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(15));await leak.WaitForExitAsync(wait.Token);ended=true;}catch(OperationCanceledException){ended=false;}
            check(ended&&PerformanceProcessControl.End(leak.Id,leakStart)!=null,"The ended fixture exits and its identity can no longer be ended");
        }
        finally
        {
            // Only our own fixtures, by exact identity.
            if(leak!=null&&!leak.HasExited)PerformanceProcessControl.End(leak.Id,leakStart);
            leak?.Dispose();
            if(!child.HasExited)child.Kill();child.Dispose();
            if(!launcher.HasExited)launcher.Kill(true);launcher.Dispose();
        }
    }
}

internal sealed partial class PerformancePanel
{
    internal async Task VerifyHandleColumns23(Action<bool,string> check)
    {
        check(!processColumns["HandlesText"],"Handles column is hidden by default");
        var menu=processColumnsMenu.Items.Cast<MenuItem>().ToArray();
        var handles=menu.Single(x=>(string)x.Tag=="HandlesText");
        PerformanceProcessRow[] Rows(){RefreshProcessRows([new(int.MaxValue-1,1,"handles-low",0,10){Handles=10},new(int.MaxValue-2,1,"handles-high",0,10){Handles=500_000},new(int.MaxValue-3,1,"handles-unknown",0,10)]);return table.Items.Cast<PerformanceProcessRow>().Where(x=>x.Process.StartsWith("handles-",StringComparison.Ordinal)).ToArray();}
        var rows=Rows();
        bool Shown(string property)=>((GridView)table.View).Columns.Any(x=>(x.Header as GridViewColumnHeader)?.Tag as string==property);
        check(ReferenceEquals(table.ItemsSource,processItems)&&!Shown("HandlesText")&&!Shown("Percent")&&handles.Visibility==Visibility.Visible&&menu.Single(x=>(string)x.Tag=="Percent").Visibility==Visibility.Collapsed,"The RAM table offers Handles in the Columns chooser and omits the CPU-only column");
        handles.IsChecked=true;handles.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        rows=Rows();
        check(Shown("HandlesText")&&rows.Single(x=>x.Process=="handles-high").HandlesText==500_000.ToString("N0")&&rows.Single(x=>x.Process=="handles-unknown").HandlesText=="—","Enabling Handles shows counts, with an unknown count as —");
        var header=(GridViewColumnHeader)((GridView)table.View).Columns.First(x=>(x.Header as GridViewColumnHeader)?.Tag as string=="HandlesText").Header;
        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,header));
        rows=Rows();
        check(sortProperty=="Handles"&&rows.Select(x=>x.Process).SequenceEqual(new[]{"handles-high","handles-low","handles-unknown"}),"The Handles header sorts the RAM table by count, unknown last: "+string.Join(",",rows.Select(x=>x.Process)));
        Select(Cpu);
        check(!Shown("HandlesText")&&handles.Visibility==Visibility.Collapsed,"The CPU table has no Handles column even when it is enabled for RAM");
        Select(Ram);handles.IsChecked=false;handles.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(!Shown("HandlesText")&&!processColumns["HandlesText"],"Handles can be hidden again from the Columns chooser");
        table.SelectedItem=null;UpdateProcessActions();
        check(handleTypesButton.Visibility==Visibility.Visible&&endProcessButton.Visibility==Visibility.Visible&&!endProcessButton.IsEnabled&&ramFacts.Text.Contains("kernel paged pool",StringComparison.Ordinal),
            "RAM details show kernel paged/non-paged pool; Handle types and End process wait for an explicit pinned process: "+ramFacts.Text);
    }
}
#endif
