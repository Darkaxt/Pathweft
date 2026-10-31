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
    internal sealed record Frame(BitmapImage Bitmap, byte[] Png, double[] Camera, bool Offscreen, uint Animations, double Start, double End);
    static readonly object pluginLock = new();
    static readonly Lazy<HashSet<string>> extensions = new(LoadReaders);
    BlockingCollection<Request> requests;
    Thread worker;
    ContextObject context;
    string path;
    volatile bool stopped;
    int revision;
    Border viewport;
    Point lastPointer;
    MouseButton? dragging;
    Action<string, double, double> queue;
    EventHandler animationTick;
    bool playing;
    double animationTime;
    readonly System.Diagnostics.Stopwatch animationClock = new();
    double playbackStart;
    double[] background;
    internal Task RenderCompletion { get; private set; } = Task.CompletedTask;
    internal Frame LastFrame { get; private set; }
    internal bool WorkerAlive => worker?.IsAlive == true;

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
        var color=(owner.Root.Background as SolidColorBrush)?.Color??Color.FromRgb(22,27,36);
        background=new[]{color.R/255.0,color.G/255.0,color.B/255.0};
        var image = new Image { Stretch = Stretch.Uniform };
        var notice = BuiltinViewer.Message("Loading model…"); notice.Margin = new(10, 3, 10, 5);
        notice.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA");
        viewport = new Border { Background = Brushes.Transparent, Child = image, Focusable = true, ClipToBounds = true };
        AutomationProperties.SetName(viewport, "3D model. Drag to orbit, right or middle drag to pan, wheel to zoom. Arrows rotate, plus/minus zoom, Home resets, F fits.");
        var root = new DockPanel(); var controls = new WrapPanel { Margin = new(6) };
        var animationControls = new DockPanel { Margin = new(8, 2, 8, 6), Visibility = Visibility.Collapsed };
        var play = new Button { Content = "Play", Style = (Style)owner.FindResource("Ghost"), Padding = new(8, 4, 8, 4) };
        var clips = new ComboBox { MinWidth = 85, Margin = new(4, 0, 8, 0) };
        var timeline = new Slider { MinWidth = 80, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(timeline, "Animation time"); AutomationProperties.SetName(clips, "Animation clip");
        DockPanel.SetDock(play, Dock.Left); DockPanel.SetDock(clips, Dock.Left);
        animationControls.Children.Add(play); animationControls.Children.Add(clips); animationControls.Children.Add(timeline);
        DockPanel.SetDock(animationControls, Dock.Bottom);root.Children.Add(animationControls);
        DockPanel.SetDock(controls, Dock.Bottom); DockPanel.SetDock(notice, Dock.Bottom);
        root.Children.Add(controls); root.Children.Add(notice); root.Children.Add(viewport);
        async Task Update(string operation, double x = 0, double y = 0, bool failLoad = false)
        {
            int current = ++revision;
            try
            {
                var frame = await Submit(operation, x, y);
                if (current != revision || !context.IsCurrent) return;
                LastFrame = frame; image.Source = frame.Bitmap; notice.Text = "Drag to rotate · Right-drag to pan · Wheel to zoom";
                if(frame.Animations>0)
                {
                    animationControls.Visibility=Visibility.Visible;
                    if(clips.Items.Count!=frame.Animations)
                    {for(int i=0;i<frame.Animations;i++)clips.Items.Add("Animation "+(i+1));clips.SelectedIndex=0;}
                    timeline.Minimum=frame.Start;timeline.Maximum=Math.Max(frame.Start,frame.End);
                }
            }
            catch (OperationCanceledException) when (!context.IsCurrent) { }
            catch (Exception ex) { if (failLoad) throw; if (context.IsCurrent) notice.Text = "3D preview unavailable: " + ex.Message; }
        }
        queue = (operation, x, y) => { if (stopped || !context.IsCurrent) return; RenderCompletion = Update(operation, x, y); context.Track(RenderCompletion); };
        void StopPlayback(){playing=false;animationClock.Stop();play.Content="Play";if(animationTick!=null)CompositionTarget.Rendering-=animationTick;}
        play.Click+=(_,_)=>
        {
            if(playing){StopPlayback();return;}
            if(LastFrame is not {End:var end,Start:var start}||end<=start)return;
            playing=true;playbackStart=animationTime;animationClock.Restart();play.Content="Pause";
            CompositionTarget.Rendering+=animationTick;
        };
        animationTick=(_,_)=>
        {
            if(!context.IsCurrent){StopPlayback();return;}
            if(!RenderCompletion.IsCompleted||LastFrame==null)return;
            double length=LastFrame.End-LastFrame.Start;
            animationTime=LastFrame.Start+(playbackStart-LastFrame.Start+animationClock.Elapsed.TotalSeconds)%length;
            timeline.SetCurrentValue(Slider.ValueProperty,animationTime);queue("time",animationTime,0);
        };
        timeline.ValueChanged+=(_,e)=>{if(!playing&&LastFrame!=null){animationTime=e.NewValue;queue("time",animationTime,0);}};
        clips.SelectionChanged+=(_,_)=>{if(clips.SelectedIndex>=0&&LastFrame!=null){StopPlayback();animationTime=0;queue("clip",clips.SelectedIndex,0);}};
        viewport.MouseDown += (_, e) =>
        {
            if (LastFrame == null || e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
            viewport.Focus(); lastPointer = e.GetPosition(viewport); dragging = e.ChangedButton;
            viewport.CaptureMouse(); e.Handled = true;
        };
        viewport.MouseMove += (_, e) =>
        {
            if (!viewport.IsMouseCaptured || dragging == null) return;
            Point next = e.GetPosition(viewport); Vector delta = next - lastPointer; lastPointer = next;
            ApplyDrag(delta, dragging != MouseButton.Left || (Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true;
        };
        viewport.MouseUp += (_, e) => { if (dragging == e.ChangedButton) { dragging = null; viewport.ReleaseMouseCapture(); e.Handled = true; } };
        viewport.LostMouseCapture += (_, _) => dragging = null;
        viewport.MouseWheel += (_, e) => { if (LastFrame != null) { queue("zoom", Math.Pow(1.15, e.Delta / 120.0), 0); e.Handled = true; } };
        viewport.PreviewKeyDown += (_, e) => { if (HandleKey(e.Key)) e.Handled = true; };
        foreach (var (label, operation) in new[] { ("Orbit left", "left"), ("Orbit right", "right"), ("Zoom in", "in"), ("Zoom out", "out"), ("Fit", "fit"), ("Reset view", "reset") })
        {
            var button = new Button { Content = label, Style = (Style)owner.FindResource("Ghost"), Margin = new(2), Padding = new(8, 5, 8, 5) };
            button.Click += (_, _) => queue(operation, 0, 0);
            controls.Children.Add(button);
        }
        context.ViewerContent = root;
        RenderCompletion = Update("load", failLoad: true);
        await RenderCompletion;
    }
    internal void ApplyDrag(Vector delta, bool pan)
    {
        if (LastFrame == null || delta.LengthSquared == 0) return;
        queue(pan ? "pan" : "orbit", delta.X / Math.Max(1, viewport.ActualWidth), delta.Y / Math.Max(1, viewport.ActualHeight));
    }
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
        IntPtr engine = IntPtr.Zero, window = IntPtr.Zero, camera = IntPtr.Zero, animation = IntPtr.Zero;
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
                        string cache = Path.Combine(Settings.Dir, "preview-f3d-cache"); Directory.CreateDirectory(cache);
                        if (F3dNative.f3d_engine_set_cache_path(engine, cache) == 0) throw new IOException("F3D cache could not use the application profile.");
                        window = F3dNative.f3d_engine_get_window(engine); camera = F3dNative.f3d_window_get_camera(window);
                        if (F3dNative.f3d_window_is_offscreen(window) == 0) throw new InvalidOperationException("The F3D renderer is not offscreen.");
                        F3dNative.f3d_window_set_size(window, 960, 720); F3dNative.f3d_window_set_use_hdri_cache(window, 0);
                        var options = F3dNative.f3d_engine_get_options(engine);
                        foreach (string name in new[] { "ui.axis", "ui.fps", "ui.filename", "ui.metadata", "ui.console", "ui.cheatsheet", "render.grid.enable" }) F3dNative.f3d_options_set_as_bool(options, name, 0);
                        F3dNative.f3d_options_set_as_double_vector(options, "render.background.color", background, 3);
                        F3dNative.f3d_options_set_as_double(options, "render.light.intensity", 1.2);
                        if (F3dNative.f3d_scene_add(F3dNative.f3d_engine_get_scene(engine), path) == 0) throw new InvalidDataException("F3D could not read this model. Check its format and referenced resources.");
                        F3dNative.f3d_camera_reset_to_bounds(camera, 0.9);
                        F3dNative.f3d_camera_set_current_as_default(camera);
                        animation = F3dNative.f3d_scene_get_animation(F3dNative.f3d_engine_get_scene(engine));
                    }
                    foreach (var request in batch) switch (request.Operation)
                    {
                        case "time": F3dNative.f3d_animation_load_time(animation, request.X); break;
                        case "clip": F3dNative.f3d_options_set_as_string(F3dNative.f3d_engine_get_options(engine), "scene.animation.indices", ((int)request.X).ToString(System.Globalization.CultureInfo.InvariantCulture)); F3dNative.f3d_animation_load_time(animation, 0); break;
                        case "orbit": F3dNative.f3d_camera_azimuth(camera, -request.X * 180); F3dNative.f3d_camera_elevation(camera, request.Y * 180); break;
                        case "pan":
                            var pos = new double[3]; var focal = new double[3];
                            F3dNative.f3d_camera_get_position(camera, pos); F3dNative.f3d_camera_get_focal_point(camera, focal);
                            double distance = Math.Sqrt(pos.Zip(focal).Sum(p => Math.Pow(p.First - p.Second, 2)));
                            double span = 2 * distance * Math.Tan(F3dNative.f3d_camera_get_view_angle(camera) * Math.PI / 360);
                            F3dNative.f3d_camera_pan(camera, -request.X * span * 4 / 3, request.Y * span, 0); break;
                        case "zoom": F3dNative.f3d_camera_dolly(camera, request.X); break;
                        case "up": F3dNative.f3d_camera_elevation(camera, 20); break;
                        case "down": F3dNative.f3d_camera_elevation(camera, -20); break;
                        case "fit": F3dNative.f3d_camera_reset_to_bounds(camera, 0.9); break;
                        case "left": F3dNative.f3d_camera_azimuth(camera, -20); break;
                        case "right": F3dNative.f3d_camera_azimuth(camera, 20); break;
                        case "in": F3dNative.f3d_camera_dolly(camera, 1.2); break;
                        case "out": F3dNative.f3d_camera_dolly(camera, 1 / 1.2); break;
                        case "reset": F3dNative.f3d_camera_reset_to_default(camera); break;
                    }
                    context.Token.ThrowIfCancellationRequested();
                    IntPtr image = F3dNative.f3d_window_render_to_image(window, 0);
                    if (image == IntPtr.Zero) throw new InvalidOperationException("F3D returned no rendered model image.");
                    byte[] bytes;
                    try
                    {
                        IntPtr buffer = F3dNative.f3d_image_save_buffer(image, 0, out uint size);
                        if (buffer == IntPtr.Zero) throw new InvalidDataException("F3D could not encode its frame.");
                        try
                        {
                            bytes = new byte[checked((int)size)]; Marshal.Copy(buffer, bytes, 0, bytes.Length);
                        }
                        finally { F3dNative.f3d_image_destroy_buffer(buffer); }
                    }
                    finally { F3dNative.f3d_image_destroy(image); }
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    var position = new double[3]; F3dNative.f3d_camera_get_position(camera, position);
                    uint count=F3dNative.f3d_animation_count(animation);
                    double start=0,end=0;if(count>0)F3dNative.f3d_animation_get_time_range(animation,out start,out end);
                    var frame = new Frame(bitmap, bytes, position, true, count, start, end);
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
        stopped = true; ++revision;
        playing=false;animationClock.Stop();if(animationTick!=null)CompositionTarget.Rendering-=animationTick;
        if (viewport?.IsMouseCaptured == true) viewport.ReleaseMouseCapture();
        requests?.CompleteAdding();
        if (worker != null) context?.Track(Task.Run(() => { worker.Join(); requests.Dispose(); }));
        LastFrame = null;
    }
}
