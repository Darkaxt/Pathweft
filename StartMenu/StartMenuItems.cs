using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FileExplorer.StartMenu;

/// StartAllBack's Start-menu settings, read from HKCU\Software\StartIsBack (read only; licensing is never read), and
/// the Windows 7 menu settings StartAllBack keeps in Explorer\Advanced (power button, search, recent items).
/// A value that is absent keeps StartAllBack's default. S6c verifies each against StartAllBack side by side.
internal sealed record StartMenuOptions
{
    internal int RecentPrograms { get; init; } = 10;
    internal bool Flyout { get; init; } = true;
    internal bool AutoCascade { get; init; } = true;
    internal bool AppsFolder { get; init; } = true;
    /// "Override sort order": 0 off (by name, programs first, as Windows lists them), 1 folders first, 2 programs
    /// first, 3 no preference (folders and programs by name together).
    internal int SortOrder { get; init; } = 1;
    internal bool NotifyNewApps { get; init; } = true;
    /// "Display glyph icons": the right column's items show glyphs (2) rather than their icons.
    internal bool RightPaneGlyphs { get; init; } = true;
    /// Right-column items: 0 hidden, 1 a link, 2 a menu.
    internal IReadOnlyDictionary<string, int> Show { get; init; } = new Dictionary<string, int>();
    /// Icon sizes: the pinned and recent programs (Icon size S, M or L), and "Use large icons" for All Programs,
    /// search results and the right column's menus.
    internal int ProgramIcon { get; init; } = 32;
    internal int AllProgramsIcon { get; init; } = 16;
    internal int SearchIcon { get; init; } = 16;
    internal int FlyoutIcon { get; init; } = 16;
    /// "Display user account picture".
    internal bool UserPicture { get; init; } = true;
    /// The power button's action, as Windows 7 encodes it (Start_PowerButtonAction).
    internal int PowerAction { get; init; } = 2;
    /// "Search programs and settings"; files: 0 not searched, 1 without the public folders, 2 with them.
    internal bool SearchPrograms { get; init; } = true;
    internal int SearchFiles { get; init; } = 2;
    /// Windows' "Store and display recently opened programs / items" (off: no recent programs, no recent items).
    internal bool TrackPrograms { get; init; } = true;
    internal bool TrackDocuments { get; init; } = true;
    /// Recent items in jump lists and in Recent items.
    internal int JumpListItems { get; init; } = 10;

