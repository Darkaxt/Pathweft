// libf3d's offscreen rendering example adapted to a cancellable WPF module.
// Copyright F3D contributors; BSD-3-Clause. See vendor/F3D/NOTICES.txt.
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Preview;

internal sealed class F3dViewer(MainWindow owner) : IViewer
{
    sealed record Request(string Operation, TaskCompletionSource<Frame> Result);
    internal sealed record Frame(BitmapImage Bitmap, byte[] Png, double[] Camera, bool Offscreen);
    static readonly object pluginLock = new();
    BlockingCollection<Request> requests;
    Thread worker;
    ContextObject context;
    string path;
    bool stopped;
    int revision;
    internal Task RenderCompletion { get; private set; } = Task.CompletedTask;
    internal Frame LastFrame { get; private set; }
    internal bool WorkerAlive => worker?.IsAlive == true;

    public bool CanHandle(string input)
    {
        using var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 64L << 20) throw new NotSupportedException("The GLB prototype supports self-contained models up to 64 MiB.");
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (stream.Length < 20 || reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2 || reader.ReadUInt32() != stream.Length)
            throw new InvalidDataException("Not a valid version-2 GLB container.");
        uint length = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x4E4F534A || length > 4 << 20 || length > stream.Length - stream.Position)
            throw new InvalidDataException("GLB metadata exceeds the prototype's bounded JSON chunk.");
        using var document = JsonDocument.Parse(reader.ReadBytes((int)length));
        foreach (string collection in new[] { "buffers", "images" })
            if (document.RootElement.TryGetProperty(collection, out var items))
                foreach (var item in items.EnumerateArray())
                    if (item.TryGetProperty("uri", out var uri) && !uri.GetString().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("This prototype previews embedded GLB resources only; external references are inactive.");
        return true;
    }
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
        var image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(8) };
        var notice = BuiltinViewer.Message("Drag-free camera controls · GLB prototype"); notice.Margin = new(10, 3, 10, 5);
        var root = new DockPanel(); var controls = new WrapPanel { Margin = new(6) };
        DockPanel.SetDock(controls, Dock.Bottom); DockPanel.SetDock(notice, Dock.Bottom);
        root.Children.Add(controls); root.Children.Add(notice); root.Children.Add(image);
        async Task Update(string operation)
        {
            int current = ++revision;
            try
            {
                var frame = await Submit(operation);
                if (current != revision || !context.IsCurrent) return;
                LastFrame = frame; image.Source = frame.Bitmap; notice.Text = "GLB prototype · F3D 4.0.0";
            }
            catch (OperationCanceledException) when (!context.IsCurrent) { }
            catch (Exception ex) { if (context.IsCurrent) { notice.Text = "3D preview unavailable: " + ex.Message; throw; } }
        }
        foreach (var (label, operation) in new[] { ("Orbit left", "left"), ("Orbit right", "right"), ("Zoom in", "in"), ("Zoom out", "out"), ("Reset view", "reset") })
        {
            var button = new Button { Content = label, Style = (Style)owner.FindResource("Ghost"), Margin = new(2), Padding = new(8, 5, 8, 5) };
            button.Click += (_, _) => { RenderCompletion = Update(operation); context.Track(RenderCompletion); };
            controls.Children.Add(button);
        }
        context.ViewerContent = root;
        RenderCompletion = Update("load");
        await RenderCompletion;
    }
    Task<Frame> Submit(string operation)
    {
        if (stopped || context.Token.IsCancellationRequested) return Task.FromCanceled<Frame>(new CancellationToken(true));
        var result = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests.Add(new(operation, result));
        return result.Task;
    }
    void RenderLoop()
    {
        IntPtr engine = IntPtr.Zero, window = IntPtr.Zero, camera = IntPtr.Zero;
        try
        {
            foreach (var request in requests.GetConsumingEnumerable())
            {
                if (stopped || context.Token.IsCancellationRequested) { request.Result.TrySetCanceled(); continue; }
                try
                {
                    if (engine == IntPtr.Zero)
                    {
                        lock (pluginLock)
                        {
                            if (F3dNative.f3d_engine_load_plugin("native") == 0) throw new InvalidOperationException("F3D's bundled native GLB reader is unavailable.");
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
                        F3dNative.f3d_options_set_as_double_vector(options, "render.background.color", new[] { 22 / 255.0, 27 / 255.0, 36 / 255.0 }, 3);
                        F3dNative.f3d_options_set_as_double(options, "render.light.intensity", 1.2);
                        if (F3dNative.f3d_scene_add(F3dNative.f3d_engine_get_scene(engine), path) == 0) throw new InvalidDataException("F3D could not read the GLB scene.");
                        F3dNative.f3d_camera_reset_to_bounds(camera, 0.9);
                        F3dNative.f3d_camera_set_current_as_default(camera);
                    }
                    switch (request.Operation)
                    {
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
                            if (size == 0 || size > 32 << 20) throw new InvalidDataException("F3D frame exceeds the prototype image bound.");
                            bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, bytes.Length);
                        }
                        finally { F3dNative.f3d_image_destroy_buffer(buffer); }
                    }
                    finally { F3dNative.f3d_image_destroy(image); }
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                    var position = new double[3]; F3dNative.f3d_camera_get_position(camera, position);
                    request.Result.TrySetResult(new(bitmap, bytes, position, true));
                }
                catch (Exception ex) { request.Result.TrySetException(ex); }
            }
        }
        finally { if (engine != IntPtr.Zero) F3dNative.f3d_engine_destroy(engine); }
    }
    public void Cleanup()
    {
        if (stopped) return;
        stopped = true; ++revision;
        requests?.CompleteAdding();
        if (worker != null) context?.Track(Task.Run(() => { worker.Join(); requests.Dispose(); }));
        LastFrame = null;
    }
}
