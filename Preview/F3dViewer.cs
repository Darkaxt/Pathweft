// libf3d's offscreen rendering example adapted to a cancellable WPF module.
// Copyright F3D contributors; BSD-3-Clause. See vendor/F3D/NOTICES.txt.
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Preview;

internal sealed class F3dViewer(MainWindow owner) : IViewer
{
    sealed record Request(string Operation, double X, double Y, TaskCompletionSource<Frame> Result);
    internal sealed record Frame(BitmapSource Bitmap,byte[] Pixels,double[] Camera,bool Offscreen,uint Animations,double Start,double End,long Sequence,double RenderMilliseconds,double TransferMilliseconds,double[] Up,double[] Focal,double InputMilliseconds,int InputSamples,int InputRenderCalls,double? SmartEnd)
    {
        // Export only on explicit diagnostic/screenshot demand, never per gesture.
        internal byte[] Png {get{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}}
    }
    static readonly object pluginLock = new();
    static readonly Lazy<HashSet<string>> extensions = new(LoadReaders);
    BlockingCollection<Request> requests;
    Thread worker;
    ContextObject context;
    string path;
    volatile bool stopped;
    long renderedSequence,presentedSequence;
    int renderWidth=960,renderHeight=720;
    readonly bool light=Appearance.Native&&Appearance.Light;
    Border viewport;
    Point lastPointer;
    MouseButton? dragging;
    Action<string, double, double> queue;
    EventHandler animationTick;
    EventHandler resizeHandler;
    bool playing;
    double animationTime;
    readonly System.Diagnostics.Stopwatch animationClock = new();
    double playbackStart;
    internal Task RenderCompletion { get; private set; } = Task.CompletedTask;
    internal Frame LastFrame { get; private set; }
    internal bool WorkerAlive => worker?.IsAlive == true;
    internal long PresentedFrames {get;private set;}
    internal event Action<Frame> FramePresented;
#if DEBUG
    internal Action<Frame> BeforeFrameCompletionForVerification;
    internal volatile bool NativeInputForVerification;
#endif

