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
    internal async Task VerifyVectorCore16(string root,Action<bool,string> check)
    {
        string inputs=Path.Combine(Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS"),"../../2026-11-06-s16/vector-inputs");
        var resources=new List<object>();
        foreach(string name in new[]{"artboards.vectorcraft","artboards.drawcraft","artboards.svg","artboards.svgz","artboards.ai","empty.vectorcraft","linked.svg"})
        {
            string path=Path.Combine(root,name);File.Copy(Path.Combine(inputs,name),path);
            byte[] original=SHA256.HashData(File.ReadAllBytes(path));File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=new OfficeDocument();(string Engine,int Pid)[] workers;string snapshot;
            try
            {
                await document.OpenCore(path,"vectorcraft",CancellationToken.None);
                check(document.Info.Engine=="VectorCraft"&&!document.FallbackForVerification,"Actual source-built vector importer without editor or Office recovery: "+name);
                int count=name is "artboards.vectorcraft" or "artboards.drawcraft" or "artboards.ai"?2:1;
                check(document.Info.Kind=="artboards"&&document.Info.Parts.Length==count&&document.Info.Parts.All(p=>p.Pages==1),"Native artboard/page counts: "+name);
                byte[] first=null;
                for(int part=0;part<count;part++)
                {
                    var frame=(await document.RenderPage(part,0,CancellationToken.None)).Raster;
                    check(frame.Width>0&&frame.Height>0&&Math.Max(frame.Width,frame.Height)<=1400&&frame.Pixels.Length==frame.Width*frame.Height*4,"Bounded CPU vector raster: "+name+" / "+part);
                    if(part==0)first=frame.Pixels;
                    else check(!first.SequenceEqual(frame.Pixels),"Artboard navigation changes actual artwork: "+name);
                    byte[] bgra=(byte[])frame.Pixels.Clone();for(int i=0;i<bgra.Length;i+=4)(bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(frame.Width,frame.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32,null,bgra,frame.Width*4)));
                    using(var output=File.Create(Path.Combine(root,name+"-"+part+".png")))encoder.Save(output);
                }
                var records=new List<OfficeWorker.TextRecord>();OfficeWorker.TextCursor cursor=null;int batches=0;string status="";
                do {var batch=await document.ExtractText(cursor,CancellationToken.None,1024);status=batch.Status;check(batch.Coverage.Length>0,"Vector extraction reports its actual coverage: "+name);records.AddRange(batch.Records);cursor=batch.Next;batches++;}while(cursor!=null);
                File.WriteAllText(Path.Combine(root,name+"-text.json"),System.Text.Json.JsonSerializer.Serialize(records,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                if(name is "empty.vectorcraft" or "linked.svg")check(records.Count==0&&status=="empty","Empty vector text is explicit without OCR");
                else
                {
                    string joined=string.Concat(records.Select(r=>r.Text));
                    check(joined.Contains("Second board")&&records.All(r=>r.Location.TryGetProperty("objectPath",out _)&&r.Location.TryGetProperty("transform",out _)),"Imported text returns native object paths and document transforms: "+name);
                    // SVG collapsible whitespace trims the final text-node
                    // space before it enters the saved native model.
                    if(name!="artboards.ai")check(batches>1&&joined==string.Concat(Enumerable.Repeat("Located é 子 😀 ",100)).TrimEnd()+"Second board","Bounded vector extraction preserves the complete imported Unicode text across continuations: "+name);
                }
                if(name=="linked.svg")check(document.Info.Notes.Any(n=>n.Contains("placeholder")),"Linked SVG resource is not fetched and its placeholder/import warning is explicit");
                using(var cancelled=new CancellationTokenSource())
                {
                    bool caught=false;document.RequestSentForVerification=request=>{if(request.Action=="extract")cancelled.Cancel();};
                    try{await document.ExtractText(null,cancelled.Token);}catch(OperationCanceledException){caught=true;}finally{document.RequestSentForVerification=null;}
                    check(caught&&(await document.RenderPage(0,0,CancellationToken.None)).Raster.Pixels.SequenceEqual(first),"Sent extraction cancellation preserves subsequent vector rendering: "+name);
                }
                workers=document.WorkersForVerification.ToArray();snapshot=document.SourceForVerification;
                using(var process=System.Diagnostics.Process.GetProcessById(document.WorkerForVerification))resources.Add(new{file=name,pid=process.Id,peakWorkingSet=process.PeakWorkingSet64,privateBytes=process.PrivateMemorySize64});
                check(File.ReadAllText(snapshot+":Zone.Identifier").Contains("ZoneId=3"),"Vector snapshot preserves source security marker: "+name);
            }
            finally{await document.DisposeAsync();}
            check(workers.All(w=>Exited(w.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot))&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Vector worker exits and source remains unchanged: "+name);
        }
        File.WriteAllText(Path.Combine(root,"vector-resources.json"),System.Text.Json.JsonSerializer.Serialize(resources,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        foreach(string name in new[]{"legacy.ai","invalid.vectorcraft"})
        {
            string path=Path.Combine(inputs,name);var document=new OfficeDocument();bool rejected=false;
            try{await document.OpenCore(path,"vectorcraft",CancellationToken.None);}catch(IOException error){rejected=error.Message.Length>0;}finally{await document.DisposeAsync();}
            check(rejected&&!document.FallbackForVerification&&document.WorkersForVerification.All(w=>Exited(w.Pid)),"Unsupported/malformed vector input reports an error and exits: "+name);
        }
        await S1Verification.Navigate(Left,root);SetActive(Left);Settings.SinglePane=true;ApplyPanes();Settings.Preview=true;
        async Task Select(string name)
        {
            Left.List.SelectedItem=Left.Items.Single(e=>e.Path==Path.Combine(root,name));ApplyPreview();await sidePreview.Completion;
            await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="vectorcraft"&&sidePreview.RendererId=="VectorCraft","Selected vector file follows the preview host: "+previewMode+" / "+name);
            var boards=PreviewChildren<ComboBox>(PreviewBody).Single(c=>AutomationProperties.GetName(c)=="Artboard");
            check(boards.Items.Count==2,"Actual host exposes both artboards");
            boards.SelectedIndex=1;await sidePreview.Completion;await PreviewSession.DrainAsync();
        }
        try
        {
            foreach(var mode in new[]{PreviewMode.Sidebar,PreviewMode.PictureInPicture,PreviewMode.Windowed})
            {
                MovePreviewHost(mode);await Select("artboards.vectorcraft");await Select("artboards.drawcraft");
                await S1Verification.Snapshot(previewWindow??this,root,"vector-"+mode);
            }
            previewWindow.Close();await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
            check(previewMode==PreviewMode.Sidebar,"Floating close restores vector preview to sidebar");
            string archive=Path.Combine(root,"artboards.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))zip.CreateEntryFromFile(Path.Combine(root,"artboards.vectorcraft"),"nested/artboards.vectorcraft");
            sidePreview.Show(new Entry{Path=archive+"\\nested\\artboards.vectorcraft",Name="artboards.vectorcraft"});await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="vectorcraft","Archive-contained vector artwork uses the same source core");
            Settings.DisabledPreviewModules.Add("vectorcraft");sidePreview.Refresh();await sidePreview.Completion;
            check(sidePreview.ModuleId!="vectorcraft","Vector module respects disable setting");
        }
        finally{Settings.DisabledPreviewModules.Remove("vectorcraft");Settings.Preview=false;ApplyPreview();await PreviewSession.DrainAsync();}
        check(HiddenTrialPresentation.ForegroundViolations==0,"Vector workflow does not activate host desktop");
    }
}
#endif
