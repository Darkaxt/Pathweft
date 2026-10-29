using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer;

// Ferry's shared queue, strengthened under S5's REA-derived adoption contract:
// scan/conflict ownership, native direct copying, complete stream verification
// and verified source removal. Moves always verify copied data;
// same-volume renames preserve file identity without claiming a hash pass.

public enum Phase { Queued, Scanning, Conflicts, Copying, Verifying, Done, Cancelled, Failed }
public enum Choice { Skip, Overwrite, Rename, Newer }

public record Conflict(int File, string Name, long SourceSize, DateTime SourceModified, long TargetSize, DateTime TargetModified)
{
    public string Detail => $"{MainWindow.Fmt(SourceSize)}, {SourceModified:yyyy-MM-dd HH:mm}  →  existing {MainWindow.Fmt(TargetSize)}, {TargetModified:yyyy-MM-dd HH:mm}";
}

public class Job : INotifyPropertyChanged
{
    public List<string> Sources { get; init; }
    public string Destination { get; init; }
    public bool IsMove { get; init; }
    public bool Verify { get; init; }
    public bool Parallel { get; init; } // Settings → Transfers: small files several at once, where the drives take it well
    public bool ArchiveOperation { get; init; }
    public string Description { get; init; }
    public int SkippedFiles { get; internal set; }
    public int ExcludedItems { get; internal set; }
    public int CopiedFiles, VerifiedFiles, RenamedFiles;
    public long VerificationBytes;
    public List<string> Arrivals { get; internal set; }
    readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => completed.Task;
    internal void Complete() => completed.TrySetResult();

    // 0 running, 1 paused, 2 cancelled — read from the CopyFileExW callback, so kept lock-free.
    volatile int state;
    readonly ManualResetEventSlim resumed = new(true);
    public bool Paused => state == 1 && Active;
    public bool IsCancelled => state == 2;
    public void Pause() { lock (resumed) { if (Active && state == 0) { state = 1; resumed.Reset(); } } Refresh(); }
    public void Resume() { lock (resumed) { if (state == 1) { state = 0; resumed.Set(); } } Refresh(); }
    public void Cancel() { lock (resumed) { if (!Active) return; state = 2; resumed.Set(); } resolved.TrySetResult(null); Refresh(); }
    internal void WaitUntilResumed() => resumed.Wait();

    public Phase Phase { get; internal set; }
    public long TotalBytes, DoneBytes, CurrentSize, CurrentDone;
    public int TotalFiles, DoneFiles;
    public string Current { get; internal set; } = "";
    public List<Conflict> Conflicts { get; internal set; } = new();
    public bool Merged { get; internal set; } // a folder went into a folder of the same name that was already there
    public readonly Dictionary<string, string> Duplicates = new(StringComparer.OrdinalIgnoreCase); // source → its "- Copy" beside it, which Undo removes
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Errors = new(); // written by the worker, read by the UI
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Notes = new();

    readonly TaskCompletionSource<Choice?> resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Resolve(Choice c) => resolved.TrySetResult(c);
    internal Choice? WaitForResolution() => resolved.Task.Result;

    // Speed reading, Shelf-style: age-weighted (time constant, not a sample window), so it neither
    // lurches when an old sample drops out nor freezes through thousands of small files.
    const double Tau = 1.5; // seconds
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastAt, lastPointAt = -1, rate, peak;
    internal void AddBytes(long delta)
    {
        Interlocked.Add(ref DoneBytes, delta);
        Interlocked.Add(ref CurrentDone, delta);
        lock (clock) AddRate(delta);
    }
    internal void AddVerificationBytes(long delta)
    {
        Interlocked.Add(ref VerificationBytes, delta);
        if (!Concurrent) Interlocked.Add(ref CurrentDone, delta);
        lock (clock) AddRate(delta);
    }

    void AddRate(long delta)
    {
        double now = clock.Elapsed.TotalSeconds, dt = now - lastAt;
        if (dt <= 0) return;
        double instant = delta / dt;
        rate += (1 - Math.Exp(-dt / Tau)) * (instant - rate);
        lastAt = now;
        // chart: a point per report, throttled to ten a second, kept for ninety seconds
        if (now - lastPointAt < 0.1) return;
        lastPointAt = now;
        peak = Math.Max(peak, rate);
        lock (speeds)
        {
            speeds.Add((now * 1000, rate));
            while (speeds.Count > 900 || speeds[0].at < now * 1000 - 90000) speeds.RemoveAt(0);
        }
    }

