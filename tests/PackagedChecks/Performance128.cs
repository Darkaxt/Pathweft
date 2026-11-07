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
        var performance=owner.GetType().GetField("performance",Flags).GetValue(owner);
        Type("PerformancePanel").GetProperty("AutomaticIo",Flags).SetValue(performance,false);
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
        var ram=Get(sample,"Ram");
        Check(ram!=null&&(long)Get(ram,"Total")>0&&(long)Get(ram,"Used")>=0&&(long)Get(ram,"Used")<=(long)Get(ram,"Total"),"Delivered physical RAM reports actual used/total bytes separately from VRAM");
        var counters=Type("WindowsPerformanceCounters");
        Check(Call(counters,null,"ForPath",sample,root)!=null&&Call(counters,null,"ForPath",sample,@"\\unavailable\share")==null,
            "Delivered library resolves the owned volume and refuses a fabricated network physical device");
        var command=(Button)owner.FindName("PerformanceCommand");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(command.Tag as string=="on"&&AutomationProperties.GetItemStatus(command)=="On"&&AutomationProperties.GetName(command)=="Show folder statistics",
            "Delivered performance command exposes active state and the return action");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(command.Tag==null&&AutomationProperties.GetItemStatus(command)=="Off","Delivered command restores the statistics mode");
        VerifyObservedGraphs();

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
    void VerifyObservedGraphs()
    {
        var type=Type("PerformanceGraph");
        void Render(int seconds,bool gap,string name)
        {
            var graph=(FrameworkElement)Activator.CreateInstance(type,true);
            graph.Resources["Accent"]=Brushes.DodgerBlue;
            var start=DateTimeOffset.UtcNow;
            var points=Enumerable.Range(0,seconds+1).Select(i=>(start.AddSeconds(i),(double?)(gap&&i>seconds/3&&i<seconds*2/3?null:40+10*Math.Sin(i)))).ToArray();
            Call(type,graph,"Set",points,100d);
            graph.Measure(new Size(400,50));graph.Arrange(new Rect(0,0,400,50));graph.UpdateLayout();
            var bitmap=new RenderTargetBitmap(400,50,96,96,PixelFormats.Pbgra32);bitmap.Render(graph);
            var pixels=new byte[400*50*4];bitmap.CopyPixels(pixels,400*4,0);
            bool Ink(int x)=>Enumerable.Range(0,48).Any(y=>{int p=(y*400+x)*4;return pixels[p]>100&&pixels[p+2]<90&&pixels[p+3]>60;});
            Check(Enumerable.Range(0,8).Any(Ink)&&Enumerable.Range(392,8).Any(Ink),"Delivered "+name+" history spans the graph width");
            if(gap)Check(!Enumerable.Range(180,40).Any(Ink),"Delivered unavailable interval remains a visible gap without invented zero samples");
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(root,"performance-graph-"+name+".png"));png.Save(output);
        }
        Render(60,false,"one-minute");Render(3599,false,"one-hour");Render(60,true,"gapped");
    }
}
