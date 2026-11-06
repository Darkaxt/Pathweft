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
    ArchiveContent.Lease archiveContent;
    List<Task> requestWork = new();
    bool disposed;
    long generation;
    internal Entry Entry { get; private set; }
    internal string ModuleId { get; private set; }
    internal string RendererId {get;private set;}
    internal string RendererDescription {get;private set;}
    string preferredModule;
    internal ContextMenu Menu {get;}=new();
    internal string State { get; private set; } = "empty";
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal long Generation => generation;
#if DEBUG
    internal IViewer ViewerForVerification => viewer;
    internal string ArchiveDirectoryForVerification=>archiveContent?.Directory;
#endif
    internal Dictionary<string, int> Initializations { get; } = new();

    internal PreviewSession(MainWindow owner, ContentControl target, Action<string> title, bool autoplay = false)
    { this.owner = owner; this.target = target; this.title = title; this.autoplay = autoplay; sessions.Add(this); target.ContextMenu=Menu;Menu.Opened+=(_,_)=>FillMenu(); }

    internal void Show(Entry entry,string moduleId=null)
    {
        if (disposed) return;
        Clear();
        Entry = entry;
        preferredModule=moduleId;
        if (entry == null || entry.IsUp) return;
        var revision = generation;
        cancellation = new();
        var token = cancellation.Token;
        var work = requestWork = new();
        title(entry.Name);
        State = "loading";
        target.Content = BuiltinViewer.Message("Loading preview…");
        Completion = Load(entry, revision, token, work);
        Track(Completion);
    }

    async Task Load(Entry entry, long revision, CancellationToken token, List<Task> work)
    {
        IViewer selected = null;
        bool Current() => !disposed && generation == revision;
        try
        {
            string path = entry.Path;
            var candidates=PreviewModules.Candidates(entry).Where(m=>preferredModule==null||m.Id==preferredModule).ToArray();
            bool scopedHtml=candidates.FirstOrDefault()?.Id=="html";
            if (!scopedHtml && !entry.IsDir && PaneView.ArchiveOf(path) is { Inner.Length: > 0 })
            {
                var extracted = await Task.Run(() => ArchiveContent.Preview(path), token);
                if (!Current() || token.IsCancellationRequested) { extracted.Dispose(); return; }
                archiveContent = extracted; path = extracted.File;
            }
            foreach (var module in candidates)
            {
                token.ThrowIfCancellationRequested();
                IViewer candidate = module.Id switch
                {
                    "f3d"=>new F3dViewer(owner),"html"=>new HtmlViewer(entry),"csv"=>new CsvViewer(owner),
                    "database"=>new DatabaseViewer(owner),"mail"=>new MailViewer(entry),"certificate"=>new CertificateViewer(owner),
                    "design"=>new DesignThumbnailViewer(),"binary"=>new BinaryViewer(owner),"office"=>new OfficeViewer(owner,entry),"photocraft"=>new OfficeViewer(owner,entry,"photocraft"),"vectorcraft"=>new OfficeViewer(owner,entry,"vectorcraft"),"figcraft"=>new OfficeViewer(owner,entry,"figcraft"),"pdfcraft"=>new OfficeViewer(owner,entry,"pdfcraft"),"chm"=>new CompiledHelpViewer(owner,entry),
                    "app" or "elf" or "dump" or "prefetch" or "clsid" or "mediainfo"=>new InspectionViewer(module.Id),
                    _=>new BuiltinViewer(module, owner, entry)
                };
                if (!await Task.Run(() => candidate.CanHandle(path), token)) { candidate.Cleanup(); continue; }
                if (!Current() || token.IsCancellationRequested) { candidate.Cleanup(); return; }
                selected = viewer = candidate;
                ModuleId = module.Id;
                RendererId=module.Id;RendererDescription=module.Name;
                Initializations[module.Id] = Initializations.GetValueOrDefault(module.Id) + 1;
                candidate.Init();
                var context = new ContextObject(token, Current, content => target.Content = content, task => { work.Add(task); Track(task); }, autoplay,
                    (name,details)=>{RendererId=name;RendererDescription=string.IsNullOrWhiteSpace(details)?name:name+"\n"+details;title(entry.Name);});
                candidate.Prepare(path, context);
                await candidate.View(path, context);
                if (Current() && !token.IsCancellationRequested)
                { State = "ready"; title(entry.Name); context.IsBusy = false; }
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
        if (archiveContent is { } extracted)
        {
            archiveContent = null;
            Track(ReleaseContent(extracted, Completion, requestWork));
        }
        RendererId=RendererDescription=null;target.Content = null; title("");
        Entry = null; ModuleId = null; State = "empty";
    }
    internal void Refresh() { if (Entry is { } entry) Show(entry,PreviewModules.All.Any(m=>m.Id==preferredModule&&m.Enabled)?preferredModule:null); }
    void FillMenu()
    {
        Menu.Items.Clear();var entry=Entry;if(entry==null)return;
        long current=generation;
        foreach(var module in PreviewModules.Candidates(entry))
        {
            var item=new MenuItem{Header=module.Name,IsCheckable=true,IsChecked=ModuleId==module.Id};
            item.Click+=(_,_)=>{if(!disposed&&current==generation)Show(entry,module.Id);};Menu.Items.Add(item);
        }
    }
    static async Task ReleaseContent(ArchiveContent.Lease extracted, Task load, List<Task> work)
    {
        try
        {
            await load;
            int observed = 0;
            while (observed < work.Count)
            { var pendingWork = work.Skip(observed).ToArray(); observed = work.Count; await Task.WhenAll(pendingWork); }
        }
        finally { extracted.Dispose(); }
    }
    internal bool HandleKey(System.Windows.Input.Key key) => viewer is F3dViewer model ? model.HandleKey(key) : viewer is BuiltinViewer builtin && builtin.HandleKey(key);
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
