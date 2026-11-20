using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace FileExplorer;

/// OPUS-08: picking images while looking at them. The preview's Mark button (M in Quick View) puts the image
/// shown into the "Marked images" collection or takes it out; the arrows beside it (Shift+Up/Down in Quick
/// View) go to the previous or next marked image of the listing. A mark is a reference in that collection: it
/// is not the selection, not a rating and not the preview's pin, and marking never moves or changes a file.
public partial class MainWindow
{
    Entry PreviewedEntry => quick != null ? quickPreview?.Entry : sidePreview?.Entry;
    PaneView MarkSource => quick != null ? quickSource : active;
    static bool Markable(Entry e) => e is { IsDir: false, IsUp: false } && !PaneView.IsVirtual(e.Path) && !RemoteAddress.IsRemote(e.Path) && ResizableImage(e);
    static HashSet<string> MarkedPaths() => (Collections.Find(Collections.Marked)?.Items ?? new()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal bool ToggleMark()
    {
        if (PreviewedEntry is not { } e || !Markable(e)) return false;
        bool marked = Collections.Contains(Collections.Marked, e.Path);
        if (marked) Collections.Remove(Collections.Marked, new[] { e.Path });
        else Collections.Add(Collections.Marked, new[] { e.Path });
        Status.Text = marked ? $"Unmarked {e.Name}" : $"Marked {e.Name}  ·  {Collections.Find(Collections.Marked)?.Items.Count ?? 0:N0} in Marked images";
        UpdateMarkButtons();
        return true;
    }

    /// The previous (-1) or next (+1) marked image in the listing's order, wrapping around.
    internal bool StepMarked(int step)
    {
        var pane = MarkSource;
        if (pane == null) return false;
        var marked = MarkedPaths();
        var rows = pane.ListingRows;
        if (marked.Count == 0 || rows.Count == 0) { Status.Text = "No marked images"; return false; }
        int at = pane.List.SelectedItem is Entry current ? rows.ToList().IndexOf(current) : -1;
        int start = at >= 0 ? at : step > 0 ? -1 : rows.Count;
        for (int n = 1; n <= rows.Count; n++)
        {
            int i = ((start + step * n) % rows.Count + rows.Count) % rows.Count;
            if (i == at || !marked.Contains(rows[i].Path)) continue;
            pane.List.SelectedItem = rows[i]; pane.List.ScrollIntoView(rows[i]);
            PreviewFollow();
            return true;
        }
        Status.Text = at >= 0 && marked.Contains(rows[at].Path) ? "This is the only marked image here" : "No marked images in this listing";
        return false;
    }

    void UpdateMarkButtons()
    {
        if (PreviewMarkButton == null) return;
        var e = sidePreview?.Entry;
        bool can = Markable(e);
        var visibility = can ? Visibility.Visible : Visibility.Collapsed;
        PreviewMarkButton.Visibility = PreviewMarkPrevButton.Visibility = PreviewMarkNextButton.Visibility = visibility;
        if (!can) return;
        bool marked = Collections.Contains(Collections.Marked, e.Path);
        PreviewMarkButton.Tag = marked ? "on" : null;
        PreviewMarkButton.Content = marked ? "" : "";
        string tip = marked ? "Unmark this image" : "Mark this image";
        PreviewMarkButton.ToolTip = tip; AutomationProperties.SetName(PreviewMarkButton, tip);
        AutomationProperties.SetItemStatus(PreviewMarkButton, marked ? "Marked" : "Not marked");
    }

    void PreviewMark_Click(object sender, RoutedEventArgs e) => ToggleMark();
    void PreviewMarkPrev_Click(object sender, RoutedEventArgs e) => StepMarked(-1);
    void PreviewMarkNext_Click(object sender, RoutedEventArgs e) => StepMarked(1);

    /// Quick View's keys: M marks or unmarks, Shift+Up/Down go to the previous/next marked image.
    bool MarkKey(Key key)
    {
        if (key == Key.M && Keyboard.Modifiers == ModifierKeys.None) return ToggleMark() || true;
        if (key is Key.Up or Key.Down && Keyboard.Modifiers == ModifierKeys.Shift) { StepMarked(key == Key.Down ? 1 : -1); return true; }
        return false;
    }
}
