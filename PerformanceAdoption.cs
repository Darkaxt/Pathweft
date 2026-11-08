using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FileExplorer;

internal sealed partial class PerformancePanel
{
    readonly StackPanel appCharts=new(){Visibility=Visibility.Collapsed,Margin=new(0,12,0,12)};
    readonly PerformanceGraph[] appPlots=[new(),new(),new()];
    readonly TextBlock[] appLabels=[new(),new(),new()];
    PerformanceQuery displayedQuery;
    PerformanceResult displayedResult;
    PerformanceAppSeries displayedApp;
    CancellationTokenSource appChartCancel;Task appChartWork=Task.CompletedTask,appChartRead=Task.CompletedTask;
#if DEBUG
    internal async Task AdoptionForVerification(int kind,string name=null,long start=0)
    {
        if(name!=null)appFilter.Text=name;
        range.SelectedIndex=2;analysisKind.SelectedIndex=kind;await historyQuery;
        if(start!=0)table.SelectedItem=table.Items.Cast<PerformanceFact>().Single(x=>x.Started==start);
        await appChartWork;UpdateLayout();
    }
    internal PerformanceAppSeries AppForVerification=>displayedApp;
    internal PerformanceResult AdoptionResultForVerification=>displayedResult;
    internal bool AppChartsVisibleForVerification=>appCharts.IsVisible&&appPlots.All(x=>x.ActualWidth>100&&x.ActualHeight>=60);
    internal bool PendingAppPresentationForCloseVerification()
    {
        appChartRead=Task.CompletedTask;
        appChartWork=Dispatcher.InvokeAsync(()=>{}).Task;
        return !appChartWork.IsCompleted;
    }
#endif
    void BuildAdoptionControls(WrapPanel controls)
    {
        foreach(string format in new[]{"CSV","JSON","HTML"})
        {
            var button=Action("Export "+format,()=>ExportAnalysis(format));button.Margin=new(0,0,8,6);controls.Children.Add(button);
        }
        for(int i=0;i<3;i++)
        {
            appLabels[i].SetResourceReference(TextBlock.ForegroundProperty,"Muted");appCharts.Children.Add(appLabels[i]);
            appPlots[i].Height=64;appPlots[i].Margin=new(0,3,0,9);appCharts.Children.Add(appPlots[i]);
        }
        table.SelectionChanged+=(_,_)=>
        {
            if(analysisKind.SelectedIndex==4&&table.SelectedItem is PerformanceFact {Pid:{} pid,Started:>0} fact)
            {
                appChartCancel?.Cancel();var previous=appChartWork;var cancellation=new CancellationTokenSource();appChartCancel=cancellation;
                appChartWork=Next();
                async Task Next(){await previous;if(!cancellation.IsCancellationRequested)await ShowApp(fact,pid,cancellation);}
            }
        };
    }
    async Task ShowApp(PerformanceFact fact,int pid,CancellationTokenSource cancellation)
    {
        var query=displayedQuery;if(query==null)return;
        try
        {
            var live=includeLiveHistory?monitor.ProcessHistory():null;
            var read=Task.Run(()=>PerformanceHistory.AppSeries(query,pid,fact.Started,cancellation.Token,historySource,live),cancellation.Token);appChartRead=read;
            var charts=await read;
            if(cancellation.IsCancellationRequested||disposed||analysisKind.SelectedIndex!=4)return;
            displayedApp=charts;var series=new[]{charts.Cpu,charts.Ram,charts.Gpu};
            for(int i=0;i<3;i++)
            {
                double? peak=series[i].Max(x=>x.Peak??x.Value);
                appLabels[i].Text=new[]{"CPU","RAM working set","GPU busiest engine"}[i]+" · "+fact.Name+" · PID "+pid+(peak is {} value?" · peak "+(i==1?MainWindow.Fmt((long)value):value.ToString("0.#")+"%"):" · no observations");
                appPlots[i].SetHistory(series[i].Select(x=>(x.Time,x.Value)).ToArray(),i==1?0:100,Math.Max(20,(query.To.Value-query.From.Value).TotalSeconds/300));
            }
            analysisGraph.Visibility=Visibility.Collapsed;appCharts.Visibility=Visibility.Visible;
            detailState.Text="Observed app snapshots · exact PID/start identity · gaps retained; unobserved intervals are not zero.";
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException){if(!cancellation.IsCancellationRequested)detailState.Text="App charts unavailable: "+ex.Message;}
        finally{if(!ReferenceEquals(appChartCancel,cancellation))cancellation.Dispose();}
    }
    async Task<PerformanceResult> ReadAdoption(int kind,PerformanceQuery query,CancellationToken cancel,PerformanceSample[] live,PerformanceProcessSnapshot[] apps)
    {
        string changed=null;
        if(kind==7&&historySource==null)changed=await PerformanceChanges.Check(monitor,cancel);
        return await Task.Run(()=>
        {
            var rows=new List<PerformanceFact>();var issues=new List<string>();
            if(kind==6)
            {
                foreach(var boot in BootService.Read(30,cancel,issues).Where(x=>x.StartUtc>=query.From.Value.UtcDateTime&&x.StartUtc<=query.To.Value.UtcDateTime))
                    rows.Add(new(new DateTimeOffset(DateTime.SpecifyKind(boot.StartUtc,DateTimeKind.Utc)),"Windows boot",null,boot.DesktopSeconds,boot.Seconds,boot.StartupApps,"Seconds to desktop / settled boot · "+string.Join("; ",boot.SlowedBy.Select(x=>$"{x.Name}: +{x.ExtraSeconds:0.#}s"))));
            }
            else if(kind==7)
            {
                rows.AddRange(BootService.WindowsChanges(query.From.Value.UtcDateTime,new(),cancel,issues).Where(x=>x.Utc<=query.To.Value.UtcDateTime).Select(x=>new PerformanceFact(new DateTimeOffset(DateTime.SpecifyKind(x.Utc,DateTimeKind.Utc)),x.Kind,null,null,null,1,x.Detail)));
                rows.AddRange(PerformanceHistory.Query(query with{Tool="get_events"},cancel,historySource,live,apps).Rows);
            }
            else
            {
                // InsightService's pressure/large-holder/sustained-CPU/boot
                // comparisons; no inferred network, temperature or causality.
                var ram=PerformanceHistory.Query(query with{Tool="find_spikes",Metric="ram",Threshold=85,Device=null},cancel,historySource,live,apps);
                rows.AddRange(ram.Rows.Select(x=>x with{Name="High memory pressure",Details="RAM at least 85% in these observations. Inspect contemporaneous apps; this does not prove paging or a cause."}));
                foreach(string metric in new[]{"ram","cpu"})
                {
                    var heavy=PerformanceHistory.Query(query with{Tool="get_top_processes",Metric=metric,Device=null},cancel,historySource,live,apps);
                    rows.AddRange(heavy.Rows.Where(x=>x.Observations>=60&&x.Average>=(metric=="ram"?3L*1024*1024*1024:5)).Take(3).Select(x=>x with{Name=(metric=="ram"?"Large memory holder · ":"Sustained CPU · ")+x.Name}));
                }
                var boots=BootService.Read(30,cancel,issues);var now=query.To.Value.UtcDateTime;
                var recent=boots.Where(x=>x.StartUtc>now.AddDays(-7)).ToArray();var before=boots.Where(x=>x.StartUtc<=now.AddDays(-7)&&x.StartUtc>now.AddDays(-14)).ToArray();
                if(recent.Length>=2&&before.Length>=2)
                {
                    double current=recent.Average(x=>x.Seconds),old=before.Average(x=>x.Seconds);
                    if(current-old>=10&&current>=old*1.2)rows.Add(new(query.To.Value,"Slower Windows startup",null,old,current,recent.Length,"Average boot seconds: previous week / recent week; inspect the boot slow-starter list."));
                }
            }
            cancel.ThrowIfCancellationRequested();
            foreach(string issue in issues)rows.Add(new(default,"Windows diagnostics unavailable",null,null,null,0,issue));
            var ordered=rows.OrderByDescending(x=>x.Time).ToArray();
            return new PerformanceResult((changed??"Local Windows diagnostics and retained observations.")+" Missing observations are not zero. "+(kind==6?"Boots: desktop seconds / settled seconds / startup apps.":""),ordered.Take(500).ToArray(),[],ordered.Length,ordered.Length>500);
        },cancel);
    }
    void ExportAnalysis(string format)
    {
        if(displayedResult==null||displayedQuery==null||!historyQuery.IsCompleted){detailState.Text="Select a completed history query before exporting.";return;}
        var dialog=new SaveFileDialog{FileName="Pathweft-analysis."+format.ToLowerInvariant(),Filter=format+"|*."+format.ToLowerInvariant()};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{WriteReport(dialog.FileName,format,displayedQuery,displayedResult,displayedApp);detailState.Text="Exported "+Path.GetFileName(dialog.FileName)+".";}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){detailState.Text="Export failed: "+ex.Message;}
    }
    internal static void WriteReport(string file,string format,PerformanceQuery query,PerformanceResult result,PerformanceAppSeries app=null)
    {
        string text;
        if(format=="JSON")text=JsonSerializer.Serialize(new{query,result,app},new JsonSerializerOptions{WriteIndented=true});
        else if(format=="CSV")
        {
            static string Csv(object value)
            {
                string cell=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
                if(value is string&&cell.TrimStart() is {Length:>0} trimmed&&"=+-@".Contains(trimmed[0]))cell="'"+cell;
                return "\""+cell.Replace("\"","\"\"")+"\"";
            }
            var csv=new StringBuilder("Time,Name,PID,Start,Average,Peak,Samples,Details\r\n");
            foreach(var row in result.Rows)csv.AppendLine(string.Join(",",new object[]{row.Time.ToString("O"),row.Name,row.Pid,row.Started,row.Average,row.Peak,row.Observations,row.Details+" · "+result.Status}.Select(Csv)));text=csv.ToString();
        }
        else if(format=="HTML")
        {
            // ReportService's standalone summary/table, escaped for arbitrary
            // process/event names; no remote assets or executable document code.
            static string H(object value)=>WebUtility.HtmlEncode(Convert.ToString(value,CultureInfo.InvariantCulture));
            var html=new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Pathweft analysis</title><style>body{font:14px system-ui;margin:2em;color:#222}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ccc;padding:.6em;text-align:left}p{max-width:90em}</style><h1>System analysis</h1>");
            html.Append("<p>").Append(H(query.From)).Append(" – ").Append(H(query.To)).Append("</p><p>").Append(H(result.Status)).Append("</p><table><tr><th>Observed / peak time<th>Metric / app / event<th>PID<th>Average<th>Peak<th>Samples<th>Details</tr>");
            foreach(var row in result.Rows){html.Append("<tr>");foreach(var value in new object[]{row.When,row.Name,row.Pid,row.Average,row.Peak,row.Observations,row.Details})html.Append("<td>").Append(H(value)).Append("</td>");html.Append("</tr>");}
            html.Append("</table>");text=html.ToString();
        }
        else throw new ArgumentException("Unsupported report format");
        File.WriteAllText(file,text,new UTF8Encoding(false));
    }
}