    internal const string Key = @"Software\StartIsBack", AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    internal static StartMenuOptions Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        using var advanced = Registry.CurrentUser.OpenSubKey(AdvancedKey);
        int Get(string name, int fallback) => key?.GetValue(name) is int v ? v : fallback;
        int Advanced(string name, int fallback) => advanced?.GetValue(name) is int v ? v : fallback;
        // Icon size's S, L, M are 0, 1, 2 (the Cfg combo's order)
        int programIcon = Get("Start_LargeMFUIcons", 1) switch { 0 => 16, 2 => 24, _ => 32 };
        var show = new Dictionary<string, int>();
        foreach (var (name, fallback) in Items) show[name] = Get(name, fallback);
        return new StartMenuOptions
        {
            RecentPrograms = Math.Clamp(Get("Start_MinMFU", 10), 0, 30),
            Flyout = Get("AllProgramsFlyout", 1) != 0,
            AutoCascade = Get("Start_AutoCascade", 1) != 0,
            AppsFolder = Get("StartMetroAppsFolder", 1) != 0,
            SortOrder = Math.Clamp(Get("Start_SortOverride", 1), 0, 3),
            NotifyNewApps = Get("Start_NotifyNewApps", 1) != 0,
            RightPaneGlyphs = Get("Start_RightPaneIcons", 2) == 2,
            Show = show,
            ProgramIcon = programIcon,
            AllProgramsIcon = Get("Start_LargeAllAppsIcons", 0) != 0 ? 24 : 16,
            SearchIcon = Get("Start_LargeSearchIcons", 0) != 0 ? 24 : 16,
            FlyoutIcon = Get("Start_LargeFlyoutIcons", 0) != 0 ? 24 : 16,
            UserPicture = Get("HideUserFrame", 0) == 0,
            PowerAction = PolicyPowerAction() ?? Advanced("Start_PowerButtonAction", 2),
            SearchPrograms = Advanced("Start_SearchPrograms", 1) != 0,
            SearchFiles = Math.Clamp(Advanced("Start_SearchFiles", 2), 0, 2),
            TrackPrograms = Advanced("Start_TrackProgs", 1) != 0,
            TrackDocuments = Advanced("Start_TrackDocs", 1) != 0,
            JumpListItems = Math.Clamp(Advanced("Start_JumpListItems", Advanced("JumpListItems_Maximum", 10)), 0, 60),
        };
    }

    internal const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    /// The power button's Group Policy ("Change Start Menu power button", PowerButtonAction), which wins over the
    /// user's own choice: the machine's policy first, then the user's.
    static int? PolicyPowerAction()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var policy = root.OpenSubKey(PolicyKey);
            if (policy?.GetValue("PowerButtonAction") is int v) return v;
        }
        return null;
    }

    /// The right column's items in StartAllBack's order, with their defaults.
    internal static readonly (string Name, int Default)[] Items =
    [
        ("Start_ShowUser", 1), ("Start_ShowMyDocs", 1), ("Start_ShowMyPics", 1), ("Start_ShowMyMusic", 1), ("Start_ShowVideos", 0),
        ("Start_ShowDownloads", 0), ("Start_ShowSkyDrive", 0), ("StartMenuFavorites", 0), ("Start_ShowRecentDocs", 0),
        ("Start_ShowMyComputer", 1), ("Start_ShowNetPlaces", 0), ("Start_ShowNetConn", 0), ("Start_ShowControlPanel", 1),
        ("Start_ShowPCSettings", 1), ("Start_ShowPrinters", 1), ("Start_ShowSetProgramAccessAndDefaults", 1), ("Start_AdminToolsRoot", 0),
        ("Start_ShowTerminal", 0), ("Start_ShowCommandPrompt", 0), ("Start_ShowRun", 0),
    ];
}

/// SAB-02 (S6b): the menu's contents, read where Windows and StartAllBack keep them: the pinned programs in
/// User Pinned\StartMenu that Windows' pin list (StartPage2's Favorites) holds, in its order, the recent programs from UserAssist, All Programs
/// from both Start Menu\Programs folders and shell:AppsFolder, and the right column's known folders and commands.
internal static class StartMenuItems
{
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    internal static string PinnedFolder => Path.Combine(AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu");
    internal static readonly string[] ProgramRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    ];
    static readonly string[] Launchable = [".lnk", ".url", ".exe", ".appref-ms", ".website"];

    // ---- pinned ----

    const string StartPage = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartPage2";

