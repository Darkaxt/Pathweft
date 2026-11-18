using System.IO;
using System.Text;
using System.Windows;

namespace FileExplorer;

// COLS-01..04: what a Details column can be. A key is a built-in name (Size, Modified, Owner…), a hash ("#SHA-1")
// or a Windows property by its canonical name ("System.Author"). Layouts are kept per view kind; until the owner
// chooses columns for a kind it shows the columns its data has, as before.
internal static class ColumnCatalog
{
    internal const string Folder = "folder", Archive = "archive", Search = "search", Space = "space", Drives = "drives";
    internal static readonly string[] Kinds = { Folder, Archive, Search, Space, Drives };

    /// The menu's common columns per kind, in menu order (Name is fixed and not listed).
    internal static string[] Common(string kind) => kind switch
    {
        Drives => new[] { "Capacity", "Free space", "Filesystem", "Connection", "Location" },
        Space => new[] { "Modified", "Created", "Type", "Version", "Files", "Folders" },
        Archive => new[] { "Modified", "Type", "Size", "Attributes" },
        _ => new[] { "Modified", "Type", "Size", "Created", "Accessed", "Attributes", "Version", "Owner" },
    };

    internal static bool IsHash(string key) => key.StartsWith('#');
    internal static string Algorithm(string key) => key[1..];
    internal static bool IsProperty(string key) => key.Contains('.');

    internal static string Header(string key) => key switch
    {
        "Modified" => "Date modified", "Created" => "Date created", "Accessed" => "Date accessed",
        "Files" => "Files", "Folders" => "Folders",
        _ when IsHash(key) => Algorithm(key),
        _ when IsProperty(key) => ShellProperties.Find(key)?.Label ?? key,
        _ => key,
    };

    internal static double DefaultWidth(string key) => key switch
    {
        "Modified" or "Created" or "Accessed" => 150, "Type" => 50, "Size" => 80, "Version" => 90, "Attributes" => 80, "Owner" => 160,
        "Files" or "Folders" => 70,
        "#CRC-32" => 80, "#CRC-64" or "#XXH64" or "#XXH3-64" => 140, "#MD5" => 250, "#SHA-1" => 300,
        _ when IsHash(key) => 420,
        _ when IsProperty(key) => ShellProperties.Find(key)?.Width ?? 140,
        _ => 100,
    };

    /// The kind's chosen columns in order (without Name), or null while the kind shows what its data has.
    internal static List<string> Layout(string kind) => Settings.ColumnSets.TryGetValue(kind, out var set) ? set : null;

    /// Raised with the kind whose columns changed: every open pane of that kind follows at once.
    internal static event Action<string> LayoutChanged;

    internal static void SetLayout(string kind, IEnumerable<string> keys)
    {
        Settings.ColumnSets[kind] = keys.Where(k => k != "Name" && k != "Message").Distinct().ToList();
        Settings.Save();
        LayoutChanged?.Invoke(kind);
    }

    /// Automatic: the kind shows what its data has again (COLS-03).
    internal static void ResetLayout(string kind)
    {
        if (!Settings.ColumnSets.Remove(kind)) return;
        Settings.Save();
        LayoutChanged?.Invoke(kind);
    }

    /// Hashes > Lowercase changed: every pane shows its cells again.
    internal static void CaseChanged() => LayoutChanged?.Invoke(null);

    /// Settings: Hashes > Lowercase. Off: every hash in upper case, as HashTab shows them.
    internal static string Case(string value) => value == null ? null : Settings.HashLowercase ? value.ToLowerInvariant() : value.ToUpperInvariant();

    internal static string Attributes(FileAttributes a, bool isDir)
    {
        var text = new StringBuilder();
        void Add(bool on, char letter) { if (on) text.Append(letter); }
        Add((a & FileAttributes.ReadOnly) != 0, 'R'); Add((a & FileAttributes.Hidden) != 0, 'H'); Add((a & FileAttributes.System) != 0, 'S');
        Add(isDir, 'D'); Add((a & FileAttributes.Archive) != 0, 'A'); Add((a & FileAttributes.Compressed) != 0, 'C'); Add((a & FileAttributes.Encrypted) != 0, 'E');
        Add((a & FileAttributes.Offline) != 0, 'O'); Add(((int)a & 0x80000) != 0, 'P'); Add(((int)a & 0x100000) != 0, 'U'); Add((a & FileAttributes.ReparsePoint) != 0, 'L');
        Add((a & FileAttributes.NotContentIndexed) != 0, 'I');
        return text.ToString();
    }

