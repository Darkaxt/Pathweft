#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyOfficeExtraction16(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string marker="Located é 子 😀 \"quoted\" ";
        string longText=string.Concat(Enumerable.Repeat(marker,200));
        foreach(var item in new[]{("document.docx","word/document.xml","WordCraft","paragraph"),("spreadsheet.xlsx","xl/worksheets/sheet1.xml","GridCraft","cell"),("navigation-distinct.pptx","ppt/slides/slide1.xml","DeckCraft","shape")})
        {
            string path=Path.Combine(root,"extract-"+item.Item1);
            RewriteExtractionFixture(Path.Combine(inputs,item.Item1),path,item.Item2,longText);
            byte[] before=SHA256.HashData(File.ReadAllBytes(path));
            File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=new OfficeDocument();
            (string Engine,int Pid)[] workers; string snapshot;
            try
            {
                await document.Open(path,CancellationToken.None);
                check(document.Info.Engine==item.Item3&&!document.FallbackForVerification,"Refreshed core owns actual import: "+item.Item3);
                var first=await document.RenderPage(0,0,CancellationToken.None);
                check(first.Raster.Pixels.Length>0,"Refreshed core renders before extraction: "+item.Item3);
                var records=new List<OfficeWorker.TextRecord>(); OfficeWorker.TextCursor cursor=null;
                int batches=0;
                do
                {
                    var batch=await document.ExtractText(cursor,CancellationToken.None,1024);
                    check(batch.Status is "text" or "empty"&&batch.Coverage.Length>0,"Located extraction reports explicit coverage: "+item.Item3);
                    check(batch.Records.All(r=>r.Location.TryGetProperty(item.Item4,out _)),"Each extracted string has a native coordinate: "+item.Item3);
                    records.AddRange(batch.Records); cursor=batch.Next;batches++;
                }while(cursor!=null);
                var joined=records.GroupBy(r=>r.Location.GetRawText()).Select(g=>string.Concat(g.OrderBy(r=>r.Offset).Select(r=>r.Text))).ToArray();
                check(batches>1&&joined.Any(text=>text.Contains(longText,StringComparison.Ordinal)),"Bounded worker continuations preserve complete Unicode text: "+item.Item3);
                using(var cancelled=new CancellationTokenSource())
                {
                    bool rejected=false;
                    document.RequestSentForVerification=request=>{if(request.Action=="extract")cancelled.Cancel();};
                    try{await document.ExtractText(null,cancelled.Token);}catch(OperationCanceledException){rejected=true;}
                    finally{document.RequestSentForVerification=null;}
                    check(rejected&&cancelled.IsCancellationRequested,"Extraction drains its actual sent response before reporting caller cancellation: "+item.Item3);
                }
                int last=Math.Max(0,document.Info.Parts[0].Pages-1);
                var after=await document.RenderPage(0,last,CancellationToken.None);
                check(after.Raster.Pixels.Length>0&&!document.FallbackForVerification,"Rendering survives extraction and cancellation without fallback: "+item.Item3);
                workers=document.WorkersForVerification.ToArray();snapshot=document.SourceForVerification;
            }
            finally{await document.DisposeAsync();}
            check(workers.All(worker=>Exited(worker.Pid))&&!Directory.Exists(Path.GetDirectoryName(snapshot)),"Extraction worker exits and releases owned snapshot: "+item.Item3);
            check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))&&File.ReadAllText(path+":Zone.Identifier").Contains("ZoneId=3"),"Extraction preserves source bytes and security marker: "+item.Item3);
        }
        string empty=Path.Combine(root,"empty-text.docx");
        RewriteExtractionFixture(Path.Combine(inputs,"document.docx"),empty,"word/document.xml","",true);
        await using(var document=new OfficeDocument())
        {
            await document.Open(empty,CancellationToken.None);
            var batch=await document.ExtractText(null,CancellationToken.None);
            check(document.Info.Engine=="WordCraft"&&batch.Status=="empty"&&batch.Records.Length==0&&batch.Next==null,"Empty native document text is explicit and does not start an OCR or alternate extractor");
        }
        foreach(var item in new[]{("malformed.docx",false),("protected.docx",true)})
        {
            string path=Path.Combine(root,item.Item1);
            if(item.Item2)File.Copy(Path.Combine(inputs,"fallback-Encrypted_MSO2013_abc.docx"),path);
            else File.WriteAllText(path,"not an Office container");
            byte[] original=SHA256.HashData(File.ReadAllBytes(path));
            var process=new OfficeWorkerProcess(root,CancellationToken.None);int pid=process.ProcessId;bool rejected=false;
            try{await process.Request(new("open",Source:path,Engine:"wordcraft"),CancellationToken.None);}
            catch(IOException error){rejected=!string.IsNullOrWhiteSpace(error.Message);}
            finally{await process.DisposeAsync();}
            check(rejected&&Exited(pid)&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Native extraction import rejects invalid/protected input with explicit failure and verified exit: "+item.Item1);
        }
    }

    static void RewriteExtractionFixture(string source,string target,string member,string text,bool clearAll=false)
    {
        using var original=ZipFile.OpenRead(source);using var result=ZipFile.Open(target,ZipArchiveMode.Create);
        foreach(var entry in original.Entries)
        {
            using var input=entry.Open();using var output=result.CreateEntry(entry.FullName).Open();
            if(entry.FullName!=member){input.CopyTo(output);continue;}
            var xml=XDocument.Load(input,LoadOptions.PreserveWhitespace);
            if(clearAll)
            {foreach(var node in xml.Descendants().Where(e=>e.Name.LocalName=="t"))node.Value="";}
            else if(member.StartsWith("xl/",StringComparison.Ordinal))
            {
                XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var data=xml.Descendants(ns+"sheetData").Single();var row=data.Elements(ns+"row").First();
                var cell=row.Elements(ns+"c").First();cell.SetAttributeValue("t","inlineStr");cell.ReplaceNodes(new XElement(ns+"is",new XElement(ns+"t",text)));
            }
            else
            {
                var node=xml.Descendants().First(e=>e.Name.LocalName=="t");
                node.Value=text;node.SetAttributeValue(XNamespace.Xml+"space","preserve");
            }
            using var writer=new StreamWriter(output,new UTF8Encoding(false),leaveOpen:true);xml.Save(writer,SaveOptions.DisableFormatting);
        }
    }
}
#endif
