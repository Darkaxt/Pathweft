#if DEBUG
using FileExplorer.Preview;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Clipboard = System.Windows.Forms.Clipboard;

namespace FileExplorer;

internal static class S4Verification
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder name, int length, out int required);
    internal static string DesktopDiagnostic()
    {
        IntPtr desktop = OpenInputDesktop(0, false, 1);
        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        string name = null;
        if (desktop != IntPtr.Zero)
        {
            try { var buffer = new System.Text.StringBuilder(256); if (GetUserObjectInformation(desktop, 2, buffer, 512, out _)) name = buffer.ToString(); }
            finally { CloseDesktop(desktop); }
        }
        return JsonSerializer.Serialize(new { inputDesktop = name, openInputDesktopError = desktop == IntPtr.Zero ? error : 0, clipboardAvailable = MainWindow.ProbeClipboard(), sequence = GetClipboardSequenceNumber(), formats = MainWindow.CountClipboardFormats() });
    }
    internal static bool OwnedRoot(string path) => Path.GetFullPath(path).StartsWith(@"D:\Temp\pathweft-s4-", StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(@"D:\Temp\pathweft-rars-native\", StringComparison.OrdinalIgnoreCase);
    internal sealed record ClipboardPart(string Format, string Kind, string Value);
    internal static ClipboardPart SavePart(string format, object value) => value switch
    {
        string text => new(format, "text", text),
        MemoryStream stream => new(format, "stream", Convert.ToBase64String(stream.ToArray())),
        byte[] bytes => new(format, "bytes", Convert.ToBase64String(bytes)),
        string[] paths => new(format, "paths", JsonSerializer.Serialize(paths)),
        _ => throw new IOException("Unsupported clipboard snapshot type; no mutation is permitted.")
    };
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    internal static int CaptureClipboard(string snapshot, string expected)
    {
        if (!OwnedRoot(snapshot) || !OwnedRoot(expected)) return 1;
        try
        {
            if (!MainWindow.ProbeClipboard()) return 3;
            uint sequence = GetClipboardSequenceNumber();
            var parts = S4NativeClipboard.Read();
            var hashes = parts.ToDictionary(part => part.Format, part => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(part.Value))));
            hashes["__native_hglobal_v1__"] = "";
            File.WriteAllText(snapshot, JsonSerializer.Serialize(parts));
            File.WriteAllText(expected, JsonSerializer.Serialize(hashes));
            if (GetClipboardSequenceNumber() != sequence) throw new IOException("Clipboard changed during snapshot; no trial mutation permitted.");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(snapshot + ".error.txt", ex.ToString()); return 1; }
    }
    internal static int RestoreClipboard(string path, uint expectedSequence)
    {
        if (!OwnedRoot(path)) return 1;
        var parts = JsonSerializer.Deserialize<ClipboardPart[]>(File.ReadAllText(path));
        try { return S4NativeClipboard.Restore(parts, expectedSequence); }
        catch (Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); return 1; }
    }
    internal static string ClipboardHash(object value) => value switch
    {
        string text => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))),
        MemoryStream stream => Convert.ToHexString(SHA256.HashData(stream.ToArray())),
        byte[] bytes => Convert.ToHexString(SHA256.HashData(bytes)),
        string[] strings => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(strings))),
        _ => throw new IOException("The current clipboard format cannot be safely materialized by this finite trial.")
    };
    internal static int ReadClipboard(string path)
    {
        if (!OwnedRoot(path)) return 1;
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        if (!MainWindow.ProbeClipboard()) return 3;
        if (expected.Remove("__native_hglobal_v1__"))
        {
            var parts = S4NativeClipboard.Read();
            var hashes = parts.ToDictionary(part => part.Format, part => Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(part.Value))));
            var nativeMatches = expected.ToDictionary(item => item.Key, item => hashes.TryGetValue(item.Key, out string hash) && hash == item.Value);
            File.WriteAllText(path + ".readback.json", JsonSerializer.Serialize(new { sequence = GetClipboardSequenceNumber(), ownerProcess = MainWindow.ClipboardOwnerProcess(), formatCount = hashes.Count, expectedCount = expected.Count, matches = nativeMatches }));
            return hashes.Count == expected.Count && nativeMatches.Values.All(match => match) ? 0 : 1;
        }
        var actual = Clipboard.GetDataObject();
        bool matches = expected.All(item => actual != null && actual.GetDataPresent(item.Key, false) && ClipboardHash(actual.GetData(item.Key, false)) == item.Value);
        File.WriteAllText(path + ".readback.json", JsonSerializer.Serialize(new { sequence = GetClipboardSequenceNumber(), formats = actual?.GetFormats(false), matches = expected.ToDictionary(item => item.Key, item => actual != null && actual.GetDataPresent(item.Key, false) && ClipboardHash(actual.GetData(item.Key, false)) == item.Value) }));
        return matches ? 0 : !MainWindow.ProbeClipboard() || actual == null && MainWindow.CountClipboardFormats() != 0 ? 3 : 1;
    }
    internal static int RunConsumer(string path)
    {
        if (!OwnedRoot(path)) return 1;
        using var ready = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("PATHWEFT_S4_READY"));
        using var release = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("PATHWEFT_S4_RELEASE"));
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(SHA256.HashData(file));
        File.WriteAllText(Environment.GetEnvironmentVariable("PATHWEFT_S4_RECEIPT"), JsonSerializer.Serialize(new { hash, pid = Environment.ProcessId, readBytes = file.Length }));
        ready.Set(); release.WaitOne();
        return 0;
    }
    internal static int ReadExternalFileClipboard(string path, bool move)
    {
        if (!OwnedRoot(path)) return 1;
        if (!MainWindow.ProbeClipboard()) return 3;
        var files = Clipboard.GetFileDropList();
        return files.Count == 1 && files[0] == path && Clipboard.GetData("Preferred DropEffect") is MemoryStream effect && effect.Length >= 4 && ((BitConverter.ToInt32(effect.ToArray(), 0) & 2) != 0) == move ? 0 : 1;
    }
}

