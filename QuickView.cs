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
    bool closingPreviews, previewCloseAccepted;
    static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> ZipExt = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".jar", ".nupkg", ".apk", ".vsix", ".whl" };

    void InitializePreviews()
    {
        sidePreview = new(this, PreviewBody, text => PreviewTitle.Text = text);
        // Await actual owned work before the application's dispatcher exits.
        Closing += async (_, change) =>
        {
            if (previewCloseAccepted) return;
            closingPreviews = true;
            PreviewSession.ClearAll();
            previewShown = quickShown = null;
            if (change.Cancel) { closingPreviews = false; return; } // normal notification-area hide
            change.Cancel = true;
            await PreviewSession.DrainAsync();
            await driveRefreshWork;
            await Task.WhenAll(Left.driveListingWork,Right.driveListingWork);
            await Task.WhenAll(jobs.Where(job => job.ArchiveOperation).Select(job => job.Completion));
            previewCloseAccepted = true;
            _ = Dispatcher.BeginInvoke(Close);
        };
        Closed += (_, _) => { sidePreview.Dispose(); Archive.ForgetPasswords(); };
    }
    void ApplyPreview()
    {
        var on = Settings.Preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewPane.Visibility = PreviewSplitter.Visibility = on;
        PreviewGap.Width = new GridLength(Settings.Preview ? 10 : 0);
        PreviewColumn.Width = new GridLength(Settings.Preview ? Settings.PreviewWidth : 0);
        PreviewColumn.MinWidth = Settings.Preview ? 200 : 0;
        FitPreview(); PlaceCaption(); previewShown = null;
        if (Settings.Preview) PreviewFollow(); else sidePreview?.Clear();
    }
    void TogglePreview() { Settings.Preview = !Settings.Preview; Settings.Save(); ApplyPreview(); }
    void Preview_Click(object sender, RoutedEventArgs e) { TogglePreview(); active.FocusList(); }
    void FitPreview()
    {
        double panes = Settings.SinglePane ? 250 : 510;
        PreviewColumn.MaxWidth = Settings.Preview ? Math.Max(PreviewColumn.MinWidth, PaneArea.ActualWidth - 10 - panes) : double.PositiveInfinity;
    }
    void PaneArea_SizeChanged(object sender, SizeChangedEventArgs e) => FitPreview();
    void PreviewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    { Settings.PreviewWidth = PreviewColumn.ActualWidth; Settings.Save(); }
    void PreviewFollow()
    {
        if (closingPreviews) return;
        if (quick != null)
        {
            var selected = quickSource?.List.SelectedItem as Entry;
            if (selected?.Path != quickShown) { quickShown = selected?.Path; quickPreview.Show(selected); }
            return;
        }
        if (!Settings.Preview || sidePreview == null) return;
        var entry = active?.List.SelectedItem as Entry;
        if (entry?.Path == previewShown) return;
        previewShown = entry?.Path; sidePreview.Show(entry);
    }
    void QuickView() => ToggleQuickView(active, this);
    internal void ToggleQuickView(PaneView source, Window owner)
    {
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
        quickPreview = new(this, quickBody, text => { if (quick != null) quick.Title = text; }, autoplay: true);
        void Follow() => PreviewFollow();
        source.StatsChanged += Follow;
        quick.Closed += (_, _) =>
        {
            source.StatsChanged -= Follow;
            quickPreview.Dispose(); quickPreview = null;
            quick = null; quickBody = null; quickSource = null; quickShown = null;
#if DEBUG
            if (!HiddenTrialPresentation.Enabled)
#endif
                if(source.IsVisible)source.FocusList();
            PreviewFollow();
        };
        quick.PreviewKeyDown += (_, key) =>
        {
            if (key.Key is Key.Space or Key.Escape) { quick.Close(); key.Handled = true; }
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
    internal void RefreshPreviewModules() { ApplyPreviewAppearance(); PreviewSession.RefreshAll(); }
    internal void CloseWidgetPreview(PaneView pane) { if (quickSource == pane) quick?.Close(); }
    void ApplyPreviewAppearance()
    { if (quickBody != null) quickBody.LayoutTransform = Settings.TextScale > 1 ? new ScaleTransform(Settings.TextScale, Settings.TextScale) : null; }
}
