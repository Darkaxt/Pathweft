using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;

// All preview surfaces use the same built-in module selection/lifecycle.
public partial class MainWindow
{
    Window quick;
    ContentControl quickBody;
    PaneView quickSource;
    PreviewSession sidePreview, quickPreview;
    string previewShown, quickShown;
    Entry quickExplicit;
    bool closingPreviews, previewCloseAccepted;
    static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> ZipExt = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".jar", ".nupkg", ".apk", ".vsix", ".whl" };

    void InitializePreviews()
    {
        sidePreview = new(this, PreviewBody, UpdateSidePreviewHeading);
        PreviewTitle.ContextMenu=sidePreview.Menu;PreviewTitle.ToolTip="Right-click to choose a preview module";
        // Await actual owned work before the application's dispatcher exits.
        Closing += async (_, change) =>
        {
            if (previewCloseAccepted) return;
            closingPreviews = true;
            PreviewSession.ClearAll();
            previewShown = quickShown = null;
            if (change.Cancel) { MovePreviewHost(PreviewMode.Sidebar);closingPreviews = false; return; } // normal notification-area hide
            change.Cancel = true;
            await PreviewSession.DrainAsync();
            await driveRefreshWork;
            await Task.WhenAll(contextMenuInspections.ToArray());
            await Task.WhenAll(Left.driveListingWork,Right.driveListingWork);
            await Task.WhenAll(jobs.Where(job => job.ArchiveOperation).Select(job => job.Completion));
            MovePreviewHost(PreviewMode.Sidebar);
            previewCloseAccepted = true;
            _ = Dispatcher.BeginInvoke(Close);
        };
        Closed += (_, _) => { sidePreview.Dispose(); Archive.ForgetPasswords(); };
    }
    void ApplyPreview()
    {
        UpdateCommandStates();
        if(previewMode==PreviewMode.Sidebar&&!restoredPreview)previewShown = null;
        if(Settings.Preview)PreviewFollow();
        else{MovePreviewHost(PreviewMode.Sidebar);restoredPreview=false;previewShown=null;SetPreviewVisible(false);sidePreview?.Clear();}
    }
    void SetPreviewVisible(bool visible)
    {
        bool column=visible&&previewMode==PreviewMode.Sidebar;
        PreviewPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = column ? Visibility.Visible : Visibility.Collapsed;
        if(column== (PreviewColumn.Width.Value>0))return;
        PreviewColumn.MinWidth = column ? 200 : 0;
        PreviewColumn.MaxWidth=double.PositiveInfinity;
        PreviewGap.Width = new GridLength(column ? 10 : 0);
        PreviewColumn.Width = new GridLength(column ? Settings.PreviewWidth : 0);
        FitPreview(); PlaceCaption();
    }
    void TogglePreview() { Settings.Preview = !Settings.Preview; Settings.Save(); ApplyPreview(); }
    void Preview_Click(object sender, RoutedEventArgs e) { TogglePreview(); active.FocusList(); }
    void FitPreview()
    {
        double panes = Settings.SinglePane ? 250 : 510;
        PreviewColumn.MaxWidth = Settings.Preview ? Math.Max(PreviewColumn.MinWidth, PaneArea.ActualWidth - 10 - panes) : double.PositiveInfinity;
    }
    void PaneArea_SizeChanged(object sender, SizeChangedEventArgs e) {FitPreview();FitPictureInPicture();}
    void PreviewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    { Settings.PreviewWidth = PreviewColumn.ActualWidth; Settings.Save(); }
    void PreviewFollow()
    {
        if (closingPreviews) return;
        if(previewMode!=PreviewMode.Sidebar){SetPreviewVisible(sidePreview?.Entry!=null);return;}
        if (quick != null)
        {
            SetPreviewVisible(false);
            var selected = quickExplicit??quickSource?.List.SelectedItem as Entry;
            if (selected?.Path != quickShown) { quickShown = selected?.Path; quickPreview.Show(selected); }
            return;
        }
        if (!Settings.Preview || sidePreview == null) return;
        var entry = active?.List.SelectedItem as Entry;
        if(entry?.IsUp==true)entry=null;
        if(restoredPreview)
        {
            if(entry?.Path==selectionAtPreviewRestore){SetPreviewVisible(sidePreview.Entry!=null);return;}
            restoredPreview=false;
        }
        SetPreviewVisible(entry!=null);
        if (entry?.Path == previewShown) return;
        previewShown = entry?.Path; sidePreview.Show(entry);
    }
    void QuickView() => ToggleQuickView(active, this);
    internal void InspectShellClass(PaneView source,Entry entry,Window previewOwner=null)
    {
        if(quick!=null)quick.Close();
        ToggleQuickView(source,previewOwner??this);
        quickExplicit=entry;quickShown=entry.Path;quickPreview.Show(entry);
    }
    internal void ToggleQuickView(PaneView source, Window owner)
    {
        if(previewMode!=PreviewMode.Sidebar)MovePreviewHost(PreviewMode.Sidebar);
        if (quick != null) { quick.Close(); return; }
        sidePreview.Clear(); previewShown = null; quickSource = source;
        quickBody = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        quick = new Window
        {
            Owner = owner, Width = 1000, Height = 720, MinWidth = 420, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = quickBody,
            Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily, FontSize = 12, ShowInTaskbar = false
        };
#if DEBUG
        HiddenTrialPresentation.Prepare(quick);
#endif
        quick.SourceInitialized += (_, _) => ApplyAcrylic(quick);
        if(owner is SearchWidgetWindow widget)quick.Deactivated+=(_,_)=>widget.DismissAfterDeactivation();
        quickPreview = new(this, quickBody, text => { if (quick != null){quick.Title = text;quick.Icon=PreviewControls.RendererIcon(quickPreview?.RendererId)??quickPreview?.Entry?.SmallIcon;} }, autoplay: true);
        void Follow() => PreviewFollow();
        source.StatsChanged += Follow;
        quick.Closed += (_, _) =>
        {
            source.StatsChanged -= Follow;
            quickPreview.Dispose(); quickPreview = null;
            quick = null; quickBody = null; quickSource = null; quickShown = null;
            quickExplicit=null;
#if DEBUG
            if (!HiddenTrialPresentation.Enabled)
#endif
                if(source.IsVisible)source.FocusList();
            PreviewFollow();
        };
        quick.PreviewKeyDown += (_, key) =>
        {
            if(key.Key!=Key.Escape&&IsPreviewInput(key.OriginalSource as DependencyObject))return;
            if (key.Key is Key.Space or Key.Escape) { quick.Close(); key.Handled = true; }
            else if(quickExplicit!=null)return;
            else if (quickPreview.HandleKey(key.Key)) key.Handled = true;
            else if (key.Key is Key.Up or Key.Down)
            {
                var list = source.List;
                if (list.Items.Count > 0)
                {
                    list.SelectedIndex = Math.Clamp(list.SelectedIndex + (key.Key == Key.Down ? 1 : -1), 0, list.Items.Count - 1);
                    list.ScrollIntoView(list.SelectedItem); PreviewFollow();
                }
                key.Handled = true;
            }
        };
        ApplyPreviewAppearance(); quick.Show(); PreviewFollow();
    }
    static bool IsPreviewInput(DependencyObject value)
    {
        for(var current=value;current!=null;current=current is Visual||current is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))
            if(current is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox or DataGrid)return true;
        return false;
    }
    internal void RefreshPreviewModules() { ApplyPreviewAppearance(); PreviewSession.RefreshAll(); }
    internal void CloseWidgetPreview(PaneView pane) { if (quickSource == pane) quick?.Close(); }
    void ApplyPreviewAppearance()
    {
        if (quickBody != null) quickBody.LayoutTransform = Settings.TextScale > 1 ? new ScaleTransform(Settings.TextScale, Settings.TextScale) : null;
        if(PreviewPane!=null)PreviewPane.LayoutTransform=previewMode==PreviewMode.Windowed&&Settings.TextScale>1?new ScaleTransform(Settings.TextScale,Settings.TextScale):null;
        if(PreviewWindowButton!=null)UpdatePreviewModeButtons();
    }
}
