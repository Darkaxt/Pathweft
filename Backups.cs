using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileExplorer;

// Backups made by two sister apps (1.0.40), told apart by their first bytes, never by the name alone:
//   Stash        Stash-<date>.stash (2.1; before: Stash-<date>.zip, Thunderbird-Backup-<date>.zip): a zip with a
//                manifest.json, or "STASHENC" + AES when it has a password
//   Backup Labs  <stamp>.bkl (1.3.0; before: plain <stamp>.zip): a zip of C/Users/..., UNC/server/share/...
//                (drive letter first, no manifest), or "BKLENC1\0" + AES when it has a password
// The files come from anywhere: every read is capped, and one that isn't what it claims is "could not read"
// (its own extension) or an ordinary file (a .zip), never an exception.
static class Backups
{
    internal sealed class Info
    {
        public string Id, App;                   // "stash" / "Stash", "backuplabs" / "Backup Labs"
        public bool Encrypted, Unreadable;
        public DateTime? Made;                   // from the manifest or the file name
        public List<(string Name, string Value)> Rows = new();
    }

    static readonly Regex OldStash = new(@"^(Stash|Thunderbird-Backup)-\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d\.zip$", RegexOptions.IgnoreCase);
    static readonly Regex OldBackupLabs = new(@"^\d{4}-\d\d-\d\d_\d{6}\.zip$", RegexOptions.IgnoreCase);
    static readonly Regex InProfile = new(@"^Thunderbird/(.+?)/(?:Mail|ImapMail)/([^/]+)/(.+)$");  // Stash's own rule (MailStore.Folders)
    static readonly Regex Outside = new(@"^External/(?:Thunderbird/)?(\d+)/(.+)$");

    /// Worth a look: the apps' own extensions, or a .zip named the way they named backups. Every other file
    /// (an ordinary zip included) costs nothing and behaves as before.
    internal static bool Named(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name);
        return ext.Equals(".stash", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bkl", StringComparison.OrdinalIgnoreCase)
            || OldStash.IsMatch(name) || OldBackupLabs.IsMatch(name);
    }

    /// What the file is, or null when it is not a backup of either app. Without `details` only the first bytes
    /// are read (and a .zip's list of entries); with them, what is inside, stopping when `stale` says so.
    internal static Info Inspect(string path, bool details = false, Func<bool> stale = null)
    {
        if (!Named(path)) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var own = ext == ".stash" ? New("stash", path) : ext == ".bkl" ? New("backuplabs", path) : null;
        try
        {
            var head = new byte[8];
            int n;
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = f.ReadAtLeast(head, 8, throwOnEndOfStream: false);
            if (n == 8 && head.AsSpan().SequenceEqual("STASHENC"u8)) return Encrypted(New("stash", path));
            if (n == 8 && head.AsSpan().SequenceEqual("BKLENC1\0"u8)) return Encrypted(New("backuplabs", path));
            bool zip = n >= 4 && head[0] == 'P' && head[1] == 'K' && (head[2] == 3 && head[3] == 4 || head[2] == 5 && head[3] == 6);
            if (!zip) return Unreadable(own);
            if (own != null && !details) return own;

            using var z = ZipFile.OpenRead(path);
            if (own?.Id != "backuplabs" && Manifest(z) is { } manifest) return details ? Stash(New("stash", path), z, manifest, stale ?? (() => false)) : New("stash", path);
            if (own?.Id == "backuplabs" || ext == ".zip" && OldBackupLabs.IsMatch(Path.GetFileName(path)) && z.Entries.Count > 0 && z.Entries.All(e => Root(e.FullName) != null))
                return details ? BackupLabs(New("backuplabs", path), z) : New("backuplabs", path);
            return Unreadable(own); // a .stash without a manifest; a .zip that only looks like one by name is null
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or JsonException)
        {
            return Unreadable(own);
        }
    }

    static Info New(string id, string path)
    {
        var name = Path.GetFileName(path);
        var stamp = Regex.Match(name, @"\d{4}-\d\d-\d\d_(\d\d-\d\d-\d\d|\d{6})");
        var formats = new[] { "yyyy-MM-dd_HH-mm-ss", "yyyy-MM-dd_HHmmss" };
        return new Info
        {
            Id = id, App = id == "stash" ? "Stash" : "Backup Labs",
            Made = stamp.Success && DateTime.TryParseExact(stamp.Value, formats, null, System.Globalization.DateTimeStyles.None, out var d) ? d : null,
        };
    }

    static Info Encrypted(Info i) { i.Encrypted = true; return i; }
    static Info Unreadable(Info i) { if (i != null) i.Unreadable = true; return i; }

    // Stash's manifest: an object with a Format number and an Apps object. Capped: it lists every file's
    // SHA-256 (about 150 bytes a file), so 32 MB is some 200 000 files.
    static JsonElement? Manifest(ZipArchive z)
    {
        if (z.GetEntry("manifest.json") is not { } entry || entry.Length > 32 << 20) return null;
        using var s = entry.Open();
        var bytes = new MemoryStream();
        s.CopyTo(bytes);
        if (bytes.Length > 32 << 20) return null; // the stated length can lie
        using var doc = JsonDocument.Parse(bytes.ToArray());
        var m = doc.RootElement;
        return m.ValueKind == JsonValueKind.Object && m.TryGetProperty("Format", out var f) && f.ValueKind == JsonValueKind.Number
            && m.TryGetProperty("Apps", out var a) && a.ValueKind == JsonValueKind.Object ? m.Clone() : null;
    }

