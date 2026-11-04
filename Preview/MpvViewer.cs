using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileExplorer.Preview;

internal sealed class MpvViewer(MainWindow owner)
{
    MpvHost host;
    MpvPlayer player;
    ContextObject context;
    Button play,mute;
    PreviewTimeline position;
    TextBlock time;
    bool paused=true,muted;
    double seconds,duration;
    volatile bool cleaned;
    internal MpvPlayer Player=>player;
#if DEBUG
    internal MpvHost HostForVerification=>host;
#endif
    internal Task Ready {get;private set;}=Task.CompletedTask;
    internal async Task View(string path,ContextObject current)
    {
        context=current;paused=!context.Autoplay;
#if DEBUG
        muted=HiddenTrialPresentation.Enabled;
#endif
        var created=new TaskCompletionSource<MpvPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);
        host=new MpvHost {MinHeight=80,Start=window=>
        {
            try{player=new MpvPlayer(window,path,context.Autoplay,muted,Changed);created.TrySetResult(player);return player;}
            catch(Exception error){created.TrySetException(error);throw;}
        }};
        play=PreviewControls.Icon(owner,paused?"Play":"Pause",paused?"\uE768":"\uE769");play.IsEnabled=false;
        mute=PreviewControls.Icon(owner,muted?"Unmute":"Mute",muted?"\uE74F":"\uE767");
        play.Click+=(_,_)=>Toggle();mute.Click+=(_,_)=>{muted=!muted;player?.Command("set","mute",muted?"yes":"no");PreviewControls.SetIcon(mute,muted?"Unmute":"Mute",muted?"\uE74F":"\uE767");};
        position=new PreviewTimeline {Minimum=0,Maximum=0,MinWidth=50,Margin=new(8,0,8,0),VerticalAlignment=VerticalAlignment.Center,IsEnabled=false};
        AutomationProperties.SetName(position,"Playback position");
        position.SeekRequested+=Seek;
        time=new TextBlock {Text="0:00 / 0:00",VerticalAlignment=VerticalAlignment.Center,Margin=new(6)};
        var controls=new Grid {Margin=new(6)};
        foreach(var width in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})controls.ColumnDefinitions.Add(new(){Width=width});
        int column=0;foreach(var control in new UIElement[]{play,mute,position,time}){Grid.SetColumn(control,column++);controls.Children.Add(control);}
        var root=new DockPanel();DockPanel.SetDock(controls,Dock.Bottom);root.Children.Add(controls);root.Children.Add(host);
        context.ViewerContent=root;
        using var cancelled=context.Token.Register(()=>created.TrySetCanceled(context.Token));
        Ready=Loaded();await Ready;
        await owner.AddReelPreview(path,root,()=>context.IsCurrent);
        async Task Loaded(){var native=await created.Task;await native.Ready;context.Token.ThrowIfCancellationRequested();play.IsEnabled=position.IsEnabled=true;}
    }
    void Changed(string name,object value)
    {
        if(cleaned)return;
        _=owner.Dispatcher.BeginInvoke(()=>
        {
            if(cleaned||!context.IsCurrent)return;
            switch(name)
            {
                case "time-pos":seconds=(double)value;position.ReportPosition(seconds);break;
                case "duration":duration=(double)value;position.Maximum=Math.Max(0,duration);break;
                case "pause":paused=(bool)value;UpdatePlay();break;
                case "eof-reached" when (bool)value:player.Command("set","pause","yes");player.Command("seek","0","absolute+exact");break;
                case "error":context.ViewerContent=BuiltinViewer.Message("Media unavailable\n"+value);Cleanup();break;
            }
            time.Text=$"{TimeSpan.FromSeconds(Math.Max(0,seconds)):h\\:mm\\:ss} / {TimeSpan.FromSeconds(Math.Max(0,duration)):h\\:mm\\:ss}";
        });
    }
    void UpdatePlay()=>PreviewControls.SetIcon(play,paused?"Play":"Pause",paused?"\uE768":"\uE769");
    void Toggle(){if(player==null)return;paused=!paused;player.Command("set","pause",paused?"yes":"no");UpdatePlay();}
    void Seek(double value)=>player?.Command("seek",value.ToString("R",CultureInfo.InvariantCulture),"absolute+exact");
    internal bool HandleKey(Key key)
    {
        if(player==null||!Ready.IsCompletedSuccessfully||cleaned)return false;
        switch(key){case Key.Enter:Toggle();return true;case Key.Left:Seek(Math.Max(0,seconds-5));return true;case Key.Right:Seek(Math.Min(duration,seconds+5));return true;default:return false;}
    }
    internal void Cleanup()
    {
        if(cleaned)return;cleaned=true;
        // Dispose initiates host teardown; its HWND stays alive until libmpv's
        // real event/render threads have finished. PreviewSession owns the wait.
        host?.Dispose();if(host!=null)context.Track(host.Closed);
    }
}
