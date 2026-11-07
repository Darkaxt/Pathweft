using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

sealed partial class PackageProbe
{
    async Task VerifyPerformance128()
    {
        var pane=(FrameworkElement)owner.FindName("Left");
        Check(pane.FindName("UsedBar")==null&&pane.FindName("DriveInfoText")==null&&pane.FindName("FolderStrip")==null&&((FrameworkElement)pane.FindName("ReadOnlyWatermark")).Visibility==Visibility.Collapsed,
            "Delivered folder view omits the duplicate address capacity strip");
        var type=Type("PerformanceMonitor");
        var monitor=type.GetProperty("Current",Flags).GetValue(null);
        Check(ReferenceEquals(monitor,type.GetProperty("Current",Flags).GetValue(null)),"Delivered panes share one aggregate collector");
        async Task Next()
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Action updated=()=>ready.TrySetResult();var changed=type.GetEvent("Changed",Flags);
            changed.GetAddMethod(true).Invoke(monitor,new object[]{updated});
            try{await ready.Task;}finally{changed.GetRemoveMethod(true).Invoke(monitor,new object[]{updated});}
        }
        await Next();await Next();
        var sample=Get(monitor,"Latest");
        Check(Get(sample,"Cpu") is double cpu&&cpu>=0&&cpu<=100,"Delivered library collects actual Windows CPU deltas");
        var counters=Type("WindowsPerformanceCounters");
        Check(Call(counters,null,"ForPath",sample,root)!=null&&Call(counters,null,"ForPath",sample,@"\\unavailable\share")==null,
            "Delivered library resolves the owned volume and refuses a fabricated network physical device");
        var command=(Button)owner.FindName("PerformanceCommand");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(command.Tag as string=="on"&&AutomationProperties.GetItemStatus(command)=="On"&&AutomationProperties.GetName(command)=="Show folder statistics",
            "Delivered performance command exposes active state and the return action");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(command.Tag==null&&AutomationProperties.GetItemStatus(command)=="Off","Delivered command restores the statistics mode");

        var release=Type("WhatsNewWindow").GetNestedType("Release",BindingFlags.NonPublic);
        var hashes=new HashSet<string>();
        foreach(int month in Enumerable.Range(1,12))
        {
            object Entry(int year)=>Activator.CreateInstance(release,Flags,null,new object[]{"test",new DateTime(year,month,1),"test",new List<string>()},null);
            using var first=(Stream)Call(Type("WhatsNewWindow"),null,"Picture",Entry(2026));
            using var next=(Stream)Call(Type("WhatsNewWindow"),null,"Picture",Entry(2027));
            string hash=Convert.ToHexString(SHA256.HashData(first));hashes.Add(hash);
            Check(hash==Convert.ToHexString(SHA256.HashData(next)),"Delivered monthly artwork survives year rollover: "+month);
            var banner=(Border)Call(Type("WhatsNewWindow"),null,"Banner",Entry(2027));
            Check(banner.Width==960&&banner.Height==170&&banner.Background is ImageBrush {AlignmentY:AlignmentY.Center,Stretch:Stretch.UniformToFill},
                "Delivered banner uses the centered 960:170 viewport: "+month);
            banner.Measure(new Size(960,170));banner.Arrange(new Rect(0,0,960,170));banner.UpdateLayout();
            var bitmap=new RenderTargetBitmap(960,170,96,96,PixelFormats.Pbgra32);bitmap.Render(banner);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(root,$"packaged-month-{month:00}.png"));png.Save(output);
        }
        Check(hashes.Count==12,"Delivered package contains twelve distinct ImageGen monthly banners");
    }
}
