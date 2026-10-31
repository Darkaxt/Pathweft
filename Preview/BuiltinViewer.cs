// Selected QuickLook viewer dispatch/text detection adapted for .NET 8 WPF.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
// Image/PDF/media/font/listing rendering reuses File Labs' MIT-licensed paths.
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace FileExplorer.Preview;

internal sealed class BuiltinViewer(PreviewModule module, MainWindow owner, Entry entry) : IViewer
{
    MediaElement media;
    DispatcherTimer positionTimer;
    byte[] pdfBytes;
    int pageRevision;
    bool cleaned;
    bool paused;
    internal Task Ready { get; private set; } = Task.CompletedTask;

    public bool CanHandle(string path)
    {
        if (module.Id != "text") return true;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[16 * 1024];
        int count = stream.Read(bytes, 0, bytes.Length);
        if (count >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF)) return true;
        // QuickLook TextViewer's bounded binary sniff, with BOM-aware UTF-16.
        for (int i = 1; i < count; i++) if (bytes[i - 1] == 0 && bytes[i] == 0) return false;
        return true;
    }
    public void Init() { }
    public void Prepare(string path, ContextObject context) => context.Title = entry.Name + " · " + module.Name;
    public async Task View(string path, ContextObject context)
    {
        UIElement content;
        switch (module.Id)
        {
            case "text": content = Text(await ReadText(path, 256 * 1024, context.Token)); break;
            case "markdown": content = MarkdownContent.Render(await ReadText(path, 1024 * 1024, context.Token)); break;
            case "image": content = await Image(path, context.Token); break;
            case "pdf": await Pdf(path, context); return;
            case "executable": content = ExecutableDetails.View(await Task.Run(()=>ExecutableDetails.Read(path,context.Token),context.Token)); break;
            case "archive":
                if (entry.IsDir && PaneView.ArchiveOf(entry.Path) is { } location)
                {
                    content = ArchiveTree.View(await Task.Run(() => ArchiveTree.Read(location.Archive, location.Inner, context.Token), context.Token), context);
                    break;
                }
                if (!entry.IsDir && Backups.Named(path) && await Task.Run(() => Backups.Inspect(path, details: true, () => context.Token.IsCancellationRequested), context.Token) is { } backup)
                    content = owner.BackupView(entry, backup);
                else if (!entry.IsDir) content = ArchiveTree.View(await Task.Run(() => ArchiveTree.Read(path, "", context.Token), context.Token), context);
                else content = ArchiveTree.View(await Task.Run(() => ArchiveTree.ReadDirectory(path, context.Token, PaneView.ShowHidden), context.Token), context);
                break;
            case "font": content = Font(path); break;
            case "media": await Media(path, context); return;
            default: throw new NotSupportedException("Unknown built-in preview module.");
        }
        context.Token.ThrowIfCancellationRequested();
        context.ViewerContent = content;
    }
    public void Cleanup()
    {
        if (cleaned) return;
        cleaned = true; ++pageRevision;
        positionTimer?.Stop(); positionTimer = null;
        media?.Close(); if (media != null) media.Source = null; media = null;
        pdfBytes = null;
    }

    internal static TextBlock Message(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(20), Foreground = MainWindow.Hex("#B3FFFFFF") };
    internal static TextBox Text(string text) => new()
    {
        Text = text, IsReadOnly = true, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12,
        Background = MainWindow.Hex("#59000000"), Foreground = MainWindow.Hex("#E6FFFFFF"), BorderThickness = new(0), Padding = new(14),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    internal static async Task<string> ReadText(string path, int limit, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var bytes = new byte[(int)Math.Min(file.Length, limit)];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = await file.ReadAsync(bytes.AsMemory(read), token);
            if (count == 0) break;
            read += count;
        }
        using var reader = new StreamReader(new MemoryStream(bytes, 0, read), Encoding.UTF8, true);
        string text = await reader.ReadToEndAsync(token);
        return file.Length > read ? text + $"\n\n… Preview truncated at {limit / 1024} KiB." : text;
    }
    static async Task<byte[]> ReadBytes(string path, long maximum, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        if (file.Length > maximum) throw new NotSupportedException($"This preview supports files up to {maximum / (1024 * 1024)} MiB.");
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await file.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + count > maximum) throw new NotSupportedException("The file grew beyond this preview's size limit.");
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
    static async Task<UIElement> Image(string path, CancellationToken token)
    {
        byte[] bytes = await ReadBytes(path, 64L << 20, token);
        var bitmap = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 200_000_000) throw new NotSupportedException("Image exceeds the 200-megapixel preview limit.");
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(2000, frame.PixelWidth);
            else image.DecodePixelHeight = Math.Min(2000, frame.PixelHeight);
            image.StreamSource = stream; image.EndInit(); image.Freeze();
            return image;
        }, token);
        return new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(16) };
    }
    static UIElement Font(string path)
    {
        if (new FileInfo(path).Length > 16L << 20) throw new NotSupportedException("Font exceeds the 16-MiB preview limit.");
        var family = Fonts.GetFontFamilies(new Uri(path)).FirstOrDefault() ?? throw new FileFormatException("Not a readable font.");
        var sample = new StackPanel { Margin = new(24) };
        sample.Children.Add(Message(string.Join(", ", family.FamilyNames.Values)));
        foreach (int size in new[] { 48, 32, 20, 14 }) sample.Children.Add(new TextBlock { Text = "The quick brown fox jumps over the lazy dog 0123456789 ăâîșț", FontFamily = family, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
        return new ScrollViewer { Content = sample, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    async Task Pdf(string path, ContextObject context)
    {
        pdfBytes = await ReadBytes(path, 64L << 20, context.Token);
        context.Token.ThrowIfCancellationRequested();
        var root = new DockPanel();
        var controls = new WrapPanel { Margin = new(8) };
        var label = Message("Loading PDF…"); label.Margin = new(10, 5, 10, 5);
        var previous = PreviewControls.Icon(owner,"Previous page","\uE76B");
        var next = PreviewControls.Icon(owner,"Next page","\uE76C");
        controls.Children.Add(previous); controls.Children.Add(label); controls.Children.Add(next);
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);
        var pageImage = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(12) };
        root.Children.Add(new ScrollViewer { Content = pageImage, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        context.ViewerContent = root;
        uint current = 0, total = 0;
        async Task Render(uint index)
        {
            int revision = ++pageRevision;
            var bytes = pdfBytes;
            if (bytes == null || !context.IsCurrent) return;
            previous.IsEnabled = next.IsEnabled = false;
            try
            {
                using var source = new MemoryStream(bytes);
                using var random = source.AsRandomAccessStream();
                var document = await PdfDocument.LoadFromStreamAsync(random);
                context.Token.ThrowIfCancellationRequested();
                using var page = document.GetPage(index);
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 1400 });
                context.Token.ThrowIfCancellationRequested();
                using var rendered = stream.AsStreamForRead();
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = rendered; bitmap.EndInit(); bitmap.Freeze();
                if (!context.IsCurrent || revision != pageRevision) return;
                pageImage.Source = bitmap; current = index; total = document.PageCount;
                label.Text = $"Page {index + 1} of {total}";
                previous.IsEnabled = index > 0; next.IsEnabled = index + 1 < total;
            }
            catch (OperationCanceledException) when (context.Token.IsCancellationRequested) { }
            catch (Exception ex) { if (context.IsCurrent && revision == pageRevision) label.Text = "PDF unavailable: " + ex.Message; }
        }
        previous.Click += (_, _) => context.Track(Render(current - 1));
        next.Click += (_, _) => context.Track(Render(current + 1));
        await Render(0);
        if (context.IsCurrent && total == 0) throw new InvalidDataException(label.Text);
    }

    Button Button(string label) => new() { Content = label, Style = (Style)owner.FindResource("Ghost"), Margin = new(3), Padding = new(8, 5, 8, 5) };
    async Task Media(string path, ContextObject context)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var element = media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, ScrubbingEnabled = true };
#if DEBUG
        if (HiddenTrialPresentation.Enabled) element.Volume = 0;
#endif
        var play = Button("Play / pause");
        var position = new Slider { Minimum = 0, MinWidth = 80, Margin = new(8), VerticalAlignment = VerticalAlignment.Center };
        var time = Message(""); time.Margin = new(8);
        paused = !context.Autoplay;
        play.Click += (_, _) => { if (paused) element.Play(); else element.Pause(); paused = !paused; };
        position.PreviewMouseLeftButtonUp += (_, _) => element.Position = TimeSpan.FromSeconds(position.Value);
        element.MediaOpened += (_, _) =>
        {
            if (!context.IsCurrent) return;
            if (element.NaturalDuration.HasTimeSpan) position.Maximum = element.NaturalDuration.TimeSpan.TotalSeconds;
            opened.TrySetResult();
        };
        element.MediaFailed += (_, e) =>
        {
            opened.TrySetException(e.ErrorException);
            if (context.IsCurrent) context.ViewerContent = Message("Media unavailable: " + e.ErrorException.Message);
            positionTimer?.Stop(); element.Close();
        };
        element.MediaEnded += (_, _) => { element.Pause(); element.Position = TimeSpan.Zero; paused = true; };
        var root = new DockPanel(); var controls = new DockPanel { Margin = new(6) };
        DockPanel.SetDock(play, Dock.Left); DockPanel.SetDock(time, Dock.Right);
        controls.Children.Add(play); controls.Children.Add(time); controls.Children.Add(position);
        DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls); root.Children.Add(element);
        context.ViewerContent = root;
        using var registration = context.Token.Register(() => opened.TrySetCanceled(context.Token));
        element.Source = new Uri(path);
        if (context.Autoplay) element.Play(); else element.Pause();
        // Display-only position refresh. Loading/cleanup never depends on this timer.
        positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        positionTimer.Tick += (_, _) =>
        {
            if (!context.IsCurrent) return;
            if (!position.IsMouseCaptureWithin) position.Value = element.Position.TotalSeconds;
            time.Text = $"{element.Position:mm\\:ss} / {TimeSpan.FromSeconds(position.Maximum):mm\\:ss}";
        };
        positionTimer.Start();
        Ready = opened.Task;
        await Ready;
        await owner.AddReelPreview(path, root, () => context.IsCurrent);
    }
    internal bool HandleKey(System.Windows.Input.Key key)
    {
        if (media == null) return false;
        switch (key)
        {
            case System.Windows.Input.Key.Enter: if (paused) media.Play(); else media.Pause(); paused = !paused; return true;
            case System.Windows.Input.Key.Left: media.Position = media.Position > TimeSpan.FromSeconds(5) ? media.Position - TimeSpan.FromSeconds(5) : TimeSpan.Zero; return true;
            case System.Windows.Input.Key.Right: media.Position += TimeSpan.FromSeconds(5); return true;
            default: return false;
        }
    }
}