public partial class PaneView
{
    // Same listener used by the pane's native context-action entry point.
    internal void S4ContextAction() => RightDragSelected?.Invoke();
}

public partial class MainWindow
{
    internal async Task VerifyS4(string root)
    {
        root = Path.GetFullPath(root);
        if (!S4Verification.OwnedRoot(root) || Settings.Profile == null) throw new ArgumentException("Registered fixtures and an isolated profile are required.");
        bool rarWriterTrial = Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_RAR") == "1";
        string evidence = Path.Combine(root, "evidence", Environment.ProcessId.ToString()); Directory.CreateDirectory(evidence);
        var observations = new List<object>(); var clock = Stopwatch.StartNew(); int result = 1;
        bool clipboardChanged = false, clipboardFinished = false, originalClipboardRestored = false;
        uint restoredClipboardSequence = 0;
        EventWaitHandle consumerReady = null, consumerRelease = null; Process consumer = null;
        void Check(bool pass, string criterion)
        {
            observations.Add(new { criterion, passed = pass, elapsedMs = clock.Elapsed.TotalMilliseconds });
            HiddenTrialPresentation.Phase = criterion;
            File.WriteAllText(Path.Combine(evidence, "phase.json"), JsonSerializer.Serialize(new { criterion, pass }));
            if (!pass) throw new InvalidOperationException(criterion);
        }
        IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T value) yield return value;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task ClipboardAvailable()
        {
            await Task.Run(() =>
            {
                while (!ProbeClipboard())
                {
                    if (Volatile.Read(ref closingPreviews)) throw new OperationCanceledException();
                    Thread.Yield(); // Windows exposes no clipboard-release event; wait on actual availability, without a deadline.
                }
            });
        }
        ContextMenu Menu(PaneView pane)
        { SetActive(pane); archiveMenuForVerification = null; pane.S4ContextAction(); return archiveMenuForVerification ?? throw new IOException("Context menu was not constructed by its pane listener."); }
        void Action(PaneView pane, string action)
        {
            var menu = Menu(pane);
            try
            {
                var item = menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == action);
                Check(item.IsEnabled, "Actual pane action is enabled: " + action);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            finally { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
        }
        async Task Select(PaneView pane, string name)
        { SetActive(pane); pane.ListForVerification.SelectedItem = pane.Items.Single(e => e.Name == name); await PreviewSession.DrainAsync(); }
        async Task Listed(PaneView pane, string path, System.Action request)
        {
            var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed() { if (pane.Listed && pane.Dir.Equals(path, StringComparison.OrdinalIgnoreCase)) listed.TrySetResult(); }
            void Failed(string message) => listed.TrySetException(new IOException(message));
            pane.StatsChanged += Changed; pane.Error += Failed;
            try { pane.Listed = false; request(); await listed.Task; }
            finally { pane.StatsChanged -= Changed; pane.Error -= Failed; }
        }
        async Task<Job> Paste(PaneView pane, Choice? choice = null, bool expectDone = true)
        {
            pane.ListForVerification.SelectedItems.Clear(); // Paste into the viewed folder's background, not a selected child.
            var prior = jobs.ToHashSet(); Job job;
            while (true)
            {
                Action(pane, "Paste"); job = jobs.FirstOrDefault(j => !prior.Contains(j));
                if (job != null) break;
                if (!Status.Text.Contains("clipboard is in use", StringComparison.OrdinalIgnoreCase)) throw new IOException("Paste did not enter the shared queue: " + Status.Text);
                observations.Add(new { diagnostic = "Windows clipboard busy before any job dispatch; waiting for actual availability", status = Status.Text });
                await ClipboardAvailable();
            }
            if (choice is { } decision)
            {
                var conflict = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Changed(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (job.Phase == Phase.Conflicts) conflict.TrySetResult(); }
                job.PropertyChanged += Changed;
                try
                {
                    Changed(null, null);
                    await Task.WhenAny(conflict.Task, job.Completion);
                    Check(job.Phase == Phase.Conflicts, "Real archive conflict reaches the existing transfer card");
                    await Dispatcher.InvokeAsync(() => transfersWindow.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    var button = Descendants<Button>(transfersWindow).Single(b => b.DataContext == job && b.CommandParameter?.ToString() == decision.ToString());
                    Click(button);
                }
                finally { job.PropertyChanged -= Changed; }
            }
            await job.Completion; await clipboardOperation;
            observations.Add(new { diagnostic = "Ferry archive outcome", phase = job.Phase.ToString(), errors = job.Errors.ToArray(), notes = job.Notes.ToArray(), job.DoneFiles, job.TotalFiles, job.SkippedFiles });
            Check(expectDone ? job.Phase == Phase.Done && job.Errors.IsEmpty : job.Phase == Phase.Cancelled, expectDone ? "Actual paste finished in the one Ferry job owner" : "Interrupted paste terminates in the existing Ferry owner");
            return job;
        }
        string Hash(string path) => Archive.Fingerprint(path);
        async Task Copy(PaneView pane, bool cut)
        {
            var paths = pane.Selected.Select(e => e.Path).ToArray();
            Action(pane, cut ? "Cut" : "Copy"); await clipboardOperation;
            async Task<T> ReadClipboardWhenAvailable<T>(Func<T> read)
            {
                while (true)
                {
                    try { return read(); }
                    catch (IOException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
                    {
                        observations.Add(new { diagnostic = "Windows refused clipboard read before access; waiting for availability" });
                        await ClipboardAvailable();
                    }
                }
            }
            if (pane.ArchiveRoot != null)
            {
                var selected = await ReadClipboardWhenAvailable(() => ReadArchiveClipboard(out _));
                if ((selected == null || selected.Move != cut || !selected.Paths.SequenceEqual(paths)) && GetClipboardSequenceNumber() == lastOwnedClipboardSequence)
                    File.WriteAllText(Path.Combine(evidence, "clipboard-transfer-raw.json"), JsonSerializer.Serialize(new { expectedPaths = paths, readerDrop = ClipboardDropForVerification, parts = S4NativeClipboard.Read() }));
                Check(selected != null && selected.Move == cut && selected.Paths.SequenceEqual(paths), "Actual OS archive clipboard read-back matches the fixture selection");
            }
            else
            {
                var actual = await ReadClipboardWhenAvailable(() => ReadFileClipboard(out _));
                observations.Add(new { diagnostic = "Native clipboard copy readback", status = Status.Text, expectedCount = paths.Length, actualCount = actual.Paths.Count, actual.Move, cut, effect = ClipboardEffectForVerification, currentSequence = GetClipboardSequenceNumber(), ownedSequence = lastOwnedClipboardSequence });
                if ((!actual.Paths.SequenceEqual(paths) || actual.Move != cut) && GetClipboardSequenceNumber() == lastOwnedClipboardSequence)
                    File.WriteAllText(Path.Combine(evidence, "clipboard-transfer-raw.json"), JsonSerializer.Serialize(new { expectedPaths = paths, readerDrop = ClipboardDropForVerification, parts = S4NativeClipboard.Read() }));
                Check(actual.Paths.SequenceEqual(paths) && actual.Move == cut, "Actual native OS CF_HDROP read-back matches the fixture selection");
                if (paths.Length == 1)
                {
                    int code;
                    do
                    {
                        await ClipboardAvailable();
                        var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                        start.ArgumentList.Add("--verify-s4-external-file=" + paths[0]);
                        if (cut) start.ArgumentList.Add("--clipboard-cut");
                        using var reader = Process.Start(start); await reader.WaitForExitAsync(); code = reader.ExitCode;
                    } while (code == 3);
                    Check(code == 0, "Independent OLE consumer reads the actual file list and copy/cut DWORD");
                }
            }
        }
        async Task RestoreClipboard()
        {
            if (!clipboardChanged || clipboardFinished) return;
            if (GetClipboardSequenceNumber() != lastOwnedClipboardSequence)
            {
                observations.Add(new { criterion = "A newer clipboard owner superseded the trial; its contents were preserved", passed = true });
                clipboardFinished = true; return;
            }
            var restoreStart = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            restoreStart.ArgumentList.Add("--restore-s4-clipboard=" + Path.Combine(Settings.Dir, "clipboard-snapshot.json"));
            restoreStart.ArgumentList.Add("--clipboard-sequence=" + lastOwnedClipboardSequence);
            int restoreCode;
            do
            {
                await ClipboardAvailable();
                using var restorer = Process.Start(restoreStart); await restorer.WaitForExitAsync(); restoreCode = restorer.ExitCode;
            } while (restoreCode == 3); // native OpenClipboard refused before mutation; wait for actual availability.
            if (restoreCode == 2) { clipboardFinished = true; observations.Add(new { criterion = "Newer clipboard owner preserved at the independent restore boundary", passed = true }); return; }
            if (restoreCode != 0) throw new IOException("Independent clipboard restoration failed; the complete private snapshot was retained.");
            int readerCode;
            do
            {
                await ClipboardAvailable();
                var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--verify-s4-clipboard=" + Path.Combine(evidence, "clipboard-expected.json"));
                using var reader = Process.Start(start); await reader.WaitForExitAsync(); readerCode = reader.ExitCode;
            } while (readerCode == 3); // read-only retry at the observed Windows resource boundary
            Check(readerCode == 0, "Independent process verified original OS clipboard restoration against all captured format hashes");
            restoredClipboardSequence = GetClipboardSequenceNumber();
            clipboardFinished = originalClipboardRestored = true;
        }
        try
        {
            captureS4Menu = true; // MenuItem listeners run; native popup placement is not a hidden-test claim.
            s4ClipboardFixtureRoot = root;
            Check(HiddenTrialPresentation.Enabled && HiddenTrialPresentation.IsOutsideDisplays(this), "Trial uses actual offscreen native geometry");
            string containers = Path.Combine(root, "containers"), source = Path.Combine(root, "source"), destination = Path.Combine(root, "destination");
            File.WriteAllText(Path.Combine(root, "active-trial.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, evidence }));
            await S1Verification.Navigate(Left, containers); await S1Verification.Navigate(Right, destination);
            Width = 1280; Height = 820;
            if (Settings.SinglePane) SetSinglePane(false);
            foreach (string extension in new[] { "zip", "7z", "tar", "cab", "rar" })
            {
                await S1Verification.Navigate(Left, containers); await Select(Left, "sample." + extension);
                Action(Left, "Quick View"); await quickPreview.Completion;
                Check(quickPreview.State == "ready" && quickPreview.ModuleId == "archive" && !ArchiveContent.Retained().Any(), "ARCHIVE-05 physical " + extension + " container preview reads metadata without extracting itself");
                quick.Close(); await PreviewSession.DrainAsync();
                await Listed(Left, Path.Combine(containers, "sample." + extension), () => Action(Left, "Open"));
                Check(Left.ArchiveRoot != null && Left.Items.Any(e => e.Name == (extension == "rar" ? "helloworld.txt" : "image.png")), "ARCHIVE-01/02 real pane Open browses native " + extension + " metadata");
                if (extension == "rar")
                {
                    Check(!Archive.CanChange(Left.Dir), "The read-only RAR fixture disables editing without losing previews");
                    await Select(Left, "helloworld.txt"); Action(Left, "Preview"); await quickPreview.Completion;
                    Check(quickPreview.State == "ready" && quickPreview.ModuleId == "text", "Real RAR5 text entry renders without a standalone utility");
                    var content = ArchiveContent.Retained().Single(r => r.Purpose == "preview");
                    Check(File.ReadAllText(Path.Combine(ArchiveContent.Root, content.Id, content.Files[0])) == "hello libarchive test suite!\n", "RAR5 extracted content matches the independent upstream fixture oracle");
                    quick.Close(); await PreviewSession.DrainAsync();
                }
            }
            string archive = Path.Combine(containers, rarWriterTrial ? "writable.rar" : "sample.7z");
            await S1Verification.Navigate(Left, archive);
            int priorTabs = Left.OpenTabs.Count;
            await Listed(Left, archive, () => Click((Button)Left.FindName("NewTabButton")));
            Check(Left.OpenTabs.Count == priorTabs + 1 && Left.OpenTabs[Left.ActiveTab] == archive, "ARCHIVE-02 the actual new-tab action preserves its virtual archive location");
            Left.CloseTab();
            await S1Verification.Navigate(Left, archive);
            var sizes = Left.Items.Where(e => !e.IsDir && !e.IsUp).OrderByDescending(e => e.Size).Select(e => e.Name).ToArray();
            Left.SetSort("Size", true);
            Check(Left.ListForVerification.Items.Cast<Entry>().Where(e => !e.IsDir && !e.IsUp).Select(e => e.Name).SequenceEqual(sizes), "ARCHIVE-02 archive rows use normal pane sorting");
            Left.SetSort("Name", false);
            Check(!ArchiveContent.Retained().Any(), "ARCHIVE-02 archive browsing materializes no extracted working copy");
            if (!Settings.Preview) Click(Descendants<Button>(this).Single(b => b.ToolTip?.ToString() == "Show the selected file beside the panes"));
            await Select(Left, "image.png");
            Check(sidePreview.ModuleId == "image" && sidePreview.State == "ready", "ARCHIVE-05 native archive entry reaches the real internal image preview");
            var previewCopy = ArchiveContent.Retained().Single(r => r.Purpose == "preview");
            bool activePreviewProtected = false;
            try { ArchiveContent.Release(previewCopy); } catch (IOException) { activePreviewProtected = true; }
            Check(activePreviewProtected && Directory.Exists(Path.Combine(ArchiveContent.Root, previewCopy.Id)), "ARCHIVE-05 an active internal viewer owns its lease until its real completion");
            Check(previewCopy.Files.Length == 1 && Hash(Path.Combine(ArchiveContent.Root, previewCopy.Id, previewCopy.Files[0])) == Hash(Path.Combine(source, "image.png")), "ARCHIVE-05 only the selected entry is extracted and hash-bound");
            await S1Verification.Snapshot(this, evidence, "archive-native-image-preview");
            await Select(Left, "nested"); await Listed(Left, archive + "\\nested", () => Action(Left, "Open"));
            await Select(Left, "readme.md"); Action(Left, "Preview"); await quickPreview.Completion;
            Check(quickPreview.State == "ready" && quickPreview.ModuleId == "markdown", "ARCHIVE-05 nested archive Markdown renders in the governed popup");
            await S1Verification.Snapshot(quick, evidence, "archive-markdown-popup");
            quick.Close(); sidePreview.Clear(); await PreviewSession.DrainAsync();
            Check(!ArchiveContent.Retained().Any(r => r.Purpose == "preview"), "ARCHIVE-05 closing internal previews releases their entry-bound copies");
            if (Settings.Preview) TogglePreview();
            var crumbs = (Panel)Left.FindName("Crumbs");
            var rootCrumb = crumbs.Children.OfType<Button>().Single(b => b.Content is TextBlock text && text.Text == Path.GetFileName(archive));
            await Listed(Left, archive, () => Click(rootCrumb));
            Check(Left.Items.Any(e => e.Name == "nested"), "ARCHIVE-02 actual breadcrumb returns from nested entries to the archive root");
            await S1Verification.Navigate(Left, archive + "\\nested");

            await Select(Left, "second.txt"); Action(Left, "Rename");
            await S1Verification.Snapshot(this, evidence, "archive-inline-rename");
            var box = Descendants<TextBox>(Left).Single(b => b.Tag is Entry { Renaming: true }); box.Text = "renamed 子.txt";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await lastArchiveJob.Completion;
            Check(Archive.List(archive, "nested").Any(i => i.Name == "renamed 子.txt") && !Archive.List(archive, "nested").Any(i => i.Name == "second.txt"), "ARCHIVE-03 actual inline rename updates the native container");

            var captureStart = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            captureStart.ArgumentList.Add("--snapshot-s4-clipboard=" + Path.Combine(Settings.Dir, "clipboard-snapshot.json"));
            captureStart.ArgumentList.Add("--clipboard-expected=" + Path.Combine(evidence, "clipboard-expected.json"));
            int captureCode;
            do
            {
                await ClipboardAvailable();
                using var capture = Process.Start(captureStart); await capture.WaitForExitAsync(); captureCode = capture.ExitCode;
            } while (captureCode == 3);
            Check(captureCode == 0, "Independent STA snapshot persisted all original clipboard formats before any mutation");
            clipboardChanged = true;
            await S1Verification.Navigate(Left, containers); await Select(Left, "sample.zip"); await Copy(Left, false);
            await S1Verification.Navigate(Right, destination); var archiveFileCopy = await Paste(Right);
            Check(!archiveFileCopy.ArchiveOperation && Hash(Path.Combine(destination, "sample.zip")) == Hash(Path.Combine(containers, "sample.zip")), "CORE-02 ordinary archive files still copy as disk files through Ferry");
            await S1Verification.Navigate(Right, archive); await Paste(Right);
            Check(Archive.Read(archive).Any(e => e.Path == "sample.zip"), "ARCHIVE-03 filesystem archive files can be copied into another archive as ordinary payloads");
            await S1Verification.Navigate(Left, source); await Select(Left, "plain 子 spaced.txt"); await Copy(Left, false);
            Action(Left, "Compress to ZIP"); await lastArchiveJob.Completion;
            string createdZip = Path.Combine(source, "plain 子 spaced.zip");
            Check(lastArchiveJob.Phase == Phase.Done && lastArchiveJob.Errors.IsEmpty && Archive.EntryHash(createdZip, Archive.Read(createdZip).Single()) == Hash(Path.Combine(source, "plain 子 spaced.txt")), "ARCHIVE-01/03 Compress to ZIP uses the native backend and existing verified job owner");
            if (rarWriterTrial)
            {
                await Select(Left, "plain 子 spaced.txt"); // ZIP creation selects its arrival; restore this test's input.
                Action(Left, "Compress to RAR"); await lastArchiveJob.Completion;
                string createdRar = Path.Combine(source, "plain 子 spaced.rar");
                Check(lastArchiveJob.Phase == Phase.Done && lastArchiveJob.Errors.IsEmpty && Archive.EntryHash(createdRar, Archive.Read(createdRar).Single()) == Hash(Path.Combine(source, "plain 子 spaced.txt")), "RAR-01/02 real Compress to RAR uses the native DLL and shared queue");
            }
            await S1Verification.Navigate(Right, archive + "\\nested"); await Paste(Right);
            Check(Archive.List(archive, "nested").Any(e => e.Name == "plain 子 spaced.txt"), "ARCHIVE-03 actual OS clipboard copies filesystem content into the viewed archive folder");
            await S1Verification.Navigate(Left, archive + "\\nested"); await Select(Left, "plain 子 spaced.txt"); await Copy(Left, true);
            Check(HasArchiveClipboard() && !IsClipboardFormatAvailable(15), "ARCHIVE-03 archive cut retains virtual source identity without pretending it is a disk file");
            await S1Verification.Navigate(Right, destination); await Paste(Right);
            Check(Hash(Path.Combine(destination, "plain 子 spaced.txt")) == Hash(Path.Combine(source, "plain 子 spaced.txt")) && !Archive.List(archive, "nested").Any(e => e.Name == "plain 子 spaced.txt"), "ARCHIVE-03/04 archive cut removes its entry only after a verified filesystem destination");

            await S1Verification.Navigate(Left, archive + "\\nested"); await Select(Left, "readme.md"); await Copy(Left, false);
            string zip = Path.Combine(containers, "sample.zip"); await S1Verification.Navigate(Right, zip); await Paste(Right);
            Check(Archive.List(zip, "").Any(e => e.Name == "readme.md") && Archive.List(archive, "nested").Any(e => e.Name == "readme.md"), "ARCHIVE-03 archive-to-archive copy preserves its source");
            await S1Verification.Navigate(Left, zip); await Select(Left, "readme.md"); await Copy(Left, true);
            await S1Verification.Navigate(Right, archive); await Paste(Right);
            Check(Archive.List(archive, "").Any(e => e.Name == "readme.md") && !Archive.List(zip, "").Any(e => e.Name == "readme.md"), "ARCHIVE-03 archive-to-archive cut verifies destination before source removal");

            await S1Verification.Navigate(Left, source); await Select(Left, "plain 子 spaced.txt"); await Copy(Left, false);
            await S1Verification.Navigate(Right, archive); var skipped = await Paste(Right, Choice.Skip);
            Check(skipped.SkippedFiles == 1 && skipped.DoneFiles == 0 && skipped.Title.Contains("skipped"), "ARCHIVE-04 actual Skip decision reports retained source and incomplete member delivery");
            await Paste(Right, Choice.Rename);
            Check(Archive.List(archive, "").Any(e => e.Name == "plain 子 spaced (2).txt"), "ARCHIVE-04 actual Keep both decision preserves both archive members");
            await Paste(Right, Choice.Overwrite);
            Check(Archive.EntryHash(archive, Archive.Read(archive).Single(e => e.Path == "plain 子 spaced.txt")) == Hash(Path.Combine(source, "plain 子 spaced.txt")), "ARCHIVE-04 actual Replace decision publishes verified content");

            string tree = Path.Combine(source, "tree-cut"); Directory.CreateDirectory(Path.Combine(tree, "empty"));
            File.WriteAllText(Path.Combine(tree, "one.txt"), "one"); File.WriteAllText(Path.Combine(tree, "two.txt"), "two");
            await S1Verification.Navigate(Left, source); await Select(Left, "tree-cut"); await Copy(Left, true);
            await S1Verification.Navigate(Right, archive); await Paste(Right);
            Check(!Directory.Exists(tree) && Archive.List(archive, "tree-cut").Any(e => e.IsDir && e.Name == "empty"), "ARCHIVE-03/04 filesystem folder cut preserves empty directories and removes only verified sources");
            await S1Verification.Navigate(Left, archive); await Select(Left, "tree-cut"); await Copy(Left, false);
            await S1Verification.Navigate(Right, zip); await Paste(Right);
            Check(Archive.List(zip, "tree-cut").Count == 3, "ARCHIVE-03 native archive-to-archive directory copy preserves descendants");
            await S1Verification.Navigate(Left, archive); await Select(Left, "tree-cut"); await Copy(Left, true);
            await S1Verification.Navigate(Right, archive + "\\nested"); await Paste(Right);
            Check(!Archive.List(archive, "").Any(e => e.Name == "tree-cut") && Archive.List(archive, "nested/tree-cut").Count == 3, "ARCHIVE-03 same-container directory cut is one verified native edit");
            string folderDest = Path.Combine(destination, "tree-cut"); Directory.CreateDirectory(folderDest);
            File.WriteAllText(Path.Combine(folderDest, "one.txt"), "existing destination");
            await S1Verification.Navigate(Left, archive + "\\nested"); await Select(Left, "tree-cut"); await Copy(Left, true);
            await S1Verification.Navigate(Right, destination); var partial = await Paste(Right, Choice.Skip);
            Check(partial.SkippedFiles == 1 && File.ReadAllText(Path.Combine(folderDest, "one.txt")) == "existing destination" && File.ReadAllText(Path.Combine(folderDest, "two.txt")) == "two" && Archive.List(archive, "nested/tree-cut").Any(e => e.Name == "one.txt"), "ARCHIVE-04 partial cut retains skipped source and existing target while verified members move");
            await S1Verification.Navigate(Left, archive + "\\nested"); await Select(Left, "tree-cut"); await Copy(Left, true);
            await Paste(Right, Choice.Overwrite);
            Check(File.ReadAllText(Path.Combine(folderDest, "one.txt")) == "one" && !Archive.List(archive, "nested").Any(e => e.Name == "tree-cut"), "ARCHIVE-04 remaining cut members can be explicitly replaced without losing skipped data");

            string cancelSource = Path.Combine(source, "cancel.txt"); File.WriteAllText(cancelSource, "retained on cancellation");
            await S1Verification.Navigate(Left, source); await Select(Left, "cancel.txt"); await Copy(Left, true);
            await S1Verification.Navigate(Right, archive); string originalArchive = Hash(archive);
            using (var cancelled = new ManualResetEventSlim())
            {
                Archive.VerificationBoundary = phase =>
                {
                    if (phase != "stage-written") return;
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            transfersWindow.UpdateLayout();
                            Click(Descendants<Button>(transfersWindow).Single(b => b.DataContext == lastArchiveJob && b.Content is TextBlock text && new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text.Trim().EndsWith("Cancel")));
                        }
                        finally { cancelled.Set(); }
                    });
                    cancelled.Wait();
                };
                try { await Paste(Right, expectDone: false); }
                finally { Archive.VerificationBoundary = null; }
            }
            Check(Hash(archive) == originalArchive && File.Exists(cancelSource), "ARCHIVE-04 real transfer-card cancellation before publication preserves archive and cut source");
            Archive.VerificationBoundary = phase => { if (phase == "before-source-removal") File.AppendAllText(cancelSource, " changed concurrently"); };
            try { await Paste(Right, expectDone: false); }
            finally { Archive.VerificationBoundary = null; }
            Check(File.ReadAllText(cancelSource).EndsWith(" changed concurrently") && Archive.Read(archive).Any(e => e.Path == "cancel.txt"), "ARCHIVE-04 concurrent source change retains the source after a verified destination was published");
            await S1Verification.Navigate(Left, Path.Combine(containers, "sample.rar")); await Select(Left, "helloworld.txt");
            var rarMenu = Menu(Left);
            Check(!rarMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Cut").IsEnabled && !rarMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Rename").IsEnabled, "RAR menu exposes Copy/Preview while disabling unsupported native edits");
            rarMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            await Copy(Left, false); await S1Verification.Navigate(Right, destination); await Paste(Right);
            Check(File.ReadAllText(Path.Combine(destination, "helloworld.txt")) == "hello libarchive test suite!\n", "RAR5 selected-entry copy uses the same queue and verified filesystem destination");
            await RestoreClipboard();
            await S1Verification.Navigate(Left, containers); await Select(Left, "sample.rar"); Action(Left, "Extract to sample\\"); await lastArchiveJob.Completion;
            Check(lastArchiveJob.Phase == Phase.Done && lastArchiveJob.Errors.IsEmpty && File.ReadAllText(Path.Combine(containers, "sample", "helloworld.txt")) == "hello libarchive test suite!\n", "ARCHIVE-01/03 whole-container Extract routes through the same verified Ferry owner");

            await S1Verification.Navigate(Left, archive); await Select(Left, "image.png");
            string key = Guid.NewGuid().ToString("N"), receipt = Path.Combine(evidence, "external-consumer.json");
            consumerReady = new(false, EventResetMode.ManualReset, "Local\\Pathweft-S4-ready-" + key);
            consumerRelease = new(false, EventResetMode.ManualReset, "Local\\Pathweft-S4-release-" + key);
            ArchiveConsumerForVerification = path =>
            {
                var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--archive-s4-consumer=" + path);
                start.Environment["PATHWEFT_S4_READY"] = "Local\\Pathweft-S4-ready-" + key;
                start.Environment["PATHWEFT_S4_RELEASE"] = "Local\\Pathweft-S4-release-" + key;
                start.Environment["PATHWEFT_S4_RECEIPT"] = receipt;
                return consumer = Process.Start(start);
            };
            Action(Left, "Open"); await archiveOpenCompletion;
            await Task.WhenAny(Task.Run(() => consumerReady.WaitOne()), consumer.WaitForExitAsync());
            Check(File.Exists(receipt) && !consumer.HasExited, "ARCHIVE-05 a real hidden consumer opened the actual selected extracted file");
            var openedCopy = ArchiveContent.Retained().Single(r => r.Purpose == "opened file");
            var consumerEvidence = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(receipt));
            Check(consumerEvidence.GetProperty("hash").GetString() == Hash(Path.Combine(source, "image.png")), "ARCHIVE-05 independent consumer read-back matches the archived content");
            ShowArchiveWorkingCopies(); await S1Verification.Snapshot(archiveCopiesWindow, evidence, "archive-working-copies-100");
            Button ReleaseButton() => Descendants<Button>(archiveCopiesWindow).Single(b => b.Tag is ArchiveContent.Record r && r.Id == openedCopy.Id);
            Click(ReleaseButton());
            Check(ArchiveContent.Retained().Any(r => r.Id == openedCopy.Id), "ARCHIVE-05 locked consumer content survives a premature release action");
            consumerRelease.Set(); await consumer.WaitForExitAsync();
            Check(consumer.ExitCode == 0 && ArchiveContent.Retained().Any(r => r.Id == openedCopy.Id), "ARCHIVE-05 consumer/launcher exit alone does not delete its retained copy");
            Click(ReleaseButton()); Check(!Directory.Exists(Path.Combine(ArchiveContent.Root, openedCopy.Id)), "ARCHIVE-05 explicit closed-consumer action releases the owned copy");
            Settings.TextScale = 1.5; ApplyAppearance();
            await S1Verification.Snapshot(archiveCopiesWindow, evidence, "archive-working-copies-150");
            archiveCopiesWindow.Close(); Settings.TextScale = 1; ApplyAppearance();

            foreach (string bad in new[] { "broken.7z", "traversal.zip", "duplicate.zip", "multipart.7z.001" })
            {
                bool unavailable = false;
                try { await S1Verification.Navigate(Left, Path.Combine(containers, bad)); } catch (IOException) { unavailable = true; }
                Check(unavailable && !File.Exists(Path.Combine(root, "escape.txt")), "ARCHIVE-02/04 honest unavailable state and no unsafe extraction: " + bad);
            }
            foreach (string readOnly in new[] { "wrapped.tgz", "single.txt.gz" })
            {
                await S1Verification.Navigate(Left, Path.Combine(containers, readOnly));
                Check(Left.Items.Any() && !Archive.CanChange(Left.Dir), "ARCHIVE-01/02 preserved read-only compressed-container browsing: " + readOnly);
            }
            string protectedArchive = Path.Combine(containers, "protected.zip"); File.Copy(Path.Combine(containers, "sample.zip"), protectedArchive);
            File.SetAttributes(protectedArchive, File.GetAttributes(protectedArchive) | FileAttributes.ReadOnly);
            try
            {
                await S1Verification.Navigate(Left, protectedArchive); await Select(Left, "image.png");
                var protectedMenu = Menu(Left);
                try { Check(!Archive.CanChange(protectedArchive) && !protectedMenu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Rename").IsEnabled && Descendants<TextBlock>(Left).Any(t => t.Text.Contains("read-only")), "ARCHIVE-02/03 Windows read-only archive files remain browsable and disable editing in the actual pane"); }
                finally { protectedMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
            }
            finally { File.SetAttributes(protectedArchive, File.GetAttributes(protectedArchive) & ~FileAttributes.ReadOnly); }
            bool encrypted = false;
            string encryptedPath = Path.Combine(containers, "encrypted.7z");
            try { await S1Verification.Navigate(Left, encryptedPath); } catch (IOException) { encrypted = true; }
            Check(encrypted, "ARCHIVE-02 encrypted headers are unavailable without a password");
            var passwordListed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ListedPassword() { if (Left.Listed && Left.Items.Any(e => e.Name == "readme.md")) passwordListed.TrySetResult(); }
            Left.StatsChanged += ListedPassword;
            try
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Archive password");
                    Descendants<PasswordBox>(dialog).Single().Password = "pathweft-fixture-only";
                    Click(Descendants<Button>(dialog).Single(b => b.Name == "ArchivePasswordOpen"));
                });
                Action(Left, "Enter archive password…"); await passwordListed.Task;
            }
            finally { Left.StatsChanged -= ListedPassword; }
            Check(!Archive.CanChange(encryptedPath) && !File.ReadAllText(Settings.FilePath).Contains("pathweft-fixture-only"), "ARCHIVE-01/05 encrypted contents list with an ephemeral password and honest read-only capability");
            await Select(Left, "readme.md"); Action(Left, "Preview"); await quickPreview.Completion;
            Check(quickPreview.State == "ready" && quickPreview.ModuleId == "markdown", "ARCHIVE-05 encrypted entry previews with its ephemeral session password");
            quick.Close(); await PreviewSession.DrainAsync();
            Action(Left, "Forget archive password");
            if (rarWriterTrial)
            {
                string protectedRar = Path.Combine(containers, "encrypted-rar5.rar");
                bool needsPassword = false;
                try { await S1Verification.Navigate(Left, protectedRar); } catch (IOException) { needsPassword = true; }
                Check(needsPassword, "RAR-03 encrypted RAR headers require a session password");
                var unlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void RarListed() { if (Left.Listed && Left.Items.Any(e => e.Name == "second.txt")) unlocked.TrySetResult(); }
                Left.StatsChanged += RarListed;
                try
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Archive password");
                        Descendants<PasswordBox>(dialog).Single().Password = "fixture-password";
                        Click(Descendants<Button>(dialog).Single(b => b.Name == "ArchivePasswordOpen"));
                    });
                    Action(Left, "Enter archive password…"); await unlocked.Task;
                }
                finally { Left.StatsChanged -= RarListed; }
                Check(Archive.CanChange(protectedRar), "RAR-03 qualified encrypted RAR exposes native edit actions after unlock");
                await Select(Left, "second.txt"); Action(Left, "Preview"); await quickPreview.Completion;
                Check(quickPreview.State == "ready" && quickPreview.ModuleId == "text", "RAR-05 encrypted RAR selection reaches the real internal text preview");
                quick.Close(); await PreviewSession.DrainAsync();
                Action(Left, "Rename");
                var rename = Descendants<TextBox>(Left).Single(b => b.Tag is Entry { Renaming: true }); rename.Text = "secret 子.txt";
                rename.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await lastArchiveJob.Completion;
                Check(lastArchiveJob.Phase == Phase.Done && lastArchiveJob.Errors.IsEmpty && Archive.Read(protectedRar).Any(e => e.Path == "secret 子.txt"), "RAR-02/03 actual encrypted RAR inline rename preserves a verified readable container");
                await S1Verification.Navigate(Left, protectedRar);
                await S1Verification.Snapshot(this, evidence, "rar-encrypted-edited-pane");
                Check(!File.ReadAllText(Settings.FilePath).Contains("fixture-password"), "RAR-03 session password never reaches settings");
                Action(Left, "Forget archive password");
                Check(!Archive.CanChange(protectedRar), "RAR-03 forgetting the password disables encrypted editing");
                await S1Verification.Navigate(Left, Path.Combine(containers, "with_recovery.rar"));
                await Select(Left, Left.Items.First(e => !e.IsUp).Name);
                var unavailable = Menu(Left);
                try
                {
                    var item = unavailable.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Rename");
                    Check(!item.IsEnabled && item.ToolTip?.ToString()?.Contains("recovery") == true, "RAR-03 actual unsupported edit action exposes its preservation reason");
                }
                finally { unavailable.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
            }
            Check(HiddenTrialPresentation.ForegroundObserverActive && HiddenTrialPresentation.ForegroundViolations == 0, "Hidden archive verification generated zero foreground events");
            result = 0;
        }
        catch (Exception ex) { observations.Add(new { failure = ex.ToString() }); }
        finally
        {
            consumerRelease?.Set();
            if (consumer != null) { await consumer.WaitForExitAsync(); consumer.Dispose(); }
            consumerReady?.Dispose(); consumerRelease?.Dispose();
            ArchiveConsumerForVerification = null;
            if (clipboardChanged)
            {
                try
                {
                    await RestoreClipboard();
                }
                catch (Exception ex) { result = 1; observations.Add(new { clipboardRestorationFailure = ex.ToString() }); }
            }
            closingPreviews = true; taskbarSearch?.Dispose();
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) if (window != this) window.Close();
            PreviewSession.ClearAll(); await PreviewSession.DrainAsync();
            if (!clipboardChanged || clipboardFinished)
                foreach (var record in ArchiveContent.Retained()) ArchiveContent.Release(record);
            File.WriteAllText(Path.Combine(evidence, "s4-results.json"), JsonSerializer.Serialize(new { result, pid = Environment.ProcessId, scenario = rarWriterTrial ? "embedded-rar-native-archive-workflow" : "complete-native-archive-workflow", originalClipboardRestored, restoredClipboardSequence, foregroundViolations = HiddenTrialPresentation.ForegroundViolations, HiddenTrialPresentation.ForegroundEvents, HiddenTrialPresentation.ActivationMessages, observations }, new JsonSerializerOptions { WriteIndented = true }));
            quitting = true;
            Application.Current.Shutdown(result);
        }
    }
}
#endif
