using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.StartMenu;

/// SAB-07 (S6d): StartAllBack's Start-menu visual styles. "Visual style" Default is drawn by StartAllBack itself; Plain8
/// and Windows 7 are theme files (AlterStyle, a .msstyles in StartAllBack's Styles folder) whose StartPanelPriv parts
/// (with their Composited and Dark variants) and SearchBox parts draw the menu. The theme file is read as data
/// (LoadLibraryEx as a data file, its CMAP, VARIANT and IMAGE resources), never applied or loaded as code: each part's
/// images, image count and layout, sizing and content margins, text and fill colours, and the per-DPI images.
internal sealed class StartMenuTheme
{
    /// One part (in one state's class) of the theme: its background image (null if it has none), the images chosen by
    /// DPI (a glyph drawn centred, or the background when the part has no other), how many state frames each image
    /// holds and whether they are side by side, the sizing margins (the nine-grid's fixed edges), the content margins
    /// and the colours.
    internal sealed record Part(BitmapSource Image, BitmapSource[] DpiImages, int[] MinDpi, int ImageCount, bool Horizontal,
        Thickness Sizing, Thickness Content, Color? Text, Color? Fill, bool Stretch);

    readonly Dictionary<(string Class, int Part), Part> parts = [];
    internal string File { get; }

    StartMenuTheme(string file) { File = file; }