    /// The pinned programs StartAllBack shows: the shortcuts in User Pinned\StartMenu that Windows' pin list
    /// (StartPage2's Favorites, a list of item IDs whose names are UTF-16 at either byte alignment) holds, in its order.
    /// A shortcut left in the folder but not in the list is not pinned. Without the list, every shortcut by name.
    internal static List<Entry> Pinned()
    {
        if (!Directory.Exists(PinnedFolder)) return [];
        var files = Directory.EnumerateFiles(PinnedFolder).Where(f => Launchable.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
        if (Registry.GetValue(StartPage, "Favorites", null) is not byte[] favorites)
            return files.Select(Program).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        string even = Encoding.Unicode.GetString(favorites, 0, favorites.Length & ~1), odd = favorites.Length > 1 ? Encoding.Unicode.GetString(favorites, 1, (favorites.Length - 1) & ~1) : "";
        int At(string name)
        {
            // the name ends where the item's string does: a NUL after it
            int Find(string s, int shift) { int i = s.IndexOf(name + "\0", StringComparison.OrdinalIgnoreCase); return i < 0 ? int.MaxValue : i * 2 + shift; }
            return Math.Min(Find(even, 0), Find(odd, 1));
        }
        return files.Select(f => (At: At(Path.GetFileName(f)), File: f)).Where(x => x.At != int.MaxValue).OrderBy(x => x.At).Select(x => Program(x.File)).ToList();
    }

    /// Windows' Start-menu pin handler (shell32's "Start Menu Pin", verbs startpin and startunpin): what StartAllBack
    /// and Windows 7 use, so a pin from Pathweft is in StartAllBack's menu too.
    static readonly Guid StartMenuPin = new("a2a9545d-a0c2-42b4-9708-a0b2badd77c8");

    internal static Entry Program(string file)
    {
        var link = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? StartMenuShell.ReadShortcut(file) : null;
        return new Entry(StartMenuShell.DisplayName(file) ?? Path.GetFileNameWithoutExtension(file), EntryKind.Program, file)
        {
            Tip = link?.Description ?? link?.Path, Owner = StartMenuShell.AppIdOf(link),
        };
    }

    /// Pin to Start menu, as Windows 7 does: Windows' pin handler adds it after the pinned ones; if the handler fails,
    /// a copy of the shortcut in User Pinned\StartMenu (shown while Windows' pin list is absent).
    internal static void Pin(Entry e)
    {
        if (e.Target == null || !File.Exists(e.Target) || !Launchable.Contains(Path.GetExtension(e.Target), StringComparer.OrdinalIgnoreCase)) return;
        if (FileExplorer.ShellMenu.RunHandler(StartMenuPin, e.Target, "startpin")) return;
        Directory.CreateDirectory(PinnedFolder);
        string to = Path.Combine(PinnedFolder, Path.GetFileName(e.Target));
        if (!File.Exists(to)) File.Copy(e.Target, to);
    }

    /// Unpin from Start menu: the pinned copy is removed (never the program's own shortcut).
    internal static void Unpin(Entry e)
    {
        if (e.Target != null && File.Exists(e.Target) && FileExplorer.ShellMenu.RunHandler(StartMenuPin, e.Target, "startunpin")) return;
        if (e.Target != null && string.Equals(Path.GetDirectoryName(e.Target), PinnedFolder, StringComparison.OrdinalIgnoreCase) && File.Exists(e.Target)) File.Delete(e.Target);
    }

    internal static bool IsPinned(Entry e) => e.Target != null && Pinned().Any(p => string.Equals(Path.GetFileName(p.Target), Path.GetFileName(e.Target), StringComparison.OrdinalIgnoreCase));

    /// StartAllBack's "Custom folders...": the folders it adds to the right column, after the known folders, kept in
    /// StartPage2's CustomFolders as item IDs, each after its 16-bit length.
    internal static List<Entry> CustomFolders()
    {
        var list = new List<Entry>();
        if (Registry.GetValue(StartPage, "CustomFolders", null) is not byte[] data) return list;
        for (int at = 0; at + 2 <= data.Length;)
        {
            int size = BitConverter.ToUInt16(data, at);
            if (size < 4 || at + 2 + size > data.Length) break;
            var ids = new byte[size + 2]; // and a terminator, in case the stored list has none
            Array.Copy(data, at + 2, ids, 0, size);
            at += 2 + size;
            if (StartMenuShell.FolderOf(ids) is { } folder && Directory.Exists(folder.Path))
                list.Add(new Entry(folder.Name, EntryKind.Folder, folder.Path) { Glyph = "\uE90F" });
        }
        return list;
    }

    /// Remove from this list, as Windows 7 does for a recent program: its UserAssist count is deleted.
    internal static void ForgetRecent(Entry e)
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count", true);
        if (root == null || e.Target == null) return;
        foreach (var name in root.GetValueNames())
            if (string.Equals(Expand(Rot13(name)), e.Target, StringComparison.OrdinalIgnoreCase)) root.DeleteValue(name, false);
    }

    // ---- recent programs (UserAssist) ----

