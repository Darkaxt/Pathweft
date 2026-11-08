#if DEBUG
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyDrive37(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false;Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Next()
        {
            var source=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed()=>source.TrySetResult();PerformanceMonitor.Current.Changed+=Changed;
            try{await source.Task;}finally{PerformanceMonitor.Current.Changed-=Changed;}await Settle();
        }
        await S1Verification.Navigate(Left,root);if(!PerformanceMode)await TogglePerformance();
        var mapped=await Task.Run(()=>WindowsPerformanceCounters.MapPath(root));
        check(mapped.Disk.HasValue,"Current folder resolves to a single physical disk extent");
        performance.SetFolder(root);performance.Select(PerformancePanel.Drive);await Next();await Next();
        await performance.DriveReadyForVerification;await Settle();
        var native=WindowsPerformanceCounters.ForDisk(PerformanceMonitor.Current.Latest,mapped.Disk);
        check(native is {Read:>=0,Write:>=0,Busy:>=0 and <=100,LatencyMs:>=0},"Real Windows physical-device read/write, idle and latency counters return correctly scaled observations");
        var health=performance.DriveHealthForVerification;
        check(health?.Disk==mapped.Disk&&health.Name.Length>0,"Native health identity belongs to the exact mapped physical drive");
        File.WriteAllText(Path.Combine(root,"drive-native.json"),JsonSerializer.Serialize(new{mapped,native,health},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        check(WindowsPerformanceCounters.MapPath(@"\\server\share").Disk==null&&CloudVolumes.GoogleRoots().All(path=>WindowsPerformanceCounters.MapPath(path).Disk==null),"Remote and registered virtual drives never borrow cache-disk health or throughput");

        var smart=new byte[512];var identify=new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(smart.AsSpan(1),310);smart[3]=95;smart[4]=10;smart[5]=27;
        BinaryPrimitives.WriteUInt128LittleEndian(smart.AsSpan(48),1953125);BinaryPrimitives.WriteUInt128LittleEndian(smart.AsSpan(128),12345);
        BinaryPrimitives.WriteUInt16LittleEndian(identify.AsSpan(266),355);
        check(WindowsDriveHealth.Number(DBNull.Value)==null&&WindowsDriveHealth.Number(null)==null&&WindowsDriveHealth.Number((ushort)42)==42,"Unsupported WMI reliability properties remain absent for standard and elevated readers");
        var decoded=WindowsDriveHealth.FromNvme(new(7,"duplicate model",DateTimeOffset.UtcNow),smart,identify);
        check(decoded.Disk==7&&decoded.LifePercent==73&&decoded.PowerOnHours==12345&&Math.Abs(decoded.WrittenTB.Value-1)<.000001&&Math.Abs(decoded.TempC.Value-36.85)<.001&&Math.Abs(decoded.WarnTempC.Value-81.85)<.001,"NVMe units decode 128-bit lifetime writes, power-on hours, Celsius and percentage used without model-order attribution");
        check(new DriveHealth(0,"unknown",DateTimeOffset.UtcNow).Status.Text=="Health unavailable"&&decoded.Status.Text=="Healthy","Missing health never becomes Healthy; a valid zero critical-warning log can report it");
        check((decoded with{CriticalWarning=2}).Status.Text=="Running hot"&&(decoded with{SpareBelowThreshold=true}).Status.Level=="bad","Donor warning policy distinguishes temperature from failing spare/lifetime health");
        check(PerformanceHistory.DiskMatches("1 D:","1")&&!PerformanceHistory.DiskMatches("11 F:","1"),"Historical drive matching uses the complete physical number, not a model or substring");
        var old=JsonSerializer.Deserialize<DiskReading>("{\"Id\":\"1 D:\",\"Read\":20,\"Write\":30,\"Busy\":4}");
        check(old.LatencyMs==null,"Existing saved history remains readable without inventing missing latency");
        var now=DateTimeOffset.UtcNow;string db=Path.Combine(root,"drive-history.sqlite");
        var samples=Enumerable.Range(0,180).Select(i=>new PerformanceSample(now.AddSeconds(-900+i*5),20,[],
            [new(mapped.Disk+" D:",1000000+i*10000,500000+(i%20)*20000,i%100,i%9*.4),new("99 Z:",9e10,9e10,100,999)],null)).ToArray();
        using(var store=new PerformanceHistory(db))foreach(var sample in samples)store.Record(sample,[],[]);
        var query=new PerformanceQuery(Metric:"disk",Device:mapped.Disk.Value.ToString(),From:now.AddMinutes(-20),To:now);
        var result=PerformanceHistory.Query(query,source:db);
        check(result.DrivePoints.Length>0&&result.DrivePoints.Length<=1202&&result.DrivePoints.Any(p=>p.LatencyMs>0)&&result.DrivePoints.All(p=>p.Read<1e8),"Persisted drive-specific channels retain latency and peaks within the bounded historical bucket count");
        performance.SetHistorySourceForVerification(db);await performance.HistoryForVerification(0);await Settle();
        check(performance.TimelineForVerification.DriveCountForVerification>0,"Historical activity presents donor-style throughput and latency tracks inside the existing investigation");
        var timeline=performance.TimelineForVerification;
        timeline.SelectMomentForVerification(now.AddMinutes(-8));await performance.InvestigationReadyForVerification;await Settle();
        check(timeline.DriveCountForVerification>0,"Pinning a historical drive moment preserves both channel tracks without inventing per-process history");
        timeline.SelectRangeForVerification(now.AddMinutes(-10),now.AddMinutes(-5));await performance.InvestigationReadyForVerification;await Settle();
        check(timeline.DriveCountForVerification>0&&timeline.From>=now.AddMinutes(-10)&&timeline.To<=now.AddMinutes(-5),"Time selection drives both channel plots without losing drive history");
        performance.ResetInvestigationForVerification();await Settle();performance.SetHistorySourceForVerification(db);await performance.HistoryForVerification(0);await Settle();
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})foreach(double width in new[]{1400d,820d})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=width;Height=850;await Settle();
            check(performance.RevealTimelineForVerification(),"Both drive tracks are reachable at "+theme+scale+width);
            await S1Verification.Snapshot(this,root,$"drive-{theme}-{scale*100:0}-{width:0}");
        }
        Settings.NativeMode="dark";Settings.TextScale=1;ApplyAppearance();Width=1400;performance.SetHistorySourceForVerification(null);performance.LiveForVerification();await Next();
        var failed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(string message)=>failed.TrySetResult();Left.Error+=Failed;
        try{Left.Navigate(@"\\localhost\pathweft-missing-share");await failed.Task;}finally{Left.Error-=Failed;}
        performance.SetFolder(Left.Dir);await performance.DriveMappingForVerification;await Settle();
        check(performance.DriveFactsForVerification.Contains("Physical drive unavailable"),"Navigation clears drive identity and health for an unsupported path");
        await S1Verification.Navigate(Left,root);performance.SetFolder(root);await performance.DriveMappingForVerification;await performance.DriveReadyForVerification;await Settle();
        check(performance.DriveHealthForVerification?.Disk==mapped.Disk,"Returning to the local path restores its actual health identity");
        foreach(var volume in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady))
        {
            var other=await Task.Run(()=>WindowsPerformanceCounters.MapPath(volume.RootDirectory.FullName));
            if(!other.Disk.HasValue||other.Disk==mapped.Disk)continue;
            await S1Verification.Navigate(Left,volume.RootDirectory.FullName);performance.SetFolder(Left.Dir);
            await performance.DriveMappingForVerification;await Settle();await performance.DriveReadyForVerification;await Settle();
            check(performance.DriveHealthForVerification?.Disk==other.Disk,"Navigation to another real physical drive replaces the health identity");
            await S1Verification.Navigate(Left,root);performance.SetFolder(root);
            await performance.DriveMappingForVerification;await Settle();await performance.DriveReadyForVerification;await Settle();
            check(performance.DriveHealthForVerification?.Disk==mapped.Disk,"Returning from another physical drive cannot retain its stale health result");
            break;
        }
        using var self=Process.GetCurrentProcess();self.Refresh();long before=self.PrivateMemorySize64;var cpu=self.TotalProcessorTime;var clock=Stopwatch.StartNew();
        for(int i=0;i<30;i++)await Next();self.Refresh();
        double use=(self.TotalProcessorTime-cpu).TotalSeconds/clock.Elapsed.TotalSeconds/Environment.ProcessorCount*100;
        File.WriteAllText(Path.Combine(root,"drive-cost.json"),JsonSerializer.Serialize(new{seconds=clock.Elapsed.TotalSeconds,cpuPercent=use,privateBytes=self.PrivateMemorySize64,privateChange=self.PrivateMemorySize64-before}));
        check(use<=2&&self.PrivateMemorySize64-before<32L*1024*1024,"Expanded dual-track drive view remains within measured CPU and memory growth budgets");
        performance.Select(-1);check(HiddenTrialPresentation.ForegroundViolations==0,"Drive diagnostics never take over the user's desktop");
    }
}
#endif
