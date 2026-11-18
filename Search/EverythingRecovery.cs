using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorer.Search;

// SEARCH-05: what to do when Everything stops answering. Status is read with
// SendMessageTimeout so a hung Everything window cannot block this check too.
// A restart only helps a hung or silently stuck instance: while Everything
// loads or updates its index, restarting would start that work over.
// The monitor checks Everything's window twice a minute whether or not anyone is
// searching, so a hung instance is restarted even when no query is waiting on it.
internal static class EverythingRecovery
{
    internal enum State { Missing, Loading, Busy, Ready, Hung }
    internal static TimeSpan ReplyTimeout = TimeSpan.FromSeconds(45), RestartAfter = TimeSpan.FromMinutes(3),
        HungRestartAfter = TimeSpan.FromMinutes(2), RestartCooldown = TimeSpan.FromMinutes(30), MonitorInterval = TimeSpan.FromSeconds(30);
    // Environment.TickCount64 of the first of a run of hung checks (0: answering), and of the last restart.
    static long hungSince, lastRestart;
    static int restarting;
    static Timer monitor;
    // MCP-01: the read-only MCP server reports a stuck Everything but never restarts it.
    internal static bool RestartAllowed = true;
#if DEBUG
    internal static Func<State> ProbeOverride;
    internal static Func<Task<bool>> RestartOverride;
    internal static Func<long> ClockOverride;
    internal static void ResetForVerification() { hungSince = 0; lastRestart = 0; }
#endif
    static long Now()
    {
#if DEBUG
        if (ClockOverride != null) return ClockOverride();
#endif
        return Environment.TickCount64;
    }
    const string WindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
    const uint IpcMessage = 0x400, IsDbLoaded = 401, IsDbBusy = 402, AbortIfHung = 0x2, Block = 0x1;

    // The app's own window starts this; verification profiles and the MCP server never do.
    internal static void StartMonitor() => monitor ??= new Timer(_ => _ = CheckAsync(), null, MonitorInterval, MonitorInterval);
    internal static void StopMonitor() { monitor?.Dispose(); monitor = null; }

    // One monitor check: a hung window is restarted once it has stayed hung for
    // HungRestartAfter; any answer (even loading or busy) ends the run.
    internal static async Task<string> CheckAsync()
    {
        var state = Probe();
        if (state != State.Hung) { Interlocked.Exchange(ref hungSince, 0); return null; }
        long now = Now();
        Interlocked.CompareExchange(ref hungSince, now, 0);
        if (now - Interlocked.Read(ref hungSince) < HungRestartAfter.TotalMilliseconds) return null;
        return await RestartOnceAsync().ConfigureAwait(false);
    }

    internal static async Task<string> AfterTimeoutAsync(TimeSpan unanswered)
    {
        var state = await Task.Run(Probe).ConfigureAwait(false);
        string waited = $"Everything didn't answer within {ReplyTimeout.TotalSeconds:0} seconds";
        switch (state)
        {
            case State.Missing: return "Everything is unavailable. Start Everything, then search again.";
            case State.Loading: return waited + " because it is still loading its index. Search again when it's ready.";
            case State.Busy: return waited + " because it is busy updating its index. Search again in a moment.";
            case State.Hung:
                // The query already waited ReplyTimeout on a window that no longer answers status
                // messages: that is the hang, whatever the caller's cancellations did to the clock.
                Interlocked.CompareExchange(ref hungSince, Now() - (long)ReplyTimeout.TotalMilliseconds, 0);
                return await RestartOnceAsync().ConfigureAwait(false) ?? "Everything has stopped responding. Restart Everything, then search again.";
        }
        if (unanswered < RestartAfter) return waited + ". Search again in a moment.";
        return await RestartOnceAsync().ConfigureAwait(false) ?? "Everything still isn't answering. Restart Everything, then search again.";
    }

