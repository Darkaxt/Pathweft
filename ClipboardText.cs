namespace FileExplorer;

/// Text on the clipboard (paths, names, details, hashes, reports). Another program can hold the
/// clipboard open (a clipboard manager or sync tool, remote desktop); Windows then refuses the write
/// after its retries. That is reported where the copy was asked for and never ends Pathweft.
internal static class ClipboardText
{
    /// Null when the text was copied, else the reason it was not.
    internal static string TrySet(string text)
    {
        try { System.Windows.Forms.Clipboard.SetText(text); return null; }
        catch (System.Runtime.InteropServices.ExternalException ex) { return "Nothing copied: another program is using the clipboard (" + ex.Message.TrimEnd('.') + "). Try again."; }
    }

    /// The clipboard's text, or "" when there is none or it can't be read now.
    internal static string TryGet()
    {
        try { return System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : ""; }
        catch (System.Runtime.InteropServices.ExternalException) { return ""; }
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
