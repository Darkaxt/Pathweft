using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// HASH-04: every hash Pathweft computed for a file, kept in its data folder (never in the file or a stream of it).
// A file is known by its volume and file ID with its size and last write: a rename or a move on the volume keeps
// the values, any change of content (size or time) is a new key. Capped, the least recently used dropped first.
// The hash columns and the Checksum command read and fill the same cache.
internal static class HashCache
{
    internal readonly record struct Key(ulong Volume, UInt128 File, long Size, long Written);
    sealed class Item { internal Dictionary<string, string> Values = new(StringComparer.Ordinal); internal long Used; }
    const FileAttributes InCloud = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000; // RECALL_ON_OPEN, RECALL_ON_DATA_ACCESS

    static readonly object gate = new();
    static Dictionary<Key, Item> items;
    static long bytes;
    static bool dirty;
    /// Files actually read to hash them (verification: a cache hit reads nothing).
    internal static long Reads;
    internal static string FilePath => Path.Combine(DataFolder.Cache, "hashes.bin");
    internal static long Limit => Math.Max(1, Settings.HashCacheMB) * (1L << 20);

    internal static bool IsPlaceholder(FileAttributes attributes) => (attributes & InCloud) != 0;

    /// The file's identity, read through a handle that asks only for its attributes (a placeholder is not recalled).
    internal static Key? Identify(string path) => Inspect(path).Key;

