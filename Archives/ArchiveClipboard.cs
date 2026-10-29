using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

public partial class MainWindow
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    static bool HasArchiveClipboard() => IsClipboardFormatAvailable(RegisterClipboardFormat(ArchiveClipboardFormat));
    static bool HasTransferClipboard() => HasArchiveClipboard() || IsClipboardFormatAvailable(15);
#if DEBUG
    [DllImport("user32.dll")] internal static extern int CountClipboardFormats();
    internal static bool ProbeClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        CloseClipboard(); return true;
    }
#endif
    [DllImport("user32.dll")] static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] static extern uint ClipboardWindowProcess(IntPtr window, out uint process);
    static void OpenTransferClipboard()
    {
        if (OpenClipboard(IntPtr.Zero)) return;
        int error = Marshal.GetLastWin32Error();
        ClipboardWindowProcess(GetOpenClipboardWindow(), out uint process);
        throw new IOException($"The clipboard is in use (holding process {process}).", new Win32Exception(error));
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(IntPtr memory);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop, uint index, System.Text.StringBuilder name, uint size);
    internal static (List<string> Paths, bool Move) ReadFileClipboard()
    {
        OpenTransferClipboard();
        try
        {
            IntPtr drop = GetClipboardData(15); // CF_HDROP
            if (drop == IntPtr.Zero) return (new(), false);
            var paths = new List<string>(); uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
            for (uint index = 0; index < count; index++)
            {
                uint length = DragQueryFile(drop, index, null, 0);
                var name = new System.Text.StringBuilder(checked((int)length + 1));
                DragQueryFile(drop, index, name, length + 1); paths.Add(name.ToString());
            }
            IntPtr effect = GetClipboardData(RegisterClipboardFormat("Preferred DropEffect"));
            bool move = false;
            if (effect != IntPtr.Zero && GlobalSize(effect) >= 4)
            {
                IntPtr bytes = GlobalLock(effect);
                if (bytes != IntPtr.Zero) { try { move = (Marshal.ReadInt32(bytes) & 2) != 0; } finally { GlobalUnlock(effect); } }
            }
            return (paths, move);
        }
        finally { CloseClipboard(); }
    }

    // Read this private UTF-8 format from its actual HGLOBAL. Local .NET 8 OLE
    // read-backs were inconsistent while independent processes read the right data.
    static ArchiveClipboard ReadArchiveClipboard()
    {
        OpenTransferClipboard();
        try
        {
            IntPtr memory = GetClipboardData(RegisterClipboardFormat(ArchiveClipboardFormat));
            if (memory == IntPtr.Zero) return null;
            int length = checked((int)GlobalSize(memory));
            IntPtr bytes = GlobalLock(memory);
            if (bytes == IntPtr.Zero) throw new IOException("Archive clipboard data is unavailable.");
            try
            {
                byte[] buffer = new byte[length]; Marshal.Copy(bytes, buffer, 0, length);
                // HGLOBAL allocation may include zero-filled alignment bytes.
                while (length > 0 && buffer[length - 1] == 0) length--;
                return System.Text.Json.JsonSerializer.Deserialize<ArchiveClipboard>(buffer.AsSpan(0, length));
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
}