    // ---- view ----
    public event PropertyChangedEventHandler PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new(null));
    /// Only the two bars, every frame: the text and the chart change ten times a second (Refresh).
    public void RefreshBars()
    {
        PropertyChanged?.Invoke(this, new(nameof(Fraction)));
        PropertyChanged?.Invoke(this, new(nameof(CurrentFraction)));
    }

    string Verb => IsMove ? "Moving" : "Copying";
    public bool Partial => SkippedFiles > 0 || ExcludedItems > 0;
    public bool Successful => Phase == Phase.Done && !Partial && Errors.IsEmpty && DoneFiles == TotalFiles;
    string ResultDetail => $"{DoneFiles}/{TotalFiles} completed" + (SkippedFiles > 0 ? $", {SkippedFiles} skipped" : "") + (ExcludedItems > 0 ? $", {ExcludedItems} unsupported" : "");
    public string Title => Description != null ? Description + (Phase == Phase.Done ? Partial ? " · " + ResultDetail : " · complete" : Phase == Phase.Cancelled ? " · cancelled, " + ResultDetail : Phase == Phase.Failed ? " · failed, " + ResultDetail : Paused ? " · paused" : "") : Phase switch
    {
        Phase.Done when Partial => $"Finished — {ResultDetail} to {Destination}",
        Phase.Done => $"{(IsMove ? "Moved" : "Copied")} {DoneFiles} file(s) to {Destination}" + (RenamedFiles == TotalFiles && TotalFiles > 0 ? " · renamed" : $" · {VerifiedFiles} verified" + (VerifiedFiles < CopiedFiles ? ", unchecked copies" : "")),
        Phase.Failed => $"Failed — {ResultDetail} to {Destination}",
        Phase.Cancelled => $"Cancelled — {DoneFiles} of {TotalFiles} file(s) to {Destination}",
        Phase.Scanning => $"Scanning {Sources.Count} item(s)…",
        Phase.Verifying => $"Checking transferred data in {Destination}",
        _ => $"{Verb} {TotalFiles} file(s) to {Destination}",
    } + (Paused ? "  (paused)" : "");
    public double Fraction => Math.Clamp(Finished ? TotalFiles > 0 ? (double)DoneFiles / TotalFiles : Successful ? 1 : 0 : TotalBytes > 0 ? (double)DoneBytes / TotalBytes : 0, 0, 1);
    public double CurrentFraction => Concurrent ? 0 : CurrentSize > 0 ? Math.Clamp((double)CurrentDone / CurrentSize, 0, 1) : 0;
    internal int ParallelWorkers;
    internal bool Concurrent;
    public string Stats
    {
        get
        {
            var s = $"{DoneFiles}/{TotalFiles} files · {MainWindow.Fmt(DoneBytes)} processed";
            if (ArchiveOperation) { if (SkippedFiles > 0) s += $" · {SkippedFiles} skipped"; }
            else s += $" · {VerifiedFiles} verified" + (RenamedFiles > 0 ? $" · {RenamedFiles} renamed" : "") + (SkippedFiles > 0 ? $" · {SkippedFiles} skipped" : "");
            if (Copying && !Paused && rate > 0)
                s += $" · {MainWindow.Fmt((long)rate)}/s I/O";
            return s;
        }
    }
    public bool Active => Phase is Phase.Queued or Phase.Scanning or Phase.Copying or Phase.Verifying or Phase.Conflicts;
    public bool Finished => !Active;
    public bool Copying => Phase is Phase.Copying or Phase.Verifying;
    public bool HasConflicts => Phase == Phase.Conflicts;
    public string ConflictTitle => $"{Conflicts.Count} file(s) already exist in the destination";
    public bool HasErrors => Errors.Count > 0 || Notes.Count > 0;
    public bool HasTransferErrors => !Errors.IsEmpty;
    public string ErrorSummary => string.Join("  ·  ", new[] { Errors.Count > 0 ? $"{Errors.Count} error(s)" : null }.Concat(Notes).Where(x => x != null));
    public string ErrorDetail => string.Join("\n", Errors.Take(50).Concat(Notes));
    public string PauseGlyph => Paused ? "\uE768" : "\uE769";
    public string PauseText => Paused ? "Resume" : "Pause";

    // ---- the Transfers window: a colour per job (its card and its band in the total chart), and the section it sits in ----
    public System.Windows.Media.Brush Hue { get; set; } = System.Windows.Media.Brushes.CornflowerBlue;
    public int Seq { get; set; } // newest first within a section
    public int StateOrder => HasConflicts || Phase == Phase.Failed || Finished && !Errors.IsEmpty ? 0 : Active ? 1 : 2;
    public string StateGroup => HasConflicts ? "Needs you" : Phase == Phase.Failed || Finished && !Errors.IsEmpty ? "Needs attention" : Active ? "Running" : "Done";
    public string PercentText => Phase == Phase.Failed ? "Failed" : Phase == Phase.Cancelled ? "Cancelled" : Phase == Phase.Done && Partial ? "Partial" : $"{Fraction * 100:0}%";
    public double Rate => Copying && !Paused ? rate : 0; // what this job adds to the total chart right now
    public string Where => System.IO.Path.GetFileName(Destination.TrimEnd('\\')) is { Length: > 0 } n ? n : Destination;

    // Speed chart data (drawn by SpeedChart): timestamped points and the held ceiling.
    readonly List<(double at, double value)> speeds = new();
    public (double at, double value)[] SpeedSamples { get { lock (speeds) return speeds.ToArray(); } }
    public double SpeedCeiling => SpeedChart.NiceCeiling(peak);
    public bool HasGraph { get { lock (speeds) return speeds.Count >= 2; } }
}

