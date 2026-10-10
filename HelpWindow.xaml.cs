using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

// F1: every key and the features that have no key, in one searchable list. The text is kept here
// next to nothing else, so a new shortcut in MainWindow.OnPreviewKeyDown needs a line here too.
public partial class HelpWindow : Window
{
    static readonly (string Section, (string Key, string What)[] Rows)[] Help =
    {
        ("Panes and tabs", new[]
        {
            ("Tab", "Switch to the other pane"),
            ("Ctrl+P", "One pane or two (also in Settings)"),
            ("Ctrl+U", "Swap the two panes"),
            ("Ctrl+T", "New tab"),
            ("Ctrl+W", "Close the tab (pinned tabs stay)"),
            ("Ctrl+Tab / Ctrl+Shift+Tab", "Next / previous tab"),
            ("Middle-click a folder", "Open it in a new tab behind this one: in the list, the sidebar, the folder tree, the path, and on Back, Forward and Up"),
            ("Middle-click a tab", "Close it"),
            ("Right-click a tab", "Pin or unpin it. Pinned tabs sit at the left, have no close button and are remembered"),
            ("Drag a tab", "Reorder, or drop it on the other pane to move it there"),
            ("Many tabs", "Below 120 px each they wrap into more rows, which stay where they are when you switch tabs"),
            ("Double-click the splitter", "Make the two panes equal again"),
        }),
        ("Moving around", new[]
        {
            ("Enter / F3", "Open"),
            ("Backspace / Alt+Up", "Up one folder"),
            ("Alt+Left / Alt+Right", "Back / Forward (also the mouse side buttons)"),
            ("Ctrl+L", "Type a path; Tab completes, Down picks a suggestion"),
            ("Chevron after a path segment", "The folders inside it, to jump sideways"),
            ("Ctrl+F", "Filter this folder as you type; Enter searches every subfolder, * and ? work"),
            ("Esc", "Leave search results"),
            ("Ctrl+D", "Pin the folder to Quick access"),
        }),
        ("Files", new[]
        {
            ("F5 / F6", "Copy / move to the other pane (with one pane, the first press brings it back)"),
            ("Ctrl+C / Ctrl+X / Ctrl+V", "Copy, cut, paste, also with Explorer"),
            ("F2", "Rename in place; with several selected, rename them all"),
            ("Shift+F2", "Compare the two panes"),
            ("F4", "Edit in the built-in code editor"),
            ("F7 / Ctrl+Shift+N", "New folder"),
            ("Ctrl+N", "New file"),
            ("F8 / Del", "Delete to the Recycle Bin"),
            ("Shift+Del, Enter", "Delete permanently"),
            ("Ctrl+Z", "Undo the last copy, move, rename, new file or folder, zip or extract"),
            ("Ctrl+Shift+C", "Copy the selected paths as text"),
            ("Alt+Enter", "Properties"),
            ("Space", "Quick View: images, video, audio, PDF, text; or QuickLook, if you turn it on in Settings → Files"),
        }),
        ("Selecting", new[]
        {
            ("Drag on empty space", "Select with a rectangle; Ctrl adds to the selection"),
            ("Num +", "Select by pattern, like *.jpg;*.png"),
            ("Num −", "Deselect by pattern"),
            ("Num *", "Invert the selection"),
        }),
        ("View", new[]
        {
            ("Ctrl+Shift+1…5", "Details, List, icons M, L, XL"),
            ("Ctrl+wheel", "Zoom through the views"),
            ("Click a column header", "Sort; the selected file stays in sight"),
            ("Ctrl+H", "Show hidden files"),
            ("Ctrl+R", "Refresh and measure folder sizes again"),
            ("Settings → Text size", "Make the whole window larger"),
        }),
        ("Everything else", new[]
        {
            ("Ctrl+`", "Terminal in this folder"),
            ("Ctrl+,", "Settings"),
            ("F1", "This help"),
        }),
    };

    public HelpWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => Find.Focus();
        Show("");
    }

    void Find_Changed(object s, TextChangedEventArgs e)
    {
        FindHint.Visibility = Find.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        Show(Find.Text.Trim());
    }

    void Show(string query)
    {
        Sections.Children.Clear();
        foreach (var (section, rows) in Help)
        {
            var hits = rows.Where(r => query == "" || r.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                    || r.What.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) continue;
            Sections.Children.Add(new TextBlock { Text = section, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new(0, Sections.Children.Count == 0 ? 0 : 14, 0, 6) });
            foreach (var (key, what) in hits)
            {
                var row = new Grid { Margin = new(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var k = new TextBlock { Text = key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Foreground = (Brush)FindResource("Accent"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 0) };
                var w = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Hex("#C9D3E0") };
                Grid.SetColumn(w, 1);
                row.Children.Add(k); row.Children.Add(w);
                Sections.Children.Add(row);
            }
        }
        if (Sections.Children.Count == 0)
            Sections.Children.Add(new TextBlock { Text = "Nothing matches.", Foreground = MainWindow.Hex("#8A97AA") });
    }
}