    /// UserAssist's shortcut counts (ROT13 names, a known-folder id for the Programs folders), as the Windows 7
    /// menu ranks its frequently used programs: launches weighted by how recent the last one was. Uninstallers and
    /// the programs Windows keeps out of the list (FileAssociation\AddRemoveApps) and pinned programs are left out.
    internal static List<Entry> Recent(int count, IReadOnlyCollection<Entry> pinned)
    {
        var result = new List<(double Score, Entry Entry)>();
        if (count <= 0) return [];
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count");
        if (root == null) return [];
        var excluded = new HashSet<string>(((Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileAssociation", "AddRemoveApps", "") as string) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var pinnedTargets = new HashSet<string>(pinned.Select(p => StartMenuShell.ReadShortcut(p.Target)?.Path).Where(p => p != null), StringComparer.OrdinalIgnoreCase);
        var pinnedNames = new HashSet<string>(pinned.Select(p => p.Name), StringComparer.CurrentCultureIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in root.GetValueNames())
        {
            if (root.GetValue(name) is not byte[] data || data.Length < 68) continue;
            int runs = BitConverter.ToInt32(data, 4);
            if (runs <= 0) continue;
            string path = Expand(Rot13(name));
            if (path == null || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !ProgramRoots.Any(r => path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)) || !File.Exists(path)) continue;
            if (!seen.Add(path)) continue;
            var link = StartMenuShell.ReadShortcut(path);
            if (link?.Path is { } target && (excluded.Contains(Path.GetFileName(target)) || pinnedTargets.Contains(target))) continue;
            var entry = Program(path);
            if (pinnedNames.Contains(entry.Name)) continue;
            long last = BitConverter.ToInt64(data, 60);
            double days = last > 0 ? Math.Max(0, (DateTime.UtcNow - DateTime.FromFileTimeUtc(last)).TotalDays) : 365;
            result.Add((runs / (1 + days / 7), entry));
        }
        return result.OrderByDescending(x => x.Score).Take(count).Select(x => x.Entry).ToList();
    }

    static string Rot13(string s)
    {
        var c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            c[i] = c[i] switch { >= 'a' and <= 'z' => (char)('a' + (c[i] - 'a' + 13) % 26), >= 'A' and <= 'Z' => (char)('A' + (c[i] - 'A' + 13) % 26), _ => c[i] };
        return new string(c);
    }

    /// "{known folder id}\rest" to a path.
    static string Expand(string name)
    {
        if (name.Length > 38 && name[0] == '{' && name[37] == '}' && Guid.TryParse(name[..38], out var id))
            return StartMenuShell.KnownFolder(id) is { } folder ? folder + name[38..] : null;
        return name.Length > 2 && name[1] == ':' ? name : null;
    }

    /// Launch counts by shortcut path, to tell a newly installed program that was never opened.
    static HashSet<string> Launched()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}\Count");
        if (root == null) return set;
        foreach (var name in root.GetValueNames())
            if (root.GetValue(name) is byte[] { Length: >= 8 } data && BitConverter.ToInt32(data, 4) > 0 && Expand(Rot13(name)) is { } path) set.Add(path);
        return set;
    }

    // ---- All Programs ----

    /// Both Programs folders merged (a folder of the same name in each is one folder), and the packaged apps in
    /// their own folder or among the programs. New programs (made in the last week and never opened) are marked,
    /// and so is each folder holding one.
    internal static List<Entry> AllPrograms(StartMenuOptions options)
    {
        var launched = Launched();
        var since = DateTime.UtcNow.AddDays(-7);
        var root = Folder(ProgramRoots.Where(Directory.Exists).ToList(), "", options, launched, since);
        var apps = Apps();
        if (apps.Count > 0)
        {
            if (options.AppsFolder) root.Add(new Entry("Windows Apps", EntryKind.Folder, null) { Children = () => apps, Icon = "shell:AppsFolder" });
            else root.AddRange(apps);
        }
        return Sort(root, options);
    }

