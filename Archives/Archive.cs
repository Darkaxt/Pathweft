using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using SharpSevenZip;
using SharpSevenZip.Exceptions;
using NativeCompressionMode = SharpSevenZip.CompressionMode;

namespace FileExplorer;

// Native 7-Zip owns container IO. Pathweft owns virtual paths, verification and publication.
internal static partial class Archive
{
    internal sealed record Raw(int Index, string Path, bool IsDir, long Size, DateTime Modified, bool Encrypted = false, FileAttributes Attributes = 0);
    public sealed record Item(string Name, bool IsDir, long Size, DateTime Modified, FileAttributes Attributes = 0);
    static readonly Lazy<bool> native = new(() =>
    {
        SharpSevenZipBase.SetLibraryPath(System.IO.Path.Combine(AppContext.BaseDirectory, "SevenZip", "7z.dll"));
        return true;
    });
    static readonly ConcurrentDictionary<string, string> passwords = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".7z", ".zip", ".jar", ".nupkg", ".apk", ".vsix", ".whl", ".tar", ".tgz", ".gz", ".bz2", ".xz", ".lzma", ".zst", ".rar", ".cab", ".iso", ".wim", ".arj", ".cpio", ".deb", ".rpm", ".xar", ".dmg", ".001" };
    public static bool IsArchiveName(string path) => extensions.Contains(System.IO.Path.GetExtension(path));
    static bool TarGzip(string path) => path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    static bool Rar(string path) => System.IO.Path.GetExtension(path).Equals(".rar", StringComparison.OrdinalIgnoreCase);
    static OutArchiveFormat Format(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".7z" => OutArchiveFormat.SevenZip, ".tar" => OutArchiveFormat.Tar,
        ".zip" or ".jar" or ".nupkg" or ".apk" or ".vsix" or ".whl" => OutArchiveFormat.Zip,
        _ => throw new IOException("This container is read-only in Pathweft. Extract its entries to edit them.")
    };
    public static bool CanChange(string archive)
    {
        return ChangeReason(archive) == "";
    }
    internal static string ChangeReason(string archive)
    {
        try
        {
            if ((File.GetAttributes(archive) & FileAttributes.ReadOnly) != 0) return "This archive file is read-only.";
            var entries = Read(archive);
            if (Rar(archive)) return RarNative.ChangeReason(archive, passwords.GetValueOrDefault(archive));
            _ = Format(archive);
            return passwords.ContainsKey(archive) || entries.Any(e => e.Encrypted) ? "Encrypted containers of this format are read-only." : "";
        }
        catch (IOException ex) { return ex.Message; }
    }
    internal static void SetPassword(string archive, string password)
    { if (string.IsNullOrEmpty(password)) passwords.TryRemove(archive, out _); else passwords[archive] = password; }
    internal static void ForgetPasswords() => passwords.Clear();
    static SharpSevenZipExtractor Open(string path)
    {
        _ = native.Value;
        try { return new SharpSevenZipExtractor(path, passwords.GetValueOrDefault(path, "")); }
        catch (SharpSevenZipException ex) { throw new IOException("Archive unavailable: unsupported/corrupt data, a missing volume or a password may be required.", ex); }
    }

    internal static string Clean(string path)
    {
        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        if (path == "." || path == "") return "";
        if (path.StartsWith('/') || path.Contains(':')) throw new IOException("Absolute paths and alternate streams are not valid archive entry names.");
        path = path.TrimEnd('/');
        foreach (string part in path.Split('/'))
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9')
                throw new IOException("Unsafe or ambiguous archive entry name: " + path);
        }
        return path;
    }
    static bool Under(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
    static string Prefix(string inner) => Clean(inner) is { Length: > 0 } name ? name + "/" : "";

    internal static List<Raw> Read(string archive)
    {
        if (archive.EndsWith(".001", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Split-volume containers are not supported for virtual browsing. Use a complete single-file archive.");
        var result = new List<Raw>();
        if (TarGzip(archive))
            VisitTarGzip(archive, (name, dir, size, time, stream) => result.Add(new(result.Count, name, dir, size, time)));
        else
        {
            using var reader = Open(archive);
            try
            {
                foreach (var entry in reader.ArchiveFileData)
                {
                    var name = Clean(entry.FileName);
                    if (name == "") continue;
                    uint attributes = entry.Attributes;
                    if ((attributes & (uint)FileAttributes.ReparsePoint) != 0 || ((attributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("Links in archives are not extracted or edited: " + name);
                    result.Add(new(entry.Index, name, entry.IsDirectory, checked((long)entry.Size), entry.LastWriteTime, entry.Encrypted, (FileAttributes)(attributes & 0xFFFF)));
                }
            }
            catch (SharpSevenZipException ex) { throw new IOException("Cannot read archive metadata; enter its password or check its format/volumes.", ex); }
        }
        var names = new Dictionary<string, Raw>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result)
            if (!names.TryAdd(entry.Path, entry)) throw new IOException("Duplicate or case-ambiguous archive entry: " + entry.Path);
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result)
        {
            string full = "";
            foreach (string component in entry.Path.Split('/'))
            {
                full = full == "" ? component : full + "/" + component;
                if (spelling.TryGetValue(full, out string prior) && prior != full) throw new IOException("Case-ambiguous archive directory: " + full);
                spelling.TryAdd(full, full);
            }
            string parent = entry.Path;
            while (parent.LastIndexOf('/') is var slash && slash >= 0)
            {
                parent = parent[..slash];
                if (names.TryGetValue(parent, out var ancestor) && !ancestor.IsDir)
                    throw new IOException("A file is also used as an archive directory: " + parent);
            }
        }
        return result;
    }
    static void VisitTarGzip(string archive, Action<string, bool, long, DateTime, Stream> each)
    {
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var tar = new System.Formats.Tar.TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            bool dir = entry.EntryType == System.Formats.Tar.TarEntryType.Directory;
            if (!dir && entry.EntryType is not (System.Formats.Tar.TarEntryType.RegularFile or System.Formats.Tar.TarEntryType.V7RegularFile or System.Formats.Tar.TarEntryType.ContiguousFile))
                throw new IOException("Unsupported TAR link or special entry: " + entry.Name);
            string name = Clean(entry.Name); if (name == "") continue;
            each(name, dir, entry.Length, entry.ModificationTime.LocalDateTime, entry.DataStream);
        }
    }
    public static List<Item> List(string archive, string inner)
    {
        string prefix = Prefix(inner);
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Read(archive))
        {
            if (!entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.Path.Length == prefix.Length) continue;
            var rest = entry.Path[prefix.Length..]; int slash = rest.IndexOf('/');
            if (slash >= 0) { var name = rest[..slash]; items.TryAdd(name, new(name, true, -2, entry.Modified)); }
            else items[rest] = new(rest, entry.IsDir, entry.IsDir ? -2 : entry.Size, entry.Modified, entry.Attributes);
        }
        return items.Values.ToList();
    }
    internal static string Fingerprint(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    static void Check(Job job)
    {
        if (job == null) return;
        job.WaitUntilResumed();
        if (job.IsCancelled) throw new OperationCanceledException();
    }
    static string Target(string directory, string entry)
    {
        string root = System.IO.Path.GetFullPath(directory), path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, Clean(entry).Replace('/', '\\')));
        if (!path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Entry leaves its selected destination.");
        for (var parent = new DirectoryInfo(System.IO.Path.GetDirectoryName(path)); parent != null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Extraction through directory links is not supported.");
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Extraction over a link is not supported.");
        return path;
    }
    sealed class CheckedOutput(Stream output, long maximum, Job job) : Stream
    {
        long written;
        public override void Write(byte[] buffer, int offset, int count)
        { Check(job); if (count > maximum - written) throw new IOException("Entry exceeds its declared size."); output.Write(buffer, offset, count); written += count; }
        public override void Flush() => output.Flush();
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => written; public override long Position { get => written; set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }
    internal static void CopyEntry(string archive, Raw entry, Stream output, Job job = null)
    {
        Check(job);
        using var bounded = new CheckedOutput(output, entry.Size, job);
        if (TarGzip(archive))
            VisitTarGzip(archive, (name, dir, size, time, stream) => { if (name == entry.Path && !dir) stream.CopyTo(bounded); });
        else
        {
            using var reader = Open(archive);
            try { reader.ExtractFile(entry.Index, bounded); }
            catch (SharpSevenZipException ex) { Check(job); throw new IOException("Cannot extract the selected entry; check its password and archive integrity.", ex); }
        }
        if (bounded.Length != entry.Size) throw new IOException("Extracted size does not match the selected entry.");
    }
    internal static string EntryHash(string archive, Raw entry, Job job = null)
    {
        if (entry.IsDir) return "directory";
        using var hash = SHA256.Create();
        using var sink = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        CopyEntry(archive, entry, sink, job); sink.FlushFinalBlock(); return Convert.ToHexString(hash.Hash);
    }
    public static (int Written, int Skipped) Extract(string archive, IReadOnlyCollection<string> names, string inner, string dest) => ExtractTo(archive, names, inner, dest, Choice.Skip, null);
    internal static (int Written, int Skipped) ExtractTo(string archive, IReadOnlyCollection<string> names, string inner, string dest, Choice choice, Job job)
    {
        string prefix = Prefix(inner); var picked = names.Select(n => prefix + Clean(n)).ToArray();
        var entries = Read(archive).Where(e => picked.Any(p => Under(e.Path, p))).ToArray();
        // Validate all destinations before the first write; recheck immediately before each publication.
        foreach (var entry in entries) _ = Target(dest, entry.Path[prefix.Length..]);
        int written = 0, skipped = 0;
        foreach (var entry in entries)
        {
            Check(job); string target = Target(dest, entry.Path[prefix.Length..]);
            if (entry.IsDir) { Directory.CreateDirectory(target); continue; }
            if (File.Exists(target))
            {
                if (choice == Choice.Skip || choice == Choice.Newer && File.GetLastWriteTime(target) >= entry.Modified) { skipped++; continue; }
                if (choice == Choice.Rename) target = Unique(target);
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            string temporary = target + ".pathweft-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { CopyEntry(archive, entry, output, job); output.Flush(true); }
                if (Fingerprint(temporary) != EntryHash(archive, entry, job)) throw new IOException("Extracted content verification failed.");
                Check(job); _ = Target(dest, entry.Path[prefix.Length..]);
                File.Move(temporary, target, choice != Choice.Rename && choice != Choice.Skip);
                File.SetLastWriteTime(target, entry.Modified); written++;
                if (job != null) { job.DoneFiles++; job.AddBytes(entry.Size); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return (written, skipped);
    }
    static string Unique(string path)
    {
        string parent = System.IO.Path.GetDirectoryName(path), stem = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        int n = 2; string result = path;
        while (File.Exists(result) || Directory.Exists(result)) result = System.IO.Path.Combine(parent, stem + " (" + n++ + ")" + ext);
        return result;
    }
    static SharpSevenZipCompressor Compressor(string archive, string temporaryDirectory, Job job)
    {
        _ = native.Value;
        var compressor = new SharpSevenZipCompressor(temporaryDirectory) { ArchiveFormat = Format(archive), CompressionLevel = SharpSevenZip.CompressionLevel.Normal };
        compressor.CustomParameters["mt"] = "2";
        compressor.FileCompressionStarted += (_, e) => { job?.WaitUntilResumed(); e.Cancel = job?.IsCancelled == true; };
        return compressor;
    }
#if DEBUG
    internal static Action<string> VerificationBoundary = null;
#endif
    static void Boundary(string phase)
    {
#if DEBUG
        VerificationBoundary?.Invoke(phase);
#endif
    }
    internal static Dictionary<string, string> Files(IEnumerable<string> paths, string inner)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); string prefix = Prefix(inner);
        void Add(string path, string name)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive writes do not follow links: " + path);
            name = Clean(name);
            if (!result.TryAdd(name, (attributes & FileAttributes.Directory) != 0 ? null : path)) throw new IOException("Duplicate input name: " + name);
            if ((attributes & FileAttributes.Directory) != 0)
                foreach (string child in Directory.EnumerateFileSystemEntries(path)) Add(child, name + "/" + System.IO.Path.GetFileName(child));
        }
        foreach (string path in paths) Add(path, prefix + System.IO.Path.GetFileName(path.TrimEnd('\\')));
        return result;
    }
    internal static void Edit(string archive, Dictionary<int, string> changes, Dictionary<string, string> additions, Job job = null, string expectedOriginal = null, Action validateSources = null)
    {
        Check(job); if (!Rar(archive)) _ = Format(archive);
        if ((File.GetAttributes(archive) & FileAttributes.ReadOnly) != 0) throw new IOException("This archive file is read-only.");
        string original = Fingerprint(archive); var before = Read(archive);
        if (expectedOriginal != null && original != expectedOriginal) throw new IOException("The destination changed after transfer planning; no archive edit was published.");
        if (ChangeReason(archive) is { Length: > 0 } reason) throw new IOException(reason);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in before)
        {
            string name = changes.TryGetValue(entry.Index, out var replacement) ? replacement : entry.Path;
            if (name != null && !expected.TryAdd(Clean(name), EntryHash(archive, entry, job))) throw new IOException("The edit would create duplicate entry names.");
        }
        var inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in additions)
        {
            string hash = entry.Value == null ? "directory" : Fingerprint(entry.Value);
            expected.Add(Clean(entry.Key), hash);
            if (entry.Value != null) inputHashes[entry.Value] = hash;
        }
        string temporaryDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(archive), ".pathweft-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string stage = System.IO.Path.Combine(temporaryDirectory, "stage" + System.IO.Path.GetExtension(archive));
        try
        {
            if (Rar(archive))
            {
                RarNative.Write(archive, stage, temporaryDirectory, passwords.GetValueOrDefault(archive),
                    changes.ToDictionary(c => before.Single(e => e.Index == c.Key).Path, c => c.Value), additions, job);
                if (passwords.TryGetValue(archive, out string password)) passwords[stage] = password;
            }
            else
            {
                File.Copy(archive, stage);
                var compressor = Compressor(archive, temporaryDirectory, job);
                if (changes.Count > 0) compressor.ModifyArchive(stage, changes);
                if (additions.Count > 0) { compressor.CompressionMode = NativeCompressionMode.Append; compressor.CompressFileDictionary(additions, stage); }
            }
            Check(job); Boundary("stage-written");
            if (job != null) job.Phase = Phase.Verifying;
            var actual = Read(stage);
            if (actual.Count != expected.Count || actual.Any(e => !expected.TryGetValue(e.Path, out var hash) || hash != EntryHash(stage, e, job))) throw new IOException("Staged archive verification failed; original preserved.");
            foreach (var input in inputHashes) if (Fingerprint(input.Key) != input.Value) throw new IOException("An input changed while the archive was written; original preserved.");
            Boundary("before-publish"); Check(job);
            validateSources?.Invoke();
            using var publication = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (Convert.ToHexString(SHA256.HashData(publication)) != original) throw new IOException("The archive changed concurrently; staged edit was not published.");
            File.Replace(stage, archive, null);
        }
        catch (SharpSevenZipException ex) { Check(job); throw new IOException("Native archive update failed; original preserved.", ex); }
        finally { passwords.TryRemove(stage, out _); Directory.Delete(temporaryDirectory, recursive: true); }
    }
    public static void Delete(string archive, IReadOnlyCollection<string> names, string inner, Job job = null)
    {
        string prefix = Prefix(inner); var picked = names.Select(n => prefix + Clean(n)).ToArray();
        Edit(archive, Read(archive).Where(e => picked.Any(p => Under(e.Path, p))).ToDictionary(e => e.Index, _ => (string)null), new(), job);
    }
    public static (int Added, int Skipped) Add(string archive, string inner, IEnumerable<string> paths)
    {
        var additions = Files(paths, inner); var have = Read(archive).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int skipped = additions.Keys.Count(have.Contains);
        foreach (string name in additions.Keys.Where(have.Contains).ToArray()) additions.Remove(name);
        if (additions.Count > 0) Edit(archive, new(), additions);
        return (additions.Count, skipped);
    }
    internal static void Rename(string archive, string inner, string name, string replacement, Job job = null)
    {
        string from = Prefix(inner) + Clean(name), to = Prefix(inner) + Clean(replacement);
        if (replacement.Contains('/') || replacement.Contains('\\')) throw new IOException("Rename accepts one entry name.");
        var selected = Read(archive).Where(e => Under(e.Path, from)).ToArray();
        if (selected.Length == 0) throw new IOException("The selected entry no longer exists.");
        Edit(archive, selected.ToDictionary(e => e.Index, e => to + e.Path[from.Length..]), new(), job);
    }
    internal static void Create(string archive, Dictionary<string, string> files, Job job = null)
    {
        Check(job);
        if (File.Exists(archive)) throw new IOException("Archive already exists.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archive));
        string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(archive), ".pathweft-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string stage = System.IO.Path.Combine(directory, "stage" + System.IO.Path.GetExtension(archive));
        try
        {
            var expected = files.ToDictionary(e => Clean(e.Key), e => e.Value == null ? "directory" : Fingerprint(e.Value), StringComparer.OrdinalIgnoreCase);
            if (job != null)
            {
                job.TotalFiles = files.Count(e => e.Value != null); job.TotalBytes = files.Where(e => e.Value != null).Sum(e => new FileInfo(e.Value).Length);
                job.Phase = Phase.Copying;
            }
            if (Rar(archive)) RarNative.Write(null, stage, directory, null, new(), files.ToDictionary(e => Clean(e.Key), e => e.Value), job);
            else
            {
                var compressor = Compressor(archive, directory, job);
                compressor.CompressFileDictionary(files, stage);
            }
            Boundary("stage-written"); Check(job);
            if (job != null) job.Phase = Phase.Verifying;
            var actual = Read(stage);
            if (actual.Count != expected.Count || actual.Any(e => !expected.TryGetValue(e.Path, out var hash) || hash != EntryHash(stage, e, job))) throw new IOException("New archive content verification failed.");
            foreach (var file in files.Where(e => e.Value != null)) if (Fingerprint(file.Value) != expected[Clean(file.Key)]) throw new IOException("An input changed while the archive was created.");
            Boundary("before-publish"); Check(job);
            File.Move(stage, archive);
            if (job != null) { job.DoneFiles = job.TotalFiles; job.AddBytes(job.TotalBytes); }
        }
        catch (SharpSevenZipException ex) { Check(job); throw new IOException("Native archive creation failed.", ex); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
