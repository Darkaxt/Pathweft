using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileExplorer;

/// OPUS-05: what Pathweft did to files, kept after the transfer cards close and across restarts. A record is
/// data only (what, when, from where, to where, how it ended): nothing in it is ever run again. Undo stays the
/// existing Undo history's: a record offers Undo only while that history still holds the same operation, and
/// Undo itself checks every item first and leaves anything changed since alone.
internal sealed class OperationRecord
{
    public string What { get; set; } = "";
    public string Result { get; set; } = "done"; // done, partial, failed, cancelled
    public DateTime When { get; set; }
    public List<string> Sources { get; set; } = new();
    public string Destination { get; set; }
    public int Items { get; set; }
    public string Error { get; set; }
    /// The Undo history entry made from the same operation (its time and description), when there is one.
    public DateTime? UndoWhen { get; set; }
    public string UndoWhat { get; set; }

    internal string ResultText => Result switch { "partial" => "Partly done", "failed" => "Failed", "cancelled" => "Cancelled", _ => "Done" };
    internal string Folder => Destination is { Length: > 0 } d ? d : Sources.Count > 0 ? Path.GetDirectoryName(Sources[0].TrimEnd('\\')) : null;
}

internal static class OperationHistory
{
    internal const int MaxRecords = 500;
    internal static readonly TimeSpan Kept = TimeSpan.FromDays(90);
    internal static readonly JsonStore<OperationRecord> Store = new(() => Path.Combine(DataFolder.Bulk, "history.json"), Bound);

    /// Newest first, at most 500, none older than 90 days.
    static List<OperationRecord> Bound(List<OperationRecord> list)
    {
        var since = DateTime.Now - Kept;
        return list.Where(r => r.When >= since).OrderByDescending(r => r.When).Take(MaxRecords).ToList();
    }

    internal static void Add(OperationRecord record) => Store.Replace(Bound(Store.All.Prepend(record).ToList()));
    internal static void Clear() => Store.Replace(new());
}

public partial class MainWindow
{
    bool rememberingJob;

    /// An operation that isn't a transfer (rename, new folder, delete to the Recycle Bin…): recorded as the Undo
    /// history records it.
    void RecordOperation(UndoEntry entry)
    {
        if (rememberingJob || entry == null) return;
        var paths = entry.Steps.Select(s => s.Path).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        OperationHistory.Add(new OperationRecord
        {
            What = Capital(entry.What), When = entry.When, Sources = paths.Take(50).ToList(), Items = paths.Count,
            UndoWhen = entry.When, UndoWhat = entry.What,
        });
    }

