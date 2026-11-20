using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer.Search;

internal enum ContentState { Pending, Current, Stale, Failed, Unsupported, Unavailable }
// A file's size and last-write time (FILETIME, UTC), as the name index records them.
internal readonly record struct ContentStamp(long Size, long Modified);
internal sealed record ContentFile(string Path, ContentState State, long Indexed, string Error)
{
    internal static ContentState? ParseState(string state) => state switch
    {
        "pending" => ContentState.Pending, "current" => ContentState.Current, "stale" => ContentState.Stale,
        "failed" => ContentState.Failed, "unsupported" => ContentState.Unsupported, "unavailable" => ContentState.Unavailable, _ => null,
    };
}
internal sealed record ContentWork(long Id, string Path, ContentStamp Stamp);
internal sealed record ContentScope(string Path, long Reconciled, string Error);
internal sealed record ContentScopeStatus(string Path, long Files, long Pending, long Current, long Stale, long Failed, long Unsupported, long Unavailable, long Reconciled, string Error);
internal sealed record ContentStatus(IReadOnlyList<ContentScopeStatus> Scopes, long StoreBytes, long Waiting);

// CONTENT-01/04/06, Pathweft's side (spec 1.44 split by where it runs). The index service keeps the content
// scopes, tracks which files need reading, stores the terms and answers `content:`. Pathweft, as the user, starts
// the extraction worker when the service has files waiting, and shows the toggle, badges and Settings.
internal static class ContentIndexing
{
    static readonly object gate = new();
    static volatile string[] scopes = [];
    static volatile bool available;
    static Timer periodic, progress;
    static Process worker;
    static SafeFileHandle job;
    static long lastWaiting = -1;
    static readonly TimeSpan Every = TimeSpan.FromMinutes(1); // the service reconciles its scopes about once a minute

    // Raised (on a worker thread) when indexed states may have changed: panes refresh their badges.
    internal static event Action Changed;
    internal static bool Running { get { lock (gate) return worker is { HasExited: false }; } }
    // The running service has the content index (protocol 2).
    internal static bool Available => available;
    internal static IReadOnlyList<string> Scopes => scopes;

    static IndexServiceClient Service => IndexedSearch.Service;

    // Called once at startup: learns whether the service has a content index, its scopes, and starts checking.
    internal static void Start() => _ = Task.Run(async () =>
    {
        await Check();
        lock (gate) periodic ??= new Timer(_ => _ = Check(), null, Every, Every);
    });

    // Reads the scopes and the queue; starts the worker when files are waiting.
    internal static async Task Check()
    {
        try
        {
            var status = await Service.StatusAsync(CancellationToken.None).ConfigureAwait(false);
            available = status?.HasContentIndex == true;
            if (!available) { if (scopes.Length > 0) { scopes = []; Changed?.Invoke(); } return; }
            await RefreshScopes().ConfigureAwait(false);
            var content = await Service.ContentStatusAsync(CancellationToken.None).ConfigureAwait(false);
            if (content == null) return;
            if (content.Waiting > 0) Kick();
            if (Interlocked.Exchange(ref lastWaiting, content.Waiting) != content.Waiting) Changed?.Invoke();
        }
        catch (InvalidOperationException error) { Trace.WriteLine("Content index: " + error.Message); }
    }

    static async Task RefreshScopes()
    {
        var list = await Service.ContentScopesAsync(CancellationToken.None).ConfigureAwait(false);
        if (list == null) return;
        var paths = list.Select(s => s.Path).ToArray();
        bool same = paths.Length == scopes.Length && paths.All(p => scopes.Contains(p, StringComparer.OrdinalIgnoreCase));
        scopes = paths;
        if (!same) Changed?.Invoke();
    }

    internal static string Normalize(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    internal static bool Under(string path, string folder)
    {
        string prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + "\\";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // The scope that includes this folder (itself or an ancestor), or null.
    internal static string Covering(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder)) return null;
        string path = Normalize(folder);
        return scopes.FirstOrDefault(scope => scope.Equals(path, StringComparison.OrdinalIgnoreCase) || Under(path, scope));
    }

