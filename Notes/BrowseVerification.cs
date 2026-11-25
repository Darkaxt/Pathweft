#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

// S21 qualification ("browse" scenario): BROWSE-01..11 and MONITOR-25 through the real panes, stores and
// processes. Everything stays under `root`; remote checks use local rclone endpoints serving a folder under
// `root` (never a user server); no clipboard, Recycle Bin or Credential Manager writes.
public partial class MainWindow
{
    internal async Task VerifyBrowse21(string root, Action<bool, string> strict)
    {
        var failures = new List<string>();
        void check(bool ok, string label) { if (ok) strict(true, label); else failures.Add(label); }
        SetSinglePane(false); SetActive(Left);
        await Step("suggestions", () => VerifySuggestions21(Path.Combine(root, "suggest"), check));
        await Step("actions", () => VerifyActions21(Path.Combine(root, "actions"), check));
        await Step("profiles", () => VerifyProfiles21(Path.Combine(root, "profiles"), check));
        await Step("sync", () => VerifySync21(Path.Combine(root, "sync"), check));
        await Step("notes-filter", () => VerifyNotes21(Path.Combine(root, "notes"), check));
        await Step("links", () => VerifyLinks21(Path.Combine(root, "links"), check));
        await Step("remote", () => VerifyRemote21(Path.Combine(root, "remote"), check));
        await Step("terminal", () => VerifyTerminal21(Path.Combine(root, "terminal"), check));
        await Step("monitor", () => VerifyMonitor25(root, check));
        await Step("busy-clipboard", async () =>
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "clipboard 子")).FullName, file = Path.Combine(folder, "copied é.txt");
            File.WriteAllText(file, "CORE-34");
            await VerifyBusyClipboard34(folder, file, check); // held open only: this desktop shares the owner's clipboard
        });
        strict(failures.Count == 0, "S21: every browse check passed" + string.Concat(failures.Select(f => " | failed: " + f)));

        async Task Step(string name, Func<Task> run)
        {
            try { await run(); }
            catch (Exception e) { check(false, $"S21 {name} checks ran to the end: {e.GetType().Name}: {e.Message}"); }
        }
    }

    static async Task<bool> Until(Func<bool> done, int ms = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (!done()) { if (clock.ElapsedMilliseconds > ms) return false; await Task.Delay(50); }
        return true;
    }
    static IEnumerable<DependencyObject> Tree(DependencyObject node)
    {
        yield return node;
        int n = node is Visual ? VisualTreeHelper.GetChildrenCount(node) : 0;
        for (int i = 0; i < n; i++) foreach (var d in Tree(VisualTreeHelper.GetChild(node, i))) yield return d;
    }

    // BROWSE-01/02 and BROWSE-03
    async Task VerifySuggestions21(string root, Action<bool, string> check)
    {
        var a = Directory.CreateDirectory(Path.Combine(root, "Alpha projects")).FullName;
        var unicode = Directory.CreateDirectory(Path.Combine(root, "Ünïcode 子 folder")).FullName;
        var deep = Directory.CreateDirectory(Path.Combine(root, "deep", "alphabet soup")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "complete-me"));
        Settings.RecentFolders = new() { unicode, deep, a };
        await S1Verification.Navigate(Right, a); // an open tab in the other pane
        await S1Verification.Navigate(Left, root);
        var saved = new SavedSearch { Name = "Alpha reports", Query = "*.txt", Folder = root };
        SavedSearches.Put(saved);
        check(SavedSearchList.Children.OfType<FrameworkElement>().Any(), "BROWSE-03: a saved search appears in the sidebar section");

        List<PaneView.Suggestion> Shown() => Left.SuggestList.Items.Cast<FrameworkElement>().Select(r => r.Tag as PaneView.Suggestion).Where(s => s != null).ToList();
        Activate(); // the list opens only for a focused address, as when the user clicks it
        Left.FocusFilter();
        check(await Until(() => IsActive && Left.FilterBox.IsKeyboardFocusWithin, 5000), "BROWSE-01: the address field has keyboard focus (precondition)");
        check(await Until(() => Shown().Count > 0 && Shown().All(s => s.Kind == PaneView.SuggestionKind.Recent)), "BROWSE-01: an empty focused address lists recent destinations");
        Left.FilterBox.Text = "alpha";
        var shown = Shown();
        check(shown.Any(s => s.Kind == PaneView.SuggestionKind.Tab && s.Pane == Right), "BROWSE-01: an open tab of the other pane is offered as a switch-to-tab row");
        check(shown.Any(s => s.Kind == PaneView.SuggestionKind.SavedSearch && s.Search == saved), "BROWSE-01: the saved search is offered");
        check(shown.Any(s => s.Path == deep), "BROWSE-01: a name substring (not a prefix, not an absolute path) matches");
        check(shown.FindIndex(s => s.Path == a && s.Kind != PaneView.SuggestionKind.Tab) < shown.FindIndex(s => s.Path == deep), "BROWSE-01: a name prefix ranks above a name substring");
        check(shown.Count(s => s.Path == a && s.Kind != PaneView.SuggestionKind.Tab) == 1, "BROWSE-01: equal destinations collapse into one row besides the tab row");
        Left.FilterBox.Text = "子";
        check(Shown().Any(s => s.Path == unicode), "BROWSE-01: Unicode names match");
        // completion: one listing of the typed parent, merged later; an older keystroke never replaces it
        Left.FilterBox.Text = root + "\\compl";
        check(await Until(() => Shown().Any(s => s.Kind == PaneView.SuggestionKind.Completion && s.Path.EndsWith("complete-me"))), "BROWSE-01: filesystem completion still supplements a typed path");
        check(Left.FilterBox.Text == root + "\\compl" && Left.FilterBox.IsKeyboardFocusWithin && Left.Suggest.IsOpen, "BROWSE-02: background completion leaves the draft and focus alone");
        // Escape closes the list and keeps the draft
        var esc = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(Left.FilterBox), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        Left.FilterBox.RaiseEvent(esc);
        check(!Left.Suggest.IsOpen && Left.FilterBox.Text == root + "\\compl", "BROWSE-02: Escape dismisses suggestions without discarding the draft");
        // BROWSE-13: %name offers the environment variables naming a folder; Tab writes the variable, ready for the next level
        Environment.SetEnvironmentVariable("PathweftVerifyFolder", unicode);
        Environment.SetEnvironmentVariable("PathweftVerifyFile", Path.Combine(unicode, "none.txt"));
        Left.FilterBox.Text = "%pathweftverify";
        var variables = Shown().Where(s => s.Kind == PaneView.SuggestionKind.Variable).ToList();
        check(variables.Count == 1 && variables[0].Name == "%PATHWEFTVERIFYFOLDER%" && variables[0].Path == unicode && Left.Suggest.IsOpen, "BROWSE-13: %name lists only the variables naming a folder: " + string.Join(", ", variables.Select(v => v.Name)));
        Left.SuggestList.SelectedIndex = Shown().FindIndex(s => s.Kind == PaneView.SuggestionKind.Variable);
        var tab = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(Left.FilterBox), Environment.TickCount, System.Windows.Input.Key.Tab) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        Left.FilterBox.RaiseEvent(tab);
        check(Left.FilterBox.Text == "%PATHWEFTVERIFYFOLDER%\\", "BROWSE-13: Tab completes the variable itself: " + Left.FilterBox.Text);
        Environment.SetEnvironmentVariable("PathweftVerifyFolder", null); Environment.SetEnvironmentVariable("PathweftVerifyFile", null);
        // remove one history row: only that entry goes
        Left.FilterBox.Text = "";
        Left.FilterBox.Text = "soup"; // names match, not the folders above them
        var recent = Shown().First(s => s.Path == deep);
        Left.RemoveFromHistory(recent);
        check(!Settings.RecentFolders.Contains(deep) && Directory.Exists(deep) && Settings.RecentFolders.Contains(a), "BROWSE-02: removing a history row removes only that entry, never the folder");
        // the tab row switches to the other pane's tab
        Left.FilterBox.Text = "alpha";
        Left.ActivateSuggestion(Shown().First(s => s.Kind == PaneView.SuggestionKind.Tab));
        check(active == Right && Right.Dir == a, "BROWSE-02: a tab row switches to that pane and tab");
        SetActive(Left);
        // Settings > Privacy > Clear history
        ClearHistory();
        check(Settings.RecentFolders.Count == 0 && !File.ReadAllText(Settings.FilePath).Contains(unicode), "BROWSE-02: Clear history empties and persists the history");
        SavedSearches.Remove(saved);
        check(!SavedSearches.All.Contains(saved), "BROWSE-03: removing a saved search removes only that entry");
    }

    // BROWSE-04
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CommandLineToArgvW(string line, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr mem);
    static string[] Argv(string arguments)
    {
        var p = CommandLineToArgvW("x.exe " + arguments, out int n);
        try { return Enumerable.Range(1, n - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, i * IntPtr.Size))).ToArray(); }
        finally { LocalFree(p); }
    }
    async Task VerifyActions21(string root, Action<bool, string> check)
    {
        var dir = Directory.CreateDirectory(root).FullName;
        var paths = new List<string> { Path.Combine(dir, "plain.txt"), Path.Combine(dir, "with space & ^caret.txt"), Path.Combine(dir, "Ünï 子.jpg"), Path.Combine(dir, "ends with backslash") + "\\" };
        var argv = Argv(CustomAction.Expand("--first %f --all %F --name %n --dir %d 100%%", paths, dir));
        var expected = new List<string> { "--first", paths[0], "--all" }.Concat(paths).Concat(new[] { "--name", "plain.txt", "--dir", dir, "100%" }).ToArray();
        check(argv.SequenceEqual(expected), "BROWSE-04: placeholders expand to exact argument boundaries (spaces, metacharacters, Unicode, trailing backslash)");
        var jpg = new Entry { Name = "Ünï 子.jpg", Path = paths[2] };
        var txt = new Entry { Name = "plain.txt", Path = paths[0] };
        var folder = new Entry { Name = "sub", Path = Path.Combine(dir, "sub"), IsDir = true };
        CustomActions.Save(new List<CustomAction>
        {
            new() { Name = "Images only", Executable = "notepad.exe", Arguments = "%f", Patterns = "*.jpg;*.png", AppliesTo = "files", Selection = "one" },
            new() { Name = "Folders", Executable = "notepad.exe", AppliesTo = "folders", Submenu = "Group" },
            new() { Name = "Missing program", Executable = Path.Combine(dir, "nothing-here.exe"), Selection = "any" },
        });
        check(CustomActions.Menu(new[] { jpg }, dir, _ => { })?.Items.Count >= 1 && CustomActions.Menu(new[] { txt }, dir, _ => { }) is { } m && m.Items.OfType<MenuItem>().All(i => i.Header as string != "Images only"),
            "BROWSE-04: applicability by pattern and selection count decides where an action appears");
        check(CustomActions.Menu(new[] { folder }, dir, _ => { })?.Items.OfType<MenuItem>().Any(i => i.Header as string == "Group") == true, "BROWSE-04: an action's submenu groups it");
        check(CustomActions.All[2].Run(new[] { paths[0] }, dir) is { } error && error.Contains("could not start"), "BROWSE-04: a start failure is reported, nothing runs on its own");
        File.WriteAllText(CustomActions.FilePath, "{ not json");
        check(CustomActions.All.Count == 0 && CustomActions.LoadError != null, "BROWSE-04: a broken actions.json is reported and runs nothing");
        File.Delete(CustomActions.FilePath);
        await Task.CompletedTask;
    }

    // BROWSE-05
    async Task VerifyProfiles21(string root, Action<bool, string> check)
    {
        var parent = Directory.CreateDirectory(Path.Combine(root, "photos")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(parent, "2026")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
        await S1Verification.Navigate(Left, other);
        var defaultMode = Left.Mode;
        await S1Verification.Navigate(Left, parent);
        Left.SetView(PaneView.ViewMode.IconsL);
        Left.SaveProfile(true);
        await S1Verification.Navigate(Left, other);
        check(Left.Mode == defaultMode, "BROWSE-05: an unprofiled folder keeps the default view");
        await S1Verification.Navigate(Left, child);
        check(Left.Mode == PaneView.ViewMode.IconsL && Left.HasFolderProfile, "BROWSE-05: a profile saved with its subfolders applies below");
        FolderProfiles.Remove(parent);
        await S1Verification.Navigate(Left, other);
        await S1Verification.Navigate(Left, child);
        check(!Left.HasFolderProfile, "BROWSE-05: Reset returns the folder to the default view");
    }

    // BROWSE-06
    async Task VerifySync21(string root, Action<bool, string> check)
    {
        var l = Directory.CreateDirectory(Path.Combine(root, "left")).FullName;
        var r = Directory.CreateDirectory(Path.Combine(root, "right")).FullName;
        File.WriteAllText(Path.Combine(l, "only-left.txt"), "a");
        File.WriteAllText(Path.Combine(r, "only-right.txt"), "b");
        Directory.CreateDirectory(Path.Combine(l, "sub")); File.WriteAllText(Path.Combine(l, "sub", "deep.txt"), "c");
        Directory.CreateDirectory(Path.Combine(r, "sub"));
        var stamp = DateTime.UtcNow.AddHours(-1);
        File.WriteAllText(Path.Combine(l, "same-date.txt"), "one"); File.WriteAllText(Path.Combine(r, "same-date.txt"), "two");
        File.SetLastWriteTimeUtc(Path.Combine(l, "same-date.txt"), stamp); File.SetLastWriteTimeUtc(Path.Combine(r, "same-date.txt"), stamp);
        var shallow = FolderSync.Compare(l, r, false, false, null, CancellationToken.None);
        var deep = FolderSync.Compare(l, r, true, true, null, CancellationToken.None);
        check(!shallow.Any(x => x.Relative.Contains("deep.txt")) && deep.Any(x => x.Relative.EndsWith("deep.txt") && x.Chosen == SyncRow.Act.ToRight), "BROWSE-06: Compare subfolders finds what only exists deeper");
        check(deep.Any(x => x.Relative == "same-date.txt" && x.Chosen == SyncRow.Act.Skip), "BROWSE-06: a conflict (same date, different content) defaults to Skip");
        check(deep.Any(x => x.Relative == "only-right.txt" && x.Chosen == SyncRow.Act.ToLeft), "BROWSE-06: a file only on the right is copied to the left by default");
        await Task.CompletedTask;
    }

    // BROWSE-07 and BROWSE-10
    async Task VerifyNotes21(string root, Action<bool, string> check)
    {
        var dir = Directory.CreateDirectory(root).FullName;
        var red = Path.Combine(dir, "red.txt"); File.WriteAllText(red, "r");
        var plain = Path.Combine(dir, "plain.txt"); File.WriteAllText(plain, "p");
        var gone = Path.Combine(dir, "gone.txt"); File.WriteAllText(gone, "g");
        FileNotes.Set(red, new FileNote(new[] { "red", "work" }, 4, "a comment"));
        FileNotes.Set(gone, new FileNote(new[] { "old" }, 1, ""));
        var bytes = File.ReadAllBytes(red);
        check(bytes.Length == 1 && !File.Exists(red + ":Zone.Identifier") && Directory.GetFiles(dir).Length == 3, "BROWSE-07: nothing is written into the file or beside it");
        var moved = Path.Combine(dir, "renamed red.txt"); File.Move(red, moved);
        File.Delete(gone);
        FileNotes.Heal();
        check(FileNotes.Find(moved)?.Rating == 4 && FileNotes.Find(red) == null, "BROWSE-07: a note follows its file through a rename (NTFS file ID)");
        check(FileNotes.Stale().Contains(gone), "BROWSE-07: a record whose file is gone is listed as stale");
        check(FileNotes.Prune() == 1 && FileNotes.Find(gone) == null && FileNotes.Find(moved) != null, "BROWSE-07: Prune forgets only the missing records");
        await S1Verification.Navigate(Left, dir);
        Left.StartLiveFilter();
        Left.FilterBox.Text = "tag:work";
        var visible = Left.List.Items.Cast<Entry>().Where(e => !e.IsUp).Select(e => e.Name).ToList();
        check(visible.SequenceEqual(new[] { "renamed red.txt" }), "BROWSE-10/07: the live filter narrows the listing by tag");
        Left.FilterBox.Text = "plain";
        check(Left.List.Items.Cast<Entry>().Where(e => !e.IsUp).Select(e => e.Name).SequenceEqual(new[] { "plain.txt" }), "BROWSE-10: the live filter narrows by name");
        Left.StopLiveFilter();
        check(Left.List.Items.Cast<Entry>().Count(e => !e.IsUp) == 2 && !Left.LiveFiltering, "BROWSE-10: stopping the filter restores the full listing");
    }

    // BROWSE-11
    async Task VerifyLinks21(string root, Action<bool, string> check)
    {
        var dir = Directory.CreateDirectory(root).FullName;
        var target = Directory.CreateDirectory(Path.Combine(dir, "target folder")).FullName;
        File.WriteAllText(Path.Combine(target, "inside.txt"), "keep");
        var file = Path.Combine(dir, "file.txt"); File.WriteAllText(file, "f");
        var junction = Path.Combine(dir, "junction");
        Links.Create(Links.Kind.Junction, junction, target);
        check(Links.Describe(junction, File.GetAttributes(junction)).StartsWith("Junction") && Links.Target(junction)?.TrimEnd('\\') == target, "BROWSE-11: a junction is created and described with its target");
        var hard = Path.Combine(dir, "hard.txt");
        Links.Create(Links.Kind.Hard, hard, file);
        check(File.ReadAllText(hard) == "f", "BROWSE-11: a hard link to a file on the same volume is created");
        var why = Links.Unavailable(Links.Kind.Symbolic, dir, file);
        if (why == null)
        {
            var sym = Path.Combine(dir, "symbolic.txt"); Links.Create(Links.Kind.Symbolic, sym, "file.txt");
            check(Links.Describe(sym, File.GetAttributes(sym)).StartsWith("Symbolic link"), "BROWSE-11: a relative symbolic link is created and described");
            File.Delete(file);
            check(Links.Describe(sym, File.GetAttributes(sym)).EndsWith("(broken)"), "BROWSE-11: a broken link is marked");
            File.Delete(sym);
        }
        else check(why.Contains("Developer Mode") || why.Contains("administrator"), "BROWSE-11: symbolic links say why they are unavailable");
        check(Links.Unavailable(Links.Kind.Junction, dir, file) != null, "BROWSE-11: a junction to a file is refused with the reason");
        Directory.Delete(junction); // removing the link
        check(File.Exists(Path.Combine(target, "inside.txt")), "BROWSE-11: removing a junction never touches its target");
        await Task.CompletedTask;
    }

    // BROWSE-09: local rclone endpoints over a disposable folder under root
    async Task VerifyRemote21(string root, Action<bool, string> check)
    {
        var served = Directory.CreateDirectory(Path.Combine(root, "served")).FullName;
        File.WriteAllText(Path.Combine(served, "hello.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(served, "folder"));
        var local = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
        File.WriteAllText(Path.Combine(local, "upload me.txt"), "up");
        string rclone = Environment.GetEnvironmentVariable("PATHWEFT_RCLONE") ?? throw new IOException("PATHWEFT_RCLONE is not set");
        static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
        var servers = new List<Process>();
        Process Serve(string kind, int port)
        {
            var start = new ProcessStartInfo(rclone) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "serve", kind, served, "--addr", $"127.0.0.1:{port}", "--user", "tester", "--pass", "disposable", "--config", Path.Combine(root, "rclone.conf"), "--cache-dir", Path.Combine(root, "rclone-cache") })
                start.ArgumentList.Add(arg);
            var p = Process.Start(start)!; p.BeginErrorReadLine(); p.BeginOutputReadLine(); servers.Add(p); return p;
        }
        try
        {
            int davPort = FreePort(), sftpPort = FreePort();
            Serve("webdav", davPort); Serve("sftp", sftpPort);
            static async Task<bool> Listening(int port) => await Until(() => { try { using var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); return true; } catch (SocketException) { return false; } }, 15000);
            check(await Listening(davPort) && await Listening(sftpPort), "BROWSE-09: the local rclone WebDAV and SFTP endpoints are up");
            foreach (var (kind, port) in new[] { ("webdav", davPort), ("sftp", sftpPort) })
            {
                var c = new RemoteConnection { Name = "test " + kind, Kind = kind, Host = "127.0.0.1", Port = port, User = "tester", Root = "/" };
                // the protocol client: host key asked once, then remembered; a wrong password is refused
                bool asked = false;
                using (var fs = RemoteFileSystems.Create(c, "disposable", (_, fp) => { asked = fp.StartsWith("SHA256:"); return Task.FromResult(true); }))
                {
                    await fs.Connect(CancellationToken.None);
                    var listed = await fs.List("/", CancellationToken.None);
                    check(listed.Any(i => i.Name == "hello.txt" && !i.IsDir && i.Size == 5) && listed.Any(i => i.Name == "folder" && i.IsDir), $"BROWSE-09 {kind}: listing with names, kinds and sizes");
                    if (kind == "sftp") check(asked && KnownHosts.Find("127.0.0.1", port) != null, "BROWSE-09 sftp: the first connection shows the host key fingerprint and remembers it");
                    await fs.Upload(Path.Combine(local, "upload me.txt"), "/folder/upload me.txt", false, null, CancellationToken.None);
                    bool refused = false;
                    try { await fs.Upload(Path.Combine(local, "upload me.txt"), "/folder/upload me.txt", false, null, CancellationToken.None); } catch (IOException) { refused = true; }
                    check(refused && File.ReadAllText(Path.Combine(served, "folder", "upload me.txt")) == "up", $"BROWSE-09 {kind}: upload, and an existing name is refused without replace");
                    await fs.Move("/folder/upload me.txt", "/folder/renamed.txt", CancellationToken.None);
                    var down = Path.Combine(local, kind + "-down.txt");
                    await fs.Download("/folder/renamed.txt", down, null, CancellationToken.None);
                    await fs.Delete("/folder/renamed.txt", false, false, CancellationToken.None);
                    check(File.ReadAllText(down) == "up" && !File.Exists(Path.Combine(served, "folder", "renamed.txt")), $"BROWSE-09 {kind}: rename, download and delete");
                    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                    bool stopped = false;
                    try { await fs.List("/", cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
                    check(stopped, $"BROWSE-09 {kind}: a cancelled operation stops");
                }
                using (var wrong = RemoteFileSystems.Create(c, "wrong password", (_, _) => Task.FromResult(true)))
                {
                    bool denied = false;
                    try { await wrong.Connect(CancellationToken.None); await wrong.List("/", CancellationToken.None); } catch (Exception e) when (e is UnauthorizedAccessException or IOException or Renci.SshNet.Common.SshAuthenticationException) { denied = true; }
                    check(denied, $"BROWSE-09 {kind}: a wrong password is refused");
                }
                // the pane: a saved connection browses under its address; the password stays in memory only
                RemoteConnections.Upsert(c); RemoteSessions.Remember(c.Id, "disposable");
                string address = c.Address("/");
                await S1Verification.Navigate(Left, address);
                check(await Until(() => Left.Items.Any(e => e.Name == "hello.txt" && e.Path == c.Address("/hello.txt"))) && Left.IsRemoteFolder, $"BROWSE-09 {kind}: the pane lists the server under its address, not a local path");
                // a remote -> local copy through the transfer path
                Left.SetSelection(e => e.Name == "hello.txt");
                RemoteTransfer(new List<string> { c.Address("/hello.txt") }, local, false);
                check(await Until(() => File.Exists(Path.Combine(local, "hello.txt")) && File.ReadAllText(Path.Combine(local, "hello.txt")) == "hello", 20000), $"BROWSE-09 {kind}: copy from the server through the transfer window");
                File.Delete(Path.Combine(local, "hello.txt"));
                await S1Verification.Navigate(Left, local);
                check(!Left.IsRemoteFolder, $"BROWSE-09 {kind}: leaving the server closes the pane's remote session");
                RemoteSessions.Forget(c.Id); RemoteConnections.Remove(c.Id); KnownHosts.Remove("127.0.0.1", port);
            }
        }
        finally
        {
            foreach (var p in servers) { try { if (!p.HasExited) { p.Kill(true); p.WaitForExit(5000); } } catch (InvalidOperationException) { } p.Dispose(); }
        }
    }

    // BROWSE-08
    async Task VerifyTerminal21(string root, Action<bool, string> check)
    {
        var first = Directory.CreateDirectory(Path.Combine(root, "first 'quoted' folder")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "second Ünï")).FullName;
        await S1Verification.Navigate(Left, first);
        Left.ToggleTerminal();
        var panel = Left.TerminalForVerification;
        check(await Until(() => panel?.Running == true && panel.Synchronized, 30000), "BROWSE-08: the pane's terminal starts a shell with shell integration (OSC 9;9 / 133)");
        check(await Until(() => panel.FolderForVerification?.TrimEnd('\\').Equals(first, StringComparison.OrdinalIgnoreCase) == true, 15000)
            && Left.Dir.TrimEnd('\\').Equals(first, StringComparison.OrdinalIgnoreCase), "BROWSE-08: the terminal starts in the pane's folder (quotes included) and the pane stays there");
        check(panel.SendForVerification($"Set-Location -LiteralPath '{TerminalPanel.Quote(second)}'") && await Until(() => Left.Dir.TrimEnd('\\').Equals(second, StringComparison.OrdinalIgnoreCase), 15000),
            "BROWSE-08: a directory change in the shell navigates the pane");
        await S1Verification.Navigate(Left, PaneView.ThisPC);
        check(await Until(() => panel.UnsupportedForVerification == "not a local folder"), "BROWSE-08: a non-local place says the terminal does not follow it");
        Left.ToggleTerminal();
        check(!Left.TerminalShown && panel.Running, "BROWSE-08: hiding keeps the session");
        Left.NewTab(); Left.CloseTab();
        check(panel.Running, "BROWSE-08: tab switches keep the session");
        Left.CloseTerminalForVerification();
        check(!panel.Running, "BROWSE-08: closing ends the shell");
    }

    // MONITOR-25
    async Task VerifyMonitor25(string root, Action<bool, string> check)
    {
        await S1Verification.Navigate(Left, root);
        PerformanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => AnalysisVisible, 5000);
        performance.Select(PerformancePanel.Cpu); UpdateLayout(); // the analysis view holds the filter and Pause
        await Task.Delay(500);
        var nodes = Tree(this).ToList();
        var filter = nodes.OfType<TextBox>().FirstOrDefault(t => AutomationProperties.GetName(t) == "Filter processes in the graph and table");
        var pause = nodes.OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == "Pause live analysis");
        check(filter != null && filter.IsVisible, "MONITOR-25: the process filter for graph and table is shown");
        check(pause != null, "MONITOR-25: the analysis has a Pause button");
        if (pause != null)
        {
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(AutomationProperties.GetName(pause) == "Resume live analysis", "MONITOR-25: Pause freezes the analysis and offers Resume");
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        PerformanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
#endif
