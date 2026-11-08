using System.Collections;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

sealed partial class PackageProbe
{
    async Task VerifyDrive137()
    {
        var counters=Type("WindowsPerformanceCounters");
        var mapped=await Task.Run(()=>Call(counters,null,"MapPath",root));
        var number=mapped.GetType().GetField("Item3").GetValue(mapped);
        Check(number is uint,"Delivered current folder maps to one exact physical drive");
        var monitor=Type("PerformanceMonitor").GetProperty("Current",Flags).GetValue(null);
        var disk=Call(counters,null,"ForDisk",Get(monitor,"Latest"),number);
        Check(disk!=null&&Get(disk,"Read") is double read&&read>=0&&Get(disk,"Write") is double write&&write>=0&&Get(disk,"LatencyMs") is double latency&&latency>=0,
            "Delivered Windows counters return read/write throughput and millisecond latency");
        var healthType=Type("WindowsDriveHealth");
        Check(Call(healthType,null,"Number",DBNull.Value)==null&&Call(healthType,null,"Number",new object[]{null})==null&&Equals(Call(healthType,null,"Number",(ushort)42),42d),
            "Delivered WMI reader preserves unavailable reliability values without terminating the shared helper");
        var health=await Task.Run(()=>Call(healthType,null,"Read",number));
        Check(Equals(Get(health,"Disk"),number)&&!string.IsNullOrWhiteSpace((string)Get(health,"Name")),
            "Delivered read-only health query retains the mapped device identity");
        File.WriteAllText(Path.Combine(root,"drive-health.json"),JsonSerializer.Serialize(health,health.GetType(),new JsonSerializerOptions{WriteIndented=true}));
        var old=JsonSerializer.Deserialize("{\"Id\":\"1 D:\",\"Read\":20,\"Write\":30,\"Busy\":4}",Type("DiskReading"));
        Check(Get(old,"LatencyMs")==null,"Delivered history accepts older samples without inventing latency");
        var history=Type("PerformanceHistory");
        var query=Activator.CreateInstance(Type("PerformanceQuery"),Flags,null,new object[]{"get_usage_summary",60,"disk",number.ToString(),null,80d,100,null,null},null);
        var result=Call(history,null,"Query",query);
        var channels=(Array)Get(result,"DrivePoints");
        Check(channels.Length>0&&channels.Cast<object>().Any(point=>Get(point,"LatencyMs") is double),
            "Delivered bounded history returns the selected drive's saved throughput and latency channels");
        var panel=owner.GetType().GetField("performance",Flags).GetValue(owner);var panelType=panel.GetType();
        Call(panelType,panel,"SetFolder",root);
        await(Task)panelType.GetField("mapping",Flags).GetValue(panel);
        await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var range=(ComboBox)panelType.GetField("range",Flags).GetValue(panel);range.SelectedIndex=0;
        var kind=(ComboBox)panelType.GetField("analysisKind",Flags).GetValue(panel);kind.SelectedIndex=0;
        Call(panelType,panel,"Select",3);Call(panelType,panel,"Refresh");
        var chart=(FrameworkElement)panelType.GetField("analysisGraph",Flags).GetValue(panel);
        Check(((Array)chart.GetType().GetField("drive",Flags).GetValue(chart)).Length>0,
            "Delivered Drive I/O selection wires the current device channels into its existing investigation");
        range.SelectedIndex=2;
        await(Task)panelType.GetField("historyQuery",Flags).GetValue(panel);
        var moment=(DateTimeOffset)Get(channels.GetValue(channels.Length-1),"Time");
        Call(chart.GetType(),chart,"ChooseMoment",moment);
        await(Task)panelType.GetField("historyQuery",Flags).GetValue(panel);
        Check(((Array)chart.GetType().GetField("drive",Flags).GetValue(chart)).Length>0,
            "Delivered historical moment selection preserves both drive tracks");
        Call(chart.GetType(),chart,"SetDrive",channels);
        chart.Measure(new Size(820,340));chart.Arrange(new Rect(0,0,820,340));chart.UpdateLayout();
        var image=new RenderTargetBitmap(820,340,96,96,PixelFormats.Pbgra32);image.Render(chart);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
        using(var output=File.Create(Path.Combine(root,"drive-channels.png")))png.Save(output);
        Call(panelType,panel,"Select",-1);
    }
}
