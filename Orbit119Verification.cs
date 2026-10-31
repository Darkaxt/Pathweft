#if DEBUG
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyOrbit119(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        string model=Path.GetFullPath(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_MODEL")??throw new ArgumentException("A real model is required."));
        string Hash(){using var input=File.OpenRead(model);return Convert.ToHexString(SHA256.HashData(input));}
        string before=Hash();Settings.DisabledPreviewModules.Remove("f3d");SetSinglePane(true);Settings.Preview=true;
        await S1Verification.Navigate(Left,Path.GetDirectoryName(model));SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==model);ApplyPreview();PreviewFollow();await sidePreview.Completion;await Settle();
        var viewer=(F3dViewer)sidePreview.ViewerForVerification;await viewer.RenderCompletion;
        var viewport=(Border)((DockPanel)PreviewBody.Content).Children.OfType<Border>().Single();
        var frames=new List<F3dViewer.Frame>();viewer.FramePresented+=frames.Add;
        static double Distance(double[] a,double[] b)=>Math.Sqrt(a.Zip(b).Sum(p=>Math.Pow(p.First-p.Second,2)));
        var start=viewer.LastFrame;double distance=Distance(start.Camera,start.Focal);
        async Task Circle(int direction)
        {
            viewer.BeginDrag(new Point(.72*viewport.ActualWidth,.5*viewport.ActualHeight),false);
            for(int i=1;i<=40;i++)
            {
                double angle=direction*2*Math.PI*i/40;
                viewer.MoveDrag(new Point((.5+.22*Math.Cos(angle))*viewport.ActualWidth,(.5+.22*Math.Sin(angle))*viewport.ActualHeight));await viewer.RenderCompletion;
            }
            viewer.EndDrag();await viewer.RenderCompletion;
        }
        await Circle(1);
        var end=viewer.LastFrame;
        File.WriteAllText(Path.Combine(root,"orbit-camera.json"),JsonSerializer.Serialize(new{start=new{start.Camera,start.Up,start.Focal},end=new{end.Camera,end.Up,end.Focal},frames=frames.Select(f=>new{f.Camera,f.Up,f.Focal,f.Sequence})},new JsonSerializerOptions{WriteIndented=true}));
        check(frames.All(f=>f.Up[1]>0),"Circular mouse orbit keeps the model-up hemisphere instead of accumulating camera roll");
        check(Distance(start.Camera,end.Camera)/distance<.02&&Distance(start.Up,end.Up)<.02,"A closed circle away from the poles returns to its starting orientation");
        check(frames.All(f=>Distance(f.Focal,start.Focal)<1e-8&&Math.Abs(Distance(f.Camera,f.Focal)/distance-1)<1e-8),"Orbit preserves its focal point and distance");
        check(frames.Any(f=>f.Camera[1]<f.Focal[1])&&frames.Any(f=>f.Camera[2]<f.Focal[2]),"Circular orbit reveals the underside and side beyond the initial hemisphere");
        File.WriteAllBytes(Path.Combine(root,"orbit-model.png"),end.Png);
        viewer.HandleKey(Key.Home);await viewer.RenderCompletion;await Circle(-1);
        check(viewer.LastFrame.Up[1]>0&&Distance(start.Camera,viewer.LastFrame.Camera)/distance<.02,"Reverse circular orbit returns upright to its original orientation");
        var center=new Point(viewport.ActualWidth/2,viewport.ActualHeight/2);
        viewer.BeginDrag(center,false);viewer.MoveDrag(center+new Vector(0,5*viewport.ActualHeight));await viewer.RenderCompletion;
        check(viewer.LastFrame.Up.All(double.IsFinite)&&viewer.LastFrame.Up[1]>=-1e-8,"A vertical drag is bounded at the first native pole without flipping");
        viewer.MoveDrag(center-new Vector(0,5*viewport.ActualHeight));await viewer.RenderCompletion;viewer.EndDrag();await viewer.RenderCompletion;
        check(viewer.LastFrame.Up.All(double.IsFinite)&&viewer.LastFrame.Up[1]>=-1e-8,"Dragging across both native poles keeps a valid upright camera");
        viewer.HandleKey(Key.Home);await viewer.RenderCompletion;
        viewer.ApplyDrag(new Vector(30,20),true);await viewer.RenderCompletion;var panned=viewer.LastFrame;
        check(Distance(panned.Focal,start.Focal)>0,"Existing right/middle/Shift pan mapping reaches native pan");
        viewer.HandleKey(Key.Add);await viewer.RenderCompletion;
        check(Distance(viewer.LastFrame.Camera,viewer.LastFrame.Focal)<Distance(panned.Camera,panned.Focal),"Zoom still moves closer after native pan");
        viewer.HandleKey(Key.Home);await viewer.RenderCompletion;
        check(Distance(start.Camera,viewer.LastFrame.Camera)/distance<1e-8&&Distance(start.Focal,viewer.LastFrame.Focal)<1e-8,"Reset restores the original camera after orbit, poles, pan and zoom");
        Width+=100;await Settle();await viewer.RenderCompletion;var dpi=VisualTreeHelper.GetDpi(viewport);
        check(viewer.LastFrame.Bitmap.PixelWidth==(int)Math.Ceiling(viewport.ActualWidth*dpi.DpiScaleX)&&viewer.LastFrame.Bitmap.PixelHeight==(int)Math.Ceiling(viewport.ActualHeight*dpi.DpiScaleY),"Native mouse coordinates continue to match the actual resized viewport/DPI");
        var resized=viewer.LastFrame;
        viewer.BeginDrag(center,false);viewer.MoveDrag(center+new Vector(20,10));await viewer.RenderCompletion;
        check(!resized.Camera.SequenceEqual(viewer.LastFrame.Camera)&&viewer.LastFrame.Up[1]>0,"Orbit remains usable and upright after viewport resizing");
        check(Hash()==before,"Circular orbit preserves the source model");
        sidePreview.Clear();await PreviewSession.DrainAsync();check(!viewer.WorkerAlive,"Closing during a native drag joins its worker and releases the gesture with its engine");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Orbit checks never activate a desktop window");
    }
}
#endif
