// QuickLook's prepare/view/cleanup ownership adapted to independent WPF surfaces.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Preview;

internal sealed class PreviewSession : IDisposable
{
    static readonly HashSet<PreviewSession> sessions = new();
    static readonly HashSet<Task> pending = new();
    readonly MainWindow owner;
    readonly ContentControl target;
    readonly Action<string> title;
    readonly bool autoplay;
    CancellationTokenSource cancellation;
    IViewer viewer;
    bool disposed;
    long generation;
    internal Entry Entry { get; private set; }
    internal string ModuleId { get; private set; }
    internal string State { get; private set; } = "empty";
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal long Generation => generation;
#if DEBUG
    internal IViewer ViewerForVerification => viewer;
#endif
    internal Dictionary<string, int> Initializations { get; } = new();

    internal PreviewSession(MainWindow owner, ContentControl target, Action<string> title, bool autoplay = false)
    { this.owner = owner; this.target = target; this.title = title; this.autoplay = autoplay; sessions.Add(this); }

    internal void Show(Entry entry)
    {
        if (disposed) return;
        Clear();
        Entry = entry;
        if (entry == null || entry.IsUp) return;
        var revision = generation;
        cancellation = new();
        var token = cancellation.Token;
        title(entry.Name);
        State = "loading";
        target.Content = BuiltinViewer.Message("Loading preview…");
        Completion = Load(entry, revision, token);
        Track(Completion);
    }

    async Task Load(Entry entry, long revision, CancellationToken token)
    {
        IViewer selected = null;
        bool Current() => !disposed && generation == revision;
        try
        {
            foreach (var module in PreviewModules.Candidates(entry))
            {
                token.ThrowIfCancellationRequested();
                IViewer candidate = module.Id == "f3d" ? new F3dViewer(owner) : new BuiltinViewer(module, owner, entry);
                if (!await Task.Run(() => candidate.CanHandle(entry.Path), token)) { candidate.Cleanup(); continue; }
                if (!Current() || token.IsCancellationRequested) { candidate.Cleanup(); return; }
                selected = viewer = candidate;
                ModuleId = module.Id;
                Initializations[module.Id] = Initializations.GetValueOrDefault(module.Id) + 1;
                candidate.Init();
                var context = new ContextObject(token, Current, content => target.Content = content, Track, autoplay);
                candidate.Prepare(entry.Path, context);
                await candidate.View(entry.Path, context);
                if (Current() && !token.IsCancellationRequested)
                { State = "ready"; title(context.Title); context.IsBusy = false; }
                return;
            }
            if (Current() && !token.IsCancellationRequested)
            {
                State = "unsupported";
                target.Content = BuiltinViewer.Message("No enabled preview module can display this file.\nChoose preview modules in Settings.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (Current() && !token.IsCancellationRequested)
            { State = "error"; target.Content = BuiltinViewer.Message("Preview unavailable\n" + ex.Message); }
            selected?.Cleanup();
        }
        finally
        {
            if (!Current() || token.IsCancellationRequested) selected?.Cleanup();
        }
    }

    internal void Clear()
    {
        ++generation;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        viewer?.Cleanup(); viewer = null;
        target.Content = null; title("");
        Entry = null; ModuleId = null; State = "empty";
    }
    internal void Refresh() { if (Entry is { } entry) Show(entry); }
    internal bool HandleKey(System.Windows.Input.Key key) => viewer is BuiltinViewer builtin && builtin.HandleKey(key);
    internal static void RefreshAll() { foreach (var session in sessions.ToArray()) session.Refresh(); }
    internal static void ClearAll() { foreach (var session in sessions.ToArray()) session.Clear(); }
    internal static async Task DrainAsync()
    {
        while (pending.Count > 0)
        { await Task.WhenAll(pending.ToArray()); pending.RemoveWhere(task => task.IsCompleted); }
    }
    static void Track(Task work)
    {
        pending.Add(work);
        _ = Observe(work);
    }
    static async Task Observe(Task work)
    {
        try { await work; }
        finally { pending.Remove(work); }
    }
    public void Dispose() { if (disposed) return; Clear(); disposed = true; sessions.Remove(this); }
}
