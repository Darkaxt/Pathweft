#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyAnalysis28(string root,Action<bool,string> check)
    {
        for(int i=0;i<45;i++)File.WriteAllText(Path.Combine(root,$"scroll-{i:00}.txt"),"Browser state fixture");
        await S1Verification.Navigate(active,root);performance.Select(-1);UpdateLayout();
        active.ListForVerification.SelectedItem=active.Items.First(x=>!x.IsUp);
        var scroller=active.ScrollerForAnalysisVerification;scroller.ScrollToVerticalOffset(10);UpdateLayout();
        double offset=scroller.VerticalOffset;int tab=active.ActiveTab;
        performance.Select(PerformancePanel.Cpu);performance.Select(-1);
        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        check(offset>0&&Math.Abs(scroller.VerticalOffset-offset)<1&&active.ActiveTab==tab,"Opening and returning preserves a genuinely scrolled browser and selected tab");
        var pane=active;string dir=pane.Dir;var selected=pane.Selected.Select(x=>x.Path).ToArray();
        performance.Select(PerformancePanel.Cpu);UpdateLayout();
        check(PaneArea.Visibility==Visibility.Collapsed&&performance.Analysis.IsVisible&&performance.Analysis.ActualHeight>250,"Widget opens main browser analysis and keeps compact cards visible");
        check(!TerminalCommand.IsEnabled&&PerformanceCommand.IsEnabled,"Analysis disables hidden file commands");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})foreach(double width in new[]{1400d,820d})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=width;Height=850;UpdateLayout();
            check(!TerminalCommand.IsEnabled&&!AnalyzeSpaceCommand.IsEnabled,"Appearance updates preserve the analysis command boundary");
            performance.Select(PerformancePanel.Gpu);await Dispatcher.InvokeAsync(UpdateLayout);
            await S1Verification.Snapshot(this,root,$"analysis-{theme}-{scale:0.0}-{width:0}");
            check(performance.Analysis.ActualHeight>200&&performance.AnalysisLayoutForVerification,"Analysis graph/table retain bounded visible space at "+theme+scale+width);
        }
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();Width=1400;
        performance.Select(-1);UpdateLayout();
        check(ReferenceEquals(active,pane)&&pane.Dir==dir&&pane.Selected.Select(x=>x.Path).SequenceEqual(selected)&&PaneArea.IsVisible&&TerminalCommand.IsEnabled,"Returning restores the same pane, path and selection through background listing refreshes");
        var monitor=PerformanceMonitor.Current;
        async Task Next(){var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);void Updated()=>ready.TrySetResult();monitor.Changed+=Updated;try{await ready.Task;}finally{monitor.Changed-=Updated;}}
        Settings.PerformanceRetention=7;
        for(int i=0;i<12;i++)await Next();
        var current=PerformanceHistory.Query(new("get_current_usage"));
        check(current.Rows.Any(x=>x.Name=="CPU %"&&x.Average is >=0 and <=100)&&monitor.HistoryIssue==null,"Real shared sampler durably records actual CPU/RAM/device observations");
        check(PerformanceHistory.Query(new("get_top_processes")).Rows.Any(),"Actual sampled processes enter history with PID/start identity");
        performance.Select(PerformancePanel.Cpu);await performance.HistoryForVerification(1);UpdateLayout();
        check(performance.HistoryRowsForVerification>0&&!performance.HistoryTextForVerification.StartsWith("History unavailable"),"Actual recorded range renders through the same shared query used by MCP");
        await S1Verification.Snapshot(this,root,"analysis-recorded-summary");
        performance.Select(-1);await Next();
        check(monitor.DetailKind==0,"Closing analysis leaves no per-second process detail consumer");
        string path=PerformanceHistory.FilePath;
        string mcpProfile=Path.Combine(root,"mcp-snapshot"),snapshot=Path.Combine(mcpProfile,"Runtime","Performance","history.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshot));
        File.WriteAllText(Path.Combine(mcpProfile,"settings.txt"),"performance.record=0\n");
        using(var liveDb=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        using(var copy=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+snapshot+";Pooling=False"))
        {liveDb.Open();copy.Open();liveDb.BackupDatabase(copy);}
        var before=SHA256.HashData(File.ReadAllBytes(snapshot));
        var start=new ProcessStartInfo(Program.Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--performance-mcp");
        start.Environment["PATHWEFT_PROFILE"]=mcpProfile;
        using var child=Process.Start(start);var errors=child.StandardError.ReadToEndAsync();
        async Task<JsonElement> Call(string method,object parameters=null)
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id=1,method,@params=parameters??new{}}));await child.StandardInput.FlushAsync();
            string line=await child.StandardOutput.ReadLineAsync();check(line!=null,"MCP replies on actual redirected stdout");using var response=JsonDocument.Parse(line??"{}");return response.RootElement.Clone();
        }
        check((await Call("tools/list")).GetProperty("error").GetProperty("code").GetInt32()==-32002,"MCP requires initialization");
        var initialized=await Call("initialize",new{protocolVersion="unknown",capabilities=new{},clientInfo=new{name="Pathweft qualification",version="1"}});
        check(initialized.GetProperty("result").GetProperty("protocolVersion").GetString()=="2025-06-18","MCP negotiates a supported finite version");
        await child.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");await child.StandardInput.FlushAsync();
        var listed=(await Call("tools/list")).GetProperty("result").GetProperty("tools");
        check(listed.GetArrayLength()==23&&listed.EnumerateArray().All(x=>x.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()),"MCP exposes exactly twenty-three read-only tools (six history, five app activity, twelve Maintenance/file/search/boot/index)");
        var reply=(await Call("tools/call",new{name="get_current_usage",arguments=new{}})).GetProperty("result");
        var result=JsonSerializer.Deserialize<PerformanceResult>(reply.GetProperty("content")[0].GetProperty("text").GetString());
        var persisted=PerformanceHistory.Query(new("get_current_usage"),source:snapshot);
        check(!reply.GetProperty("isError").GetBoolean()&&result.Rows.SequenceEqual(persisted.Rows),"Independent MCP process reads the same committed facts after writer closure");
        check((await Call("tools/call",new{name="get_app_history",arguments=new{}})).GetProperty("result").GetProperty("isError").GetBoolean(),"Invalid app history request returns structured error");
        await VerifyMcpTools48(root,mcpProfile,Call,check);
        await VerifyElevatedReads06((name,arguments)=>Call("tools/call",new{name,arguments}),check);
        // MCP-05: closing stdin while a slow request (every Cleanup target) runs ends the server promptly.
        await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id=2,method="tools/call",@params=new{name="get_maintenance",arguments=new{category="Cleanup"}}}));await child.StandardInput.FlushAsync();
        child.StandardInput.Close();var stopwatch=Stopwatch.StartNew();
        try{await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}catch(TimeoutException){}
        check(child.HasExited&&stopwatch.Elapsed<TimeSpan.FromSeconds(20),"MCP exits promptly when stdin closes mid-request");
        if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}
        check(child.ExitCode==0&&(await errors).Length==0&&before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(snapshot))),"Independent reopened history survives while collection continues; MCP exits on EOF without changing it");
        VerifyHistoryBoundaries28(root,check);
        VerifyGraphTransition28(check);
        await VerifyTimeline29(root,check);
        string invalid=Path.Combine(root,"invalid-history.sqlite");File.WriteAllText(invalid,"Invalid SQLite bytes in this isolated test profile");
        performance.SetHistorySourceForVerification(invalid);
        performance.Select(PerformancePanel.Cpu);await performance.HistoryForVerification(1);
        check(performance.HistoryTextForVerification.StartsWith("History unavailable:"),"Unreadable history is explicit and does not prevent subsequent app/worker shutdown");
        performance.Select(-1);performance.LiveForVerification();Settings.Save();
    }
    static void VerifyGraphTransition28(Action<bool,string> check)
    {
        var start=DateTimeOffset.UtcNow;
        var points=new[]{0,1,10,11}.Select(i=>(start.AddSeconds(i),(double?)40)).ToArray();
        var graph=new PerformanceGraph();graph.Resources["Accent"]=Brushes.DodgerBlue;
        graph.SetHistory(points,100,600);var live=new PerformanceGraph();live.Set(points,100);live.CopyTo(graph);
        graph.Measure(new Size(400,50));graph.Arrange(new Rect(0,0,400,50));graph.UpdateLayout();
        var bitmap=new RenderTargetBitmap(400,50,96,96,PixelFormats.Pbgra32);bitmap.Render(graph);
        var pixels=new byte[400*50*4];bitmap.CopyPixels(pixels,1600,0);
        bool Ink(int x)=>Enumerable.Range(0,48).Any(y=>{int p=(y*400+x)*4;return pixels[p]>100&&pixels[p+2]<90&&pixels[p+3]>60;});
        check(Enumerable.Range(0,8).Any(Ink)&&Enumerable.Range(392,8).Any(Ink)&&!Enumerable.Range(180,40).Any(Ink),"Recorded-to-live graph transition preserves a real collection gap");
    }
    static void VerifyHistoryBoundaries28(string root,Action<bool,string> check)
    {
        string path=Path.Combine(root,"history-boundaries.sqlite");Settings.PerformanceRetention=7;
        var now=DateTimeOffset.UtcNow.AddMinutes(-1);
        var apps=new[]{new ProcessMetric(42,now.UtcTicks,"observed-app",25,1000),new ProcessMetric(42,now.AddSeconds(1).UtcTicks,"observed-app",50,2000)};
        PerformanceSample Sample(DateTimeOffset time,double? cpu)=>new(time,cpu,[new("gpu-a","Adapter A",40,10,100),new("gpu-b","Adapter B",70,20,200)],[new("0 D:",100,200,20),new("1 E:",900,100,30)],null,new(30,100));
        using(var history=new PerformanceHistory(path))
        {
            history.Record(Sample(now.AddDays(-8),99),apps,[]);history.Record(Sample(now,20),apps,[]);history.Record(Sample(now.AddSeconds(5),90),apps,[]);history.Record(Sample(now.AddSeconds(40),null),apps,[]);
            check(history.Issue==null,"Fixture persistence commits without storage failure");
        }
        var query=new PerformanceQuery(LastMinutes:1440*7);var cpu=PerformanceHistory.Query(query,source:path);
        check(cpu.Rows.Single().Peak==90&&cpu.Points.Any(x=>x.Value==null),"Retention prunes expired data; unavailable/gapped CPU is not zero");
        var gpu=PerformanceHistory.Query(query with{Metric="gpu"},source:path);
        check(gpu.Rows.Length==2&&gpu.Rows.Any(x=>x.Peak==40)&&gpu.Rows.Any(x=>x.Peak==70),"GPU history retains separate adapter identities");
        var process=PerformanceHistory.Query(query with{Tool="get_app_history",Name="observed-app"},source:path);
        check(process.Rows.Length==2&&process.Rows.Select(x=>x.Details).Distinct().Count()==2,"PID reuse does not merge distinct starts");
        var top=PerformanceHistory.Query(query with{Tool="get_top_processes",Limit=1},source:path);
        check(top.Total==2&&top.Truncated&&top.Rows.Single().Average==50,"Bounded top-app sorting retains full matching counts and the actual highest average");
        var limited=PerformanceHistory.Query(query with{Metric="disk",Limit=1},source:path);
        check(limited.Total==2&&limited.Truncated&&limited.Rows.Length==1,"Bounded output reports full matching count");
        var spikes=PerformanceHistory.Query(query with{Tool="find_spikes",Threshold=80},source:path);
        check(spikes.Total==1&&spikes.Rows.Single().Peak==90,"Spike query finds observed threshold exceedance");
    }
    // MCP-06: the worker's read-only boot action exists; without an administrator session nothing is launched
    // from the window process and a folder only administrators can list is a clear, prompt structured error.
    static async Task VerifyElevatedReads06(Func<string,object,Task<JsonElement>> tool,Action<bool,string> check)
    {
        bool answered;
        try{answered=(await MaintenanceWorker.Apply(new("boots","",Maintenance.Journal,null))).StartsWith("[");}
        catch(IOException error){answered=error.Message.Length>0;}
        check(answered,"MCP-06 worker boot action returns boots or a clear reason");
        if(!AdministratorSession.Available)
        {
            var notes=new List<string>();
            check(!ElevatedReads.StartSilently&&await ElevatedReads.Boots(notes,default)==null&&notes.Count==0&&!AdministratorSession.Available,"MCP-06 without a session the window reads boots as the user and starts no helper");
        }
        const string protectedFolder=@"C:\System Volume Information";
        bool denied=false;try{Directory.EnumerateFileSystemEntries(protectedFolder).Any();}catch(UnauthorizedAccessException){denied=true;}catch(IOException){}
        if(denied&&!StartupTasks.SessionRegistered.GetValueOrDefault())
        {
            var watch=Stopwatch.StartNew();var reply=(await tool("list_folder",new{path=protectedFolder})).GetProperty("result");
            check(reply.GetProperty("isError").GetBoolean()&&reply.GetProperty("content")[0].GetProperty("text").GetString().Contains("administrator session")&&watch.Elapsed<TimeSpan.FromSeconds(15),
                "MCP-06 list_folder on an administrators-only folder without a session is a prompt structured error");
        }
    }
    // S48 MCP-05: the added tools answer through the real stdio server; bad input is a structured error;
    // reading Maintenance/Cleanup leaves the profile (journal, settings, baselines) byte-identical.
    static async Task VerifyMcpTools48(string root,string profile,Func<string,object,Task<JsonElement>> call,Action<bool,string> check)
    {
        Dictionary<string,string> Hashes()=>Directory.EnumerateFiles(profile,"*",SearchOption.AllDirectories).Where(x=>!x.EndsWith("-wal")&&!x.EndsWith("-shm"))
            .ToDictionary(x=>x,x=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        var before=Hashes();
        async Task<JsonElement> Tool(string name,object arguments)=>(await call("tools/call",new{name,arguments})).GetProperty("result");
        static string Text(JsonElement x)=>x.GetProperty("content")[0].GetProperty("text").GetString();
        string folder=Path.Combine(root,"mcp-folder");Directory.CreateDirectory(Path.Combine(folder,"sub"));File.WriteAllText(Path.Combine(folder,"a.txt"),"12345");
        var listed=await Tool("list_folder",new{path=folder});
        using(var page=JsonDocument.Parse(Text(listed)))
            check(!listed.GetProperty("isError").GetBoolean()&&page.RootElement.GetProperty("Total").GetInt32()==2&&page.RootElement.GetProperty("Rows")[0].GetProperty("type").GetString()=="folder"
                &&page.RootElement.GetProperty("Rows")[1].GetProperty("size_bytes").GetInt64()==5,"MCP list_folder returns metadata, folders first, with the total");
        var details=await Tool("get_file_details",new{path=Program.Executable});
        using(var item=JsonDocument.Parse(Text(details)))
            check(!details.GetProperty("isError").GetBoolean()&&item.RootElement.GetProperty("size_bytes").GetInt64()>0&&item.RootElement.GetProperty("product").GetString()!=null,"MCP get_file_details reports size, times and executable version");
        var size=await Tool("get_folder_size",new{path=folder});
        check(!size.GetProperty("isError").GetBoolean()&&Text(size).Contains("\"method\""),"MCP get_folder_size answers without starting a scan and states its method");
        var search=await Tool("search_files",new{query="\""+Path.GetFileName(Program.Executable)+"\"",limit=5});
        check(!search.GetProperty("isError").GetBoolean()?Text(search).Contains("\"total\""):Text(search).StartsWith("Query unavailable:"),"MCP search_files pages index results or states why the index cannot answer");
        // MCP-08: structured updating fields, batched folder sizes, and the read-only index and content status tools.
        check(!search.GetProperty("isError").GetBoolean()?Text(search).Contains("\"updating_stores\"")&&Text(search).Contains("\"elapsed_us\""):Text(search).StartsWith("Query unavailable:"),"MCP-08 search_files carries the service's updating, updating_stores and elapsed_us");
        var batched=await Tool("get_folder_size",new{paths=new[]{folder,Path.Combine(folder,"sub")}});
        using(var page=JsonDocument.Parse(Text(batched)))
            check(!batched.GetProperty("isError").GetBoolean()&&page.RootElement.GetProperty("total").GetInt32()==2&&page.RootElement.GetProperty("rows")[1].GetProperty("method").GetString().Length>0,"MCP-08 get_folder_size answers a list of folders in one call, each with its method");
        foreach(string tool in new[]{"get_index_status","get_content_status"})
        {
            var reply=await Tool(tool,new{});
            check(!reply.GetProperty("isError").GetBoolean()?Text(reply).Contains(tool=="get_index_status"?"\"stores\"":"\"scopes\""):Text(reply).StartsWith("Query unavailable:"),"MCP-08 "+tool+" passes the service's reply or states why the index cannot answer");
        }
        foreach(var (tool,arguments,what) in new (string,object,string)[]{
            ("search_files",new{query="content:x",locations=11},"more than 10 locations"),("get_folder_size",new{path=folder,paths=new[]{folder}},"both path and paths"),
            ("get_folder_size",new{},"neither path nor paths"),("get_content_state",new{folder="relative"},"a relative content folder")})
            check((await Tool(tool,arguments)).GetProperty("isError").GetBoolean(),"MCP-08 rejects "+what+" with a structured error");
        foreach(string tool in new[]{"get_maintenance_changes","get_boots","get_system_changes","get_insights"})
            check(!(await Tool(tool,new{})).GetProperty("isError").GetBoolean(),"MCP read-only tool answers: "+tool);
        var startup=await Tool("get_maintenance",new{category="Startup",limit=5});
        check(!startup.GetProperty("isError").GetBoolean(),"MCP get_maintenance reads Startup rows as the signed-in user");
        var cleanup=await Tool("get_maintenance",new{category="Cleanup",target="user-temp"});
        using(var page=JsonDocument.Parse(Text(cleanup)))
            check(!cleanup.GetProperty("isError").GetBoolean()&&page.RootElement.GetProperty("Rows")[0].GetProperty("method").GetString().Length>0,"MCP Cleanup size report states the method of each size");
        foreach(var (tool,arguments,what) in new (string,object,string)[]{
            ("list_folder",new{path="relative\folder"},"relative path"),("get_maintenance",new{category="Nope"},"unknown category"),
            ("get_maintenance",new{category="Startup",target="user-temp"},"target outside Cleanup"),("list_folder",new{path=folder,extra=1},"unknown argument"),
            ("search_files",new{query="x",limit=501},"limit above 500"),("get_folder_size",new{path=Path.Combine(root,"missing-folder")},"missing folder")})
            check((await Tool(tool,arguments)).GetProperty("isError").GetBoolean(),"MCP rejects "+what+" with a structured error");
        var after=Hashes();
        check(before.Count==after.Count&&before.All(x=>after.TryGetValue(x.Key,out var hash)&&hash==x.Value),"MCP Maintenance and Cleanup reads leave the profile's journal, settings and baselines byte-identical");
    }
}
public partial class PaneView
{
    internal ScrollViewer ScrollerForAnalysisVerification=>FindScroller(List);

}
#endif
