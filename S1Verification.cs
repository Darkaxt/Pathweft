#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileExplorer;

// A finite local scenario, compiled out of Release. It invokes real controls
// and events; no listener, remote command executor or production test endpoint.
internal static class S1Verification
{
    static readonly List<object> evidence = new();
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static void Check(bool condition, string criterion)
    {
        evidence.Add(new { criterion, passed = condition, elapsedMs = clock.Elapsed.TotalMilliseconds });
        if (!condition) throw new InvalidOperationException(criterion);
    }
    internal static async Task Run(MainWindow window, string root)
    {
        string output = Path.Combine(root, "evidence");
        int result = 1;
        try
        {
            root = Path.GetFullPath(root);
            if (!root.StartsWith(@"D:\Temp\pathweft-s1-", StringComparison.OrdinalIgnoreCase) || Settings.Profile == null)
                throw new ArgumentException("S1 verification requires its task-owned fixture and an isolated profile.");
            Directory.CreateDirectory(output);
            var left = (PaneView)window.FindName("Left");
            left.VerificationQueryEvents = new();
            var right = (PaneView)window.FindName("Right");
            string scope = Path.Combine(root, "scope spaced é");
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination", Environment.ProcessId.ToString())).FullName;
            string removedFixture = Path.Combine(scope, "pwfts1_removed.txt");
            if (!File.Exists(removedFixture)) File.WriteAllText(removedFixture, "delete-after-query fixture");
            string oleFixture = Path.Combine(scope, "pwfts1_ole_drag.txt");
            if (!File.Exists(oleFixture)) File.WriteAllText(oleFixture, "native OLE drag fixture");
            await Navigate(left, scope);
            await Navigate(right, destination);
            Check(left.Dir == scope && left.Items.Any(e => e.Name == "pwfts1_clipboard.txt"), "CORE-02 real folder navigation and selection");
            int tabsBefore = left.OpenTabs.Count;
            ((Button)left.FindName("NewTabButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(left.OpenTabs.Count == tabsBefore + 1 && left.ActiveTab == tabsBefore, "CORE-02 new-tab button");
            left.CloseTab();
            Check(left.OpenTabs.Count == tabsBefore, "CORE-02 tab close preserves remaining tab");
            await window.VerifyClientHandoff(left, root);
            left.CloseTab();
            await Navigate(left, scope);
            string pipe = Program.PipeName;
            Check(pipe.StartsWith("Pathweft-") && !Settings.Dir.Contains("ProtagonistLabs") && typeof(App).Assembly.GetName().Name == "Pathweft", "CORE-01 product/profile/IPC identity");
            if (Environment.GetEnvironmentVariable("PATHWEFT_S1_VIRTUAL_ONLY") == "1")
            {
                await VerifyVirtualOrigins(window, left, root);
                result = 0;
                return;
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("PATHWEFT_S1_BASELINE_PID"), out int baselinePid))
            {
                using var baseline = Process.GetProcessById(baselinePid);
                Check(!baseline.HasExited && baseline.MainModule.FileName.StartsWith(@"D:\Temp\pathweft-s1-") && baseline.ProcessName == "FileLabs", "CORE-01 original File Labs and Pathweft run side by side");
            }

            var box = (TextBox)left.FindName("FilterBox");
            box.Text = "pwfts1_évidence";
            Check(!left.InSearch && left.Items.Count() > 1, "SEARCH-01 typing filters without starting indexed search");
            await left.ArmWatchBeforeVerificationSearch();
            await window.VerifyKeyboardSearch(left, "pwfts1_hit_*.txt");
            evidence.Add(new { diagnostic = "first indexed page", count = left.Items.Count(), total = left.IndexedTotal, notice = left.SearchNotice });
            Check(left.IndexedTotal == 620 && left.Items.Count() == 200, "SEARCH-01/02 real scoped index and first page");
            Check(left.Items.All(e => e.Path.StartsWith(scope + "\\", StringComparison.OrdinalIgnoreCase)), "SEARCH-01 subtree excludes similarly prefixed sibling");
            left.ListForVerification.SelectedItem = left.Items.First();
            await window.VerifyRevealAction(left);
            Check(!left.InSearch && left.Selected.Single().Name == "pwfts1_hit_0000.txt", "SEARCH-01 real indexed result menu reveals parent and selects result");
            await Query(left, "pwfts1_hit_*.txt", 0);
            ((Button)left.FindName("MoreResults")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await left.SearchCompletion;
            Check(left.Items.Count() == 400 && left.Items.Select(e => e.Path).Distinct().Count() == 400, "SEARCH-02 second page appends without duplicates");
            ((Button)left.FindName("MoreResults")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await left.SearchCompletion;
            ((Button)left.FindName("MoreResults")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await left.SearchCompletion;
            Check(left.Items.Count() == 620 && ((Button)left.FindName("MoreResults")).Visibility == Visibility.Collapsed, "SEARCH-02 last page and total");
            left.SetSort("Size", true);
            await left.SearchCompletion;
            Check(left.Items.First().Size == 620 && left.Items.Last().Size == 421, "SEARCH-02 index sorts entire result set before paging");
            await Query(left, "pwfts1_hit_*.txt", 1);
            Check(left.IndexedTotal == 621 && left.Items.Count() == 200, "SEARCH-01 explicit all-index scope");
            await Query(left, "regex:^pwfts1_hit_00[0-9][0-9]\\.txt$", 0);
            Check(left.IndexedTotal == 100, "SEARCH-01 native regex syntax");
            await Query(left, "pwfts1_évidence space.txt", 0);
            Check(left.IndexedTotal == 1 && left.Items.Single().Name == "pwfts1_évidence space.txt", "SEARCH-01 Unicode and spaces");
            await Query(left, "pwfts1_hit_0001 | pwfts1_hit_outside", 0);
            Check(left.IndexedTotal == 1 && left.Items.Single().Name == "pwfts1_hit_0001.txt", "SEARCH-01 grouped OR cannot escape subtree");

            StartQuery(left, "pwfts1_hit_*.txt", 0);
            var stale = left.SearchCompletion;
            StartQuery(left, "pwfts1_no_such_result_8902", 0);
            await Task.WhenAll(stale, left.SearchCompletion);
            Check(left.IndexedTotal == 0 && !left.Items.Any(), "SEARCH-02 superseded query cannot replace empty newer result");
            StartQuery(left, "pwfts1_hit_0001", 0);
            StartQuery(right, "pwfts1_évidence", 1);
            await Task.WhenAll(left.SearchCompletion, right.SearchCompletion);
            Check(left.Items.Single().Name == "pwfts1_hit_0001.txt" && right.Items.Single().Name == "pwfts1_évidence space.txt", "SEARCH-03 shared engine with independent pane state");
            string removed = Path.Combine(scope, "pwfts1_removed.txt");
            await Query(left, "pwfts1_removed.txt", 0);
            Check(left.Items.Count() == 1, "SEARCH-02 result exists before external deletion");
            File.Delete(removed); // the feature's changing-path scenario, not cleanup
            left.ListForVerification.SelectedItem = left.Items.Single();
            await window.VerifyMissingOpen(left);
            Check(!File.Exists(removed) && left.SearchNotice.Contains("unavailable or was removed"), "SEARCH-02 opening a removed indexed path reports its unavailable state");
            await Query(left, "pwfts1_hit_0001", 2);
            Check(left.Items.Single().Name == "pwfts1_hit_0001.txt" && left.SearchNotice.Contains("Filesystem"), "SEARCH-04 explicit filesystem subtree mode");

            left.SearchInstance = "Pathweft-S1-absent-instance";
            await Query(left, "pwfts1_hit_0001", 0);
            Check(!left.Items.Any() && left.SearchNotice.Contains("unavailable"), "SEARCH-04 unavailable selected engine does not crawl silently");
            left.SearchInstance = "";
            await Query(left, "pwfts1_hit_0001", 0);
            Check(left.Items.Count() == 1, "SEARCH-03 explicit reconnect to unchanged existing instance");
            StartQuery(left, "pwfts1_hit_*.txt", 0);
            var departing = left.SearchCompletion;
            await Navigate(left, Path.Combine(root, "large"));
            await departing;
            Check(!left.InSearch && left.Items.Count() == 2400, "CORE-02/SEARCH-02 navigation cancels search and shows real large folder");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(left.ListForVerification.ItemContainerGenerator.ContainerFromIndex(2000) == null, "SEARCH-02 large listing remains virtualized");
            window.SetSinglePane(true);
            Check(right.Visibility != Visibility.Visible, "CORE-02 single pane");
            window.SetSinglePane(false);
            Check(right.Visibility == Visibility.Visible, "CORE-02 restore two panes");
            string unc = @"\\localhost\D$\" + root[3..] + @"\scope spaced é";
            await Navigate(left, unc);
            await Query(left, "pwfts1_hit_*.txt", 0);
            Check(left.IndexedTotal == 0 && left.SearchNotice.Contains("Index coverage only"), "SEARCH-04 unindexed UNC scope reports index coverage");
            await Query(left, "pwfts1_hit_*.txt", 2);
            Check(left.Items.Count() == 620 && left.SearchNotice.Contains("Filesystem"), "SEARCH-04 explicit live network subtree returns real fixture results");
            string unavailable = @"\\localhost\Pathweft-S1-unavailable-share\folder";
            bool unavailableReported = false;
            try { await Navigate(left, unavailable); }
            catch (IOException) { unavailableReported = true; }
            await Query(left, "pwfts1_hit_*.txt", 2);
            Check(unavailableReported && !left.Items.Any() && left.SearchNotice.Contains("Partial results"), "SEARCH-04 unavailable network location is an error with partial coverage, not successful empty search");
            if (Environment.GetEnvironmentVariable("PATHWEFT_S1_COVERAGE_FILE") is { } coverageFile && File.Exists(coverageFile))
            {
                using var coverage = JsonDocument.Parse(File.ReadAllText(coverageFile));
                if (coverage.RootElement.GetProperty("nonNtfs") is { ValueKind: JsonValueKind.Object } nonNtfs)
                {
                    string folder = nonNtfs.GetProperty("path").GetString();
                    await Navigate(left, folder);
                    await Query(left, "*", 0);
                    Check(left.IndexedTotal == 0, "SEARCH-04 exFAT volume is absent from the selected index");
                    await Query(left, "*", 2);
                    Check(left.Items.Count() == nonNtfs.GetProperty("count").GetInt32() && left.Items.Any(e => e.Name == nonNtfs.GetProperty("sample").GetString()), "SEARCH-04 explicit exFAT live search verified read-only against a bounded leaf folder");
                }
                else throw new InvalidOperationException("SEARCH-04 requires an available bounded non-NTFS coverage case.");
            }
            else throw new InvalidOperationException("SEARCH-04 coverage configuration is missing; non-NTFS acceptance cannot be skipped.");
            await Navigate(left, scope);
            await Navigate(right, destination);
            bool clipboardVerified = await window.VerifyBrowsingActions(left, right, output);
            evidence.Add(new { criterion = "CORE-02 OS clipboard and physical OLE drag", passed = clipboardVerified, blocker = clipboardVerified ? null : "Input desktop unavailable / Windows lock screen; clipboard and physical drag remain unverified. Routed drop and cross-pane copy are separate passing observations." });

            await Query(left, "pwfts1_hit_*.txt", 0);
            await VerifyScopeSelector(left, output);
            await Query(left, "pwfts1_hit_*.txt", 0);
            window.Width = 1120; window.Height = 760;
            await Snapshot(window, output, "two-panes-1120x760");
            Settings.TextScale = 1.25;
            window.ApplyAppearance();
            window.Width = 1440; window.Height = 960;
            await Snapshot(window, output, "two-panes-1440x960-scale125");
            Check(Control(left, "FilterBox").ActualWidth >= 50 && Control(left, "SearchScope").ActualWidth >= 140, "CORE-03 search controls fit at representative scales");
            var settings = new SettingsWindow(window);
            settings.Show();
            await Snapshot(settings, output, "settings");
            settings.Close();
            Check(true, "CORE-03 single integration settings interface rendered");
            result = clipboardVerified ? 0 : 1;
        }
        catch (Exception ex) { evidence.Add(new { failure = ex.ToString(), elapsedMs = clock.Elapsed.TotalMilliseconds }); }
        finally
        {
            Directory.CreateDirectory(output);
            if (((PaneView)window.FindName("Left")).VerificationQueryEvents is { } transitions)
                File.WriteAllLines(Path.Combine(output, "query-transitions.txt"), transitions);
            File.WriteAllText(Path.Combine(output, "s1-results.json"), JsonSerializer.Serialize(new
            {
                result, scenario = Environment.GetEnvironmentVariable("PATHWEFT_S1_VIRTUAL_ONLY") == "1" ? "virtual-origin-only" : "full-s1",
                pid = Environment.ProcessId, executable = Environment.ProcessPath,
                assembly = typeof(App).Assembly.GetName().Version.ToString(), evidence,
                measurementLimits = "WPF RenderTargetBitmap and measured visual tree; physical display presentation is not timed. Backend comparisons are separate evidence."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(result);
        }
    }

    static FrameworkElement Control(PaneView pane, string name) => (FrameworkElement)pane.FindName(name);
    static async Task VerifyVirtualOrigins(MainWindow window, PaneView pane, string root)
    {
        await Navigate(pane, PaneView.ThisPC);
        await Query(pane, "pwfts1_hit_0001.txt", 1);
        pane.ListForVerification.SelectedItem = pane.Items.Single();
        Check(pane.Selected.Count == 1 && !pane.Selected[0].IsDir, "SEARCH-01 all-index hits from This PC remain actionable");
        await window.VerifyRevealAction(pane);
        Check(!pane.InSearch && pane.Selected.Single().Name == "pwfts1_hit_0001.txt", "SEARCH-01 reveal from This PC search selects the real file");
        string archive = Path.Combine(root, "virtual-origin.zip");
        if (!File.Exists(archive))
        {
            using var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create);
            using var text = new StreamWriter(zip.CreateEntry("inside.txt").Open()); text.Write("archive browsing fixture");
        }
        await Navigate(pane, archive);
        Check(pane.ArchiveRoot != null && pane.Items.Any(e => e.Name == "inside.txt"), "CORE-02 real archive navigation");
        await Query(pane, "pwfts1_hit_0001.txt", 1);
        pane.ListForVerification.SelectedItem = pane.Items.Single();
        Check(pane.ArchiveRoot == null && pane.Selected.Count == 1, "SEARCH-01 all-index hits leave archive dispatch mode");
        pane.ClearFilter();
        await Navigate(pane, archive);
        Check(pane.ArchiveRoot != null && !pane.InSearch && pane.Items.Any(e => e.Name == "inside.txt"), "CORE-02 Esc restores archive browsing after indexed search");
    }
    static async Task VerifyScopeSelector(PaneView pane, string output)
    {
        var combo = (ComboBox)pane.FindName("SearchScope");
        pane.ListForVerification.SelectedItem = null;
        combo.Focus();
        var enterKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        combo.RaiseEvent(enterKey);
        Check(!enterKey.Handled, "CORE-03 scope keyboard does not invoke the window's file-open command");
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(combo);
        var expand = (System.Windows.Automation.Provider.IExpandCollapseProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse);
        expand.Expand();
        await combo.Dispatcher.InvokeAsync(() => { combo.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
        var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
        var child = (FrameworkElement)popup.Child;
        Check(popup.IsOpen && child.IsVisible && child.ActualWidth >= combo.ActualWidth, "CORE-03 scoped search dropdown opens through UI Automation");
        var dpi = VisualTreeHelper.GetDpi(child);
        var image = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(child.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(child);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(output, "scope-options.png"))) encoder.Save(stream);
        // SelectionItem belongs to the selector's data-item peer, rather than
        // the presentation container's element peer.
        var itemPeer = peer.GetChildren().Single(item => item.GetName() == "Filesystem · Folder");
        ((System.Windows.Automation.Provider.ISelectionItemProvider)itemPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
        expand.Collapse();
        await pane.SearchCompletion;
        Check(combo.SelectedIndex == 2 && pane.Items.Count() == 620, "CORE-03 scope choice dispatches the actual filesystem search");
    }
    static void StartQuery(PaneView pane, string text, int scope)
    {
        ((ComboBox)pane.FindName("SearchScope")).SelectedIndex = scope;
        ((TextBox)pane.FindName("FilterBox")).Text = text;
        ((Button)pane.FindName("SearchSubmit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    static async Task Query(PaneView pane, string text, int scope) { StartQuery(pane, text, scope); await pane.SearchCompletion; }
    static async Task Navigate(PaneView pane, string path)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool requested = false;
        void Done() { if (requested && pane.Listed && pane.Dir.Equals(path, StringComparison.OrdinalIgnoreCase) && !pane.InSearch) complete.TrySetResult(); }
        void Failed(string error) => complete.TrySetException(new IOException(error));
        pane.StatsChanged += Done; pane.Error += Failed;
        try { pane.Listed = false; requested = true; pane.Navigate(path); await complete.Task; }
        finally { pane.StatsChanged -= Done; pane.Error -= Failed; }
    }

    static async Task Snapshot(Window window, string folder, string label)
    {
        await window.Dispatcher.InvokeAsync(() => { window.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(folder, label + ".png"))) png.Save(stream);
        var nodes = new List<object>();
        void Walk(DependencyObject node, int parent)
        {
            int id = nodes.Count;
            if (node is FrameworkElement element)
            {
                Point origin;
                try { origin = element.TranslatePoint(new Point(), window); } catch (InvalidOperationException) { origin = new(); }
                nodes.Add(new { id, parent, role = element.GetType().Name, name = element.Name,
                    text = element is TextBlock text ? text.Text : element is TextBox input ? input.Text : null,
                    x = origin.X, y = origin.Y, width = element.ActualWidth, height = element.ActualHeight,
                    visible = element.IsVisible, enabled = element.IsEnabled, focused = element.IsKeyboardFocusWithin });
            }
            else { id = parent; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i), id);
        }
        Walk(window, -1);
        File.WriteAllText(Path.Combine(folder, label + ".json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, label, units = "WPF DIP", dpi = dpi.PixelsPerInchX, textScale = Settings.TextScale, elapsedMs = clock.Elapsed.TotalMilliseconds, nodes }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public partial class PaneView
{
    internal FileList ListForVerification => List;
    internal Action<Exception> VerificationDragFailure;
    internal List<string> VerificationQueryEvents;
    internal async Task ArmWatchBeforeVerificationSearch()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = watcher ?? throw new InvalidOperationException("The real folder watcher is not active.");
        FileSystemEventHandler changed = (_, e) => { if (e.Name.StartsWith("pwfts1_watch_")) observed.TrySetResult(); };
        owner.Created += changed;
        try
        {
            File.WriteAllText(Path.Combine(Dir, "pwfts1_watch_" + Environment.ProcessId + ".tmp"), "watch handoff fixture");
            await observed.Task;
            bool armed = await Dispatcher.InvokeAsync(() => debounce.IsEnabled, DispatcherPriority.Normal);
            if (!armed) throw new InvalidOperationException("The folder notification did not arm its refresh.");
        }
        finally { owner.Created -= changed; }
    }
}

public partial class MainWindow
{
    internal async Task VerifyClientHandoff(PaneView pane, string root)
    {
        string target = Directory.CreateDirectory(Path.Combine(root, "external-open")).FullName;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Listed() { if (pane.Listed && pane.Dir == target) observed.TrySetResult(); }
        pane.StatsChanged += Listed;
        try
        {
            SetActive(pane); pane.Listed = false;
            using var client = Process.Start(new ProcessStartInfo(Environment.ProcessPath) { ArgumentList = { target }, UseShellExecute = false, CreateNoWindow = true });
            await client.WaitForExitAsync();
            if (client.ExitCode != 0) throw new IOException("Second launch handoff failed.");
            await observed.Task;
            if (Process.GetProcessesByName("Pathweft").Count(p => p.MainModule.FileName == Environment.ProcessPath) != 1)
                throw new InvalidOperationException("A second Pathweft window process was created.");
        }
        finally { pane.StatsChanged -= Listed; }
    }

    internal async Task VerifyKeyboardSearch(PaneView pane, string query)
    {
        var box = (TextBox)pane.FindName("FilterBox");
        box.Text = query;
        SetActive(pane); box.Focus();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(key);
        if (!key.Handled || !pane.InSearch) throw new InvalidOperationException("Enter did not submit the pane search.");
        await pane.SearchCompletion;
    }

    internal async Task VerifyMissingOpen(PaneView pane)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed() { if (pane.SearchNotice.Contains("unavailable or was removed")) complete.TrySetResult(); }
        pane.StatsChanged += Changed;
        try
        {
            SetActive(pane);
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.F3) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            pane.ListForVerification.RaiseEvent(key);
            await complete.Task;
        }
        finally { pane.StatsChanged -= Changed; }
    }

    internal async Task VerifyRevealAction(PaneView pane)
    {
        var selected = pane.Selected.Single();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Listed() { if (!pane.InSearch && pane.Selected.Any(e => e.Path == selected.Path)) complete.TrySetResult(); }
        pane.StatsChanged += Listed;
        try
        {
            var menu = ItemMenu(pane, pane.Selected, null);
            menu.PlacementTarget = pane.ListForVerification;
            menu.IsOpen = true;
            menu.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == "Open file location").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await complete.Task;
            menu.IsOpen = false;
        }
        finally { pane.StatsChanged -= Listed; }
    }

    internal async Task<bool> VerifyBrowsingActions(PaneView left, PaneView right, string evidenceFolder)
    {
        // Invoke the real Copy button event, then observe the existing queue's
        // completion event. No second transfer path is added for diagnostics.
        left.ListForVerification.SelectedItem = left.Items.Single(e => e.Name == "pwfts1_clipboard.txt");
        SetActive(left);
        var completed = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(Job job) => completed.TrySetResult(job);
        Ferry.Finished += Finished;
        try
        {
            CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var job = await completed.Task;
            if (job.Phase != Phase.Done || !File.Exists(Path.Combine(right.Dir, "pwfts1_clipboard.txt"))) throw new IOException("Cross-pane copy failed.");
        }
        finally { Ferry.Finished -= Finished; }
        // Exercise the actual destination's routed drop handler with CF_HDROP.
        // This checks pane dispatch and queue completion; the OLE source drag
        // loop remains a distinct observation boundary.
        var dropped = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        void DropFinished(Job job) => dropped.TrySetResult(job);
        Ferry.Finished += DropFinished;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(left.Dir, "pwfts1_drag.txt") });
            var constructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
            var drop = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.ControlKey, DragDropEffects.Copy, right.ListForVerification, new Point(5, 5) });
            drop.RoutedEvent = DragDrop.DropEvent;
            right.ListForVerification.RaiseEvent(drop);
            var job = await dropped.Task;
            if (job.Phase != Phase.Done || !File.Exists(Path.Combine(right.Dir, "pwfts1_drag.txt"))) throw new IOException("Cross-pane routed drop failed.");
        }
        finally { Ferry.Finished -= DropFinished; }
        File.WriteAllText(Path.Combine(evidenceFolder, "browsing-actions.txt"), "Real cross-pane Copy button and destination routed Drop handler completed through Ferry; destinations verified. OLE source drag loop is a separate boundary.\n");
        bool physicalDragVerified = await VerifyOleDrag(left, right, evidenceFolder);

        // Clipboard format is a real OS boundary; preserve the prior object.
        // A blocked external boundary is recorded without preventing independent
        // UI verification, and without changing application behavior.
        IDataObject prior;
        try { prior = Clipboard.GetDataObject(); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x800401D0)
        {
            File.WriteAllText(Path.Combine(evidenceFolder, "clipboard-blocker.txt"), ex.ToString());
            return false;
        }
        try
        {
            ClipboardPut(false);
            if (!Clipboard.GetFileDropList().Contains(Path.Combine(left.Dir, "pwfts1_clipboard.txt"))) throw new IOException("Clipboard file format failed.");
        }
        finally { if (prior != null) Clipboard.SetDataObject(prior, true); else Clipboard.Clear(); }
        File.AppendAllText(Path.Combine(evidenceFolder, "browsing-actions.txt"), "OS clipboard CF_HDROP round trip verified and prior clipboard restored.\n");
        return physicalDragVerified;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NativePoint { internal int X, Y; }

    async Task<bool> VerifyOleDrag(PaneView left, PaneView right, string evidenceFolder)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        using (var process = Process.GetProcessById((int)foreground))
            if (process.ProcessName == "LockApp")
            {
                File.AppendAllText(Path.Combine(evidenceFolder, "native-drag-events.txt"), "BLOCKED: Windows LockApp owns the input desktop. No mouse input dispatched.\n");
                return false;
            }
        transfersWindow?.Hide();
        Activate();
        string source = Path.Combine(left.Dir, "pwfts1_ole_drag.txt");
        var entry = left.Items.Single(e => e.Path == source);
        left.ListForVerification.SelectedItem = entry;
        left.ListForVerification.ScrollIntoView(entry);
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle);
        var row = (FrameworkElement)left.ListForVerification.ItemContainerGenerator.ContainerFromItem(entry);
        Point start = row.PointToScreen(new Point(50, row.ActualHeight / 2));
        Point finish = right.ListForVerification.PointToScreen(new Point(60, 150));
        var startWindow = WindowFromPoint(new NativePoint { X = (int)start.X, Y = (int)start.Y });
        if (GetAncestor(startWindow, 2) != new System.Windows.Interop.WindowInteropHelper(this).Handle)
        {
            File.AppendAllText(Path.Combine(evidenceFolder, "native-drag-events.txt"), "BLOCKED: the measured source row is not on the available input surface. No mouse input dispatched.\n");
            return false;
        }
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dragging = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        MouseButtonEventHandler down = (_, _) => pressed.TrySetResult();
        GiveFeedbackEventHandler feedback = (_, _) => dragging.TrySetResult();
        DragEventHandler enter = (_, _) => entered.TrySetResult();
        void JobFinished(Job job) => finished.TrySetResult(job);
        left.VerificationDragFailure = ex => { dragging.TrySetException(ex); entered.TrySetException(ex); };
        left.ListForVerification.AddHandler(PreviewMouseLeftButtonDownEvent, down, true);
        left.ListForVerification.GiveFeedback += feedback;
        right.ListForVerification.AddHandler(DragEnterEvent, enter, true);
        Ferry.Finished += JobFinished;
        GetCursorPos(out var previous);
        void Trace(string phase) => File.AppendAllText(Path.Combine(evidenceFolder, "native-drag-events.txt"), phase + "\n");
        static void Mouse(uint flags)
        {
            var input = new byte[40]; // x64 INPUT, MOUSEINPUT union at offset 8
            BitConverter.GetBytes(flags).CopyTo(input, 20);
            if (SendInput(1, input, input.Length) != 1) throw new InvalidOperationException("Native test mouse input was not accepted.");
        }
        try
        {
            Activate(); SetActive(left);
            Trace("prepared actual row and destination screen coordinates");
            await Task.Run(async () =>
            {
                SetCursorPos((int)start.X, (int)start.Y);
                Mouse(2);
                Trace("mouse down dispatched");
                await pressed.Task.ConfigureAwait(false);
                Trace("source press observed");
                SetCursorPos((int)start.X + 24, (int)start.Y);
                await dragging.Task.ConfigureAwait(false);
                Trace("OLE GiveFeedback observed");
                SetCursorPos((int)finish.X, (int)finish.Y);
                await entered.Task.ConfigureAwait(false);
                Trace("destination DragEnter observed");
                Mouse(4);
            });
            var job = await finished.Task;
            Trace("transfer finished");
            if (job.Phase != Phase.Done || !File.Exists(Path.Combine(right.Dir, entry.Name))) throw new IOException("Native OLE drag did not deliver its fixture.");
            File.AppendAllText(Path.Combine(evidenceFolder, "browsing-actions.txt"), "Physical mouse input -> real source Drag_Move -> OLE GiveFeedback -> destination DragEnter/Drop -> Ferry completion verified.\n");
            return true;
        }
        finally
        {
            Mouse(4); SetCursorPos(previous.X, previous.Y);
            left.ListForVerification.RemoveHandler(PreviewMouseLeftButtonDownEvent, down);
            left.ListForVerification.GiveFeedback -= feedback;
            right.ListForVerification.RemoveHandler(DragEnterEvent, enter);
            left.VerificationDragFailure = null;
            Ferry.Finished -= JobFinished;
        }
    }
}
#endif
