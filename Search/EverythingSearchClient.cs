// Selected queue/reply-correlation and result-reading code adapted from
// EverythingToolbar, MIT, Copyright (c) 2023 Stephan Rumswinkel.
// See vendor/EverythingSDK/NOTICES.txt for the pinned source and terms.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FileExplorer.Search;

internal sealed record IndexedHit(string Path, bool IsFolder, long Size, DateTime Modified);
internal sealed record IndexedPage(IReadOnlyList<IndexedHit> Hits, uint Total, string Engine);

// One SDK state owner for both panes. Native SendMessage calls and the reply
// window live on a dedicated STA dispatcher; an indexing stall never blocks WPF.
internal sealed class EverythingSearchClient
{
    static readonly Lazy<EverythingSearchClient> shared = new(() => new());
    internal static EverythingSearchClient Shared => shared.Value;
    internal static Task StopIfStartedAsync() => shared.IsValueCreated ? shared.Value.StopAsync() : Task.CompletedTask;
    readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Queue<Request> queue = new();
    Request current;
    HwndSource window;
    uint nextReply;
    bool closing;

    EverythingSearchClient()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                window = new HwndSource(new HwndSourceParameters("Pathweft.Everything.Replies") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
                window.AddHook(Receive);
                ready.SetResult(dispatcher);
                Dispatcher.Run();
                window.Dispose();
                Native.Everything_CleanUp();
                stopped.TrySetResult();
            }
            catch (Exception ex) { ready.TrySetException(ex); stopped.TrySetException(ex); }
        }) { IsBackground = true, Name = "Pathweft Everything IPC" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal async Task<IndexedPage> QueryAsync(string expression, string instance, uint offset, uint count, string sort, bool descending, CancellationToken token)
    {
        var dispatcher = await ready.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var request = new Request(expression, instance, offset, count, Sort(sort, descending), token);
        using var cancellation = token.Register(() =>
        {
            request.Completion.TrySetCanceled(token);
            dispatcher.BeginInvoke(ProcessNext);
        });
        await dispatcher.InvokeAsync(() =>
        {
            if (closing) request.Completion.TrySetException(new ObjectDisposedException(nameof(EverythingSearchClient)));
            else { queue.Enqueue(request); ProcessNext(); }
        });
        return await request.Completion.Task.ConfigureAwait(false);
    }

    internal async Task StopAsync()
    {
        var dispatcher = await ready.Task.ConfigureAwait(false);
        await dispatcher.InvokeAsync(() =>
        {
            closing = true;
            current?.Completion.TrySetCanceled();
            current?.Dispose();
            current = null;
            while (queue.TryDequeue(out var item)) item.Completion.TrySetCanceled();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }).Task.ConfigureAwait(false);
        await stopped.Task.ConfigureAwait(false);
    }

    static uint Sort(string column, bool descending) => (column switch
    {
        "Name" => 1u, "Size" => 5u, "Type" => 7u, "Modified" => 13u,
        _ => throw new ArgumentException("Everything cannot sort by that column. Choose Name, Size, Type or Modified.")
    }) + (descending ? 1u : 0u);

    void ProcessNext()
    {
        if (current != null && !current.Token.IsCancellationRequested) return;
        current?.Dispose();
        current = null;
        if (closing) return;
        while (queue.TryDequeue(out var request))
        {
            if (request.Token.IsCancellationRequested) continue;
            current = request;
            try
            {
                string cls = "EVERYTHING_TASKBAR_NOTIFICATION" + (request.Instance.Length == 0 ? "" : "_(" + request.Instance + ")");
                var engine = Native.FindWindowW(cls, null);
                if (engine == IntPtr.Zero) throw new InvalidOperationException("Everything is unavailable. Start Everything, then search again.");
                if (Native.IsHungAppWindow(engine)) throw new InvalidOperationException("Everything is not responding. Search again when it is ready.");
                Native.GetWindowThreadProcessId(engine, out uint pid);
                var process = request.Process = Process.GetProcessById((int)pid);
                string version = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileVersion;
                if (!Version.TryParse(version, out var parsed) || parsed < new Version(1, 4, 1))
                    throw new InvalidOperationException("Everything 1.4.1 or later is required.");
                request.Engine = $"Everything {version} · {(request.Instance.Length == 0 ? "default instance" : request.Instance)}";
                request.OnExit = (_, _) => window.Dispatcher.BeginInvoke(() =>
                {
                    if (current != request) return;
                    request.Completion.TrySetException(new InvalidOperationException("Everything exited. Its index has not been replaced; start the selected instance and search again."));
                    current = null;
                    request.Dispose();
                    ProcessNext();
                });
                process.Exited += request.OnExit;
                process.EnableRaisingEvents = true;
                // The pinned official SDK targets the default instance. Do not
                // redirect a named-instance request into another user's index.
                if (request.Instance.Length != 0) throw new InvalidOperationException("This build supports the existing default Everything instance only.");
                if (!Native.Everything_IsDBLoaded()) throw new InvalidOperationException("Everything is loading its index. Search again after it is ready; no filesystem fallback was started.");
                Native.Everything_Reset();
                Native.Everything_SetSearchW(request.Expression);
                Native.Everything_SetRequestFlags(0x4 | 0x10 | 0x40);
                Native.Everything_SetSort(request.Sort);
                Native.Everything_SetMax(request.Count);
                Native.Everything_SetOffset(request.Offset);
                Native.Everything_SetReplyWindow(window.Handle);
                request.Reply = ++nextReply;
                Native.Everything_SetReplyID(request.Reply);
                if (!Native.Everything_QueryW(false)) throw new InvalidOperationException($"Everything query failed (SDK error {Native.Everything_GetLastError()}). Search again to reconnect.");
                return;
            }
            catch (Exception ex) { request.Completion.TrySetException(ex); request.Dispose(); current = null; }
        }
    }

    IntPtr Receive(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (current == null || !Native.Everything_IsQueryReply((uint)message, wparam, lparam, current.Reply)) return IntPtr.Zero;
        var completed = current;
        current = null;
        try
        {
            if (!completed.Token.IsCancellationRequested)
            {
                var hits = new List<IndexedHit>();
                uint count = Native.Everything_GetNumResults();
                for (uint i = 0; i < count; i++)
                {
                    uint length = Native.Everything_GetResultFullPathNameW(i, null, 0);
                    var path = new StringBuilder(checked((int)length + 1));
                    Native.Everything_GetResultFullPathNameW(i, path, (uint)path.Capacity);
                    bool sizeKnown = Native.Everything_GetResultSize(i, out long size);
                    bool dateKnown = Native.Everything_GetResultDateModified(i, out long date);
                    hits.Add(new(path.ToString(), !Native.Everything_IsFileResult(i), sizeKnown ? size : -2,
                        dateKnown && date > 0 ? DateTime.FromFileTime(date) : default));
                }
                completed.Completion.TrySetResult(new(hits, Native.Everything_GetTotResults(), completed.Engine));
            }
        }
        catch (Exception ex) { completed.Completion.TrySetException(ex); }
        finally { completed.Dispose(); }
        handled = true;
        window.Dispatcher.BeginInvoke(ProcessNext);
        return new IntPtr(1);
    }

    sealed class Request(string expression, string instance, uint offset, uint count, uint sort, CancellationToken token) : IDisposable
    {
        internal readonly string Expression = expression, Instance = instance;
        internal readonly uint Offset = offset, Count = count, Sort = sort;
        internal readonly CancellationToken Token = token;
        internal uint Reply;
        internal string Engine;
        internal Process Process;
        internal EventHandler OnExit;
        internal readonly TaskCompletionSource<IndexedPage> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose()
        {
            if (Process == null) return;
            if (OnExit != null) Process.Exited -= OnExit;
            Process.Dispose(); Process = null;
        }
    }

    static class Native
    {
        const string Dll = "Everything64.dll";
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport(Dll)] internal static extern bool Everything_IsDBLoaded();
        [DllImport(Dll)] internal static extern void Everything_Reset();
        [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern void Everything_SetSearchW(string search);
        [DllImport(Dll)] internal static extern void Everything_SetRequestFlags(uint flags);
        [DllImport(Dll)] internal static extern void Everything_SetSort(uint sort);
        [DllImport(Dll)] internal static extern void Everything_SetMax(uint count);
        [DllImport(Dll)] internal static extern void Everything_SetOffset(uint offset);
        [DllImport(Dll)] internal static extern void Everything_SetReplyWindow(IntPtr hwnd);
        [DllImport(Dll)] internal static extern void Everything_SetReplyID(uint id);
        [DllImport(Dll)] internal static extern bool Everything_QueryW(bool wait);
        [DllImport(Dll)] internal static extern uint Everything_GetLastError();
        [DllImport(Dll)] internal static extern bool Everything_IsQueryReply(uint message, IntPtr wparam, IntPtr lparam, uint id);
        [DllImport(Dll)] internal static extern uint Everything_GetNumResults();
        [DllImport(Dll)] internal static extern uint Everything_GetTotResults();
        [DllImport(Dll)] internal static extern bool Everything_IsFileResult(uint index);
        [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder path, uint length);
        [DllImport(Dll)] internal static extern bool Everything_GetResultSize(uint index, out long size);
        [DllImport(Dll)] internal static extern bool Everything_GetResultDateModified(uint index, out long date);
        [DllImport(Dll)] internal static extern void Everything_CleanUp();
    }
}
