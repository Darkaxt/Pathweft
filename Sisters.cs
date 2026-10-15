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
        exe = Environment.ProcessPath, edition = "free", paid = false,
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
        Read(file) is { } c && c.TryGetProperty("exe", out var e) && e.ValueKind == JsonValueKind.String && File.Exists(e.GetString()) ? e.GetString() : null;

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
}
