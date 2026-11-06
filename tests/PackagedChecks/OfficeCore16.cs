using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

sealed partial class PackageProbe
{
    async Task VerifyOfficeCore16()
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS")??throw new InvalidOperationException("Pinned Office inputs required.");
        var records=new List<object>();
        var type=Type("Preview.OfficeDocument");
        foreach(string name in new[]{"document-pages.docx","spreadsheet.xlsx","navigation-distinct.pptx"})
        {
            string source=Path.Combine(inputs,name);byte[] original=SHA256.HashData(File.ReadAllBytes(source));
            var document=Activator.CreateInstance(type,true);
            var watch=Stopwatch.StartNew();
            try
            {
                await (Task)Call(type,document,"Open",source,CancellationToken.None);
                var info=Get(document,"Info");string engine=(string)Get(info,"Engine");
                Check(!string.Equals(engine,"libreoffice",StringComparison.OrdinalIgnoreCase),"Actual core comparison imports through its primary renderer: "+name);
                var parts=((Array)Get(info,"Parts")).Cast<object>().ToArray();
                foreach(int part in new[]{0,parts.Length-1}.Distinct())
                foreach(int page in new[]{0,(int)Get(parts[part],"Pages")-1}.Distinct())
                {
                    var render=(Task)Call(type,document,"RenderPage",part,page,CancellationToken.None);await render;
                    var raster=Get(Get(render,"Result"),"Raster");
                    byte[] bytes=(byte[])Get(raster,"Pixels");int width=(int)Get(raster,"Width"),height=(int)Get(raster,"Height");
                    Check(bytes.Length==width*height*4&&width>0&&height>0,"Actual reference raster has complete geometry: "+name+"/"+part+"/"+page);
                    string stem=name+"-"+part+"-"+page;
                    string hash=Convert.ToHexString(SHA256.HashData(bytes));
                    for(int i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,bytes,width*4)));
                    using(var output=File.Create(Path.Combine(root,stem+".png")))png.Save(output);
                    var worker=type.GetField("worker",Flags).GetValue(document);
                    using var process=Process.GetProcessById((int)Get(worker,"ProcessId"));
                    records.Add(new{name,engine,part,page,parts=parts.Length,pages=(int)Get(parts[part],"Pages"),width,height,sha256=hash,elapsedMs=watch.ElapsedMilliseconds,workerPeakBytes=process.PeakWorkingSet64});
                }
            }
            finally{await ((IAsyncDisposable)document).DisposeAsync();}
            Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),"Core comparison leaves its reference source unchanged: "+name);
        }
        File.WriteAllText(Path.Combine(root,"office-core-comparison.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
    }
}
