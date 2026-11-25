using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer;

// Paste as file (TOOLS-06): the deterministic part of PowerToys Advanced Paste. What is on the clipboard as text, HTML or a
// picture becomes "Pasted text.txt", "Pasted HTML.html" or "Pasted image.png" in the current folder. The clipboard is
// only read (native formats, nothing through OLE, nothing rendered back), so Copy/Cut/Paste of files are untouched.
public partial class MainWindow
{
    const uint CF_UNICODETEXT = 13, CF_DIB = 8, CF_DIBV5 = 17;

    /// Something Paste as file can save: text, HTML or a picture.
    internal static bool CanPasteAsFile() => CanPasteAsFile("txt") || CanPasteAsFile("html") || CanPasteAsFile("png");

    internal static bool CanPasteAsFile(string kind) => kind switch
    {
        "txt" => IsClipboardFormatAvailable(CF_UNICODETEXT),
        "html" => IsClipboardFormatAvailable(RegisterClipboardFormat("HTML Format")),
        "png" => IsClipboardFormatAvailable(RegisterClipboardFormat("PNG")) || IsClipboardFormatAvailable(CF_DIBV5) || IsClipboardFormatAvailable(CF_DIB),
        _ => false,
    };

    /// The folder Paste as file and New from template write into, or null (with the reason in the status line).
    string WritableFolder(PaneView pane, string what)
    {
        if (pane.ArchiveRoot != null || PaneView.IsVirtual(pane.Dir) || !Directory.Exists(pane.Dir))
        { Status.Text = $"{what} needs a folder on disk"; return null; }
        return pane.Dir;
    }

    void PasteAsFile(string kind)
    {
        var pane = active;
        if (WritableFolder(pane, "Paste as file") is not { } dir) return;
        var (stem, ext, label) = kind switch
        {
            "txt" => ("Pasted text", ".txt", "text"),
            "html" => ("Pasted HTML", ".html", "HTML"),
            "png" => ("Pasted image", ".png", "image"),
            _ => throw new ArgumentException("Unknown kind " + kind, nameof(kind)),
        };
        byte[] bytes;
        try { bytes = ReadClipboardAs(kind); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or COMException or FileFormatException)
        { Status.Text = $"Paste as file: {ex.Message}"; return; }
        if (bytes == null) { Status.Text = $"There is no {label} on the clipboard"; return; }
        string path = null;
        try
        {
            // a name another app takes between Unique and CreateNew only moves us one number on
            for (int attempt = 0; ; attempt++)
                try { path = Unique(Path.Combine(dir, stem + ext)); WriteNew(path, bytes); break; }
                catch (IOException) when (attempt < 5 && File.Exists(path)) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status.Text = $"Paste as file: {ex.Message}"; return; }
        Remember($"paste as file {Path.GetFileName(path)}", Made(path));
        ShowCreated(pane, path);
        Status.Text = $"Clipboard {label} saved as {Path.GetFileName(path)}";
    }

