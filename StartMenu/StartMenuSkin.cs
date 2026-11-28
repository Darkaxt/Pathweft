using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.StartMenu;

/// SAB-07 (S6d): the menu's look as StartAllBack draws it for its settings. Every style shares one window glass: the
/// desktop behind the menu blurred (acrylic, or a plain blur with "blur" off) under a tint at the menu's translucency,
/// light #F3F3F3 or the dark tint, as measured beside StartAllBack. Default draws its rows, bands and box in code
/// (StartAllBack's Default has no theme file); a theme file (Plain8, Windows 7 or another in the Styles folder) draws
/// them from its StartPanelPriv and SearchBox parts, read at run time from the installed file (StartMenuTheme), the
/// Composited class over glass in light mode and the Dark class in dark mode when the theme has one.
internal sealed class StartMenuSkin
{
    internal StartMenuTheme Theme { get; }
    /// The settings this look was made for.
    internal StartMenuOptions Options { get; }
    internal bool Dark { get; }
    internal int Alpha { get; }
    internal bool Blur { get; }
    readonly string[] classes;

    StartMenuSkin(StartMenuOptions options, StartMenuTheme theme)
    {
        Options = options; Theme = theme; Dark = options.Dark; Alpha = options.Alpha; Blur = options.Blur;
        classes = theme == null ? [] : Dark && theme.Has("StartPanelDark::StartPanelPriv") ? ["StartPanelDark::StartPanelPriv"] : ["StartPanelComposited::StartPanelPriv"];
    }

    internal static StartMenuSkin For(StartMenuOptions o) => new(o, StartMenuTheme.Load(StartMenuTheme.Resolve(o.AlterStyle)));

    internal bool Themed => Theme != null;
    internal StartMenuTheme.Part Part(int id) => Theme?.Get(id, classes);
    internal StartMenuTheme.Part SearchPart(int id) => Theme?.Search(id);

    // ---- the parts StartAllBack's StartPanelPriv numbers ----
    internal const int SearchBox = 1, AllProgramsBand = 2, AllProgramsArrow = 5, ProgramsBack = 6, ProgramsSeparator = 8, PlacesBack = 9,
        PlacesBottom = 14, AllProgramsButton = 16, ProgramsBottom = 18, SplitButton = 23, PlaceArrow = 25, LeftItemHot = 28, SignOut = 37, PlaceHot = 36,
        // while searching a theme's panel is other parts: one back across both columns, its own All Programs band and bottom
        SearchBack = 20, SearchBand = 22, SearchBottom = 19, SearchBottomRight = 15, ResultHot = 29;
    internal const int SearchGlyph = 3;

    // ---- the layout, in the menu's units (Default measured at 100 %, the themes from their parts' geometry) ----
    internal double Width => 441;
    internal double MinHeight => Themed ? 523 : 528;
    internal double LeftWidth => Themed ? 261 : 262;
    /// Default: the left column's separator on the All Programs row's top edge and the full-width one on the bottom
    /// band's, each row taking its line.
    internal double AllProgramsHeight => Themed ? 30 : 28;
    internal double BottomHeight => Themed ? 54 : 48;
    /// The bottom band's visible height: a theme's 54-pixel bottom parts reach under the taskbar by 8.
    internal double BottomVisible => Themed ? 46 : 48;
    /// The search box in the bottom band (left, top, width, height), the Sign out button and its arrow beside it in the
    /// right column's part of the band (left, top, height).
    internal Rect SearchRect => Themed ? new Rect(21, 12, 222, 22) : new Rect(13, 12, 235, 24);
    internal Rect SignOutRect => Themed ? new Rect(6, 13, 80, 22) : new Rect(3, 12, 82, 24);
    /// Where the right column's first row starts, below the user picture.
    internal double PlacesTop => Themed ? 42 : 49;
    /// The menu's distance from the screen's edge beside the orb: StartAllBack's Default floats 8 pixels in.
    internal double EdgeGap => Themed ? 0 : 8;
    /// The user picture: its size, how far it rises above the menu, its centre across the menu, and its frame.
    internal double PictureSize => Themed ? 48 : 64;
    internal double PictureFrame => Themed ? 56 : 64;
    internal double PictureRise => Themed ? 25 : 28;
    internal double PictureCentre => 343;

