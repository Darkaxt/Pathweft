using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FileExplorer;

/// BROWSE-08: the pane's embedded terminal, docked under its list. One session per pane, started the first
/// time the panel is shown and kept across tab switches and hiding; CloseView (pane closed, app exit) ends it.
/// Ctrl+` keeps opening the external terminal.
public partial class PaneView
{
    TerminalPanel terminal;
    internal bool TerminalShown => TerminalHost.Visibility == Visibility.Visible;

    internal void ToggleTerminal()
    {
        if (TerminalShown) { TerminalHost.Visibility = Visibility.Collapsed; FocusList(); return; }
        if (terminal == null)
        {
            terminal = new TerminalPanel();
            terminal.FolderReported += path =>
            {
                // the shell went somewhere: the pane follows (FollowFolder then skips the echo)
                if (!path.Equals(Dir, StringComparison.OrdinalIgnoreCase)) Navigate(path);
            };
            TerminalHost.Children.Add(terminal);
        }
        TerminalHost.Height = Math.Clamp(Settings.TerminalHeight, 120, Math.Max(120, ActualHeight - 160));
        SyncTerminal(); // the folder first: the session starts there when the panel shows
        TerminalHost.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => terminal?.FocusShell(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// The browser moved: a local folder is followed, any other place says why it is not.
    void SyncTerminal()
    {
        if (terminal == null) return;
        string dir = Dir;
        if (InSearch || ArchiveRoot != null || dir == ThisPC || string.IsNullOrEmpty(dir) || RemoteAddress.IsRemote(dir) || IsVirtual(dir)) terminal.ShowUnsupported("not a local folder");
        else terminal.FollowFolder(dir);
    }

#if DEBUG
    internal TerminalPanel TerminalForVerification => terminal;
    internal void CloseTerminalForVerification() => CloseTerminal();
#endif
    void CloseTerminal()
    {
        terminal?.Dispose();
        terminal = null;
    }

    void TerminalSplitter_DragDelta(object s, DragDeltaEventArgs e)
    {
        double height = Math.Clamp(TerminalHost.ActualHeight - e.VerticalChange, 120, Math.Max(120, ActualHeight - 160));
        TerminalHost.Height = height;
        Settings.TerminalHeight = height;
    }

    /// The window's shortcuts stay out of a focused terminal (Tab, Ctrl+W, F-keys belong to the shell).
    internal static bool InTerminal(DependencyObject d)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TerminalPanel) return true;
        return false;
    }
}
