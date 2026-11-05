using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Preview;

namespace FileExplorer;

public partial class MainWindow
{
    enum PreviewMode { Sidebar, PictureInPicture, Windowed }
    PreviewMode previewMode;
    Window previewWindow;
    bool changingPreviewHost,previewPinned;

    void PreviewPin_Click(object sender,RoutedEventArgs args)
    {
        if(sidePreview?.Entry==null)return;
        previewPinned=!previewPinned;UpdatePreviewModeButtons();
        if(!previewPinned)PreviewFollow();
    }

    void UpdateSidePreviewHeading(string text)
    {
        PreviewTitle.Text=text;PreviewHeader.DataContext=text.Length>0?sidePreview?.Entry:null;
        var icon=PreviewControls.RendererIcon(sidePreview?.RendererId);
        if(icon!=null)PreviewAppIcon.Source=icon;
        else PreviewAppIcon.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding(nameof(Entry.SmallIcon)){Source=PreviewHeader.DataContext});
        PreviewAppIcon.ToolTip=sidePreview?.RendererDescription;
        AutomationProperties.SetName(PreviewAppIcon,sidePreview?.RendererDescription??"Preview icon");
        if(previewWindow!=null){previewWindow.Title=text;previewWindow.Icon=icon??sidePreview?.Entry?.SmallIcon;}
        UpdatePreviewModeButtons();
    }

    void PreviewWindow_Click(object sender,RoutedEventArgs args)
        =>MovePreviewHost(previewMode==PreviewMode.Windowed?PreviewMode.Sidebar:PreviewMode.Windowed);
    void PreviewPip_Click(object sender,RoutedEventArgs args)
        =>MovePreviewHost(previewMode==PreviewMode.PictureInPicture?PreviewMode.Sidebar:PreviewMode.PictureInPicture);

    void MovePreviewHost(PreviewMode destination)
    {
        if(changingPreviewHost||previewMode==destination)return;
        if(destination!=PreviewMode.Sidebar&&sidePreview?.Entry==null)return;
        changingPreviewHost=true;
        try
        {
            // Move the whole existing container. The session, content, HWND,
            // native document, playback and archive lease keep their identities.
            if(previewWindow is {} previous)
            {
                previewWindow=null;previous.Content=null;previous.Close();
            }
            else PaneArea.Children.Remove(PreviewPane);
            previewMode=destination;
            PreviewPane.Width=PreviewPane.Height=double.NaN;
            PreviewPane.HorizontalAlignment=HorizontalAlignment.Stretch;
            PreviewPane.VerticalAlignment=VerticalAlignment.Stretch;
            PreviewPane.Margin=new Thickness(0);Grid.SetColumnSpan(PreviewPane,1);
            PreviewPane.LayoutTransform=null;Panel.SetZIndex(PreviewPane,0);
            if(destination==PreviewMode.Windowed)
            {
                var window=previewWindow=new Window
                {
                    Owner=this,Content=PreviewPane,Title=PreviewTitle.Text,Width=840,Height=680,MinWidth=360,MinHeight=260,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,ShowActivated=false,
                    Background=Hex("#161B24"),Foreground=Foreground,FontFamily=FontFamily,FontSize=FontSize
                };
                window.Icon=PreviewControls.RendererIcon(sidePreview.RendererId)??sidePreview.Entry.SmallIcon;
                AuxiliaryWindows.Prepare(window,PreviewPane,scaleContent:false);
#if DEBUG
                HiddenTrialPresentation.Prepare(window);
#endif
                window.SourceInitialized+=(_,_)=>ApplyAcrylic(window);
                window.Closed+=(_,_)=>
                {
                    if(previewWindow!=window)return;
                    // Native window close restores; the header's explicit ×
                    // remains the separate close-preview action.
                    previewWindow=null;window.Content=null;
                    if(!closingPreviews)MovePreviewHost(PreviewMode.Sidebar);
                };
                ApplyPreviewAppearance();window.Show();
            }
            else
            {
                Grid.SetColumn(PreviewPane,destination==PreviewMode.Sidebar?2:0);
                PaneArea.Children.Add(PreviewPane);
                if(destination==PreviewMode.PictureInPicture)
                {
                    Grid.SetColumnSpan(PreviewPane,3);Panel.SetZIndex(PreviewPane,10);
                    PreviewPane.HorizontalAlignment=HorizontalAlignment.Right;PreviewPane.VerticalAlignment=VerticalAlignment.Bottom;
                    PreviewPane.Margin=new Thickness(12);FitPictureInPicture();
                }
                else
                {
                    previewShown=sidePreview?.Entry?.Path;
                }
            }
            UpdatePreviewModeButtons();SetPreviewVisible(sidePreview?.Entry!=null);
        }
        finally{changingPreviewHost=false;}
    }

    void FitPictureInPicture()
    {
        if(previewMode!=PreviewMode.PictureInPicture)return;
        double availableWidth=Math.Max(0,PaneArea.ActualWidth-24),availableHeight=Math.Max(0,PaneArea.ActualHeight-24);
        PreviewPane.Width=Math.Min(availableWidth,Math.Min(400,Math.Max(280,availableWidth*.45)));
        PreviewPane.Height=Math.Min(availableHeight,Math.Min(320,Math.Max(220,availableHeight*.55)));
    }
    void UpdatePreviewModeButtons()
    {
        string pin=previewPinned?"Unpin preview and follow selection":"Pin this preview";
        PreviewPinButton.ToolTip=pin;AutomationProperties.SetName(PreviewPinButton,pin);
        AutomationProperties.SetItemStatus(PreviewPinButton,previewPinned?"Pinned":"Following selection");
        PreviewPinButton.Tag=previewPinned?"on":null;PreviewPinButton.IsEnabled=sidePreview?.Entry!=null;
        string floating=previewMode==PreviewMode.Windowed?"Return preview to sidebar":"Open preview in a floating window";
        string pip=previewMode==PreviewMode.PictureInPicture?"Return preview to sidebar":"Picture in picture inside Pathweft";
        PreviewWindowButton.ToolTip=floating;AutomationProperties.SetName(PreviewWindowButton,floating);
        PreviewPipButton.ToolTip=pip;AutomationProperties.SetName(PreviewPipButton,pip);
        PreviewWindowButton.Content=previewMode==PreviewMode.Windowed?"\uE73F":"\uE8A7";
        if(previewMode==PreviewMode.Sidebar)PreviewPane.SetResourceReference(Border.BackgroundProperty,"Palette.0DFFFFFF");
        else PreviewPane.Background=Hex("#161B24");
    }
}
