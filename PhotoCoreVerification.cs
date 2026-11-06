#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPhotoCore16(string root,Action<bool,string> check)
    {
        string inputs=Path.Combine(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS"),"../../2026-11-06-s16/photo-inputs");
        string[] names={"layers.pcraft","layers.psd","layers.psb","text.pcraft","oracle-2layers.psd","oracle-2layers.psb","oracle-text.psd","oracle-16bit5x5.psb"};
        var resources=new List<object>();
        foreach(string name in names)
        {
            string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);
            byte[] original=SHA256.HashData(File.ReadAllBytes(path));File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=new OfficeDocument();(string Engine,int Pid)[] workers;string snapshot;
            try
            {
                await document.OpenCore(path,"photocraft",CancellationToken.None);
                check(document.Info.Engine=="PhotoCraft"&&!document.FallbackForVerification,"Actual native photo importer, without editor or fallback: "+name);
                var frame=(await document.RenderPage(0,0,CancellationToken.None)).Raster;
                check(frame.Width>0&&frame.Height>0&&frame.Pixels.Length==frame.Width*frame.Height*4,"Actual source-built photo compositor returns bounded pixels: "+name);
                if(name.StartsWith("layers.",StringComparison.Ordinal))
                {
                    byte[] pixel=frame.Pixels.Take(4).ToArray();
                    check(frame.Width==128&&frame.Height==96&&pixel[0] is >=126 and <=129&&pixel[1]==0&&pixel[2] is >=126 and <=129&&pixel[3]==255,"Native/PSD/PSB composition preserves half-opacity red over blue and hidden green: "+name+" / "+string.Join(',',pixel));
                }
                var encoder=new PngBitmapEncoder();
                // Save the actual RGBA result through WPF's premultiplied BGRA path.
                byte[] bgra=(byte[])frame.Pixels.Clone();for(int i=0;i<bgra.Length;i+=4)(bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
                encoder.Frames.Clear();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(frame.Width,frame.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32,null,bgra,frame.Width*4)));
                using(var output=File.Create(Path.Combine(root,name+".png")))encoder.Save(output);
                var records=new List<OfficeWorker.TextRecord>();OfficeWorker.TextCursor cursor=null;string status="";int batches=0;
                do {var batch=await document.ExtractText(cursor,CancellationToken.None,1024);status=batch.Status;check(batch.Coverage.Length>0,"Photo text coverage is explicit: "+name);records.AddRange(batch.Records);cursor=batch.Next;batches++;}while(cursor!=null);
                if(name=="text.pcraft")
                {
                    string joined=string.Concat(records.OrderBy(r=>r.Offset).Select(r=>r.Text));
                    check(batches>1&&joined==string.Concat(Enumerable.Repeat("Located é 子 😀 \"quoted\" ",200))&&records.All(r=>r.Location.GetProperty("layerPath").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{3,0})&&!r.Location.GetProperty("visible").GetBoolean()),"Native text layers preserve complete Unicode and stable nested/hidden locations across bounded batches");
                }
                else if(name=="oracle-text.psd")check(records.Count>0&&records.All(r=>r.Location.TryGetProperty("layerId",out _)),"Independent Photoshop-authored type layers provide real located text");
                else if(name.StartsWith("layers.",StringComparison.Ordinal))check(status=="image-only"&&records.Count==0,"Raster/fill-only documents explicitly report image-only without OCR: "+name);
                using(var cancelled=new CancellationTokenSource())
                {
                    bool caught=false;document.RequestSentForVerification=request=>{if(request.Action=="extract")cancelled.Cancel();};
                    try{await document.ExtractText(null,cancelled.Token);}catch(OperationCanceledException){caught=true;}finally{document.RequestSentForVerification=null;}
                    check(caught&&(await document.RenderPage(0,0,CancellationToken.None)).Raster.Pixels.SequenceEqual(frame.Pixels),"Cancelled sent extraction drains its response and preserves photo rendering: "+name);
                }
                workers=document.WorkersForVerification.ToArray();snapshot=document.SourceForVerification;
                using(var process=System.Diagnostics.Process.GetProcessById(document.WorkerForVerification))resources.Add(new{file=name,pid=process.Id,peakWorkingSet=process.PeakWorkingSet64,privateBytes=process.PrivateMemorySize64});
                check(File.ReadAllText(snapshot+":Zone.Identifier").Contains("ZoneId=3"),"Photo snapshot carries its source security marker: "+name);
            }
            finally{await document.DisposeAsync();}
            check(workers.All(worker=>Exited(worker.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot))&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Photo worker exits, releases its scratch and preserves source bytes: "+name);
        }
        File.WriteAllText(Path.Combine(root,"photo-resources.json"),System.Text.Json.JsonSerializer.Serialize(resources,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        foreach(string extension in new[]{"pcraft","psd","psb"})
        {
            string path=Path.Combine(root,"malformed."+extension);File.WriteAllText(path,"not a native image");
            var document=new OfficeDocument();bool rejected=false;
            try{await document.OpenCore(path,"photocraft",CancellationToken.None);}catch(IOException error){rejected=error.Message.Length>0;}finally{await document.DisposeAsync();}
            check(rejected&&!document.FallbackForVerification&&document.WorkersForVerification.All(w=>Exited(w.Pid)),"Malformed photo input returns a real error and exits without LibreOffice: "+extension);
        }
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();Settings.Preview=true;
        async Task Select(string name)
        {
            Left.List.SelectedItem=Left.Items.Single(entry=>entry.Path==Path.Combine(root,name));ApplyPreview();await sidePreview.Completion;
            await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="photocraft"&&sidePreview.RendererId=="PhotoCraft"&&PreviewChildren<Image>(PreviewBody).Any(image=>image.Source is BitmapSource),"Actual selected photo renders in host: "+previewMode+" / "+name);
            check(!PreviewChildren<Button>(PreviewBody).Any(button=>button.IsVisible&&button.ToolTip as string=="Next page"),"Single-composite photo does not display document page controls");
        }
        try
        {
            await Select("oracle-2layers.psd");
            foreach(var mode in new[]{PreviewMode.Sidebar,PreviewMode.PictureInPicture,PreviewMode.Windowed})
            {
                MovePreviewHost(mode);await Select("layers.pcraft");await Select("oracle-text.psd");
                await S1Verification.Snapshot(previewWindow??this,root,"photo-"+mode);
            }
            previewWindow.Close();await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(previewMode==PreviewMode.Sidebar&&sidePreview.RendererId=="PhotoCraft","Floating close restores the same PhotoCraft preview to the sidebar");
            string archive=Path.Combine(root,"photos.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"layers.psb"),"nested/layers.psb");
            sidePreview.Show(new Entry{Path=archive+"\\nested\\layers.psb",Name="layers.psb"});await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="photocraft","Archive-contained PSB uses the same source-built composition workflow");
            Settings.DisabledPreviewModules.Add("photocraft");sidePreview.Refresh();await sidePreview.Completion;
            check(sidePreview.ModuleId!="photocraft","The new photo module respects the existing enable/disable setting");
        }
        finally
        {
            Settings.DisabledPreviewModules.Remove("photocraft");Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();
        }
        check(HiddenTrialPresentation.ForegroundViolations==0,"Photo previews and workers never activate the host desktop");
    }
}
#endif
