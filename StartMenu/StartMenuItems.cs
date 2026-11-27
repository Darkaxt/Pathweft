using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FileExplorer.StartMenu;

/// StartAllBack's Start-menu settings, read from HKCU\Software\StartIsBack (read only; licensing is never read).
/// A value that is absent keeps StartAllBack's default. S6c verifies each against StartAllBack side by side.
internal sealed record StartMenuOptions
{
    internal int RecentPrograms { get; init; } = 10;
    internal bool Flyout { get; init; } = true;
    internal bool AutoCascade { get; init; } = true;
    internal bool AppsFolder { get; init; } = true;
    internal bool Sorted { get; init; } = true;
    internal bool NotifyNewApps { get; init; } = true;
    internal bool RightPaneIcons { get; init; } = true;
    /// Right-column items: 0 hidden, 1 a link, 2 a menu.
    internal IReadOnlyDictionary<string, int> Show { get; init; } = new Dictionary<string, int>();

    internal const string Key = @"Software\StartIsBack";

    internal static StartMenuOptions Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        int Get(string name, int fallback) => key?.GetValue(name) is int v ? v : fallback;
        var show = new Dictionary<string, int>();
        foreach (var (name, fallback) in Items) show[name] = Get(name, fallback);
        return new StartMenuOptions
        {
            RecentPrograms = Math.Clamp(Get("Start_MinMFU", 10), 0, 30),
            Flyout = Get("AllProgramsFlyout", 1) != 0,
            AutoCascade = Get("Start_AutoCascade", 1) != 0,
            AppsFolder = Get("StartMetroAppsFolder", 1) != 0,
            Sorted = Get("Start_SortOverride", 1) != 0,
            NotifyNewApps = Get("Start_NotifyNewApps", 1) != 0,
            RightPaneIcons = Get("Start_RightPaneIcons", 2) != 0,
            Show = show,
        };
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
/// User Pinned\StartMenu (their order in StartPage2's Favorites), the recent programs from UserAssist, All Programs
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

