using System.Windows;
using System.Windows.Media;

namespace FileExplorer;

// The owned auxiliary surfaces use one typography/spacing contract. Content
// scaling is explicit so native preview/editor renderers retain their own DPI.
internal static class AuxiliaryWindows
{
    internal static void Prepare(Window window, FrameworkElement body, bool scaleContent=true)
    {
        window.SetResourceReference(FrameworkElement.StyleProperty,"AuxiliaryWindow");
        if(!scaleContent)return;
        double scale=Math.Max(1,Settings.TextScale);
        body.LayoutTransform=scale>1?new ScaleTransform(scale,scale):null;
        var area=SystemParameters.WorkArea;
        if(!double.IsNaN(window.Width))window.Width=Math.Min(area.Width,window.Width*scale);
        if(!double.IsNaN(window.Height))window.Height=Math.Min(area.Height,window.Height*scale);
        window.MinWidth=Math.Min(area.Width,window.MinWidth*scale);
        window.MinHeight=Math.Min(area.Height,window.MinHeight*scale);
        window.MaxWidth=Math.Min(window.MaxWidth,area.Width);window.MaxHeight=Math.Min(window.MaxHeight,area.Height);
    }
}
