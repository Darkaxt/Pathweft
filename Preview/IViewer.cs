// Ported from QuickLook.Common/Plugin/IViewer.cs and ContextObject.cs.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
// Async loading/cancellation and a per-surface context replace donor window globals.
// See vendor/QuickLook/NOTICES.txt and COPYING-GPL-3.0.txt.
using System.Windows;

namespace FileExplorer.Preview;

internal interface IViewer
{
    bool CanHandle(string path);
    void Init();
    void Prepare(string path, ContextObject context);
    Task View(string path, ContextObject context);
    void Cleanup();
}

internal sealed class ContextObject
{
    readonly Func<bool> current;
    readonly Action<UIElement> publish;
    readonly Action<Task> track;
    internal ContextObject(CancellationToken token, Func<bool> current, Action<UIElement> publish, Action<Task> track, bool autoplay)
    { Token = token; this.current = current; this.publish = publish; this.track = track; Autoplay = autoplay; }
    internal CancellationToken Token { get; }
    internal bool IsCurrent => !Token.IsCancellationRequested && current();
    internal bool Autoplay { get; }
    internal string Title { get; set; } = "";
    internal bool IsBusy { get; set; } = true;
    internal UIElement ViewerContent { set { if (IsCurrent) publish(value); } }
    internal void Track(Task work) => track(work);
}
