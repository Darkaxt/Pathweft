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
    PreviewSession sidePreview, quickPreview; // sidePreview: the left (or only) pane's card's session
    string quickShown;
    Entry quickExplicit;
    bool closingPreviews, previewCloseAccepted;
    static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> ZipExt = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".jar", ".nupkg", ".apk", ".vsix", ".whl" };

    void InitializePreviews()
    {
        primary = new PreviewCard(this, Left, UpdatePreviewHeading);
        sidePreview = primary.Session;
        RefreshPreviewHosts();
        // Await actual owned work before the application's dispatcher exits.
        Closing += async (_, change) =>
        {
            if (previewCloseAccepted) return;
            closingPreviews = true;
            foreach(var card in Cards){card.Pinned=false;card.Shown=null;}
            PreviewSession.ClearAll();
            quickShown = null;
            if (change.Cancel) { ResetPreviewHosts();closingPreviews = false; return; } // normal notification-area hide
            change.Cancel = true;
            await StopUpdateChecks();
            await Task.WhenAll(Left.DrainSpaceAndSizesAsync(),Right.DrainSpaceAndSizesAsync());
            await PreviewSession.DrainAsync();
            await driveRefreshWork;
            await Task.WhenAll(Left.driveListingWork,Right.driveListingWork);
            await Task.WhenAll(jobs.Select(job => job.Completion));
            // Finished dispatches precede this turn's barrier; drain durable undo
            // saves before the dispatcher and its completion handlers disappear.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await FinishUndoWork();
            ResetPreviewHosts();
            previewCloseAccepted = true;
            _ = Dispatcher.BeginInvoke(Close);
        };
        Closed += (_, _) => { foreach(var card in Cards)card.Session.Dispose(); Archive.ForgetPasswords(); ArchiveContent.ReleaseNested(); }; // nested archives were written back as they changed
    }
    void ApplyPreview()
    {
        UpdateCommandStates();
        foreach(var card in Cards)if(card.Mode==PreviewMode.Sidebar&&!card.Pinned)card.Shown=null;
        if(Settings.Preview)PreviewFollow();
        else foreach(var card in Cards){card.Pinned=false;MoveHost(card,PreviewMode.Sidebar);card.Shown=null;SetPreviewVisible(card,false);card.Session.Clear();UpdatePreviewModeButtons(card);}
    }
    void TogglePreview() { Settings.Preview = !Settings.Preview; Settings.Save(); ApplyPreview(); }
    void Preview_Click(object sender, RoutedEventArgs e) { TogglePreview(); active.FocusList(); }
    void PreviewFollow()
    {
        if (closingPreviews) return;
        if (quick != null)
        {
            foreach(var card in Cards)SetPreviewVisible(card,false);
            var selected = quickExplicit??quickSource?.List.SelectedItem as Entry;
            if (selected?.Path != quickShown) { quickShown = selected?.Path; quickPreview.Show(selected); }
            return;
        }
        if (!Settings.Preview) return;
        foreach(var card in Cards)Follow(card);
    }
    /// PREVIEW-41: each card shows its own pane's selection.
    void Follow(PreviewCard card)
    {
        var entry = card.Pane.List.SelectedItem as Entry;
        if(entry?.IsUp==true)entry=null;
        if(card.Pinned&&card.Session.Entry!=null){SetPreviewVisible(card,true);return;}
        SetPreviewVisible(card,entry!=null);
        if (entry?.Path == card.Shown) return;
        card.Shown = entry?.Path; card.Session.Show(entry);UpdatePreviewModeButtons(card);
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
        ResetPreviewHosts();
        if (quick != null) { quick.Close(); return; }
        foreach(var card in Cards){card.Session.Clear();card.Pinned=false;card.Shown=null;}
        quickSource = source;UpdatePreviewModeButtons();
        quickBody = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        quick = new Window
        {
            Owner = owner, Width = 1000, Height = 720, MinWidth = 420, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = quickBody,
            Background = Hex("#161B24"), Foreground = Foreground, FontFamily = FontFamily, FontSize = 12, ShowInTaskbar = false
        };
        AuxiliaryWindows.Prepare(quick,quickBody,scaleContent:false);
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
            else if (MarkKey(key.Key)) key.Handled = true; // OPUS-08
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
        foreach(var card in Cards)card.LayoutTransform=card.Mode==PreviewMode.Windowed&&Settings.TextScale>1?new ScaleTransform(Settings.TextScale,Settings.TextScale):null;
        UpdatePreviewModeButtons();
    }
}