public static class Ferry
{
    static readonly Dictionary<string, Task> lanes = new(StringComparer.OrdinalIgnoreCase);
    public static event Action<Job> Finished;

    // Reserve the destination and every move-source volume through actual completion.
    // Archive cuts also edit their source container after destination verification.
    public static Job Submit(IEnumerable<string> sources, string destination, bool move, Action<Job> operation = null, string description = null)
    {
        var selected = sources.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToList();
        selected = selected.Where(source => !selected.Any(parent => parent != source && Directory.Exists(parent)
            && source.StartsWith(Path.TrimEndingDirectorySeparator(parent) + "\\", StringComparison.Ordinal))).ToList();
        var job = new Job { Sources = selected, Destination = Path.GetFullPath(destination), IsMove = move, Verify = Settings.Verify || move, Parallel = Settings.ParallelCopy, ArchiveOperation = operation != null, Description = description };
        var volumes = (move ? job.Sources.Select(Path.GetPathRoot).Append(Path.GetPathRoot(job.Destination)) : new[] { Path.GetPathRoot(job.Destination) })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (lanes)
        {
            var previous = Task.WhenAll(volumes.Select(volume => lanes.TryGetValue(volume, out var task) ? task : Task.CompletedTask));
            var execution = previous.ContinueWith(_ => Run(job, operation), TaskContinuationOptions.LongRunning);
            foreach (var volume in volumes) lanes[volume] = execution;
        }
        return job;
    }

    static void Run(Job job, Action<Job> operation)
    {
        try
        {
            job.WaitUntilResumed();
            if (job.IsCancelled) { job.Phase = Phase.Cancelled; return; }
            job.Phase = Phase.Scanning;
            if (operation != null)
            {
                operation(job);
                job.Phase = job.IsCancelled ? Phase.Cancelled : job.Errors.IsEmpty ? Phase.Done : Phase.Failed;
                return;
            }
            var plan = Scan(job);
            if (job.IsCancelled) { job.Phase = Phase.Cancelled; return; }

            Choice choice = Choice.Overwrite; // irrelevant when there are no conflicts
            if (plan.Conflicts.Count > 0)
            {
                job.Conflicts = plan.Conflicts;
                job.Phase = Phase.Conflicts;
                Boundary("conflicts", job, null, null);
                if (job.WaitForResolution() is not { } c) { job.Phase = Phase.Cancelled; return; }
                choice = c;
            }
            job.Phase = Phase.Copying;
            Transfer(job, plan, choice);
            job.Phase = job.IsCancelled ? Phase.Cancelled : job.Errors.IsEmpty ? Phase.Done : Phase.Failed;
        }
        catch (OperationCanceledException) { job.Phase = Phase.Cancelled; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // e.g. destination vanished mid-job: stop, and say why on the card
            job.Errors.Enqueue(ex.Message);
            job.Phase = Phase.Failed;
        }
        finally
        {
            job.Current = ""; job.CurrentSize = job.CurrentDone = 0;
            try { Finished?.Invoke(job); } finally { job.Complete(); }
        }
    }

    record FileEntry(string Source, string Relative, TransferFile.Stamp Snapshot, IReadOnlyList<TransferFile.StreamEntry> Streams)
    {
        public long Size => Streams.Sum(stream => stream.Size);
    }
    record DirectoryEntry(string Source, string Relative, TransferFile.Stamp Snapshot)
    {
        public HashSet<string> Children = new(StringComparer.OrdinalIgnoreCase);
    }
    class Plan
    {
        public List<FileEntry> Files = new();
        public List<DirectoryEntry> Dirs = new();
        public List<Conflict> Conflicts = new();
    }

