// Selected search-box keyboard/focus handling adapted from EverythingToolbar,
// MIT, Copyright (c) 2023 Stephan Rumswinkel. See vendor/EverythingSDK/NOTICES.txt.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Runtime.InteropServices;
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
    ContextMenu resultMenu;

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
        AuxiliaryWindows.Prepare(this,root,scaleContent:false);
        query = (TextBox)Pane.FindName("FilterBox");
        Pane.ConfigureWidgetColumns();
        Pane.Loaded += (_, _) => Pane.ConfigureWidgetColumns();
        Pane.Error += message => {Notice.Text=message;Notice.Visibility=Visibility.Visible;};
        Pane.WidgetLocationOpened+=path=>{main.OpenWidgetLocation(path);Dismiss();};
        Pane.InspectRequested+=entry=>main.InspectShellClass(Pane,entry,this);
        Pane.List.ContextMenuOpening+=(_,args)=>
        {
            args.Handled=true;
            if(args.CursorLeft>=0&&Pane.RowAt(args.OriginalSource as DependencyObject)==null)Pane.List.SelectedItems.Clear();
            ShowResultMenu();
        };
        query.TextChanged+=(_,_)=>Notice.Visibility=Visibility.Collapsed;
        PreviewKeyDown += OnWidgetKeyDown;
        Deactivated += (_, _) => DismissAfterDeactivation();
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

    bool IsOwnedWindowActive()
    {
        if(resultMenu?.IsOpen==true||OwnedWindows.Cast<Window>().Any(w=>w.IsActive))return true;
        // Native menus/dialogs are not represented in WPF OwnedWindows.
        var own=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        for(var window=GetForegroundWindow();window!=IntPtr.Zero;window=GetWindow(window,4))if(window==own)return true;
        return false;
    }
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint relation);
    internal void DismissAfterDeactivation() => Dispatcher.BeginInvoke(() =>
    {
        // Keyboard focus can remain cached in the query when native activation
        // leaves. Read window activation after this transition, including an
        // owned preview gaining activation, rather than testing that cache.
        if(IsVisible&&!IsActive&&!IsOwnedWindowActive())Dismiss();
    },System.Windows.Threading.DispatcherPriority.Input);
    internal void Dismiss() {if(resultMenu!=null)resultMenu.IsOpen=false; if (IsVisible) Hide(); main.CloseWidgetPreview(Pane); Pane.StopWidgetWork(); }
    internal void Shutdown() { main.CloseWidgetPreview(Pane); closing = true; Close(); }
    internal void Handoff(){if(!string.IsNullOrWhiteSpace(Pane.SearchRequest.Query)){main.AcceptWidgetSearch(Pane.SearchRequest);Dismiss();}}
    internal void Preview()=>main.ToggleQuickView(Pane,this);
    static bool PhysicalSelection(IReadOnlyList<Entry> selection)=>selection.Count>0&&selection.All(entry=>
        Path.IsPathFullyQualified(entry.Path)&&PaneView.ArchiveOf(entry.Path) is not {Inner.Length:>0});
    internal void RevealSelection()
    {
        if(Pane.Selected is not {Count:1} selection||!PhysicalSelection(selection)||Path.GetDirectoryName(selection[0].Path.TrimEnd('\\'))==null)return;
        main.RevealWidgetResult(selection[0].Path);Dismiss();
    }
    internal void PropertiesSelection()
    {
        var selected=Pane.Selected;
        var paths=selected.Select(entry=>entry.Path).ToArray();
        if(!PhysicalSelection(selected)||!MainWindow.SupportsProperties(paths))return;
        if(main.Properties(paths,main))Dismiss();
        else foreach(var entry in selected)entry.Message="Could not open Properties for this item.";
    }
    internal ContextMenu ResultMenu()
    {
        var selected=Pane.Selected;bool one=selected.Count==1;
        bool physical=PhysicalSelection(selected);
        var paths=selected.Select(entry=>entry.Path).ToArray();
        bool shell=physical&&ShellMenu.CanShowItems(paths);
        var menu=new ContextMenu();
        menu.Items.Add(MainWindow.Item("Open","\uE8E5","Enter",Pane.OpenSelected,selected.Count>0));
        menu.Items.Add(MainWindow.Item("Preview","\uE8B7","Space",Preview,one));
        menu.Items.Add(MainWindow.Item("Open containing folder","\uE838","Ctrl+Shift+Enter",RevealSelection,one&&physical&&Path.GetDirectoryName(paths[0].TrimEnd('\\'))!=null));
        menu.Items.Add(new Separator());
        menu.Items.Add(MainWindow.Item("Copy path","\uE8C8","Ctrl+Shift+C",()=>System.Windows.Forms.Clipboard.SetText(Entry.QuotedPaths(Pane.Selected)),selected.Count>0));
        var windows=MainWindow.Item("Windows menu…","\uE712","",()=>ShellMenu.Show(this,Path.GetDirectoryName(paths[0]),paths,rename:false),shell);
        if(physical&&!shell){windows.ToolTip="Select results from one containing folder for the Windows menu.";ToolTipService.SetShowOnDisabled(windows,true);}menu.Items.Add(windows);
        var properties=MainWindow.Item("Properties","\uE946","Alt+Enter",PropertiesSelection,physical&&MainWindow.SupportsProperties(paths));
        if(physical&&!properties.IsEnabled){properties.ToolTip="Select results on the same drive or share for combined Properties.";ToolTipService.SetShowOnDisabled(properties,true);}menu.Items.Add(properties);
        MainWindow.PrepareContextMenu(menu,Pane.List);
        return menu;
    }
    void ShowResultMenu()
    {
        if(Settings.NativeMenu&&PhysicalSelection(Pane.Selected)&&ShellMenu.CanShowItems(Pane.Selected.Select(entry=>entry.Path).ToArray()))
        {
            var selected=Pane.Selected;
            ShellMenu.Show(this,Path.GetDirectoryName(selected[0].Path),selected.Select(entry=>entry.Path).ToArray(),rename:false,
                top:selected.Count==1?new[]{("Open containing folder",(Action)RevealSelection)}:null);
            return;
        }
        resultMenu=ResultMenu();resultMenu.Closed+=(_,_)=>DismissAfterDeactivation();resultMenu.IsOpen=true;
    }
    async void OnWidgetKeyDown(object sender, KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if (main.HandleHiddenShortcut(key, e.KeyboardDevice.Modifiers, e.IsRepeat, e.OriginalSource)) { e.Handled = true; return; }
        if (key == Key.Escape) { Keyboard.ClearFocus(); Dismiss(); e.Handled = true; }
        else if (e.OriginalSource is ComboBoxItem or ComboBox or System.Windows.Controls.Primitives.ButtonBase) return;
        else if(HandleResultShortcut(key,e.KeyboardDevice.Modifiers,e.OriginalSource)){e.Handled=true;}
        else if (key==Key.Enter&&e.KeyboardDevice.Modifiers==ModifierKeys.Control){Handoff();e.Handled=true;}
        else if (query.IsKeyboardFocusWithin && key == Key.Enter) return; // Pane's query handler opens a result, never resolves an address
        else if (query.IsKeyboardFocusWithin && key == Key.Down)
        {e.Handled=true;var text=query.Text;await Pane.SearchCompletion;if(IsVisible&&query.Text==text)Pane.FocusWidgetResults();}
        else if(e.KeyboardDevice.Modifiers==ModifierKeys.Control&&key is Key.F or Key.L){Pane.FocusFilter();e.Handled=true;}
        else if (key == Key.Space && e.OriginalSource is not TextBox) { Preview(); e.Handled = true; }
        else if (Pane.IsKeyboardFocusWithin && e.OriginalSource is not TextBox && key == Key.Enter) { Pane.OpenSelected(); e.Handled = true; }
    }
    internal bool HandleResultShortcut(Key key,ModifierKeys modifiers,object source)
    {
        if(key==Key.Enter&&modifiers==ModifierKeys.Alt){PropertiesSelection();return true;}
        if(key==Key.Enter&&modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){RevealSelection();return true;}
        if(key==Key.C&&modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&source is not TextBox)
        {
            if(Pane.Selected.Count>0)System.Windows.Forms.Clipboard.SetText(Entry.QuotedPaths(Pane.Selected));
            return true;
        }
        return false;
    }
}
