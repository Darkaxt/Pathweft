using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileExplorer;
public partial class PaneView
{
    int loadingListing=-1,loadingSearch=-1;
    long resolvingAddress;
    bool addressIsResolving;
#if DEBUG
    internal Action<string> BeforeListingForVerification;
#endif
    void ListingLoading(int generation,bool show)
    {loadingListing=show?generation:-1;UpdateAddressLoading();}
    void ListingLoaded(int generation)
    {if(loadingListing==generation){loadingListing=-1;UpdateAddressLoading();}}
    void SearchLoading(int generation)
    {loadingSearch=generation;UpdateAddressLoading();}
    void SearchLoaded(int generation)
    {if(loadingSearch==generation){loadingSearch=-1;UpdateAddressLoading();}}
    void UpdateAddressLoading()
    {
        if(AddressActivity==null)return;
        if(AddressActivity.OpacityMask is LinearGradientBrush previous)
            foreach(var stop in previous.GradientStops)stop.BeginAnimation(GradientStop.OffsetProperty,null);
        bool loading=loadingListing>=0||loadingSearch>=0||addressIsResolving;
        AddressActivity.Visibility=loading?Visibility.Visible:Visibility.Collapsed;
        AutomationProperties.SetItemStatus(AddressField,loading?"Loading":"Ready");
        // The original address surface remains the fill. A masked dark track
        // reveals a moving portion of that exact surface; no invented percent.
        AddressActivity.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:Appearance.Light?Brushes.Gainsboro:Brushes.Black;
        if(!loading){AddressActivity.OpacityMask=null;return;}
        if(!Motion.On||SystemParameters.HighContrast)
        {AddressActivity.OpacityMask=null;AddressActivity.Opacity=0.35;return;}
        AddressActivity.Opacity=1;
        var mask=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0)};
        foreach(var (color,offset) in new[]{(Colors.Black,0d),(Colors.Transparent,0.02),(Colors.Transparent,0.28),(Colors.Black,0.3)})
        {
            var stop=new GradientStop(color,offset-0.3);mask.GradientStops.Add(stop);
            stop.BeginAnimation(GradientStop.OffsetProperty,new DoubleAnimation(offset-0.3,offset+1,TimeSpan.FromSeconds(1.8)){RepeatBehavior=RepeatBehavior.Forever});
        }
        AddressActivity.OpacityMask=mask;
    }
}
