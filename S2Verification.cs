#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace FileExplorer;

public partial class MainWindow
{
    // Finite local acceptance workflow; absent from Release and not a command server.
    internal async Task VerifyS2(string root)
    {
        string evidencePath = Path.Combine(root, "evidence", Environment.ProcessId.ToString());
        var observations = new List<object>();
        int result = 1;
        void Check(bool passed, string criterion)
        {
            HiddenTrialPresentation.Phase = criterion;
            observations.Add(new { criterion, passed });
            if (!passed) throw new InvalidOperationException(criterion);
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Query(PaneView pane, string text, int scope)
        {
            ((ComboBox)pane.FindName("SearchScope")).SelectedIndex = scope;
            var query = (TextBox)pane.FindName("FilterBox");
            query.Text = text;
            ((Button)pane.FindName("SearchSubmit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pane.SearchCompletion;
        }
        async Task<Job> Transfer(Button button, Func<Job> selectedJob)
        {
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(Job job) => done.TrySetResult(job);
            Ferry.Finished += Completed;
            try
            {
                Click(button);
                var submitted = selectedJob();
                if (submitted == null) throw new InvalidOperationException("Transfer was not submitted.");
                var job = await done.Task;
                Check(ReferenceEquals(submitted, job) && jobs.Contains(job) && job.Phase == Phase.Done, "WIDGET-02 one tracked Ferry job completes for the actual caller");
                return job;
            }
            finally { Ferry.Finished -= Completed; }
        }
        try
        {
            root = Path.GetFullPath(root);
            if (!root.StartsWith(@"D:\Temp\pathweft-s2-", StringComparison.OrdinalIgnoreCase) || Settings.Profile == null)
                throw new ArgumentException("S2 requires its registered fixtures and isolated profile.");
            Directory.CreateDirectory(evidencePath);
            File.WriteAllText(Path.Combine(root, "active-trial.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, evidencePath }));
            string scope = Path.Combine(root, "scope spaced é"), destination = Directory.CreateDirectory(Path.Combine(root, "destination", Environment.ProcessId.ToString())).FullName;
            if (Environment.GetEnvironmentVariable("PATHWEFT_S2_PRESENTATION_ONLY") == "1")
            {
                var presentationSettings = new SettingsWindow(this);
                presentationSettings.Show();
                var presentationWidget = new SearchWidgetWindow(this);
                presentationWidget.ShowSearch(scope);
                await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                foreach (Window owned in Application.Current.Windows)
                    Check(HiddenTrialPresentation.IsOutsideDisplays(owned), "Hidden presentation keeps " + owned.Title + " outside every display");
                Check(HiddenTrialPresentation.ForegroundObserverActive && HiddenTrialPresentation.ForegroundViolations == 0,
                    "Hidden presentation generates no foreground event for the owned process");
                presentationWidget.Shutdown(); presentationSettings.Close();
                result = 0;
                return;
            }
            await S1Verification.Navigate(Left, scope);
            await S1Verification.Navigate(Right, destination);
            if (Environment.GetEnvironmentVariable("PATHWEFT_S2_DPI_ONLY") == "1")
            {
                var dpiWidget = new SearchWidgetWindow(this);
                dpiWidget.ShowSearch(scope);
                await VerifyWidgetMonitorTransitions(dpiWidget, Check, observations);
                dpiWidget.Shutdown();
                Check(HiddenTrialPresentation.ForegroundViolations == 0, "Hidden monitor verification did not activate its process");
                result = 0;
                return;
            }
            SetActive(Left);
            if (Environment.GetEnvironmentVariable("PATHWEFT_S2_NATIVE_ONLY") == "1")
            {
                Settings.TaskbarSearch = true;
                taskbarSearch.ApplySettings();
                await taskbarSearch.LayoutCompletion;
                await VerifyNativeWidgetShortcut(observations, Check);
                var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Recreated() => restarted.TrySetResult();
                taskbarSearch.ExplorerRecreated += Recreated;
                try
                {
                    using var current = System.Diagnostics.Process.GetCurrentProcess();
                    File.WriteAllText(Path.Combine(root, "shell-ready.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, startTicks = current.StartTime.ToUniversalTime().Ticks, phase = "waiting-for-real-Explorer-restart", hidden = true }));
                    await restarted.Task;
                    await taskbarSearch.LayoutCompletion;
                    await VerifyNativeWidgetShortcut(observations, Check, "pwfts2_hit_0002.txt");
                    Check(taskbarSearch.HotkeyRegistered, "WIDGET-03 actual Explorer restart retains the global hotkey");
                    observations.Add(new { afterRealExplorerRestart = taskbarSearch.Status });
                }
                finally { taskbarSearch.ExplorerRecreated -= Recreated; }
                Check(HiddenTrialPresentation.ForegroundViolations == 0, "Native lifecycle test activated only its explicitly interactive search popup");
                result = 0;
                return;
            }
            var rightState = Right.SearchRequest;
            Check(!Settings.TaskbarSearch && !taskbarSearch.HotkeyRegistered && !taskbarSearch.BoxAttached, "WIDGET-03 fresh profile integration is off");
            var settings = new SettingsWindow(this);
            settings.Show();
            var enable = (CheckBox)settings.FindName("TaskbarSearchBox");
            enable.IsChecked = true;
            enable.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await taskbarSearch.LayoutCompletion;
            Check(Settings.TaskbarSearch && File.ReadAllText(Settings.FilePath).Contains("taskbarsearch=1"), "WIDGET-03 actual settings control persists opt-in");
            observations.Add(new { taskbarMode = taskbarSearch.Status, boxAttached = taskbarSearch.BoxAttached, hotkey = taskbarSearch.HotkeyRegistered, windows = Environment.OSVersion.Version.ToString() });
            Check(taskbarSearch.HotkeyRegistered, "WIDGET-03 real global hotkey registration");
            var integrationPanel = (FrameworkElement)((FrameworkElement)settings.FindName("TaskbarSearchBox")).Parent;
            integrationPanel.BringIntoView(new Rect(0, 0, integrationPanel.ActualWidth, Math.Min(320, integrationPanel.ActualHeight)));
            await S1Verification.Snapshot(settings, evidencePath, "settings-taskbar");
            settings.Close();
            taskbarSearch.ShowSearch();
            var widget = taskbarSearch.Widget;
            Check(widget.IsVisible && !ReferenceEquals(widget.Pane, Left) && !ReferenceEquals(widget.Pane, Right), "WIDGET-01/02 distinct live widget surface");
            await Query(widget.Pane, "pwfts2_hit_outside", 1);
            Check(widget.Pane.IndexedTotal == 1, "WIDGET-01 widget full-index scope finds outside sibling");
            await Query(widget.Pane, "pwfts2_hit_outside", 0);
            Check(widget.Pane.IndexedTotal == 0, "WIDGET-01 widget subtree excludes outside sibling");
            await Query(widget.Pane, "pwfts2_évidence space.txt", 0);
            Check(widget.Pane.IndexedTotal == 1, "WIDGET-01 widget Unicode/space query against real index");
            await Query(widget.Pane, "pwfts2_hit_*.txt", 0);
            Check(widget.Pane.IndexedTotal == 220 && widget.Pane.Items.Count() == 200, "WIDGET-01 actual Everything query in widget with paged results");
            widget.Pane.SetSort("Size", true);
            await widget.Pane.SearchCompletion;
            Check(widget.Pane.Items.First().Size == 220, "WIDGET-01 widget sorts the real indexed set");
            var request = widget.Pane.SearchRequest;
            int tabs = Left.OpenTabs.Count;
            Click(widget.HandoffButton);
            await Left.SearchCompletion;
            Check(Left.OpenTabs.Count == tabs + 1 && Left.SearchRequest == request && Left.IndexedTotal == 220, "WIDGET-01 query, explicit scope and sort copied into a full pane tab");
            Check(Right.SearchRequest == rightState, "WIDGET-02 handoff preserves other pane state");
            taskbarSearch.ShowSearch();
            await widget.Pane.SearchCompletion;
            await Query(Left, "pwfts2_no_match", 0);
            Check(widget.Pane.SearchRequest == request && widget.Pane.Items.Count() == 200 && Left.IndexedTotal == 0, "WIDGET-02 main-pane query does not alter widget query or results");
            widget.Pane.ListForVerification.SelectedItem = widget.Pane.Items.First();
            string selected = widget.Pane.Selected.Single().Path;
            var revealed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Listed() { if (new[] { Left, Right }.Any(p => !p.InSearch && p.Selected.Any(e => e.Path == selected))) revealed.TrySetResult(); }
            Left.StatsChanged += Listed; Right.StatsChanged += Listed;
            try { Click(widget.RevealButton); await revealed.Task; }
            finally { Left.StatsChanged -= Listed; Right.StatsChanged -= Listed; }
            Check(new[] { Left, Right }.Any(p => p.Dir == Path.GetDirectoryName(selected) && p.Selected.Any(e => e.Path == selected)), "WIDGET-01 actual Reveal opens parent and selects the result");
            taskbarSearch.ShowSearch();
            await widget.Pane.SearchCompletion;
            widget.Destination.Text = Directory.CreateDirectory(Path.Combine(destination, "widget-copy")).FullName;
            widget.Pane.ListForVerification.SelectedItem = widget.Pane.Items.First();
            var copied = widget.Pane.Selected.Single();
            await Transfer(widget.CopyButton, () => widget.LastTransfer);
            Check(File.ReadAllBytes(copied.Path).SequenceEqual(File.ReadAllBytes(Path.Combine(widget.Destination.Text, copied.Name))), "WIDGET-02 widget copy delivers matching file contents");
            widget.Pane.ListForVerification.SelectedItem = widget.Pane.Items.Skip(1).First();
            var moved = widget.Pane.Selected.Single();
            var movedBytes = File.ReadAllBytes(moved.Path);
            widget.Destination.Text = Directory.CreateDirectory(Path.Combine(destination, "widget-move")).FullName;
            await Transfer(widget.MoveButton, () => widget.LastTransfer);
            Check(!File.Exists(moved.Path) && movedBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(widget.Destination.Text, moved.Name))), "WIDGET-02 widget move delivers contents and removes source on success");
            await S1Verification.Navigate(Left, scope);
            await S1Verification.Navigate(Right, Directory.CreateDirectory(Path.Combine(destination, "pane-copy")).FullName);
            SetActive(Left);
            Left.ListForVerification.SelectedItem = Left.Items.Single(e => e.Name == "pwfts2_hit_0001.txt");
            int jobsBefore = jobs.Count;
            await Transfer(CopyButton, () => jobs.Count > jobsBefore ? jobs[0] : null);
            Check(File.Exists(Path.Combine(Right.Dir, "pwfts2_hit_0001.txt")), "WIDGET-02 main-pane copy uses the same queue after widget operations");
            taskbarSearch.ShowSearch();
            await widget.Pane.SearchCompletion;
            await S1Verification.Snapshot(widget, evidencePath, "widget-680x520");
            widget.Width = 900; widget.Height = 650;
            await S1Verification.Snapshot(widget, evidencePath, "widget-900x650");
            Settings.TextScale = 1.5;
            ApplyAppearance();
            widget.Width = 1020; widget.Height = 780;
            await S1Verification.Snapshot(widget, evidencePath, "widget-1020x780-text150");
            Check(((FrameworkElement)widget.Content).LayoutTransform is System.Windows.Media.ScaleTransform { ScaleX: 1.5 }, "CORE-03 widget honors the shared 150-percent text setting");
            Settings.TextScale = 1;
            ApplyAppearance();
            await Query(widget.Pane, "folder: pwfts2_folder", 0);
            widget.Pane.ListForVerification.SelectedItem = widget.Pane.Items.Single(e => e.IsDir);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void FolderListed() { if (!widget.Pane.InSearch && widget.Pane.Dir == Path.Combine(scope, "pwfts2_folder") && widget.Pane.Items.Any(e => e.Name == "pwfts2_inside.txt")) opened.TrySetResult(); }
            widget.Pane.StatsChanged += FolderListed;
            try { Click(widget.OpenButton); await opened.Task; }
            finally { widget.Pane.StatsChanged -= FolderListed; }
            Check(widget.Pane.Items.Any(e => e.Name == "pwfts2_inside.txt"), "WIDGET-02 actual widget Open button navigates the real folder");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(widget), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            widget.RaiseEvent(escape);
            Check(!widget.IsVisible, "WIDGET-03 Escape dismisses the actual widget");
            if (HiddenTrialPresentation.Enabled)
                await VerifyWidgetMonitorTransitions(widget, Check, observations);
            if (Environment.GetEnvironmentVariable("PATHWEFT_S2_EXPLORER_LIFECYCLE") == "1")
            {
                var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Recreated() => restarted.TrySetResult();
                taskbarSearch.ExplorerRecreated += Recreated;
                try
                {
                    File.WriteAllText(Path.Combine(root, "shell-ready.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, phase = "waiting-for-real-Explorer-restart", hidden = HiddenTrialPresentation.Enabled }));
                    await restarted.Task;
                    await taskbarSearch.LayoutCompletion;
                    await Query(Left, "pwfts2_hit_0000.txt", 0);
                    Check(Left.IndexedTotal == 1 && taskbarSearch.HotkeyRegistered, "WIDGET-03 actual Explorer restart preserves the real query and registered hotkey");
                    observations.Add(new { afterExplorerRestart = taskbarSearch.Status });
                }
                finally { taskbarSearch.ExplorerRecreated -= Recreated; }
            }
            settings = new SettingsWindow(this); settings.Show();
            enable = (CheckBox)settings.FindName("TaskbarSearchBox");
            enable.IsChecked = false;
            enable.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!taskbarSearch.HotkeyRegistered && !taskbarSearch.BoxAttached && taskbarSearch.Widget == null, "WIDGET-03 disabling releases host, widget and global hotkey");
            settings.Close();
            Check(new[] { Left, Right }.All(p => p != widget.Pane), "WIDGET-02 main panes survive widget disposal");
            if (HiddenTrialPresentation.Enabled)
                Check(HiddenTrialPresentation.ForegroundObserverActive && HiddenTrialPresentation.ForegroundViolations == 0, "Hidden verification generated no foreground event for the test application");
            result = 0;
        }
        catch (Exception ex) { observations.Add(new { failure = ex.ToString() }); }
        finally
        {
            taskbarSearch?.Dispose();
            Directory.CreateDirectory(evidencePath);
            File.WriteAllText(Path.Combine(evidencePath, "s2-results.json"), JsonSerializer.Serialize(new { result,
                scenario = Environment.GetEnvironmentVariable("PATHWEFT_S2_PRESENTATION_ONLY") == "1" ? "hidden-presentation-diagnostic" : Environment.GetEnvironmentVariable("PATHWEFT_S2_DPI_ONLY") == "1" ? "real-monitor-dpi-only" : Environment.GetEnvironmentVariable("PATHWEFT_S2_NATIVE_ONLY") == "1" ? "native-shortcut-and-real-Explorer-lifecycle" : "s2-real-widget-workflow", hidden = HiddenTrialPresentation.Enabled,
                foregroundObserverActive = HiddenTrialPresentation.ForegroundObserverActive, foregroundViolations = HiddenTrialPresentation.ForegroundViolations, foregroundEvents = HiddenTrialPresentation.ForegroundEvents, activationMessages = HiddenTrialPresentation.ActivationMessages, pid = Environment.ProcessId, observations,
                remainingVerification = "Native hotkey dispatch and real Explorer restart remain distinct acceptance boundaries unless explicitly recorded above." }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(result);
        }
    }

    async Task VerifyNativeWidgetShortcut(List<object> observations, Action<bool, string> check, string term = "pwfts2_hit_0000.txt")
    {
        check(taskbarSearch.HotkeyRegistered, "WIDGET-03 OS owns the registered Ctrl+Alt+Space shortcut");
        check(new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(key => (NativeShortcut.GetAsyncKeyState(key) & 0x8000) == 0), "Native shortcut trial starts with no user modifier key held");
        IntPtr previous = NativeShortcut.GetForegroundWindow();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Displayed() => shown.TrySetResult();
        taskbarSearch.SearchShown += Displayed;
        try
        {
            var chord = new[] { NativeShortcut.Key(0x11), NativeShortcut.Key(0x12), NativeShortcut.Key(0x20), NativeShortcut.Key(0x20, 2), NativeShortcut.Key(0x12, 2), NativeShortcut.Key(0x11, 2) };
            NativeShortcut.Send(chord);
            await shown.Task;
            var widget = taskbarSearch.Widget;
            var query = (TextBox)widget.Pane.FindName("FilterBox");
            check(widget.IsActive && query.IsKeyboardFocusWithin, "WIDGET-03 real global shortcut activates the widget and focuses its query");
            // The shortcut's modifier-up events can still be queued when the
            // WM_HOTKEY handler shows the popup. Observe their actual processing.
            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ProcessInputEventHandler ReleaseInput = (_, _) => { if (Keyboard.Modifiers == ModifierKeys.None) released.TrySetResult(); };
            InputManager.Current.PostProcessInput += ReleaseInput;
            try { if (Keyboard.Modifiers == ModifierKeys.None) released.TrySetResult(); await released.Task; }
            finally { InputManager.Current.PostProcessInput -= ReleaseInput; }
            check(query.IsKeyboardFocusWithin, "WIDGET-03 query retains focus after the shortcut modifiers are released");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            KeyEventHandler EnterObserved = (_, key) => { if (key.Key == Key.Enter) entered.TrySetResult(); };
            widget.AddHandler(Keyboard.PreviewKeyDownEvent, EnterObserved, true);
            var previousSearch = widget.Pane.SearchCompletion;
            try
            {
                var typing = term.SelectMany(c => new[] { NativeShortcut.Unicode(c), NativeShortcut.Unicode(c, true) }).ToList();
                typing.Add(NativeShortcut.Key(0x0D)); typing.Add(NativeShortcut.Key(0x0D, 2));
                NativeShortcut.Send(typing.ToArray());
                await entered.Task;
                observations.Add(new { nativeTypedLength = query.Text.Length, typedExactFixture = query.Text == term, queryFocusedAtEnter = query.IsKeyboardFocusWithin, modifiersAtEnter = Keyboard.Modifiers.ToString() });
                check(query.Text == term && !ReferenceEquals(widget.Pane.SearchCompletion, previousSearch), "WIDGET-03 native Enter submits exactly the typed fixture query");
                await widget.Pane.SearchCompletion;
                check(query.Text == term && widget.Pane.Items.Single().Name == term, "WIDGET-03 native typing and Enter execute the actual widget query");
            }
            finally { widget.RemoveHandler(Keyboard.PreviewKeyDownEvent, EnterObserved); }
            observations.Add(new { nativeShortcut = "Ctrl+Alt+Space", resultCount = widget.Pane.IndexedTotal });
        }
        finally
        {
            taskbarSearch.SearchShown -= Displayed;
            taskbarSearch.Widget?.Dismiss();
            if (NativeShortcut.IsWindow(previous)) NativeShortcut.SetForegroundWindow(previous);
        }
    }

    static class NativeShortcut
    {
        [StructLayout(LayoutKind.Explicit, Size = 40)] internal struct Input
        {
            [FieldOffset(0)] internal uint Type;
            [FieldOffset(8)] internal ushort VirtualKey;
            [FieldOffset(10)] internal ushort Scan;
            [FieldOffset(12)] internal uint Flags;
        }
        internal static Input Key(ushort key, uint flags = 0) => new() { Type = 1, VirtualKey = key, Flags = flags };
        internal static Input Unicode(char c, bool up = false) => new() { Type = 1, Scan = c, Flags = up ? 6u : 4u };
        internal static void Send(Input[] input)
        {
            uint sent = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
            if (sent != input.Length)
            {
                var release = new[] { Key(0x20, 2), Key(0x12, 2), Key(0x11, 2) };
                SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                throw new InvalidOperationException("Windows did not accept the complete native test key packet.");
            }
        }
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
    }

    async Task VerifyWidgetMonitorTransitions(SearchWidgetWindow widget, Action<bool, string> check, List<object> observations)
    {
        await widget.Dispatcher.InvokeAsync(widget.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        HiddenTrialPresentation.AllowNativelyHiddenMonitorMoves(widget);
        var dpiEvents = new List<double>();
        widget.DpiChanged += (_, change) => dpiEvents.Add(change.NewDpi.PixelsPerInchX);
        var dpiValues = new List<uint>();
        foreach (var display in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = display.WorkingArea;
            var handle = new WindowInteropHelper(widget).Handle;
            check(TaskbarSearchHost.Native.SetWindowPos(handle, IntPtr.Zero, bounds.Left + 40, bounds.Top + 40, 680, 520, 0x0094), "WIDGET-03 hidden HWND moved to a real monitor");
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            uint dpi = TaskbarSearchHost.Native.GetDpiForWindow(handle);
            dpiValues.Add(dpi);
            check(HiddenTrialPresentation.IsNativelyHidden(widget), "WIDGET-03 monitor transition kept the native window hidden");
            observations.Add(new { physicalMonitor = bounds.ToString(), nativeDpi = dpi, visualDpi = System.Windows.Media.VisualTreeHelper.GetDpi(widget).PixelsPerInchX, dpiEvents = dpiEvents.ToArray() });
        }
        check(dpiValues.Distinct().Count() >= 2 && dpiEvents.Distinct().Count() >= 2, "WIDGET-03 real mixed-DPI monitor transitions generated WPF DPI events");
    }
}
#endif
