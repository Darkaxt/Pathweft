using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;

namespace FileExplorer;

// Preview copies have an observable internal lifetime. Shell-open/clipboard copies
// remain until the user declares their external consumer finished; launcher exit is insufficient.
internal static class ArchiveContent
{
    internal sealed record Record(string Id, string Source, string Purpose, string[] Files, string[] Directories);
    internal static string Root => Path.Combine(Settings.Dir, "archive-content");
    static readonly ConcurrentDictionary<string, byte> activePreviews = new();
    internal sealed class Lease(Record record) : IDisposable
    {
        int disposed;
        internal Record Record => record;
        internal string Directory => Path.Combine(Root, record.Id);
        internal string File => Path.Combine(Directory, record.Files.Single());
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            activePreviews.TryRemove(record.Id, out _);
            Release(record);
        }
    }
    internal static Lease Extract(string archive, IReadOnlyCollection<string> names, string inner, string purpose)
    {
        string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(Root, id);
        Directory.CreateDirectory(directory);
        try
        {
            string data = Path.Combine(directory, "data"); Directory.CreateDirectory(data);
            Archive.Extract(archive, names, inner, data);
            var record = new Record(id, archive + (inner == "" ? "" : "\\" + inner.Replace('/', '\\')), purpose,
                Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p)).ToArray(),
                Directory.GetDirectories(directory, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(directory, p)).ToArray());
            System.IO.File.WriteAllText(Path.Combine(directory, "lease.json"), JsonSerializer.Serialize(record));
            if (purpose == "preview") activePreviews.TryAdd(id, 0);
            return new(record);
        }
        catch { Directory.Delete(directory, recursive: true); throw; }
    }
    internal static Lease Preview(string path)
    {
        var at = PaneView.ArchiveOf(path) ?? throw new IOException("Not an archive entry.");
        string inner = at.Inner.Contains('/') ? at.Inner[..at.Inner.LastIndexOf('/')] : "";
        return Extract(at.Archive, new[] { Path.GetFileName(path) }, inner, "preview");
    }
    internal static IEnumerable<Record> Retained()
    {
        if (!Directory.Exists(Root)) yield break;
        foreach (string directory in Directory.EnumerateDirectories(Root))
        {
            Record record = null;
            try { record = JsonSerializer.Deserialize<Record>(System.IO.File.ReadAllText(Path.Combine(directory, "lease.json"))); }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            if (record != null && record.Id == Path.GetFileName(directory)) yield return record;
        }
    }
    internal static void Release(Record record)
    {
        if (record.Id.Length != 32 || !record.Id.All(Uri.IsHexDigit)) throw new IOException("Invalid working-copy identity.");
        if (activePreviews.ContainsKey(record.Id)) throw new IOException("Close the internal preview before releasing this copy.");
        string directory = Path.Combine(Root, record.Id);
        if (!Directory.Exists(directory)) return;
        // Prove each known file is currently unlocked before deleting any member.
        // For external consumers this supplements the user's explicit closure action.
        foreach (string name in record.Files)
        {
            string path = Checked(directory, name);
            if (System.IO.File.Exists(path)) using (System.IO.File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        foreach (string name in record.Files) { string path = Checked(directory, name); if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        foreach (string name in record.Directories.OrderByDescending(n => n.Length))
        { string path = Checked(directory, name); if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        if (Directory.EnumerateFileSystemEntries(directory).Any(p => Path.GetFileName(p) != "lease.json"))
            throw new IOException("Unrecognized working-copy files remain; they were preserved with the ownership record.");
        System.IO.File.Delete(Path.Combine(directory, "lease.json"));
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    static string Checked(string directory, string name)
    {
        string path = Path.GetFullPath(Path.Combine(directory, name));
        if (!path.StartsWith(directory + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Working-copy path leaves its owner.");
        if ((System.IO.File.Exists(path) || Directory.Exists(path)) && (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Working-copy member was replaced by a link.");
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(path)); parent != null && parent.FullName.StartsWith(directory, StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Working-copy directory was replaced by a link.");
        return path;
    }
}
