#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyFigCore16(string root,Action<bool,string> check)
    {
        string inputs=Path.Combine(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS"),"../../2026-11-06-s16/fig-inputs");
        var resources=new List<object>();
        foreach(string name in new[]{"frames.figcraft","frames.fig","empty.figcraft"})
        {
            string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);
            byte[] original=SHA256.HashData(File.ReadAllBytes(path));File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=new OfficeDocument();(string Engine,int Pid)[] workers;string snapshot;
            try
            {
                await document.OpenCore(path,"figcraft",CancellationToken.None);
                int count=name=="empty.figcraft"?1:3;
                check(document.Info.Engine=="FigCraft"&&document.Info.Kind=="frames"&&document.Info.Parts.Length==count&&!document.FallbackForVerification,"Real native/Figma importer exposes page overview and frames without editor: "+name);
                byte[] first=null,frameOne=null;
                for(int part=0;part<count;part++)
                {
                    var frame=(await document.RenderPage(part,0,CancellationToken.None)).Raster;
                    check(frame.Width>0&&frame.Height>0&&Math.Max(frame.Width,frame.Height)<=1400&&frame.Pixels.Length==frame.Width*frame.Height*4,"Bounded design raster: "+name+" / "+part);
                    if(part==0)first=frame.Pixels;if(part==1)frameOne=frame.Pixels;if(part==2)check(!frameOne.SequenceEqual(frame.Pixels),"Frame selection changes actual design pixels: "+name);
                    byte[] bgra=(byte[])frame.Pixels.Clone();for(int i=0;i<bgra.Length;i+=4)(bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(frame.Width,frame.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32,null,bgra,frame.Width*4)));
                    using(var output=File.Create(Path.Combine(root,name+"-"+part+".png")))encoder.Save(output);
                }
                var records=new List<OfficeWorker.TextRecord>();OfficeWorker.TextCursor cursor=null;int batches=0;string status="";
                do {var batch=await document.ExtractText(cursor,CancellationToken.None,1024);status=batch.Status;records.AddRange(batch.Records);cursor=batch.Next;batches++;}while(cursor!=null);
                File.WriteAllText(Path.Combine(root,name+"-text.json"),System.Text.Json.JsonSerializer.Serialize(records,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                if(name=="empty.figcraft")check(records.Count==0&&status=="empty","Empty design extraction explicitly reports no editable text");
                else
                {
                    string joined=string.Concat(records.Select(r=>r.Text));
                    check(batches>1&&joined==string.Concat(Enumerable.Repeat("Located é 子 😀 \"quoted\"\n",100))+"Second frameHidden note","Complete imported Unicode design text and page/tree order survive bounded continuations: "+name);
                    check(records.All(r=>r.Location.GetProperty("page").GetInt32()==0&&r.Location.TryGetProperty("nodePath",out _)&&r.Location.TryGetProperty("transform",out _))&&string.Concat(records.Where(r=>!r.Location.GetProperty("visible").GetBoolean()).Select(r=>r.Text))=="Hidden note","Native node locations/transforms and hidden-type visibility: "+name);
                }
                if(name=="frames.fig")check(document.Info.Notes.Any(n=>n.Contains("Best-effort")),"Figma import limitations are explicit independently of native rendering");
                using(var cancelled=new CancellationTokenSource())
                {
                    bool caught=false;document.RequestSentForVerification=request=>{if(request.Action=="extract")cancelled.Cancel();};
                    try{await document.ExtractText(null,cancelled.Token);}catch(OperationCanceledException){caught=true;}finally{document.RequestSentForVerification=null;}
                    check(caught&&(await document.RenderPage(0,0,CancellationToken.None)).Raster.Pixels.SequenceEqual(first),"Sent extraction cancellation preserves subsequent design rendering: "+name);
                }
                workers=document.WorkersForVerification.ToArray();snapshot=document.SourceForVerification;
                using(var process=System.Diagnostics.Process.GetProcessById(document.WorkerForVerification))resources.Add(new{file=name,pid=process.Id,peakWorkingSet=process.PeakWorkingSet64,privateBytes=process.PrivateMemorySize64});
                check(File.ReadAllText(snapshot+":Zone.Identifier").Contains("ZoneId=3"),"Design snapshot preserves source security marker: "+name);
            }
            finally{await document.DisposeAsync();}
            check(workers.All(w=>Exited(w.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot))&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Design worker exits and source remains unchanged: "+name);
        }
        File.WriteAllText(Path.Combine(root,"fig-resources.json"),System.Text.Json.JsonSerializer.Serialize(resources,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        foreach(string name in new[]{"invalid.figcraft","invalid.fig"})
        {
            var document=new OfficeDocument();bool rejected=false;
            try{await document.OpenCore(Path.Combine(inputs,name),"figcraft",CancellationToken.None);}catch(IOException error){rejected=error.Message.Length>0;}finally{await document.DisposeAsync();}
            check(rejected&&!document.FallbackForVerification&&document.WorkersForVerification.All(w=>Exited(w.Pid)),"Malformed native/Figma design rejects and exits: "+name);
        }
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();Settings.Preview=true;
        async Task Select(string name)
        {
            Left.List.SelectedItem=Left.Items.Single(e=>e.Path==Path.Combine(root,name));ApplyPreview();await sidePreview.Completion;
            await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="figcraft"&&sidePreview.RendererId=="FigCraft","Selected design follows actual preview host: "+previewMode+" / "+name);
            var frames=PreviewChildren<ComboBox>(PreviewBody).Single(c=>AutomationProperties.GetName(c)=="Design frame");check(frames.Items.Count==3,"Actual host exposes page overview and both frames");
            frames.SelectedIndex=2;await PreviewSession.DrainAsync();
        }
        try
        {
            foreach(var mode in new[]{PreviewMode.Sidebar,PreviewMode.PictureInPicture,PreviewMode.Windowed})
            {
                MovePreviewHost(mode);await Select("frames.figcraft");await Select("frames.fig");await S1Verification.Snapshot(previewWindow??this,root,"fig-"+mode);
            }
            previewWindow.Close();await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(previewMode==PreviewMode.Sidebar,"Floating close restores design preview to sidebar");
            string thumbnail=Path.Combine(root,"thumbnail.fig");using(var zip=ZipFile.Open(thumbnail,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"frames.figcraft-1.png"),"thumbnail.png");
            sidePreview.Show(new Entry{Path=thumbnail,Name="thumbnail.fig"});await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="design","Existing embedded Figma thumbnail retains its default priority");
            string archive=Path.Combine(root,"designs.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"frames.figcraft"),"nested/frames.figcraft");
            sidePreview.Show(new Entry{Path=archive+"\\nested\\frames.figcraft",Name="frames.figcraft"});await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="figcraft","Archive-contained native design uses same source core");
            Settings.DisabledPreviewModules.Add("figcraft");sidePreview.Refresh();await sidePreview.Completion;check(sidePreview.ModuleId!="figcraft","Fig module respects disable setting");
        }
        finally{Settings.DisabledPreviewModules.Remove("figcraft");Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();}
        check(HiddenTrialPresentation.ForegroundViolations==0,"Design workflow does not activate host desktop");
    }
}
#endif
