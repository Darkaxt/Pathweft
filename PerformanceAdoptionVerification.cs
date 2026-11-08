#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyAdoption32(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false;Settings.Stats=2;ApplyStats();SetSinglePane(true);SetActive(Left);await S1Verification.Navigate(active,root);
        PerformanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));performance.Select(PerformancePanel.Cpu);
        string file=Path.Combine(root,"adoption.sqlite");var from=DateTimeOffset.UtcNow.AddMinutes(-20);long start=from.AddMinutes(-1).UtcTicks;
        using(var history=new PerformanceHistory(file))
        {
            for(int i=0;i<75;i++)
            {
                var time=from.AddSeconds(i*10);var app=new ProcessMetric(123,start,"analysis-fixture",20,4L*1024*1024*1024);var other=app with{Started=start+1,Usage=90,Memory=12};
                var gpu=app with{Usage=30,Adapter="gpu-fixture"};
                history.Record(new(time,30,[new("gpu-fixture","Fixture adapter",40,20,100)],[new("0 D:",100,200,2)],null,new(90,100)),[app,other],[gpu]);
            }
            check(history.Issue==null,"Donor-derived investigation fixtures use the real bounded history writer");
        }
        var query=new PerformanceQuery("get_app_history",60,"cpu",Name:"analysis-fixture",From:from.AddMinutes(-1),To:from.AddMinutes(20));
        var series=PerformanceHistory.AppSeries(query,123,start,default,file);
        check(series.Cpu.All(x=>x.Value==20)&&series.Ram.All(x=>x.Value==4L*1024*1024*1024)&&series.Gpu.All(x=>x.Value==30),"Three app charts preserve exact PID/start identity and CPU/RAM/GPU units without merging PID reuse");
        performance.SetHistorySourceForVerification(file);await performance.AdoptionForVerification(4,"analysis-fixture",start);UpdateLayout();
        check(performance.AppChartsVisibleForVerification&&performance.AppForVerification.Cpu.Length==75,"Actual app-history selection displays all three charts in the main analysis area");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1400;Height=900;UpdateLayout();
            await S1Verification.Snapshot(this,root,$"app-history-{theme}-{scale:0.0}");
            check(performance.AppChartsVisibleForVerification,"App history remains themed and visible at "+theme+" / "+scale);
        }
        await performance.AdoptionForVerification(8);
        check(performance.AdoptionResultForVerification.Rows.Any(x=>x.Name.StartsWith("Large memory holder"))&&performance.AdoptionResultForVerification.Rows.Any(x=>x.Name.StartsWith("Sustained CPU")),"On-demand donor insights identify observed large memory holders and sustained CPU without network or sensor fields");
        await S1Verification.Snapshot(this,root,"performance-insights");
        await performance.AdoptionForVerification(6);check(performance.AdoptionResultForVerification!=null,"Boot diagnostics return native facts or an explicit access/log issue");
        await performance.AdoptionForVerification(7);check(performance.AdoptionResultForVerification!=null,"System changes combine native Windows/driver events and retained local history");
        var before=new PerformanceChanges.Baseline(from,new(){["64/a"]=new("Same name","1"),["32/b"]=new("Same name","2")});
        var now=new PerformanceChanges.Baseline(from.AddHours(1),new(){["64/a"]=new("Same name","3")});var diff=PerformanceChanges.Diff(before,now);
        check(diff.Length==2&&diff.Any(x=>x.Name=="App updated")&&diff.Any(x=>x.Name=="App removed")&&diff.All(x=>x.Details.Contains("actual installation/removal time is unknown")),"Donor app-change comparison preserves separate installed identities and does not invent installation timestamps");
        var result=new PerformanceResult("Gaps retained; observed facts only",[new(from,"=1+1 <script>",123,20,40,10,"<img src=x onerror=alert(1)>; commas, and \"quotes\""){Started=start}],series.Cpu,1,false);
        foreach(string format in new[]{"CSV","JSON","HTML"})PerformancePanel.WriteReport(Path.Combine(root,"analysis."+format.ToLowerInvariant()),format,query,result,series);
        check(File.ReadAllText(Path.Combine(root,"analysis.csv")).Contains("'=1+1")&&!File.ReadAllText(Path.Combine(root,"analysis.html")).Contains("<script>"),"Exports escape untrusted app/event text and neutralize spreadsheet formulas");
        using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"analysis.json"))))check(json.RootElement.GetProperty("app").GetProperty("Gpu").GetArrayLength()==75,"JSON export retains selected range, identity and all three chart series");
        performance.SetHistorySourceForVerification(null);performance.Select(-1);performance.LiveForVerification();Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();
        check(performance.PendingAppPresentationForCloseVerification(),"Natural close is exercised with a chart UI presentation pending and its native read already complete");
    }
}
#endif