    internal static async Task Enable(string folder)
    {
        await Service.ContentScopeAddAsync(Normalize(folder), CancellationToken.None);
        await RefreshScopes();
        Changed?.Invoke();
        Kick(); // the service lists the new scope's files right away; the worker waits for them
    }

    // Removes the scope; the service purges its terms except files another scope covers. Source files are untouched.
    internal static async Task Disable(string scope)
    {
        await Service.ContentScopeRemoveAsync(scope, CancellationToken.None);
        await RefreshScopes();
        Changed?.Invoke();
    }

    // Starts the worker unless it runs; it exits when the service has nothing left for it.
    internal static void Kick()
    {
        try
        {
            lock (gate)
            {
                if (!available || worker is { HasExited: false }) return;
                worker = Launch();
                progress ??= new Timer(_ => { if (Running) Changed?.Invoke(); });
                progress.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception) { Trace.WriteLine("Content indexing did not start: " + error.Message); }
    }

    // One job for every run: closing Pathweft ends the worker and its document workers. The job caps their
    // memory and keeps them below normal priority; the worker also enters background mode for its own I/O.
    static Process Launch()
    {
        if (job == null)
        {
            job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid) { job = null; throw new System.ComponentModel.Win32Exception(); }
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 | 0x200 | 0x20, PriorityClass = 0x4000 }, JobMemory = (nuint)(2048L << 20) };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) { var code = Marshal.GetLastWin32Error(); job.Dispose(); job = null; throw new System.ComponentModel.Win32Exception(code); }
        }
        var start = new ProcessStartInfo(Program.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--content-index-worker"); start.ArgumentList.Add(Environment.ProcessId.ToString());
#if DEBUG
        if (IndexedSearch.ServiceOverride is { } fake) start.ArgumentList.Add(fake.PipeName); // verification: the worker talks to the fake service
#endif
        var process = Process.Start(start) ?? throw new IOException("The content indexer did not start.");
        if (!AssignProcessToJobObject(job, process.Handle)) { var code = Marshal.GetLastWin32Error(); try { process.Kill(); } catch (InvalidOperationException) { } throw new System.ComponentModel.Win32Exception(code); }
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (gate) progress?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Changed?.Invoke();
        };
        return process;
    }

#if DEBUG
    internal static Process WorkerForVerification { get { lock (gate) return worker; } }
    internal static void ResetForVerification()
    {
        lock (gate)
        {
            if (worker is { HasExited: false }) { worker.Kill(); worker.WaitForExit(); }
            worker = null; scopes = []; available = false; lastWaiting = -1;
            periodic?.Dispose(); periodic = null;
        }
    }
#endif

    // ---- What panes ask ----
    // Direct children of a folder in a scope, by path; empty for folders outside every scope.
    internal static async Task<Dictionary<string, ContentFile>> StatesAsync(string folder) =>
        Covering(folder) == null ? new(StringComparer.OrdinalIgnoreCase)
            : await Service.ContentStateAsync(Normalize(folder), CancellationToken.None).ConfigureAwait(false) ?? new(StringComparer.OrdinalIgnoreCase);

    // How a file's indexed state reads in its tooltip.
    internal static string Describe(ContentFile file) => file.State switch
    {
        ContentState.Current => "Contents indexed " + When(file.Indexed),
        ContentState.Pending => "Contents waiting to be indexed",
        ContentState.Stale => (file.Indexed > 0 ? "Contents indexed " + When(file.Indexed) + ", changed since" : "Contents changed while being indexed") + (file.Error is { } e ? " (" + e + ")" : ""),
        ContentState.Failed => "Contents could not be indexed: " + file.Error,
        ContentState.Unsupported => "Contents not indexed: " + file.Error,
        ContentState.Unavailable => (file.Indexed > 0 ? "Contents indexed " + When(file.Indexed) + "; " : "") + "the file is not available now",
        _ => null,
    };
    internal static string When(long filetime) => filetime <= 0 ? "never" : DateTime.FromFileTimeUtc(filetime).ToLocalTime().ToString("g");

    [StructLayout(LayoutKind.Sequential)] struct BasicLimits
    {
        internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcessLimit; internal nuint Affinity; internal uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
    {
        internal BasicLimits Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] static extern bool SetInformationJobObject(SafeFileHandle job, int info, ref ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
