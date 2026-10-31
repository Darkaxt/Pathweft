using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Preview;

// Archive metadata only. Children are materialized as WPF rows on expansion,
// so a large archive does not build an entire visual tree on the UI thread.
internal static class ArchiveTree
{
    internal sealed class Node(string name, string path, bool directory)
    {
        internal string Name { get; } = name;
        internal string Path { get; } = path;
        internal bool Directory { get; set; } = directory;
        internal long Size { get; set; }
        internal Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
    }

    internal static Node Read(string archive, string inner, CancellationToken token)
    {
        var root = new Node("", "", true);
        string prefix = inner.Length == 0 ? "" : Archive.Clean(inner) + "/";
        foreach (var entry in Archive.Read(archive))
        {
            token.ThrowIfCancellationRequested();
            if (!entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = entry.Path[prefix.Length..];
            if (relative.Length == 0) continue;
            var parts = relative.Split('/'); var parent = root;
            for (int i = 0; i < parts.Length; i++)
            {
                bool directory = i < parts.Length - 1 || entry.IsDir;
                if (!parent.Children.TryGetValue(parts[i], out var node))
                {
                    node = new(parts[i], (parent.Path.Length == 0 ? "" : parent.Path + "/") + parts[i], directory);
                    parent.Children.Add(parts[i], node);
                }
                node.Directory |= directory;
                if (i == parts.Length - 1) node.Size = entry.Size;
                parent = node;
            }
        }
        return root;
    }

    internal static TreeView View(Node root, ContextObject context)
    {
        var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(6) };
        tree.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
        tree.Resources[SystemColors.HighlightBrushKey] = Application.Current.FindResource("AccentSelect");
        tree.Resources[SystemColors.HighlightTextBrushKey] = Application.Current.FindResource("AccentSelectText");
        tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Application.Current.FindResource("AccentSelect");
        tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Application.Current.FindResource("AccentSelectText");
        VirtualizingPanel.SetIsVirtualizing(tree, true);
        VirtualizingPanel.SetVirtualizationMode(tree, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tree, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(tree, "Archive contents");
        TreeViewItem Row(Node item)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            // Use Windows' type icon by filename; this never opens/extracts an entry.
            header.Children.Add(new Image { Source = Thumbnails.TypeIcon(item.Name, item.Directory), Width = 16, Height = 16, Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center });
            if (!item.Directory)
            {
                var size = new TextBlock { Text = MainWindow.Fmt(item.Size), Margin = new(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                size.SetResourceReference(TextBlock.ForegroundProperty, "Palette.8A97AA"); header.Children.Add(size);
            }
            var row = new TreeViewItem { Header = header, Tag = item, Padding = new(2, 3, 4, 3), ToolTip = item.Path };
            row.SetResourceReference(Control.ForegroundProperty, "Palette.E8EEF6");
            AutomationProperties.SetName(row, item.Name);
            if (item.Children.Count > 0)
            {
                row.Items.Add("…");
                row.Expanded += (_, e) =>
                {
                    e.Handled = true;
                    if (!context.IsCurrent || row.Items.Count != 1 || row.Items[0] is not string) return;
                    row.Items.Clear(); Fill(row, item);
                };
            }
            return row;
        }
        void Fill(ItemsControl parent, Node node)
        {
            foreach (var child in node.Children.Values.OrderByDescending(n => n.Directory).ThenBy(n => n.Name, ExplorerOrder.Instance))
                parent.Items.Add(Row(child));
        }
        Fill(tree, root);
        if (tree.Items.Count == 0) tree.Items.Add(new TextBlock { Text = "Empty archive folder", Margin = new(8) });
        return tree;
    }
}
