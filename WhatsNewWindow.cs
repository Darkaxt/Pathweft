
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FileExplorer;

// What's new, the way PowerToys (and Shelf 3.66) show it: every release in a list on the left, under the month
// it came out, and the chosen one on the right under a banner of its own. The notes are whatsnew.txt, built
// into the exe, so it works offline and shows every release, not only the latest.
public class WhatsNewWindow : Window
{
    internal record Release(string Version, DateTime Date, string Title, List<string> Lines);

    readonly List<Release> releases = Load();
    readonly StackPanel nav = new();
    readonly Border banner = new() { Height = 168, CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new(0, 0, 0, 14) };
    readonly StackPanel notes = new();
    readonly ScrollViewer notesScroll;
    int shown = -1;

    internal static List<Release> Load()
    {
        using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream("whatsnew.txt");
        using var reader = new System.IO.StreamReader(stream!);
        var list = new List<Release>();
        foreach (var line in reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith("## ") && line[3..].Split(" | ", 3) is [var v, var d, var t])
                list.Add(new Release(v.Trim(), DateTime.ParseExact(d.Trim(), "yyyy-MM-dd", null), t.Trim(), new()));
            else if (line.StartsWith("# ") || line.Trim() == "" || list.Count == 0) continue; // comments, blank lines
            else list[^1].Lines.Add(line);
        }
        return list;
    }

    public WhatsNewWindow(Window owner)
    {
        Owner = owner;
        Title = "What's new — File Labs";
        Width = 980; Height = 680; MinWidth = 700; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        Foreground = MainWindow.Hex("#E8EEF6");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 12;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Down or Key.Up && e.OriginalSource is not TextBox) { Show(Math.Clamp(shown + (e.Key == Key.Down ? 1 : -1), 0, releases.Count - 1)); e.Handled = true; }
        };

        var close = new Button { Content = "Close", Style = (Style)FindResource("Tab"), Tag = "on", Padding = new(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        close.Click += (_, _) => Close();

        var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 200, Margin = new(0, 0, 16, 0) };
        notesScroll = new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 8, 0) };
        var right = new DockPanel();
        DockPanel.SetDock(banner, Dock.Top);
        right.Children.Add(banner);
        right.Children.Add(notesScroll);

        var body = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        DockPanel.SetDock(navScroll, Dock.Left);
        body.Children.Add(close);
        body.Children.Add(navScroll);
        body.Children.Add(right);
        Content = new Grid { Background = MainWindow.Tint(), Children = { new Border { Padding = new(18, 16, 18, 14), Child = body } } };

        BuildNav();
        // the version running, which is the newest unless this build is older than its own notes
        Show(Math.Max(0, releases.FindIndex(r => r.Version == Updater.Current.ToString())));
    }

    void BuildNav()
    {
        string month = null;
        for (int i = 0; i < releases.Count; i++)
        {
            var r = releases[i];
            var m = r.Date.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            if (m != month)
            {
                month = m;
                nav.Children.Add(new TextBlock { Text = m, Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new(8, nav.Children.Count == 0 ? 0 : 12, 0, 4) });
            }
            int idx = i;
            var row = new DockPanel();
            var when = new TextBlock { Text = i == 0 ? "Latest" : r.Date.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture), Foreground = (Brush)FindResource("Muted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(when, Dock.Right);
            row.Children.Add(when);
            row.Children.Add(new TextBlock { Text = r.Version, VerticalAlignment = VerticalAlignment.Center });
            // the tab look: Tag "on" marks the release shown
            var b = new Button { Content = row, Style = (Style)FindResource("Tab"), MaxWidth = double.PositiveInfinity, FontSize = 12, Margin = new(0, 1, 0, 1), ToolTip = r.Title };
            System.Windows.Automation.AutomationProperties.SetName(b, $"{r.Version}, {r.Title}");
            b.Click += (_, _) => Show(idx);
            nav.Children.Add(b);
        }
    }

    void Show(int i)
    {
        if (i == shown || i < 0 || i >= releases.Count) return;
        shown = i;
        var r = releases[i];
        var buttons = nav.Children.OfType<Button>().ToList();
        foreach (var b in buttons) b.Tag = null;
        buttons[i].Tag = "on";
        buttons[i].BringIntoView();

        banner.Child = Banner(r);
        notes.Children.Clear();
        notes.Children.Add(new TextBlock { Text = r.Date.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), Foreground = (Brush)FindResource("Muted"), Margin = new(0, 0, 0, 8) });
        foreach (var line in r.Lines)
        {
            var text = line.Trim();
            if (text.StartsWith("- ") || text.StartsWith("* "))
            {
                var point = new DockPanel { Margin = new(0, 3, 0, 3) };
                var dot = new TextBlock { Text = "•", Foreground = (Brush)FindResource("Accent"), Margin = new(2, 0, 10, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                point.Children.Add(dot);
                point.Children.Add(Rich(text[2..], "#D6DEEA"));
                notes.Children.Add(point);
            }
            else if (text.StartsWith("**") && text.EndsWith("**") && text.Length > 4 && !text[2..^2].Contains("**"))
                notes.Children.Add(new TextBlock { Text = text[2..^2], FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new(0, 12, 0, 4) });
            else
            {
                var p = Rich(text, "#C9D3E0");
                p.Margin = new(0, 4, 0, 6);
                notes.Children.Add(p);
            }
        }
        notesScroll.ScrollToTop();
    }

    /// A line of the notes with its **bold** parts in bold.
    static TextBlock Rich(string text, string colour)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Hex(colour), LineHeight = 18 };
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] == "") continue;
            else if (i % 2 == 1) t.Inlines.Add(new Run(parts[i]) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            else t.Inlines.Add(new Run(parts[i])); // takes the block's colour
        return t;
    }

    // ---- the banner: a colour and a picture of its own for every release, the same each time it is opened ----

    /// FNV-1a over the version, then a small mixer: the same numbers for the same release, every time.
    static Func<double> Seeded(string key)
    {
        uint h = 2166136261;
        foreach (var c in key) h = (h ^ c) * 16777619;
        return () =>
        {
            h ^= h >> 15; h *= h | 1;
            h ^= h + (h ^ (h >> 7)) * (h | 61);
            return (h ^ (h >> 14)) / 4294967296.0;
        };
    }

    static Color Hsl(double h, double s, double l, double a = 1)
    {
        h = (h % 360 + 360) % 360 / 360; s /= 100; l /= 100;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Ch(double t) { t = (t + 1) % 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        return Color.FromArgb((byte)(a * 255), (byte)(Ch(h + 1 / 3.0) * 255), (byte)(Ch(h) * 255), (byte)(Ch(h - 1 / 3.0) * 255));
    }

    // Twelve pictures of File Labs itself — its panes, tabs, drives, transfers, tree, path, thumbnails, the Drop bin,
    // search, age colours, archives — taking turns by release, each varied by the seed (counts, sizes, tilt) and laid
    // on a palette of its own: the version picks the hue, the seed picks how the second colour relates to it.
    const string FolderShape = "M0,8 Q0,2 6,2 L18,2 L24,8 L48,8 Q54,8 54,14 L54,40 Q54,46 48,46 L6,46 Q0,46 0,40 Z"; // 54 × 46
    const string SheetShape = "M10,0 L84,0 L110,26 L110,140 Q110,150 100,150 L10,150 Q0,150 0,140 L0,10 Q0,0 10,0 Z"; // 110 × 150

    static UIElement Banner(Release r)
    {
        var rnd = Seeded(r.Version);
        var v = r.Version.Split('.').Select(int.Parse).ToArray();
        int n = v[0] * 10000 + v[1] * 100 + v[2];
        double hue = n * 137.508 % 360;            // the golden angle: neighbouring releases never share a colour
        double second = new[] { 35, 150, 180, 210, -60 }[(int)(rnd() * 5)]; // analogous, split, complementary, triadic-ish
        double sat = 55 + rnd() * 30;
        Brush C(double dh, double l, double a = 1) => new SolidColorBrush(Hsl(hue + dh, sat, l, a));
        Brush A(double l, double a = 1) => C(second, l, a); // the accent, for what is lit up
        var art = new Canvas { Width = 900, Height = 180 };
        void Put(Panel into, UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); into.Children.Add(e); }
        Rectangle Bar(double w, double h, Brush fill, double radius = 3) => new() { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Fill = fill };
        Path Shape(string data, Brush fill, double scale = 1, Brush stroke = null) =>
            new() { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = 1.2, RenderTransform = scale == 1 ? null : new ScaleTransform(scale, scale) };
        Canvas Group(double angle = 0, double cx = 0, double cy = 0) => new() { RenderTransform = angle == 0 ? null : new RotateTransform(angle, cx, cy) };
        double tilt = (rnd() - 0.5) * 16;

        switch (v[2] % 12)
        {
            case 0: // two panes of rows
                for (int pane = 0; pane < 2; pane++)
                {
                    var g = Group(tilt / 2 - 4 + pane * 4, 90, 90);
                    g.Children.Add(new Rectangle { Width = 175, Height = 190, RadiusX = 12, RadiusY = 12, Fill = C(pane * 20, 24), Stroke = C(pane * 20, 70, 0.45), StrokeThickness = 1.5 });
                    Put(g, Bar(175, 22, C(pane * 20, 18), 10), 0, 0);
                    for (int row = 0; row < 7; row++)
                        Put(g, Bar(40 + rnd() * 100, 7, rnd() < 0.18 ? A(65, 0.95) : C(row * 6, 55, 0.35)), 16, 34 + row * 20);
                    Put(art, g, 500 + pane * 190, -10 + pane * 18);
                }
                break;
            case 1: // a tilted grid of folders, a few lit up
                var grid = Group(-10 + tilt, 700, 90);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 7; x++)
                        Put(grid, Shape(FolderShape, rnd() < 0.18 ? A(65, 0.9) : C(rnd() * 40, 28 + rnd() * 25, 0.65)), 470 + x * 66, -30 + y * 64);
                art.Children.Add(grid);
                break;
            case 2: // a fan of sheets with folded corners
                int sheets = 4 + (int)(rnd() * 3);
                for (int i = 0; i < sheets; i++)
                {
                    var sheet = Group((i - (sheets - 1) / 2.0) * (9 + rnd() * 7), 55, 170);
                    sheet.Children.Add(Shape(SheetShape, i == sheets - 1 ? A(45) : C(i * 12, 26 + i * 7), 1, C(i * 12, 75, 0.5)));
                    sheet.Children.Add(Shape("M84,0 L84,26 L110,26 Z", C(i * 12, 62, 0.5)));
                    for (int l = 0; l < 4; l++) Put(sheet, Bar(50 + rnd() * 30, 6, C(i * 12 + 30, 75, 0.3)), 14, 50 + l * 18);
                    Put(art, sheet, 600 + i * 30, 30);
                }
                break;
            case 3: // a transfer: its card, the speed chart filled under the line, the progress bar
                var card = Group(tilt / 3, 200, 90);
                card.Children.Add(new Rectangle { Width = 400, Height = 170, RadiusX = 14, RadiusY = 14, Fill = C(0, 20, 0.9), Stroke = C(0, 60, 0.35) });
                Put(card, Bar(150 + rnd() * 80, 9, C(0, 80, 0.7)), 22, 18);
                var area = new PointCollection { new(22, 140) }; var line = new PointCollection();
                double sp = 60 + rnd() * 30;
                for (int x = 22; x <= 378; x += 12) { sp = Math.Clamp(sp + (rnd() - 0.45) * 22, 20, 110); area.Add(new(x, 140 - sp)); line.Add(new(x, 140 - sp)); }
                area.Add(new(378, 140));
                card.Children.Add(new Polygon { Points = area, Fill = A(55, 0.35) });
                card.Children.Add(new Polyline { Points = line, Stroke = A(72), StrokeThickness = 2.5 });
                Put(card, Bar(356, 6, C(0, 35)), 22, 152);
                Put(card, Bar(356 * (0.3 + rnd() * 0.6), 6, A(62)), 22, 152);
                Put(art, card, 470, 8);
                break;
            case 4: // tabs in rows, the browser shape, one of them active with its accent line
                int lit = 1 + (int)(rnd() * 2);
                for (int row = 0; row < 2; row++)
                {
                    Put(art, Bar(520, 58, C(0, 12, 0.8), 12), 440 + row * 40, 22 + row * 76);
                    for (int t = 0; t < 3; t++)
                    {
                        bool on = row == 0 && t == lit;
                        var tab = Group();
                        tab.Children.Add(Shape("M0,52 Q12,52 12,40 L12,12 Q12,0 24,0 L146,0 Q158,0 158,12 L158,40 Q158,52 170,52 Z", on ? C(0, 42) : C(t * 10, 24 + row * 5, 0.95)));
                        if (on) Put(tab, Bar(130, 4, A(65), 2), 20, 0);
                        Put(tab, Shape(FolderShape, on ? A(65) : C(30, 58, 0.8), 0.45), 26, 16);
                        Put(tab, Bar(40 + rnd() * 50, 9, C(0, 85, on ? 0.9 : 0.4)), 60, 22);
                        Put(art, tab, 448 + t * 160 + row * 40, 28 + row * 76);
                    }
                }
                break;
            case 5: // drives, as in the sidebar: a name and how full each one is, one nearly full in red
                int drives = 4 + (int)(rnd() * 2), full = (int)(rnd() * drives);
                for (int d = 0; d < drives; d++)
                {
                    double y = 16 + d * (150.0 / drives), used = d == full ? 0.93 : 0.15 + rnd() * 0.65;
                    Put(art, Shape("M0,4 Q0,0 4,0 L30,0 Q34,0 34,4 L34,18 Q34,22 30,22 L4,22 Q0,22 0,18 Z", C(0, 62, 0.75)), 500, y);
                    Put(art, new Ellipse { Width = 5, Height = 5, Fill = A(70) }, 525, y + 13);
                    Put(art, Bar(70 + rnd() * 60, 10, C(0, 85, 0.6)), 548, y);
                    Put(art, Bar(330, 11, C(0, 28), 5.5), 548, y + 16);
                    var fill = Bar(330 * used, 11, d == full ? new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x47)) : A(60));
                    if (d == full) fill.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0x33, 0x47), BlurRadius = 9, ShadowDepth = 0 };
                    Put(art, fill, 548, y + 16);
                }
                break;
            case 6: // the Folders tree: branches opening down and to the right
                var tree = Group(tilt / 2, 600, 90);
                double ty = 4; int depth = 0;
                for (int i = 0; i < 6; i++)
                {
                    depth = Math.Clamp(depth + (rnd() < 0.55 ? 1 : -1) * (i == 0 ? 0 : 1), 0, 4);
                    double x = 480 + depth * 56;
                    Put(tree, new Line { X1 = x - 26, Y1 = ty + 14, X2 = x - 4, Y2 = ty + 14, Stroke = C(0, 70, 0.55), StrokeThickness = 2.5 }, 0, 0);
                    Put(tree, new Line { X1 = x - 26, Y1 = ty - 16, X2 = x - 26, Y2 = ty + 14, Stroke = C(0, 70, 0.55), StrokeThickness = 2.5 }, 0, 0);
                    Put(tree, Shape(FolderShape, i == 5 ? A(65) : C(depth * 15, 42 + depth * 7, 0.95), 0.62), x, ty);
                    Put(tree, Bar(60 + rnd() * 110, 10, C(0, 85, i == 5 ? 0.9 : 0.45)), x + 42, ty + 10);
                    ty += 30;
                }
                art.Children.Add(tree);
                break;
            case 7: // the path bar: segments and chevrons, the last one lit
                for (int row = 0; row < 3; row++)
                {
                    double x = 430 + row * 40 + tilt * 3;
                    int segs = 3 + (int)(rnd() * 2);
                    for (int s = 0; s < segs; s++)
                    {
                        double w = 50 + rnd() * 50; bool last = s == segs - 1 && row == 1;
                        Put(art, Bar(w, 30, last ? A(50) : C(s * 8, 24 + row * 4, 0.85), 8), x, 25 + row * 48);
                        Put(art, Bar(w - 22, 6, C(0, 85, last ? 0.9 : 0.45)), x + 11, 37 + row * 48);
                        x += w + 6;
                        if (s < segs - 1) { Put(art, Shape("M0,0 L7,7 L0,14", null, 1, C(0, 75, 0.7)), x, 33 + row * 48); x += 16; }
                    }
                }
                break;
            case 8: // thumbnails: little pictures, each a hill and a sun
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 5; x++)
                    {
                        double dh = rnd() * 70 - 35;
                        var tile = Group();
                        tile.Children.Add(new Rectangle { Width = 84, Height = 54, RadiusX = 6, RadiusY = 6, Fill = C(dh, 28 + rnd() * 12) });
                        Put(tile, new Ellipse { Width = 12, Height = 12, Fill = A(75) }, 58, 8);
                        tile.Children.Add(Shape($"M4,50 L{24 + rnd() * 20:F0},{20 + rnd() * 12:F0} L50,42 L62,30 L80,50 Z", C(dh + 20, 50 + rnd() * 15)));
                        Put(art, tile, 450 + x * 92 + (y % 2) * 30, 6 + y * 60);
                    }
                break;
            case 9: // the Drop bin: a tray, files dropping into it from several sides
                Put(art, Shape("M0,0 L240,0 L220,70 Q218,78 210,78 L30,78 Q22,78 20,70 Z", C(0, 26), 1, C(0, 65, 0.5)), 560, 100);
                Put(art, Bar(240, 6, A(60), 3), 560, 96);
                for (int i = 0; i < 5; i++)
                {
                    var s = Group(-30 + rnd() * 60, 27, 37);
                    s.Children.Add(Shape(SheetShape, i % 2 == 0 ? A(55, 0.9) : C(i * 15, 45, 0.9), 0.5));
                    Put(art, s, 520 + i * 60 + rnd() * 20, 8 + rnd() * 50);
                }
                break;
            case 10: // search: a list, one row found, the magnifier over it
                for (int row = 0; row < 8; row++)
                {
                    bool hit = row == 3 || row == 6 && rnd() < 0.5;
                    if (hit) Put(art, Bar(380, 18, A(45, 0.6), 5), 470, 8 + row * 21);
                    Put(art, Shape(FolderShape, C(row * 6, 55, 0.7), 0.26), 480, 12 + row * 21);
                    Put(art, Bar(80 + rnd() * 160, 6, C(0, 85, hit ? 0.9 : 0.35)), 502, 14 + row * 21);
                }
                var lens = Group(tilt, 700, 90);
                Put(lens, new Ellipse { Width = 90, Height = 90, Stroke = C(0, 85), StrokeThickness = 8, Fill = C(0, 80, 0.12) }, 690, 40);
                Put(lens, new Line { X1 = 765, Y1 = 118, X2 = 815, Y2 = 168, Stroke = C(0, 85), StrokeThickness = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 0, 0);
                art.Children.Add(lens);
                break;
            default: // colours by age: rows with their bar and badge, red for minutes ago to blue-grey for a year
                string[] ages = { "#F43F5E", "#FB923C", "#FACC15", "#34D399", "#2DD4BF", "#60A5FA", "#94A3B8" };
                var list = Group(tilt / 2, 650, 90);
                list.Children.Add(new Rectangle { Width = 440, Height = 200, RadiusX = 14, RadiusY = 14, Fill = C(0, 16, 0.85) });
                for (int row = 0; row < 6; row++)
                {
                    var age = MainWindow.Hex(ages[(row + (int)(rnd() * 2)) % ages.Length]);
                    double y = 14 + row * 30;
                    if (row % 2 == 1) Put(list, Bar(440, 30, C(0, 22, 0.6), 0), 0, y - 4);
                    Put(list, Bar(8, 22, age, 3), 16, y);
                    Put(list, Bar(110 + rnd() * 150, 10, C(0, 85, 0.55)), 36, y + 6);
                    Put(list, Bar(56, 20, age, 5), 330, y + 1);
                    Put(list, Bar(30, 6, new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0))), 343, y + 8);
                }
                Put(art, list, 450, -10);
                break;
        }

        // a big faint folder or sheet behind it all, a different size and angle each time
        var ghost = Group(-25 + rnd() * 50, 100, 100);
        ghost.Children.Add(rnd() < 0.5 ? Shape(FolderShape, C(0, 70, 0.07), 4 + rnd() * 2) : Shape(SheetShape, C(0, 70, 0.07), 1.6 + rnd()));
        Canvas.SetLeft(ghost, 360 + rnd() * 380); Canvas.SetTop(ghost, -90 + rnd() * 60);
        art.Children.Insert(0, ghost);

        // the ground: dark to lighter at an angle of its own, sometimes with a glow behind the picture
        var ground = new LinearGradientBrush(Hsl(hue, sat, 13), Hsl(hue + second / 4, sat, 30), 20 + rnd() * 50);
        var glow = rnd() < 0.5 ? new Ellipse { Width = 520, Height = 360, Fill = new RadialGradientBrush(Hsl(hue + second, sat, 50, 0.35), Hsl(hue + second, sat, 50, 0)) } : null;
        if (glow != null) { Canvas.SetLeft(glow, 380 + rnd() * 200); Canvas.SetTop(glow, -120 + rnd() * 60); art.Children.Insert(0, glow); }
        var picture = new Viewbox { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Right, Child = art };
        // the words sit on the dark side; a shade under them keeps them readable over any picture
        var shade = new Border { Background = new LinearGradientBrush(Color.FromArgb(0xB0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0) };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(26, 0, 0, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        words.Children.Add(new TextBlock { Text = "WHAT’S NEW IN", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)) });
        words.Children.Add(new TextBlock { Text = r.Version, FontSize = 34, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        words.Children.Add(new TextBlock { Text = r.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)) });
        return new Grid { Background = ground, Children = { picture, shade, words } };
    }
}
