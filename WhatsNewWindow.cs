
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

// What's new, the way PowerToys (and Shelf 3.66) show it: every release in a list on the left, under the month
// it came out, and the chosen one on the right under the picture of its month. The notes are whatsnew.txt, built
// into the exe, so it works offline and shows every release, not only the latest.
public class WhatsNewWindow : Window
{
    internal record Release(string Version, DateTime Date, string Title, List<string> Lines);

    readonly List<Release> releases = Load();
    readonly StackPanel nav = new();
    // 960 x 170 in every app (Shelf too), so a picture made at that size fits exactly; a narrower window shrinks it
    readonly Border banner = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Left };
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
        Title = "What's new — Pathweft";
        Width = 1240; Height = 720; MinWidth = 700; MinHeight = 420; // room for the list and a 960 x 170 banner
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true; // minimized, it goes to the taskbar like Settings, not a stub above it (Rico, 1.0.61)
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

        banner.Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = Banner(r) };
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
            else if (i % 2 == 1) t.Inlines.Add(new Run(parts[i]) { FontWeight = FontWeights.SemiBold, Foreground = MainWindow.Hex("#FFFFFF") });
            else t.Inlines.Add(new Run(parts[i])); // takes the block's colour
        return t;
    }

    // ---- the banner: the studio's picture for the month the release came out, built in as whatsnew-yyyy-MM.jpg ----

    internal static System.IO.Stream Picture(Release r) =>
        typeof(WhatsNewWindow).Assembly.GetManifestResourceStream($"whatsnew-{r.Date:yyyy-MM}.jpg");

    static UIElement Banner(Release r)
    {
        using var stream = Picture(r);
        var collage = new System.Windows.Media.Imaging.BitmapImage();
        collage.BeginInit();
        collage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        collage.StreamSource = stream;
        collage.EndInit();
        collage.Freeze();
        return new Image { Source = collage, Width = 960, Height = 170, Stretch = Stretch.UniformToFill };
    }
}
