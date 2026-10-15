using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FileExplorer;

// key=value lines in %AppData%\FileLabs\settings.txt
public static class Settings
{
    // FILELABS_PROFILE=<folder>: a copy for testing beside the installed one, with its own settings and
    // its own single instance, writing nothing the installed copy or the sister apps read
    public static readonly string Profile = Environment.GetEnvironmentVariable("FILELABS_PROFILE") is { Length: > 0 } p ? p : null;
    public static readonly string Dir = Profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileLabs");
    static readonly string FilePath = Path.Combine(Dir, "settings.txt");

    public static int Backdrop = 3;          // 1 solid, 2 mica, 3 acrylic
    public static double Opacity = 70;       // 30–100, floor keeps text readable
    public static string Accent = "#4C8DFF"; // Protagonist Labs blue by default
    public static double RowPad = 3;         // row height: 1 compact, 3 normal, 6 roomy
    public static bool Animations = true;
    public static double TextScale = 1;      // the whole window, text included: 1, 1.15, 1.3, 1.5
    public static bool SinglePane;           // the right pane is hidden
    public static int Stats = 2;             // the cards above the panes: 2 full, 1 compact (one line each), 0 hidden
    public static double SidebarScale = 1, TabsScale = 1, PathScale = 1, ListScale = 1; // per area, on top of TextScale: 0.8–1.5
    public static bool QuickLook;            // Space asks QuickLook (QL-Win) for the preview when it is running
    public static bool ItemTips = true;      // hovering a file shows its full name, type, size and date
    public static bool DeleteSound = true;   // Shift+Delete's warning and Windows' delete sound; off = silent
    public static string BackgroundImage = ""; // a picture behind the panes, "" = none
    public static double ImageOpacity = 40;  // how much of it shows, 5–100
    public static double ImageBlur;          // blur radius in px, 0–40
    public static double ImageBrightness;    // −100 (black) … 0 … +100 (white)
    public static double AccentStrength = 1.0; // how opaque the accent tints are, 1 = as designed
    public static bool InternalEditor = true; // Enter on a code/text file opens the F4 editor, not its app
    public static bool Verify;               // xxHash3 check after copying
    public static bool ShowHidden;
    public static string LastSeenVersion = "";   // What's new is shown once per version
    public static bool NativeMenu;          // right-click shows the Windows menu instead of ours
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