    // Counting stops after this much mail per folder and this long for the whole backup, and the count becomes
    // "≥ n": a folder is read through deflate at ~100 MB/s, and the preview follows the selection.
    internal const long FolderCap = 64L << 20;
    internal static readonly TimeSpan CountTime = TimeSpan.FromSeconds(3);

    static Info Stash(Info i, ZipArchive z, JsonElement m, Func<bool> stale)
    {
        if (m.TryGetProperty("Created", out var c) && c.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(c.GetString(), out var made)) i.Made = made.LocalDateTime;
        foreach (var app in m.GetProperty("Apps").EnumerateObject().Take(10))
        {
            var profiles = Profiles(z, app.Name);
            i.Rows.Add((Clip(app.Name), profiles.Count == 0 ? "" : (profiles.Count == 1 ? "profile " : "profiles ") + string.Join(", ", profiles)));
        }
        var files = z.Entries.Where(e => e.Name != "" && e.FullName != "manifest.json").ToList();
        i.Rows.Add(("Files", $"{files.Count:N0} · {MainWindow.Fmt(files.Sum(e => e.Length))}"));

        var names = new HashSet<string>(z.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
        var clock = Stopwatch.StartNew();
        foreach (var e in files)
        {
            if (stale()) break;
            string file = e.Name, account, folder;
            if (file.EndsWith(".msf", StringComparison.OrdinalIgnoreCase) || !names.Contains(e.FullName + ".msf") && file.Contains('.')) continue;
            if (InProfile.Match(e.FullName) is { Success: true } p) { account = p.Groups[2].Value; folder = p.Groups[3].Value; }
            else if (Outside.Match(e.FullName) is { Success: true } o) { account = "Mail outside the profile"; folder = o.Groups[2].Value; }
            else continue;
            string count;
            if (clock.Elapsed > CountTime) count = "not counted";
            else
            {
                using var s = e.Open();
                var (found, whole) = CountMessages(s, FolderCap, stale);
                count = (whole ? "" : "≥ ") + $"{found:N0} message{(found == 1 && whole ? "" : "s")}";
            }
            i.Rows.Add((Clip($"{account} / {folder.Replace(".sbd/", "/")}"), count));
        }
        return i;
    }

    // the profile names in <app>/profiles.ini (Name= lines), from at most its first 64 KB
    static List<string> Profiles(ZipArchive z, string app)
    {
        if (z.GetEntry(app + "/profiles.ini") is not { } ini) return new();
        using var s = ini.Open();
        var buf = new byte[64 << 10];
        var text = Encoding.UTF8.GetString(buf, 0, s.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false));
        return text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                   .Select(l => Clip(l[5..])).Distinct().Take(10).ToList();
    }

    /// Messages in an mbox stream: lines starting "From " that are the first line or follow an empty one, the rule
    /// Stash splits folders by (RFC 4155). Reads 1 MB at a time; stops after `cap` bytes or when `stale` says so,
    /// and then says the count is not the whole folder.
    internal static (long Found, bool Whole) CountMessages(Stream s, long cap, Func<bool> stale)
    {
        var buf = new byte[1 << 20];
        long found = 0, read = 0;
        int col = 0, got;
        bool afterBlank = true, from = true, blank = true;
        while ((got = s.Read(buf, 0, buf.Length)) > 0)
        {
            for (int k = 0; k < got; k++)
            {
                byte b = buf[k];
                if (b == '\n') { afterBlank = blank; col = 0; from = blank = true; continue; }
                if (col < 5) { from &= b == "From "[col]; if (col == 4 && from && afterBlank) found++; }
                if (b != '\r') blank = false;
                col++;
            }
            read += got;
            if (read >= cap || stale()) return (found, false);
        }
        return (found, true);
    }

    // Backup Labs keeps no manifest; what it backed up is where the files came from: per drive (or share), the
    // deepest folder they all share.
    static Info BackupLabs(Info i, ZipArchive z)
    {
        var files = z.Entries.Where(e => e.Name != "").ToList();
        i.Rows.Add(("Files", $"{files.Count:N0} · {MainWindow.Fmt(files.Sum(e => e.Length))}"));
        foreach (var group in z.Entries.Where(e => Root(e.FullName) != null).GroupBy(e => Root(e.FullName), StringComparer.OrdinalIgnoreCase).Take(20))
        {
            // a folder entry ("C/x/empty/") is itself, a file is its folder
            var common = group.Select(e => e.Name == "" ? e.FullName.TrimEnd('/') : e.FullName[..e.FullName.LastIndexOf('/')]).Aggregate(Shared);
            i.Rows.Add(("From", Clip(Original(common), 120)));
        }
        return i;
    }

    // "C/..." or "UNC/server/share/...": the drive or share an entry came from, or null
    static string Root(string entry)
    {
        var m = Regex.Match(entry, @"^(?:[A-Za-z]|UNC/[^/]+/[^/]+)(?=/|$)");
        return m.Success ? m.Value : null;
    }

    static string Shared(string a, string b)
    {
        var x = a.Split('/'); var y = b.Split('/');
        int k = 0;
        while (k < x.Length && k < y.Length && x[k].Equals(y[k], StringComparison.OrdinalIgnoreCase)) k++;
        return string.Join('/', x.Take(k));
    }

    // C/Users/x → C:\Users\x, UNC/server/share/x → \\server\share\x
    static string Original(string rel) =>
        rel.StartsWith("UNC/", StringComparison.OrdinalIgnoreCase) ? @"\\" + rel[4..].Replace('/', '\\')
        : rel.Length == 1 ? rel + @":\" : rel[0] + ":" + rel[1..].Replace('/', '\\');

    static string Clip(string s, int max = 60) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
