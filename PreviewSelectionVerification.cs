#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPreviewSelection38(string root,Action<bool,string> check)
    {
        string first=Path.Combine(root,"selection-red.png"),second=Path.Combine(root,"selection-blue.png");
        void Write(string path,byte red,byte blue)
        {
            byte[] pixels=Enumerable.Range(0,4).SelectMany(_=>new byte[]{blue,0,red,255}).ToArray();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,pixels,8)));
            using var output=File.Create(path);encoder.Save(output);
        }
        Write(first,255,0);Write(second,0,255);
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.Preview=true;ApplyPreview();
        async Task Layout()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        async Task Select(string path)
        {
            Left.List.SelectedItem=Left.Items.Single(entry=>entry.Path==path);await Layout();await sidePreview.Completion;await Layout();
        }
        bool Blue()
        {
            if(PreviewBody.Content is not Image{Source:BitmapSource image})return false;
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixel=new byte[4];converted.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);return pixel[0]==255&&pixel[2]==0;
        }
        foreach(var mode in new[]{PreviewMode.PictureInPicture,PreviewMode.Windowed})
        {
            await Select(first);MovePreviewHost(mode);await Layout();var window=previewWindow;
            double width=window?.Width??PreviewPane.Width,height=window?.Height??PreviewPane.Height;
            await Select(second);
            check(previewMode==mode&&ReferenceEquals(window,previewWindow)&&sidePreview.Entry?.Path==second&&PreviewTitle.Text==Path.GetFileName(second)&&Blue(),"Actual selection replaces pixels/title in the same preview host: "+mode);
            check((window?.Width??PreviewPane.Width)==width&&(window?.Height??PreviewPane.Height)==height,"Selection preserves preview host dimensions: "+mode);
            long pinnedGeneration=sidePreview.Generation;
            PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Select(first);
            check(previewPinned&&PreviewPinButton.Tag as string=="on"&&System.Windows.Automation.AutomationProperties.GetItemStatus(PreviewPinButton)=="Pinned"&&sidePreview.Generation==pinnedGeneration&&Blue(),"Explicit pin visibly retains the original pixels/request while selection changes: "+mode);
            PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await sidePreview.Completion;await Layout();
            check(!previewPinned&&PreviewPinButton.Tag==null&&sidePreview.Entry?.Path==first&&!Blue()&&previewMode==mode,"Unpin immediately follows current selection in the same host: "+mode);
            await Select(second);
            Left.List.SelectedItem=Left.Items.Single(entry=>entry.Path==first);Left.List.SelectedItem=Left.Items.Single(entry=>entry.Path==second);
            await Layout();await PreviewSession.DrainAsync();await Layout();
            check(sidePreview.Entry?.Path==second&&Blue(),"Rapid selections finish with the latest image: "+mode);
            Left.List.UnselectAll();await Layout();await PreviewSession.DrainAsync();
            check(sidePreview.Entry==null&&PreviewBody.Content==null&&PreviewPane.Visibility==Visibility.Collapsed&&(window==null||!window.IsVisible),"Empty selection clears stale content and hides its host: "+mode);
            await Select(second);
            check(previewMode==mode&&ReferenceEquals(window,previewWindow)&&Blue()&&(window==null||window.IsVisible),"A new selection returns in the preserved host mode: "+mode);
            if(window!=null){window.Close();await Layout();check(previewMode==PreviewMode.Sidebar&&sidePreview.Entry?.Path==second&&Blue(),"Floating close restores the current selected preview");}
            else MovePreviewHost(PreviewMode.Sidebar);
        }
        check(PreviewWindowButton.FontSize<=PreviewCloseButton.FontSize&&((FrameworkElement)PreviewPipButton.Content).Width<=12&&PreviewPipButton.MinWidth>=32&&PreviewWindowButton.MinWidth>=32,"Small host glyphs match close proportions without shrinking accessible hit targets");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();MovePreviewHost(PreviewMode.PictureInPicture);await Layout();
            await S1Verification.Snapshot(this,root,$"selection-host-{theme}-{scale*100:0}");MovePreviewHost(PreviewMode.Sidebar);
        }
        PreviewPinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settings.TextScale=1;ApplyAppearance();Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();
        check(!previewPinned&&PreviewPinButton.Tag==null,"Explicit preview close clears the session pin");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Selection-following previews cause no host desktop activation");
    }
}
#endif
