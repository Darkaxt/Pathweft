using System.Windows.Threading;
using FileExplorer.Search;

namespace FileExplorer;

/// CONTENT-01: content-index badges in a pane. One service request per listing (the folder's direct children),
/// refreshed while the worker runs; folders outside every content scope ask nothing.
public partial class PaneView
{
    int contentVersion;
    // A folder on a drive or share, listed in the pane (not a search, widget or virtual folder).
    internal bool CanIndexContents => Listed && !IsWidget && !InSearch && System.IO.Path.IsPathFullyQualified(Dir) && !IsVirtual(Dir);

    void ContentIndexChanged() => Dispatcher.BeginInvoke(RefreshContentStates, DispatcherPriority.Background);

    internal async void RefreshContentStates()
    {
        if (InSearch || items == null) return;
        string folder = Dir;
        int version = ++contentVersion;
        if (ContentIndexing.Covering(folder) == null)
        {
            foreach (var entry in items) entry.Content = null;
            return;
        }
        Dictionary<string, ContentFile> states;
        try { states = await ContentIndexing.StatesAsync(folder); }
        catch (InvalidOperationException) { return; } // the service refused or went away: keep what is shown
        if (version != contentVersion || InSearch || !string.Equals(folder, Dir, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var entry in items)
            entry.Content = !entry.IsDir && states.TryGetValue(entry.Path, out var file) ? file : null;
    }
}