    /// A finished transfer, with its real outcome: partly done, failed and cancelled ones too.
    async Task RecordJob(Job job, Task<UndoEntry> undone)
    {
        UndoEntry entry = null;
        try { entry = await undone; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        string result = job.Phase == Phase.Cancelled || job.IsCancelled ? "cancelled" : job.Successful ? "done" : job.Phase == Phase.Failed && job.DoneFiles == 0 ? "failed" : "partial";
        int items = job.Sources?.Count ?? 0;
        string what = job.Description is { Length: > 0 } d ? d
            : $"{(job.IsMove ? "Move" : "Copy")} of {(items == 1 ? Path.GetFileName(job.Sources[0].TrimEnd('\\')) : $"{items:N0} items")}";
        OperationHistory.Add(new OperationRecord
        {
            What = what, Result = result, When = DateTime.Now, Sources = (job.Sources ?? new()).Take(50).ToList(), Destination = job.Destination, Items = items,
            Error = job.Errors.TryPeek(out var first) ? first : null, UndoWhen = entry?.When, UndoWhat = entry?.What,
        });
    }

    static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    UndoEntry UndoFor(OperationRecord r) => r.UndoWhen is { } when ? undo.LastOrDefault(u => u.When == when && u.What == r.UndoWhat) : null;

    /// History → Operation history…: the records, newest first, with Open folder and Undo.
    internal void ShowOperationHistory() => new OperationHistoryWindow(this).Present();

    internal sealed class OperationHistoryWindow : AuxDialog
    {
        readonly MainWindow main;
        readonly ListBox list = new() { Height = 360, Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly Button open, undoButton;
        readonly TextBlock detail = Note("");

        internal OperationHistoryWindow(MainWindow main) : base(main, "Operation history", "Close", 640)
        {
            this.main = main;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            foreach (var b in Footer.Children.OfType<Button>().Where(b => b != Accept)) b.Visibility = Visibility.Collapsed; // one Close, no Cancel
            System.Windows.Automation.AutomationProperties.SetName(list, "Operations, newest first");
            Body.Children.Add(Label($"The last {OperationHistory.MaxRecords} operations of the last {OperationHistory.Kept.Days} days. Nothing here is run again by itself.", 0));
            Body.Children.Add(list);
            Body.Children.Add(detail);
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(open = Action("Open folder", OpenFolder));
            actions.Children.Add(undoButton = Action("Undo…", UndoSelected));
            actions.Children.Add(Action("Clear history", () => { OperationHistory.Clear(); Fill(); }));
            Body.Children.Add(actions);
            list.SelectionChanged += (_, _) => Describe();
            list.MouseDoubleClick += (_, e) => { if (list.SelectedItem != null) { OpenFolder(); e.Handled = true; } };
            Fill();
        }

        void Fill()
        {
            list.Items.Clear();
            foreach (var r in OperationHistory.Store.All)
            {
                var row = new DockPanel { Tag = r, Margin = new Thickness(2, 3, 2, 3) };
                var state = new TextBlock { Text = r.ResultText, Width = 82, Opacity = r.Result == "done" ? 0.7 : 1 };
                if (r.Result != "done") state.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                var when = new TextBlock { Text = r.When.ToString("g"), Opacity = 0.7, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(state, Dock.Left); DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(state); row.Children.Add(when);
                row.Children.Add(new TextBlock { Text = r.What + (r.Destination is { Length: > 0 } d ? "  →  " + d : ""), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Tip(r) });
                list.Items.Add(new ListBoxItem { Content = row, Tag = r });
            }
            if (list.Items.Count == 0) list.Items.Add(new ListBoxItem { Content = "No operations recorded yet", IsEnabled = false });
            else list.SelectedIndex = 0;
            Describe();
        }

        static string Tip(OperationRecord r) =>
            string.Join("\n", new[] { r.What, r.ResultText + "  ·  " + r.When.ToString("F") }
                .Concat(r.Sources.Take(8).Select(s => "From " + s)).Concat(r.Items > 8 ? new[] { $"…and {r.Items - 8:N0} more" } : Array.Empty<string>())
                .Concat(r.Destination is { Length: > 0 } d ? new[] { "To " + d } : Array.Empty<string>())
                .Concat(r.Error is { Length: > 0 } e ? new[] { "First problem: " + e } : Array.Empty<string>()));

        OperationRecord Selected => (list.SelectedItem as ListBoxItem)?.Tag as OperationRecord;

        void Describe()
        {
            var r = Selected;
            open.IsEnabled = r?.Folder != null;
            var entry = r == null ? null : main.UndoFor(r);
            undoButton.IsEnabled = entry != null;
            detail.Text = r == null ? "" : (r.Error is { Length: > 0 } e ? "First problem: " + e + "\n" : "")
                + (entry != null ? "Undo is available: each item is checked first, and anything changed since is left alone."
                   : r.UndoWhen != null ? "Undo is no longer available for this operation (newer operations replaced it, or it was undone)."
                   : "This operation has nothing to undo.");
        }

        void OpenFolder()
        {
            if (Selected?.Folder is not { } folder) return;
            if (!Directory.Exists(folder)) { detail.Text = "That folder is not there now: " + folder; return; }
            main.active.Navigate(folder); main.active.FocusList(); Close();
        }

        void UndoSelected()
        {
            if (Selected is not { } r || main.UndoFor(r) is not { } entry) return;
            if (PromptDialog.Choose(this, $"Undo {entry.What}?", "Each item is checked first; anything changed since stays as it is.", "Undo", "Cancel") != 0) return;
            main.Undo(entry); Close();
        }
    }
}
