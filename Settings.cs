using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FileExplorer;

// key=value lines in %AppData%\Pathweft\settings.txt, or an explicit isolated profile.
public static class Settings
{
    // PATHWEFT_PROFILE=<folder>: a copy for testing beside the installed one, with its own settings and
    // its own single instance, writing nothing the installed copy or the sister apps read
    public static readonly string Profile = Environment.GetEnvironmentVariable("PATHWEFT_PROFILE") is { Length: > 0 } p ? p : null;
    // Decided at the first call, not when the class loads: an explicit profile wins, and
    // .NET may load this class before that line runs. Every path below the data folder is derived at call time too.
    public static string Dir => dir ??= Profile ?? Portable ?? DataFolder.Current;
    /// A portable copy keeps everything in Data beside the exe, so the folder can travel on a stick (Rico, 1.0.61).
    public static readonly string Portable = Program.PortableDir is { } d ? Path.Combine(d, "Data") : null;
    static string dir;
    internal static string FilePath => Path.Combine(Dir, "settings.txt");

    public static int Backdrop = 3;          // 1 solid, 2 mica, 3 acrylic
    public static double Opacity = 70;       // 30–100, floor keeps text readable
    public static string Accent = "#4C8DFF"; // Protagonist Labs blue by default
    public static double RowPad = 3;         // row height: 1 compact, 3 normal, 6 roomy
    public static bool Animations = true;
    public static bool SmoothScroll;         // the wheel glides the file list instead of jumping
    public static double TextScale = 1;      // the whole window, text included: 1, 1.15, 1.3, 1.5
    public static bool SinglePane;           // the right pane is hidden
    public static bool Preview;              // the preview pane right of the panes (Alt+P)
    public static double PreviewWidth = 380;
    public static int Stats = 2;             // the cards below the panes: 2 full, 1 compact (one line each), 0 hidden
    public static double SidebarScale = 1, TabsScale = 1, PathScale = 1, ListScale = 1; // per area, on top of TextScale: 0.8–1.5
    public static HashSet<string> DisabledPreviewModules = new(new[] { "f3d" }, StringComparer.Ordinal);
    public static bool TaskbarSearch;        // explicit opt-in; lifetime belongs to the running application
    public static bool TaskbarSearchBox = true;
    public static bool ItemTips = true;      // hovering a file shows its full name, type, size and date
    public static bool DeleteSound = true;   // Shift+Delete's warning and Windows' delete sound; off = silent
    public static string BackgroundImage = ""; // a picture behind the panes, "" = none
    public static double ImageOpacity = 40;  // how much of it shows, 5–100
    public static double ImageBlur;          // blur radius in px, 0–40
    public static double ImageBrightness;    // −100 (black) … 0 … +100 (white)
    public static double AccentStrength = 1.0; // how opaque the accent tints are, 1 = as designed
    public static bool InternalEditor = true; // Enter on a code/text file opens the F4 editor, not its app
    public static bool Verify;               // xxHash3 check after copying
    public static bool ParallelCopy = true;  // small files several at once on SSDs and network shares (Ferry.Fast)
    public static bool ShowHidden;
    public static bool ShortcutsWithFolders;     // shortcuts to folders sort among the folders, not the files
    public static string LastSeenVersion = "";   // What's new is shown once per version
    public static bool NativeMenu;          // right-click shows the Windows menu instead of ours
    public static bool TakeWinE = true;         // with Windows integration on, Win+E opens Pathweft; off = File Explorer
    public static bool CloseToTray;             // closing hides the window to the notification area; the next open is instant
    public static bool CloseEmptyPane;          // closing the second pane's last tab hides that pane (one pane), as Ctrl+P does
    public static int TipDelay = 1500;          // ms before a tooltip shows; WPF's own wait was 1000 (1.0.51: "a little slower")
    public static bool Autoscroll;              // a middle click on the list's empty space scrolls it by moving the mouse, as in a browser
    public static bool AutoscrollSideways = true; // ... sideways too; off: up and down only
    public static int QuickList;                // which Quick access list the sidebar shows (MainWindow.QuickLists)