    static string Date(DateTime t) => t == default ? "" : t.ToString("yyyy-MM-dd  HH:mm");

    /// A cell's text for the extended columns (the original ones keep their own templates and properties).
    internal static string Text(Entry e, string key)
    {
        if (e.IsUp) return "";
        switch (key)
        {
            case "Created": return Date(e.Created);
            case "Accessed": return Date(e.Accessed);
            case "Attributes": return e.Attributes == 0 && !e.IsDir ? "" : Attributes(e.Attributes, e.IsDir);
            case "Files": return e is SpaceEntry { IsDir: true } f ? f.Files.ToString("N0") : "";
            case "Folders": return e is SpaceEntry { IsDir: true } g ? g.Folders.ToString("N0") : "";
            case "Modified": return e.DateText;
            case "Size": return e.SizeText;
            case "Type": return e.Ext;
            case "Version": return e.Version;
        }
        if (IsHash(key))
        {
            if (e.IsDir) return "";
            if (key == "#CRC-32" && e.Crc is { } stored) return Case(stored.ToString("X8"));
            var value = e.Cell(key);
            return value is { Length: > 0 } ? Case(value) : e.Cell("~" + key) ?? ""; // why not yet: Hashing…, Over 256 MB, Online-only
        }
        return e.Cell(key) ?? "";
    }

    /// A hash as computed (or an archive's stored CRC-32), without the case chosen for display; null when not known.
    internal static string HashValue(Entry e, string key)
    {
        if (e.IsDir || e.IsUp) return null;
        if (key == "#CRC-32" && e.Crc is { } stored) return stored.ToString("X8");
        return e.Cell(key) is { Length: > 0 } value ? value : null;
    }

    /// Sorting by an extended column: dates and counts as values, hashes and text the way names sort.
    internal static int Compare(Entry x, Entry y, string key)
    {
        switch (key)
        {
            case "Created": return x.Created.CompareTo(y.Created);
            case "Accessed": return x.Accessed.CompareTo(y.Accessed);
            case "Files": return ((x as SpaceEntry)?.Files ?? -1).CompareTo((y as SpaceEntry)?.Files ?? -1);
            case "Folders": return ((x as SpaceEntry)?.Folders ?? -1).CompareTo((y as SpaceEntry)?.Folders ?? -1);
        }
        if (IsProperty(key) && x.SortValue(key) is { } a && y.SortValue(key) is { } b && a.GetType() == b.GetType()) return a.CompareTo(b);
        if (IsProperty(key) && (x.SortValue(key) == null) != (y.SortValue(key) == null)) return x.SortValue(key) == null ? -1 : 1;
        string p = Text(x, key), q = Text(y, key);
        return IsHash(key) ? string.CompareOrdinal(p, q) : ExplorerOrder.Instance.Compare(p, q);
    }

    /// The text a Copy <column> or Copy… entry writes for this row: what the column shows, in full.
    internal static string Value(Entry e, string key) => key switch
    {
        "Name" => e.Name,
        "Path" => e.Path,
        "Modified" => e.Modified == default ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
        "Created" => e.Created == default ? "" : e.Created.ToString("yyyy-MM-dd HH:mm:ss"),
        "Accessed" => e.Accessed == default ? "" : e.Accessed.ToString("yyyy-MM-dd HH:mm:ss"),
        "Size" => e.IsDir && e.DisplaySize < 0 ? "" : e.DisplaySize < 0 ? "" : e.DisplaySize.ToString(),
        "Type" => e.IsDir ? "File folder" : Thumbnails.TypeName(e.Name, false),
        "Extension" => e.Ext,
        "Space used" => e.DisplaySize < 0 ? "" : e.DisplaySize.ToString(),
        "Capacity" => e.HasDriveCapacity ? e.DriveTotal.ToString() : "",
        "Free space" => e.HasDriveCapacity ? e.DriveFree.ToString() : "",
        "Filesystem" => e.DriveFormat, "Connection" => e.DriveConnection, "Location" => e.DriveLocation,
        _ when IsHash(key) => HashValue(e, key) is { } hash ? Case(hash) : "",
        _ => Text(e, key),
    };
}
