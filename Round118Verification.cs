#if DEBUG
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyRound118(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();
            var settings=new SettingsWindow(this);settings.Show();await Settle();
            try
            {
                var categories=(ListBox)settings.FindName("SettingsCategories");
                var cards=(StackPanel)settings.FindName("SettingsCards");var scroll=(ScrollViewer)settings.FindName("CategoryScroll");
                check(categories.Items.Count==6&&settings.Width>=740,"Settings preserves content width and exposes six vertical categories: "+theme+scale);
                foreach(ListBoxItem item in categories.Items)
                {
                    ((ISelectionItemProvider)new ListBoxItemAutomationPeer(item,new ListBoxAutomationPeer(categories)).GetPattern(PatternInterface.SelectionItem)).Select();await Settle();
                    check(cards.Children.OfType<Border>().Count(b=>b.Visibility==Visibility.Visible)==1&&((Border)item.Tag).IsVisible,"Accessible category selection shows exactly its own existing settings: "+item.Content+theme+scale);
                    check(scroll.ViewportWidth>300&&scroll.ActualWidth+categories.ActualWidth<=((Grid)settings.FindName("Body")).ActualWidth+1,"Category and content columns remain within the actual window: "+item.Content+theme+scale);
                }
                categories.SelectedIndex=0;await Settle();
                check(scroll.ScrollableHeight>0,"Tall Appearance content scrolls within its selected category: "+theme+scale);
                await S1Verification.Snapshot(settings,root,$"settings-tabs-{theme}-{scale*100:0}");
                categories.SelectedIndex=1;await Settle();
                var hidden=(CheckBox)settings.FindName("HiddenBox");bool original=Settings.ShowHidden;
                hidden.IsChecked=!original;hidden.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                check(Settings.ShowHidden==!original,"A setting in the selected Files category still reaches the real persistence/action path");
                hidden.IsChecked=original;hidden.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            }
            finally{settings.Close();}
            var help=new HelpWindow(this);help.Show();await Settle();
            try
            {
                var topics=(StackPanel)help.FindName("Topics");var scroller=(ScrollViewer)help.FindName("Scroller");var find=(TextBox)help.FindName("Find");
                Button[] Buttons()=>topics.Children.OfType<Button>().ToArray();
                check(Buttons()[0].FontWeight==FontWeights.Bold&&Buttons().Count(b=>b.FontWeight==FontWeights.Bold)==1,"Help initially identifies exactly one current section: "+theme+scale);
                var topic=Buttons()[3];((IInvokeProvider)new ButtonAutomationPeer(topic).GetPattern(PatternInterface.Invoke)).Invoke();await Settle();
                check(topic.FontWeight==FontWeights.Bold,"Clicking a Help category highlights the actual section reached: "+theme+scale);
                scroller.ScrollToEnd();await Settle();
                check(Buttons()[^1].FontWeight==FontWeights.Bold&&Buttons().Count(b=>b.FontWeight==FontWeights.Bold)==1,"Manual content scrolling updates the bold category without moving the reader: "+theme+scale);
                find.Text="The window";await Settle();check(Buttons().Length==1&&Buttons()[0].FontWeight==FontWeights.Bold,"Filtered Help sections retain their reading highlight");
                find.Text="no_such_pathweft_help_section";await Settle();check(Buttons().Length==0,"No Help matches leave no stale highlighted category");
                find.Text="";await Settle();await S1Verification.Snapshot(help,root,$"help-reading-{theme}-{scale*100:0}");
            }
            finally{help.Close();}
        }
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();
        check(F3dViewer.TopDownBgra(new byte[]{255,0,0,255,0,255,0,100,0,0,255,0,255,255,255,255},2,2).SequenceEqual(new byte[]{255,0,0,0,255,255,255,255,0,0,255,255,0,255,0,100}),"Raw native RGBA becomes top-down BGRA with exact colors and alpha");
        string model=Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_MODEL");
        if(string.IsNullOrEmpty(model))throw new ArgumentException("The real large-model fixture is required.");
        model=Path.GetFullPath(model);
        string Hash(){using var stream=File.OpenRead(model);return Convert.ToHexString(SHA256.HashData(stream));}
        string before=Hash();Settings.DisabledPreviewModules.Remove("f3d");SetSinglePane(true);Settings.Preview=true;
        await S1Verification.Navigate(Left,Path.GetDirectoryName(model));SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==model);ApplyPreview();PreviewFollow();await sidePreview.Completion;await Settle();
        var viewer=(F3dViewer)sidePreview.ViewerForVerification;await viewer.RenderCompletion;
        var start=viewer.LastFrame;var frames=new List<F3dViewer.Frame>();viewer.FramePresented+=frames.Add;
        for(int i=0;i<8;i++)
        {
            using var release=new ManualResetEventSlim();
            var prepared=new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            viewer.BeforeFrameCompletionForVerification=frame=>{prepared.TrySetResult(frame.Sequence);release.Wait();};
            viewer.ApplyDrag(new Vector(12,3),false);var first=viewer.RenderCompletion;
            await Task.WhenAny(prepared.Task,first);
            if(!prepared.Task.IsCompleted){await first;throw new InvalidOperationException("The gesture did not produce a native frame.");}
            long sequence=await prepared.Task;
            viewer.BeforeFrameCompletionForVerification=null;
            viewer.ApplyDrag(new Vector(10,-2),false);var second=viewer.RenderCompletion;
            release.Set();await first;await second;
            check(frames.Any(f=>f.Sequence==sequence),"A completed drag frame is presented even while newer mouse input is pending: "+i);
        }
        check(frames.Count>=8&&!start.Camera.SequenceEqual(viewer.LastFrame.Camera)&&frames.Zip(frames.Skip(1)).All(pair=>pair.First.Sequence<pair.Second.Sequence),"Continuous drag publishes ordered intermediate native frames and changes the camera");
        var viewport=(Border)((DockPanel)PreviewBody.Content).Children.OfType<Border>().Single();
        var dpi=VisualTreeHelper.GetDpi(viewport);
        check(viewer.LastFrame.Bitmap.PixelWidth==(int)Math.Ceiling(viewport.ActualWidth*dpi.DpiScaleX)&&viewer.LastFrame.Bitmap.PixelHeight==(int)Math.Ceiling(viewport.ActualHeight*dpi.DpiScaleY),"Native frames match the actual preview viewport and DPI, rather than a fixed 4:3 image");
        Width+=100;await Settle();await viewer.RenderCompletion;
        viewer.HandleKey(System.Windows.Input.Key.Home);await viewer.RenderCompletion;
        await S1Verification.Snapshot(this,root,"interactive-large-model");
        File.WriteAllBytes(Path.Combine(root,"interactive-model.png"),viewer.LastFrame.Png);
        File.WriteAllText(Path.Combine(root,"render-metrics.json"),JsonSerializer.Serialize(new{frames=frames.Select(f=>new{f.Sequence,f.RenderMilliseconds,f.TransferMilliseconds,width=f.Bitmap.PixelWidth,height=f.Bitmap.PixelHeight}),viewer.PresentedFrames,note="Native render/readback and raw transfer timings; hidden WPF image-source presentation is not physical display FPS."},new JsonSerializerOptions{WriteIndented=true}));
        check(Hash()==before,"Interactive preview preserves the actual source model");
        sidePreview.Clear();await PreviewSession.DrainAsync();check(!viewer.WorkerAlive,"Closing the interactive preview joins the native renderer and releases its input/layout subscriptions");
        check(HiddenTrialPresentation.ForegroundViolations==0,"This complete refinement trial never activates a desktop window");
    }
}
#endif
