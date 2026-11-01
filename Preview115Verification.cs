#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPreview115(string root,Action<bool,string> check)
    {
        async Task Settled()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        async Task Select(string path)
        {
            await S1Verification.Navigate(Left,Path.GetDirectoryName(path));SetActive(Left);
            Left.List.SelectedItem=Left.Items.Single(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));PreviewFollow();
            await sidePreview.Completion;await Settled();
        }
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.Preview=true;ApplyPreview();
        Settings.DisabledPreviewModules.Remove("f3d");Settings.DisabledPreviewModules.Remove("html");
        string model=@"D:\Downloads\SNES_PAL_-_Super_Mario_World-31f4914a.glb";
        var analysis=GltfTimeline.Read(model);
        check(Math.Abs((analysis.EndFor(0,0,55.41666793823242)??0)-6.875)<.001,"Real SNES stored curves identify the6.875-second active range without altering the55.417-second native clip");
        string fixture=WriteCurve115(root,"hold",new[]{0f,2f,10f},new[]{0f,1f,1f});
        check(GltfTimeline.Read(fixture).EndFor(0,0,10)==2,"A held final pose is trimmed after its actual transition");
        check(GltfTimeline.Read(WriteCurve115(root,"slow",new[]{0f,5f,10f},new[]{0f,.0001f,.0002f})).EndFor(0,0,10)==null,"Small continuous movement is preserved through the full range");
        check(GltfTimeline.Read(WriteCurve115(root,"pause",new[]{0f,2f,7f,8f,10f},new[]{0f,1f,1f,2f,2f})).EndFor(0,0,10)==8,"Internal pauses and later movement remain intact");
        check(GltfTimeline.Read(WriteCurve115(root,"static",new[]{0f,10f},new[]{1f,1f})).EndFor(0,0,10)==null,"Static animation retains its full native range");
        string small=WriteCurve115(root,"small",new[]{0f,2f,10f},new[]{0f,.0001f,.0001f});
        var smallJson=JsonNode.Parse(File.ReadAllText(small));
        smallJson["accessors"][0]["min"]=JsonSerializer.SerializeToNode(new[]{-.001f,-.001f,0});smallJson["accessors"][0]["max"]=JsonSerializer.SerializeToNode(new[]{.001f,.001f,0});
        File.WriteAllText(small,smallJson.ToJsonString());
        check(GltfTimeline.Read(small).EndFor(0,0,10)==2,"A small animated part is judged against its own bounds");
        string multiple=WriteCurve115(root,"multiple",new[]{0f,2f,10f},new[]{0f,1f,1f});var multipleJson=JsonNode.Parse(File.ReadAllText(multiple));
        ((JsonArray)multipleJson["animations"]).Add(multipleJson["animations"][0].DeepClone());File.WriteAllText(multiple,multipleJson.ToJsonString());
        check(GltfTimeline.Read(multiple).EndFor(1,0,10)==2&&GltfTimeline.Read(multiple).EndFor(1,0,11)==null,"Analysis preserves clip identity and refuses a mismatched native range");
        string unknown=WriteCurve115(root,"cubic",new[]{0f,10f},new[]{0f,1f});var unknownJson=JsonNode.Parse(File.ReadAllText(unknown));
        unknownJson["animations"][0]["samplers"][0]["interpolation"]="CUBICSPLINE";File.WriteAllText(unknown,unknownJson.ToJsonString());
        check(GltfTimeline.Read(unknown).EndFor(0,0,10)==null,"Uninspectable cubic curves retain their native range");
        File.WriteAllText(Path.Combine(root,"invalid.gltf"),"{\"buffers\":[{\"uri\":null}],\"animations\":[{}]}");
        check(GltfTimeline.Read(Path.Combine(root,"invalid.gltf")).EndFor(0,0,10)==null,"Malformed metadata retains native range rather than breaking preview");

        await Select(fixture);
        check(sidePreview.State=="ready"&&sidePreview.ViewerForVerification is F3dViewer,"The real F3D module loads the smart timeline fixture");
        var renderer=(F3dViewer)sidePreview.ViewerForVerification;var timeline=PreviewChildren<Slider>(PreviewBody).Single();
        check(timeline.Maximum==2&&renderer.LastFrame.End==10,"Actual F3D keeps its full range while the themed timeline uses the active end");
        var option=(MenuItem)timeline.ContextMenu.Items[0];option.IsChecked=false;
        check(timeline.Maximum==10,"The actual timeline context-menu option restores the original duration");
        option.IsChecked=true;
        var peer=new SliderAutomationPeer(timeline);((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)).SetValue(1);
        await renderer.RenderCompletion;
        check(Math.Abs((double)typeof(F3dViewer).GetField("animationTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer)-1)<.001,"Smart timeline seeking submits the selected original animation time");
        var play=PreviewChildren<Button>(PreviewBody).Single(button=>AutomationProperties.GetName(button)=="Play");
        typeof(F3dViewer).GetField("animationTime",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(renderer,1.99);
        var looped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loop(F3dViewer.Frame frame){if((double)typeof(F3dViewer).GetField("animationTime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer)<1)looped.TrySetResult();}
        renderer.FramePresented+=Loop;play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await looped.Task;play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));renderer.FramePresented-=Loop;
        check(timeline.Maximum==2,"Native playback loops inside the meaningful range");
        sidePreview.Clear();await PreviewSession.DrainAsync();check(!renderer.WorkerAlive,"Smart timeline cleanup releases its real renderer");
        await Select(multiple);renderer=(F3dViewer)sidePreview.ViewerForVerification;
        PreviewChildren<ComboBox>(PreviewBody).Single().SelectedIndex=1;await renderer.RenderCompletion;
        check(PreviewChildren<Slider>(PreviewBody).Single().Maximum==2&&renderer.LastFrame.End==10,"Actual native clip switching retains its own smart range");
        await Select(model);renderer=(F3dViewer)sidePreview.ViewerForVerification;
        check(Math.Abs(PreviewChildren<Slider>(PreviewBody).Single().Maximum-6.875)<.001&&renderer.LastFrame.End>55,"The reported SNES renders with its actual smart timeline");
        sidePreview.Clear();await PreviewSession.DrainAsync();

        string document=Path.Combine(root,"static.html"),stylesheet=Path.Combine(root,"style.css"),picture=Path.Combine(root,"pixel.svg");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"segoeui.ttf"),Path.Combine(root,"preview.ttf"));
        using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        File.WriteAllText(stylesheet,$"@font-face{{font-family:PreviewFont;src:url('preview.ttf')}} body {{background:rgb(247,248,249);color:rgb(20,21,22);font:16px PreviewFont}} h1 {{color:rgb(32,96,160)}} p{{background-image:url(http://127.0.0.1:{port}/css-tracker)}}");
        File.WriteAllText(picture,"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32' fill='#228855'/></svg>");
        File.WriteAllText(document,"""
        <!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="style.css"></head>
        <body><h1>Static HTML preview</h1><p>Local styling and images.</p><img id="picture" src="pixel.svg">
        <script>window.pageScriptRan=true;fetch('https://example.invalid/leak')</script>
        <img src="https://example.invalid/tracker.png"><iframe src="https://example.invalid/frame"></iframe>
        <a id="outside" href="https://example.invalid/">Outside link</a><form action="https://example.invalid/post"><button>Submit</button></form>
        </body></html>
        """);
        File.AppendAllText(document,$"<img src='http://127.0.0.1:{port}/tracker'><iframe src='http://127.0.0.1:{port}/frame'></iframe>");
        async Task InspectHtml(string path,string name)
        {
            await Select(path);
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="html","Actual HTML preview loads its controlled document: "+name+" "+(PreviewBody.Content as TextBlock)?.Text);
            var viewer=(HtmlViewer)sidePreview.ViewerForVerification;var core=viewer.CoreForVerification;
            check(!core.Settings.IsScriptEnabled&&!core.Settings.IsWebMessageEnabled&&!core.Settings.AreHostObjectsAllowed,"HTML disables page scripts and all native/editor bridge access");
            string result=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","""{"expression":"JSON.stringify({title:document.querySelector('h1')?.textContent,color:getComputedStyle(document.querySelector('h1')).color,image:document.getElementById('picture').naturalWidth,script:typeof window.pageScriptRan})","returnByValue":true}""");
            using var outer=JsonDocument.Parse(result);using var state=JsonDocument.Parse(outer.RootElement.GetProperty("result").GetProperty("value").GetString());
            check(state.RootElement.GetProperty("title").GetString()=="Static HTML preview"&&state.RootElement.GetProperty("color").GetString()=="rgb(32, 96, 160)"&&state.RootElement.GetProperty("image").GetInt32()==32,"Actual browser renders document CSS and its local image: "+name);
            check(state.RootElement.GetProperty("script").GetString()=="undefined","Document JavaScript remains inactive: "+name);
            string font=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","""{"expression":"document.fonts.ready.then(()=>document.fonts.check('16px PreviewFont'))","awaitPromise":true,"returnByValue":true}""");
            using(var fontState=JsonDocument.Parse(font))check(fontState.RootElement.GetProperty("result").GetProperty("value").GetBoolean(),"Actual browser loads its checked local font: "+name);
            string before=core.Source;
            var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Navigation(object sender,CoreWebView2NavigationStartingEventArgs args){if(args.Cancel)blocked.TrySetResult();}
            core.NavigationStarting+=Navigation;core.Navigate("https://example.invalid/");await blocked.Task;core.NavigationStarting-=Navigation;
            check(core.Source==before,"External navigation is blocked before leaving the preview origin");
            using(var file=File.Create(Path.Combine(root,"html-"+name.Replace('.','-')+".png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            File.WriteAllText(Path.Combine(root,"tcp-"+name+".json"),JsonSerializer.Serialize(TcpOwners115(port)));
            string profile=viewer.ProfileForVerification;sidePreview.Clear();await PreviewSession.DrainAsync();
            check(viewer.Closed.IsCompletedSuccessfully&&!Directory.Exists(profile),"HTML selection cleanup observes browser exit and removes only its owned profile");
            int connections=0,received=0;
            while(listener.Pending())
            {using var client=await listener.AcceptTcpClientAsync();using var bytes=new MemoryStream();await client.GetStream().CopyToAsync(bytes);connections++;received+=(int)bytes.Length;File.WriteAllBytes(Path.Combine(root,"network-"+name+"-"+connections+".bin"),bytes.ToArray());}
            File.WriteAllText(Path.Combine(root,"network-"+name+".json"),JsonSerializer.Serialize(new{connections,received}));
            check(connections==0&&received==0,"Offline HTML makes no connection or HTTP request to the actual local listener");
        }
        await InspectHtml(document,"local.html");
        string archive=Path.Combine(root,"static.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        {
            foreach(var file in new[]{document,stylesheet,picture,Path.Combine(root,"preview.ttf")})zip.CreateEntryFromFile(file,"Takeout/"+Path.GetFileName(file));
        }
        await InspectHtml(archive+@"\Takeout\static.html","archive.html");
        check(!ArchiveContent.Retained().Any(record=>record.Source.StartsWith(archive,StringComparison.OrdinalIgnoreCase)),"HTML archive preview leaves no extracted working copies");
        var resources=new HtmlResources(document,document,CancellationToken.None);
        async Task Rejected(Uri uri)
        {bool denied=false;try{await resources.Read(uri);}catch(IOException){denied=true;}check(denied,"Static resource handler rejects "+uri.Scheme+" "+uri.AbsolutePath);}
        await Rejected(new Uri("https://example.invalid/tracker.png"));await Rejected(new Uri("file:///C:/Windows/win.ini"));await Rejected(new Uri("mailto:test@example.invalid"));
        await Rejected(new Uri("https://"+resources.Host+"/outside.txt"));await Rejected(new Uri("https://"+resources.Host+"/%2e%2e%5coutside.css"));
        var reparseDocument=new HtmlResources(@"C:\Documents and Settings\static.html",@"C:\Documents and Settings\static.html",CancellationToken.None);
        bool reparseDenied=false;try{await reparseDocument.Read(reparseDocument.Document);}catch(IOException error){reparseDenied=error.Message.Contains("filesystem links");}
        check(reparseDenied,"Existing Windows directory junctions cannot expose resources to HTML preview");
        using(var cancel=new CancellationTokenSource())
        {cancel.Cancel();var canceledResources=new HtmlResources(document,document,cancel.Token);bool cancelled=false;try{await canceledResources.Read(canceledResources.Document);}catch(OperationCanceledException){cancelled=true;}check(cancelled,"Resource cancellation is observed");}
        // The detached Space and search surfaces share this same session contract.
        var detached=new ContentControl();var parameters=new System.Windows.Interop.HwndSourceParameters("Pathweft hidden detached preview")
        {Width=700,Height=500,PositionX=(int)(SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth+8192),WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080};
        using var host=new System.Windows.Interop.HwndSource(parameters);host.RootVisual=detached;detached.Measure(new Size(700,500));detached.Arrange(new Rect(0,0,700,500));
        using(var isolated=new PreviewSession(this,detached,_=>{},false))
        {isolated.Show(new Entry{Name="static.html",Path=document});await isolated.Completion;check(isolated.State=="ready"&&isolated.ModuleId=="html","The shared detached preview surface renders HTML");isolated.Clear();await PreviewSession.DrainAsync();}
        host.RootVisual=null;
        using(var cancelled=new CancellationTokenSource())
        {
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var tasks=new List<Task>();
            var context=new ContextObject(cancelled.Token,()=>!cancelled.IsCancellationRequested,_=>started.TrySetResult(),task=>tasks.Add(task),false);
            var pending=new HtmlViewer(new Entry{Name="static.html",Path=document});pending.Prepare(document,context);var load=pending.View(document,context);
            await started.Task;cancelled.Cancel();pending.Cleanup();try{await load;}catch(OperationCanceledException){}
            await pending.Closed;check(pending.Closed.IsCompletedSuccessfully,"HTML cancelled before attachment releases initialization without a browser or a delay");
        }
        foreach(string mode in new[]{"dark","light"})
        {
            Settings.NativeTheme=true;Settings.NativeMode=mode;Appearance.Apply();await Select(document);await Settled();await S1Verification.Snapshot(this,root,"html-frame-"+mode);
            sidePreview.Clear();await PreviewSession.DrainAsync();
        }
        Settings.DisabledPreviewModules.Add("html");PreviewSession.RefreshAll();await Select(document);
        check(sidePreview.ModuleId=="text"&&PreviewBody.Content is TextBox,"Disabling HTML restores the existing source-text viewer: "+sidePreview.ModuleId+" "+sidePreview.State+" "+PreviewBody.Content?.GetType().Name);
        sidePreview.Clear();await PreviewSession.DrainAsync();Settings.DisabledPreviewModules.Remove("html");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Both preview features are checked without foreground takeover");
    }
    [System.Runtime.InteropServices.DllImport("iphlpapi.dll")]static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool order,int family,int kind,int reserved);
    static List<object> TcpOwners115(int port)
    {
        int size=0;GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,5,0);var data=System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
        try
        {
            uint result=GetExtendedTcpTable(data,ref size,false,2,5,0);if(result!=0)throw new IOException("TCP diagnostic failed: "+result);
            var rows=new List<object>();int count=System.Runtime.InteropServices.Marshal.ReadInt32(data);
            for(int i=0;i<count;i++)
            {
                var row=IntPtr.Add(data,4+i*24);int encoded=System.Runtime.InteropServices.Marshal.ReadInt32(row,16);int remotePort=((encoded&255)<<8)|((encoded>>8)&255);
                if(remotePort!=port)continue;
                int pid=System.Runtime.InteropServices.Marshal.ReadInt32(row,20);string process;
                try{process=System.Diagnostics.Process.GetProcessById(pid).ProcessName;}catch(ArgumentException){process="exited";}
                rows.Add(new{pid,process,remotePort,state=System.Runtime.InteropServices.Marshal.ReadInt32(row)});
            }
            return rows;
        }
        finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(data);}
    }
    static string WriteCurve115(string root,string name,float[] times,float[] positions)
    {
        string binary=name+".bin",path=Path.Combine(root,name+".gltf");
        float[] geometry={-1,-1,0,1,-1,0,0,1,0};var bytes=new List<byte>();
        foreach(float value in geometry.Concat(times).Concat(positions.SelectMany(x=>new[]{x,0f,0f})))bytes.AddRange(BitConverter.GetBytes(value));
        File.WriteAllBytes(Path.Combine(root,binary),bytes.ToArray());
        var document=new{asset=new{version="2.0"},buffers=new[]{new{uri=binary,byteLength=bytes.Count}},
            bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=36},new{buffer=0,byteOffset=36,byteLength=times.Length*4},new{buffer=0,byteOffset=36+times.Length*4,byteLength=positions.Length*12}},
            accessors=new object[]{new{bufferView=0,componentType=5126,count=3,type="VEC3",min=new[]{-1,-1,0},max=new[]{1,1,0}},new{bufferView=1,componentType=5126,count=times.Length,type="SCALAR",min=new[]{times[0]},max=new[]{times[^1]}},new{bufferView=2,componentType=5126,count=positions.Length,type="VEC3"}},
            meshes=new[]{new{primitives=new[]{new{attributes=new{POSITION=0}}}}},nodes=new[]{new{mesh=0}},scenes=new[]{new{nodes=new[]{0}}},scene=0,
            animations=new[]{new{samplers=new[]{new{input=1,output=2}},channels=new[]{new{sampler=0,target=new{node=0,path="translation"}}}}}};
        File.WriteAllText(path,JsonSerializer.Serialize(document));return path;
    }
}
#endif