    internal static List<Entry> Pinned()
    {
        var list = new List<(int Order, Entry Entry)>();
        if (!Directory.Exists(PinnedFolder)) return [];
        byte[] favorites = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartPage2", "Favorites", null) as byte[];
        string order = favorites == null ? "" : Encoding.Unicode.GetString(favorites, 0, favorites.Length & ~1);
        foreach (var file in Directory.EnumerateFiles(PinnedFolder))
        {
            if (!Launchable.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            int at = order.IndexOf(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
            list.Add((at < 0 ? int.MaxValue : at, Program(file)));
        }
        return list.OrderBy(x => x.Order).ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Entry).ToList();
    }

    internal static Entry Program(string file)
    {
        var link = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? StartMenuShell.ReadShortcut(file) : null;
        return new Entry(StartMenuShell.DisplayName(file) ?? Path.GetFileNameWithoutExtension(file), EntryKind.Program, file)
        {
            Tip = link?.Description ?? link?.Path, Owner = StartMenuShell.AppIdOf(link),
        };
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
        return list;
    }

    /// Windows 7 order with StartAllBack's sorting on: programs first, then folders, each by name.
    static List<Entry> Sort(List<Entry> list, StartMenuOptions options) => options.Sorted
        ? list.OrderBy(e => e.Children != null).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList() : list;

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
        void Known(string setting, string name, string path)
        {
            int mode = Mode(setting);
            if (mode == 0 || string.IsNullOrEmpty(path)) return;
            list.Add(new Entry(name, EntryKind.Folder, path) { Children = mode == 2 ? () => FolderMenu(path) : null });
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Mode("Start_ShowUser") != 0) list.Add(new Entry(UserName(), EntryKind.Folder, profile) { Children = Mode("Start_ShowUser") == 2 ? () => FolderMenu(profile) : null, Tip = "Personal folder" });
        Known("Start_ShowMyDocs", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Known("Start_ShowMyPics", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Known("Start_ShowMyMusic", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Known("Start_ShowVideos", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Known("Start_ShowDownloads", "Downloads", StartMenuShell.KnownFolder(Downloads));
        Known("Start_ShowSkyDrive", "OneDrive", StartMenuShell.KnownFolder(OneDrive) is { } od && Directory.Exists(od) ? od : null);
        Known("StartMenuFavorites", "Favorites", Environment.GetFolderPath(Environment.SpecialFolder.Favorites));
        if (Mode("Start_ShowRecentDocs") != 0)
            list.Add(new Entry("Recent items", EntryKind.Folder, Environment.GetFolderPath(Environment.SpecialFolder.Recent)) { Children = Mode("Start_ShowRecentDocs") == 2 ? RecentItems : null });
        list.Add(Separator);
        if (Mode("Start_ShowMyComputer") != 0)
            list.Add(new Entry("Computer", EntryKind.Folder, StartMenuShell.ThisPC) { Children = Mode("Start_ShowMyComputer") == 2 ? Drives : null, Icon = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" });
        if (Mode("Start_ShowNetPlaces") != 0)
            list.Add(new Entry("Network", EntryKind.Shell, "shell:NetworkPlacesFolder") { Children = Mode("Start_ShowNetPlaces") == 2 ? () => ShellMenu("shell:NetworkPlacesFolder") : null });
        if (Mode("Start_ShowNetConn") != 0)
            list.Add(new Entry("Connect To", EntryKind.Shell, "shell:ConnectionsFolder") { Children = Mode("Start_ShowNetConn") == 2 ? () => ShellMenu("shell:ConnectionsFolder") : null });
        list.Add(Separator);
        const string controlPanel = "::{26EE0668-A00A-44D7-9371-BEB064C98683}";
        if (Mode("Start_ShowControlPanel") != 0)
            list.Add(new Entry("Control Panel", EntryKind.Shell, controlPanel) { Children = Mode("Start_ShowControlPanel") == 2 ? () => ShellMenu(controlPanel + @"\0") : null });
        if (Mode("Start_ShowPCSettings") != 0)
            list.Add(new Entry("PC settings", EntryKind.Shell, "ms-settings:") { Children = Mode("Start_ShowPCSettings") == 2 ? () => SettingsPages.Where(p => p.Top).Select(p => p.Entry).ToList() : null, Icon = SettingsIcon });
        if (Mode("Start_ShowPrinters") != 0)
            list.Add(new Entry("Devices and Printers", EntryKind.Shell, "::{A8A91A66-3A7D-4424-8D24-04E180695C7A}") { Children = Mode("Start_ShowPrinters") == 2 ? () => ShellMenu("::{A8A91A66-3A7D-4424-8D24-04E180695C7A}") : null });
        if (Mode("Start_ShowSetProgramAccessAndDefaults") != 0) list.Add(new Entry("Default Programs", EntryKind.Shell, "ms-settings:defaultapps") { Icon = SettingsIcon });
        if (Mode("Start_AdminToolsRoot") != 0)
            list.Add(new Entry("Windows Tools", EntryKind.Shell, "shell:::{D20EA4E1-3957-11D2-A40B-0C5020524153}") { Children = Mode("Start_AdminToolsRoot") == 2 ? () => ShellMenu("shell:::{D20EA4E1-3957-11D2-A40B-0C5020524153}") : null });
        if (Mode("Start_ShowTerminal") != 0) list.Add(Terminal());
        if (Mode("Start_ShowCommandPrompt") != 0) list.Add(new Entry("Command Prompt", EntryKind.Program, Path.Combine(Environment.SystemDirectory, "cmd.exe")) { WorkingDirectory = profile });
        if (Mode("Start_ShowRun") != 0) list.Add(new Entry("Run...", EntryKind.Shell, "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}"));
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

    /// Recent items: the shortcuts in the Recent folder, newest first, as many as jump lists show.
    internal static List<Entry> RecentItems()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        int max = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "JumpListItems_Maximum", 10) is int m and > 0 ? m : 10;
        var list = new List<Entry>();
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
