using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

sealed partial class PackageProbe
{
    async Task VerifyPerformance131()
    {
        var panel=owner.GetType().GetField("performance",Flags).GetValue(owner);var panelType=Type("PerformancePanel");
        // This independent consumer deliberately never shows its host window.
        // Give its real content a finite viewport; IsVisible cannot be true
        // beneath an unshown Window. Presented visibility is qualified separately.
        var content=(FrameworkElement)owner.Content;
        void Layout(){content.Measure(new Size(1200,780));content.Arrange(new Rect(0,0,1200,780));content.UpdateLayout();}
        var pane=(FrameworkElement)owner.FindName("Left");
        var tab=Get(pane,"ActiveTab");var directory=Get(pane,"Dir");
        var selected=((IEnumerable)Get(pane,"Selected")).Cast<object>().Select(x=>(string)Get(x,"Path")).ToArray();
        Layout();
        var scroll=Descendants<ScrollViewer>((FrameworkElement)pane.FindName("List")).FirstOrDefault(x=>x.ScrollableHeight>0);
        scroll?.ScrollToVerticalOffset(Math.Min(10,scroll.ScrollableHeight));Layout();double offset=scroll?.VerticalOffset??0;
        var command=(Button)owner.FindName("PerformanceCommand");command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Call(panelType,panel,"Select",0);Layout();
        var analysis=(FrameworkElement)Get(panel,"Analysis");
        Check(((FrameworkElement)owner.FindName("PaneArea")).Visibility==Visibility.Collapsed&&analysis.Visibility==Visibility.Visible&&analysis.ActualHeight>200,
            "Delivered CPU widget opens analysis in the main browser region");
        Check(!((Button)owner.FindName("TerminalCommand")).IsEnabled,"Delivered main analysis disables hidden file commands");
        Call(panelType,panel,"Select",2);Layout();
        var picker=Descendants<ComboBox>(analysis).Single(x=>x.DisplayMemberPath=="Name");
        Check(picker.Style!=null&&picker.Template!=null&&picker.Visibility==Visibility.Visible,"Delivered GPU adapter selector uses the themed application template");
        Call(panelType,panel,"Select",-1);Layout();
        Check(((FrameworkElement)owner.FindName("PaneArea")).Visibility==Visibility.Visible&&((Button)owner.FindName("TerminalCommand")).IsEnabled,
            "Delivered Back to files restores the live browser and file commands");
        Check(Equals(Get(pane,"ActiveTab"),tab)&&Equals(Get(pane,"Dir"),directory)&&selected.SequenceEqual(((IEnumerable)Get(pane,"Selected")).Cast<object>().Select(x=>(string)Get(x,"Path")))&&Math.Abs((scroll?.VerticalOffset??0)-offset)<1,
            "Delivered analysis preserves the selected tab, path, selection and browser scroll");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var monitorType=Type("PerformanceMonitor");var monitor=monitorType.GetProperty("Current",Flags).GetValue(null);
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Action updated=()=>ready.TrySetResult();var changed=monitorType.GetEvent("Changed",Flags);
            changed.GetAddMethod(true).Invoke(monitor,new object[]{updated});try{await ready.Task;}finally{changed.GetRemoveMethod(true).Invoke(monitor,new object[]{updated});}
        }
        Check(Type("Settings").GetField("PerformanceRecording",Flags)==null,"Delivered automatic history has no recording opt-in setting");
        for(int i=0;i<12;i++)await Next();
        await Next();
        var history=Type("PerformanceHistory");var queryType=Type("PerformanceQuery");
        object Query(string name)=>Activator.CreateInstance(queryType,Flags,null,new object[]{name,60,"cpu",null,null,80d,100,null,null},null);
        var expected=Call(history,null,"Query",Query("get_current_usage"));
        Check(((IEnumerable)Get(expected,"Rows")).Cast<object>().Any(x=>(string)Get(x,"Name")=="CPU %"&&Get(x,"Average") is double),
            "Delivered shared sampler persists real CPU readings, independent of the live graph");
        string source=(string)history.GetProperty("FilePath",Flags).GetValue(null);
        string mcpProfile=Path.Combine(root,"mcp-snapshot"),database=Path.Combine(mcpProfile,"Runtime","Performance","history.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(database));
        using(var live=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=source,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        using(var copy=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+database+";Pooling=False"))
        {live.Open();copy.Open();live.BackupDatabase(copy);}
        expected=Call(history,null,"Query",Query("get_current_usage"),CancellationToken.None,database);
        byte[] before=SHA256.HashData(File.ReadAllBytes(database));
        var start=new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Product.Location),"Pathweft.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--performance-mcp");
        start.Environment["PATHWEFT_PROFILE"]=mcpProfile;
        using var process=Process.Start(start);var error=process.StandardError.ReadToEndAsync();
        async Task<JsonElement> Send(string method,object parameters)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{jsonrpc="2.0",id=7,method,@params=parameters}));await process.StandardInput.FlushAsync();
            string response=await process.StandardOutput.ReadLineAsync();using var document=JsonDocument.Parse(response??"{}");return document.RootElement.Clone();
        }
        var init=await Send("initialize",new{protocolVersion="2025-06-18",capabilities=new{},clientInfo=new{name="Independent package consumer",version="1"}});
        Check(init.GetProperty("result").GetProperty("protocolVersion").GetString()=="2025-06-18","Delivered stdio MCP negotiates its actual supported protocol");
        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");await process.StandardInput.FlushAsync();
        var tools=(await Send("tools/list",new{})).GetProperty("result").GetProperty("tools");
        Check(tools.GetArrayLength()==23&&tools.EnumerateArray().All(x=>x.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()),
            "Delivered MCP advertises exactly twenty-three read-only tools");
        // MCP-08: the index and content status tools, and search_files' locations capped at 10 with visible_only.
        var searchSchema=tools.EnumerateArray().Single(x=>x.GetProperty("name").GetString()=="search_files").GetProperty("inputSchema").GetProperty("properties");
        Check(new[]{"get_index_status","get_content_status","get_content_state"}.All(n=>tools.EnumerateArray().Any(x=>x.GetProperty("name").GetString()==n))
            &&searchSchema.GetProperty("locations").GetProperty("maximum").GetInt32()==10&&searchSchema.TryGetProperty("visible_only",out _),
            "Delivered MCP has the index and content status tools, and search_files takes locations (at most 10) and visible_only");
        foreach(string tool in new[]{"get_current_usage","get_usage_summary","get_top_processes","find_spikes","get_app_history","get_events","get_run_history","get_workloads","get_top_consumers","get_app_usage","get_activity_intervals"})
        {
            var response=(await Send("tools/call",new{name=tool,arguments=new{name="Pathweft"}})).GetProperty("result");
            Check(!response.GetProperty("isError").GetBoolean(),"Delivered real MCP query succeeds: "+tool);
            if(tool=="get_current_usage")
            {
                using var actual=JsonDocument.Parse(response.GetProperty("content")[0].GetProperty("text").GetString());
                string expectedRows=JsonSerializer.Serialize(Get(expected,"Rows"));
                Check(actual.RootElement.GetProperty("Rows").GetRawText()==expectedRows,"Delivered independent MCP process returns the same committed facts as the application query");
            }
        }
        var invalid=(await Send("tools/call",new{name="get_usage_summary",arguments=new{metric="invalid"}})).GetProperty("result");
        Check(invalid.GetProperty("isError").GetBoolean(),"Delivered MCP rejects unsupported metrics without mutation");
        var listed=(await Send("tools/call",new{name="list_folder",arguments=new{path=Path.GetDirectoryName(Product.Location)}})).GetProperty("result");
        Check(!listed.GetProperty("isError").GetBoolean()&&listed.GetProperty("content")[0].GetProperty("text").GetString().Contains("Pathweft.exe"),"Delivered MCP lists the installed folder's metadata");
        Check((await Send("tools/call",new{name="get_maintenance",arguments=new{category="Nope"}})).GetProperty("result").GetProperty("isError").GetBoolean(),"Delivered MCP rejects an unknown Maintenance category");
        process.StandardInput.Close();await process.WaitForExitAsync();
        Check(process.ExitCode==0&&(await error).Length==0&&before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))),
            "Delivered MCP exits naturally on EOF and does not modify recorded history");
        VerifyActivityCommandLines();
        await VerifyPerformance132();
    }

    // ACTIVITY-07: run history answers from a 1.1.87 store (no command columns) and from a store with them.
    void VerifyActivityCommandLines()
    {
        var queryType=Type("ActivityQuery");var requestType=Type("ActivityQuery+Request");
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonElement Run(string file,bool commands,string name)
        {
            using(var db=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+file+";Pooling=False"))
            {
                db.Open();using var command=db.CreateCommand();
                command.CommandText="CREATE TABLE meta(key TEXT PRIMARY KEY,value); CREATE TABLE workloads(id INTEGER PRIMARY KEY,root TEXT NOT NULL,start INTEGER NOT NULL,end INTEGER,processes INTEGER NOT NULL,tiny INTEGER NOT NULL,cycles INTEGER NOT NULL,read INTEGER NOT NULL,write INTEGER NOT NULL,peak INTEGER NOT NULL);"
                    +"CREATE TABLE processes(workload INTEGER NOT NULL,pid INTEGER NOT NULL,parent INTEGER NOT NULL,start INTEGER NOT NULL,exit INTEGER NOT NULL,path TEXT NOT NULL,cycles INTEGER NOT NULL,read INTEGER NOT NULL,write INTEGER NOT NULL,peak INTEGER NOT NULL,code INTEGER"+(commands?",cmd INTEGER,cwd INTEGER":"")+");"
                    +$"INSERT INTO meta VALUES('heartbeat',{now}); INSERT INTO workloads VALUES(1,'C:\\Tools\\build.exe',{now-60000},{now-1000},1,0,1000,1,1,1);"
                    +(commands?"CREATE TABLE texts(id INTEGER PRIMARY KEY,text TEXT NOT NULL UNIQUE); INSERT INTO texts VALUES(1,'build.exe --target probe-package'),(2,'D:\\Work');"
                        +$"INSERT INTO processes VALUES(1,10,4,{now-60000},{now-1000},'C:\\Tools\\build.exe',1000,1,1,1,0,1,2);":$"INSERT INTO processes VALUES(1,10,4,{now-60000},{now-1000},'C:\\Tools\\build.exe',1000,1,1,1,0);");
                command.ExecuteNonQuery();
            }
            var request=Activator.CreateInstance(requestType,"get_run_history",DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow,name,"cpu",50);
            using var document=JsonDocument.Parse(JsonSerializer.Serialize(Call(queryType,null,"Query",request,file)));return document.RootElement.Clone();
        }
        var older=Run(Path.Combine(root,"activity-1187.sqlite"),false,null).GetProperty("rows");
        Check(older.GetArrayLength()==1&&older[0].GetProperty("path").GetString()==@"C:\Tools\build.exe"&&older[0].GetProperty("command_line").ValueKind==JsonValueKind.Null,
            "Delivered run history still answers from a store recorded before command lines");
        var newer=Run(Path.Combine(root,"activity-1188.sqlite"),true,"probe-package").GetProperty("rows");
        Check(newer.GetArrayLength()==1&&newer[0].GetProperty("command_line").GetString()=="build.exe --target probe-package"&&newer[0].GetProperty("working_directory").GetString()==@"D:\Work",
            "Delivered run history returns the command line and starting folder, and its name filter matches the command line");
    }
}
