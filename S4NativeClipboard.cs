#if DEBUG
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DataFormats = System.Windows.Forms.DataFormats;

namespace FileExplorer;

// Finite trial rollback for actual HGLOBAL formats. OLE's delayed rendering and
// flush are unsuitable for an exact saved snapshot. Never used by product copy.
internal static class S4NativeClipboard
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
    internal static S4Verification.ClipboardPart[] Read()
    {
        var owner = new System.Windows.Forms.NativeWindow();
        try
        {
            owner.CreateHandle(new System.Windows.Forms.CreateParams { Caption = "Pathweft clipboard snapshot", Parent = new IntPtr(-3) });
            if (!OpenClipboard(owner.Handle)) throw new IOException("Clipboard is busy.", new Win32Exception(Marshal.GetLastWin32Error()));
            try { return ReadOpen(); }
            finally { CloseClipboard(); }
        }
        finally { owner.DestroyHandle(); }
    }
    static S4Verification.ClipboardPart[] ReadOpen()
    {
        var parts = new List<S4Verification.ClipboardPart>();
        uint format = 0;
        while ((format = EnumClipboardFormats(format)) != 0)
        {
            // Bitmap/palette/metafile/private handles cannot be serialized as bytes.
            if (format < 0xC000 && format is not (1 or 6 or 7 or 8 or 11 or 12 or 13 or 15 or 16 or 17)) throw new IOException("Unsupported clipboard handle format; no trial mutation permitted.");
            IntPtr memory = GetClipboardData(format); nuint length = GlobalSize(memory);
            if (memory == IntPtr.Zero || length == 0 || length > int.MaxValue) throw new IOException("Cannot materialize clipboard HGLOBAL; no trial mutation permitted.");
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            byte[] bytes = new byte[(int)length];
            try { Marshal.Copy(pointer, bytes, 0, bytes.Length); }
            finally { GlobalUnlock(memory); }
            parts.Add(new(DataFormats.GetFormat((int)format).Name, "native", Convert.ToBase64String(bytes)));
        }
        return parts.ToArray();
    }
    static Dictionary<uint, byte[]> Bytes(S4Verification.ClipboardPart[] parts)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int locale = parts.FirstOrDefault(p => p.Format == DataFormats.Locale) is { Kind: "stream" } saved ? BitConverter.ToInt32(Convert.FromBase64String(saved.Value)) : System.Globalization.CultureInfo.CurrentCulture.LCID;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
        return parts.ToDictionary(p => (uint)DataFormats.GetFormat(p.Format).Id, p => p.Kind switch
        {
            "native" or "stream" or "bytes" => Convert.FromBase64String(p.Value),
            "text" when p.Format == DataFormats.UnicodeText => Encoding.Unicode.GetBytes(p.Value + '\0'),
            "text" when p.Format == DataFormats.Text => Encoding.GetEncoding(culture.TextInfo.ANSICodePage).GetBytes(p.Value + '\0'),
            "text" when p.Format == DataFormats.OemText => Encoding.GetEncoding(culture.TextInfo.OEMCodePage).GetBytes(p.Value + '\0'),
            _ => throw new IOException("Legacy snapshot format cannot be restored without OLE; original retained.")
        });
    }
    static Dictionary<uint, IntPtr> Allocate(Dictionary<uint, byte[]> data)
    {
        var result = new Dictionary<uint, IntPtr>();
        try
        {
            foreach (var item in data)
            {
                IntPtr memory = GlobalAlloc(0x42, (nuint)item.Value.Length);
                if (memory == IntPtr.Zero) throw new OutOfMemoryException();
                result.Add(item.Key, memory);
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { Marshal.Copy(item.Value, 0, pointer, item.Value.Length); }
                finally { GlobalUnlock(memory); }
            }
            return result;
        }
        catch { Free(result); throw; }
    }
    static void Free(Dictionary<uint, IntPtr> data) { foreach (var pointer in data.Values) if (pointer != IntPtr.Zero) GlobalFree(pointer); }
    static void Publish(Dictionary<uint, IntPtr> data)
    {
        if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
        foreach (uint format in data.Keys.ToArray())
        {
            if (SetClipboardData(format, data[format]) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            data[format] = IntPtr.Zero; // Windows now owns the HGLOBAL.
        }
    }
    internal static int Restore(S4Verification.ClipboardPart[] parts, uint expectedSequence)
    {
        var target = Allocate(Bytes(parts));
        var owner = new System.Windows.Forms.NativeWindow();
        try
        {
            owner.CreateHandle(new System.Windows.Forms.CreateParams { Caption = "Pathweft trial clipboard rollback", Parent = new IntPtr(-3) });
            if (!OpenClipboard(owner.Handle)) return 3; // Windows explicitly rejected before any mutation.
            try
            {
                if (GetClipboardSequenceNumber() != expectedSequence) return 2;
                var rollback = Allocate(Bytes(ReadOpen()));
                try { Publish(target); }
                catch { Publish(rollback); throw; }
                finally { Free(rollback); }
                return 0;
            }
            finally { CloseClipboard(); }
        }
        finally { owner.DestroyHandle(); Free(target); }
    }
}
#endif
