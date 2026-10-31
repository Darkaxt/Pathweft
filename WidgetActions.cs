using System.IO;
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    internal TaskbarSearchHost taskbarSearch;
    internal void ApplyTaskbarSearch() => taskbarSearch?.ApplySettings();
    internal void OpenWidgetLocation(string path)
    {
        Show();
        if(WindowState==System.Windows.WindowState.Minimized)WindowState=System.Windows.WindowState.Normal;
        if(Archive.IsArchiveName(path)&&File.Exists(path)){active.NewTab();active.Navigate(path);}
        else OpenPath(active,path);
#if DEBUG
        if(!HiddenTrialPresentation.Enabled)
#endif
        {Activate();active.FocusList();}
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
