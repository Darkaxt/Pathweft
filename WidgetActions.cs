using System.IO;
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    internal TaskbarSearchHost taskbarSearch;
    internal string WidgetFolder => active?.Dir is { Length: > 0 } path ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal string WidgetDefaultDestination => Other.Dir;
    internal void ApplyTaskbarSearch() => taskbarSearch?.ApplySettings();
    internal void OpenWidgetSettings() { Show(); OpenSettings(); }
    internal void OpenWidgetArchive(PaneView pane, Entry entry) => OpenFromArchive(pane, entry);
    internal void SubmitWidgetDrop(PaneView pane, List<string> paths, string destination, bool move)
    {
        Submit(paths, destination, move);
    }
    internal void AcceptWidgetSearch(PaneSearchRequest request)
    {
        Show();
        if (WindowState == System.Windows.WindowState.Minimized) WindowState = System.Windows.WindowState.Normal;
        active.NewTab();
        active.ApplySearchRequest(request);
#if DEBUG
        if (!HiddenTrialPresentation.Enabled)
#endif
        { Activate(); active.FocusList(); }
    }
}
