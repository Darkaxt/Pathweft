using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern uint ExtractIconExW(string file,int index,out IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    void VerifyIcon(System.Windows.Media.Imaging.BitmapSource source,string criterion)
    {
        var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(source,System.Windows.Media.PixelFormats.Bgra32,null,0);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        int yellow=0,brown=0;
        for(int i=0;i<pixels.Length;i+=4){int b=pixels[i],g=pixels[i+1],r=pixels[i+2],a=pixels[i+3];if(a>200&&r>180&&g>110&&b<130)yellow++;if(a>200&&r>40&&r<170&&g<110&&b<65)brown++;}
        Check(pixels[3]==0&&pixels[^1]==0&&yellow>bitmap.PixelWidth*bitmap.PixelHeight/4&&brown>bitmap.PixelWidth*bitmap.PixelHeight/30,criterion);
    }
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
    // Force the receiving WPF object through the native data contract, rather
    // than letting it reuse the source's managed IDataObject directly.
    sealed class NativeDataConsumer(System.Runtime.InteropServices.ComTypes.IDataObject source):System.Runtime.InteropServices.ComTypes.IDataObject
    {
        internal int Reads;
        public void GetData(ref FORMATETC format,out STGMEDIUM medium){Reads++;source.GetData(ref format,out medium);}
        public void GetDataHere(ref FORMATETC format,ref STGMEDIUM medium)=>source.GetDataHere(ref format,ref medium);
        public int QueryGetData(ref FORMATETC format)=>source.QueryGetData(ref format);
        public int GetCanonicalFormatEtc(ref FORMATETC format,out FORMATETC result)=>source.GetCanonicalFormatEtc(ref format,out result);
        public void SetData(ref FORMATETC format,ref STGMEDIUM medium,bool release)=>source.SetData(ref format,ref medium,release);
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)=>source.EnumFormatEtc(direction);
        public int DAdvise(ref FORMATETC format,ADVF flags,IAdviseSink sink,out int connection)=>source.DAdvise(ref format,flags,sink,out connection);
        public void DUnadvise(int connection)=>source.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA values)=>source.EnumDAdvise(out values);
    }
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
            string downloads=(string)Call(typeof(MainWindow),null,"DownloadsFolder");
            Check(((Panel)owner.FindName("Favorites")).Children.OfType<Button>().Any(button=>button.ToolTip as string==downloads),"Delivered Favorites contains the actual Windows Downloads location");
            int exports=0;
            string[] archivePaths={Path.Combine(root,"native.zip","renamed.txt")};
            var dragType=Type("ArchiveDragData");
            var lazyDrag=Activator.CreateInstance(dragType,Flags,null,new object[]{archivePaths,(Func<string[]>)(()=>{exports++;return new[]{payload};})},null);
            var drag=new DataObject(lazyDrag);
            Check(((string[])Call(dragType,null,"Files",drag)).SequenceEqual(archivePaths)&&exports==0,"Delivered archive drag identities are available without materializing export files");
            var bridge=new NativeDataConsumer((System.Runtime.InteropServices.ComTypes.IDataObject)drag);
            var received=new DataObject(bridge);
            Check(((string[])Call(dragType,null,"Files",received)).SequenceEqual(archivePaths)&&exports==0&&bridge.Reads>0,"Native OLE round-trip preserves internal archive identities without requesting export");
            Check(((string[])received.GetData(DataFormats.FileDrop)).SequenceEqual(new[]{payload})&&((string[])received.GetData(DataFormats.FileDrop)).SequenceEqual(new[]{payload})&&exports==1,"Delivered Windows file-drop export stays compatible across native OLE and materializes once on demand");
            new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle(); // an owned, never-shown HWND is required by WPF's Owner contract
            using(var icon=Application.GetResourceStream(new Uri("/Pathweft;component/app.ico",UriKind.Relative)).Stream)
                VerifyIcon(System.Windows.Media.Imaging.BitmapDecoder.Create(icon,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0],"Delivered WPF app icon has a transparent background, yellow folder and contrasting brown marks");
            IntPtr largeIcon=IntPtr.Zero,smallIcon=IntPtr.Zero;
            try
            {
                uint extracted=ExtractIconExW(Path.Combine(AppContext.BaseDirectory,"Pathweft.exe"),0,out largeIcon,out smallIcon,1);
                Check(extracted==2&&largeIcon!=IntPtr.Zero&&smallIcon!=IntPtr.Zero,"The delivered executable exposes both native Windows icon sizes");
                VerifyIcon(System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(largeIcon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()),"The native executable/shortcut icon uses the same transparent yellow-folder identity");
            }
            finally{if(largeIcon!=IntPtr.Zero)DestroyIcon(largeIcon);if(smallIcon!=IntPtr.Zero)DestroyIcon(smallIcon);}
            string rejectedHtml=Path.Combine(root,"rejected.html");File.WriteAllBytes(rejectedHtml,new byte[]{0,1,2,3});
            Check(!(bool)Call(Type("EditorWindow"),null,"TryOpen",rejectedHtml)&&!(bool)Type("EditorWindow").GetProperty("HasOpenWindow",Flags).GetValue(null),"Delivered editor declines binary HTML without creating a window");
            var left=(PaneView)owner.FindName("Left");
            left.Navigate(PaneView.ThisPC);await (Task)typeof(PaneView).GetField("driveListingWork",Flags).GetValue(left);
            var driveRows=left.Items.Where(e=>e.IsDrive).ToArray();
            Check(driveRows.Any(e=>e.HasDriveCapacity)&&driveRows.Where(e=>e.HasDriveCapacity).All(e=>e.DriveFree>=0&&e.DriveFree<=e.DriveTotal),"Delivered This PC reads real drive capacities and free-space values");
            var columnKeys=(IDictionary)typeof(PaneView).GetField("colKey",Flags).GetValue(left);
            string[] CurrentColumns()=>((GridView)((ListView)left.FindName("List")).View).Columns.Select(c=>(string)columnKeys[c]).ToArray();
            Check(CurrentColumns().Contains("Capacity")&&!CurrentColumns().Contains("Version"),"Delivered drive view contains populated drive columns without empty file columns");
            Check(driveRows.Where(e=>e.HasDriveCapacity).All(e=>e.FreeText.Contains('%')),"Delivered drive free-space values include measured percentages");
            Check(driveRows.All(e=>e.HasDriveCapacity),"Delivered This PC omits unavailable reader slots");
            foreach(var cloud in driveRows.Where(e=>e.DriveFormat=="Cloud"))
                Check(cloud.DriveConnection is "Google Drive" or "Tresorit"&&cloud.DriveRawFormat.Length>0,"Delivered provider registration identifies the actual cloud mount: "+cloud.Path);
            foreach(var entry in driveRows.Where(e=>e.Path is "E:\\" or "G:\\"))
                Check(entry.DriveConnection=="USB · 10 Gbps","Delivered native USB query reports the actual external link: "+entry.Path);
            var folderArrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void FolderArrived(){if(left.Dir==root&&left.Listed&&left.Items.Any(e=>!e.IsDrive))folderArrived.TrySetResult();}
            left.StatsChanged+=FolderArrived;
            try{left.Navigate(root);FolderArrived();await folderArrived.Task;}finally{left.StatsChanged-=FolderArrived;}
            await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(!CurrentColumns().Contains("Capacity")&&!CurrentColumns().Contains("Version")&&CurrentColumns().Contains("Size"),"Delivered folder navigation restores only populated file columns");
            var sidebarMask=(System.Windows.Media.LinearGradientBrush)Call(typeof(MainWindow),null,"SidebarEdgeMask",200d,true,true,false);
            Check(owner.FindName("SidebarScroll") is ScrollViewer&&sidebarMask.MappingMode==System.Windows.Media.BrushMappingMode.Absolute&&sidebarMask.EndPoint.Y==200&&sidebarMask.GradientStops[0].Color.A==0&&sidebarMask.GradientStops[^1].Color.A==0&&Call(typeof(MainWindow),null,"SidebarEdgeMask",200d,true,true,true)==null,"Delivered sidebar owns a viewport-coordinate fade and preserves high-contrast opacity");
            Check(left.FindName("AddressField") is FrameworkElement&&left.FindName("SearchScope")==null&&left.FindName("SearchBar")==null&&left.FindName("SearchSubmit")==null&&new[]{"MatchCase","MatchWord","MatchPath","MatchRegex"}.All(name=>left.FindName(name)!=null),"Delivered controls contain the unified field and four match switches, without the retired search row");
            var settings=new SettingsWindow(owner);
            Check(settings.FindName("OpenSettingsFolder") is Button&&settings.FindName("ProfileText")==null,"Delivered About contains the settings-folder action without the old Diagnostics expander");
            Check(settings.FindName("AppearanceCard") is FrameworkElement,"Delivered Settings starts with Appearance and keeps documentation in Help");
            var restore=(Button)settings.FindName("RestoreFolderHandlers");
            Check(restore.Content is TextBlock label&&label.Text=="Restore previous folder handlers"&&label.TextWrapping==TextWrapping.Wrap&&double.IsPositiveInfinity(restore.MaxWidth),"Delivered restore action has a wrapping label and no tab-width cap");
            settings.Close();
            var widgetType=Type("SearchWidgetWindow");
            var widget=(Window)Activator.CreateInstance(widgetType,Flags,null,new object[]{owner},null);
            try
            {
                var widgetPane=(PaneView)Get(widget,"Pane");
                var widgetList=(ListView)widgetPane.FindName("List");
                Check(widget.WindowStyle==WindowStyle.None&&widget.ShowInTaskbar==false&&widgetPane.Dir==PaneView.ThisPC&&!widgetPane.Items.Any()&&((GridView)widgetList.View).Columns.Count==4&&widgetType.GetField("Destination",Flags)==null,"Delivered popup has only its global query and four-column result view, without the former title/destination/actions");
                var input=(TextBox)widgetPane.FindName("FilterBox");
                input.Text="path:\""+Path.GetDirectoryName(known)+"\" \""+Path.GetFileName(known)+"\"";
                await (Task)Get(widgetPane,"SearchCompletion");
                Check(widgetPane.Items.Any(entry=>entry.Path.Equals(known,StringComparison.OrdinalIgnoreCase))&&ReferenceEquals(widgetList.ItemContainerStyle,widgetPane.FindResource("WidgetResultRow")),"Delivered compact popup queries the actual index as text changes and retains its plain result style");
                input.Text="";await (Task)Get(widgetPane,"SearchCompletion");
                Check(!widgetPane.Items.Any(),"Clearing the delivered popup empties results instead of listing drives");
            }
            finally{Call(widgetType,widget,"Shutdown");}
            var target=new ContentControl();
            var previewType=Type("Preview.PreviewSession");
            session=Activator.CreateInstance(previewType,Flags,null,new object[]{owner,target,(Action<string>)(_=>{}),false},null);
            var markdown=Path.Combine(root,"preview.md");File.WriteAllText(markdown,"# Packaged Markdown\n\nReadable **content**.");
            Call(previewType,session,"Show",new Entry{Name="preview.md",Path=markdown});await (Task)Get(session,"Completion");
            Check((string)Get(session,"State")=="ready"&&(string)Get(session,"ModuleId")=="markdown"&&target.Content!=null,"Delivered internal Markdown module renders through the actual preview session");
            var appHost=Path.Combine(AppContext.BaseDirectory,"Pathweft.exe");
            Call(previewType,session,"Show",new Entry{Name="Pathweft.exe",Path=appHost});await (Task)Get(session,"Completion");
            Check((string)Get(session,"State")=="ready"&&(string)Get(session,"ModuleId")=="executable"&&target.Content is ScrollViewer,"Delivered executable preview renders metadata through its actual module");
            Call(previewType,session,"Show",new Entry{Name="native.zip",Path=Path.Combine(root,"native.zip")});await (Task)Get(session,"Completion");
            var archiveTree=target.Content as TreeView;
            Check((string)Get(session,"State")=="ready"&&archiveTree!=null&&archiveTree.Items.OfType<TreeViewItem>().Any(),"Delivered archive preview is a native themed tree rather than the old flat text dump");

            string model=Path.Combine(root,"triangle.glb");WriteTriangle(model);
            Settings.DisabledPreviewModules.Remove("f3d");
            Call(previewType,session,"Show",new Entry{Name="triangle.glb",Path=model});await (Task)Get(session,"Completion");
            Check((string)Get(session,"State")=="ready"&&(string)Get(session,"ModuleId")=="f3d","Delivered F3D DLL and native dependencies render an embedded GLB through the actual module");
            var viewer=previewType.GetField("viewer",Flags).GetValue(session);
            var frame=Get(viewer,"LastFrame");var png=(byte[])Get(frame,"Png");
            Check((bool)Get(frame,"Offscreen")&&png.Length>1000,"Delivered native preview produces a real offscreen image");
            var bitmap=(System.Windows.Media.Imaging.BitmapSource)Get(frame,"Bitmap");
            var bgra=new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap,System.Windows.Media.PixelFormats.Bgra32,null,0);var alpha=new byte[4];bgra.CopyPixels(new Int32Rect(0,0,1,1),alpha,4,0);
            Check(alpha[3]==0,"Delivered model renderer composites transparently into the themed preview");
            File.WriteAllBytes(Path.Combine(root,"packaged-f3d.png"),png);
            Check(((string[])Type("Preview.F3dViewer").GetProperty("SupportedExtensions",Flags).GetValue(null)).Contains(".obj"),"Delivered F3D selection discovers its actual bundled model readers");
            Call(viewer.GetType(),viewer,"HandleKey",System.Windows.Input.Key.Right);await (Task)Get(viewer,"RenderCompletion");
            var moved=(byte[])Get(Get(viewer,"LastFrame"),"Png");
            Check(!png.SequenceEqual(moved),"Delivered native keyboard camera interaction changes the rendered model");
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
            if(owner!=null)
            {
                var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                owner.Closed+=(_,_)=>closed.TrySetResult();owner.Quit();
                await closed.Task; // the first Close waits for owned preview/archive work
            }
            if(owner!=null)
                Check(new[]{"Left","Right"}.All(name=>typeof(PaneView).GetField("watcher",Flags).GetValue(owner.FindName(name))==null&&!((DispatcherTimer)typeof(PaneView).GetField("debounce",Flags).GetValue(owner.FindName(name))).IsEnabled),"Closing the actual window releases both folder watchers and pending refresh timers before resource teardown");
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
