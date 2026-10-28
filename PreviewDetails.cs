// Existing File Labs preview metadata/actions, shared by the internal viewers.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer;

public partial class MainWindow
{
    internal static (string Name, string State) ReelLine(Sisters.ReelFile f)
    {
        var name = f.Series == null ? f.Title + (f.Year is { } y ? $" ({y})" : "")
            : f.Series + " — " + (f.Season is { } s && f.Episode is { } n ? $"S{s:00}E{n:00}" + (f.Title != f.Series ? " " + f.Title : "") : f.Title);
        var state = !f.Watched ? "not watched"
            : f.LastWatched is { } d ? "watched " + d.ToString(d.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : "watched";
        return (name, state);
    }
    internal static BitmapImage Poster(string path)
    {
        try
        {
            if (new FileInfo(path) is not { Exists: true, Length: > 0 and < 10 << 20 }) return null;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 200; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
            or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return null; }
    }
    internal UIElement BackupView(Entry entry, Backups.Info backup)
    {
        var content = new StackPanel { Margin = new(20) };
        content.Children.Add(new TextBlock { Text = $"{backup.App} backup", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var made = backup.Made?.ToString("dddd, yyyy-MM-dd HH:mm:ss") ?? entry.FullDate;
        var what = backup.Unreadable ? "" : backup.Encrypted ? " · encrypted" : " · not encrypted";
        content.Children.Add(new TextBlock { Text = $"{made} · {entry.SizeText}{what}", Foreground = Hex("#99FFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 12) });
        if (backup.Unreadable) { content.Children.Add(new TextBlock { Text = "Could not read this backup", Foreground = Hex("#F87171"), TextWrapping = TextWrapping.Wrap }); return content; }
        if (backup.Encrypted) content.Children.Add(new TextBlock { Text = $"Encrypted — open it in {backup.App} with its password", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
        var open = new Button { Content = $"Open in {backup.App}", Style = (Style)FindResource("Tab"), Tag = "on", Padding = new(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => OpenBackup(backup.Id, entry.Path); content.Children.Add(open);
        var rows = new Grid { Margin = new(0, 14, 0, 0) };
        rows.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); rows.ColumnDefinitions.Add(new());
        foreach (var (name, value) in backup.Rows)
        {
            rows.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var left = new TextBlock { Text = name, Foreground = Hex("#99FFFFFF"), Margin = new(0, 2, 16, 2) };
            var right = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 2) };
            Grid.SetRow(left, rows.RowDefinitions.Count - 1); Grid.SetRow(right, rows.RowDefinitions.Count - 1); Grid.SetColumn(right, 1);
            rows.Children.Add(left); rows.Children.Add(right);
        }
        content.Children.Add(rows); return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    internal async Task AddReelPreview(string path, DockPanel root, Func<bool> current)
    {
        var (file, poster) = await Task.Run(() => Sisters.Reel(path) is { } r ? (r, r.Poster != null ? Poster(r.Poster) : null) : default);
        if (file == null || !current()) return;
        var (name, state) = ReelLine(file);
        var text = new TextBlock { Foreground = Hex("#99FFFFFF"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.Inlines.Add(new System.Windows.Documents.Run(name) { Foreground = Hex("#E6FFFFFF"), FontWeight = FontWeights.SemiBold }); text.Inlines.Add($" · {state} · ");
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Reel")) { Foreground = Hex("#B4BFCE"), ToolTip = "Open Reel" };
        link.Click += (_, _) => { if (Sisters.Exe("reel") is { } exe) System.Diagnostics.Process.Start(exe); }; text.Inlines.Add(link);
        var header = new DockPanel { Margin = new(12, 10, 12, 10) };
        if (poster != null) header.Children.Add(new Image { Source = poster, Width = 56, Margin = new(0, 0, 12, 0) });
        header.Children.Add(text); DockPanel.SetDock(header, Dock.Top); root.Children.Insert(0, header);
    }
}
