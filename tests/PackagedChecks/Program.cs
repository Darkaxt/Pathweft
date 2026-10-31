using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer;

// Independent consumer of an extracted, hash-checked delivered application.
// This probe is copied beside the verification copy, never into a release asset.
// It creates no visible window, global hook, clipboard or default-handler change.
sealed class PackageProbe
{
    static readonly BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static readonly Assembly Product=typeof(App).Assembly;
    static string root,known;
    readonly List<object> checks=new();
    MainWindow owner;
    object session;
    [STAThread] static void Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--native-info")
        {
            string target=Path.GetFullPath(args[1]);
            if(!target.StartsWith(@"D:\Temp\pathweft-s8-delivery\verification-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned metadata output required.");
            WriteNativeInfo(target);return;
        }
        if(args.Length==2&&args[0]=="--engine-state")
        {
            string target=Path.GetFullPath(args[1]);
            if(!target.StartsWith(@"D:\Temp\pathweft-s8-delivery\verification-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned diagnostics output required.");
            var window=FindWindowW("EVERYTHING_TASKBAR_NOTIFICATION",null);
            bool before=window!=IntPtr.Zero&&IsHungAppWindow(window);
            var library=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"Everything64.dll"));
            try
            {
                bool loaded=Marshal.GetDelegateForFunctionPointer<NativeBool>(NativeLibrary.GetExport(library,"Everything_IsDBLoaded"))();
                var state=new{window=window.ToInt64(),hungBefore=before,databaseLoaded=loaded,hungAfter=window!=IntPtr.Zero&&IsHungAppWindow(window)};
                File.WriteAllText(Path.Combine(target,"engine-state.json"),JsonSerializer.Serialize(state));Console.WriteLine(JsonSerializer.Serialize(state));
            }
            finally{NativeLibrary.Free(library);}
            return;
        }
        if(args.Length==3&&args[0]=="--wait-owner")
        {
            if(!args.Skip(1).All(name=>name.StartsWith("Local\\Pathweft-S8-Wait-",StringComparison.Ordinal)))throw new ArgumentException("Unexpected test signals.");
            using var ready=EventWaitHandle.OpenExisting(args[1]);using var release=EventWaitHandle.OpenExisting(args[2]);
            ready.Set();release.WaitOne();return;
        }
        if(args.Length!=2)throw new ArgumentException("An owned fixture root and known indexed file are required.");
        root=Path.GetFullPath(args[0]);known=Path.GetFullPath(args[1]);
        if(!root.StartsWith(@"D:\Temp\pathweft-s8-delivery\verification-",StringComparison.OrdinalIgnoreCase)||!File.Exists(known))throw new ArgumentException("Unexpected verification scope.");
        var dispatcher=Dispatcher.CurrentDispatcher;
        var posted=new List<DispatcherOperation>();
        void Posted(object sender,DispatcherHookEventArgs e){if(e.Operation.Priority==DispatcherPriority.Send)posted.Add(e.Operation);}
        dispatcher.Hooks.OperationPosted+=Posted;
        App app;
        try{app=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};}
        finally{dispatcher.Hooks.OperationPosted-=Posted;}
        // WPF Application's constructor posts its startup as the first Send
        // operation. Retain and cancel that exact pending bootstrap before
        // pumping the dispatcher. Any unexpected startup shape fails closed.
        // Keep the real App type: generated BAML requires its component and
        // resource to belong to the same assembly, so a derived test App is invalid.
        if(posted.Count!=1||!posted[0].Abort())throw new InvalidOperationException("The owned WPF startup operation could not be isolated before dispatch.");
        app.InitializeComponent();
        app.Exit+=(_,_)=>app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        var probe=new PackageProbe();
        app.Dispatcher.BeginInvoke(()=>_ = probe.RunAsync(app));
        Dispatcher.Run();
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr NativeText();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)][return:MarshalAs(UnmanagedType.Bool)]delegate bool NativeBool();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowW(string name,string title);
    [DllImport("user32.dll")]static extern bool IsHungAppWindow(IntPtr window);
    static string NativeTextValue(string library,string export)
    {
        var handle=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"F3D/bin",library),Product,DllImportSearchPath.UseDllDirectoryForDependencies|DllImportSearchPath.System32);
        try{return Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<NativeText>(NativeLibrary.GetExport(handle,export))());}
        finally{NativeLibrary.Free(handle);}
    }
    static void WriteNativeInfo(string directory)
    {
        var info=new{ffmpeg=NativeTextValue("avutil-61.dll","av_version_info"),configuration=NativeTextValue("avcodec-63.dll","avcodec_configuration"),license=NativeTextValue("avcodec-63.dll","avcodec_license")};
        File.WriteAllText(Path.Combine(directory,"native-info.json"),JsonSerializer.Serialize(info,new JsonSerializerOptions{WriteIndented=true}));
        if(info.ffmpeg!="9.0.1"||!info.configuration.Contains("--enable-libopenh264")||!info.configuration.Contains("--enable-shared")||info.configuration.Contains("--enable-gpl")||info.license!="LGPL version 2.1 or later")throw new InvalidOperationException("The packaged codec configuration differs from the corresponding source/notices.");
        Console.WriteLine(JsonSerializer.Serialize(info));
    }
    static Type Type(string name)=>Product.GetType("FileExplorer."+name,true);
    static object Call(Type type,object target,string method,params object[] args)
    {
        var member=type.GetMethod(method,Flags)??throw new MissingMethodException(type.FullName,method);
        try{return member.Invoke(target,args.Concat(Enumerable.Repeat(System.Type.Missing,member.GetParameters().Length-args.Length)).ToArray());}
        catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static object Get(object target,string name)=>target.GetType().GetProperty(name,Flags).GetValue(target);
    void Check(bool success,string criterion)
    {
        checks.Add(new{criterion,passed=success});Console.WriteLine(criterion+": "+success);
        File.WriteAllText(Path.Combine(root,"phase.json"),JsonSerializer.Serialize(new{criterion,success}));
        if(!success)throw new InvalidOperationException(criterion);
    }
    async Task RunAsync(App app)
    {
        int result=1;
        try
        {
            Check(app.Windows.Count==0,"The owned WPF startup operation was cancelled before dispatch and creates no application window");
            Settings.Load();
            Check(Path.GetFullPath(Settings.Dir).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase),"The delivered assembly uses only the explicit verification profile");
            Call(Type("Appearance"),null,"Initialize");
            using var deliveredManifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"release-manifest.json")));
            Check(Product.GetName().Version.ToString(3)==deliveredManifest.RootElement.GetProperty("version").GetString()&&Product.GetType("FileExplorer.S8ShellTrial")==null&&Product.GetType("FileExplorer.HiddenTrialPresentation")==null&&Product.GetType("FileExplorer.MainWindow").GetMethod("VerifyAppearanceCorrection",Flags)==null,"Delivered version and absence of Debug trial hooks");

            bool searchPassed=false;
            try
            {
                var clientType=Type("Search.EverythingSearchClient");
                var client=clientType.GetProperty("Shared",Flags).GetValue(null);
                string expression="path:\""+Path.GetDirectoryName(known)+"\" \""+Path.GetFileName(known)+"\"";
                var query=(Task)Call(clientType,client,"QueryAsync",expression,"",0u,200u,"Name",false,CancellationToken.None);
                await query;
                var page=Get(query,"Result");
                var paths=((IEnumerable)Get(page,"Hits")).Cast<object>().Select(hit=>(string)Get(hit,"Path")).ToArray();
                Check(paths.Contains(known,StringComparer.OrdinalIgnoreCase),"Packaged Everything SDK queries the real existing index and finds the exact known file");
                checks.Add(new{engine=Get(page,"Engine"),query=expression});searchPassed=true;
            }
            catch(Exception error)
            {
                // Preserve failed search acceptance while completing independent
                // archive/transfer/preview checks without changing the live indexer.
                checks.Add(new{criterion="Packaged real-index search",passed=false,failure=error.ToString()});
                Console.Error.WriteLine(error);
            }

            var source=Directory.CreateDirectory(Path.Combine(root,"source")).FullName;
            var destination=Directory.CreateDirectory(Path.Combine(root,"destination")).FullName;
            var payload=Path.Combine(source,"payload.txt");
            File.WriteAllText(payload,"Packaged copy, native archive and preview fixture.");
            Settings.Verify=true;
            var job=Ferry.Submit(new[]{payload},destination,false);await job.Completion;
            Check(job.Successful&&File.ReadAllBytes(payload).SequenceEqual(File.ReadAllBytes(Path.Combine(destination,"payload.txt"))),"Delivered transfer engine copies and verifies actual content");

            var archive=Type("Archive");
            foreach(var extension in new[]{".zip",".7z",".rar"})
            {
                string container=Path.Combine(root,"native"+extension);
                var files=Call(archive,null,"Files",new[]{payload},"");
                await Task.Run(()=>Call(archive,null,"Create",container,files));
                await Task.Run(()=>Call(archive,null,"Rename",container,"","payload.txt","renamed.txt"));
                var listed=((IEnumerable)Call(archive,null,"List",container,"")).Cast<object>().Select(item=>(string)Get(item,"Name")).ToArray();
                var extraction=Directory.CreateDirectory(Path.Combine(root,"extracted"+extension)).FullName;
                Call(archive,null,"Extract",container,new[]{"renamed.txt"},"",extraction);
                Check(listed.SequenceEqual(new[]{"renamed.txt"})&&File.ReadAllBytes(Path.Combine(extraction,"renamed.txt")).SequenceEqual(File.ReadAllBytes(payload)),"Delivered native "+extension+" create/rename/read/extract preserves bytes");
            }

            owner=new MainWindow(); // construct real controls/resources, never show
            var left=(PaneView)owner.FindName("Left");
            Check(left.FindName("AddressField") is FrameworkElement&&left.FindName("SearchScope")==null&&left.FindName("SearchBar")==null&&left.FindName("SearchSubmit")==null&&new[]{"MatchCase","MatchWord","MatchPath","MatchRegex"}.All(name=>left.FindName(name)!=null),"Delivered controls contain the unified field and four match switches, without the retired search row");
            var settings=new SettingsWindow(owner);
            Check(settings.FindName("AppearanceCard") is FrameworkElement,"Delivered Settings starts with Appearance and keeps documentation in Help");
            settings.Close();
            var target=new ContentControl();
            var previewType=Type("Preview.PreviewSession");
            session=Activator.CreateInstance(previewType,Flags,null,new object[]{owner,target,(Action<string>)(_=>{}),false},null);
            var markdown=Path.Combine(root,"preview.md");File.WriteAllText(markdown,"# Packaged Markdown\n\nReadable **content**.");
            Call(previewType,session,"Show",new Entry{Name="preview.md",Path=markdown});await (Task)Get(session,"Completion");
            Check((string)Get(session,"State")=="ready"&&(string)Get(session,"ModuleId")=="markdown"&&target.Content!=null,"Delivered internal Markdown module renders through the actual preview session");

            string model=Path.Combine(root,"triangle.glb");WriteTriangle(model);
            Settings.DisabledPreviewModules.Remove("f3d");
            Call(previewType,session,"Show",new Entry{Name="triangle.glb",Path=model});await (Task)Get(session,"Completion");
            Check((string)Get(session,"State")=="ready"&&(string)Get(session,"ModuleId")=="f3d","Delivered F3D DLL and native dependencies render an embedded GLB through the actual module");
            var viewer=previewType.GetField("viewer",Flags).GetValue(session);
            var frame=Get(viewer,"LastFrame");var png=(byte[])Get(frame,"Png");
            Check((bool)Get(frame,"Offscreen")&&png.Length>1000,"Delivered native preview produces a real offscreen image");
            File.WriteAllBytes(Path.Combine(root,"packaged-f3d.png"),png);
            WriteNativeInfo(root);
            ((IDisposable)session).Dispose();session=null;await (Task)Call(previewType,null,"DrainAsync");
            Check(!(bool)Get(viewer,"WorkerAlive"),"The delivered F3D render worker exits after preview cleanup");
            Check(app.Windows.Cast<Window>().All(window=>!window.IsVisible),"Package verification never displays any WPF window");
            result=searchPassed?0:1;
        }
        catch(Exception error){checks.Add(new{failure=error.ToString()});Console.Error.WriteLine(error);}
        finally
        {
            if(session is IDisposable live)live.Dispose();
            await (Task)Call(Type("Preview.PreviewSession"),null,"DrainAsync");
            owner?.Quit();
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{result,pid=Environment.ProcessId,fixture=root,runtime=Environment.Version.ToString(),product=Product.Location,checks},new JsonSerializerOptions{WriteIndented=true}));
            Environment.ExitCode=result;app.Shutdown(result);
        }
    }
    static void WriteTriangle(string path)
    {
        const string json="""
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]}],"materials":[{"doubleSided":true,"pbrMetallicRoughness":{"baseColorFactor":[0.85,0.15,0.1,1],"metallicFactor":0,"roughnessFactor":0.6}}],"buffers":[{"byteLength":44}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962},{"buffer":0,"byteOffset":36,"byteLength":6,"target":34963}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-1,-1,0],"max":[1,1,0]},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
        """;
        var text=Encoding.UTF8.GetBytes(json.PadRight((json.Length+3)&~3));
        using var file=File.Create(path);using var writer=new BinaryWriter(file);
        writer.Write(0x46546C67u);writer.Write(2u);writer.Write((uint)(12+8+text.Length+8+44));
        writer.Write((uint)text.Length);writer.Write(0x4E4F534Au);writer.Write(text);
        writer.Write(44u);writer.Write(0x004E4942u);
        foreach(float value in new[]{-1f,-1f,0f,1f,-1f,0f,0f,1f,0f})writer.Write(value);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)2);writer.Write((ushort)0);
    }
}
