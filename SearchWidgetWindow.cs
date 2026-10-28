// Selected search-box keyboard/focus handling adapted from EverythingToolbar,
// MIT, Copyright (c) 2023 Stephan Rumswinkel. See vendor/EverythingSDK/NOTICES.txt.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileExplorer.Search;

namespace FileExplorer;

internal sealed class SearchWidgetWindow : Window
{
    readonly MainWindow main;
    internal PaneView Pane { get; } = new() { Name = "Widget", IsWidget = true };
    internal TextBox Destination { get; } = new() { MinWidth = 130, Margin = new(4), Background = MainWindow.Hex("#252830"), Foreground = MainWindow.Hex("#E8EEF6"), Padding = new(5), ToolTip = "Copy or move selected results into this real folder" };
    internal TextBlock Notice { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(8, 4, 8, 4) };
    internal Button HandoffButton { get; }
    internal Button RevealButton { get; }
    internal Button CopyButton { get; }
    internal Button MoveButton { get; }
    internal Button OpenButton { get; }
    internal Button PreviewButton { get; }
    readonly TextBox query;
    readonly DockPanel root;
    double appliedTextScale = 1;
    bool closing;
    System.Windows.Threading.DispatcherOperation dpiLayout;

    internal SearchWidgetWindow(MainWindow owner)
    {
        main = owner;
        Owner = owner;
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        Title = "Pathweft Search";
        Width = 680; Height = 520; MinWidth = 560; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = MainWindow.Hex("#161B24"); Foreground = MainWindow.Hex("#E8EEF6");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;
        ShowInTaskbar = false;
        root = new DockPanel { Margin = new(6) };
        var title = new DockPanel { Margin = new(8, 4, 8, 6) };
        var settings = Button("Settings", () => main.OpenWidgetSettings());
        DockPanel.SetDock(settings, Dock.Right); title.Children.Add(settings);
        title.Children.Add(new TextBlock { Text = "Pathweft Search", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var actions = new WrapPanel { Margin = new(4) };
        HandoffButton = Button("Open search in pane", () => { main.AcceptWidgetSearch(Pane.SearchRequest); Dismiss(); });
        HandoffButton.FontWeight = FontWeights.SemiBold;
        HandoffButton.SetResourceReference(ForegroundProperty, "Accent");
        RevealButton = Button("Reveal", Reveal);
        OpenButton = Button("Open", () => Pane.OpenSelected());
        PreviewButton = Button("Preview", () => main.ToggleQuickView(Pane, this));
        CopyButton = Button("Copy to folder", () => Transfer(false));
        MoveButton = Button("Move to folder", () => Transfer(true));
        foreach (var action in new[] { HandoffButton, RevealButton, PreviewButton, OpenButton, CopyButton, MoveButton }) actions.Children.Add(action);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        DockPanel.SetDock(Notice, Dock.Bottom); root.Children.Add(Notice);
        var destination = new DockPanel();
        destination.Children.Add(new TextBlock { Text = "Destination", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 3, 0) });
        destination.Children.Add(Destination);
        System.Windows.Automation.AutomationProperties.SetName(Destination, "Transfer destination folder");
        DockPanel.SetDock(destination, Dock.Bottom); root.Children.Add(destination);
        root.Children.Add(Pane); Content = root;
        query = (TextBox)Pane.FindName("FilterBox");
        // This surface has one search session; browser tabs remain in the main window.
        var tabBand = (FrameworkElement)Pane.FindName("TabBand");
        ((FrameworkElement)tabBand.Parent).Visibility = Visibility.Collapsed;
        ((FrameworkElement)Pane.FindName("FolderStrip")).Visibility = Visibility.Collapsed;
        Pane.ConfigureWidgetColumns();
        Pane.Loaded += (_, _) => Pane.ConfigureWidgetColumns();
        Pane.Error += message => Notice.Text = message;
        Pane.OpenArchived += entry => main.OpenWidgetArchive(Pane, entry);
        Pane.FilesDropped += (sources, destinationFolder, move) => main.SubmitWidgetDrop(Pane, sources, destinationFolder, move);
        Pane.StatsChanged += UpdateActions;
        PreviewKeyDown += OnWidgetKeyDown;
        Deactivated += (_, _) => { if (!IsKeyboardFocusWithin && !IsOwnedWindowActive()) Dismiss(); };
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Dismiss(); } };
        Closed += (_, _) => Pane.StopWidgetWork();
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        DpiChanged += (_, change) =>
        {
            if (!ReferenceEquals(change.OriginalSource, this)) return;
            // WPF is processing the native DPI/size transition here. Changing
            // bounds inside it can reenter a virtualized list's layout pass.
            if (dpiLayout?.Status != System.Windows.Threading.DispatcherOperationStatus.Pending)
                dpiLayout = Dispatcher.InvokeAsync(() => ConstrainToWorkArea(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        UpdateActions();
    }

    Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new(2), Padding = new(8, 5, 8, 5), Style = (Style)main.FindResource("Ghost") };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    internal void ShowSearch(string folder, string initialQuery = null)
    {
        ApplyTextScale();
        if (Pane.Dir == "")
        {
            Pane.Navigate(folder);
            ((ComboBox)Pane.FindName("SearchScope")).SelectedIndex = 1;
        }
        if (initialQuery != null) query.Text = initialQuery;
        if (Destination.Text == "") Destination.Text = main.WidgetDefaultDestination;
        Show(); ConstrainToWorkArea(anchorAtCursor: true);
#if DEBUG
        if (!HiddenTrialPresentation.SuppressFocus(this))
#endif
        { Activate(); query.Focus(); query.SelectAll(); }
        if (initialQuery != null || Pane.SearchRequest.Query.Length > 0) Pane.Search(query.Text);
        UpdateActions();
    }

    internal void ApplyTextScale()
    {
        double scale = Settings.TextScale > 1 ? Settings.TextScale : 1;
        if (scale != appliedTextScale)
        {
            double width = Width * scale / appliedTextScale, height = Height * scale / appliedTextScale;
            MinWidth = 560 * scale; MinHeight = 380 * scale;
            Width = width; Height = height;
            appliedTextScale = scale;
        }
        root.LayoutTransform = Settings.TextScale > 1 ? new ScaleTransform(Settings.TextScale, Settings.TextScale) : null;
        Pane.ApplyScales();
    }

    internal void ConstrainToWorkArea(bool anchorAtCursor = false)
    {
        var screen = anchorAtCursor ? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position)
            : System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var transform = System.Windows.PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var bounds = screen.WorkingArea;
        var area = new Rect(transform.Transform(new Point(bounds.Left, bounds.Top)), transform.Transform(new Point(bounds.Right, bounds.Bottom)));
        MaxWidth = Math.Max(MinWidth, area.Width); MaxHeight = Math.Max(MinHeight, area.Height);
        Width = Math.Min(Width, MaxWidth); Height = Math.Min(Height, MaxHeight);
        Left = Math.Clamp(area.Left + (area.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Max(area.Top, area.Bottom - Height - 8);
    }

    bool IsOwnedWindowActive() => OwnedWindows.Cast<Window>().Any(w => w.IsActive);
    internal void Dismiss() { main.CloseWidgetPreview(Pane); if (IsVisible) Hide(); Pane.StopWidgetWork(); }
    internal void Shutdown() { main.CloseWidgetPreview(Pane); closing = true; Close(); }
    void Reveal() { if (Pane.Selected.FirstOrDefault() is { } entry) { main.Show(); main.ShowFile(entry.Path); Dismiss(); } }
    internal Job LastTransfer { get; private set; }
    void Transfer(bool move)
    {
        if (!Directory.Exists(Destination.Text)) { Notice.Text = "Choose an existing destination folder."; return; }
        LastTransfer = main.Submit(Pane.Selected.Select(e => e.Path).ToList(), Destination.Text, move);
        Notice.Text = LastTransfer == null ? "Select a result first." : "Submitted to Pathweft’s transfer queue. View progress in Transfers.";
    }
    void UpdateActions()
    {
        bool selected = Pane.Selected.Count > 0;
        RevealButton.IsEnabled = PreviewButton.IsEnabled = CopyButton.IsEnabled = MoveButton.IsEnabled = selected;
        HandoffButton.IsEnabled = !string.IsNullOrWhiteSpace(query.Text);
    }
    void OnWidgetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Keyboard.ClearFocus(); Dismiss(); e.Handled = true; }
        else if (e.OriginalSource is ComboBoxItem or ComboBox) return;
        else if (query.IsKeyboardFocusWithin && e.Key == Key.Enter) { Pane.Search(query.Text); e.Handled = true; }
        else if (query.IsKeyboardFocusWithin && e.Key == Key.Down) { Pane.FocusList(); e.Handled = true; }
        else if (e.Key == Key.Space && e.OriginalSource is not TextBox) { main.ToggleQuickView(Pane, this); e.Handled = true; }
        else if (Pane.IsKeyboardFocusWithin && e.OriginalSource is not TextBox && e.Key == Key.Enter) { Pane.OpenSelected(); e.Handled = true; }
    }
}