    // At most one restart per RestartCooldown: a restarted Everything rebuilds or reloads its
    // index, and an instance that hangs again straight away needs its owner, not a restart loop.
    static async Task<string> RestartOnceAsync()
    {
        if (!RestartAllowed) return "Everything has stopped answering. Restart Everything, then search again.";
        long now = Now(), last = Interlocked.Read(ref lastRestart);
        if (last != 0 && now - last < RestartCooldown.TotalMilliseconds) return null;
        if (Interlocked.Exchange(ref restarting, 1) == 1) return "Everything stopped answering; Pathweft is restarting it. Search again once it has loaded its index.";
        try
        {
            Interlocked.Exchange(ref lastRestart, now);
            bool done = await RestartAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref hungSince, 0);
            return done ? "Everything stopped answering, so Pathweft restarted it. Search again once it has loaded its index."
                        : "Everything stopped answering and Pathweft couldn't restart it. Restart Everything, then search again.";
        }
        finally { Interlocked.Exchange(ref restarting, 0); }
    }

    // Bounded IsDBLoaded for the IPC dispatcher: the SDK's own call waits on a hung window forever.
    internal static State Loaded()
    {
#if DEBUG
        if (ProbeOverride != null) return ProbeOverride();
#endif
        var window = FindWindowW(WindowClass, null);
        if (window == IntPtr.Zero) return State.Missing;
        if (!Ask(window, IsDbLoaded, out var loaded)) return State.Hung;
        return loaded == 0 ? State.Loading : State.Ready;
    }

    internal static State Probe()
    {
#if DEBUG
        if (ProbeOverride != null) return ProbeOverride();
#endif
        var window = FindWindowW(WindowClass, null);
        if (window == IntPtr.Zero) return State.Missing;
        if (!Ask(window, IsDbLoaded, out var loaded)) return State.Hung;
        if (loaded == 0) return State.Loading;
        if (!Ask(window, IsDbBusy, out var busy)) return State.Hung;
        return busy != 0 ? State.Busy : State.Ready;
    }

    static bool Ask(IntPtr window, uint code, out nuint result)
        => SendMessageTimeoutW(window, IpcMessage, code, 0, AbortIfHung | Block, 5000, out result) != 0;

    // Restart the exact client instance that owns the IPC window: ask it to exit,
    // stop only that process identity if it remains, then start it again with its
    // original command line. The Everything service is a different process.
    static async Task<bool> RestartAsync()
    {
#if DEBUG
        if (RestartOverride != null) return await RestartOverride().ConfigureAwait(false);
#endif
        try
        {
            var window = FindWindowW(WindowClass, null);
            if (window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId) return false;
            string path = process.MainModule.FileName;
            var started = process.StartTime.ToUniversalTime().Ticks;
            var identity = ((int)pid, started);
            string command = PerformanceProcessMetadataReader.Read([identity], CancellationToken.None).TryGetValue(identity, out var metadata) ? metadata.CommandLine : null;
            string arguments = Arguments(command, path);
            using (var exit = Process.Start(new ProcessStartInfo(path, "-exit") { UseShellExecute = false, CreateNoWindow = true }))
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await process.WaitForExitAsync(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                if (exit is { HasExited: false }) exit.Kill();
            }
            if (!process.HasExited)
            {
                process.Refresh();
                if (process.StartTime.ToUniversalTime().Ticks != started) return false;
                process.Kill();
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            using var again = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
            return again != null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or IOException)
        { return false; }
    }

    // The original arguments after the executable, or -startup when Windows
    // did not report the command line.
    internal static string Arguments(string command, string path)
    {
        if (string.IsNullOrWhiteSpace(command) || command == "Unavailable") return "-startup";
        command = command.TrimStart();
        if (command.StartsWith('"')) { int end = command.IndexOf('"', 1); return end < 0 ? "-startup" : command[(end + 1)..].Trim(); }
        if (command.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return command[path.Length..].Trim();
        int space = command.IndexOf(' ');
        return space < 0 ? "" : command[(space + 1)..].Trim();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SendMessageTimeoutW(IntPtr hwnd, uint message, nuint wparam, nint lparam, uint flags, uint timeout, out nuint result);
}
