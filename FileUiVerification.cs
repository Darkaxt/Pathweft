#if DEBUG
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyFileUi(string root)
    {
        root = Path.GetFullPath(root);
        if (!root.StartsWith(@"D:\Temp\pathweft-s4-ui\", StringComparison.OrdinalIgnoreCase) || !HiddenTrialPresentation.Enabled) throw new ArgumentException("Isolated hidden file UI trial required.");
        string evidence = Path.Combine(root, "evidence"); Directory.CreateDirectory(evidence);
        var checks = new List<object>(); int result = 1;
        void Check(bool pass, string criterion)
        {
            checks.Add(new { criterion, passed = pass });
            HiddenTrialPresentation.Phase = criterion;
            File.WriteAllText(Path.Combine(evidence, "phase.json"), JsonSerializer.Serialize(new { criterion, pass }));
            if (!pass) throw new InvalidOperationException(criterion);
        }
        IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T found) yield return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Children<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        async Task Changed(PaneView pane, Action action, Func<bool> predicate = null)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Listed() { if (pane.Listed && (predicate?.Invoke() ?? true)) done.TrySetResult(); }
            void Failed(string error) => done.TrySetException(new IOException(error));
            pane.StatsChanged += Listed; pane.Error += Failed;
            try { pane.Listed = false; action(); await done.Task; }
            finally { pane.StatsChanged -= Listed; pane.Error -= Failed; }
        }
        async Task Hidden(object source)
        {
            if (Left.InSearch)
            {
                Check(HandleHiddenShortcut(Key.H, ModifierKeys.Control, false, source), "Production Ctrl+H handler accepts search context");
                await Left.SearchCompletion;
            }
            else await Changed(Left, () => Check(HandleHiddenShortcut(Key.H, ModifierKeys.Control, false, source), "Production Ctrl+H handler accepts browser context"));
            if (Right.InSearch) await Right.SearchCompletion;
            else if (!Right.Listed) await Changed(Right, () => { });
        }
        async Task HasThumb(Entry entry)
        {
            if (entry.Thumb != null) return;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Change(object s, System.ComponentModel.PropertyChangedEventArgs e) { if (entry.Thumb != null) done.TrySetResult(); }
            entry.PropertyChanged += Change;
            try { Change(null, null); await done.Task; }
            finally { entry.PropertyChanged -= Change; }
        }
        try
        {
            Check(HiddenTrialPresentation.ForegroundObserverActive && HiddenTrialPresentation.IsOutsideDisplays(this), "Actual offscreen window and foreground observer active");
            string folder = Path.Combine(root, "fixtures"), gallery = Path.Combine(folder, "gallery"), other = Path.Combine(root, "other");
            Directory.CreateDirectory(gallery); Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(folder, "ordinary.file.txt"), "ordinary");
            File.WriteAllText(Path.Combine(folder, "ordinary.other.txt"), "ordinary");
            File.WriteAllText(Path.Combine(folder, ".env"), "dot only");
            File.WriteAllText(Path.Combine(folder, "windows-hidden.txt"), "attribute only");
            File.SetAttributes(Path.Combine(folder, "windows-hidden.txt"), FileAttributes.Hidden);
            foreach (string name in new[] { ".dot-dir", "windows-hidden-dir" })
            {
                string dir = Directory.CreateDirectory(Path.Combine(folder, name)).FullName;
                File.WriteAllText(Path.Combine(dir, "pwfui_hit_descendant.txt"), "hidden descendant");
                if (name == "windows-hidden-dir") File.SetAttributes(dir, FileAttributes.Hidden);
            }
            for (int i = 0; i < 210; i++) File.WriteAllText(Path.Combine(folder, $".pwfui_hit_{i:000}.txt"), "hidden page");
            for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(folder, $"pwfui_hit_visible{i}.txt"), "visible page");
            File.WriteAllText(Path.Combine(other, ".cached-dot"), "cached visibility");
            File.WriteAllText(Path.Combine(gallery, "editable.txt"), "normal");
            File.WriteAllText(Path.Combine(gallery, "read-only.txt"), "read only");
            File.SetAttributes(Path.Combine(gallery, "read-only.txt"), FileAttributes.ReadOnly);
            Directory.CreateDirectory(Path.Combine(gallery, "Assets")); File.SetAttributes(Path.Combine(gallery, "Assets"), FileAttributes.ReadOnly);
            byte[] pixels = { 10, 80, 240, 255, 250, 20, 60, 128, 100, 130, 20, 0 };
            var color = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 12); color.Freeze();
            var gray = (BitmapSource)ReadOnlyIcons.Gray(color); var output = new byte[12]; gray.CopyPixels(output, 12, 0);
            Check(Enumerable.Range(0, 3).All(i => output[i * 4] == output[i * 4 + 1] && output[i * 4] == output[i * 4 + 2] && output[i * 4 + 3] == pixels[i * 4 + 3]), "Grayscale preserves alpha and converts RGB without dimming");
            var sourceCheck = new byte[12]; color.CopyPixels(sourceCheck, 12, 0);
            Check(sourceCheck.SequenceEqual(pixels), "Shared native source remains unchanged");
            var presentation = new Entry { Name = "fixture", SmallIcon = color, Thumb = color, Overlay = color, ReadOnlyReason = "Read-only fixture" };
            Check(!ReferenceEquals(presentation.DisplaySmallIcon, color) && !ReferenceEquals(presentation.DisplayThumb, color) && ReferenceEquals(presentation.Overlay, color) && presentation.AccessibleName.Contains("Read-only"), "Icon and thumbnail share accessible restriction while cloud overlay is preserved");
            presentation.ReadOnlyReason = "";
            Check(ReferenceEquals(presentation.DisplaySmallIcon, color) && ReferenceEquals(presentation.DisplayThumb, color), "Clearing restriction restores original color assets");
            var picture = new byte[64 * 64 * 4];
            for (int i = 0; i < picture.Length; i += 4) { picture[i] = 30; picture[i + 1] = (byte)(90 + i / 4 / 64); picture[i + 2] = 230; picture[i + 3] = 255; }
            var image = new PngBitmapEncoder(); image.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, picture, 64 * 4)));
            using (var stream = File.Create(Path.Combine(gallery, "read-only.png"))) image.Save(stream);
            File.SetAttributes(Path.Combine(gallery, "read-only.png"), FileAttributes.ReadOnly);
            string archive = Path.Combine(gallery, "read-only.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (string name in new[] { "visible.json", ".env", ".git/member.txt", "windows-hidden.txt", "readonly-member.txt" })
                {
                    var entry = zip.CreateEntry(name);
                    if (name == "windows-hidden.txt") entry.ExternalAttributes = (int)FileAttributes.Hidden;
                    if (name == "readonly-member.txt") entry.ExternalAttributes = (int)FileAttributes.ReadOnly;
                    using var writer = new StreamWriter(entry.Open()); writer.Write("fixture");
                }
            File.SetAttributes(archive, FileAttributes.ReadOnly);
            var originalAttributes = Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetAttributes);
            bool cancelledVisibility = false;
            try { FileVisibility.PathVisible(Path.Combine(folder, "ordinary.file.txt"), folder, new Dictionary<string, bool>(), new CancellationToken(true)); }
            catch (OperationCanceledException) { cancelledVisibility = true; }
            Check(cancelledVisibility, "Cancelled query cannot begin another visibility metadata read");
            Check(!FileVisibility.PathVisible(Path.Combine(folder, ".dot-dir", "pwfui_hit_descendant.txt"), folder, new Dictionary<string, bool>()) &&
                FileVisibility.PathVisible(Path.Combine(folder, ".dot-dir", "pwfui_hit_descendant.txt"), Path.Combine(folder, ".dot-dir"), new Dictionary<string, bool>()), "Indexed visibility prunes hidden ancestors but respects an explicitly selected hidden root");
            await S1Verification.Navigate(Left, folder); await S1Verification.Navigate(Right, other);
            Check(!Settings.ShowHidden && Left.Items.Any(e => e.Name == "ordinary.file.txt") && !Left.Items.Any(e => e.Name.StartsWith('.') || e.Name.StartsWith("windows-hidden")), "Default browse hides Windows-hidden and dot files/directories; normal extensions remain visible");
            await S1Verification.Navigate(Left, other); await S1Verification.Navigate(Left, folder);
            Left.ListForVerification.SelectedItems.Clear();
            foreach (var entry in Left.Items.Where(e => e.Name.StartsWith("ordinary."))) Left.ListForVerification.SelectedItems.Add(entry);
            var treeNode = TreeNode(folder, "Visibility fixture"); folderTree.Items.Add(treeNode); treeNode.IsExpanded = true;
            await folderTreeVisibilityWork;
            Check(!treeNode.Items.OfType<TreeViewItem>().Any(n => Path.GetFileName((string)n.Tag).StartsWith('.') || Path.GetFileName((string)n.Tag) == "windows-hidden-dir"), "Actual expanded sidebar obeys hidden policy");
            var filter = (TextBox)Left.FindName("FilterBox"); filter.Text = "ordinary";
            await Hidden(filter);
            Check(Left.Selected.Count == 2, "Visibility toggle preserves surviving multi-selection");
            await folderTreeVisibilityWork;
            Check(treeNode.IsExpanded && treeNode.Items.OfType<TreeViewItem>().Any(n => Path.GetFileName((string)n.Tag) == ".dot-dir"), "Global toggle refreshes an expanded sidebar branch");
            Check(Settings.ShowHidden && Right.Items.Any(e => e.Name == ".cached-dot") && filter.Text == "ordinary" && Left.Items.Any(e => e.Name == ".env"), "Ctrl+H from filter preserves text and updates both panes globally");
            bool before = Settings.ShowHidden;
            Check(HandleHiddenShortcut(Key.H, ModifierKeys.Control, true, filter) && Settings.ShowHidden == before, "Held Ctrl+H does not toggle repeatedly");
            Check(!HandleHiddenShortcut(Key.H, ModifierKeys.Control | ModifierKeys.Shift, false, filter) && !HandleHiddenShortcut(Key.H, ModifierKeys.Control, false, new TextBox()), "Additional modifiers and inline text editing are preserved");
            await Hidden((TextBox)Left.FindName("FilterBox"));
            await S1Verification.Navigate(Left, other);
            Check(!Left.Items.Any(e => e.Name.StartsWith('.')), "Cached revisit cannot restore hidden dot entries");
            await S1Verification.Navigate(Left, folder); filter.Text = "";
            await Changed(Left, () => ((Button)FindName("HiddenToggle")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Check(Settings.ShowHidden && Left.Items.Any(e => e.Name == ".dot-dir"), "Existing Hidden button shares the persisted setting");
            await Hidden(Left.ListForVerification);
            Left.Search("pwfui_hit_"); // Everything subtree, real selected index
            await Left.SearchCompletion;
            int total = 217;
            while (Left.IndexedTotal != total)
            {
                if (Left.SearchNotice.Contains("failed", StringComparison.OrdinalIgnoreCase) || Left.SearchNotice.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) throw new IOException(Left.SearchNotice);
                Left.Search("pwfui_hit_"); await Left.SearchCompletion;
            }
            Check(!Left.Items.Any(), "First entirely hidden indexed page shows no rows");
            var more = (Button)Left.FindName("MoreResults");
            Check(more.Visibility == Visibility.Visible, "All-hidden indexed page retains its next-page action");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Left.SearchCompletion;
            Check(Left.Items.Count() == 5 && Left.Items.All(e => e.Name.StartsWith("pwfui_hit_visible")), "Real index paging advances past hidden page and prunes hidden ancestors");
            Settings.TaskbarSearch = true; taskbarSearch.ShowSearch(); Settings.TaskbarSearch = false;
            var widget = taskbarSearch.Widget;
            await S1Verification.Navigate(widget.Pane, folder);
            widget.Pane.VerificationSearchScope=0;
            widget.Pane.Search("pwfui_hit_"); await widget.Pane.SearchCompletion;
            Check(!widget.Pane.Items.Any(), "Search widget shares hidden filtering before toggle");
            await Hidden(filter);
            await widget.Pane.SearchCompletion;
            Check(widget.Pane.Items.Count() == 200, "Global toggle refreshes the actual widget query without a second setting");
            widget.Dismiss();
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Left.SearchCompletion;
            Check(Left.Items.Count() == total, "Showing hidden restores every indexed match across pages");
            Left.Search("pwfui_hit_"); var superseded = Left.SearchCompletion;
            Left.Search("ordinary."); await Task.WhenAll(superseded, Left.SearchCompletion);
            Check(Left.Items.Count() == 2 && Left.Items.All(e => e.Name.StartsWith("ordinary.")), "Superseded real indexed query cannot replace the latest visibility result");
            await S1Verification.Navigate(Left, folder);
            await Hidden(Left.ListForVerification);
            Left.VerificationSearchScope=2;
            Left.Search("pwfui_hit_"); await Left.SearchCompletion;
            Check(Left.Items.Count() == 5, "Recursive filesystem search prunes dot and Windows-hidden directory descendants");
            string duplicates = Path.Combine(root, "duplicate-fixture");
            foreach (string name in new[] { "normal", ".hidden" })
            { string dir = Directory.CreateDirectory(Path.Combine(duplicates, name)).FullName; File.WriteAllText(Path.Combine(dir, "same.txt"), "duplicate"); }
            Check(DuplicatesWindow.Find(new[] { duplicates }, true, false, null, CancellationToken.None, DuplicatesWindow.Match.Name).Count == 0 &&
                DuplicatesWindow.Find(new[] { duplicates }, true, true, null, CancellationToken.None, DuplicatesWindow.Match.Name).Single().Files.Count == 2, "Duplicate finder obeys the same recursive dot visibility rule");
            Check(!FileVisibility.Visible(".env", 0, false) && FileVisibility.Visible("ordinary.file.txt", 0, false) && FileVisibility.Visible("..", 0, false), "Dot-name contract preserves normal extensions and parent navigation");
            await S1Verification.Navigate(Left, Path.Combine(folder, ".dot-dir"));
            Check(Left.Items.Any(e => e.Name == "pwfui_hit_descendant.txt"), "Known hidden folders remain directly navigable");
            await S1Verification.Navigate(Left, archive);
            Check(Left.Items.Select(e => e.Name).Order().SequenceEqual(new[] { "readonly-member.txt", "visible.json" }) && Left.Items.All(e => e.IsReadOnly && e.ReadOnlyReason.Contains("Archive contents")), "Read-only archive entries have capability reason and global hidden filtering");
            await Hidden(Left.ListForVerification);
            Check(Left.Items.Count() == 5 && Left.Items.All(e => e.IsReadOnly), "Ctrl+H includes dot and Windows-hidden archive members");
            File.SetAttributes(archive, FileAttributes.Normal); Left.Refresh();
            await S1Verification.Navigate(Left, archive);
            Check(Left.Items.Single(e => e.Name == "readonly-member.txt").IsReadOnly && !Left.Items.Single(e => e.Name == "visible.json").IsReadOnly, "Writable archive still shows individual read-only member attributes");
            File.SetAttributes(archive, FileAttributes.ReadOnly);
            await S1Verification.Navigate(Left, gallery);
            await Left.IconsCompletionForVerification; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(Left.Items.Single(e => e.Name == "unsupported.rar").ReadOnlyReason.Contains("recovery"), "Unsupported physical archive icon exposes concrete contents restriction");
            Check(!Left.Items.Single(e => e.Name == "Assets").IsReadOnly, "Directory customization ReadOnly bit does not imply access denial");
            var locked = Left.Items.Single(e => e.Name == "read-only.txt");
            var normal = Left.Items.Single(e => e.Name == "editable.txt");
            Check(locked.IsReadOnly && !normal.IsReadOnly && !ReferenceEquals(locked.DisplaySmallIcon, locked.SmallIcon) && ReferenceEquals(normal.DisplaySmallIcon, normal.SmallIcon), "Real native icon is desaturated per entry without changing shared writable icons");
            await Changed(Left, () => File.SetAttributes(locked.Path, FileAttributes.Normal), () => !Left.Items.Single(e => e.Name == "read-only.txt").IsReadOnly);
            await Changed(Left, () => File.SetAttributes(locked.Path, FileAttributes.ReadOnly), () => Left.Items.Single(e => e.Name == "read-only.txt").IsReadOnly);
            Check(true, "Real attribute watcher refreshes the read-only cue in both directions");
            Check(originalAttributes.All(pair => File.GetAttributes(pair.Key) == pair.Value), "Visibility and presentation leave all original fixture attributes unchanged");
            SetActive(Left); Width = 1440; Height = 960;
            foreach (var mode in new[] { PaneView.ViewMode.Details, PaneView.ViewMode.List, PaneView.ViewMode.IconsM, PaneView.ViewMode.IconsL, PaneView.ViewMode.IconsXL })
            {
                Left.SetView(mode); await Left.IconsCompletionForVerification;
                if (mode is not (PaneView.ViewMode.Details or PaneView.ViewMode.List)) await HasThumb(Left.Items.Single(e => e.Name == "read-only.png"));
                await S1Verification.Snapshot(this, evidence, "fileui-" + mode + "-100");
                var badges = Children<ContentControl>(Left).Where(c => c.Tag?.ToString() == "ReadOnlyLock" && c.Visibility == Visibility.Visible).ToArray();
                Check(badges.Length >= 4 && badges.All(c => c.ActualWidth == (mode is PaneView.ViewMode.Details or PaneView.ViewMode.List ? 8 : 24)), "Rendered lock badge contract in " + mode);
            }
            Settings.TextScale = 1.5; ApplyAppearance(); Width = 1920; Height = 1240;
            foreach (var mode in new[] { PaneView.ViewMode.Details, PaneView.ViewMode.IconsL })
            { Left.SetView(mode); await Left.IconsCompletionForVerification; await S1Verification.Snapshot(this, evidence, "fileui-" + mode + "-150"); }
            Settings.TextScale = 1; ApplyAppearance(); Settings.Save();
            string receipt = Path.Combine(evidence, "settings-readback.txt");
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--verify-fileui-settings=" + receipt);
            using var reader = Process.Start(start); await reader.WaitForExitAsync();
            Check(reader.ExitCode == 0 && File.ReadAllText(receipt) == "shown", "Independent process reads persisted global visibility");
            Check(HiddenTrialPresentation.ForegroundViolations == 0, "No foreground activation during hidden verification");
            result = 0;
        }
        catch (Exception ex) { checks.Add(new { failure = ex.ToString() }); }
        finally
        {
            Left.StopWidgetWork(); Right.StopWidgetWork();
            taskbarSearch?.Widget?.Pane.StopWidgetWork();
            await Task.WhenAll(Left.IconTasksForVerification.Concat(Right.IconTasksForVerification));
            if (taskbarSearch?.Widget is { } openWidget) await Task.WhenAll(openWidget.Pane.IconTasksForVerification);
            foreach (string path in Directory.EnumerateFileSystemEntries(Path.Combine(root, "fixtures"), "*", SearchOption.AllDirectories))
            { var attributes = File.GetAttributes(path); if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly); }
            File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { result, pid = Environment.ProcessId, HiddenTrialPresentation.ForegroundViolations, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(result);
        }
    }
}
#endif
