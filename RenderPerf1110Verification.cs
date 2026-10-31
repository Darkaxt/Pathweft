#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyRenderPerf1110(string root,Action<bool,string> check)
    {
        string model=Path.GetFullPath(Environment.GetEnvironmentVariable("PATHWEFT_VERIFY_MODEL")??throw new ArgumentException("A real model is required."));
        Settings.DisabledPreviewModules.Remove("f3d");SetSinglePane(true);Settings.Preview=true;
        await S1Verification.Navigate(Left,Path.GetDirectoryName(model));SetActive(Left);
        Left.List.SelectedItem=Left.Items.Single(e=>e.Path==model);ApplyPreview();PreviewFollow();await sidePreview.Completion;
        await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        var viewer=(F3dViewer)sidePreview.ViewerForVerification;await viewer.RenderCompletion;
        var viewport=(Border)((DockPanel)PreviewBody.Content).Children.OfType<Border>().Single();
        var center=new Point(viewport.ActualWidth*.5,viewport.ActualHeight*.5);
        async Task<List<double[]>> Trace(bool native,bool pan)
        {
            viewer.NativeInputForVerification=native;
            viewer.HandleKey(System.Windows.Input.Key.Home);await viewer.RenderCompletion;
            var poses=new List<double[]>();viewer.BeginDrag(center+new Vector(80,0),pan);
            for(int i=1;i<=80;i++)
            {
                double angle=2*Math.PI*(i<=40?i:80-i)/40;
                viewer.MoveDrag(center+new Vector(80*Math.Cos(angle),80*Math.Sin(angle)));await viewer.RenderCompletion;
                var f=viewer.LastFrame;poses.Add(f.Camera.Concat(f.Up).Concat(f.Focal).ToArray());
            }
            if(!pan)foreach(double y in new[]{5.0,-5.0,0.0})
            {
                viewer.MoveDrag(center+new Vector(0,y*viewport.ActualHeight));await viewer.RenderCompletion;
                var f=viewer.LastFrame;poses.Add(f.Camera.Concat(f.Up).Concat(f.Focal).ToArray());
            }
            viewer.EndDrag();await viewer.RenderCompletion;return poses;
        }
        var reference=await Trace(true,false);var actual=await Trace(false,false);
        var referencePan=await Trace(true,true);var actualPan=await Trace(false,true);
        static double Difference(List<double[]> a,List<double[]> b)=>a.Zip(b).Max(p=>p.First.Zip(p.Second).Max(v=>Math.Abs(v.First-v.Second)));
        double orbitError=Difference(reference,actual),panError=Difference(referencePan,actualPan);
        File.WriteAllText(Path.Combine(root,"native-parity.json"),JsonSerializer.Serialize(new{orbitError,panError,reference,actual,referencePan,actualPan},new JsonSerializerOptions{WriteIndented=true}));
        static bool SamePose(double[] a,double[] b)
        {
            if(Enumerable.Range(0,9).Where(i=>i<3||i>=6).Any(i=>Math.Abs(a[i]-b[i])>1e-7))return false;
            double x=a[0]-a[6],y=a[1]-a[7],z=a[2]-a[8];
            bool pole=Math.Sqrt(x*x+z*z)/Math.Sqrt(x*x+y*y+z*z)<1e-9;
            // At F3D's 1e-10-degree pole clamp, floating-point position noise
            // makes the projected up basis ill-conditioned. Keep position/
            // focal tolerance strict and allow <.006 degrees of up difference.
            return Enumerable.Range(3,3).All(i=>Math.Abs(a[i]-b[i])<(pole?1e-4:1e-7));
        }
        check(reference.Zip(actual).All(p=>SamePose(p.First,p.Second)),"Math-only circular, reverse and pole camera states match the actual pinned controller within stated numeric tolerance");
        check(panError<1e-7,"Math-only pan states match the actual pinned native controller");
        viewer.NativeInputForVerification=false;viewer.HandleKey(System.Windows.Input.Key.Home);await viewer.RenderCompletion;
        viewer.BeginDrag(center,false);await viewer.RenderCompletion;
        using var release=new ManualResetEventSlim();var prepared=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.BeforeFrameCompletionForVerification=_=>{prepared.TrySetResult();release.Wait();};
        viewer.MoveDrag(center+new Vector(1,0));var first=viewer.RenderCompletion;await prepared.Task;
        viewer.BeforeFrameCompletionForVerification=null;
        for(int i=1;i<=24;i++){double angle=2*Math.PI*i/24;viewer.MoveDrag(center+new Vector(80*Math.Cos(angle),80*Math.Sin(angle)));}
        var burst=viewer.RenderCompletion;release.Set();await first;await burst;
        var frame=viewer.LastFrame;
        File.WriteAllText(Path.Combine(root,"burst-metrics.json"),JsonSerializer.Serialize(new{frame.InputSamples,frame.InputRenderCalls,frame.InputMilliseconds,frame.RenderMilliseconds,frame.TransferMilliseconds,frame.Camera,frame.Up,frame.Focal,width=frame.Bitmap.PixelWidth,height=frame.Bitmap.PixelHeight,note="Input work is measured before capture; includes native event-triggered draws on the old path. Not physical display FPS."},new JsonSerializerOptions{WriteIndented=true}));
        check(frame.InputSamples==24,"A real queued burst processes all24 ordered camera samples");
        check(frame.InputRenderCalls==0,"Camera input causes no implicit draws before the one batch capture");
        viewer.EndDrag();await viewer.RenderCompletion;
        sidePreview.Clear();await PreviewSession.DrainAsync();check(!viewer.WorkerAlive,"Burst cleanup joins the owned native worker");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Burst trial never activates a desktop window");
    }
}
#endif
