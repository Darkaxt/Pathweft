using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace FileExplorer;

// Folder sizes, remembered between sessions: walking a full 12 TB drive takes minutes, and the
// answer rarely changes. Every folder a scan passes through is kept, so opening any folder under a
// scanned one shows its sizes at once, and a scan cut short resumes from what it had finished.
// Timestamps detect changes outside an observed view. Recursive notifications from open views
// invalidate the changed branch and its ancestors, including during a sizing pass.
public static class SizeCache
{
    record Entry(long Size, long Stamp, long UsedAt); // Stamp = folder's LastWriteTimeUtc ticks

    static readonly ConcurrentDictionary<string, Entry> map = new(StringComparer.OrdinalIgnoreCase);
    static readonly object state = new();
    static readonly HashSet<Measurement> measuring = new();
    internal static event Action<string> Invalidated;
    internal static string FilePath => Path.Combine(DataFolder.Cache, "folder-sizes.txt");
    const int MaxEntries = 1_000_000; // Entry bound; byte size depends on path lengths. Least recently used go first.

    // Read in the background after the window's first frame (MainWindow), or as soon as something needs a size.
    // Read at startup, a big cache delayed the first frame: its parse and garbage collections took 0.8 s of the
    // UI thread here and 2.2 s on a throttled CPU (1.0.44). Callers of Get are all off the UI thread and wait for it,
    // so nothing is measured twice for having asked too early; a listing Peeks instead and never waits.
    static Task loading; // null until something asks
    static readonly object gate = new();
    public static void StartLoading() { lock (gate) loading ??= Task.Run(Load); }
    static void Ready() { StartLoading(); loading.Wait(); }

    /// For a listing: the remembered size if the cache is already read, else -1 (the sizing pass fills it in). Never waits.
    public static long Peek(string path, DateTime lastWriteUtc) => loading is { IsCompleted: true } ? Get(path, lastWriteUtc) : -1;

    /// The remembered size, or -1 when unknown or the folder has changed since. Call off the UI thread.
    public static long Get(string path, DateTime lastWriteUtc)
    {
        Ready();
        lock(state)
        {
            if (!map.TryGetValue(path, out var e)) return -1;
            if (e.Stamp == lastWriteUtc.Ticks)
            {
                map[path] = e with { UsedAt = DateTime.UtcNow.Ticks };
                return e.Size;
            }
        }
        Invalidate(path);
        return -1;
    }

    /// A folder changed: its size is stale, and so is the total of every folder that contains it.
    internal static bool Within(string path,string folder) => path.Equals(folder,StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);

    internal static void Invalidate(string path, bool subtree = false)
    {
        Ready();
        lock(state)
        {
            if(subtree)foreach(var key in map.Keys.Where(key=>Within(key,path)))map.TryRemove(key,out _);
            for(var p=path;p!=null;p=Path.GetDirectoryName(p))map.TryRemove(p,out _);
            foreach(var scan in measuring)
                if(Within(path,scan.Path)||subtree&&Within(scan.Path,path))scan.Invalid=true;
            changed=true;
        }
        Invalidated?.Invoke(path);
    }

    public static void Set(string path, DateTime lastWriteUtc, long size)
    {
        lock(state){map[path] = new Entry(size, lastWriteUtc.Ticks, DateTime.UtcNow.Ticks);changed = true;}
    }

    public static void Clear()
    {
        Ready();lock(state){map.Clear();foreach(var scan in measuring)scan.Invalid=true;changed=true;}
        Invalidated?.Invoke(null);
    }

    // Registration and publication share the invalidation lock. A notification arriving during
    // enumeration prevents that snapshot from entering the cache; callers repeat from actual state.
    internal sealed class Measurement : IDisposable
    {
        internal string Path {get;}
        internal bool Invalid;
        internal Measurement(string path){Ready();Path=path;lock(state)measuring.Add(this);}
        internal bool Complete(DateTime stamp,long bytes)
        {
            lock(state)
            {
                if(Invalid)return false;
                map[Path]=new(bytes,stamp.Ticks,DateTime.UtcNow.Ticks);changed=true;
                return true;
            }
        }
        public void Dispose(){lock(state)measuring.Remove(this);}
    }

    static volatile bool changed; // since the last save

    /// Written every few minutes, not only on exit: a crash or a power cut lost a whole session's
    /// measuring (a full drive is minutes of walking).
    public static void SaveIfChanged()
    {
        if (!changed) return;
        changed = false;
        Save();
    }

    public static void Load()
    {
        // Read the old cache once if necessary; all future writes use bulk storage.
        string source = File.Exists(FilePath) ? FilePath : Path.Combine(Settings.Dir, "folder-sizes.txt");
        if (!File.Exists(source)) return;
        try
        {
            foreach (var line in File.ReadLines(source))
            {
                var p = line.Split('|', 4);
                if (p.Length == 4 && long.TryParse(p[0], out var size) && long.TryParse(p[1], out var stamp) && long.TryParse(p[2], out var used)
                    && !Loops(p[3]))
                    map[p[3]] = new Entry(size, stamp, used);
            }
        }
        catch (IOException) { } // unreadable cache is not worth a message; it just rebuilds
    }

    /// A path that goes round a loop: the same three folders twice in a row of its parts. A junction pointing back up
    /// its own tree, copied once as real folders (moving a pnpm workspace to another drive did it), left 563,000 such
    /// entries and a 277 MB cache that was parsed at every start. Real paths that long almost never repeat like that;
    /// one that does is only measured again. Checked only past 200 characters, where loops end up.
    internal static bool Loops(string path)
    {
        if (path.Length <= 200) return false;
        var parts = path.Split('\\');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 3 <= parts.Length; i++)
            if (!seen.Add(string.Join('\\', parts, i, 3))) return true;
        return false;
    }

    public static void Save()
    {
        if (loading == null) return; // never read: saving would replace the file with nothing
        Ready();                     // still reading: saving now would write half of it
        try
        {
            Directory.CreateDirectory(DataFolder.Cache);
            // ConcurrentDictionary's own snapshot holds its collection locks.
            // LINQ's ICollection fast path reads Count and later calls CopyTo;
            // a sizing worker can add entries between those two operations.
            var keep = map.ToArray().OrderByDescending(kv => kv.Value.UsedAt).Take(MaxEntries);
            Settings.WriteAllLines(FilePath, keep.Select(kv =>
                string.Create(CultureInfo.InvariantCulture, $"{kv.Value.Size}|{kv.Value.Stamp}|{kv.Value.UsedAt}|{kv.Key}")));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
