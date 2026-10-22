using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// The Drop bin (1.0.43): files and folders gathered from anywhere while browsing — dragged onto the bin, or
// Ctrl+B on a selection — then copied or moved together into the active pane's folder. Paths only: nothing
// moves until Copy or Move is pressed, and the bin lasts for the session.
public partial class MainWindow
{
    readonly List<string> bin = new();

    void AddToBin(IEnumerable<string> paths)
    {
        int before = bin.Count;
        foreach (var p in paths)
            if (!bin.Contains(p, StringComparer.OrdinalIgnoreCase)) bin.Add(p);
        DropBin.Visibility = Visibility.Visible;
        ShowBin();
        Status.Text = bin.Count == before ? "Already in the Drop bin" : $"{bin.Count - before} added to the Drop bin";
    }

    void ShowBin()
    {
        BinCount.Text = bin.Count > 0 ? bin.Count.ToString() : "";
        BinActions.Visibility = bin.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BinList.Children.Clear();
        foreach (var path in bin)
        {
            var row = new DockPanel { Margin = new(0, 1, 0, 1), ToolTip = path };
            var remove = new Button { Style = (Style)FindResource("Ghost"), Content = "", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"), FontSize = 9, ToolTip = "Take it out of the bin" };
            System.Windows.Automation.AutomationProperties.SetName(remove, "Take " + Path.GetFileName(path.TrimEnd('\\')) + " out of the bin");
            remove.Click += (_, _) => { bin.Remove(path); ShowBin(); };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(ShellIcon(path, Directory.Exists(path) ? "" : "", 16, new(0, 0, 8, 0)));
            row.Children.Add(new TextBlock { Text = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            BinList.Children.Add(row);
        }
        ShowBinTarget();
    }

    /// The summary names the folder the buttons would send everything to: the active pane's, which changes as you browse.
    void ShowBinTarget()
    {
        if (bin.Count == 0) { BinSummary.Text = "Drag files here from any folder, or press Ctrl+B on a selection. Then copy or move them all at once."; return; }
        int folders = bin.Select(p => Path.GetDirectoryName(p.TrimEnd('\\')) ?? p).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var into = active?.Dir is { Length: > 0 } d ? (Path.GetFileName(d.TrimEnd('\\')) is { Length: > 0 } n ? n : d) : "?";
        BinSummary.Text = $"{bin.Count} item{(bin.Count == 1 ? "" : "s")} from {folders} folder{(folders == 1 ? "" : "s")}  ·  into {into}";
    }

    // Copy, never Move, as the drop's effect: the bin only keeps the paths, and a source told "moved" (Explorer)
    // could delete what it sent.
    void Bin_DragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Bin_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) AddToBin(files);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    void BinButton_Click(object s, RoutedEventArgs e)
    {
        DropBin.Visibility = DropBin.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        ShowBin();
    }

    void BinClear_Click(object s, RoutedEventArgs e) { bin.Clear(); ShowBin(); }
    void BinCopy_Click(object s, RoutedEventArgs e) => SendBin(move: false);
    void BinMove_Click(object s, RoutedEventArgs e) => SendBin(move: true);

    /// Everything still on disk goes to the active pane's folder as one transfer; the bin empties.
    void SendBin(bool move)
    {
        var there = bin.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        int gone = bin.Count - there.Count;
        if (there.Count > 0) Submit(there, active.Dir, move);
        bin.Clear();
        ShowBin();
        DropBin.Visibility = Visibility.Collapsed;
        if (gone > 0) Status.Text = $"{gone} item{(gone == 1 ? " was" : "s were")} no longer there and stayed out";
    }
}