    static void WriteNew(string path, byte[] bytes)
    {
        bool made = false;
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            made = true;
            file.Write(bytes);
        }
        catch when (made) { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } throw; }
    }

    /// The file's bytes for `kind`, or null when the clipboard holds nothing of that kind.
    byte[] ReadClipboardAs(string kind)
    {
        IntPtr owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero) throw new IOException("Clipboard access requires its live window owner.");
        // another app may hold the clipboard open for a moment while it writes: a few short tries, then say so
        for (int attempt = 0; !OpenClipboard(owner); attempt++)
        {
            if (attempt == 5)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"{ClipboardText.Holder()} is using the clipboard. Try again.", new Win32Exception(error));
            }
            Thread.Sleep(20);
        }
        try
        {
            switch (kind)
            {
                case "txt":
                    if (ClipboardBytes(CF_UNICODETEXT) is not { } unicode) return null;
                    var text = Encoding.Unicode.GetString(unicode);
                    if (text.IndexOf('\0') is int end and >= 0) text = text[..end];
                    return new UTF8Encoding(false).GetBytes(text);
                case "html":
                    return ClipboardBytes(RegisterClipboardFormat("HTML Format")) is { } cfHtml ? HtmlFile(cfHtml) : null;
                default:
                    if (ClipboardBytes(RegisterClipboardFormat("PNG")) is { } png)
                    {
                        // PNG as the app wrote it, once it is known to decode: nothing lost by encoding again
                        using (var s = new MemoryStream(png)) _ = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
                        return png;
                    }
                    var dib = ClipboardBytes(CF_DIBV5) ?? ClipboardBytes(CF_DIB);
                    return dib == null ? null : EncodePng(DecodeDib(dib));
            }
        }
        finally { CloseClipboard(); }
    }

    /// A copy of one clipboard format's HGLOBAL (the clipboard must be open), or null when it isn't there.
    static byte[] ClipboardBytes(uint format)
    {
        if (!IsClipboardFormatAvailable(format)) return null;
        IntPtr memory = GetClipboardData(format);
        if (memory == IntPtr.Zero) return null;
        nuint size = GlobalSize(memory);
        if (size == 0 || size > int.MaxValue) return null;
        IntPtr pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero) throw new IOException("Clipboard data could not be read.", new Win32Exception(Marshal.GetLastWin32Error()));
        try { var bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { GlobalUnlock(memory); }
    }

    // CF_HTML: an ASCII header of byte offsets into the UTF-8 that follows. The whole document (StartHTML..EndHTML) when
    // the app gave one, else the fragment in a minimal page. UTF-8 is marked with a BOM when the page doesn't say its charset.
    static readonly Regex HtmlOffset = new(@"^(StartHTML|EndHTML|StartFragment|EndFragment):\s*(-?\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    internal static byte[] HtmlFile(byte[] cfHtml)
    {
        int length = Array.IndexOf(cfHtml, (byte)0) is int nul and >= 0 ? nul : cfHtml.Length;
        string header = Encoding.ASCII.GetString(cfHtml, 0, Math.Min(length, 1024));
        var offsets = HtmlOffset.Matches(header).GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => long.Parse(g.First().Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        long Get(string key) => offsets.TryGetValue(key, out var v) ? v : -1;
        bool Valid(long start, long end) => start >= 0 && end > start && end <= length;
        string html;
        if (Valid(Get("StartHTML"), Get("EndHTML")))
            html = Encoding.UTF8.GetString(cfHtml, (int)Get("StartHTML"), (int)(Get("EndHTML") - Get("StartHTML")));
        else if (Valid(Get("StartFragment"), Get("EndFragment")))
            html = "<!DOCTYPE html>\r\n<html><head><meta charset=\"utf-8\"></head><body>\r\n"
                 + Encoding.UTF8.GetString(cfHtml, (int)Get("StartFragment"), (int)(Get("EndFragment") - Get("StartFragment")))
                 + "\r\n</body></html>\r\n";
        else throw new FileFormatException("The HTML on the clipboard has no readable offsets.");
        bool declared = html.Contains("charset", StringComparison.OrdinalIgnoreCase);
        return new UTF8Encoding(!declared).GetPreamble().Concat(Encoding.UTF8.GetBytes(html)).ToArray();
    }

    internal static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    // CF_DIB / CF_DIBV5: a BITMAPINFOHEADER (or V4/V5) with masks, colour table and pixels, no file header.
    // 32-bit pixels are read here so alpha survives (WPF's BMP decoder drops it); an alpha channel that is all zero is
    // the usual "no alpha" of BI_RGB and is made opaque. Every other depth goes through the BMP decoder with a file header.
    internal static BitmapSource DecodeDib(byte[] dib)
    {
        if (dib.Length < 40) throw new FileFormatException("The picture on the clipboard is incomplete.");
        int headerSize = BitConverter.ToInt32(dib, 0), width = BitConverter.ToInt32(dib, 4), height = BitConverter.ToInt32(dib, 8);
        int bits = BitConverter.ToUInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16), used = BitConverter.ToInt32(dib, 32);
        if (headerSize < 40 || headerSize > dib.Length || width <= 0 || height == 0) throw new FileFormatException("The picture on the clipboard has an unsupported header.");
        int maskBytes = headerSize == 40 ? compression == 3 ? 12 : compression == 6 ? 16 : 0 : 0;
        int table = (bits <= 8 ? (used == 0 ? 1 << bits : used) : used) * 4;
        int offset = headerSize + maskBytes + table;
        if (bits == 32 && compression is 0 or 3 or 6)
        {
            uint Mask(int at) => at + 4 <= dib.Length ? BitConverter.ToUInt32(dib, at) : 0;
            uint r = 0xFF0000, g = 0xFF00, b = 0xFF, a = 0xFF000000;
            if (compression is 3 or 6)
            {
                const int at = 40; // V4/V5 hold the masks at 40 in the header; a plain header is followed by them, also at 40
                r = Mask(at); g = Mask(at + 4); b = Mask(at + 8);
                a = headerSize >= 56 || compression == 6 ? Mask(at + 12) : 0;
            }
            else if (headerSize >= 56) a = Mask(52);
            int h = Math.Abs(height);
            long need = offset + (long)width * h * 4;
            if (need > dib.Length) throw new FileFormatException("The picture on the clipboard is incomplete.");
            var pixels = new byte[width * h * 4];
            bool anyAlpha = false;
            for (int y = 0; y < h; y++)
            {
                int source = offset + (height > 0 ? h - 1 - y : y) * width * 4; // positive height: bottom row first
                for (int x = 0; x < width; x++)
                {
                    uint v = BitConverter.ToUInt32(dib, source + x * 4);
                    int target = (y * width + x) * 4;
                    pixels[target] = Channel(v, b); pixels[target + 1] = Channel(v, g); pixels[target + 2] = Channel(v, r);
                    pixels[target + 3] = a == 0 ? (byte)255 : Channel(v, a);
                    anyAlpha |= a != 0 && pixels[target + 3] != 0;
                }
            }
            if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var image = BitmapSource.Create(width, h, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze();
            return image;
        }
        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(14 + offset).CopyTo(file, 10);
        dib.CopyTo(file, 14);
        using var stream = new MemoryStream(file);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    static byte Channel(uint value, uint mask)
    {
        if (mask == 0) return 0;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(mask), width = System.Numerics.BitOperations.PopCount(mask);
        uint v = (value & mask) >> shift;
        return width >= 8 ? (byte)(v >> (width - 8)) : (byte)(v * 255 / ((1u << width) - 1));
    }

    /// The folder background menu's "Paste as file" submenu: one item per kind, each enabled by what the clipboard holds.
    internal IEnumerable<System.Windows.Controls.MenuItem> PasteAsFileMenuItems() => new[]
    {
        Item("Text file", "", "", () => PasteAsFile("txt"), CanPasteAsFile("txt")),
        Item("HTML file", "", "", () => PasteAsFile("html"), CanPasteAsFile("html")),
        Item("Image file", "", "", () => PasteAsFile("png"), CanPasteAsFile("png")),
    };
}