    /// StartAllBack's Styles folder: its per-user installation, else the machine-wide one.
    internal static string StylesFolder()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            var styles = Path.Combine(root, "StartAllBack", "Styles");
            if (Directory.Exists(styles)) return styles;
        }
        return null;
    }

    /// The theme file AlterStyle names (a name in the Styles folder, or a full path); null for Default or a file that
    /// is not there (StartAllBack then draws its Default too).
    internal static string Resolve(string alterStyle)
    {
        if (string.IsNullOrWhiteSpace(alterStyle)) return null;
        string path = Path.IsPathRooted(alterStyle) ? alterStyle : StylesFolder() is { } styles ? Path.Combine(styles, alterStyle) : null;
        return path != null && System.IO.File.Exists(path) ? path : null;
    }

    static readonly Dictionary<string, (DateTime Written, StartMenuTheme Theme)> cache = new(StringComparer.OrdinalIgnoreCase);

    /// The theme in this file, read once per version of the file.
    internal static StartMenuTheme Load(string path)
    {
        if (path == null) return null;
        var written = System.IO.File.GetLastWriteTimeUtc(path);
        lock (cache)
        {
            if (cache.TryGetValue(path, out var c) && c.Written == written) return c.Theme;
            StartMenuTheme theme;
            try { theme = Read(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or COMException or FileFormatException) { theme = null; }
            cache[path] = (written, theme);
            return theme;
        }
    }

    /// The part as the menu draws it: the variant's class first (StartPanelComposited, StartPanelDark), then the plain
    /// class, each property from the first class that defines it, as Windows' theme classes inherit.
    internal Part Get(int part, params string[] classes)
    {
        Part found = null;
        foreach (var c in classes.Append("StartPanelPriv"))
            if (parts.TryGetValue((c, part), out var p)) found = found == null ? p : Merge(found, p);
        return found;
    }

    internal Part Search(int part) => parts.TryGetValue(("SearchBox", part), out var p) ? p : null;

    internal bool Has(string cls) => parts.Keys.Any(k => k.Class == cls);

    static Part Merge(Part a, Part b) => new(a.Image ?? b.Image, a.DpiImages.Length > 0 ? a.DpiImages : b.DpiImages, a.DpiImages.Length > 0 ? a.MinDpi : b.MinDpi,
        a.ImageCount > 0 ? a.ImageCount : b.ImageCount, a.ImageCount > 0 ? a.Horizontal : b.Horizontal,
        a.Sizing == default ? b.Sizing : a.Sizing, a.Content == default ? b.Content : a.Content, a.Text ?? b.Text, a.Fill ?? b.Fill, a.Image != null ? a.Stretch : b.Stretch);

    // ---- reading the theme file ----

    // property ids (Windows' TMT_ values) and types
    const int ImageCountId = 2401, MinDpi1 = 2420, ImageFile = 3001, ImageFile1 = 3002, SizingMargins = 3601, ContentMargins = 3602,
        TextColor = 3803, FillColor = 3802, ImageLayout = 4011, SizingType = 4004;
    const int TypeFileName = 206, TypeColor = 204, TypeMargins = 205;

    sealed class Raw
    {
        internal int Image, ImageCount, Layout = -1, SizingType = -1;
        internal readonly int[] DpiImages = new int[7], MinDpi = new int[7];
        internal Thickness? Sizing, Content;
        internal Color? Text, Fill;
    }

    static StartMenuTheme Read(string path)
    {
        var module = LoadLibraryExW(path, IntPtr.Zero, 0x2 | 0x20); // as a data file and image resource: nothing runs
        if (module == IntPtr.Zero) throw new IOException("The theme file could not be read: " + path);
        try
        {
            var cmap = Resource(module, "CMAP", "CMAP") ?? throw new InvalidDataException("No class map");
            var variant = Resource(module, "VARIANT", "NORMAL") ?? throw new InvalidDataException("No properties");
            var classes = System.Text.Encoding.Unicode.GetString(cmap).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var raw = new Dictionary<(string, int), Raw>();
            for (int o = 0; o + 32 <= variant.Length;)
            {
                int name = BitConverter.ToInt32(variant, o), type = BitConverter.ToInt32(variant, o + 4), cls = BitConverter.ToInt32(variant, o + 8),
                    part = BitConverter.ToInt32(variant, o + 12), state = BitConverter.ToInt32(variant, o + 16), inline = BitConverter.ToInt32(variant, o + 20),
                    size = BitConverter.ToInt32(variant, o + 28);
                o += 32;
                int data = o;
                // a file name holds its image's resource id in the header; other values follow it, padded to 8 bytes
                if (inline == 0) { o += size; o += (8 - o % 8) % 8; }
                if (cls < 0 || cls >= classes.Length || state != 0) continue;
                string c = classes[cls];
                if (c != "SearchBox" && !c.EndsWith("StartPanelPriv", StringComparison.Ordinal)) continue;
                if (!raw.TryGetValue((c, part), out var r)) raw[(c, part)] = r = new Raw();
                int Int() => inline == 0 && size >= 4 && data + 4 <= variant.Length ? BitConverter.ToInt32(variant, data) : 0;
                switch (name)
                {
                    case ImageFile when type == TypeFileName: r.Image = inline; break;
                    case >= ImageFile1 and < ImageFile1 + 7 when type == TypeFileName: r.DpiImages[name - ImageFile1] = inline; break;
                    case >= MinDpi1 and < MinDpi1 + 7: r.MinDpi[name - MinDpi1] = Int(); break;
                    case ImageCountId: r.ImageCount = Int(); break;
                    case ImageLayout: r.Layout = Int(); break;
                    case SizingType: r.SizingType = Int(); break;
                    case SizingMargins when type == TypeMargins && size >= 16: r.Sizing = Margins(variant, data); break;
                    case ContentMargins when type == TypeMargins && size >= 16: r.Content = Margins(variant, data); break;
                    case TextColor when type == TypeColor: r.Text = ColorRef(Int()); break;
                    case FillColor when type == TypeColor: r.Fill = ColorRef(Int()); break;
                }
            }
            var theme = new StartMenuTheme(path);
            var images = new Dictionary<int, BitmapSource>();
            BitmapSource Image(int id)
            {
                if (id <= 0) return null;
                if (images.TryGetValue(id, out var b)) return b;
                var bytes = Resource(module, "IMAGE", id);
                if (bytes != null)
                {
                    var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    b = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                    b.Freeze();
                }
                return images[id] = b;
            }
            foreach (var ((c, part), r) in raw)
            {
                var dpi = new List<BitmapSource>(); var min = new List<int>();
                for (int i = 0; i < 7; i++)
                    if (Image(r.DpiImages[i]) is { } b) { dpi.Add(b); min.Add(r.MinDpi[i] > 0 ? r.MinDpi[i] : 96 * (i + 1)); }
                theme.parts[(c, part)] = new Part(Image(r.Image), [.. dpi], [.. min], r.ImageCount, r.Layout == 1,
                    r.Sizing ?? default, r.Content ?? default, r.Text, r.Fill, r.SizingType != 0);
            }
            return theme;
        }
        finally { FreeLibrary(module); }
    }

    // MARGINS: left, right, top, bottom
    static Thickness Margins(byte[] b, int o) => new(BitConverter.ToInt32(b, o), BitConverter.ToInt32(b, o + 8), BitConverter.ToInt32(b, o + 4), BitConverter.ToInt32(b, o + 12));
    static Color ColorRef(int v) => Color.FromRgb((byte)v, (byte)(v >> 8), (byte)(v >> 16));

    static byte[] Resource(IntPtr module, string type, string name) => Copy(module, FindResourceW(module, name, type));
    static byte[] Resource(IntPtr module, string type, int id) => Copy(module, FindResourceW(module, (IntPtr)id, type));
    static byte[] Copy(IntPtr module, IntPtr info)
    {
        if (info == IntPtr.Zero) return null;
        int size = (int)SizeofResource(module, info);
        var data = LockResource(LoadResource(module, info));
        if (data == IntPtr.Zero || size <= 0) return null;
        var bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, size);
        return bytes;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResourceW(IntPtr module, string name, string type);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResourceW(IntPtr module, IntPtr name, string type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr info);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr info);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr data);
}

