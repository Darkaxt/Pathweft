using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;

sealed partial class PackageProbe
{
    async Task VerifyCraftCores16(ContentControl target,object preview)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS")??throw new InvalidOperationException("Pinned Craft inputs required");
        string craft=Path.GetFullPath(Path.Combine(inputs,"../../2026-11-06-s16"));
        var samples=new[]{
            ("wordcraft",Path.Combine(inputs,"document-pages.docx")),("gridcraft",Path.Combine(inputs,"spreadsheet.xlsx")),("deckcraft",Path.Combine(inputs,"navigation-distinct.pptx")),
            ("photocraft",Path.Combine(craft,"photo-inputs/text.pcraft")),("vectorcraft",Path.Combine(craft,"vector-inputs/artboards.vectorcraft")),
            ("figcraft",Path.Combine(craft,"fig-inputs/frames.figcraft")),("pdfcraft",Path.Combine(craft,"pdf-inputs/located.pdf"))};
        var type=Type("Preview.OfficeDocument");var sessionType=Type("Preview.PreviewSession");var resources=new List<object>();
        foreach(var (engine,input) in samples)
        {
            if(!File.Exists(input))throw new FileNotFoundException("Required independently retained core fixture",input);
            string folder=Directory.CreateDirectory(Path.Combine(root,"craft",engine)).FullName;
            string path=Path.Combine(folder,Path.GetFileName(input));File.Copy(input,path);byte[] hash=SHA256.HashData(File.ReadAllBytes(path));
            File.WriteAllText(path+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var document=Activator.CreateInstance(type,true);int pid=0;string snapshot=null;
            try
            {
                await (Task)(engine is "wordcraft" or "gridcraft" or "deckcraft"?Call(type,document,"Open",path,CancellationToken.None):Call(type,document,"OpenCore",path,engine,CancellationToken.None));
                Check(string.Equals((string)Get(Get(document,"Info"),"Engine"),engine,StringComparison.OrdinalIgnoreCase),"Delivered selected source core opens actual input: "+engine);
                var worker=type.GetField("worker",Flags).GetValue(document);pid=(int)Get(worker,"ProcessId");snapshot=(string)type.GetField("source",Flags).GetValue(document);
                object cursor=null;var text=new StringBuilder();int batches=0,locations=0;
                do
                {
                    var task=(Task)Call(type,document,"ExtractText",cursor,CancellationToken.None,1024);await task;var batch=Get(task,"Result");
                    Check((string)Get(batch,"Status")=="text"&&Get(batch,"Coverage") is string{Length:>0},"Delivered text batch has explicit coverage: "+engine);
                    foreach(var record in (IEnumerable)Get(batch,"Records"))
                    {text.Append((string)Get(record,"Text"));Check(((JsonElement)Get(record,"Location")).ValueKind==JsonValueKind.Object,"Delivered text record retains native location: "+engine);locations++;}
                    cursor=Get(batch,"Next");batches++;
                }while(cursor!=null);
                Check(text.Length>0&&locations>0&&batches>0,"Delivered source core completes located extraction: "+engine);
                var render=(Task)Call(type,document,"RenderPage",0,0,CancellationToken.None);await render;
                Check((byte[])Get(Get(Get(render,"Result"),"Raster"),"Pixels") is {Length:>0},"Delivered rendering still works after extraction: "+engine);
                using(var process=Process.GetProcessById(pid))resources.Add(new{engine,pid,peakWorkingSet=process.PeakWorkingSet64,batches,locations,textCharacters=text.Length});
                Check(File.ReadAllText(snapshot+":Zone.Identifier").Contains("ZoneId=3"),"Delivered core preserves snapshot security marker: "+engine);
            }
            finally{await ((IAsyncDisposable)document).DisposeAsync();}
            bool exited;try{using var process=Process.GetProcessById(pid);exited=process.HasExited;}catch(ArgumentException){exited=true;}
            Check(pid>0&&exited&&!Directory.Exists(Path.GetDirectoryName(snapshot))&&hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Delivered core exits, reclaims snapshot and preserves source: "+engine);
            if(engine is "wordcraft" or "gridcraft" or "deckcraft")continue; // Existing actual Office navigation/recovery runs in Specialists118.
            Call(sessionType,preview,"Show",new FileExplorer.Entry{Path=path,Name=Path.GetFileName(path)},engine);await (Task)Get(preview,"Completion");
            Check((string)Get(preview,"State")=="ready"&&(string)Get(preview,"ModuleId")==engine,"Delivered additional core is wired to the actual preview: "+engine);
            Check(Descendants<Image>(target).Any(image=>image.Source!=null),"Delivered additional preview publishes its actual raster: "+engine);
            Call(sessionType,preview,"Clear");await (Task)Call(sessionType,null,"DrainAsync");
        }
        File.WriteAllText(Path.Combine(root,"craft-resources.json"),JsonSerializer.Serialize(resources,new JsonSerializerOptions{WriteIndented=true}));
    }
}
