using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Preview;

// Shared by the embedded PDF, model and media toolbars.
internal static class PreviewControls
{
    static readonly Dictionary<string,ImageSource> rendererIcons=new(StringComparer.OrdinalIgnoreCase);
    internal static ImageSource RendererIcon(string renderer)
    {
        string id=renderer?.ToLowerInvariant();
        if(id is not ("wordcraft" or "gridcraft" or "deckcraft" or "libreoffice"))return null;
        if(!rendererIcons.TryGetValue(id,out var icon))
        {
            icon=BitmapFrame.Create(new Uri("pack://application:,,,/vendor/Office/icons/"+id+".ico"),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            icon.Freeze();rendererIcons.Add(id,icon);
        }
        return icon;
    }
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
