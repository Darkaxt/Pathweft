#if DEBUG
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyUpdate114(string root,Action<bool,string> check)
    {
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        async Task Settled()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        static void SetPoint(Slider timeline,double value)=>((IRangeValueProvider)new SliderAutomationPeer(timeline).GetPattern(PatternInterface.RangeValue)).SetValue(value);
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.Preview=true;ApplyPreview();
        string video=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("PATHWEFT_MEDIA113_INPUTS"))).RootElement.EnumerateArray().First(input=>input.GetProperty("name").GetString()=="sdr").GetProperty("path").GetString();
        sidePreview.Show(new Entry{Path=video,Name=Path.GetFileName(video)});await sidePreview.Completion;await Settled();
        check(sidePreview.State=="ready","The real video preview is ready for timeline interaction");
        var media=((BuiltinViewer)sidePreview.ViewerForVerification).MediaForVerification;
        var player=media.Player;var timeline=PreviewChildren<Slider>(PreviewBody).Single();
        check(timeline.IsMoveToPointEnabled&&timeline.Style==FindResource(typeof(Slider)),"The actual media timeline retains the themed WPF click-to-point behavior");
        foreach(bool paused in new[]{true,false})
        {
            await player.CommandAsync("set","pause",paused?"yes":"no");
            double goal=paused?3:5;
            var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Position(string name,object value){if(name=="time-pos"&&value is double seconds&&Math.Abs(seconds-goal)<.3)reached.TrySetResult();}
            player.Changed+=Position;
            try
            {
                ulong before=(ulong)typeof(MpvPlayer).GetField("commandId",fields).GetValue(player);
                SetPoint(timeline,goal); // WPF's real RangeValue/ValueChanged path, also used by track clicks.
                check((ulong)typeof(MpvPlayer).GetField("commandId",fields).GetValue(player)>before,"Timeline point selection submits a native seek immediately, before mouse-up: "+(paused?"paused":"playing"));
                await reached.Task;
                check(player.Property("pause")== (paused?"yes":"no"),"Native video reaches the selected time and preserves playback state: "+(paused?"paused":"playing"));
            }
            finally{player.Changed-=Position;}
        }
        await player.CommandAsync("set","pause","yes");await Settled();
        ulong reported=(ulong)typeof(MpvPlayer).GetField("commandId",fields).GetValue(player);
        ((PreviewTimeline)timeline).ReportPosition(5.1);
        check((ulong)typeof(MpvPlayer).GetField("commandId",fields).GetValue(player)==reported,"Engine position feedback moves the thumb without generating another seek");
        var thumb=((Track)timeline.Template.FindName("PART_Track",timeline)).Thumb;
        thumb.RaiseEvent(new DragDeltaEventArgs(-15,0){RoutedEvent=Thumb.DragDeltaEvent});
        check((ulong)typeof(MpvPlayer).GetField("commandId",fields).GetValue(player)>reported,"The real WPF thumb drag still submits a native media seek");
        sidePreview.Clear();await PreviewSession.DrainAsync();check(player.Completion.IsCompletedSuccessfully,"Timeline testing releases the actual native media player");

        string model=WriteTimelineModel114(root);
        Settings.DisabledPreviewModules.Remove("f3d");
        await S1Verification.Navigate(Left,root);SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(model,StringComparison.OrdinalIgnoreCase));PreviewFollow();
        await sidePreview.Completion;await Settled();
        check(sidePreview.State=="ready"&&sidePreview.ViewerForVerification is F3dViewer,"The actual animated model is ready for timeline interaction: "+sidePreview.State+" "+sidePreview.ModuleId+" "+(PreviewBody.Content as TextBlock)?.Text);
        var renderer=(F3dViewer)sidePreview.ViewerForVerification;
        var animation=PreviewChildren<Slider>(PreviewBody).Single();
        byte[] initial=renderer.LastFrame.Pixels.ToArray();
        SetPoint(animation,3);await renderer.RenderCompletion;
        check(Math.Abs((double)typeof(F3dViewer).GetField("animationTime",fields).GetValue(renderer)-3)<.001&&!initial.SequenceEqual(renderer.LastFrame.Pixels),"Paused model timeline selection evaluates and renders the native scene at its new time");
        var play=S3VerificationButtons(PreviewBody).Single(button=>AutomationProperties.GetName(button)=="Play");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var previous=renderer.RenderCompletion;SetPoint(animation,6);
        check(renderer.RenderCompletion!=previous,"A playing model immediately submits the timeline point selection to its renderer");
        await renderer.RenderCompletion;
        check((bool)typeof(F3dViewer).GetField("playing",fields).GetValue(renderer)&&Math.Abs((double)typeof(F3dViewer).GetField("playbackStart",fields).GetValue(renderer)-6)<.001,"A playing model re-anchors its animation clock at the requested time");
        var next=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Presented(F3dViewer.Frame frame)=>next.TrySetResult();
        renderer.FramePresented+=Presented;try{await next.Task;}finally{renderer.FramePresented-=Presented;}
        double continued=(double)typeof(F3dViewer).GetField("animationTime",fields).GetValue(renderer);
        check(continued>=6&&continued<8,"Following native animation frames continue from the seek point instead of the old clock");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        sidePreview.Clear();await PreviewSession.DrainAsync();check(!renderer.WorkerAlive,"Animated timeline cleanup stops its native worker and frame subscription");

        foreach(string mode in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=mode;Appearance.Apply();
            var release=new Updater.Release(new Version(1,1,14),"v1.1.14","Release notes.","",128*1024*1024,"");
            var dialog=new UpdateDialog(this,release);HiddenTrialPresentation.Prepare(dialog);
            ((FrameworkElement)dialog.Content).LayoutTransform=new ScaleTransform(scale,scale);dialog.Width=520*scale;
            dialog.Show();dialog.BeginDownload();dialog.Height=240*scale;
            dialog.ReportDownload(new("Downloading",32*1024*1024,128*1024*1024));
            await Settled();await S1Verification.Snapshot(dialog,root,$"update-progress-{mode}-{scale*100:0}");
            var status=(TextBlock)dialog.FindName("Status");var bar=(ProgressBar)dialog.FindName("Progress");
            check(bar.Value==.25&&!bar.IsIndeterminate&&status.Text.Contains("25%"),"The themed update popup reports measured download progress: "+mode+" "+scale);
            check(!PreviewChildren<Button>(dialog).Any(button=>button.IsVisible&&(button.ActualWidth<=0||button.ActualHeight<=0)),"Update popup actions retain usable bounds: "+mode+" "+scale);
            dialog.ReportDownload(new("Checking update",0,0));check(bar.IsIndeterminate&&status.Text=="Checking update…","Checksum verification has a named indeterminate phase, not simulated percentage");
            dialog.Close();
        }
        await VerifyDownload114(root,check);
        string[] args=Updater.InstallationArguments(123,456,@"D:\Tools\Pathweft\");
        check(args.Contains("/VERYSILENT")&&args.Contains("/RESTARTAPP=1")&&args.Contains("/WAITPID=123")&&args.Contains("/WAITCREATED=00000000000001C8")&&args.Any(value=>value.StartsWith("/UPDATEACCENT=")),"The real update handoff retains exact process identity, restart intent and the current palette");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Timeline and updater UI checks perform no foreground takeover");
    }
    static string WriteTimelineModel114(string root)
    {
        float[] data={-1,-1,0,1,-1,0,0,1,0,0,10,0,0,0,2,0,0};
        File.WriteAllBytes(Path.Combine(root,"timeline.bin"),data.SelectMany(BitConverter.GetBytes).ToArray());
        string path=Path.Combine(root,"timeline.gltf");
        File.WriteAllText(path,"""
        {"asset":{"version":"2.0"},"buffers":[{"uri":"timeline.bin","byteLength":68}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":8},{"buffer":0,"byteOffset":44,"byteLength":24}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-1,-1,0],"max":[1,1,0]},{"bufferView":1,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[10]},{"bufferView":2,"componentType":5126,"count":2,"type":"VEC3"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0,"animations":[{"samplers":[{"input":1,"output":2}],"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}]}]}
        """);return path;
    }
    static async Task VerifyDownload114(string root,Action<bool,string> check)
    {
        var bytes=new byte[1024*1024];RandomNumberGenerator.Fill(bytes);
        async Task Run(bool badHash,bool cancel)
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            using var cancelled=new CancellationTokenSource();
            var server=Task.Run(async()=>
            {
                using var client=await listener.AcceptTcpClientAsync();using var stream=client.GetStream();
                using var request=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                while(await request.ReadLineAsync() is {Length:>0}){}
                try{await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));await stream.WriteAsync(bytes);}
                catch(IOException)when(cancel){}
            });
            var release=new Updater.Release(new Version(0,0,114),"fixture","",$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/setup.exe",bytes.Length,badHash?new string('0',64):Convert.ToHexString(SHA256.HashData(bytes)));
            var reports=new List<Updater.DownloadProgress>();
            var progress=new Progress<Updater.DownloadProgress>(item=>{reports.Add(item);if(cancel&&item.Received>0)cancelled.Cancel();});
            string target=Path.Combine(Path.GetTempPath(),"Pathweft-0.0.114-setup.exe");bool rejected=false;
            try
            {
                try{await Updater.Download(release,progress,cancelled.Token);}catch(Exception error)when((badHash&&error is IOException)||(cancel&&error is OperationCanceledException)){rejected=true;}
                await server;
                await Application.Current.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if(badHash||cancel)check(rejected&&!File.Exists(target),"A real "+(cancel?"cancelled":"checksum-rejected")+" download discards its owned partial installer and cannot deploy");
                else check(File.ReadAllBytes(target).SequenceEqual(bytes)&&reports.Any(item=>item.Stage=="Checking update")&&reports.Any(item=>item.Received==bytes.Length),"Real HTTP download, measured byte progress and checksum verification preserve the installer bytes");
            }
            finally{listener.Stop();}
        }
        await Run(false,false);await Run(true,false);await Run(false,true);
    }
}
#endif
