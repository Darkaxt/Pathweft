// Selected search-box keyboard/focus handling adapted from EverythingToolbar,
// MIT, Copyright (c) 2023 Stephan Rumswinkel. See vendor/EverythingSDK/NOTICES.txt.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using FileExplorer.Search;

namespace FileExplorer;

internal sealed class SearchWidgetWindow : Window
{
    readonly MainWindow main;
    internal PaneView Pane { get; } = new() { Name = "Widget", IsWidget = true };
    internal TextBlock Notice { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0), Visibility=Visibility.Collapsed };
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
        Width = 720; Height = 520; MinWidth = 520; MinHeight = 320;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetBinding(BackgroundProperty,new Binding("Background"){Source=owner.FindName("Root")});
        Foreground = MainWindow.Hex("#E8EEF6");
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;
        ShowInTaskbar = false;
        root = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(Notice, Dock.Bottom); root.Children.Add(Notice);
        root.Children.Add(Pane); Content = root;
        query = (TextBox)Pane.FindName("FilterBox");
        Pane.ConfigureWidgetColumns();
        Pane.Loaded += (_, _) => Pane.ConfigureWidgetColumns();
        Pane.Error += message => {Notice.Text=message;Notice.Visibility=Visibility.Visible;};
        Pane.WidgetLocationOpened+=path=>{main.OpenWidgetLocation(path);Dismiss();};
        query.TextChanged+=(_,_)=>Notice.Visibility=Visibility.Collapsed;
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
    }

    internal void ShowSearch(string initialQuery = null)
    {
        ApplyTextScale();
        if(initialQuery!=null&&initialQuery!=query.Text)query.Text=initialQuery;
        else Pane.Search(query.Text);
        Show(); ConstrainToWorkArea(anchorAtCursor: true);
#if DEBUG
        if (!HiddenTrialPresentation.SuppressFocus(this))
#endif
        { Activate(); Pane.FocusFilter(); }
    }

    internal void ApplyTextScale()
    {
        double scale = Settings.TextScale > 1 ? Settings.TextScale : 1;
        if (scale != appliedTextScale)
        {
            double width = Width * scale / appliedTextScale, height = Height * scale / appliedTextScale;
            MinWidth = 520 * scale; MinHeight = 320 * scale;
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
    internal void Handoff(){if(!string.IsNullOrWhiteSpace(Pane.SearchRequest.Query)){main.AcceptWidgetSearch(Pane.SearchRequest);Dismiss();}}
    internal void Preview()=>main.ToggleQuickView(Pane,this);
    async void OnWidgetKeyDown(object sender, KeyEventArgs e)
    {
        if (main.HandleHiddenShortcut(e.Key, e.KeyboardDevice.Modifiers, e.IsRepeat, e.OriginalSource)) { e.Handled = true; return; }
        if (e.Key == Key.Escape) { Keyboard.ClearFocus(); Dismiss(); e.Handled = true; }
        else if (e.OriginalSource is ComboBoxItem or ComboBox or System.Windows.Controls.Primitives.ButtonBase) return;
        else if (e.Key==Key.Enter&&e.KeyboardDevice.Modifiers==ModifierKeys.Control){Handoff();e.Handled=true;}
        else if (query.IsKeyboardFocusWithin && e.Key == Key.Enter) return; // Pane's query handler opens a result, never resolves an address
        else if (query.IsKeyboardFocusWithin && e.Key == Key.Down)
        {e.Handled=true;var text=query.Text;await Pane.SearchCompletion;if(IsVisible&&query.Text==text)Pane.FocusWidgetResults();}
        else if(e.KeyboardDevice.Modifiers==ModifierKeys.Control&&e.Key is Key.F or Key.L){Pane.FocusFilter();e.Handled=true;}
        else if (e.Key == Key.Space && e.OriginalSource is not TextBox) { Preview(); e.Handled = true; }
        else if (Pane.IsKeyboardFocusWithin && e.OriginalSource is not TextBox && e.Key == Key.Enter) { Pane.OpenSelected(); e.Handled = true; }
    }
}