    public static void Load()
    {
        if (!File.Exists(FilePath)) return;
        foreach (var kv in File.ReadAllLines(FilePath).Select(l => l.Split('=', 2)).Where(a => a.Length == 2))
        {
            var v = kv[1].Trim();
            switch (kv[0].Trim())
            {
                case "backdrop" when int.TryParse(v, out var b) && b is >= 1 and <= 3: Backdrop = b; break;
                case "opacity" when double.TryParse(v, CultureInfo.InvariantCulture, out var o) && double.IsFinite(o): Opacity = Math.Clamp(o, 30, 100); break;
                case "accent" when System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$"): Accent = v; break;
                case "rowpad" when double.TryParse(v, CultureInfo.InvariantCulture, out var rp) && double.IsFinite(rp): RowPad = Math.Clamp(rp, 0, 10); break;
                case "animations": Animations = v == "1"; break;
                case "textscale" when double.TryParse(v, CultureInfo.InvariantCulture, out var ts) && double.IsFinite(ts): TextScale = Math.Clamp(ts, 1, 1.6); break;
                case "singlepane": SinglePane = v == "1"; break;
                case "stats" when int.TryParse(v, out var st) && st is >= 0 and <= 2: Stats = st; break;
                case "scale.sidebar" when double.TryParse(v, CultureInfo.InvariantCulture, out var s1) && double.IsFinite(s1): SidebarScale = Math.Clamp(s1, 0.8, 1.5); break;
                case "scale.tabs" when double.TryParse(v, CultureInfo.InvariantCulture, out var s2) && double.IsFinite(s2): TabsScale = Math.Clamp(s2, 0.8, 1.5); break;
                case "scale.path" when double.TryParse(v, CultureInfo.InvariantCulture, out var s3) && double.IsFinite(s3): PathScale = Math.Clamp(s3, 0.8, 1.5); break;
                case "scale.list" when double.TryParse(v, CultureInfo.InvariantCulture, out var s4) && double.IsFinite(s4): ListScale = Math.Clamp(s4, 0.8, 1.5); break;
                case "quicklook": QuickLook = v == "1"; break;
                case "itemtips": ItemTips = v == "1"; break;
                case "deletesound": DeleteSound = v == "1"; break;
                case "bgimage": BackgroundImage = v; break;
                case "bgblur" when double.TryParse(v, CultureInfo.InvariantCulture, out var bb) && double.IsFinite(bb): ImageBlur = Math.Clamp(bb, 0, 40); break;
                case "bgbright" when double.TryParse(v, CultureInfo.InvariantCulture, out var bl) && double.IsFinite(bl): ImageBrightness = Math.Clamp(bl, -100, 100); break;
                case "bgopacity" when double.TryParse(v, CultureInfo.InvariantCulture, out var bo) && double.IsFinite(bo): ImageOpacity = Math.Clamp(bo, 5, 100); break;
                case "accentstrength" when double.TryParse(v, CultureInfo.InvariantCulture, out var ast) && double.IsFinite(ast): AccentStrength = Math.Clamp(ast, 0.3, 2.5); break;
                case "verify": Verify = v == "1"; break;
                case "internaleditor": InternalEditor = v == "1"; break;
                case "hidden": ShowHidden = v == "1"; break;
                case "nativemenu": NativeMenu = v == "1"; break;
                case "lastseen": LastSeenVersion = v; break;
                case "split" when double.TryParse(v, CultureInfo.InvariantCulture, out var sp) && double.IsFinite(sp): Split = Math.Clamp(sp, 0.15, 0.85); break;
                case "maximized": Maximized = v == "1"; break;
                case "window": try { var r = Rect.Parse(v); if (!r.IsEmpty) WindowBounds = r; } catch (FormatException) { } break; // hand-edited file
                case var k when k.StartsWith("tabs.") && v != "": Tabs[k[5..]] = v.Split('|').Where(x => x != "").ToList(); break;
                case var k when k.StartsWith("activetab.") && int.TryParse(v, out var ai): ActiveTab[k[10..]] = ai; break;
                case var k when k.StartsWith("pins."): Pins[k[5..]] = v.Split(',').Select(x => int.TryParse(x, out var n) ? n : -1).Where(n => n >= 0).ToList(); break;
                case var k when k.StartsWith("view.") && Enum.TryParse<PaneView.ViewMode>(v, out var vm): Views[k[5..]] = vm; break;
                case var k when k.StartsWith("col.") && double.TryParse(v, CultureInfo.InvariantCulture, out var cw) && double.IsFinite(cw): ColumnWidths[k[4..]] = Math.Clamp(cw, 30, 800); break;
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
        WriteAllLines(FilePath, new[]
        {
            $"backdrop={Backdrop}",
            $"opacity={Opacity.ToString(CultureInfo.InvariantCulture)}",
            $"accent={Accent}",
            $"rowpad={RowPad.ToString(CultureInfo.InvariantCulture)}",
            $"animations={(Animations ? 1 : 0)}",
            $"textscale={TextScale.ToString(CultureInfo.InvariantCulture)}",
            $"singlepane={(SinglePane ? 1 : 0)}",
            $"stats={Stats}",
            $"scale.sidebar={SidebarScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.tabs={TabsScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.path={PathScale.ToString(CultureInfo.InvariantCulture)}",
            $"scale.list={ListScale.ToString(CultureInfo.InvariantCulture)}",
            $"quicklook={(QuickLook ? 1 : 0)}",
            $"itemtips={(ItemTips ? 1 : 0)}",
            $"deletesound={(DeleteSound ? 1 : 0)}",
            $"bgimage={BackgroundImage}",
            $"bgopacity={ImageOpacity.ToString(CultureInfo.InvariantCulture)}",
            $"bgblur={ImageBlur.ToString(CultureInfo.InvariantCulture)}",
            $"bgbright={ImageBrightness.ToString(CultureInfo.InvariantCulture)}",
            $"accentstrength={AccentStrength.ToString(CultureInfo.InvariantCulture)}",
            $"verify={(Verify ? 1 : 0)}",
            $"internaleditor={(InternalEditor ? 1 : 0)}",
            $"hidden={(ShowHidden ? 1 : 0)}",
            $"nativemenu={(NativeMenu ? 1 : 0)}",
            $"lastseen={LastSeenVersion}",
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
         .Concat(Pins.Select(p => $"pins.{p.Key}={string.Join(",", p.Value)}")));
    }

    // ---- Default file manager (per user, HKCU only; turning it off removes every key it added) ----
    // Folder/drive double-clicks go through the Directory/Drive default verb; Win+E through the
    // "This PC" launcher CLSID. Explorer's own window navigation is unaffected.
    const string Verb = "filelabs";
    const string WinE = @"Software\Classes\CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
    static readonly string[] Types = { "Directory", "Drive" };

    public static bool IsDefaultFileManager =>
        (Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell")?.GetValue("") as string) == Verb;

    // Whatever was the default before File Labs (e.g. another file manager's verb, or a Win+E
    // override), so switching File Labs off hands folders back to it instead of to nothing.
    const string Backup = @"Software\Protagonist Labs\File Labs\PreviousDefault";

    public static void SetDefaultFileManager(bool on)
    {
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
                verb.SetValue("", "Open in File Labs");
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

        var winEPath = WinE + @"\shell\opennewwindow\command";
        if (on)
        {
            using var cmd = hk.CreateSubKey(winEPath);
            var prev = cmd.GetValue("") as string;
            // any File Labs build counts as ours: a dev build's path was once kept as "the previous
            // Win+E", which switching File Labs off would then have restored instead of Explorer
            if (!string.IsNullOrEmpty(prev) && !prev.Contains("FileLabs.exe", StringComparison.OrdinalIgnoreCase))
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

        // Win+E itself: the key above is ignored on recent Windows 11, so a background agent catches it.
        if (on) Agent.Enable(exe); else Agent.Disable();
    }
}