    // ---- colours ----
    /// The glass's tint at full strength.
    internal Color Tint => Dark ? DarkTint : Color.FromRgb(0xF3, 0xF3, 0xF3);
    internal static readonly Color DarkTint = Color.FromRgb(0x25, 0x25, 0x25);

    Brush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    Brush PartText(int part, Color fallback) => Solid(Part(part)?.Text ?? fallback);
    Color Ink => Dark ? Colors.White : Colors.Black;
    internal Brush LeftText => Themed ? PartText(ProgramsBack, Ink) : Solid(Ink);
    internal Brush RightText => Themed ? PartText(PlacesBack, Ink) : Solid(Ink);
    internal Brush AllProgramsText => Themed ? PartText(AllProgramsBand, Ink) : Solid(Ink);
    internal Brush SignOutText => Themed ? PartText(SignOut, Ink) : Solid(Ink);
    internal Brush Muted => Solid(Dark ? Color.FromRgb(0xA0, 0xA0, 0xA0) : Color.FromRgb(0x60, 0x60, 0x60));
    /// Default's separators: grey at about a fifth (light) or a quarter (dark).
    internal Brush Separator => Solid(Color.FromArgb(Dark ? (byte)0x3B : (byte)0x33, 0x6E, 0x6E, 0x6E));
    /// Default's bottom band, a shade darker than the panel: #EDEDED under #F3F3F3, #1F1F1F under #252525.
    internal Brush BottomBand => Solid(Dark ? Color.FromArgb(0x29, 0, 0, 0) : Color.FromArgb(0x06, 0, 0, 0));
    /// The search box is dark only when apps are dark too: with the shell dark and apps light StartAllBack keeps it white.
    bool SearchDark => Dark && Options.AppsDark;
    internal Brush SearchFill => Solid(SearchDark ? Color.FromRgb(0x19, 0x19, 0x19) : Colors.White);
    internal Brush SearchEdge => Solid(SearchDark ? Color.FromRgb(0x51, 0x51, 0x51) : Color.FromRgb(0xAA, 0xAA, 0xAA));
    internal Brush SearchText => Solid(SearchDark ? Colors.White : Colors.Black);
    internal Brush SearchHint => Solid(SearchDark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x6D, 0x6D, 0x6D));
    /// Default's highlighted row: white over the glass, its 1-pixel edge fainter than its inside, rounded (measured:
    /// light #F3F3F3 panel edge at 24 %, inside at 39 %; dark #252525 to #373737 and #3F3F3F).
    Brush HotFill => Solid(Dark ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x31, 0xFF, 0xFF, 0xFF));
    Brush HotEdge => Solid(Dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
    Brush PressedFill => Solid(Dark ? Color.FromArgb(0x04, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));

    /// A button's look in its state (1 normal, 2 hot or selected, 3 pressed, 4 disabled): its theme part's frame for
    /// that state, or Default's box.
    internal void DrawState(DrawingContext dc, SkinButton.Area area, int state, Rect r)
    {
        if (Themed)
        {
            int id = area switch
            {
                SkinButton.Area.AllPrograms => AllProgramsButton,
                SkinButton.Area.SignOut => SignOut,
                SkinButton.Area.SplitArrow => SplitButton,
                SkinButton.Area.RightItem => PlaceHot,
                SkinButton.Area.Result => ResultHot,
                _ => LeftItemHot,
            };
            var part = Part(id);
            // the rows' highlight parts have no normal frame: they draw only when hot or pressed
            bool rowPart = area is SkinButton.Area.RightItem or SkinButton.Area.LeftItem or SkinButton.Area.Result;
            if (part?.Image != null && (!rowPart || state >= 2))
                ThemePart.NineGrid(dc, ThemePart.Frame(part, part.Image, rowPart ? state - 1 : state), part.Sizing, r, part.Stretch);
            if (part?.Image == null && state >= 2) DefaultBox(dc, state, r);
            return;
        }
        if (state >= 2 && state != 4) DefaultBox(dc, state, r);
    }

    void DefaultBox(DrawingContext dc, int state, Rect r)
    {
        dc.DrawRoundedRectangle(HotEdge, null, r, 4, 4);
        if (r.Width > 2 && r.Height > 2) dc.DrawRoundedRectangle(state == 3 ? PressedFill : HotFill, null, new Rect(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 3, 3);
    }

    // ---- the window glass ----

    /// The glass under the whole window (which is therefore exactly the panel): Windows' accent, acrylic or a plain blur,
    /// with the tint at the menu's translucency; DWM's rounded corners and edge for Default, square for a theme, whose
    /// parts draw their own edges.
    internal void ApplyGlass(IntPtr handle)
    {
        var t = Tint;
        // acrylic needs some tint to draw at all
        int alpha = Blur ? Math.Max(1, Alpha) : Alpha;
        var accent = new AccentPolicy { AccentState = Blur ? 4 : 3, AccentFlags = 2, GradientColor = (uint)(alpha << 24 | t.B << 16 | t.G << 8 | t.R) };
        int size = Marshal.SizeOf<AccentPolicy>();
        var p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var data = new CompositionAttributeData { Attribute = 19, Data = p, Size = size };
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally { Marshal.FreeHGlobal(p); }
        int corners = Themed ? 1 : 2; // DWMWCP_DONOTROUND, DWMWCP_ROUND
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
        int dark = Dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct CompositionAttributeData { public int Attribute; public IntPtr Data; public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref CompositionAttributeData data);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
}

/// A menu button drawn as StartAllBack draws it: no template of its own but its content, under it the theme part's
/// frame for its state (or Default's box), and Background for a new program's highlight.
internal sealed class SkinButton : Button
{
    internal enum Area { LeftItem, RightItem, AllPrograms, SignOut, SplitArrow, Result }

    static readonly ControlTemplate template = MakeTemplate();
    static ControlTemplate MakeTemplate()
    {
        var t = new ControlTemplate(typeof(SkinButton));
        var p = new FrameworkElementFactory(typeof(ContentPresenter));
        p.SetValue(MarginProperty, new TemplateBindingExtension(PaddingProperty));
        p.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        p.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        t.VisualTree = p;
        t.Seal();
        return t;
    }

    internal Area Kind { get; }
    StartMenuSkin skin;
    bool selected;
    internal StartMenuSkin Skin { get => skin; set { skin = value; InvalidateVisual(); } }
    /// Shown as hot without the pointer: the search result Enter opens.
    internal bool Selected { get => selected; set { if (selected != value) { selected = value; InvalidateVisual(); } } }

    internal SkinButton(Area kind, StartMenuSkin skin)
    {
        Kind = kind; this.skin = skin;
        Template = template; FocusVisualStyle = null; Background = Brushes.Transparent; BorderThickness = new Thickness(0);
        Padding = new Thickness(8, 0, 8, 0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    int State => !IsEnabled ? 4 : IsPressed ? 3 : IsMouseOver || IsKeyboardFocused || selected ? 2 : 1;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty || e.Property == IsPressedProperty || e.Property == IsKeyboardFocusedProperty
            || e.Property == BackgroundProperty || e.Property == IsEnabledProperty) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(RenderSize);
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, r); // the whole row takes the pointer
        if (Background is Brush b && !ReferenceEquals(b, Brushes.Transparent)) dc.DrawRoundedRectangle(b, null, r, 4, 4);
        skin?.DrawState(dc, Kind, State, r);
    }
}

/// The search box's frame: the theme's SearchBox part for its state (normal, hot, disabled, focused), or Default's
/// white (dark: near-black) box with its grey edge.
internal sealed class SearchFrame : FrameworkElement
{
    StartMenuSkin skin;
    bool hot, focused;
    internal StartMenuSkin Skin { get => skin; set { skin = value; InvalidateVisual(); } }
    internal void Show(bool hot, bool focused) { if (this.hot != hot || this.focused != focused) { this.hot = hot; this.focused = focused; InvalidateVisual(); } }

    internal SearchFrame() { IsHitTestVisible = false; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        var r = new Rect(RenderSize);
        if (skin == null || r.Width <= 0) return;
        if (skin.SearchPart(StartMenuSkin.SearchBox) is { Image: not null } part)
        {
            ThemePart.NineGrid(dc, ThemePart.Frame(part, part.Image, focused ? 4 : hot ? 2 : 1), part.Sizing, r, part.Stretch);
            return;
        }
        var pen = new Pen(skin.SearchEdge, 1); pen.Freeze();
        dc.DrawRectangle(skin.SearchFill, pen, new Rect(0.5, 0.5, r.Width - 1, r.Height - 1));
    }
}

/// StartAllBack's user picture above the menu: its own small window, so the menu's glass stays the panel's shape. It
/// never takes the foreground (the menu stays active); a click opens Your info. Default shows the picture in a circle
/// with a light ring and, while a right-column item is hot, that item's large icon instead; a theme shows it in a
/// glass frame, the item's icon inside the frame.
internal sealed class StartMenuPicture : Window
{
    readonly Grid root = new();
    readonly Image image = new() { Stretch = Stretch.UniformToFill };
    readonly Image icon = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    readonly Border clip = new();
    readonly Border ring = new() { IsHitTestVisible = false };
    StartMenuSkin skin;
    internal event Action Clicked;

    internal StartMenuPicture()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
        clip.Child = image;
        root.Children.Add(clip); root.Children.Add(icon); root.Children.Add(ring);
        Content = root;
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        System.Windows.Automation.AutomationProperties.SetName(this, "User picture");
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); // no activation, tool window
        };
    }

    internal BitmapSource Picture { set => image.Source = value; }

    internal void Restyle(StartMenuSkin s)
    {
        skin = s;
        Width = Height = s.PictureFrame;
        double inner = s.PictureSize, pad = (s.PictureFrame - inner) / 2;
        clip.Width = clip.Height = inner; icon.Width = icon.Height = Math.Min(48, inner);
        if (s.Themed)
        {
            // a glass frame: a translucent white rim with a light outer edge, the picture square inside it
            clip.CornerRadius = new CornerRadius(2);
            clip.Clip = null;
            ring.CornerRadius = new CornerRadius(5);
            ring.BorderThickness = new Thickness(pad);
            ring.BorderBrush = new LinearGradientBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x90, 0xD8, 0xE4, 0xEE), 90);
            ring.Background = Brushes.Transparent;
        }
        else
        {
            clip.Clip = new EllipseGeometry(new Point(inner / 2, inner / 2), inner / 2, inner / 2);
            ring.CornerRadius = new CornerRadius(inner / 2);
            ring.BorderThickness = new Thickness(1);
            ring.BorderBrush = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
        }
        ShowIcon(null);
    }

    /// The hot right-column item's large icon in the picture's place, or the picture again (null).
    internal void ShowIcon(ImageSource item)
    {
        icon.Source = item;
        bool showing = item != null;
        icon.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        clip.Visibility = showing ? Visibility.Hidden : Visibility.Visible;
        // Default drops its circle for the item's icon; a theme keeps its frame
        ring.Visibility = showing && skin?.Themed != true ? Visibility.Hidden : Visibility.Visible;
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
}