    internal static bool Supports(string input) => extensions.Value.Contains(Path.GetExtension(input));
    internal static string[] SupportedExtensions => extensions.Value.Order().ToArray();
    static HashSet<string> LoadReaders()
    {
        lock (pluginLock)
        {
            F3dNative.f3d_engine_autoload_plugins();
            var readers = F3dNative.f3d_engine_get_readers_info(out int count);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var reader = Marshal.PtrToStructure<F3dNative.ReaderInfo>(readers + i * Marshal.SizeOf<F3dNative.ReaderInfo>());
                    for (int n = 0; reader.Extensions != IntPtr.Zero; n++)
                    {
                        var extension = Marshal.ReadIntPtr(reader.Extensions, n * IntPtr.Size);
                        if (extension == IntPtr.Zero) break;
                        result.Add("." + Marshal.PtrToStringUTF8(extension).TrimStart('.'));
                    }
                }
            }
            finally { if (readers != IntPtr.Zero) F3dNative.f3d_engine_destroy_readers_info(readers); }
            return result;
        }
    }
    public bool CanHandle(string input) => Supports(input);
    public void Init()
    {
        requests = new();
        worker = new Thread(RenderLoop) { IsBackground = true, Name = "Pathweft F3D preview" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    public void Prepare(string input, ContextObject value) { path = input; context = value; context.Title = Path.GetFileName(input) + " · 3D preview (F3D)"; }
    public async Task View(string input, ContextObject value)
    {
        // Native alpha frames composite onto the preview's own themed surface.
        var image = new Image { Stretch = Stretch.Uniform };
        var notice = BuiltinViewer.Message("Loading model…"); notice.Margin = new(10, 3, 10, 5);
        notice.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA");
        viewport = new Border { Background = Brushes.Transparent, Child = image, Focusable = true, ClipToBounds = true };
        AutomationProperties.SetName(viewport, "3D model. Drag to orbit, right or middle drag to pan, wheel to zoom. Arrows rotate, plus/minus zoom, Home resets, F fits.");
        var root = new DockPanel(); var controls = new WrapPanel { Margin = new(6) };
        var animationControls = new DockPanel { Margin = new(8, 2, 8, 6), Visibility = Visibility.Collapsed };
        var play = PreviewControls.Icon(owner, "Play", "\uE768");
        var clips = new ComboBox { MinWidth = 110, MinHeight = 32, MaxWidth = 200, Margin = new(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Style = (Style)owner.FindResource("PreviewClipCombo") };
        var timeline = new PreviewTimeline { MinWidth = 80, VerticalAlignment = VerticalAlignment.Center };
        var trimTail=new MenuItem {Header="Trim idle tail",IsCheckable=true,IsChecked=true};
        timeline.ContextMenu=new ContextMenu();timeline.ContextMenu.Items.Add(trimTail);
        void Range()
        {
            if(LastFrame is not {} frame)return;
            timeline.Minimum=frame.Start;timeline.Maximum=Math.Max(frame.Start,trimTail.IsChecked?frame.SmartEnd??frame.End:frame.End);
            trimTail.IsEnabled=frame.SmartEnd.HasValue;
            timeline.ToolTip=frame.SmartEnd.HasValue?$"Animation position · {timeline.Maximum-frame.Start:0.##} s of {frame.End-frame.Start:0.##} s. Right-click to change idle-tail trimming.":"Animation position";
            AutomationProperties.SetHelpText(timeline,timeline.ToolTip.ToString());
        }
        trimTail.Checked+=(_,_)=>Range();trimTail.Unchecked+=(_,_)=>Range();
        bool populatingClips=false;
        AutomationProperties.SetName(timeline, "Animation time"); AutomationProperties.SetName(clips, "Animation clip");
        DockPanel.SetDock(play, Dock.Left); DockPanel.SetDock(clips, Dock.Left);
        animationControls.Children.Add(play); animationControls.Children.Add(clips); animationControls.Children.Add(timeline);
        DockPanel.SetDock(animationControls, Dock.Bottom);root.Children.Add(animationControls);
        DockPanel.SetDock(controls, Dock.Bottom); DockPanel.SetDock(notice, Dock.Bottom);
        root.Children.Add(controls); root.Children.Add(notice); root.Children.Add(viewport);
        async Task Update(string operation, double x = 0, double y = 0, bool failLoad = false)
        {
            try
            {
                var frame = await Submit(operation, x, y);
                // Input revisions are not content generations: a completed frame
                // remains useful while a newer gesture is rendering. Reject only
                // an old viewer generation or an out-of-order/duplicate frame.
                if (!context.IsCurrent||frame.Sequence<=presentedSequence) return;
                presentedSequence=frame.Sequence;PresentedFrames++;
                LastFrame = frame; image.Source = frame.Bitmap; notice.Text = "Drag to rotate · Right-drag to pan · Wheel to zoom";
                FramePresented?.Invoke(frame);
                if(frame.Animations>0)
                {
                    animationControls.Visibility=Visibility.Visible;
                    if(clips.Items.Count!=frame.Animations)
                    {
                        populatingClips=true;
                        try{clips.Items.Clear();for(int i=0;i<frame.Animations;i++)clips.Items.Add("Animation "+(i+1));clips.SelectedIndex=0;}
                        finally{populatingClips=false;}
                    }
                    Range();
                }
            }
            catch (OperationCanceledException) when (!context.IsCurrent) { }
            catch (Exception ex) { if (failLoad) throw; if (context.IsCurrent) notice.Text = "3D preview unavailable: " + ex.Message; }
        }
        queue = (operation, x, y) => { if (stopped || !context.IsCurrent) return; RenderCompletion = Update(operation, x, y); context.Track(RenderCompletion); };
        void StopPlayback(){playing=false;animationClock.Stop();PreviewControls.SetIcon(play,"Play","\uE768");if(animationTick!=null)CompositionTarget.Rendering-=animationTick;}
        play.Click+=(_,_)=>
        {
            if(playing){StopPlayback();return;}
            if(LastFrame==null||timeline.Maximum<=timeline.Minimum)return;
            playing=true;playbackStart=animationTime;animationClock.Restart();PreviewControls.SetIcon(play,"Pause","\uE769");
            CompositionTarget.Rendering+=animationTick;
        };
        animationTick=(_,_)=>
        {
            if(!context.IsCurrent){StopPlayback();return;}
            if(!RenderCompletion.IsCompleted||LastFrame==null||timeline.IsMouseCaptureWithin)return;
            double length=timeline.Maximum-LastFrame.Start;
            animationTime=LastFrame.Start+(playbackStart-LastFrame.Start+animationClock.Elapsed.TotalSeconds)%length;
            timeline.ReportPosition(animationTime);queue("time",animationTime,0);
        };
        timeline.SeekRequested+=value=>
        {
            if(LastFrame==null)return;
            animationTime=value;
            if(playing){playbackStart=value;animationClock.Restart();}
            queue("time",animationTime,0);
        };
        clips.SelectionChanged+=(_,_)=>{if(!populatingClips&&clips.SelectedIndex>=0&&LastFrame!=null){StopPlayback();animationTime=0;queue("clip",clips.SelectedIndex,0);}};
        viewport.MouseDown += (_, e) =>
        {
            if (LastFrame == null || e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
            viewport.Focus(); lastPointer = e.GetPosition(viewport); dragging = e.ChangedButton;
            BeginDrag(lastPointer,dragging!=MouseButton.Left||(Keyboard.Modifiers&ModifierKeys.Shift)!=0);
            viewport.CaptureMouse(); e.Handled = true;
        };
        viewport.MouseMove += (_, e) =>
        {
            if (!viewport.IsMouseCaptured || dragging == null) return;
            lastPointer=e.GetPosition(viewport);MoveDrag(lastPointer);e.Handled=true;
        };
        viewport.MouseUp += (_, e) => { if (dragging == e.ChangedButton) { dragging = null;EndDrag();viewport.ReleaseMouseCapture();e.Handled=true;} };
        viewport.LostMouseCapture += (_, _) => {if(dragging!=null){dragging=null;EndDrag();}};
        viewport.MouseWheel += (_, e) => { if (LastFrame != null) { queue("zoom", Math.Pow(1.15, e.Delta / 120.0), 0); e.Handled = true; } };
        viewport.PreviewKeyDown += (_, e) => { if (HandleKey(e.Key)) e.Handled = true; };
        foreach (var (label, operation, glyph) in new[] { ("Orbit left", "left", "\uE7AD"), ("Orbit right", "right", "\uE7AD"), ("Zoom in", "in", "\uE8A3"), ("Zoom out", "out", "\uE71F"), ("Fit model", "fit", "\uE9A6"), ("Reset view", "reset", "\uE777") })
        {
            var button = PreviewControls.Icon(owner,label,glyph);
            if(operation=="right")((TextBlock)button.Content).LayoutTransform=new ScaleTransform(-1,1);
            button.Click += (_, _) => queue(operation, 0, 0);
            controls.Children.Add(button);
        }
        context.ViewerContent = root;
        void Resize()
        {
            if(stopped||!context.IsCurrent||viewport.ActualWidth<=0||viewport.ActualHeight<=0)return;
            var dpi=VisualTreeHelper.GetDpi(viewport);
            int width=Math.Max(1,(int)Math.Ceiling(viewport.ActualWidth*dpi.DpiScaleX)),height=Math.Max(1,(int)Math.Ceiling(viewport.ActualHeight*dpi.DpiScaleY));
            if(width==renderWidth&&height==renderHeight)return;
            renderWidth=width;renderHeight=height;queue("resize",width,height);
        }
        resizeHandler=(_,_)=>Resize();viewport.LayoutUpdated+=resizeHandler;
        RenderCompletion = Update("load", failLoad: true);
        await RenderCompletion;
    }
    internal void ApplyDrag(Vector delta, bool pan)
    {
        if (LastFrame == null || delta.LengthSquared == 0) return;
        var start=new Point(viewport.ActualWidth/2,viewport.ActualHeight/2);
        BeginDrag(start,pan);MoveDrag(start+delta);EndDrag();
    }
    internal void BeginDrag(Point point,bool pan)=>Pointer(pan?"pan-start":"orbit-start",point);
    internal void MoveDrag(Point point)=>Pointer("pointer-move",point);
    internal void EndDrag(){if(LastFrame!=null)queue("pointer-end",0,0);}
    void Pointer(string operation,Point point)
    {if(LastFrame!=null)queue(operation,point.X/Math.Max(1,viewport.ActualWidth),point.Y/Math.Max(1,viewport.ActualHeight));}
    internal void Seek(double time) {animationTime=time;queue("time",time,0);}
    internal bool HandleKey(Key key)
    {
        string operation = key switch { Key.Left => "left", Key.Right => "right", Key.Up => "up", Key.Down => "down", Key.Add or Key.OemPlus => "in", Key.Subtract or Key.OemMinus => "out", Key.Home => "reset", Key.F => "fit", _ => null };
        if (operation == null || LastFrame == null) return false;
        queue(operation, 0, 0); return true;
    }
    Task<Frame> Submit(string operation, double x, double y)
    {
        if (stopped || context.Token.IsCancellationRequested) return Task.FromCanceled<Frame>(new CancellationToken(true));
        var result = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests.Add(new(operation, x, y, result));
        return result.Task;
    }
    void RenderLoop()
    {
        IntPtr engine = IntPtr.Zero, window = IntPtr.Zero, camera = IntPtr.Zero, animation = IntPtr.Zero,interactor=IntPtr.Zero;
        GltfTimeline ranges=null;int clipIndex=0;
        bool pointerDown=false,pointerPan=false;int pointerX=0,pointerY=0;
        F3dOrbit orbit=null;
        bool cameraMoved=false;double defaultAspect=4.0/3;
        try
        {
            foreach (var first in requests.GetConsumingEnumerable())
            {
                // Coalesce gestures received during a render, preserving their total
                // motion without replaying stale intermediate frames.
                var batch = new List<Request> { first };
                while (requests.TryTake(out var next)) batch.Add(next);
                if (stopped || context.Token.IsCancellationRequested) { foreach (var r in batch) r.Result.TrySetCanceled(); continue; }
                try
                {
                    if (engine == IntPtr.Zero)
                    {
                        lock (pluginLock)
                        {
                            engine = F3dNative.f3d_engine_create(1);
                        }
                        if (engine == IntPtr.Zero) throw new InvalidOperationException("F3D could not create an offscreen renderer.");
                        string cache = Path.Combine(DataFolder.Cache, "preview-f3d-cache"); Directory.CreateDirectory(cache);
                        if (F3dNative.f3d_engine_set_cache_path(engine, cache) == 0) throw new IOException("F3D cache could not use the application profile.");
                        window = F3dNative.f3d_engine_get_window(engine); camera = F3dNative.f3d_window_get_camera(window);
                        interactor=F3dNative.f3d_engine_get_interactor(engine);
                        if (F3dNative.f3d_window_is_offscreen(window) == 0) throw new InvalidOperationException("The F3D renderer is not offscreen.");
                        F3dNative.f3d_window_set_size(window, 960, 720); F3dNative.f3d_window_set_use_hdri_cache(window, 0);
                        var options = F3dNative.f3d_engine_get_options(engine);
                        foreach (string name in new[] { "ui.axis", "ui.fps", "ui.filename", "ui.metadata", "ui.console", "ui.cheatsheet" }) F3dNative.f3d_options_set_as_bool(options, name, 0);
                        F3dNative.f3d_options_set_as_string(options,"render.effect.antialiasing.mode","fxaa");
                        foreach(string name in new[]{"render.effect.ambient_occlusion","render.effect.tone_mapping","render.grid.enable","render.hdri.ambient"})F3dNative.f3d_options_set_as_bool(options,name,1);
                        F3dNative.f3d_options_set_as_double_vector(options,"render.grid.color",light?new[]{0.4,0.4,0.4}:new[]{0.12,0.12,0.12},3);
                        F3dNative.f3d_options_set_as_double(options,"render.grid.reflection",0.2);
                        F3dNative.f3d_options_set_as_double(options, "render.light.intensity", 1.2);
                        if (F3dNative.f3d_scene_add(F3dNative.f3d_engine_get_scene(engine), path) == 0) throw new InvalidDataException("F3D could not read this model. Check its format and referenced resources.");
                        var modelUp=new double[3];F3dNative.f3d_camera_get_view_up(camera,modelUp);orbit=new(modelUp);
                        F3dNative.f3d_camera_azimuth(camera,20);F3dNative.f3d_camera_elevation(camera,15);
                        F3dNative.f3d_camera_reset_to_bounds(camera, 0.9);
                        F3dNative.f3d_camera_set_current_as_default(camera);
                        animation = F3dNative.f3d_scene_get_animation(F3dNative.f3d_engine_get_scene(engine));
                        ranges=GltfTimeline.Read(path);
                    }
                    var inputClock=System.Diagnostics.Stopwatch.StartNew();int inputSamples=0,inputRenderCalls=0;
                    foreach (var request in batch) switch (request.Operation)
                    {
                        case "time": F3dNative.f3d_animation_load_time(animation, request.X); break;
                        case "clip": clipIndex=(int)request.X;F3dNative.f3d_options_set_as_string(F3dNative.f3d_engine_get_options(engine), "scene.animation.indices", clipIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)); F3dNative.f3d_animation_load_time(animation, 0); break;
                        case "orbit-start":case "pan-start":
                            pointerX=(int)(request.X*F3dNative.f3d_window_get_width(window));
                            pointerY=F3dNative.f3d_window_get_height(window)-1-(int)(request.Y*F3dNative.f3d_window_get_height(window));
                            pointerPan=request.Operation=="pan-start";
#if DEBUG
                            if(NativeInputForVerification){
                            if(pointerDown)F3dNative.f3d_interactor_trigger_mouse_button(interactor,1,0);
                            F3dNative.f3d_interactor_trigger_mouse_position(interactor,request.X*F3dNative.f3d_window_get_width(window),request.Y*F3dNative.f3d_window_get_height(window));
                            // triggerMousePosition resets VTK's modifier bits;
                            // set Shift after positioning and before the press.
                            F3dNative.f3d_interactor_trigger_mod_update(interactor,request.Operation=="pan-start"?2:0);
                            F3dNative.f3d_interactor_trigger_mouse_button(interactor,0,0);}
#endif
                            pointerDown=true;break;
                        case "pointer-move":
                            if(pointerDown)
                            {
                                cameraMoved=true;inputSamples++;
                                int width=F3dNative.f3d_window_get_width(window),height=F3dNative.f3d_window_get_height(window);
                                int x=(int)(request.X*width),y=height-1-(int)(request.Y*height);
#if DEBUG
                                if(NativeInputForVerification){inputRenderCalls++;F3dNative.f3d_interactor_trigger_mouse_position(interactor,request.X*width,request.Y*height);}
                                else
#endif
                                orbit.Apply(camera,x-pointerX,y-pointerY,width,height,pointerPan);
                                pointerX=x;pointerY=y;
                            }break;
                        case "pointer-end":
#if DEBUG
                            if(NativeInputForVerification){
                            if(pointerDown)F3dNative.f3d_interactor_trigger_mouse_button(interactor,1,0);
                            F3dNative.f3d_interactor_trigger_mod_update(interactor,0);}
#endif
                            pointerDown=false;break;
                        case "resize":
                            double oldAspect=(double)F3dNative.f3d_window_get_width(window)/Math.Max(1,F3dNative.f3d_window_get_height(window));
                            double newAspect=request.X/request.Y;
                            F3dNative.f3d_window_set_size(window,(int)request.X,(int)request.Y);
                            if(!cameraMoved)
                            {F3dNative.f3d_camera_reset_to_bounds(camera,0.9);F3dNative.f3d_camera_set_current_as_default(camera);defaultAspect=newAspect;}
                            else F3dNative.f3d_camera_dolly(camera,Math.Min(1,newAspect)/Math.Min(1,oldAspect));
                            break;
                        case "zoom": cameraMoved=true;F3dNative.f3d_camera_dolly(camera, request.X); break;
                        case "up": cameraMoved=true;F3dNative.f3d_camera_elevation(camera, 20); break;
                        case "down": cameraMoved=true;F3dNative.f3d_camera_elevation(camera, -20); break;
                        case "fit": cameraMoved=true;F3dNative.f3d_camera_reset_to_bounds(camera, 0.9); break;
                        case "left": cameraMoved=true;F3dNative.f3d_camera_azimuth(camera, -20); break;
                        case "right": cameraMoved=true;F3dNative.f3d_camera_azimuth(camera, 20); break;
                        case "in": cameraMoved=true;F3dNative.f3d_camera_dolly(camera, 1.2); break;
                        case "out": cameraMoved=true;F3dNative.f3d_camera_dolly(camera, 1 / 1.2); break;
                        case "reset":
                            F3dNative.f3d_camera_reset_to_default(camera);
                            double aspect=(double)F3dNative.f3d_window_get_width(window)/Math.Max(1,F3dNative.f3d_window_get_height(window));
                            F3dNative.f3d_camera_dolly(camera,Math.Min(1,aspect)/Math.Min(1,defaultAspect));break;
                    }
                    context.Token.ThrowIfCancellationRequested();
                    double inputMs=inputClock.Elapsed.TotalMilliseconds;
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    IntPtr image = F3dNative.f3d_window_render_to_image(window, 1);
                    double renderMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
                    if (image == IntPtr.Zero) throw new InvalidOperationException("F3D returned no rendered model image.");
                    byte[] pixels;BitmapSource bitmap;
                    try
                    {
                        int width=checked((int)F3dNative.f3d_image_get_width(image)),height=checked((int)F3dNative.f3d_image_get_height(image));
                        if(width<=0||height<=0||F3dNative.f3d_image_get_channel_count(image)!=4||F3dNative.f3d_image_get_channel_type(image)!=0)throw new InvalidDataException("F3D did not return an RGBA byte frame.");
                        var content=F3dNative.f3d_image_get_content(image);
                        if(content==IntPtr.Zero)throw new InvalidDataException("F3D returned no pixel content.");
                        var rgba=new byte[checked(width*height*4)];Marshal.Copy(content,rgba,0,rgba.Length);
                        pixels=TopDownBgra(rgba,width,height);
                        bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();
                    }
                    finally { F3dNative.f3d_image_destroy(image); }
                    var position = new double[3]; F3dNative.f3d_camera_get_position(camera, position);
                    uint count=F3dNative.f3d_animation_count(animation);
                    double start=0,end=0;if(count>0)F3dNative.f3d_animation_get_time_range(animation,out start,out end);
                    var up=new double[3];var target=new double[3];F3dNative.f3d_camera_get_view_up(camera,up);F3dNative.f3d_camera_get_focal_point(camera,target);
                    var frame = new Frame(bitmap,pixels,position,true,count,start,end,++renderedSequence,renderMs,clock.Elapsed.TotalMilliseconds,up,target,inputMs,inputSamples,inputRenderCalls,ranges.EndFor(clipIndex,start,end));
#if DEBUG
                    BeforeFrameCompletionForVerification?.Invoke(frame);
#endif
                    foreach (var r in batch) r.Result.TrySetResult(frame);
                }
                catch (Exception ex) { foreach (var r in batch) r.Result.TrySetException(ex); }
            }
        }
        finally { if (engine != IntPtr.Zero) F3dNative.f3d_engine_destroy(engine); }
    }
    public void Cleanup()
    {
        if (stopped) return;
        stopped = true;
        playing=false;animationClock.Stop();if(animationTick!=null)CompositionTarget.Rendering-=animationTick;
        if(viewport!=null&&resizeHandler!=null)viewport.LayoutUpdated-=resizeHandler;
        if (viewport?.IsMouseCaptured == true) viewport.ReleaseMouseCapture();
        requests?.CompleteAdding();
        if (worker != null) context?.Track(Task.Run(() => { worker.Join(); requests.Dispose(); }));
        LastFrame = null;
    }
    internal static byte[] TopDownBgra(byte[] rgba,int width,int height)
    {
        // libf3d exports lower-left RGBA; WPF expects top-left BGRA.
        int stride=checked(width*4);var pixels=new byte[checked(stride*height)];
        if(rgba.Length!=pixels.Length)throw new ArgumentException("Unexpected RGBA dimensions.");
        for(int y=0;y<height;y++)for(int x=0;x<stride;x+=4)
        {
            int source=(height-y-1)*stride+x,target=y*stride+x;
            pixels[target]=rgba[source+2];pixels[target+1]=rgba[source+1];pixels[target+2]=rgba[source];pixels[target+3]=rgba[source+3];
        }
        return pixels;
    }
}
