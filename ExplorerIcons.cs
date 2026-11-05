using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace FileExplorer;

// Explorer's installed vector artwork, read as data. No Windows assets are shipped.
// The glyph form keeps the supported Windows 10 path independent of these Win11 files.
internal static class ExplorerIcons
{
    static readonly (string Key,string File,string Glyph)[] icons =
    {
        ("Rename","windows.rename.svg","\uE8AC"),("Open","windows.openfolder.svg","\uE8E5"),
        ("Copy","windows.copy.svg","\uE8C8"),("Move","windows.sendto.svg","\uE8DE"),
        ("Cut","windows.cut.svg","\uE8C6"),("Paste","windows.paste.svg","\uE77F"),
        ("NewFolder","newfolder.svg","\uE8F4"),("NewFile",null,"\uEA1A"),
        ("Delete","windows.ribbondelete.svg","\uE74D"),("Terminal",null,"\uE756"),
        ("AnalyzeSpace",null,"\uE9F5"),
        ("Hidden","windows.showhiddenfiles.svg","\uE890"),("Panes",null,"\uE89F"),
        // These historical filenames are opposite to their visible roles.
        ("Preview","windows.readingpane.svg","\uE90D"),("DetailsPane","windows.previewpane.svg","\uE9F9"),
        ("Transfers",null,"\uE8AB"),("Sort","SortBy.svg","\uE8CB"),
        ("IconsXL","windows.iconsize.extralarge.svg","\uE7F4"),("IconsL","windows.iconsize.large.svg","\uE770"),
        ("IconsM","windows.iconsize.medium.svg","\uE770"),("List","windows.iconsize.list.svg","\uEA37"),
        ("Details","windows.iconsize.details.svg","\uE700")
    };
    static readonly Dictionary<(string Variant,string Key),DrawingImage> cache=new();
    static string SourceRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "SystemApps","MicrosoftWindows.Client.FileExp_cw5n1h2txyewy","FileExplorerExtensions","Assets","images");

    internal static void Apply()
    {
        string variant=SystemParameters.HighContrast
            ? (SystemColors.WindowColor.R+SystemColors.WindowColor.G+SystemColors.WindowColor.B<384?"contrast-black":"contrast-white")
            : Appearance.Light?"contrast-standard/theme-light":"contrast-standard/theme-dark";
        foreach(var icon in icons)
        {
            // High-contrast colors can change without changing the variant name.
            if(SystemParameters.HighContrast||!cache.TryGetValue((variant,icon.Key),out var image))
            {
                image=Load(icon.File,variant)??Glyph(icon.Glyph);
                if(!SystemParameters.HighContrast)cache[(variant,icon.Key)]=image;
            }
            Application.Current.Resources["CommandIcon."+icon.Key]=image;
        }
    }

    static DrawingImage Load(string name,string variant)
    {
        if(name==null)return null;
        string path=Path.Combine(SourceRoot,variant.Replace('/',Path.DirectorySeparatorChar),name);
        try
        {
            using var stream=File.OpenRead(path);
            using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536});
            var svg=XDocument.Load(reader).Root;
            if(svg?.Name.LocalName!="svg"||svg.Attribute("viewBox")?.Value!="0 0 16 16")return null;
            var drawing=new DrawingGroup{ClipGeometry=new RectangleGeometry(new Rect(0,0,16,16))};
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,16,16))));
            // This finite system asset set contains path fills/strokes and full-viewBox
            // clipping only; unsupported future SVG structures use the glyph form.
            if(svg.Descendants().Any(e=>e.Name.LocalName is not ("path" or "g" or "defs" or "clipPath")))return null;
            foreach(var element in svg.Descendants().Where(e=>e.Name.LocalName=="path"&&!e.Ancestors().Any(a=>a.Name.LocalName=="defs")))
            {
                var geometry=Geometry.Parse((string)element.Attribute("d"));
                if(geometry is PathGeometry paths)paths.FillRule=(string)element.Attribute("fill-rule")=="evenodd"?FillRule.EvenOdd:FillRule.Nonzero;
                var fill=ColorBrush((string)element.Attribute("fill"));
                var stroke=ColorBrush((string)element.Attribute("stroke"));
                Pen pen=stroke==null?null:new(stroke,1);
                if(pen!=null&&(string)element.Attribute("stroke-linecap")=="round")pen.StartLineCap=pen.EndLineCap=PenLineCap.Round;
                var part=new DrawingGroup();
                if(element.Attribute("opacity") is {} opacity)part.Opacity=double.Parse(opacity.Value,CultureInfo.InvariantCulture);
                part.Children.Add(new GeometryDrawing(fill,pen,geometry));drawing.Children.Add(part);
            }
            drawing.Freeze();var result=new DrawingImage(drawing);result.Freeze();return result;
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or XmlException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException){return null;}
    }

    static Brush ColorBrush(string value)
    {
        if(value==null||value=="none")return null;
        var color=(Color)ColorConverter.ConvertFromString(value);
        if(SystemParameters.HighContrast)
        {
            bool sourceDark=SystemColors.WindowColor.R+SystemColors.WindowColor.G+SystemColors.WindowColor.B<384;
            bool ink=color.R+color.G+color.B>384;
            color=sourceDark==ink?SystemColors.WindowTextColor:SystemColors.WindowColor;
        }
        var brush=new SolidColorBrush(color);brush.Freeze();return brush;
    }

    internal static DrawingImage Glyph(string glyph)
    {
        Brush ink=SystemParameters.HighContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Appearance.Light?Color.FromRgb(70,70,70):Color.FromRgb(224,223,223));
        var text=new FormattedText(glyph,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),16,
            ink,1);
        var geometry=text.BuildGeometry(new Point());var bounds=geometry.Bounds;
        if(bounds.Width>0&&bounds.Height>0)
        {
            double scale=Math.Min(14/bounds.Width,14/bounds.Height);
            geometry.Transform=new MatrixTransform(scale,0,0,scale,8-(bounds.X+bounds.Width/2)*scale,8-(bounds.Y+bounds.Height/2)*scale);
        }
        var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,16,16))));
        group.Children.Add(new GeometryDrawing(ink,null,geometry));group.Freeze();var image=new DrawingImage(group);image.Freeze();return image;
    }

    static Brush CommandInk=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:new SolidColorBrush(Appearance.Light?Color.FromRgb(70,70,70):Color.FromRgb(224,223,223));
    internal static DrawingImage Crossed(DrawingImage source)
    {
        var group=new DrawingGroup();group.Children.Add(source.Drawing);
        group.Children.Add(new GeometryDrawing(null,new Pen(CommandInk,1.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},new LineGeometry(new Point(2,14),new Point(14,2))));
        group.Freeze();var image=new DrawingImage(group);image.Freeze();return image;
    }
    internal static DrawingImage PaneLayout(bool two)
    {
        var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,16,16))));
        var pen=new Pen(CommandInk,1);
        group.Children.Add(new GeometryDrawing(null,pen,new RectangleGeometry(new Rect(1,2,14,12),1.5,1.5)));
        if(two)group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(8,2),new Point(8,14))));
        group.Freeze();var image=new DrawingImage(group);image.Freeze();return image;
    }

    internal static System.Windows.Controls.Image Image(string key,double size=14)
    {
        var image=new System.Windows.Controls.Image{Width=size,Height=size,Stretch=Stretch.Uniform,SnapsToDevicePixels=true};
        image.SetResourceReference(System.Windows.Controls.Image.SourceProperty,"CommandIcon."+key);return image;
    }
#if DEBUG
    internal static DrawingImage NativeForVerification(string key,string variant)=>Load(icons.Single(icon=>icon.Key==key).File,variant);
#endif
}
