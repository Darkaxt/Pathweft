using System.Windows.Controls;

namespace FileExplorer.Preview;

// Media and model playback share this distinction: engine position feedback
// moves the thumb, while a user value change must seek immediately. A track
// click does not capture the mouse, so waiting until mouse-up loses its value
// when a playing engine reports another position in between.
internal sealed class PreviewTimeline : Slider
{
    internal PreviewTimeline()=>SetResourceReference(StyleProperty,typeof(Slider));
    bool reporting;
    internal event Action<double> SeekRequested;
    internal void ReportPosition(double value)
    {
        if(IsMouseCaptureWithin)return;
        reporting=true;
        try{SetCurrentValue(ValueProperty,Math.Clamp(value,Minimum,Maximum));}
        finally{reporting=false;}
    }
    protected override void OnValueChanged(double oldValue,double newValue)
    {
        base.OnValueChanged(oldValue,newValue);
        if(!reporting)SeekRequested?.Invoke(newValue);
    }
}
