
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

    static UIElement Banner(Release r)
    {
        var rnd = Seeded(r.Version);
        var v = r.Version.Split('.').Select(int.Parse).ToArray();
        int n = v[0] * 10000 + v[1] * 100 + v[2];
        double hue = n * 137.508 % 360;            // the golden angle: neighbouring releases never share a colour
        Brush C(double dh, double l, double a = 1) => new SolidColorBrush(Hsl(hue + dh, 70, l, a));
        var art = new Canvas { Width = 900, Height = 180 };
        void Add(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); art.Children.Add(e); }

        switch (v[2] % 5) // the five pictures take turns
        {
            case 0: // two panes of rows, File Labs' own shape
                for (int pane = 0; pane < 2; pane++)
                {
                    double x0 = 500 + pane * 190, tilt = -8 + pane * 4;
                    var g = new Canvas { RenderTransform = new RotateTransform(tilt, 90, 90) };
                    g.Children.Add(new Rectangle { Width = 175, Height = 190, RadiusX = 12, RadiusY = 12, Fill = C(pane * 30, 26), Stroke = C(pane * 30, 70, 0.45), StrokeThickness = 1.5 });
                    for (int row = 0; row < 8; row++)
                    {
                        var line = new Rectangle { Width = 40 + rnd() * 100, Height = 7, RadiusX = 3, RadiusY = 3, Fill = rnd() < 0.15 ? C(40, 70, 0.95) : C(row * 8, 55, 0.35) };
                        Canvas.SetLeft(line, 16); Canvas.SetTop(line, 22 + row * 20);
                        g.Children.Add(line);
                    }
                    Add(g, x0, -10 + pane * 18);
                }
                break;
            case 1: // a tilted grid of folders, a few lit up
                var grid = new Canvas { RenderTransform = new RotateTransform(-14, 700, 90) };
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 7; x++)
                    {
                        var folder = new Path
                        {
                            Data = Geometry.Parse("M0,8 Q0,2 6,2 L18,2 L24,8 L48,8 Q54,8 54,14 L54,40 Q54,46 48,46 L6,46 Q0,46 0,40 Z"),
                            Fill = rnd() < 0.18 ? C(40, 70, 0.9) : C(rnd() * 60, 30 + rnd() * 25, 0.6),
                        };
                        Canvas.SetLeft(folder, 470 + x * 66); Canvas.SetTop(folder, -30 + y * 64);
                        grid.Children.Add(folder);
                    }
                art.Children.Add(grid);
                break;
            case 2: // soft orbs
                for (int i = 0; i < 6; i++)
                {
                    double rad = 30 + rnd() * 80;
                    Add(new Ellipse { Width = rad * 2, Height = rad * 2, Fill = C(i * 30, 45 + rnd() * 30, 0.25 + rnd() * 0.3) }, 520 + rnd() * 360 - rad, rnd() * 180 - rad);
                }
                break;
            case 3: // a transfer's speed chart, line over line
                double amp = 12 + rnd() * 20, freq = 0.01 + rnd() * 0.015;
                for (int i = 0; i < 9; i++)
                {
                    var pts = new PointCollection();
                    for (int x = 380; x <= 900; x += 10) pts.Add(new Point(x, 30 + i * 16 + Math.Sin(x * freq + i * 0.6) * amp));
                    art.Children.Add(new Polyline { Points = pts, Stroke = C(i * 12, 65, 0.15 + i * 0.07), StrokeThickness = 3 });
                }
                break;
            default: // a fan of file sheets with folded corners
                int sheets = 4 + (int)(rnd() * 3);
                for (int i = 0; i < sheets; i++)
                {
                    double angle = (i - (sheets - 1) / 2.0) * (10 + rnd() * 6);
                    var sheet = new Canvas { RenderTransform = new RotateTransform(angle, 55, 170) };
                    sheet.Children.Add(new Path { Data = Geometry.Parse("M10,0 L84,0 L110,26 L110,140 Q110,150 100,150 L10,150 Q0,150 0,140 L0,10 Q0,0 10,0 Z"), Fill = C(i * 25, 30 + i * 8), Stroke = C(i * 25, 75, 0.5) });
                    sheet.Children.Add(new Path { Data = Geometry.Parse("M84,0 L84,26 L110,26 Z"), Fill = C(i * 25, 60, 0.5) });
                    for (int l = 0; l < 4; l++)
                    {
                        var line = new Rectangle { Width = 50 + rnd() * 30, Height = 6, RadiusX = 3, RadiusY = 3, Fill = C(i * 25 + 40, 70, 0.3) };
                        Canvas.SetLeft(line, 14); Canvas.SetTop(line, 50 + l * 18);
                        sheet.Children.Add(line);
                    }
                    Add(sheet, 600 + i * 30, 30);
                }
                break;
        }

        var ground = new LinearGradientBrush(Hsl(hue, 70, 16), Hsl(hue + 35, 70, 34), 45);
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