    /// The identity and attributes of a file (attributes 0 when it cannot be opened).
    internal static (Key? Key, FileAttributes Attributes) Inspect(string path)
    {
        using var handle = CreateFileW(path.StartsWith(@"\\?\") ? path : @"\\?\" + (path.StartsWith(@"\\") ? "UNC" + path[1..] : path), 0x80 /*FILE_READ_ATTRIBUTES*/, 7, IntPtr.Zero, 3, 0x02000000 /*BACKUP_SEMANTICS*/, IntPtr.Zero);
        if (handle.IsInvalid) return (null, 0);
        if (!GetFileInformationByHandle(handle, out var info)) return (null, 0);
        if ((info.Attributes & 0x10) != 0) return (null, (FileAttributes)info.Attributes); // a folder has no content hash
        ulong volume = info.Volume; UInt128 file = ((ulong)info.IndexHigh << 32) | info.IndexLow;
        if (GetFileInformationByHandleEx(handle, 18 /*FileIdInfo*/, out var id, Marshal.SizeOf<FileIdInfo>()))
        { volume = id.Volume; file = new UInt128(id.High, id.Low); }
        return (new Key(volume, file, ((long)info.SizeHigh << 32) | info.SizeLow, ((long)info.WriteHigh << 32) | info.WriteLow), (FileAttributes)info.Attributes);
    }

    /// The values known for this file, or an empty set.
    internal static Dictionary<string, string> Get(Key key)
    {
        lock (gate)
        {
            Load();
            if (!items.TryGetValue(key, out var item)) return new(StringComparer.Ordinal);
            item.Used = DateTime.UtcNow.Ticks; dirty = true;
            return new(item.Values, StringComparer.Ordinal);
        }
    }

    internal static void Put(Key key, IReadOnlyDictionary<string, string> values)
    {
        lock (gate)
        {
            Load();
            if (!items.TryGetValue(key, out var item)) { items[key] = item = new Item(); bytes += 64; }
            foreach (var (name, value) in values)
            {
                if (value == null) continue;
                if (item.Values.TryGetValue(name, out var old)) bytes -= Cost(name, old);
                item.Values[name] = value; bytes += Cost(name, value);
            }
            item.Used = DateTime.UtcNow.Ticks; dirty = true;
            if (bytes > Limit) Prune(Limit * 9 / 10);
        }
    }

    static long Cost(string name, string value) => 4 + name.Length + value.Length;
    static long Cost(Item item) => 64 + item.Values.Sum(v => Cost(v.Key, v.Value));

    static void Prune(long target)
    {
        foreach (var (key, item) in items.OrderBy(p => p.Value.Used).ToList())
        {
            if (bytes <= target) break;
            items.Remove(key); bytes -= Cost(item);
        }
    }

    /// What the cache holds now, in bytes (what Settings shows).
    internal static long Size { get { lock (gate) { Load(); return bytes; } } }

    internal static void Clear()
    {
        lock (gate)
        {
            items = new(); bytes = 0; dirty = false;
            try { File.Delete(FilePath); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// The limit lowered in Settings: drop what no longer fits now.
    internal static void Trim() { lock (gate) { Load(); if (bytes > Limit) { Prune(Limit * 9 / 10); dirty = true; } } }

    static void Load()
    {
        if (items != null) return;
        items = new(); bytes = 0;
        try
        {
            if (!File.Exists(FilePath)) return;
            using var reader = new BinaryReader(File.OpenRead(FilePath));
            if (reader.ReadString() != "PWHC1") return;
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var key = new Key(reader.ReadUInt64(), new UInt128(reader.ReadUInt64(), reader.ReadUInt64()), reader.ReadInt64(), reader.ReadInt64());
                var item = new Item { Used = reader.ReadInt64() };
                int values = reader.ReadByte();
                for (int v = 0; v < values; v++) item.Values[reader.ReadString()] = reader.ReadString();
                items[key] = item; bytes += Cost(item);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EndOfStreamException or FormatException)
        { items = new(); bytes = 0; }
    }

    /// Written on exit and with the folder sizes' periodic save, only when something changed.
    internal static void SaveIfChanged()
    {
        KeyValuePair<Key, Item>[] snapshot;
        lock (gate)
        {
            if (items == null || !dirty) return;
            snapshot = items.Select(p => new KeyValuePair<Key, Item>(p.Key, new Item { Used = p.Value.Used, Values = new(p.Value.Values) })).ToArray();
            dirty = false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write("PWHC1"); writer.Write(snapshot.Length);
                foreach (var (key, item) in snapshot)
                {
                    writer.Write(key.Volume); writer.Write((ulong)(key.File >> 64)); writer.Write((ulong)key.File);
                    writer.Write(key.Size); writer.Write(key.Written); writer.Write(item.Used);
                    writer.Write((byte)item.Values.Count);
                    foreach (var (name, value) in item.Values) { writer.Write(name); writer.Write(value); }
                }
            }
            File.Move(temporary, FilePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { lock (gate) dirty = true; }
    }

    /// The asked-for hashes of one file: those already known without reading it, the rest in one read, then kept.
    /// Null for a name means this Windows lacks that algorithm (Checksums.Unavailable), or the file is online-only
    /// and `recall` was not given: a cloud placeholder is read (downloaded) only when the owner asked for it.
    internal static Dictionary<string, string> OfFile(string path, IReadOnlyCollection<string> names, CancellationToken token, Action<long> progress = null, bool recall = false)
    {
        var (key, attributes) = Inspect(path);
        var known = key is { } k ? Get(k) : new(StringComparer.Ordinal);
        var missing = names.Where(n => !known.ContainsKey(n) && Checksums.Unavailable(n) == null).ToList();
        if (missing.Count > 0 && (recall || !IsPlaceholder(attributes)))
        {
            Interlocked.Increment(ref Reads);
            var computed = Checksums.OfFile(path, missing, token, progress);
            // written while it was read: the values belong to no single version of the file, so they are not kept
            if (key is { } before && Identify(path) == before) Put(before, computed);
            foreach (var (name, value) in computed) known[name] = value;
        }
        return names.ToDictionary(n => n, n => known.GetValueOrDefault(n));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ByHandle
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] struct FileIdInfo { public ulong Volume; public ulong Low, High; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandle info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int type, out FileIdInfo info, int size);
}
