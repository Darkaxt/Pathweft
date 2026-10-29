using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

public partial class MainWindow
{
    Job lastArchiveJob;
    Task archiveOpenCompletion = Task.CompletedTask;
    List<Entry> ArchiveSelection(PaneView pane) => pane.Selected;
    Job SubmitArchiveEdit(IEnumerable<string> sources, string destination, string description, Action<Job> work) =>
        TrackSubmittedJob(Ferry.Submit(sources, destination, false, work, description));

    void ExtractFromArchive(PaneView pane, string destination) => Submit(pane.Selected.Select(e => e.Path).ToList(), destination, false);
    void ExtractFromArchiveTo(PaneView pane)
    {
        var pick = new Microsoft.Win32.OpenFolderDialog { Title = "Extract to which folder?" };
        if (pick.ShowDialog(this) == true) ExtractFromArchive(pane, pick.FolderName);
    }
    void OpenFromArchive(PaneView pane, Entry entry) => archiveOpenCompletion = OpenArchiveEntry(pane, entry);
#if DEBUG
    internal Func<string, Process> ArchiveConsumerForVerification = null;
#endif
    async Task OpenArchiveEntry(PaneView pane, Entry entry, bool editor = false)
    {
        if (pane.ArchiveRoot is not { } at) return;
        ArchiveContent.Lease copy = null;
        try
        {
            copy = await Task.Run(() => ArchiveContent.Extract(at.Archive, new[] { entry.Name }, at.Inner, "opened file"));
#if DEBUG
            if (ArchiveConsumerForVerification != null) { ArchiveConsumerForVerification(copy.File); }
            else
#endif
            if (editor || Settings.InternalEditor && EditorWindow.IsText(copy.File) && !Runnable.Contains(Path.GetExtension(copy.File))) EditorWindow.Open(copy.File);
            else Process.Start(new ProcessStartInfo(copy.File) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(copy.File) });
            Status.Text = "Opened an extracted copy. Changes are not written into the archive. Release it after closing its app in Settings → Archive working copies.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { copy?.Dispose(); Status.Text = "Archive open failed: " + ex.Message; }
    }
    void DeleteFromArchive(PaneView pane)
    {
        if (pane.ArchiveRoot is not { } at || pane.Selected.Count == 0) return;
        if (Archive.ChangeReason(at.Archive) is { Length: > 0 } reason) { Status.Text = reason; return; }
        if (PromptDialog.Choose(this, $"Delete {pane.Selected.Count} item(s) from {Path.GetFileName(at.Archive)}?", "The original archive is replaced only after the staged edit is verified. Deleted entries do not go to the Recycle Bin.", "Delete", "Cancel") != 0) return;
        var names = pane.Selected.Select(e => e.Name).ToArray();
        SubmitArchiveEdit(pane.Selected.Select(e => e.Path), pane.Dir, "Delete archive entries", job => Archive.Delete(at.Archive, names, at.Inner, job));
    }
    void ArchivePassword(PaneView pane)
    {
        if (pane.ArchiveRoot is not { } at) return;
        var password = new PasswordBox { Name = "ArchivePassword", Margin = new(0, 12, 0, 12) };
        var open = new Button { Name = "ArchivePasswordOpen", Content = "Open archive", Style = (Style)FindResource("Ghost"), IsDefault = true, Padding = new(12, 6, 12, 6) };
        var panel = new StackPanel { Margin = new(20), LayoutTransform = ArchiveTextScale() };
        panel.Children.Add(new TextBlock { Text = "Password for " + Path.GetFileName(at.Archive), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(password); panel.Children.Add(new TextBlock { Text = "Kept only in memory for this session. Editing depends on the archive's preservation capabilities.", TextWrapping = TextWrapping.Wrap, Foreground = Hex("#8A97AA") }); panel.Children.Add(open);
        var dialog = new Window { Owner = this, Title = "Archive password", Content = panel, Width = 440, SizeToContent = SizeToContent.Height, Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        System.Windows.Automation.AutomationProperties.SetName(password, "Archive password");
        dialog.Loaded += (_, _) =>
        {
#if DEBUG
            if (HiddenTrialPresentation.Enabled) return;
#endif
            password.Focus();
        };
#if DEBUG
        HiddenTrialPresentation.Prepare(dialog);
#endif
        open.Click += (_, _) => dialog.DialogResult = true;
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) dialog.Close(); };
        if (dialog.ShowDialog() == true) { Archive.SetPassword(at.Archive, password.Password); pane.Refresh(); }
        password.Clear();
    }
    Window archiveCopiesWindow;
    Action refreshArchiveCopies;
    static System.Windows.Media.Transform ArchiveTextScale() => Settings.TextScale > 1 ? new System.Windows.Media.ScaleTransform(Settings.TextScale, Settings.TextScale) : null;
    void ApplyArchiveAppearance()
    {
        if (archiveCopiesWindow?.Content is FrameworkElement content) content.LayoutTransform = ArchiveTextScale();
    }
    internal void ShowArchiveWorkingCopies()
    {
        if (archiveCopiesWindow != null) { refreshArchiveCopies(); archiveCopiesWindow.Show(); return; }
        var list = new StackPanel();
        var panel = new DockPanel { Margin = new(20) };
        var description = new TextBlock { Text = "Archive working copies\nClose the app or clipboard consumer before marking a copy closed. Copies remain across Pathweft exits; app-launcher exit alone cannot prove that an external consumer finished. Changes to a copy are not written back to its archive.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) };
        DockPanel.SetDock(description, Dock.Top); panel.Children.Add(description);
        panel.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var window = archiveCopiesWindow = new Window { Owner = this, Title = "Archive working copies", Width = 760, Height = 540, MinWidth = 420, MinHeight = 320, Content = panel, Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily, ShowInTaskbar = false };
#if DEBUG
        HiddenTrialPresentation.Prepare(window);
#endif
        void RefreshCopies()
        {
            list.Children.Clear();
            foreach (var record in ArchiveContent.Retained())
            {
                var row = new StackPanel { Margin = new(0, 4, 0, 16) };
                var label = new TextBlock { Text = record.Purpose + " · " + record.Source + "\n" + string.Join(", ", record.Files.Select(f => f.StartsWith("data\\", StringComparison.Ordinal) ? f[5..] : f)), TextWrapping = TextWrapping.Wrap };
                var release = new Button { Content = "Mark closed and release", Tag = record, Style = (Style)FindResource("Ghost"), Padding = new(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 0) };
                release.Click += (_, _) =>
                {
                    try
                    {
                        string directory = Path.Combine(ArchiveContent.Root, record.Id);
                        if (record.Files.Any(f => EditorWindow.OwnsArchiveCopy(Path.Combine(directory, f)))) throw new IOException("Close the editor tab before releasing this copy.");
                        ArchiveContent.Release(record); RefreshCopies();
                    }
                    catch (IOException ex) { label.Text = ex.Message; }
                };
                row.Children.Add(label); row.Children.Add(release); list.Children.Add(row);
            }
            if (list.Children.Count == 0) list.Children.Add(new TextBlock { Text = "No retained archive working copies." });
        }
        refreshArchiveCopies = RefreshCopies;
        window.Closed += (_, _) => { archiveCopiesWindow = null; refreshArchiveCopies = null; };
        ApplyArchiveAppearance(); RefreshCopies(); window.Show();
    }
}