    // Started at sign-in, hidden in the notification area (needs CloseToTray). The Run value is the truth; a test
    // copy (PATHWEFT_PROFILE) never writes it.
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunValue = "Pathweft";
    public static bool StartWithWindows
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(RunValue) is string; }
        set
        {
            if (Profile != null) return;
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
    public static List<string> RecentFolders = new();      // newest first, for the history menu
    public static List<string> RecentDestinations = new(); // where copies and moves went, newest first, for Copy to / Move to
    public static double SidebarWidth = 270;
    public static List<string> SidebarOrder = new() { "quick", "favorites", "drives" };
    public static readonly HashSet<string> SidebarCollapsed = new();
    public static double Split = 0.5;                                  // left pane share
    public static Rect? WindowBounds;                                  // restore bounds
    public static bool Maximized;
    public static readonly Dictionary<string, double> ColumnWidths = new(); // "Size" → 80
    public static readonly Dictionary<string, PaneView.ViewMode> Views = new();
    public static readonly Dictionary<string, List<string>> Tabs = new();   // "Left" → folders open in that pane
    public static readonly Dictionary<string, int> ActiveTab = new(); // "Left" → Details
    public static readonly Dictionary<string, List<int>> Pins = new(); // "Left" → indexes of pinned tabs
    public static readonly Dictionary<string, (string Key, bool Desc)> FolderSorts = new(StringComparer.OrdinalIgnoreCase); // folder → its own order; absent = by name
    public static readonly HashSet<string> DateGroupFolders = new(StringComparer.OrdinalIgnoreCase); // folders shown grouped by date (Details)

    // Every setting as it comes, in the file's own form, taken before Load from the initial values above (it must stay
    // the last field: fields are set in the order they're written). Reset reads them back through the same parser.
    static readonly string[] Defaults = Lines().ToArray();

    public const int RecentMax = 25, DestinationsMax = 8;

    /// Puts `path` first in a most-recent-first list, once, and cuts the list to `max`.
    public static void Bump(List<string> list, string path, int max)
    {
        list.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }

    public static void Load()
    {
        if (File.Exists(FilePath)) Apply(File.ReadAllLines(FilePath));
    }

    /// Settings → Reset to defaults: these keys back to how Pathweft comes. Not saved here.
    public static void Reset(IEnumerable<string> keys)
    {
        var set = keys.ToHashSet();
        Apply(Defaults.Where(l => set.Contains(l.Split('=', 2)[0])));
    }

    /// The keys a fresh copy writes (the selftest checks that every setting can be reset).
    internal static IEnumerable<string> DefaultKeys => Defaults.Where(l => l != "").Select(l => l.Split('=', 2)[0]);

    static void Apply(IEnumerable<string> lines)
    {
        foreach (var kv in lines.Select(l => l.Split('=', 2)).Where(a => a.Length == 2))
        {
            var v = kv[1].Trim();
            switch (kv[0].Trim())
            {
                case "backdrop" when int.TryParse(v, out var b) && b is >= 1 and <= 3: Backdrop = b; break;
                case "opacity" when double.TryParse(v, CultureInfo.InvariantCulture, out var o) && double.IsFinite(o): Opacity = Math.Clamp(o, 30, 100); break;
                case "accent" when System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$"): Accent = v; break;
                case "rowpad" when double.TryParse(v, CultureInfo.InvariantCulture, out var rp) && double.IsFinite(rp): RowPad = Math.Clamp(rp, 0, 10); break;
                case "animations": Animations = v == "1"; break;
                case "smoothscroll": SmoothScroll = v == "1"; break;
                case "textscale" when double.TryParse(v, CultureInfo.InvariantCulture, out var ts) && double.IsFinite(ts): TextScale = Math.Clamp(ts, 1, 1.6); break;
                case "singlepane": SinglePane = v == "1"; break;
                case "preview": Preview = v == "1"; break;
                case "previewwidth" when double.TryParse(v, CultureInfo.InvariantCulture, out var pw) && double.IsFinite(pw): PreviewWidth = Math.Clamp(pw, 200, 1600); break;
                case "stats" when int.TryParse(v, out var st) && st is >= 0 and <= 2: Stats = st; break;
                case "scale.sidebar" when double.TryParse(v, CultureInfo.InvariantCulture, out var s1) && double.IsFinite(s1): SidebarScale = Math.Clamp(s1, 0.8, 1.5); break;
                case "scale.tabs" when double.TryParse(v, CultureInfo.InvariantCulture, out var s2) && double.IsFinite(s2): TabsScale = Math.Clamp(s2, 0.8, 1.5); break;
                case "scale.path" when double.TryParse(v, CultureInfo.InvariantCulture, out var s3) && double.IsFinite(s3): PathScale = Math.Clamp(s3, 0.8, 1.5); break;
                case "scale.list" when double.TryParse(v, CultureInfo.InvariantCulture, out var s4) && double.IsFinite(s4): ListScale = Math.Clamp(s4, 0.8, 1.5); break;
                case "preview.modules.disabled": DisabledPreviewModules = new(v.Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal); break;
                case "taskbarsearch": TaskbarSearch = v == "1"; break;
                case "taskbarsearchbox": TaskbarSearchBox = v == "1"; break;
                case "itemtips": ItemTips = v == "1"; break;
                case "deletesound": DeleteSound = v == "1"; break;
                case "bgimage": BackgroundImage = v; break;
                case "bgblur" when double.TryParse(v, CultureInfo.InvariantCulture, out var bb) && double.IsFinite(bb): ImageBlur = Math.Clamp(bb, 0, 40); break;
                case "bgbright" when double.TryParse(v, CultureInfo.InvariantCulture, out var bl) && double.IsFinite(bl): ImageBrightness = Math.Clamp(bl, -100, 100); break;
                case "bgopacity" when double.TryParse(v, CultureInfo.InvariantCulture, out var bo) && double.IsFinite(bo): ImageOpacity = Math.Clamp(bo, 5, 100); break;
                case "accentstrength" when double.TryParse(v, CultureInfo.InvariantCulture, out var ast) && double.IsFinite(ast): AccentStrength = Math.Clamp(ast, 0.3, 2.5); break;
                case "verify": Verify = v == "1"; break;
                case "parallel": ParallelCopy = v != "0"; break;
                case "internaleditor": InternalEditor = v == "1"; break;
                case "hidden": ShowHidden = v == "1"; break;
                case "shortcutfolders": ShortcutsWithFolders = v == "1"; break;
                case "nativemenu": NativeMenu = v == "1"; break;
                case "wine": TakeWinE = v != "0"; break;
                case "lastseen": LastSeenVersion = v; break;
                case "tray": CloseToTray = v == "1"; break;
                case "closeemptypane": CloseEmptyPane = v == "1"; break;
                case "tipdelay" when int.TryParse(v, out var td): TipDelay = Math.Clamp(td, 0, 3000); break;
                case "autoscroll": Autoscroll = v == "1"; break;
                case "autoscrollx": AutoscrollSideways = v != "0"; break;
                case "quicklist" when int.TryParse(v, out var ql) && ql >= 0: QuickList = ql; break;
                // paths can't hold |, so it separates them
                case "recent": RecentFolders = v.Split('|').Where(x => x != "").Take(RecentMax).ToList(); break;
                case "destinations": RecentDestinations = v.Split('|').Where(x => x != "").Take(DestinationsMax).ToList(); break;
                case "split" when double.TryParse(v, CultureInfo.InvariantCulture, out var sp) && double.IsFinite(sp): Split = Math.Clamp(sp, 0.15, 0.85); break;
                case "maximized": Maximized = v == "1"; break;
                case "window": try { var r = Rect.Parse(v); if (!r.IsEmpty) WindowBounds = r; } catch (FormatException) { } break; // hand-edited file
                case var k when k.StartsWith("tabs.") && v != "": Tabs[k[5..]] = v.Split('|').Where(x => x != "").ToList(); break;
                case var k when k.StartsWith("activetab.") && int.TryParse(v, out var ai): ActiveTab[k[10..]] = ai; break;
                case var k when k.StartsWith("pins."): Pins[k[5..]] = v.Split(',').Select(x => int.TryParse(x, out var n) ? n : -1).Where(n => n >= 0).ToList(); break;
                case var k when k.StartsWith("view.") && Enum.TryParse<PaneView.ViewMode>(v, out var vm): Views[k[5..]] = vm; break;
                case var k when k.StartsWith("col.") && double.TryParse(v, CultureInfo.InvariantCulture, out var cw) && double.IsFinite(cw): ColumnWidths[k[4..]] = Math.Clamp(cw, k == "col.Name" ? 30 : 0, 4000); break; // a column squeezed shut stays shut (Rico, 1.0.53)
                // sort=Modified|1|D:\Photos — the path last: it may hold anything but |
                case "sort" when v.Split('|', 3) is [var sk, var sd, var sp] && PaneView.SortKeys.Contains(sk) && sp != "": FolderSorts[sp] = (sk, sd == "1"); break;
                case "groupdate" when v != "": DateGroupFolders.Add(v); break;
                case "sidebarorder" when v != "": SidebarOrder = v.Split(',').Where(x => x != "").ToList(); break;
                case "sidebarcollapsed": SidebarCollapsed.Clear(); SidebarCollapsed.UnionWith(v.Split(',').Where(x => x != "")); break;
                case "sidebar" when double.TryParse(v, CultureInfo.InvariantCulture, out var w) && double.IsFinite(w): SidebarWidth = Math.Clamp(w, 160, 600); break;
            }
        }
    }

    /// Written beside the real file, then moved over it: a shutdown in the middle of a plain write left
    /// an empty file behind (the folder-size cache was found at 0 bytes) and everything in it was lost.
    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        var tmp = path + ".tmp";
        File.WriteAllLines(tmp, lines);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        WriteAllLines(FilePath, Lines());
    }

    static IEnumerable<string> Lines() => new[]
        {
            $"backdrop={Backdrop}",
            $"opacity={Opacity.ToString(CultureInfo.InvariantCulture)}",
            $"accent={Accent}",
            $"rowpad={RowPad.ToString(CultureInfo.InvariantCulture)}",
            $"animations={(Animations ? 1 : 0)}",
            $"smoothscroll={(SmoothScroll ? 1 : 0)}",
            $"textscale={TextScale.ToString(CultureInfo.InvariantCulture)}",
            $"singlepane={(SinglePane ? 1 : 0)}",
            $"preview={(Preview ? 1 : 0)}",
            $"previewwidth={PreviewWidth.ToString(CultureInfo.InvariantCulture)}",
            $"stats={Stats}",
            $"scale.sidebar={SidebarScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.tabs={TabsScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.path={PathScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.list={ListScale.ToString(CultureInfo.InvariantCulture)}",
            $"preview.modules.disabled={string.Join(",", DisabledPreviewModules.OrderBy(id => id, StringComparer.Ordinal))}",
            $"taskbarsearch={(TaskbarSearch ? 1 : 0)}",
            $"taskbarsearchbox={(TaskbarSearchBox ? 1 : 0)}",
            $"itemtips={(ItemTips ? 1 : 0)}",
            $"deletesound={(DeleteSound ? 1 : 0)}",
            $"bgimage={BackgroundImage}",
            $"bgopacity={ImageOpacity.ToString(CultureInfo.InvariantCulture)}",
            $"bgblur={ImageBlur.ToString(CultureInfo.InvariantCulture)}",
            $"bgbright={ImageBrightness.ToString(CultureInfo.InvariantCulture)}",
            $"accentstrength={AccentStrength.ToString(CultureInfo.InvariantCulture)}",
            $"verify={(Verify ? 1 : 0)}",
            $"parallel={(ParallelCopy ? 1 : 0)}",
            $"internaleditor={(InternalEditor ? 1 : 0)}",
            $"hidden={(ShowHidden ? 1 : 0)}",
            $"shortcutfolders={(ShortcutsWithFolders ? 1 : 0)}",
            $"nativemenu={(NativeMenu ? 1 : 0)}",
            $"wine={(TakeWinE ? 1 : 0)}",
            $"lastseen={LastSeenVersion}",
            $"tray={(CloseToTray ? 1 : 0)}",
            $"closeemptypane={(CloseEmptyPane ? 1 : 0)}",
            $"tipdelay={TipDelay}",
            $"autoscroll={(Autoscroll ? 1 : 0)}",
            $"autoscrollx={(AutoscrollSideways ? 1 : 0)}",
            $"quicklist={QuickList}",
            $"recent={string.Join("|", RecentFolders)}",
            $"destinations={string.Join("|", RecentDestinations)}",
            $"sidebar={SidebarWidth.ToString(CultureInfo.InvariantCulture)}",
            $"sidebarorder={string.Join(",", SidebarOrder)}",
            $"sidebarcollapsed={string.Join(",", SidebarCollapsed)}",
            $"split={Split.ToString(CultureInfo.InvariantCulture)}",
            $"maximized={(Maximized ? 1 : 0)}",
            WindowBounds is { } r ? $"window={r.ToString(CultureInfo.InvariantCulture)}" : "",
        }.Concat(ColumnWidths.Select(c => $"col.{c.Key}={c.Value.ToString(CultureInfo.InvariantCulture)}"))
         .Concat(Views.Select(v => $"view.{v.Key}={v.Value}"))
         .Concat(Tabs.Select(t => $"tabs.{t.Key}={string.Join("|", t.Value)}"))
         .Concat(ActiveTab.Select(a => $"activetab.{a.Key}={a.Value}"))
         .Concat(Pins.Select(p => $"pins.{p.Key}={string.Join(",", p.Value)}"))
         .Concat(FolderSorts.Select(s => $"sort={s.Value.Key}|{(s.Value.Desc ? 1 : 0)}|{s.Key}"))
         .Concat(DateGroupFolders.Select(f => $"groupdate={f}"));

    // ---- Default file manager (per user, HKCU only; turning it off removes every key it added) ----
    // Folder/drive double-clicks go through the Directory/Drive default verb; Win+E through the
    // "This PC" launcher CLSID. Explorer's own window navigation is unaffected.
    const string Verb = "pathweft";
    const string WinE = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    static readonly string[] Types = { "Directory", "Drive" };

    public static bool IsDefaultFileManager =>
        (Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell")?.GetValue("") as string) == Verb;

    // Whatever was the default before Pathweft (e.g. another file manager's verb, or a Win+E
    // override), so switching Pathweft off hands folders back to it instead of to nothing.
    const string Backup = @"Software\Darkaxt\Pathweft\PreviousDefault";

    public static void SetDefaultFileManager(bool on)
    {
        if (Profile != null) throw new InvalidOperationException("An isolated test profile cannot change Windows integration.");
        var exe = Environment.ProcessPath;
        var hk = Registry.CurrentUser;
        using var backup = hk.CreateSubKey(Backup);
        foreach (var t in Types)
        {
            var shellPath = $@"Software\Classes\{t}\shell";
            if (on)
            {
                using var shell = hk.CreateSubKey(shellPath);
                if (shell.GetValue("") is string prev && prev != Verb) backup.SetValue(t, prev);
                shell.SetValue("", Verb);
                using var verb = shell.CreateSubKey(Verb);
                verb.SetValue("", "Open in Pathweft");
                verb.SetValue("Icon", exe);
                using var cmd = verb.CreateSubKey("command");
                cmd.SetValue("", $"\"{exe}\" \"%1\"");
            }
            else
            {
                using var shell = hk.OpenSubKey(shellPath, writable: true);
                if (shell == null) continue;
                if (shell.GetValue("") as string == Verb)
                {
                    // restore the previous default only if its verb is still there (the other app may be gone)
                    if (backup.GetValue(t) is string prev && shell.OpenSubKey(prev) != null) shell.SetValue("", prev);
                    else shell.DeleteValue("");
                }
                shell.DeleteSubKeyTree(Verb, throwOnMissingSubKey: false);
                backup.DeleteValue(t, throwOnMissingValue: false);
            }
        }

        SetWinE(on && TakeWinE);

        // Win+E itself: the key above is ignored on recent Windows 11, so a background agent catches it.
        // With Win+E released the agent stays on: it also hands Explorer windows other apps open to Pathweft.
        if (on) Agent.Enable(exe); else Agent.Disable();
    }

    /// Win+E through the "This PC" launcher key; off gives it back to whatever had it (Explorer). The agent
    /// reads TakeWinE itself, at each press.
    public static void SetWinE(bool on)
    {
        var exe = Environment.ProcessPath;
        var hk = Registry.CurrentUser;
        using var backup = hk.CreateSubKey(Backup);
        var winEPath = WinE + @"\shell\opennewwindow\command";
        if (!on)
            using (var cur = hk.OpenSubKey(winEPath))
                // not ours (never taken, or Win+E was off when Pathweft became the default): nothing to give back
                if ((cur?.GetValue("") as string)?.Contains("Pathweft.exe", StringComparison.OrdinalIgnoreCase) != true) return;
        if (on)
        {
            using var cmd = hk.CreateSubKey(winEPath);
            var prev = cmd.GetValue("") as string;
            // any Pathweft build counts as ours: a dev build's path was once kept as "the previous
            // Win+E", which switching Pathweft off would then have restored instead of Explorer
            if (!string.IsNullOrEmpty(prev) && !prev.Contains("Pathweft.exe", StringComparison.OrdinalIgnoreCase))
            {
                backup.SetValue("WinE", prev);
                backup.SetValue("WinEDelegate", cmd.GetValue("DelegateExecute") as string ?? "");
            }
            cmd.SetValue("", $"\"{exe}\"");
            cmd.SetValue("DelegateExecute", "");
        }
        else if (backup.GetValue("WinE") is string prevWinE)
        {
            using var cmd = hk.CreateSubKey(winEPath);
            cmd.SetValue("", prevWinE);
            cmd.SetValue("DelegateExecute", backup.GetValue("WinEDelegate") as string ?? "");
            backup.DeleteValue("WinE", false); backup.DeleteValue("WinEDelegate", false);
        }
        else hk.DeleteSubKeyTree(WinE + @"\shell\opennewwindow", throwOnMissingSubKey: false); // only what we added
    }
}

// Pathweft never discovers or migrates an upstream application's profile.
static class DataFolder
{
    internal static readonly string New = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pathweft");
    internal static string Current => New;
    internal static string WebView2 => Path.Combine(Settings.Dir, "WebView2");
}
