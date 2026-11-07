using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FileExplorer;

// Transfer actions align visible font outlines, not the different line boxes of
// Fluent Icons and Segoe UI. Keep native text drawing and inherited button ink.
internal sealed class TransferActionLabel : FrameworkElement
{
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(TransferActionLabel),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlyphProperty=DependencyProperty.Register(nameof(Glyph),typeof(string),typeof(TransferActionLabel),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value); }
    public string Glyph { get=>(string)GetValue(GlyphProperty);set=>SetValue(GlyphProperty,value); }

    static TransferActionLabel()
    {
        TextElement.ForegroundProperty.OverrideMetadata(typeof(TransferActionLabel),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
        TextElement.FontFamilyProperty.OverrideMetadata(typeof(TransferActionLabel),new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
        TextElement.FontSizeProperty.OverrideMetadata(typeof(TransferActionLabel),new FrameworkPropertyMetadata(12.0,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
    }
    FormattedText Format(string value,bool icon)=>new(value??"",CultureInfo.CurrentUICulture,FlowDirection,
        new Typeface(icon?new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"):TextElement.GetFontFamily(this),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),
        TextElement.GetFontSize(this),TextElement.GetForeground(this),VisualTreeHelper.GetDpi(this).PixelsPerDip);
    internal double TextOffset => Format(Glyph,true).BuildGeometry(new Point()).Bounds.Width+6;
    protected override Size MeasureOverride(Size available)
    {
        var label=Format(Text,false);var icon=Format(Glyph,true);
        return new Size(TextOffset+label.WidthIncludingTrailingWhitespace,Math.Max(label.Height,icon.Height));
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var icon=Format(Glyph,true);var label=Format(Text,false);
        var iconInk=icon.BuildGeometry(new Point()).Bounds;var labelInk=label.BuildGeometry(new Point()).Bounds;
        if(!iconInk.IsEmpty)drawing.DrawText(icon,new Point(-iconInk.X,(ActualHeight-iconInk.Height)/2-iconInk.Y));
        if(!labelInk.IsEmpty)drawing.DrawText(label,new Point(TextOffset,(ActualHeight-labelInk.Height)/2-labelInk.Y));
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    {base.OnDpiChanged(oldDpi,newDpi);InvalidateMeasure();InvalidateVisual();}
}
