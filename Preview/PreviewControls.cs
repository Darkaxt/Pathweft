using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Preview;

// Shared only by the embedded PDF and model toolbars.
internal static class PreviewControls
{
    internal static Button Icon(MainWindow owner, string name, string glyph)
    {
        var button = new Button
        {
            Style = (Style)owner.FindResource("PreviewIcon"), ToolTip = name,
            MinWidth = 32, MinHeight = 32, Margin = new(2), Padding = new(7),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        SetIcon(button, name, glyph);
        return button;
    }
    internal static void SetIcon(Button button, string name, string glyph)
    {
        button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        button.ToolTip = name;
        AutomationProperties.SetName(button, name);
    }
}
