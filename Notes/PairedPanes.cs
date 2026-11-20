using System.IO;

namespace FileExplorer;

/// OPUS-02: linked relative navigation. Linking records the two panes' current folders as roots; going into a
/// folder below one root takes the other pane to the same relative folder below its root. A counterpart that
/// doesn't exist is said in that pane, which stays where it is: nothing is created, copied or synchronized.
/// The link belongs to the panes, not to a tab: while a pane shows a folder outside its root (another tab, or
/// a folder elsewhere) following pauses, and it resumes as soon as that pane is back below its root.
/// Re-link at current folders moves both roots (rebase); Unlink ends it. Closing tabs never changes it.
/// Session-only, unless saved in a workspace.
public partial class MainWindow
{
    internal (string Left, string Right)? paneLink;
    bool followingLink, linkPaused;
    internal bool PanesLinked => paneLink != null;

    internal void LinkPanes() => LinkPanes(Left.Dir, Right.Dir, quiet: false);

    internal void LinkPanes(string left, string right, bool quiet)
    {
        if (Settings.SinglePane) { Status.Text = "Show both panes to link them"; return; }
        static bool Usable(string p) => !string.IsNullOrEmpty(p) && p != PaneView.ThisPC && !PaneView.IsVirtual(p);
        if (!Usable(left) || !Usable(right)) { Status.Text = "Open a folder in each pane to link them"; return; }
        paneLink = (left.TrimEnd('\\'), right.TrimEnd('\\'));
        linkPaused = false;
        // a workspace's link: each pane goes to its root when it is not already below it
        foreach (var (pane, root) in new[] { (Left, paneLink.Value.Left), (Right, paneLink.Value.Right) })
            if (Relative(root, pane.Dir) == null && Directory.Exists(root)) { followingLink = true; try { pane.Navigate(root); } finally { followingLink = false; } }
        if (!quiet) Status.Text = $"Panes linked: {paneLink.Value.Left}  ⇄  {paneLink.Value.Right}";
    }

    internal void UnlinkPanes(string why = null)
    {
        if (paneLink == null) return;
        paneLink = null; linkPaused = false;
        Status.Text = why ?? "Panes unlinked";
    }

    /// The path below root ("" for root itself), or null when path is not inside root.
    static string Relative(string root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var p = path.TrimEnd('\\');
        if (p.Equals(root, StringComparison.OrdinalIgnoreCase)) return "";
        return p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length + 1) : null;
    }

    void FollowLink(PaneView pane, string path)
    {
        if (paneLink is not { } link || followingLink || Settings.SinglePane) return;
        bool left = pane == Left;
        var other = left ? Right : Left;
        var (root, otherRoot) = left ? (link.Left, link.Right) : (link.Right, link.Left);
        var rel = Relative(root, path);
        if (rel == null)
        {
            // said in the pane that stays (the status bar is replaced as soon as the folder changes)
            if (!linkPaused) { linkPaused = true; other.ShowNotice($"Linked navigation paused: the {(left ? "left" : "right")} pane is outside {root}"); }
            return;
        }
        if (linkPaused) { linkPaused = false; other.ClearNotice(); }
        if (Relative(otherRoot, other.Dir) == null) return; // the other pane is elsewhere (another tab): it is left alone
        var target = rel.Length == 0 ? otherRoot : Path.Combine(otherRoot, rel);
        if (other.Dir.TrimEnd('\\').Equals(target, StringComparison.OrdinalIgnoreCase)) return;
        if (!Directory.Exists(target)) { other.ShowNotice($"No matching folder: {target}"); return; }
        followingLink = true;
        try { other.Navigate(target); }
        finally { followingLink = false; }
    }
}