    static List<Entry> Folder(List<string> folders, string relative, StartMenuOptions options, HashSet<string> launched, DateTime since)
    {
        var list = new List<Entry>();
        var subfolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!Launchable.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(file) & FileAttributes.Hidden) != 0) continue;
                    bool fresh = options.NotifyNewApps && File.GetCreationTimeUtc(file) > since && !launched.Contains(file);
                    list.Add(new Entry(StartMenuShell.DisplayName(file) ?? Path.GetFileNameWithoutExtension(file), EntryKind.Program, file) { New = fresh });
                }
                foreach (var dir in Directory.EnumerateDirectories(folder))
                {
                    if ((File.GetAttributes(dir) & FileAttributes.Hidden) != 0) continue;
                    var name = Path.GetFileName(dir);
                    if (!subfolders.TryGetValue(name, out var same)) subfolders[name] = same = [];
                    same.Add(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var (name, dirs) in subfolders)
        {
            var children = Folder(dirs, Path.Combine(relative, name), options, launched, since);
            if (children.Count == 0) continue;
            var sorted = Sort(children, options);
            list.Add(new Entry(StartMenuShell.DisplayName(dirs[0]) ?? name, EntryKind.Folder, dirs[0]) { Children = () => sorted, New = sorted.Any(c => c.New) });
        }
        // one entry per name, as Windows shows them: the user's shortcut over the all-users one of the same name,
        // and a shortcut over a folder shown with its name (Windows Tools and the Administrative Tools folder)
        return list.GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.FirstOrDefault(e => e.Children == null) ?? g.First()).ToList();
    }

    /// StartAllBack's "Override sort order": folders first, programs first, or both by name together.
    static List<Entry> Sort(List<Entry> list, StartMenuOptions options) => options.SortOrder switch
    {
        1 => list.OrderBy(e => e.Children == null).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        3 => list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => list.OrderBy(e => e.Children != null).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
    };

    /// The packaged apps (an AppUserModelID with '!') from shell:AppsFolder; desktop programs come from the folders.
    internal static List<Entry> Apps() => StartMenuShell.Children("shell:AppsFolder")
        .Where(a => a.Relative?.Contains('!') == true && !string.IsNullOrEmpty(a.Name))
        .Select(a => new Entry(a.Name, EntryKind.Shell, @"shell:AppsFolder\" + a.Relative) { Owner = a.Relative })
        .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    internal static IEnumerable<Entry> Flatten(IEnumerable<Entry> list)
    {
        foreach (var e in list)
            if (e.Children == null) yield return e;
            else foreach (var c in Flatten(e.Children())) yield return c;
    }

    // ---- right column ----

    static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B"), OneDrive = new("A52BBA46-E9E1-435F-B3D9-28DAA648C0F6");

    /// The right column in StartAllBack's order; a 2 shows the item as a menu of what it holds.
    internal static List<Entry> RightColumn(StartMenuOptions o)
    {
        var list = new List<Entry>();
        int Mode(string name) => o.Show.TryGetValue(name, out int v) ? v : 0;
        void Known(string setting, string name, string path, string glyph)
        {
            int mode = Mode(setting);
            if (mode == 0 || string.IsNullOrEmpty(path)) return;
            list.Add(new Entry(name, EntryKind.Folder, path) { Children = mode == 2 ? () => FolderMenu(path) : null, Glyph = glyph });
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Mode("Start_ShowUser") != 0) list.Add(new Entry(UserName(), EntryKind.Folder, profile) { Children = Mode("Start_ShowUser") == 2 ? () => FolderMenu(profile) : null, Tip = "Personal folder", Glyph = "\uE77B" });
        Known("Start_ShowMyDocs", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5");
        Known("Start_ShowMyPics", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uEB9F");
        Known("Start_ShowMyMusic", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "\uEC4F");
        Known("Start_ShowVideos", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "\uE8B2");
        Known("Start_ShowDownloads", "Downloads", StartMenuShell.KnownFolder(Downloads), "\uE896");
        Known("Start_ShowSkyDrive", "OneDrive", StartMenuShell.KnownFolder(OneDrive) is { } od && Directory.Exists(od) ? od : null, "\uE753");
        Known("StartMenuFavorites", "Favorites", Environment.GetFolderPath(Environment.SpecialFolder.Favorites), "\uE734");
        list.AddRange(CustomFolders());
        list.Add(Separator);
        if (Mode("Start_ShowRecentDocs") != 0)
            list.Add(new Entry("Recent Items", EntryKind.Folder, Environment.GetFolderPath(Environment.SpecialFolder.Recent)) { Children = Mode("Start_ShowRecentDocs") == 2 ? () => RecentItems(o) : null, Glyph = "\uE823" });
        if (Mode("Start_ShowMyComputer") != 0)
            list.Add(new Entry("This PC", EntryKind.Folder, StartMenuShell.ThisPC) { Children = Mode("Start_ShowMyComputer") == 2 ? Drives : null, Icon = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", Glyph = "\uE977" });
        if (Mode("Start_ShowNetPlaces") != 0)
            list.Add(new Entry("Network", EntryKind.Shell, "shell:NetworkPlacesFolder") { Children = Mode("Start_ShowNetPlaces") == 2 ? () => ShellMenu("shell:NetworkPlacesFolder") : null, Glyph = "\uE968" });
        if (Mode("Start_ShowNetConn") != 0)
            list.Add(new Entry("Network Connections", EntryKind.Shell, "shell:ConnectionsFolder") { Children = Mode("Start_ShowNetConn") == 2 ? () => ShellMenu("shell:ConnectionsFolder") : null, Glyph = "\uE839" });
        list.Add(Separator);
        const string controlPanel = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
        if (Mode("Start_ShowControlPanel") != 0)
            list.Add(new Entry("Control Panel", EntryKind.Shell, controlPanel) { Children = Mode("Start_ShowControlPanel") == 2 ? () => ShellMenu(controlPanel + @"\0") : null, Glyph = "\uE770" });
        if (Mode("Start_ShowPCSettings") != 0)
            list.Add(new Entry("Settings", EntryKind.Shell, "ms-settings:") { Children = Mode("Start_ShowPCSettings") == 2 ? () => SettingsPages.Where(p => p.Top).Select(p => p.Entry).ToList() : null, Icon = SettingsIcon, Glyph = "\uE713" });
        if (Mode("Start_ShowPrinters") != 0)
            list.Add(new Entry("Devices and Printers", EntryKind.Shell, "::{A8A91A66-3A7D-4424-8D24-04E180695C7A}") { Children = Mode("Start_ShowPrinters") == 2 ? () => ShellMenu("::{A8A91A66-3A7D-4424-8D24-04E180695C7A}") : null, Glyph = "\uE772" });
        if (Mode("Start_ShowSetProgramAccessAndDefaults") != 0) list.Add(new Entry("Default Programs", EntryKind.Shell, "ms-settings:defaultapps") { Icon = SettingsIcon, Glyph = "\uE71D" });
        if (Mode("Start_AdminToolsRoot") != 0)
            list.Add(new Entry("Windows Tools", EntryKind.Shell, "shell:::{D20EA4E1-3957-11D2-A40B-0C5020524153}") { Children = Mode("Start_AdminToolsRoot") == 2 ? () => ShellMenu("shell:::{D20EA4E1-3957-11D2-A40B-0C5020524153}") : null, Glyph = "\uE90F" });
        if (Mode("Start_ShowTerminal") != 0) list.Add(Terminal() with { Glyph = "\uE756" });
        if (Mode("Start_ShowCommandPrompt") != 0) list.Add(new Entry("Command Prompt", EntryKind.Program, Path.Combine(Environment.SystemDirectory, "cmd.exe")) { WorkingDirectory = profile, Glyph = "\uE756" });
        if (Mode("Start_ShowRun") != 0) list.Add(new Entry("Run...", EntryKind.Shell, "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}") { Glyph = "\uE768" });
        // no separator first, last or twice
        for (int i = list.Count - 1; i >= 0; i--)
            if (ReferenceEquals(list[i], Separator) && (i == 0 || i == list.Count - 1 || ReferenceEquals(list[i - 1], Separator))) list.RemoveAt(i);
        return list;
    }

    internal const string SettingsIcon = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
    internal static readonly Entry Separator = new("", EntryKind.Command, null);

    static Entry Terminal()
    {
        const string app = @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
        if (StartMenuShell.DisplayName(app) != null) return new Entry("Terminal", EntryKind.Shell, app);
        return new Entry("Terminal", EntryKind.Program, Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\\v1.0\\powershell.exe"));
    }

    internal static string UserName()
    {
        var buffer = new StringBuilder(256); uint size = (uint)buffer.Capacity;
        return GetUserNameEx(3 /* NameDisplay */, buffer, ref size) && buffer.Length > 0 ? buffer.ToString() : Environment.UserName;
    }

    /// The account picture Windows shows (the per-user AccountPicture key), or Windows' default user picture.
    internal static string UserPicture()
    {
        try
        {
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            foreach (var size in new[] { "Image96", "Image64", "Image192", "Image48" })
                if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid, size, null) is string p && File.Exists(p)) return p;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\User Account Pictures\user.png");
        return File.Exists(fallback) ? fallback : null;
    }

    /// A folder shown as a menu: its folders (each a menu again), then its files, by name.
    internal static List<Entry> FolderMenu(string folder)
    {
        var list = new List<Entry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
                if ((File.GetAttributes(dir) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    list.Add(new Entry(Path.GetFileName(dir), EntryKind.Folder, dir) { Children = () => FolderMenu(dir) });
            foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
                if ((File.GetAttributes(file) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    list.Add(new Entry(StartMenuShell.DisplayName(file) ?? Path.GetFileName(file), EntryKind.Document, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (list.Count == 0) list.Add(new Entry("(Empty)", EntryKind.Command, null));
        return list;
    }

    /// Recent items: the shortcuts in the Recent folder, newest first, as many as jump lists show; none while Windows
    /// keeps no recent items.
    internal static List<Entry> RecentItems(StartMenuOptions o)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        int max = o.JumpListItems;
        var list = new List<Entry>();
        if (o.TrackDocuments && max > 0)
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                var link = StartMenuShell.ReadShortcut(file.FullName);
                if (link?.Path == null) continue;
                bool isFolder = PaneView.IsRemote(link.Path) ? !Path.HasExtension(link.Path) : Directory.Exists(link.Path);
                list.Add(new Entry(Path.GetFileNameWithoutExtension(file.Name), isFolder ? EntryKind.Folder : EntryKind.Document, link.Path) { Tip = link.Path });
                if (list.Count == max) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (list.Count == 0) list.Add(new Entry("(Empty)", EntryKind.Command, null));
        return list;
    }

    /// Computer as a menu: the drives, as This PC shows them.
    internal static List<Entry> Drives() => DriveInfo.GetDrives().Select(d =>
    {
        string label = "";
        try { if (d.IsReady) label = d.VolumeLabel; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (label.Length == 0) label = d.DriveType switch { DriveType.Removable => "Removable Disk", DriveType.Network => "Network Drive", DriveType.CDRom => "CD Drive", _ => "Local Disk" };
        return new Entry($"{label} ({d.Name.TrimEnd('\\')})", EntryKind.Folder, d.Name);
    }).ToList();

    /// A virtual Shell folder shown as a menu (Control Panel's items, Network, Connect To): it stays in its own host.
    internal static List<Entry> ShellMenu(string folder)
    {
        var list = StartMenuShell.Children(folder).Where(c => !string.IsNullOrEmpty(c.Name) && c.Parsing != null)
            .Select(c => new Entry(c.Name, KindOf(c.Parsing), c.Parsing))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (list.Count == 0) list.Add(new Entry("(Empty)", EntryKind.Command, null));
        return list;
    }

    static EntryKind KindOf(string parsing) => parsing.Length > 2 && parsing[1] == ':' && Directory.Exists(parsing) ? EntryKind.Folder : EntryKind.Shell;

    // ---- Settings (PC settings pages) for the right column's menu and for search ----

    internal static readonly (Entry Entry, bool Top)[] SettingsPages = new (string Name, string Uri, bool Top)[]
    {
        ("System", "ms-settings:system", true), ("Bluetooth & devices", "ms-settings:devices", true), ("Network & internet", "ms-settings:network", true),
        ("Personalization", "ms-settings:personalization", true), ("Apps", "ms-settings:appsfeatures", true), ("Accounts", "ms-settings:accounts", true),
        ("Time & language", "ms-settings:dateandtime", true), ("Gaming", "ms-settings:gaming-gamebar", true), ("Accessibility", "ms-settings:easeofaccess", true),
        ("Privacy & security", "ms-settings:privacy", true), ("Windows Update", "ms-settings:windowsupdate", true),
        ("Display", "ms-settings:display", false), ("Sound", "ms-settings:sound", false), ("Notifications", "ms-settings:notifications", false),
        ("Focus", "ms-settings:quiethours", false), ("Power & battery", "ms-settings:powersleep", false), ("Storage", "ms-settings:storagesense", false),
        ("Nearby sharing", "ms-settings:crossdevice", false), ("Multitasking", "ms-settings:multitasking", false), ("Activation", "ms-settings:activation", false),
        ("Troubleshoot", "ms-settings:troubleshoot", false), ("Recovery", "ms-settings:recovery", false), ("Projecting to this PC", "ms-settings:project", false),
        ("Remote Desktop", "ms-settings:remotedesktop", false), ("Clipboard", "ms-settings:clipboard", false), ("About", "ms-settings:about", false),
        ("Printers & scanners", "ms-settings:printers", false), ("Mouse", "ms-settings:mousetouchpad", false), ("Touchpad", "ms-settings:devices-touchpad", false),
        ("Pen & Windows Ink", "ms-settings:pen", false), ("AutoPlay", "ms-settings:autoplay", false), ("USB", "ms-settings:usb", false),
        ("Wi-Fi", "ms-settings:network-wifi", false), ("Ethernet", "ms-settings:network-ethernet", false), ("VPN", "ms-settings:network-vpn", false),
        ("Proxy", "ms-settings:network-proxy", false), ("Mobile hotspot", "ms-settings:network-mobilehotspot", false), ("Airplane mode", "ms-settings:network-airplanemode", false),
        ("Background", "ms-settings:personalization-background", false), ("Colors", "ms-settings:colors", false), ("Themes", "ms-settings:themes", false),
        ("Lock screen", "ms-settings:lockscreen", false), ("Fonts", "ms-settings:fonts", false), ("Start", "ms-settings:personalization-start", false),
        ("Taskbar", "ms-settings:taskbar", false), ("Installed apps", "ms-settings:appsfeatures", false), ("Default apps", "ms-settings:defaultapps", false),
        ("Optional features", "ms-settings:optionalfeatures", false), ("Startup apps", "ms-settings:startupapps", false), ("Your info", "ms-settings:yourinfo", false),
        ("Email & accounts", "ms-settings:emailandaccounts", false), ("Sign-in options", "ms-settings:signinoptions", false), ("Family", "ms-settings:family-group", false),
        ("Other users", "ms-settings:otherusers", false), ("Windows backup", "ms-settings:backup", false), ("Language & region", "ms-settings:regionlanguage", false),
        ("Typing", "ms-settings:typing", false), ("Speech", "ms-settings:speech", false), ("Game Mode", "ms-settings:gaming-gamemode", false),
        ("Text size", "ms-settings:easeofaccess-display", false), ("Mouse pointer and touch", "ms-settings:easeofaccess-mousepointer", false),
        ("Magnifier", "ms-settings:easeofaccess-magnifier", false), ("Narrator", "ms-settings:easeofaccess-narrator", false), ("Keyboard", "ms-settings:easeofaccess-keyboard", false),
        ("Location", "ms-settings:privacy-location", false), ("Camera", "ms-settings:privacy-webcam", false), ("Microphone", "ms-settings:privacy-microphone", false),
        ("Windows Security", "ms-settings:windowsdefender", false), ("For developers", "ms-settings:developers", false), ("Delivery Optimization", "ms-settings:delivery-optimization", false),
        ("Advanced options", "ms-settings:windowsupdate-options", false), ("Update history", "ms-settings:windowsupdate-history", false), ("Date & time", "ms-settings:dateandtime", false),
    }.Select(p => (new Entry(p.Name, EntryKind.Shell, p.Uri) { Icon = SettingsIcon, Tip = "Settings" }, p.Top)).ToArray();

    [DllImport("secur32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);
}
