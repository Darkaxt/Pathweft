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
        var setting=Type("Settings").GetField("PerformanceRecording",Flags);setting.SetValue(null,true);
        try{for(int i=0;i<12;i++)await Next();}finally{setting.SetValue(null,false);}
        await Next();
        var history=Type("PerformanceHistory");var queryType=Type("PerformanceQuery");
        object Query(string name)=>Activator.CreateInstance(queryType,Flags,null,new object[]{name,60,"cpu",null,null,80d,100,null,null},null);
        var expected=Call(history,null,"Query",Query("get_current_usage"));
        Check(((IEnumerable)Get(expected,"Rows")).Cast<object>().Any(x=>(string)Get(x,"Name")=="CPU %"&&Get(x,"Average") is double),
            "Delivered shared sampler persists real CPU readings, independent of the live graph");
        string database=(string)history.GetProperty("FilePath",Flags).GetValue(null);byte[] before=SHA256.HashData(File.ReadAllBytes(database));
        var start=new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Product.Location),"Pathweft.exe"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--performance-mcp");
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
        Check(tools.GetArrayLength()==6&&tools.EnumerateArray().All(x=>x.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()),
            "Delivered MCP advertises only six read-only performance/history tools");
        foreach(string tool in new[]{"get_current_usage","get_usage_summary","get_top_processes","find_spikes","get_app_history","get_events"})
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
        process.StandardInput.Close();await process.WaitForExitAsync();
        Check(process.ExitCode==0&&(await error).Length==0&&before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))),
            "Delivered MCP exits naturally on EOF and does not modify recorded history");
    }
}