/// A theme part drawn as Windows draws it: the frame for the element's state cut from the image, its sizing margins
/// kept at their size and the rest stretched (a nine-grid); a part with only per-DPI images draws the one for the
/// screen's DPI at its own size, centred. State 1 is normal, 2 hot, 3 pressed, and so on, as the theme counts them.
internal sealed class ThemePart : FrameworkElement
{
    StartMenuTheme.Part part;
    int state = 1;

    internal StartMenuTheme.Part Part { get => part; set { part = value; InvalidateVisual(); } }
    internal int State { get => state; set { if (state != value) { state = value; InvalidateVisual(); } } }

    internal ThemePart(StartMenuTheme.Part part) { this.part = part; IsHitTestVisible = false; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        if (part == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (part.Image != null) NineGrid(dc, Frame(part, part.Image, state), part.Sizing, new Rect(0, 0, ActualWidth, ActualHeight), part.Stretch);
        else if (part.Fill is { } fill) dc.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ForDpi(part, scale) is { } glyph)
        {
            var g = Frame(part, glyph, state);
            double w = g.PixelWidth / scale, h = g.PixelHeight / scale;
            dc.DrawImage(g, new Rect(Math.Round((ActualWidth - w) / 2), Math.Round((ActualHeight - h) / 2), w, h));
        }
    }

    internal static BitmapSource ForDpi(StartMenuTheme.Part part, double scale)
    {
        BitmapSource chosen = null;
        for (int i = 0; i < part.DpiImages.Length; i++) if (chosen == null || part.MinDpi[i] <= 96 * scale + 0.5) chosen = part.DpiImages[i];
        return chosen;
    }

    /// The image's frame for a state (1-based), the frames side by side or stacked as the part lays them out.
    internal static BitmapSource Frame(StartMenuTheme.Part part, BitmapSource image, int state)
    {
        int count = Math.Max(1, part.ImageCount), index = Math.Clamp(state - 1, 0, count - 1);
        if (count == 1) return image;
        var r = part.Horizontal
            ? new Int32Rect(image.PixelWidth / count * index, 0, image.PixelWidth / count, image.PixelHeight)
            : new Int32Rect(0, image.PixelHeight / count * index, image.PixelWidth, image.PixelHeight / count);
        var cropped = new CroppedBitmap(image, r); cropped.Freeze();
        return cropped;
    }

    /// The image's fixed edges (in its pixels, which are 96-DPI units) kept, the middle stretched over the rest.
    internal static void NineGrid(DrawingContext dc, BitmapSource image, Thickness m, Rect to, bool stretch)
    {
        double w = image.PixelWidth, h = image.PixelHeight;
        if (!stretch) { dc.DrawImage(image, new Rect(to.X + (to.Width - w) / 2, to.Y + (to.Height - h) / 2, w, h)); return; }
        double l = Math.Min(m.Left, to.Width / 2), r = Math.Min(m.Right, to.Width / 2), t = Math.Min(m.Top, to.Height / 2), b = Math.Min(m.Bottom, to.Height / 2);
        double[] sx = [0, m.Left, w - m.Right, w], sy = [0, m.Top, h - m.Bottom, h];
        double[] dx = [to.X, to.X + l, to.Right - r, to.Right], dy = [to.Y, to.Y + t, to.Bottom - b, to.Bottom];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                int cw = (int)(sx[x + 1] - sx[x]), ch = (int)(sy[y + 1] - sy[y]);
                if (cw <= 0 || ch <= 0 || dx[x + 1] <= dx[x] || dy[y + 1] <= dy[y]) continue;
                var piece = new CroppedBitmap(image, new Int32Rect((int)sx[x], (int)sy[y], cw, ch)); piece.Freeze();
                dc.DrawImage(piece, new Rect(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]));
            }
    }
}
