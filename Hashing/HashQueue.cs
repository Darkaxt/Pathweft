using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer;

// HASH-02: one low-priority queue for the whole app. Each physical disk has one reader, so one file at a time is
// read from it however many panes ask; the lowest priority number goes first (the active pane's visible rows,
// then the rest of its folder, then the other pane on screen). A pane replaces its own requests whenever what it
// shows changes, and cancels them when it leaves the folder or the column goes.
internal static class HashQueue
{
    internal sealed class Job
    {
        internal string Path;
        internal string[] Names;
        internal int Priority;
        internal object Owner;
        internal Action<Dictionary<string, string>> Done; // called on the reader thread
        internal Action<Exception> Failed;                 // the file could not be read (also on the reader thread)
        internal bool Recall;                              // asked for by the owner: an online-only file is downloaded to read it
        internal long Order;
    }

    sealed class Disk
    {
        internal readonly List<Job> Jobs = new();
        internal Thread Reader;
        internal Job Current;
        internal CancellationTokenSource CurrentCancel;
    }

    static readonly object gate = new();
    static readonly Dictionary<string, Disk> disks = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> diskOfRoot = new(StringComparer.OrdinalIgnoreCase);
    static long order;
#if DEBUG
    /// Verification: the order files were read in, and which disk each reader served.
    internal static readonly List<(string Disk, string Path)> ReadLog = new();
#endif

    /// Replaces every request of `owner` with these jobs.
    internal static void Submit(object owner, IEnumerable<Job> jobs) => Enqueue(owner, jobs, replace: true);

    /// Adds to `owner`'s requests (the files the owner asked for one by one).
    internal static void Add(object owner, IEnumerable<Job> jobs) => Enqueue(owner, jobs, replace: false);

    static void Enqueue(object owner, IEnumerable<Job> jobs, bool replace)
    {
        var list = jobs.ToList();
        lock (gate)
        {
            if (replace) RemoveLocked(owner, keepCurrent: true);
            foreach (var job in list)
            {
                job.Owner = owner; job.Order = ++order;
                var disk = DiskOf(job.Path);
                if (!disks.TryGetValue(disk, out var d)) disks[disk] = d = new Disk();
                // the file being read for this owner already: it finishes and reports, not read twice
                if (d.Current is { } running && running.Owner == owner && running.Path.Equals(job.Path, StringComparison.OrdinalIgnoreCase)) continue;
                d.Jobs.Add(job);
                if (d.Reader == null) { var key = disk; d.Reader = new Thread(() => Read(key, d)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Pathweft hashes " + disk }; d.Reader.Start(); }
            }
            Monitor.PulseAll(gate);
        }
    }

    /// Stops `owner`'s requests, the file being read for it too.
    internal static void Cancel(object owner) { lock (gate) RemoveLocked(owner, keepCurrent: false); }

    /// Pathweft is closing: every request stops.
    internal static void CancelAll()
    {
        lock (gate) foreach (var d in disks.Values) { d.Jobs.Clear(); d.CurrentCancel?.Cancel(); }
    }

    static void RemoveLocked(object owner, bool keepCurrent)
    {
        foreach (var d in disks.Values)
        {
            d.Jobs.RemoveAll(j => j.Owner == owner);
            if (!keepCurrent && d.Current?.Owner == owner) d.CurrentCancel?.Cancel();
        }
    }

    /// Requests not yet finished, for verification and the footer.
    internal static int Pending(object owner) { lock (gate) return disks.Values.Sum(d => d.Jobs.Count(j => j.Owner == owner) + (d.Current?.Owner == owner ? 1 : 0)); }

    static void Read(string name, Disk d)
    {
        LowPriority(() =>
        {
            while (true)
            {
                Job job; CancellationTokenSource cancel;
                lock (gate)
                {
                    while (d.Jobs.Count == 0)
                    {
                        // an idle reader leaves after a while; the next request starts it again
                        if (!Monitor.Wait(gate, TimeSpan.FromSeconds(30)) && d.Jobs.Count == 0) { d.Reader = null; return; }
                    }
                    job = d.Jobs.MinBy(j => (j.Priority, j.Order));
                    d.Jobs.Remove(job);
                    d.Current = job; d.CurrentCancel = cancel = new CancellationTokenSource();
                }
                try
                {
#if DEBUG
                    lock (ReadLog) ReadLog.Add((name, job.Path));
#endif
                    var values = HashCache.OfFile(job.Path, job.Names, cancel.Token, recall: job.Recall);
                    if (!cancel.IsCancellationRequested) job.Done(values);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (!cancel.IsCancellationRequested) job.Failed?.Invoke(error); }
                finally { lock (gate) { d.Current = null; d.CurrentCancel = null; } cancel.Dispose(); }
            }
        });
    }

    /// The physical disk a path is read from: one reader per disk, so two volumes of one disk share it.
    /// A network path, or a volume spread over several disks, is its own.
    internal static string DiskOf(string path)
    {
        string root = Path.GetPathRoot(path) ?? path;
        if (root.StartsWith(@"\\")) return root;
        lock (diskOfRoot) if (diskOfRoot.TryGetValue(root, out var known)) return known;
        string disk = root;
        try
        {
            using var volume = CreateFileW(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            var buffer = new byte[64];
            if (!volume.IsInvalid && DeviceIoControl(volume, 0x560000 /*IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS*/, IntPtr.Zero, 0, buffer, buffer.Length, out _, IntPtr.Zero)
                && BitConverter.ToUInt32(buffer, 0) == 1)
                disk = "disk" + BitConverter.ToUInt32(buffer, 8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        lock (diskOfRoot) diskOfRoot[root] = disk;
        return disk;
    }

    // Background I/O and CPU priority for the reader thread (THREAD_MODE_BACKGROUND_BEGIN/END).
    static void LowPriority(Action work)
    {
        bool lowered = SetThreadPriority(GetCurrentThread(), 0x00010000);
        try { work(); } finally { if (lowered) SetThreadPriority(GetCurrentThread(), 0x00020000); }
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool SetThreadPriority(IntPtr thread, int priority);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
