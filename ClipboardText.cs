using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileExplorer;

/// Text on the clipboard (paths, names, details, hashes, reports). Another program can hold the
/// clipboard open (a clipboard manager or sync tool, remote desktop); Windows then refuses the write
/// after its retries. That is reported where the copy was asked for, naming the program holding it
/// when Windows says which, and never ends Pathweft.
internal static class ClipboardText
{
    [DllImport("user32.dll")] static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    /// Null when the text was copied, else the reason it was not.
    internal static string TrySet(string text)
    {
        try { System.Windows.Forms.Clipboard.SetText(text); return null; }
        catch (ExternalException) { return "Nothing copied: " + Holder() + " is using the clipboard. Try again."; }
    }

    /// The clipboard's text, or "" when there is none or it can't be read now.
    internal static string TryGet()
    {
        try { return System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : ""; }
        catch (ExternalException) { return ""; }
    }

    /// The program holding the clipboard open, as far as Windows tells (a window-less holder can't be named).
    internal static string Holder()
    {
        IntPtr window = GetOpenClipboardWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint id) == 0 || id == 0) return "another program";
        if (id == Environment.ProcessId) return "Pathweft";
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName + " (process " + id + ")"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return "process " + id; }
    }
}

public partial class MainWindow
{
    /// Copies text and shows `done`, or why nothing was copied, in the status bar.
    internal void PutText(string text, string done)
    {
        string error = ClipboardText.TrySet(text);
        if (error != null || done != null) Status.Text = error ?? done;
    }
}
