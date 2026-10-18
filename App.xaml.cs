using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace FileExplorer;

public partial class App : Application
{
    // `FileLabs.exe --selftest`: runs the Ferry engine on real files in a temp dir, exit code 0 = pass.
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest")) { Shutdown(SelfTest()); return; }

        // Window-less Win+E catcher (see Agent.cs); never opens the main window.
        if (e.Args.Contains("--agent"))
        {
            int code = Agent.Run(this);
            if (code >= 0) Shutdown(code);
            return;
        }

        // Called by the installer / uninstaller: same code path as the switch in Settings.
        if (e.Args.Contains("--set-default") || e.Args.Contains("--unset-default"))
        {
            Settings.Load(); // "Win+E opens File Labs" may be off
            try { Settings.SetDefaultFileManager(e.Args.Contains("--set-default")); Shutdown(0); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Shutdown(1); }
            return;
        }

        // Single instance: a second launch (folder double-click, Win+E) hands its path to the
        // running window, which opens it as a new tab, then exits.
        instance = new Mutex(true, PipeName, out bool first);
        if (!first)
        {
            AllowSetForegroundWindow(-1); // we were just launched by the user, so we may pass focus on
            if (HandOff(e.Args.FirstOrDefault(a => !a.StartsWith("--")) ?? "")) { Shutdown(); return; }
            // first instance hung or closing: just start normally
        }
        else _ = Listen();
        Settings.Load(); // before any window: the panes read column widths while being built
        ShellMenu.FollowSystemTheme(); // the Windows right-click menu dark in dark mode
        Task.Run(Sisters.WriteCard); // the ecosystem card, so sister apps find this copy (Sisters.cs)
        SizeCache.StartLoading(); // folder sizes from previous sessions: a full 12 TB drive is minutes of walking
        Exit += (_, _) => SizeCache.Save();
        var sizeSaver = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        sizeSaver.Tick += (_, _) => Task.Run(SizeCache.SaveIfChanged); // off the UI thread: up to 50 000 lines
        sizeSaver.Start();
        base.OnStartup(e);
        new MainWindow().Show(); // opened here, not via StartupUri, so --agent etc. never build it
    }

    static readonly string PipeName = "FileLabs-" + Environment.UserName + (Settings.Profile != null ? "-profile" : "");

    /// True while a File Labs window is open for this user (it holds the single-instance mutex).
    internal static bool Running
    {
        get
        {
            if (!Mutex.TryOpenExisting(PipeName, out var m)) return false;
            m.Dispose();
            return true;
        }
    }

    /// Passes a folder to the running window ("" = just come to the front). False if it didn't answer.
    internal static bool HandOff(string path)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.WriteLine(path);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { return false; }
    }
    Mutex instance;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

    async Task Listen()
    {
        while (true)
        {
            using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            string path;
            try { path = await new StreamReader(server).ReadLineAsync(); }
            catch (IOException) { continue; } // client went away
            if (MainWindow is MainWindow w) w.OpenFromOutside(path);
        }
    }

    static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "filelabs-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(root, "src", "tree", "sub")).Parent.Parent.FullName;
            var dst = Directory.CreateDirectory(Path.Combine(root, "dst")).FullName;
            File.WriteAllText(Path.Combine(src, "tree", "a.txt"), "hello");
            File.WriteAllBytes(Path.Combine(src, "tree", "sub", "b.bin"), new byte[3_000_000]);

            // copy a folder tree
            var j = Run(Ferry.Submit(new[] { Path.Combine(src, "tree") }, dst, move: false));
            Check(j.Phase == Phase.Done && j.DoneFiles == 2 && j.Errors.IsEmpty, "copy tree");
            Check(File.ReadAllText(Path.Combine(dst, "tree", "a.txt")) == "hello", "copied content");
            Check(new FileInfo(Path.Combine(dst, "tree", "sub", "b.bin")).Length == 3_000_000, "copied size");

            // same copy again → conflicts; "keep both" renames
            j = Ferry.Submit(new[] { Path.Combine(src, "tree") }, dst, move: false);
            Wait(() => j.Phase == Phase.Conflicts);
            Check(j.Conflicts.Count == 2, "two conflicts found in one pass");
            j.Resolve(Choice.Rename);
            Run(j);
            Check(File.Exists(Path.Combine(dst, "tree", "a (2).txt")), "keep both → a (2).txt");

            // move within the same volume is a rename; source is gone afterwards
            var mv = Directory.CreateDirectory(Path.Combine(root, "moved")).FullName;
            j = Run(Ferry.Submit(new[] { Path.Combine(src, "tree") }, mv, move: true));
            Check(j.Phase == Phase.Done && !Directory.Exists(Path.Combine(src, "tree")) && File.Exists(Path.Combine(mv, "tree", "a.txt")), "same-volume move");
            Check(j.Notes.Contains("Moved within the same drive"), "move took the rename path");

            // move a folder onto a folder of the same name: merged file by file, never renamed over it,
            // and not undoable (undo would take the files that were already there with it)
            var merge = Directory.CreateDirectory(Path.Combine(root, "merge")).FullName;
            Directory.CreateDirectory(Path.Combine(merge, "tree"));
            File.WriteAllText(Path.Combine(merge, "tree", "kept.txt"), "was here");
            var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming", "tree")).FullName;
            File.WriteAllText(Path.Combine(incoming, "new.txt"), "arrived");
            // a junction inside the moved folder, pointing at a folder with an empty subfolder
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside", "empty")).Parent.FullName;
            var link = Path.Combine(incoming, "link");
            bool junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"")
                { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit(5000) && Directory.Exists(link);
            j = Run(Ferry.Submit(new[] { incoming }, merge, move: true));
            Check(j.Phase == Phase.Done && j.Merged && j.Errors.IsEmpty, "merge move finished");
            Check(File.Exists(Path.Combine(merge, "tree", "kept.txt")) && File.Exists(Path.Combine(merge, "tree", "new.txt")), "merge keeps both folders' files");
            if (junction)
            {
                Check(Directory.Exists(Path.Combine(outside, "empty")), "a junction's target is never emptied");
                Check(Directory.Exists(link), "the junction itself stays where it was");
                Directory.Delete(link); // the link only, so the clean-up below has plain folders to remove
            }

            // folder sizes: every folder a scan passes through is remembered, and a change drops the
            // totals above it
            var sizes = Path.Combine(root, "sizes");
            var deep = Directory.CreateDirectory(Path.Combine(sizes, "a", "b")).FullName;
            File.WriteAllBytes(Path.Combine(deep, "x.bin"), new byte[1000]);
            // what a listing compares against: the folder's entry in its parent
            DateTime Stamp(string p) => new DirectoryInfo(Path.GetDirectoryName(p)).EnumerateDirectories(Path.GetFileName(p)).First().LastWriteTimeUtc;
            Check(PaneView.Measure(new DirectoryInfo(sizes), default) == 1000, "folder size");
            Check(SizeCache.Get(deep, Stamp(deep)) == 1000, "subfolders remembered");
            Check(SizeCache.Get(deep, DateTime.UtcNow) == -1, "a changed folder is forgotten");
            var mid = Path.Combine(sizes, "a");
            Check(SizeCache.Get(mid, Stamp(mid)) == -1, "and so is every total above it");
            File.WriteAllBytes(Path.Combine(deep, "y.bin"), new byte[500]);
            Check(PaneView.Measure(new DirectoryInfo(sizes), default) == 1500, "measured again after the change");

            // names sort the way Explorer sorts them: symbols first, numbers by value
            var order = string.Join(",", new[] { "b", "file10", "_drafts", "file2", "A", "(old)" }.OrderBy(x => x, ExplorerOrder.Instance));
            Check(order == "(old),_drafts,A,b,file2,file10", "Explorer name order, got " + order);

            // copy with verification on
            Settings.Verify = true;
            var vdst = Directory.CreateDirectory(Path.Combine(root, "verified")).FullName;
            j = Run(Ferry.Submit(new[] { Path.Combine(mv, "tree") }, vdst, move: false));
            Settings.Verify = false;
            Check(j.Phase == Phase.Done && j.Errors.IsEmpty && j.DoneBytes == j.TotalBytes && j.TotalBytes == 2 * (5 + 3_000_000), "verify pass reads both copies");

            // cancel before it starts
            j = Ferry.Submit(new[] { Path.Combine(mv, "tree") }, dst, move: false);
            j.Cancel();
            Run(j);
            Check(j.Phase == Phase.Cancelled, "cancel");

            // Recycle Bin record ($I file, Windows 10+ layout): version 2, size, FILETIME, length, path
            var when = new DateTime(2026, 9, 21, 7, 30, 0, DateTimeKind.Local);
            var original = @"C:\Users\someone\Documents\report final.docx";
            var info = Path.Combine(root, "$IABC123.docx");
            File.WriteAllBytes(info, BitConverter.GetBytes(2L).Concat(BitConverter.GetBytes(12345L)).Concat(BitConverter.GetBytes(when.ToFileTime()))
                .Concat(BitConverter.GetBytes(original.Length + 1)).Concat(System.Text.Encoding.Unicode.GetBytes(original + "\0")).ToArray());
            Check(RecycleBinWindow.ParseInfo(info) is { } rec && rec.Path == original && rec.Size == 12345 && rec.Deleted == when, "recycle bin record");

            // a sister app is found through its uninstall entry, which names the exe and an icon index
            var self = Environment.ProcessPath;
            Check(FileExplorer.MainWindow.ExeOf(self + ",0") == self && FileExplorer.MainWindow.ExeOf($"\"{self}\",0") == self
                && FileExplorer.MainWindow.ExeOf(Path.Combine(root, "gone.exe") + ",0") == null && FileExplorer.MainWindow.ExeOf(null) == null, "installed app from its uninstall entry");

            // ecosystem cards: what this app writes reads back; garbage, another schema or a gone exe count as not installed
            var card = Path.Combine(root, "card.json");
            Sisters.Write(card, new { schema = 1, updated = "2026-10-15T09:00:00Z", exe = self });
            Check(File.ReadAllBytes(card)[0] == '{' && Sisters.ExeFromCard(card) == self, "card written without BOM and read back");
            Sisters.Write(card, new { schema = 1, exe = Path.Combine(root, "gone.exe") });
            Check(Sisters.ExeFromCard(card) == null, "card of an uninstalled app");
            Sisters.Write(card, new { schema = 2, exe = self });
            Check(Sisters.ExeFromCard(card) == null, "card with another schema");
            File.WriteAllText(card, "{ not json");
            Check(Sisters.ExeFromCard(card) == null && Sisters.ExeFromCard(Path.Combine(root, "none.json")) == null, "broken or missing card");

            // QuickLook is handed what a shortcut points at; anything else, or a shortcut that can't be read, as it is
            var lnk = Path.Combine(root, "record.lnk");
            FileExplorer.MainWindow.SaveShortcut(lnk, info, root);
            Check(FileExplorer.MainWindow.ShortcutTarget(lnk) == info && FileExplorer.MainWindow.ShortcutTarget(info) == info
                && FileExplorer.MainWindow.ShortcutTarget(card) == card && FileExplorer.MainWindow.ShortcutTarget(Path.Combine(root, "gone.lnk")) == Path.Combine(root, "gone.lnk"), "shortcut target");
            // a shortcut to a folder is where a middle-click opens and a drop lands; one to a file is not
            var folderLnk = Path.Combine(root, "dst.lnk");
            FileExplorer.MainWindow.SaveShortcut(folderLnk, dst, dst);
            Check(FileExplorer.MainWindow.ShortcutFolder(folderLnk) == dst && FileExplorer.MainWindow.ShortcutFolder(lnk) == null
                && FileExplorer.MainWindow.ShortcutFolder(dst) == null && new Entry { Name = "dst.lnk", Path = folderLnk }.FolderTarget == dst, "shortcut to a folder");

            // a silent permanent delete (Shift+Delete with the sound off): read-only files go too, and a
            // junction inside is removed without emptying the folder it points at
            var doomed = Directory.CreateDirectory(Path.Combine(root, "doomed", "sub")).Parent.FullName;
            var ro = Path.Combine(doomed, "sub", "locked.txt");
            File.WriteAllText(ro, "read-only");
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            var kept = Directory.CreateDirectory(Path.Combine(root, "kept")).FullName;
            File.WriteAllText(Path.Combine(kept, "stays.txt"), "here");
            bool doomedLink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(doomed, "link")}\" \"{kept}\"")
                { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit(5000) && Directory.Exists(Path.Combine(doomed, "link"));
            FileExplorer.MainWindow.DeleteQuietly(doomed);
            Check(!Directory.Exists(doomed), "silent delete removes the folder, read-only file included");
            Check(!doomedLink || File.Exists(Path.Combine(kept, "stays.txt")), "silent delete never empties a junction's target");

            // a failed update check is a failure, never "nothing newer": offline, 403 (rate limit), 5xx,
            // an unreadable answer all throw; a readable answer with nothing newer returns null
            Check(UpdateCheckFails("http://127.0.0.1:9/"), "update check offline fails");
            Check(UpdateCheckFails(Serve(403, "{\"message\":\"API rate limit exceeded\"}")), "update check 403 fails");
            Check(UpdateCheckFails(Serve(502, "")), "update check 5xx fails");
            Check(UpdateCheckFails(Serve(200, "<html>")), "update check unreadable answer fails");
            Updater.Feed = Serve(200, "{\"tag_name\":\"v0.0.1\",\"body\":\"\",\"assets\":[{\"name\":\"FileLabs-0.0.1-setup.exe\",\"browser_download_url\":\"x\",\"size\":1}]}");
            Check(Task.Run(() => Updater.Check(manual: true)).GetAwaiter().GetResult() == null, "update check: nothing newer is not a failure");

            // a copy into the folder it is in duplicates beside it, as in Explorer; folders keep their dots
            var dup = Directory.CreateDirectory(Path.Combine(root, "dup", "v1.2", "inner")).Parent.Parent.FullName;
            File.WriteAllText(Path.Combine(dup, "v1.2", "inner", "x.txt"), "x");
            File.WriteAllText(Path.Combine(dup, "notes.txt"), "n");
            Run(Ferry.Submit(new[] { Path.Combine(dup, "notes.txt"), Path.Combine(dup, "v1.2") }, dup, move: false));
            j = Run(Ferry.Submit(new[] { Path.Combine(dup, "notes.txt") }, dup, move: false));
            Check(File.ReadAllText(Path.Combine(dup, "notes - Copy.txt")) == "n" && File.Exists(Path.Combine(dup, "notes - Copy (2).txt"))
                && File.Exists(Path.Combine(dup, "v1.2 - Copy", "inner", "x.txt")) && File.ReadAllText(Path.Combine(dup, "notes.txt")) == "n"
                && j.Conflicts.Count == 0 && j.Duplicates[Path.Combine(dup, "notes.txt")] == Path.Combine(dup, "notes - Copy (2).txt"), "duplicate in the same folder");

            // selected text: white on the default accent at 8:1 or better (1.0.38's 6.97:1 still read as grey), and
            // readable on other accents and strengths
            var (fill, text) = FileExplorer.MainWindow.Selection(System.Windows.Media.Color.FromRgb(0x4C, 0x8D, 0xFF), 1.0);
            Check(text == System.Windows.Media.Colors.White && FileExplorer.MainWindow.Contrast(text, fill) >= 8, "selection text on the default accent");
            foreach (var hex in new[] { "#FFD23F", "#FF4FA3", "#E5484D", "#30D158", "#FFFFFF" })
                foreach (var strength in new[] { 0.3, 1.0, 2.5 })
                {
                    var (f, t) = FileExplorer.MainWindow.Selection((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex), strength);
                    Check(FileExplorer.MainWindow.Contrast(t, f) >= 4.5, $"selection text on {hex} at {strength}");
                }

            Console.WriteLine("selftest: all passed");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "filelabs-selftest.txt"), ex.ToString());
            return 1;
        }
        finally { try { Directory.Delete(root, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    static bool UpdateCheckFails(string feed)
    {
        Updater.Feed = feed;
        try { Task.Run(() => Updater.Check(manual: true)).GetAwaiter().GetResult(); return false; }
        catch (Exception ex) when (Updater.Failed(ex)) { return true; }
    }

    // A one-request local server answering with this status and body; returns its address.
    static string Serve(int status, string body)
    {
        var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        port.Start(); int p = ((System.Net.IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{p}/");
        listener.Start();
        _ = Task.Run(() =>
        {
            var c = listener.GetContext();
            c.Response.StatusCode = status;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            c.Response.ContentLength64 = bytes.Length;
            c.Response.OutputStream.Write(bytes);
            c.Response.Close(); // the listener is left to the process: stopping it here can cut the answer off
        });
        return $"http://localhost:{p}/";
    }

    static Job Run(Job j) { Wait(() => !j.Active); return j; }
    static void Wait(Func<bool> done) { for (int i = 0; i < 300 && !done(); i++) Thread.Sleep(50); if (!done()) throw new TimeoutException("job did not finish"); }
    static void Check(bool ok, string what) { if (!ok) throw new Exception("FAILED: " + what); }
}
