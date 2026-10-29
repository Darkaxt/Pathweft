using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

public partial class MainWindow
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    static bool HasArchiveClipboard() => IsClipboardFormatAvailable(RegisterClipboardFormat(ArchiveClipboardFormat));
    static bool HasTransferClipboard() => HasArchiveClipboard() || IsClipboardFormatAvailable(15);
#if DEBUG
    internal static string ClipboardEffectForVerification;
    internal static byte[] ClipboardDropForVerification;
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    internal static uint ClipboardOwnerProcess()
    { ClipboardWindowProcess(GetClipboardOwner(), out uint process); return process; }
    [DllImport("user32.dll")] internal static extern int CountClipboardFormats();
    internal static bool ProbeClipboard()
    {
        var owner = new System.Windows.Forms.NativeWindow();
        try
        {
            owner.CreateHandle(new System.Windows.Forms.CreateParams { Caption = "Pathweft clipboard availability", Parent = new IntPtr(-3) });
            if (!OpenClipboard(owner.Handle)) return false;
            CloseClipboard(); return true;
        }
        finally { owner.DestroyHandle(); }
    }
#endif
    [DllImport("user32.dll")] static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] static extern uint ClipboardWindowProcess(IntPtr window, out uint process);
    static void OpenTransferClipboard(IntPtr owner)
    {
        if (owner == IntPtr.Zero) throw new IOException("Clipboard access requires its live window owner.");
        if (OpenClipboard(owner)) return;
        int error = Marshal.GetLastWin32Error();
        ClipboardWindowProcess(GetOpenClipboardWindow(), out uint process);
        throw new IOException($"The clipboard is in use (holding process {process}).", new Win32Exception(error));
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(IntPtr memory);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop, uint index, System.Text.StringBuilder name, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);

    // Materialize every transfer format before publication. The OS owns each
    // HGLOBAL after SetClipboardData succeeds; no OLE/managed stream is retained.
    uint WriteTransferClipboard(string[] files, byte[] archiveSelection, bool move, uint expectedSequence)
    {
        var values = new Dictionary<uint, byte[]> { [RegisterClipboardFormat("Preferred DropEffect")] = BitConverter.GetBytes(move ? 2 : 5) };
        if (files is { Length: > 0 })
        {
            byte[] names = System.Text.Encoding.Unicode.GetBytes(string.Join('\0', files) + "\0\0");
            byte[] drop = new byte[checked(20 + names.Length)];
            BitConverter.GetBytes(20).CopyTo(drop, 0); // DROPFILES.pFiles
            BitConverter.GetBytes(1).CopyTo(drop, 16); // DROPFILES.fWide
            names.CopyTo(drop, 20); values[15] = drop;
        }
        if (archiveSelection != null) values[RegisterClipboardFormat(ArchiveClipboardFormat)] = archiveSelection;
        if (values.ContainsKey(0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero) throw new IOException("Clipboard transfer requires its live window owner.");
        var memory = new Dictionary<uint, IntPtr>();
        try
        {
            foreach (var value in values)
            {
                IntPtr handle = GlobalAlloc(0x42, (nuint)value.Value.Length);
                if (handle == IntPtr.Zero) throw new OutOfMemoryException();
                memory.Add(value.Key, handle);
                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { Marshal.Copy(value.Value, 0, pointer, value.Value.Length); }
                finally { GlobalUnlock(handle); }
            }
            OpenTransferClipboard(owner);
            try
            {
                if (GetClipboardSequenceNumber() != expectedSequence) throw new IOException("The clipboard changed while the copy was being prepared; newer contents were preserved.");
                if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
                foreach (uint format in memory.Keys.ToArray())
                {
                    if (SetClipboardData(format, memory[format]) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    memory[format] = IntPtr.Zero;
                }
                return GetClipboardSequenceNumber(); // capture ownership while the clipboard is still locked
            }
            finally { CloseClipboard(); }
        }
        finally { foreach (IntPtr handle in memory.Values) if (handle != IntPtr.Zero) GlobalFree(handle); }
    }
    bool ClearTransferClipboard(uint expectedSequence, out uint clearedSequence)
    {
        clearedSequence = 0;
        IntPtr owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero) return false;
        OpenTransferClipboard(owner);
        try
        {
            if (GetClipboardSequenceNumber() != expectedSequence) return false;
            if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
            clearedSequence = GetClipboardSequenceNumber();
            return true;
        }
        finally { CloseClipboard(); }
    }
    internal (List<string> Paths, bool Move) ReadFileClipboard(out uint sequence)
    {
#if DEBUG
        ClipboardDropForVerification = null; ClipboardEffectForVerification = "not-read";
#endif
        sequence = 0;
        OpenTransferClipboard(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        try
        {
            IntPtr drop = GetClipboardData(15); // CF_HDROP
            if (drop == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (IsClipboardFormatAvailable(15)) throw new IOException($"Advertised clipboard file data is unavailable (Win32 {error}); no transfer was submitted.", new Win32Exception(error));
                sequence = GetClipboardSequenceNumber(); return (new(), false);
            }
            var paths = new List<string>();
            uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(drop, i, null, 0);
                var name = new System.Text.StringBuilder(checked((int)length + 1));
                DragQueryFile(drop, i, name, (uint)name.Capacity);
                paths.Add(name.ToString());
            }
            IntPtr effect = GetClipboardData(RegisterClipboardFormat("Preferred DropEffect"));
#if DEBUG
            ClipboardEffectForVerification = effect == IntPtr.Zero ? "absent" : "unreadable";
#endif
            bool move = false;
            if (effect != IntPtr.Zero && GlobalSize(effect) >= 4)
            {
                IntPtr bytes = GlobalLock(effect);
                if (bytes != IntPtr.Zero)
                {
                    try
                    {
                        move = (Marshal.ReadInt32(bytes) & 2) != 0;
#if DEBUG
                        ClipboardEffectForVerification = $"{GlobalSize(effect)} bytes: {Marshal.ReadInt32(bytes):X8}";
#endif
                    }
                    finally { GlobalUnlock(effect); }
                }
            }
            sequence = GetClipboardSequenceNumber(); return (paths, move);
        }
        finally { CloseClipboard(); }
    }
    // Read the private UTF-8 format under the same native ownership contract as
    // the file list; no delayed OLE provider or managed stream crosses the lock.
    ArchiveClipboard ReadArchiveClipboard(out uint sequence)
    {
        sequence = 0;
        OpenTransferClipboard(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        try
        {
            IntPtr memory = GetClipboardData(RegisterClipboardFormat(ArchiveClipboardFormat));
            if (memory == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (IsClipboardFormatAvailable(RegisterClipboardFormat(ArchiveClipboardFormat))) throw new IOException($"Advertised archive clipboard data is unavailable (Win32 {error}); no transfer was submitted.", new Win32Exception(error));
                sequence = GetClipboardSequenceNumber(); return null;
            }
            int length = checked((int)GlobalSize(memory));
            IntPtr bytes = GlobalLock(memory);
            if (bytes == IntPtr.Zero) throw new IOException("Archive clipboard data is unavailable.");
            try
            {
                byte[] buffer = new byte[length]; Marshal.Copy(bytes, buffer, 0, length);
                // HGLOBAL allocation may include zero-filled alignment bytes.
                while (length > 0 && buffer[length - 1] == 0) length--;
                var selected = System.Text.Json.JsonSerializer.Deserialize<ArchiveClipboard>(buffer.AsSpan(0, length));
                sequence = GetClipboardSequenceNumber(); return selected;
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
}
