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
    internal sealed record Raw(int Index, string Path, bool IsDir, long Size, DateTime Modified, bool Encrypted = false, FileAttributes Attributes = 0, uint? Crc = null,
        string Method = null, DateTime Created = default, string Comment = null);
    // Crc: the container's stored CRC32 (no extraction); null for folders, TAR/GZip and entries without one.
    public sealed record Item(string Name, bool IsDir, long Size, DateTime Modified, FileAttributes Attributes = 0, uint? Crc = null);
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
            // a nested archive is written back into the one holding it, so that one decides
            if (OwnerOf(archive) is { } nest && ChangeReason(Real(nest.Outer)) is { Length: > 0 } outerReason) return outerReason;
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
    internal static bool HasPassword(string archive) => passwords.ContainsKey(archive);
    /// Whether the archive's file list itself is encrypted (7z with encrypted headers, RAR with encrypted file
    /// names), read from its headers as 7-Zip finds out before asking for a password on opening. A damaged or
    /// foreign file is not: it reports that it cannot be opened instead of asking for a password.
    internal static bool HidesNames(string archive)
    {
        try
        {
            using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = new byte[32];
            int read = file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
            ReadOnlySpan<byte> head = start.AsSpan(0, read);
            if (read == 32 && head.StartsWith(stackalloc byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
            {
                // start header: next header offset and size; an encoded next header whose coder is AES hides the names
                long offset = BitConverter.ToInt64(start, 12), size = BitConverter.ToInt64(start, 20);
                if (offset < 0 || size <= 0 || size > 1 << 20 || 32 + offset + size > file.Length) return false;
                var header = new byte[size]; file.Position = 32 + offset; file.ReadExactly(header);
                return header[0] == 0x17 && header.AsSpan().IndexOf(stackalloc byte[] { 0x06, 0xF1, 0x07, 0x01 }) >= 0;
            }
            if (read >= 14 && head.StartsWith(stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }))
            {
                // RAR5: header CRC32, then size and type; an archive encryption header (type 4) comes first
                int at = 12; _ = Vint(head, ref at);
                return at < read && Vint(head, ref at) == 4;
            }
            if (read >= 11 && head.StartsWith(stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }))
                return start[9] == 0x73 && (start[10] & 0x80) != 0; // RAR4 main header with encrypted block headers
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
        static ulong Vint(ReadOnlySpan<byte> bytes, ref int at)
        {
            ulong value = 0;
            for (int shift = 0; at < bytes.Length && shift < 64; shift += 7)
            {
                byte b = bytes[at++]; value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
            }
            return value;
        }
    }

    // ---- archives inside archives, opened as 7-Zip does: the inner one is extracted to a working copy that the
    // pane browses under its virtual path ("C:\a\outer.zip\docs\inner.7z\sub"); a change to the copy is written
    // back into the outer archive in the same job, so editing it is the same as editing any archive ----
    sealed record Nest(string Outer, string Entry, string Copy) { public string OuterVersion; }
    static readonly ConcurrentDictionary<string, Nest> nests = new(StringComparer.OrdinalIgnoreCase); // by virtual path
    /// The working copy shown for a virtual path that names an archive inside an archive, if it was opened.
    internal static string NestedCopy(string virtualPath) => nests.TryGetValue(virtualPath, out var nest) && File.Exists(nest.Copy) ? nest.Copy : null;
    static Nest OwnerOf(string copy) => nests.Values.FirstOrDefault(n => n.Copy.Equals(copy, StringComparison.OrdinalIgnoreCase));
    /// The file holding a virtual archive path: the archive itself, or the working copy of a nested one.
    static string Real(string virtualArchive) => NestedCopy(virtualArchive) ?? virtualArchive;
    /// `at` with any archive entries along its inner path opened (extracted once, then reused): the archive the
    /// listing really reads, and the folder in it. `virtualArchive`: the path the pane shows for `at.Archive`.
    internal static (string Archive, string Inner) Resolve((string Archive, string Inner) at, string virtualArchive)
    {
        var parts = at.Inner.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!IsArchiveName(parts[i])) continue;
            string entryPath = string.Join('/', parts[..(i + 1)]);
            var entry = Read(at.Archive).FirstOrDefault(e => !e.IsDir && e.Path.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue; // a folder named like an archive
            string nestedVirtual = virtualArchive + "\\" + entryPath.Replace('/', '\\');
            string copy = NestedCopy(nestedVirtual);
            if (copy == null || nests[nestedVirtual].OuterVersion != Version(at.Archive))
            {
                if (copy != null) ArchiveContent.ReleaseQuietly(copy);
                string folder = entryPath.Contains('/') ? entryPath[..entryPath.LastIndexOf('/')] : "";
                var lease = ArchiveContent.Extract(at.Archive, new[] { parts[i] }, folder, "nested archive", nestedVirtual);
                copy = lease.File;
                nests[nestedVirtual] = new(virtualArchive, entryPath, copy) { OuterVersion = Version(at.Archive) };
                if (passwords.TryGetValue(at.Archive, out var password) && !passwords.ContainsKey(copy)) passwords[copy] = password; // 7-Zip tries the outer one first
            }
            return Resolve((copy, string.Join('/', parts[(i + 1)..])), nestedVirtual);
        }
        return at;
    }
    /// After an edit of a nested archive's working copy: the copy replaces its entry in the outer archive (which,
    /// if nested too, is written back in turn by its own Edit).
    static void WriteBack(string copy, Job job)
    {
        if (OwnerOf(copy) is not { } nest) return;
        string outer = Real(nest.Outer);
        if (Version(outer) != nest.OuterVersion) throw new IOException("The archive holding this one changed since it was opened; reopen it. The change stays only in its working copy.");
        var entry = Read(outer).Single(e => e.Path.Equals(nest.Entry, StringComparison.OrdinalIgnoreCase));
        Edit(outer, new() { [entry.Index] = null }, new(StringComparer.OrdinalIgnoreCase) { [nest.Entry] = copy }, job);
        nest.OuterVersion = Version(outer);
    }
    /// Whether a path exists: on disk, or as an entry (or implied folder) inside an archive.
    internal static bool Exists(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        if (PaneView.ArchiveOf(path) is not { Inner.Length: > 0 } at) return false;
        try { string inner = Clean(at.Inner); return Read(at.Archive).Any(e => Under(e.Path, inner)); }
        catch (Exception ex) when (ex is IOException or SharpSevenZipException) { return false; }
    }
    /// The archive's own figures for Properties: its packed size, whether it is solid, its format.
    internal static (long Packed, bool Solid, string Format) Summary(string archive)
    {
        if (TarGzip(archive)) return (new FileInfo(archive).Length, true, "TAR.GZ");
        using var reader = Open(archive);
        try { return (reader.PackedSize, reader.IsSolid, reader.Format.ToString()); }
        catch (SharpSevenZipException ex) { throw new IOException("Cannot read archive metadata.", ex); }
    }
    /// Whether reading `inners` (paths inside the archive) needs a password not yet given: an encrypted entry
    /// among them, seen in the archive's directory without extracting anything.
    internal static bool NeedsPassword(string archive, IEnumerable<string> inners)
    {
        if (passwords.ContainsKey(archive)) return false;
        var picked = inners.Select(Clean).ToArray();
        try { return Read(archive).Any(e => e.Encrypted && picked.Any(p => p.Length == 0 || Under(e.Path, p))); }
        catch (Exception ex) when (ex is IOException or SharpSevenZipException) { return false; } // the read itself reports it
    }
    static SharpSevenZipExtractor Open(string path)
    {
        _ = native.Value;
        try
        {
            // These are declared ZIP containers. SharpSevenZip's extension
            // inference does not know APK/NuGet/JAR names when the signature is
            // invalid; select the declared container instead of throwing an
            // unrelated ArgumentException on the icon worker.
            string extension=System.IO.Path.GetExtension(path).ToLowerInvariant();
            return extension is ".apk" or ".nupkg" or ".jar" or ".vsix" or ".whl"
                ?new SharpSevenZipExtractor(path,passwords.GetValueOrDefault(path,""),InArchiveFormat.Zip)
                :new SharpSevenZipExtractor(path,passwords.GetValueOrDefault(path,""));
        }
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
                stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9')
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
                    result.Add(new(entry.Index, name, entry.IsDirectory, checked((long)entry.Size), entry.LastWriteTime, entry.Encrypted, (FileAttributes)(attributes & 0xFFFF),
                        // 7-Zip leaves 0 when kpidCrc is undefined (RAR5 BLAKE2, TAR, ISO); only an empty file's CRC is 0
                        entry.IsDirectory || entry.Crc == 0 && entry.Size > 0 ? null : entry.Crc,
                        string.IsNullOrEmpty(entry.Method) ? null : entry.Method, entry.CreationTime, string.IsNullOrEmpty(entry.Comment) ? null : entry.Comment));
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
            else items[rest] = new(rest, entry.IsDir, entry.IsDir ? -2 : entry.Size, entry.Modified, entry.Attributes, entry.Crc);
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
    /// The size and time of a container or input file: how a change is noticed between files, as Ferry's
    /// RequireCurrent does for ordinary transfers. Full content hashes are kept for moves' source removal.
    internal static string Version(string path) { var info = new FileInfo(path); return info.Length + ":" + info.LastWriteTimeUtc.Ticks; }
    /// Bounded to the entry's declared size. `progress`: counted in the job's bytes as written, as Ferry
    /// does; `hash`: computed while writing, for the read-back check when the job verifies.
    sealed class CheckedOutput(Stream output, long maximum, Job job, bool progress = false, System.IO.Hashing.XxHash3 hash = null) : Stream
    {
        long written;
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(job); if (count > maximum - written) throw new IOException("Entry exceeds its declared size.");
            output.Write(buffer, offset, count); written += count;
            hash?.Append(buffer.AsSpan(offset, count));
            if (progress) job.AddBytes(count);
        }
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
        var targets = entries.ToDictionary(e => e.Index, e => Target(dest, e.Path[prefix.Length..]));
        string version = Version(archive);
        int written = 0, skipped = 0;
        var files = new List<Raw>();
        foreach (var entry in entries)
        {
            string target = targets[entry.Index];
            if (entry.IsDir) { Directory.CreateDirectory(target); continue; }
            if (File.Exists(target))
            {
                if (choice == Choice.Skip || choice == Choice.Newer && File.GetLastWriteTime(target) >= entry.Modified) { skipped++; continue; }
                if (choice == Choice.Rename) targets[entry.Index] = Unique(target);
            }
            files.Add(entry);
        }
        Pass(archive, files, job, (entry, output) => { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targets[entry.Index])); return output; },
            targets, (entry, landed) =>
            {
                if (Version(archive) != version) throw new IOException("The archive changed during extraction.");
                _ = Target(dest, entry.Path[prefix.Length..]);
                File.Move(landed, targets[entry.Index], choice != Choice.Rename && choice != Choice.Skip);
                File.SetLastWriteTime(targets[entry.Index], entry.Modified); written++;
                if (job != null) job.DoneFiles++;
            });
        return (written, skipped);
    }

    /// Extracts the selected file entries with the container opened once. A solid 7z/RAR container is decoded
    /// in a single native pass, as 7-Zip itself extracts, into a hidden staging folder beside the targets
    /// (extracting entry by entry would re-decode the solid block from its start for every file). Other
    /// containers stream each entry into a temporary file beside its target. 7-Zip checks each entry's stored
    /// CRC while decompressing and the declared size is enforced. As in Ferry, a verifying job reads the file
    /// back: against the hash taken while writing, or against the stored CRC for a solid pass.
    /// `publish` then moves the temporary file into place; a failed or cancelled entry leaves nothing behind.
    internal static void Pass(string archive, IReadOnlyCollection<Raw> entries, Job job, Func<Raw, string, string> prepare,
        IReadOnlyDictionary<int, string> targets, Action<Raw, string> publish)
    {
        var wanted = entries.Where(e => !e.IsDir).ToDictionary(e => e.Index);
        if (wanted.Count == 0) return;
        Raw current = null; string temporary = null; FileStream file = null; CheckedOutput output = null; System.IO.Hashing.XxHash3 hash = null;
        Exception failure = null;
        bool verify = job?.Verify == true;
        void Begin(Raw entry)
        {
            Check(job);
            current = entry;
            temporary = prepare(entry, targets[entry.Index] + ".pathweft-" + Guid.NewGuid().ToString("N"));
            if (job != null) { job.Current = entry.Path; job.CurrentSize = entry.Size * (verify ? 2 : 1); job.CurrentDone = 0; }
            file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
            hash = verify ? new System.IO.Hashing.XxHash3() : null;
            output = new CheckedOutput(file, entry.Size, job, progress: job != null, hash);
        }
        void Finish()
        {
            var entry = current; string landed = temporary; current = null;
            try
            {
                file.Flush(true); file.Dispose(); file = null;
                if (output.Length != entry.Size) throw new IOException("Extracted size does not match the archive entry: " + entry.Path);
                if (verify)
                {
                    if (job != null) job.Phase = Phase.Verifying;
                    if (ReadBack(landed, job) != hash.GetCurrentHashAsUInt64()) throw new IOException("Extracted content verification failed: " + entry.Path);
                    if (job != null) job.Phase = Phase.Copying;
                }
                Check(job);
                publish(entry, landed);
            }
            finally { if (File.Exists(landed)) File.Delete(landed); temporary = null; }
        }
        void Abandon()
        {
            try { file?.Dispose(); } catch (IOException) { }
            file = null; current = null;
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            temporary = null;
        }
        try
        {
            if (TarGzip(archive))
            {
                var byName = wanted.Values.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
                VisitTarGzip(archive, (name, dir, size, time, stream) =>
                {
                    if (dir || !byName.TryGetValue(name, out var entry)) return;
                    Begin(entry); stream.CopyTo(output); Finish();
                });
                return;
            }
            using var reader = Open(archive);
            if (reader.IsSolid)
            {
                string staging = null;
                try
                {
                    var first = wanted.Values.OrderBy(e => e.Index).First();
                    staging = prepare(first, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(targets[first.Index]), ".pathweft-" + Guid.NewGuid().ToString("N")));
                    Directory.CreateDirectory(staging).Attributes |= FileAttributes.Hidden;
                    int published = 0;
                    // 7-Zip raises these synchronously from its native callback: exceptions are kept and rethrown after it returns
                    reader.FileExtractionStarted += (_, e) =>
                    {
                        try
                        {
                            Check(job);
                            if (failure != null) { e.Cancel = true; return; }
                            if (job != null && wanted.TryGetValue(e.FileInfo.Index, out var entry))
                            { job.Current = entry.Path; job.CurrentSize = entry.Size * (verify ? 2 : 1); job.CurrentDone = 0; }
                        }
                        catch (Exception ex) { failure = ex; e.Cancel = true; }
                    };
                    reader.FileExtractionFinished += (_, e) =>
                    {
                        if (failure != null || !wanted.TryGetValue(e.FileInfo.Index, out var entry)) return;
                        try
                        {
                            string staged = System.IO.Path.Combine(staging, entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                            current = entry;
                            temporary = prepare(entry, targets[entry.Index] + ".pathweft-" + Guid.NewGuid().ToString("N"));
                            File.Move(staged, temporary);
                            string landed = temporary;
                            try
                            {
                                if (new FileInfo(landed).Length != entry.Size) throw new IOException("Extracted size does not match the archive entry: " + entry.Path);
                                job?.AddBytes(entry.Size);
                                if (verify && entry.Crc is uint stored)
                                {
                                    if (job != null) job.Phase = Phase.Verifying;
                                    if (Crc(landed, job) != stored) throw new IOException("Extracted content verification failed: " + entry.Path);
                                    job?.AddVerificationBytes(entry.Size);
                                    if (job != null) job.Phase = Phase.Copying;
                                }
                                Check(job);
                                publish(entry, landed);
                                published++;
                            }
                            finally { if (File.Exists(landed)) File.Delete(landed); temporary = null; current = null; }
                        }
                        catch (Exception ex) { failure = ex; e.Cancel = true; }
                    };
                    try { reader.ExtractFiles(staging, wanted.Keys.OrderBy(i => i).ToArray()); }
                    catch (SharpSevenZipException ex) { failure ??= ex; }
                    if (failure == null && job?.IsCancelled != true && published != wanted.Count)
                        failure = new IOException("Cannot extract the selected entries; check the password and the archive's integrity.");
                }
                finally { if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            }
            else
            {
                foreach (var entry in wanted.Values.OrderBy(e => e.Index))
                {
                    Begin(entry);
                    try { reader.ExtractFile(entry.Index, output); }
                    catch (SharpSevenZipException ex) { failure = ex; break; }
                    Finish();
                }
            }
            Check(job);
            if (failure is OperationCanceledException) throw failure;
            if (failure is IOException or UnauthorizedAccessException) throw failure;
            if (failure != null) throw new IOException("Cannot extract the selected entries; check the password and the archive's integrity.", failure);
        }
        finally { Abandon(); }
    }
    static ulong ReadBack(string path, Job job)
    {
        var hash = new System.IO.Hashing.XxHash3();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            { Check(job); hash.Append(buffer.AsSpan(0, count)); job?.AddVerificationBytes(count); }
            return hash.GetCurrentHashAsUInt64();
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
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
        string original = Version(archive); var before = Read(archive);
        if (expectedOriginal != null && original != expectedOriginal) throw new IOException("The destination changed after transfer planning; no archive edit was published.");
        if (ChangeReason(archive) is { Length: > 0 } reason) throw new IOException(reason);
        var expected = new Dictionary<string, Expected>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in before)
        {
            string name = changes.TryGetValue(entry.Index, out var replacement) ? replacement : entry.Path;
            if (name != null && !expected.TryAdd(Clean(name), new(entry, null))) throw new IOException("The edit would create duplicate entry names.");
        }
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in additions)
        {
            expected.Add(Clean(entry.Key), new(null, entry.Value));
            if (entry.Value != null) inputs[entry.Value] = Version(entry.Value);
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
            if (actual.Count != expected.Count || actual.Any(e => !expected.TryGetValue(e.Path, out var want) || !Matches(stage, e, archive, want, job))) throw new IOException("Staged archive verification failed; original preserved.");
            foreach (var input in inputs) if (Version(input.Key) != input.Value) throw new IOException("An input changed while the archive was written; original preserved.");
            Boundary("before-publish"); Check(job);
            validateSources?.Invoke();
            using var publication = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (Version(archive) != original) throw new IOException("The archive changed concurrently; staged edit was not published.");
            File.Replace(stage, archive, null);
            WriteBack(archive, job);
        }
        catch (SharpSevenZipException ex) { Check(job); throw new IOException("Native archive update failed; original preserved.", ex); }
        finally { passwords.TryRemove(stage, out _); Directory.Delete(temporaryDirectory, recursive: true); }
    }
    /// What a staged entry must hold: the original entry it came from (`Source`), or the file added (`Input`;
    /// neither for an added folder).
    sealed record Expected(Raw Source, string Input);
    /// Compared through the CRC32 that ZIP, 7z and RAR4 store for every entry, read from the directory
    /// without extracting; only entries without one (TAR, RAR5's BLAKE2) are decompressed and hashed.
    static bool Matches(string stage, Raw staged, string original, Expected want, Job job)
    {
        bool directory = want.Source?.IsDir ?? want.Input == null;
        if (staged.IsDir || directory) return staged.IsDir == directory;
        if (want.Source is { } source)
            return staged.Crc is uint crc && source.Crc is uint before ? crc == before && staged.Size == source.Size
                : EntryHash(stage, staged, job) == EntryHash(original, source, job);
        if (staged.Size != new FileInfo(want.Input).Length) return false;
        if (staged.Crc is uint stored) return stored == Crc(want.Input, job);
        return EntryHash(stage, staged, job) == Fingerprint(want.Input);
    }
    static uint Crc(string path, Job job)
    {
        var crc = new System.IO.Hashing.Crc32();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { Check(job); crc.Append(buffer.AsSpan(0, count)); }
            return crc.GetCurrentHashAsUInt32();
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
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
        Relocate(archive, inner, new[] { (name, replacement) }, job);
    }
    /// Several entries renamed or moved within the folder shown, in one edit, so names may swap (Rename all…,
    /// Group into new folder…). `Replacement`: a path relative to `inner`. `folder`: a folder entry added with them.
    internal static void Relocate(string archive, string inner, IReadOnlyCollection<(string Name, string Replacement)> items, Job job = null, string folder = null)
    {
        string prefix = Prefix(inner); var entries = Read(archive); var changes = new Dictionary<int, string>();
        foreach (var (name, replacement) in items)
        {
            string from = prefix + Clean(name), to = prefix + Clean(replacement);
            var selected = entries.Where(e => Under(e.Path, from)).ToArray();
            if (selected.Length == 0) throw new IOException("An entry to rename no longer exists: " + name);
            foreach (var e in selected) changes[e.Index] = to + e.Path[from.Length..];
        }
        var additions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (folder != null && !entries.Any(e => e.Path.Equals(prefix + Clean(folder), StringComparison.OrdinalIgnoreCase))) additions[prefix + Clean(folder)] = null;
        Edit(archive, changes, additions, job);
    }
    /// New folder / New file inside an archive: `file` null adds an empty folder named `name`.
    internal static void AddNew(string archive, string inner, string name, string file, Job job = null)
    {
        string path = Prefix(inner) + Clean(name);
        if (name.Contains('/') || name.Contains('\\')) throw new IOException("Give one name, without folders.");
        if (Read(archive).Any(e => Under(e.Path, path))) throw new IOException($"“{name}” already exists here.");
        Edit(archive, new(), new(StringComparer.OrdinalIgnoreCase) { [path] = file }, job);
    }
    /// The entries a selected item stands for: itself, and everything under it when it is a folder.
    internal static List<Raw> EntriesOf(string archive, string inner, string name)
    {
        string path = Prefix(inner) + Clean(name);
        return Read(archive).Where(e => Under(e.Path, path)).ToList();
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
            var expected = files.ToDictionary(e => Clean(e.Key), e => new Expected(null, e.Value), StringComparer.OrdinalIgnoreCase);
            var inputs = files.Where(e => e.Value != null).ToDictionary(e => e.Value, e => Version(e.Value), StringComparer.OrdinalIgnoreCase);
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
            if (actual.Count != expected.Count || actual.Any(e => !expected.TryGetValue(e.Path, out var want) || !Matches(stage, e, null, want, job))) throw new IOException("New archive content verification failed.");
            foreach (var input in inputs) if (Version(input.Key) != input.Value) throw new IOException("An input changed while the archive was created.");
            Boundary("before-publish"); Check(job);
            File.Move(stage, archive);
            if (job != null) { job.DoneFiles = job.TotalFiles; job.AddBytes(job.TotalBytes); }
        }
        catch (SharpSevenZipException ex) { Check(job); throw new IOException("Native archive creation failed.", ex); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