    static Plan Scan(Job job)
    {
        var plan = new Plan();
        int links = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TransferFile.RequirePlainDestination(job.Destination);
        if (!Directory.Exists(job.Destination)) throw new IOException("The destination folder is unavailable.");
        void Walk(string dir, string rel)
        {
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { job.Errors.Enqueue($"{dir}: {ex.Message}"); return; }
            plan.Dirs[^1].Children.UnionWith(children.Select(child => child.Name));
            foreach (var c in children)
            {
                if (job.IsCancelled) return;
                job.WaitUntilResumed();
                Add(c, Path.Combine(rel, c.Name));
            }
        }
        void Add(FileSystemInfo fsi, string rel)
        {
            if (fsi.Attributes.HasFlag(FileAttributes.ReparsePoint)) { links++; return; } // Ferry: links reported, not reproduced
            if (!names.Add(rel)) throw new IOException("Selected inputs map to the same destination name: " + rel);
            var snapshot = TransferFile.Inspect(fsi.FullName);
            if (snapshot.Attributes.HasFlag(FileAttributes.ReparsePoint)) { links++; return; }
            string target = Path.Combine(job.Destination, rel);
            if (!Path.GetFullPath(target).StartsWith(Path.TrimEndingDirectorySeparator(job.Destination) + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("A transfer item cannot replace its destination root: " + rel);
            if (fsi is DirectoryInfo)
            {
                if (File.Exists(target)) throw new IOException("A file occupies the destination folder: " + rel);
                if (TransferFile.Streams(fsi.FullName, directory: true).Any(s => s.Name != "::$DATA"))
                    throw new IOException("Folder alternate streams cannot be preserved by this transfer; the source is retained: " + rel);
                if (snapshot.Attributes.HasFlag(FileAttributes.Encrypted))
                    throw new IOException("Directory encryption policy cannot be reproduced by this transfer; the source is retained: " + rel);
                plan.Dirs.Add(new(fsi.FullName, rel, snapshot)); Walk(fsi.FullName, rel);
            }
            else
            {
                var f = (FileInfo)fsi;
                if (Directory.Exists(target)) throw new IOException("A folder occupies the destination file: " + rel);
                var streams = TransferFile.Streams(f.FullName);
                if (snapshot.Links > 1 && !job.Notes.Contains("Copies of hard links are independent files; link relationships are not cloned."))
                    job.Notes.Enqueue("Copies of hard links are independent files; link relationships are not cloned.");
                plan.Files.Add(new FileEntry(f.FullName, rel, snapshot, streams));
                job.TotalBytes += streams.Sum(s => s.Size); job.TotalFiles++;
            }
        }
        foreach (var s in job.Sources)
        {
            if (job.IsCancelled) break;
            job.WaitUntilResumed();
            FileSystemInfo fsi = Directory.Exists(s) ? new DirectoryInfo(s) : new FileInfo(s);
            if (!fsi.Exists) { job.Errors.Enqueue($"{s}: cannot be read"); continue; }
            if (fsi is DirectoryInfo { Parent: null }) { job.Errors.Enqueue("Select files or folders inside the volume; transferring a volume root is unsupported: " + s); continue; }
            bool here = Path.GetDirectoryName(s.TrimEnd('\\'))?.Equals(job.Destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true;
            if (here && job.IsMove) { job.Errors.Enqueue($"{fsi.Name}: already in this folder"); continue; }
            if (fsi is DirectoryInfo && (job.Destination + "\\").StartsWith(fsi.FullName.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            { job.Errors.Enqueue($"{fsi.Name}: can't copy a folder into itself"); continue; }
            // a copy into the folder it is already in is a duplicate beside it, as in Explorer ("name - Copy.ext")
            if (here) { var copy = CopyName(s.TrimEnd('\\'), fsi is DirectoryInfo); job.Duplicates[s] = copy; Add(fsi, Path.GetFileName(copy)); continue; }
            if (fsi is DirectoryInfo && Directory.Exists(Path.Combine(job.Destination, fsi.Name))) job.Merged = true;
            Add(fsi, fsi.Name);
        }
        for (int i = 0; i < plan.Files.Count; i++)
        {
            var target = new FileInfo(Path.Combine(job.Destination, plan.Files[i].Relative));
            if (target.Exists)
            {
                var existing = TransferFile.Inspect(target.FullName);
                plan.Conflicts.Add(new Conflict(i, plan.Files[i].Relative, plan.Files[i].Snapshot.Size, DateTime.FromFileTimeUtc(plan.Files[i].Snapshot.Modified).ToLocalTime(), existing.Size, DateTime.FromFileTimeUtc(existing.Modified).ToLocalTime()));
            }
        }
        job.ExcludedItems = links;
        if (links > 0) job.Notes.Enqueue($"{links} reparse point(s) were left alone");
        return plan;
    }

    static void Transfer(Job job, Plan plan, Choice choice)
    {
        var conflicts = plan.Conflicts.ToDictionary(c => c.File);
        var arrivals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Arrived(string relative) { lock (arrivals) arrivals.Add(relative.Split(Path.DirectorySeparatorChar)[0]); }
        foreach (var directory in plan.Dirs)
        {
            Check(job);
            string target = Path.Combine(job.Destination, directory.Relative);
            TransferFile.RequirePlainDestination(target);
            if (!Directory.Exists(target)) { Directory.CreateDirectory(target); Arrived(directory.Relative); }
        }

        var work = new List<(FileEntry File, string Target, bool Overwrite)>();
        var reserved = plan.Files.Select(file => Path.Combine(job.Destination, file.Relative)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < plan.Files.Count; i++)
        {
            var e = plan.Files[i];
            var target = Path.Combine(job.Destination, e.Relative);
            bool overwrite = false;
            if (conflicts.TryGetValue(i, out var c))
            {
                var ch = choice == Choice.Newer && c.SourceModified <= c.TargetModified ? Choice.Skip : choice;
                if (ch == Choice.Skip) { job.SkippedFiles++; continue; }
                if (ch == Choice.Rename) target = UniqueName(target, reserved);
                else overwrite = true;
            }
            work.Add((e, target, overwrite));
        }
        bool together = job.Parallel && Fast(job.Destination) && job.Sources.All(Fast);
        var small = together ? work.Where(w => w.File.Size < ParallelBelow).ToList() : new();
        job.Concurrent = small.Count > 1;

        void One((FileEntry File, string Target, bool Overwrite) w)
        {
            var e = w.File;
            TransferFile.Stamp? copiedOutput = null;
            bool succeeded = false, acceptedOutput = false, countedCopy = false;
            Interlocked.Increment(ref job.ParallelWorkers);
            try
            {
                Check(job);
                Boundary("before-source-open", job, e.Source, w.Target);
                TransferFile.RequireCurrent(e.Source, e.Snapshot);
                if (e.Streams.Any(stream => stream.Name != "::$DATA") && !TransferFile.NamedStreams(job.Destination))
                    throw new IOException("The destination filesystem cannot preserve alternate streams: " + e.Relative);
                CheckTarget(w.Target, w.Overwrite);
                job.Current = job.Concurrent ? $"{job.ParallelWorkers} active files" : e.Relative;
                if (!job.Concurrent) { job.CurrentSize = e.Size; job.CurrentDone = 0; }
                bool renamed = false;
                if (job.IsMove)
                {
                    Boundary("before-rename", job, e.Source, w.Target); Check(job);
                    TransferFile.RequireCurrent(e.Source, e.Snapshot);
                    renamed = MoveFileEx(Long(e.Source), Long(w.Target), w.Overwrite ? 1u : 0u);
                    if (!renamed && Marshal.GetLastWin32Error() != 17)
                        throw TransferFile.Error(e.Source, Marshal.GetLastWin32Error());
                    // Only native ERROR_NOT_SAME_DEVICE selects verified copy.
                    // Drive-letter/root comparisons cannot identify mounts.
                }
                if (renamed)
                {
                    Interlocked.Add(ref job.DoneBytes, e.Size);
                    Interlocked.Increment(ref job.RenamedFiles);
                    succeeded = true;
                }
                else
                {
                    Boundary("before-copy", job, e.Source, w.Target); Check(job);
                    bool streamed = StreamData(e, w.Target, w.Overwrite);
                    ulong? copiedHash = null;
                    string error = streamed
                        ? CopyData(job, e.Source, w.Target, e.Snapshot.Size, w.Overwrite, out copiedOutput, out copiedHash)
                        : CopyOne(job, e.Source, w.Target, e.Size, w.Overwrite, out copiedOutput);
                    Check(job);
                    if (error != null) throw new IOException(error);
                    countedCopy = true; Interlocked.Increment(ref job.CopiedFiles);
                    Boundary("copy-written", job, e.Source, w.Target); Check(job);
                    if (streamed) RequireStreams(e.Source, e.Streams);
                    if (job.Verify)
                    {
                        if (!job.Concurrent) { job.Phase = Phase.Verifying; job.Current = "Verifying " + e.Relative; job.CurrentSize = e.Size * (streamed ? 1 : 2); job.CurrentDone = 0; }
                        RequireStreams(w.Target, e.Streams);
                        foreach (var stream in e.Streams)
                        {
                            Check(job);
                            var original = streamed ? copiedHash : Digest(job, TransferFile.StreamPath(e.Source, stream.Name));
                            Check(job);
                            var transferred = Digest(job, TransferFile.StreamPath(w.Target, stream.Name));
                            Check(job);
                            if (original == null || transferred == null || original != transferred)
                                throw new IOException("Transferred stream does not match its source: " + e.Relative + stream.Name);
                        }
                    }
                    TransferFile.RequireCurrent(e.Source, e.Snapshot);
                    if (TransferFile.ApplyMetadata(w.Target, e.Snapshot, out var storageChanges)) MetadataPrecision(job);
                    StoragePolicy(job, storageChanges);
                    Check(job);
                    if (job.Verify) Interlocked.Increment(ref job.VerifiedFiles);
                    acceptedOutput = true;
                    if (job.IsMove)
                    {
                        Boundary("before-source-removal", job, e.Source, w.Target); Check(job);
                        TransferFile.RemoveCopiedSource(e.Source, e.Snapshot, e.Streams, job);
                    }
                    succeeded = true;
                }
            }
            catch (OperationCanceledException) { if (!job.IsCancelled) job.Cancel(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            { job.Errors.Enqueue($"{e.Relative}: {ex.Message}"); }
            finally
            {
                if (job.IsCancelled && !acceptedOutput && copiedOutput is { } output)
                {
                    try
                    {
                        TransferFile.RemoveIncompleteCopy(w.Target, output);
                        if (countedCopy) Interlocked.Decrement(ref job.CopiedFiles);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { job.Errors.Enqueue("Incomplete copy could not be removed at " + w.Target + ": " + ex.Message); }
                }
                Interlocked.Decrement(ref job.ParallelWorkers);
                if (job.Concurrent) job.Current = $"{job.ParallelWorkers} active files";
                else if (!job.IsCancelled) job.Phase = Phase.Copying;
            }
            if (succeeded) Interlocked.Increment(ref job.DoneFiles);
            if (succeeded || (copiedOutput != null && File.Exists(w.Target))) Arrived(e.Relative);
        }

        if (small.Count > 1)
            System.Threading.Tasks.Parallel.ForEach(small, new ParallelOptions { MaxDegreeOfParallelism = ParallelWays }, (w, state) => { if (job.IsCancelled) state.Stop(); else One(w); });
        foreach (var w in small.Count > 1 ? work.Except(small) : work)
        {
            if (job.IsCancelled) break;
            One(w);
        }
        job.Arrivals = arrivals.ToList();
        if (job.IsCancelled) return;
        foreach (var directory in plan.Dirs.OrderByDescending(directory => directory.Relative.Length))
        {
            Check(job);
            try
            {
                string target = Path.Combine(job.Destination, directory.Relative);
                TransferFile.RequirePlainDestination(target);
                if (TransferFile.ApplyMetadata(target, directory.Snapshot, out var storageChanges)) MetadataPrecision(job);
                StoragePolicy(job, storageChanges);
                if (!job.IsMove) continue;
                var current = TransferFile.Inspect(directory.Source);
                if (current.Volume != directory.Snapshot.Volume || current.Id != directory.Snapshot.Id || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("A source folder changed identity; it was retained: " + directory.Source);
                string[] remaining = Directory.GetFileSystemEntries(directory.Source);
                if (remaining.Any(path => !directory.Children.Contains(Path.GetFileName(path))))
                    throw new IOException("New source-folder members remain; the folder was retained: " + directory.Source);
                if (remaining.Length == 0) Directory.Delete(directory.Source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { job.Errors.Enqueue(ex.Message); }
        }
    }
    static void Check(Job job)
    {
        job.WaitUntilResumed();
        if (job.IsCancelled) throw new OperationCanceledException();
    }
    static void MetadataPrecision(Job job)
    {
        const string note = "Destination filesystem timestamp precision differs; stored timestamps were rounded.";
        if (!job.Notes.Contains(note)) job.Notes.Enqueue(note);
    }
    static void RequireStreams(string source, IReadOnlyList<TransferFile.StreamEntry> expected)
    {
        if (!TransferFile.Streams(source).SequenceEqual(expected)) throw new IOException("A file's stream set changed; publication/source removal was refused: " + source);
    }
    static void StoragePolicy(Job job, FileAttributes changes)
    {
        void Note(string text) { lock (job.Notes) { if (!job.Notes.Contains(text)) job.Notes.Enqueue(text); } }
        if (changes.HasFlag(FileAttributes.Compressed)) Note("Compression settings differ from the source.");
        if (changes.HasFlag(FileAttributes.SparseFile)) Note("Sparse layout is not preserved; the copy may use more disk space.");
    }
    static void CheckTarget(string target, bool overwrite)
    {
        TransferFile.RequirePlainDestination(target);
        if (Directory.Exists(target)) throw new IOException("A folder occupies the destination file: " + target);
        if (!overwrite && File.Exists(target)) throw new IOException("A destination appeared after planning: " + target);
    }
#if DEBUG
    internal static Action<string, Job, string, string> VerificationBoundary;
#endif
    static void Boundary(string phase, Job job, string source, string target)
    {
#if DEBUG
        VerificationBoundary?.Invoke(phase, job, source, target);
#endif
    }
    static ulong? Digest(Job job, string path)
    {
        var hasher = new System.IO.Hashing.XxHash3();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            while (true)
            {
                job.WaitUntilResumed();
                if (job.IsCancelled) return null;
                int n = fs.Read(buffer, 0, 1024 * 1024);
                if (n == 0) break;
                hasher.Append(buffer.AsSpan(0, n));
                job.AddVerificationBytes(n);
                Boundary("verification-chunk", job, path, null);
            }
            return hasher.GetCurrentHashAsUInt64();
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    static string UniqueName(string target, HashSet<string> reserved)
    {
        string dir = Path.GetDirectoryName(target), stem = Path.GetFileNameWithoutExtension(target), ext = Path.GetExtension(target);
        for (int n = 2; n < 10000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate) && reserved.Add(candidate)) return candidate;
        }
        throw new IOException("No unoccupied destination name is available: " + target);
    }

    /// Explorer's name for a duplicate beside the original: "a - Copy.txt", then "a - Copy (2).txt"; a folder keeps
    /// its dots ("v1.2 - Copy").
    internal static string CopyName(string path, bool folder)
    {
        string dir = Path.GetDirectoryName(path), name = Path.GetFileName(path);
        string stem = folder ? name : Path.GetFileNameWithoutExtension(name), ext = folder ? "" : Path.GetExtension(name);
        for (int n = 1; n < 10000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} - Copy{(n > 1 ? $" ({n})" : "")}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("No unoccupied duplicate name is available: " + path);
    }

    // ---- several at once: on which drives ----
    internal const long ParallelBelow = 16L * 1024 * 1024;
    internal const int ParallelWays = 4; // ponytail: fixed; measured on NVMe and a 1 GbE NAS, tune per drive if it ever matters
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> fast = new(StringComparer.OrdinalIgnoreCase);

    /// A network path, or a drive Windows says has no seek penalty (SSD, NVMe). A spinning disk, or one Windows won't
    /// say about, is not.
    internal static bool Fast(string path)
    {
        if (path.StartsWith(@"\\")) return true;
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;
        return fast.GetOrAdd(root, r =>
        {
            try { if (new DriveInfo(r).DriveType == DriveType.Network) return true; } catch (ArgumentException) { return false; }
            using var h = CreateFile($@"\\.\{r.TrimEnd('\\')}", 0, 3 /* read | write sharing */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (h.IsInvalid) return false;
            var query = new STORAGE_PROPERTY_QUERY { PropertyId = 7 /* StorageDeviceSeekPenaltyProperty */ };
            return DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, ref query, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), out var seek, Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), out _, IntPtr.Zero)
                   && seek.IncursSeekPenalty == 0;
        });
    }

    const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    [StructLayout(LayoutKind.Sequential)] struct STORAGE_PROPERTY_QUERY { public int PropertyId, QueryType; public byte Extra; }
    [StructLayout(LayoutKind.Sequential)] struct DEVICE_SEEK_PENALTY_DESCRIPTOR { public uint Version, Size; public byte IncursSeekPenalty; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref STORAGE_PROPERTY_QUERY input, int inSize, out DEVICE_SEEK_PENALTY_DESCRIPTOR output, int outSize, out int returned, IntPtr overlapped);

    // ---- CopyFileExW ----
    const uint COPY_FILE_FAIL_IF_EXISTS = 0x1, COPY_FILE_NO_BUFFERING = 0x1000;
    const long UnbufferedThreshold = 64L * 1024 * 1024; // big files bypass the cache instead of evicting everything else
    const uint PROGRESS_CONTINUE = 0, PROGRESS_CANCEL = 1;

    delegate uint ProgressRoutine(long total, long transferred, long streamSize, long streamTransferred, uint stream, uint reason, IntPtr src, IntPtr dst, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CopyFileEx(string src, string dst, ProgressRoutine progress, IntPtr data, IntPtr cancel, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileEx(string source, string destination, uint flags);

    static string Long(string p) => p.StartsWith(@"\\?\") ? p : p.StartsWith(@"\\") ? @"\\?\UNC\" + p[2..] : @"\\?\" + p;
    static string LastError() => new Win32Exception(Marshal.GetLastWin32Error()).Message;

    static bool StreamData(FileEntry source, string destination, bool overwrite)
    {
        const FileAttributes storage = FileAttributes.Encrypted;
        if (source.Streams.Count != 1 || source.Streams[0].Name != "::$DATA" || (source.Snapshot.Attributes & storage) != 0) return false;
        // Native replacement handles existing streams/encryption; truncating
        // only the unnamed stream must not silently retain or lose that state.
        if (overwrite && File.Exists(destination))
            return (File.GetAttributes(destination) & storage) == 0 && TransferFile.Streams(destination).All(stream => stream.Name == "::$DATA");
        return true;
    }

    static string CopyData(Job job, string source, string destination, long size, bool overwrite, out TransferFile.Stamp? output, out ulong? copiedHash)
    {
        output = null; copiedHash = null;
        byte[] buffer = null;
        var hasher = job.Verify ? new System.IO.Hashing.XxHash3() : null;
        try
        {
            using var input = new FileStream(Long(source), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            using var target = new FileStream(Long(destination), overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan);
            output = TransferFile.ReadStamp(target.SafeFileHandle, destination);
            if (size > 0) buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
            long transferred = 0;
            while (transferred < size)
            {
                Check(job);
                int count = input.Read(buffer, 0, (int)Math.Min(size - transferred, 1024 * 1024));
                if (count == 0) throw new IOException("The source ended before its planned size: " + source);
                target.Write(buffer, 0, count);
                hasher?.Append(buffer.AsSpan(0, count));
                transferred += count; job.AddBytes(count);
                Boundary("copy-chunk", job, source, destination); Check(job);
            }
            copiedHash = hasher?.GetCurrentHashAsUInt64();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Match native copy's cleanup of a failed, incomplete write. The
            // using scopes above have released the writer before removal.
            if (output is { } owned)
                try { TransferFile.RemoveIncompleteCopy(destination, owned); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                { job.Errors.Enqueue("Incomplete copy could not be removed at " + destination + ": " + cleanup.Message); }
            return ex.Message;
        }
        finally { if (buffer != null) System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    static string CopyOne(Job job, string src, string dst, long size, bool overwrite, out TransferFile.Stamp? output)
    {
        long seen = 0;
        TransferFile.Stamp? copied = null;
        Exception observationFailure = null;
        ProgressRoutine cb = (_, transferred, _, _, _, _, _, destinationHandle, _) =>
        {
            if (transferred > seen) { job.AddBytes(transferred - seen); seen = transferred; }
            try
            {
                if (copied == null)
                {
                    using var borrowed = new Microsoft.Win32.SafeHandles.SafeFileHandle(destinationHandle, ownsHandle: false);
                    copied = TransferFile.ReadStamp(borrowed, dst);
                }
                if (transferred > 0) Boundary("copy-chunk", job, src, dst);
            }
            catch (Exception ex) { observationFailure = ex; return PROGRESS_CANCEL; } // never unwind a managed exception across the native callback
            // Pause blocks inside the copy with both handles open; resume continues exactly here.
            job.WaitUntilResumed();
            return job.IsCancelled ? PROGRESS_CANCEL : PROGRESS_CONTINUE;
        };
        uint flags = (overwrite ? 0 : COPY_FILE_FAIL_IF_EXISTS) | (size >= UnbufferedThreshold ? COPY_FILE_NO_BUFFERING : 0);
        bool ok = CopyFileEx(Long(src), Long(dst), cb, IntPtr.Zero, IntPtr.Zero, flags);
        var err = ok ? null : LastError();
        GC.KeepAlive(cb);
        output = copied;
        if (ok && output == null) output = TransferFile.Inspect(dst); // a completed empty file may produce no data callback
        if (observationFailure != null) return "Transfer observation failed: " + observationFailure.Message;
        if (!ok && job.IsCancelled) return null; // native PROGRESS_CANCEL owns cancellation during copying
        return err;
    }
}
