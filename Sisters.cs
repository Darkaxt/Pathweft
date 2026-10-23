using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FileExplorer;

// The Protagonist Labs ecosystem (contract v1, vault Proiecte/Ecosistem.md): every app leaves a card in
// %AppData%\ProtagonistLabs\apps\<id>.json at each start, sisters find each other through those cards,
// and File Labs shares its Quick access folders with Lume as data\filelabs.items.json.
static class Sisters
{
    static readonly string Root = Path.Combine(Settings.Profile ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtagonistLabs");
    static string Now => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    // the paid apps and their page names, for the discount line in Works with
    internal static readonly (string Id, string Name)[] Paid =
        { ("shelf", "Shelf"), ("stash", "Stash"), ("frostpane", "Frostpane"), ("reel", "Reel"), ("planner", "Planner"), ("reminder", "Reminder") };

    internal static void WriteCard() => Write(Path.Combine(Root, "apps", "filelabs.json"), new
    {
        schema = 1, updated = Now,
        id = "filelabs", name = "File Labs", version = typeof(App).Assembly.GetName().Version?.ToString(3),
        exe = Environment.ProcessPath, data = Settings.Dir, edition = "free", paid = false,
        page = "https://protagonistlabs.app/filelabs/",
        commands = new Dictionary<string, string[]> { ["open"] = new[] { "{path}" } }, // a folder → new tab (single instance)
    });

    // Quick access folders as Lume launcher entries; running one opens it in File Labs.
    internal static void WriteItems(List<string> folders) => Write(Path.Combine(Root, "data", "filelabs.items.json"), new
    {
        schema = 1, updated = Now,
        items = folders.Select(f => new
        {
            title = Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f, subtitle = f, kind = "folder",
            run = new { exe = Environment.ProcessPath, args = new[] { f } },
        }),
    });

    // UTF-8 without BOM, through a .tmp so a reader never sees half a file. Runs off the UI thread; a
    // locked or full disk only leaves the previous file in place.
    internal static void Write(string path, object body)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// A sister's exe from its card, or null when the card is missing, unreadable, not schema 1, or names an exe that is gone.
    internal static string Exe(string id) => ExeFromCard(Path.Combine(Root, "apps", id + ".json"));

    internal static string ExeFromCard(string file) =>
        Read(file) is { } c && c.TryGetProperty("exe", out var e) && e.ValueKind == JsonValueKind.String && Local(e.GetString()) && File.Exists(e.GetString()) ? e.GetString() : null;

    /// A path on a local drive, judged from the string alone before anything touches the disk: no \\server\share,
    /// no \\?\ or \\.\ device path, no mapped network drive. A card or data file is another program's word, and a
    /// File.Exists on \\host\x would make Windows offer the user's NTLM hash to that host.
    internal static bool Local(string path) =>
        path is { Length: >= 3 and <= 1024 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
        && path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && new DriveInfo(path[..1]).DriveType is not (DriveType.Network or DriveType.NoRootDirectory);

    /// True when some paid sister is installed as the bought ("full") edition.
    internal static bool OwnsPaid() => Paid.Any(p =>
        Read(Path.Combine(Root, "apps", p.Id + ".json")) is { } c && Exe(p.Id) != null
        && c.TryGetProperty("paid", out var paid) && paid.ValueKind == JsonValueKind.True
        && c.TryGetProperty("edition", out var ed) && ed.ValueKind == JsonValueKind.String && ed.GetString() == "full");

    static JsonElement? Read(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var c = doc.RootElement;
            return c.ValueKind == JsonValueKind.Object && c.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.Number
                && s.TryGetInt32(out var v) && v == 1 ? c.Clone() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    // ---- Reel (1.13.0): data\reel.files.json lists every video file in its library, one entry per file:
    // {path, title, year?, poster?, watched, lastWatched?, series?, season?, episode?}, capped by Reel at 20,000
    // (~330 bytes each, ~6.6 MB). The preview puts what it says above a video Reel knows (QuickView.ReelHeader).

    internal sealed record ReelFile(string Title, int? Year, string Poster, bool Watched, DateTime? LastWatched, string Series, int? Season, int? Episode);
    sealed record ReelCache(string File, DateTime Stamp, long Length, Dictionary<string, ReelFile> Map);
    static ReelCache reelCache; // one reference, swapped whole: lookups run on worker threads
    internal const int ReelFilesMaxBytes = 16 << 20, ReelFilesMax = 20_000;
    // Reel imports every poster into its own data folder; a poster anywhere else is not Reel's. That folder is
    // %AppData%\ProtagonistLabs\Reel from ecosystem v3 and was Electron's %AppData%\reel before: both count while users update.
    static readonly string AppData = Settings.Profile ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    internal static readonly string[] ReelPosters =
        { Path.Combine(AppData, "ProtagonistLabs", "Reel", "posters") + "\\", Path.Combine(AppData, "reel", "posters") + "\\" };

    /// What Reel knows about this video, or null: Reel not installed (card + exe), its list missing or not one, or the
    /// video not in it. Reads the disk (the list once per change of the file): call it off the UI thread.
    internal static ReelFile Reel(string video) =>
        Exe("reel") != null && ReelFiles(Path.Combine(Root, "data", "reel.files.json"), ReelPosters) is { } map && map.TryGetValue(video, out var f) ? f : null;

    /// The list by full path (case-insensitive), cached by the file's time and size; null when it isn't a usable list.
    internal static Dictionary<string, ReelFile> ReelFiles(string file, params string[] posters)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > ReelFilesMaxBytes) return null;
            if (reelCache is { } c && c.File == file && c.Stamp == info.LastWriteTimeUtc && c.Length == info.Length) return c.Map;
            var bytes = File.ReadAllBytes(file);
            Dictionary<string, ReelFile> map;
            try { map = ParseReelFiles(bytes.AsMemory(bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0), posters); }
            catch (JsonException) { map = null; } // cached too, so a broken file is not parsed again at every video
            reelCache = new(file, info.LastWriteTimeUtc, info.Length, map);
            return map;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // Every field checked: an entry without a usable path, title or watched is skipped, a bad optional field dropped.
    static Dictionary<string, ReelFile> ParseReelFiles(ReadOnlyMemory<byte> json, string[] posters)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var s) || s.ValueKind != JsonValueKind.Number
            || !s.TryGetInt32(out var v) || v != 1 || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return null;
        var map = new Dictionary<string, ReelFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.EnumerateArray().Take(ReelFilesMax))
        {
            if (f.ValueKind != JsonValueKind.Object || Text(f, "path", 1024) is not { } path || !Path.IsPathFullyQualified(path)
                || Text(f, "title", 300) is not { } title || !f.TryGetProperty("watched", out var w) || w.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) continue;
            var poster = Text(f, "poster", 1024);
            if (poster != null && !(Local(poster) && Path.GetFullPath(poster) is var full && posters.Any(p => full.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                                    && Path.GetExtension(poster).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp")) poster = null;
            DateTime? last = Text(f, "lastWatched", 40) is { } d && DateTimeOffset.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.LocalDateTime : null;
            map.TryAdd(path, new ReelFile(title, Number(f, "year", 1870, 2100), poster, w.GetBoolean(), last,
                Text(f, "series", 300), Number(f, "season", 0, 999), Number(f, "episode", 0, 999)));
        }
        return map;
    }

    static string Text(JsonElement o, string name, int max) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s && s.Length <= max && !s.Any(char.IsControl) ? s : null;

    static int? Number(JsonElement o, string name, int min, int max) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) && n >= min && n <= max ? n : null;
}
