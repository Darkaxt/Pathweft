using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

/// S21 cards: Actions (BROWSE-04 editor), Privacy and notes (BROWSE-02 clear history, BROWSE-07 stale records).
public partial class SettingsWindow
{
    void BuildBrowsingCards()
    {
        ShowActions();
        ShowPrivacy();
        Activated += (_, _) => ShowPrivacy();
    }

    // ---- Actions ----
    void ShowActions()
    {
        ActionRows.Children.Clear();
        var all = CustomActions.All;
        ActionsStatus.Text = CustomActions.LoadError ?? (all.Count == 0 ? "No actions yet." : "");
        for (int i = 0; i < all.Count; i++)
        {
            int at = i; var a = all[i];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var w in new[] { new GridLength(28), new GridLength(1, GridUnitType.Star), new GridLength(34), new GridLength(34), new GridLength(34), new GridLength(34) })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            row.Children.Add(new TextBlock { Text = a.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = (a.Submenu.Length > 0 ? a.Submenu + " › " : "") + a.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock { Text = $"{Path.GetFileName(a.Executable)} {a.Arguments}  ·  {Describe(a)}", TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Executable + " " + a.Arguments };
            detail.SetResourceReference(StyleProperty, "Hint"); detail.Margin = new Thickness(0);
            text.Children.Add(detail);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            void Icon(int column, string glyph, string tip, bool enabled, Action run)
            {
                var b = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Padding = new Thickness(7, 5, 7, 5), ToolTip = tip, IsEnabled = enabled };
                b.SetResourceReference(StyleProperty, "Ghost");
                System.Windows.Automation.AutomationProperties.SetName(b, tip + " " + a.Name);
                b.Click += (_, _) => run();
                Grid.SetColumn(b, column); row.Children.Add(b);
            }
            Icon(2, "\uE74A", "Move up", at > 0, () => SaveActions(list => (list[at - 1], list[at]) = (list[at], list[at - 1])));
            Icon(3, "\uE74B", "Move down", at < all.Count - 1, () => SaveActions(list => (list[at + 1], list[at]) = (list[at], list[at + 1])));
            Icon(4, "\uE70F", "Edit", true, () => { if (ActionDialog.Ask(this, a, "Edit action") is { } edited) SaveActions(list => list[at] = edited); });
            Icon(5, "\uE74D", "Remove", true, () => { if (PromptDialog.Choose(this, $"Remove the action “{a.Name}”?", "Only the menu entry goes; the program stays.", "Remove", "Cancel") == 0) SaveActions(list => list.RemoveAt(at)); });
            ActionRows.Children.Add(row);
        }
    }
    static string Describe(CustomAction a)
    {
        string what = a.AppliesTo switch { "files" => "files", "folders" => "folders", _ => "files and folders" };
        string count = a.Selection switch { "one" => "one", "several" => "two or more", _ => "any number of" };
        return $"{count} {what}" + (a.AppliesTo != "folders" && a.Patterns != "*" ? $" ({a.Patterns})" : "");
    }
    void SaveActions(Action<List<CustomAction>> change)
    {
        var list = CustomActions.All.ToList();
        change(list);
        try { CustomActions.Save(list); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ActionsStatus.Text = "actions.json could not be saved: " + ex.Message; return; }
        ShowActions();
    }
    void AddAction_Click(object s, RoutedEventArgs e)
    {
        if (ActionDialog.Ask(this, null, "Add action") is { } added) SaveActions(list => list.Add(added));
    }
    void OpenActionsJson_Click(object s, RoutedEventArgs e)
    {
        if (!File.Exists(CustomActions.FilePath)) SaveActions(_ => { });
        try { Process.Start(new ProcessStartInfo(CustomActions.FilePath) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { ActionsStatus.Text = ex.Message; }
    }

    // ---- Privacy and notes ----
    bool healing;
    void ShowPrivacy()
    {
        int recent = Settings.RecentFolders.Count;
        HistoryStatus.Text = recent == 0 ? "No recent folders." : $"{recent} recent folder{(recent == 1 ? "" : "s")}.";
        ClearHistory.IsEnabled = recent > 0;
        int operations = OperationHistory.Store.All.Count;
        OperationsStatus.Text = operations == 0 ? "No operations recorded." : $"{operations:N0} operation{(operations == 1 ? "" : "s")} recorded.";
        ClearOperations.IsEnabled = operations > 0;
        var stale = FileNotes.Stale();
        NotesStatus.Text = !FileNotes.Any ? "No tags, ratings or comments yet."
            : stale.Length == 0 ? "Every record still has its file." : $"{stale.Length} record{(stale.Length == 1 ? "" : "s")} whose file is gone:";
        StaleNotes.ItemsSource = stale;
        PruneNotes.IsEnabled = stale.Length > 0;
        if (FileNotes.Any && !healing)
        {
            healing = true; // follows moved files by ID and refreshes the stale list, off the UI thread
            Task.Run(FileNotes.Heal).ContinueWith(_ => Dispatcher.BeginInvoke(() => { var now = FileNotes.Stale(); StaleNotes.ItemsSource = now; PruneNotes.IsEnabled = now.Length > 0;
                NotesStatus.Text = now.Length == 0 ? "Every record still has its file." : $"{now.Length} record{(now.Length == 1 ? "" : "s")} whose file is gone:"; healing = false; }));
        }
    }
    void ClearHistory_Click(object s, RoutedEventArgs e) { main.ClearHistory(); ShowPrivacy(); }
    void ClearOperations_Click(object s, RoutedEventArgs e) { OperationHistory.Clear(); ShowPrivacy(); }
    void PruneNotes_Click(object s, RoutedEventArgs e)
    {
        int removed = FileNotes.Prune();
        ShowPrivacy();
        NotesStatus.Text = $"Forgot {removed} record{(removed == 1 ? "" : "s")}.";
    }
}
