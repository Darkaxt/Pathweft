#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyPdfCore16(string root,Action<bool,string> check)
    {
        string inputs=Path.Combine(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS"),"../../2026-11-06-s16/pdf-inputs");
        var resources=new List<object>();var comparisons=new List<object>();
        string[] expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(inputs,"expected.json"))).RootElement.GetProperty("lines").EnumerateArray().Select(v=>v.GetString()).ToArray();
        foreach(string name in new[]{"located.pdf","blank.pdf","scanned.pdf","donor-form.pdf"})
        {
            string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);byte[] original=SHA256.HashData(File.ReadAllBytes(path));
            File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=new OfficeDocument();(string Engine,int Pid)[] workers;string snapshot;
            try
            {
                await document.OpenCore(path,"pdfcraft",CancellationToken.None);
                check(document.Info.Engine=="PdfCraft"&&!document.FallbackForVerification&&document.Info.Parts.Length==1,"PDF uses selected source library without external app: "+name);
                byte[] first=null;
                using var bytes=new MemoryStream(File.ReadAllBytes(path),false);using var stream=bytes.AsRandomAccessStream();
                var windows=await PdfDocument.LoadFromStreamAsync(stream);check(windows.PageCount==document.Info.Parts[0].Pages,"Craft/Windows PDF page counts match: "+name);
                for(int page=0;page<document.Info.Parts[0].Pages;page++)
                {
                    var native=(await document.RenderPage(0,page,CancellationToken.None)).Raster;if(page==0)first=native.Pixels;
                    check(Math.Max(native.Width,native.Height)<=1400&&native.Pixels.Length==native.Width*native.Height*4,"Bounded actual PDF raster: "+name+" / "+page);
                    SavePdfCoreFrame(Path.Combine(root,name+"-craft-"+page+".png"),native);
                    using var winPage=windows.GetPage((uint)page);using var rendered=new InMemoryRandomAccessStream();
                    await winPage.RenderToStreamAsync(rendered,new PdfPageRenderOptions{DestinationWidth=(uint)native.Width,DestinationHeight=(uint)native.Height});rendered.Seek(0);
                    var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(rendered);
                    // WinRT destination dimensions are DIPs. Its encoded pixels
                    // follow display scaling (140% on the qualification host).
                    // Compare both renderers at the same physical pixel grid.
                    var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Premultiplied,new BitmapTransform{ScaledWidth=(uint)native.Width,ScaledHeight=(uint)native.Height,InterpolationMode=BitmapInterpolationMode.Fant},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
                    var reference=new OfficeWorker.Raster(native.Width,native.Height,data.DetachPixelData());
                    SavePdfCoreFrame(Path.Combine(root,name+"-windows-"+page+".png"),reference);
                    check(reference.Width==native.Width&&reference.Height==native.Height,"Compared actual equal-size PDF rasters: "+name+" / "+page);
                    long different=0,total=0;int maximum=0;
                    for(int i=0;i<native.Pixels.Length;i++){int delta=Math.Abs(native.Pixels[i]-reference.Pixels[i]);if(delta>0)different++;total+=delta;maximum=Math.Max(maximum,delta);}
                    comparisons.Add(new{file=name,page,width=native.Width,height=native.Height,windowsEncodedWidth=decoder.PixelWidth,windowsEncodedHeight=decoder.PixelHeight,windowsDpiX=decoder.DpiX,windowsDpiY=decoder.DpiY,normalization="Windows DIP-scaled output resampled by WIC Fant to Craft physical pixel grid",differentChannels=different,maximum,meanAbsolute=(double)total/native.Pixels.Length});
                }
                var records=new List<OfficeWorker.TextRecord>();OfficeWorker.TextCursor cursor=null;int batches=0;string status="";
                do{var batch=await document.ExtractText(cursor,CancellationToken.None,1024);records.AddRange(batch.Records);cursor=batch.Next;status=batch.Status;batches++;}while(cursor!=null);
                File.WriteAllText(Path.Combine(root,name+"-text.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
                if(name=="located.pdf")
                {
                    check(batches>1&&string.Concat(records.Select(r=>r.Text))==string.Concat(expected),"Independent PDF text oracle matches complete Unicode/order through bounded continuations");
                    check(records.All(r=>r.Location.TryGetProperty("page",out _)&&r.Location.TryGetProperty("line",out _)&&r.Location.TryGetProperty("glyphStart",out _)&&r.Location.GetProperty("rects").GetArrayLength()>0),"PDF records expose native page/line/glyph ranges and view-space rectangles");
                }
                if(name=="blank.pdf")check(records.Count==0&&status=="empty","Blank PDF explicitly has no text");
                if(name=="scanned.pdf")check(records.Count==0&&status=="image-only","Scanned PDF reports image-only without OCR");
                using(var cancelled=new CancellationTokenSource())
                {
                    bool caught=false;document.RequestSentForVerification=request=>{if(request.Action=="extract")cancelled.Cancel();};
                    try{await document.ExtractText(null,cancelled.Token);}catch(OperationCanceledException){caught=true;}finally{document.RequestSentForVerification=null;}
                    check(caught&&(await document.RenderPage(0,0,CancellationToken.None)).Raster.Pixels.SequenceEqual(first),"Sent PDF extraction cancellation preserves later rendering: "+name);
                }
                workers=document.WorkersForVerification.ToArray();snapshot=document.SourceForVerification;
                using(var process=System.Diagnostics.Process.GetProcessById(document.WorkerForVerification))resources.Add(new{file=name,pid=process.Id,peakWorkingSet=process.PeakWorkingSet64,privateBytes=process.PrivateMemorySize64});
                check(File.ReadAllText(snapshot+":Zone.Identifier").Contains("ZoneId=3"),"PDF worker snapshot retains security marker: "+name);
            }
            finally{await document.DisposeAsync();}
            check(workers.All(w=>Exited(w.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot))&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"PDF worker exits and source is unchanged: "+name);
        }
        File.WriteAllText(Path.Combine(root,"pdf-resources.json"),JsonSerializer.Serialize(resources,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"pdf-comparison.json"),JsonSerializer.Serialize(comparisons,new JsonSerializerOptions{WriteIndented=true}));
        foreach(string name in new[]{"protected.pdf","invalid.pdf"})
        {
            var document=new OfficeDocument();bool rejected=false;
            try{await document.OpenCore(Path.Combine(inputs,name),"pdfcraft",CancellationToken.None);}catch(IOException error){rejected=error.Message.Length>0;}finally{await document.DisposeAsync();}
            check(rejected&&!document.FallbackForVerification&&document.WorkersForVerification.All(w=>Exited(w.Pid)),"Protected/malformed PDF rejects explicitly and exits: "+name);
        }
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();Settings.Preview=true;
        try
        {
            foreach(var mode in new[]{PreviewMode.Sidebar,PreviewMode.PictureInPicture,PreviewMode.Windowed})
            {
                MovePreviewHost(mode);
                Left.List.SelectedItem=Left.Items.Single(e=>e.Path==Path.Combine(root,"blank.pdf"));ApplyPreview();await sidePreview.Completion;
                var entry=Left.Items.Single(e=>e.Path==Path.Combine(root,"located.pdf"));Left.List.SelectedItem=entry;ApplyPreview();await sidePreview.Completion;
                check(sidePreview.State=="ready"&&sidePreview.ModuleId=="pdf","Existing Windows PDF remains default in "+mode);
                sidePreview.Show(entry,"pdfcraft");await sidePreview.Completion;
                check(sidePreview.State=="ready"&&sidePreview.ModuleId=="pdfcraft"&&sidePreview.RendererId=="PdfCraft","Explicit Craft PDF renders in "+mode);
                var next=PreviewChildren<Button>(PreviewBody).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Next page");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await PreviewSession.DrainAsync();
                await S1Verification.Snapshot(previewWindow??this,root,"pdf-"+mode);
            }
            previewWindow.Close();await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);check(previewMode==PreviewMode.Sidebar,"Floating PDF close restores sidebar");
            string archive=Path.Combine(root,"documents.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"located.pdf"),"nested/located.pdf");
            sidePreview.Show(new Entry{Path=archive+"\\nested\\located.pdf",Name="located.pdf"},"pdfcraft");await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="pdfcraft","Archive PDF uses same source core when selected");
            Settings.DisabledPreviewModules.Add("pdfcraft");sidePreview.Refresh();await sidePreview.Completion;check(sidePreview.ModuleId!="pdfcraft","Craft PDF respects module disable");
        }
        finally{Settings.DisabledPreviewModules.Remove("pdfcraft");Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();}
        check(HiddenTrialPresentation.ForegroundViolations==0,"PDF workflow never activates host desktop");
    }
    static void SavePdfCoreFrame(string path,OfficeWorker.Raster frame)
    {
        byte[] bgra=(byte[])frame.Pixels.Clone();for(int i=0;i<bgra.Length;i+=4)(bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(BitmapSource.Create(frame.Width,frame.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32,null,bgra,frame.Width*4)));
        using var output=File.Create(path);encoder.Save(output);
    }
}
#endif
