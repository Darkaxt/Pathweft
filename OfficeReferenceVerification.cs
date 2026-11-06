#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyOfficeReference16(string root,Action<bool,string> check)
    {
        string inputs=Environment.GetEnvironmentVariable("PATHWEFT_SPECIALIST_INPUTS");
        string baseline=Path.GetFullPath(Path.Combine(inputs,"../../2026-11-06-s16/baseline/office-core-comparison.json"));
        using var reference=JsonDocument.Parse(File.ReadAllBytes(baseline));
        var records=new List<object>();
        foreach(var group in reference.RootElement.EnumerateArray().GroupBy(row=>row.GetProperty("name").GetString()))
        {
            string path=Path.Combine(inputs,group.Key);byte[] original=SHA256.HashData(File.ReadAllBytes(path));
            var document=new OfficeDocument();var watch=Stopwatch.StartNew();
            (string Engine,int Pid)[] workers;
            try
            {
                await document.Open(path,CancellationToken.None);
                foreach(var old in group)
                {
                    int part=old.GetProperty("part").GetInt32(),page=old.GetProperty("page").GetInt32();
                    check(document.Info.Engine==old.GetProperty("engine").GetString()&&!document.FallbackForVerification,"Reference input stays on its actual primary core: "+group.Key);
                    check(document.Info.Parts.Length==old.GetProperty("parts").GetInt32()&&document.Info.Parts[part].Pages==old.GetProperty("pages").GetInt32(),"Refreshed core preserves actual reference pagination: "+group.Key+"/"+part);
                    var frame=(await document.RenderPage(part,page,CancellationToken.None)).Raster;
                    check(frame.Width==old.GetProperty("width").GetInt32()&&frame.Height==old.GetProperty("height").GetInt32(),"Refreshed reference frame preserves geometry: "+group.Key+"/"+part+"/"+page);
                    string hash=Convert.ToHexString(SHA256.HashData(frame.Pixels));
                    bool identical=hash==old.GetProperty("sha256").GetString();
                    for(int i=0;i<frame.Pixels.Length;i+=4)(frame.Pixels[i],frame.Pixels[i+2])=(frame.Pixels[i+2],frame.Pixels[i]);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Pbgra32,null,frame.Pixels,frame.Width*4)));
                    using(var output=File.Create(Path.Combine(root,group.Key+"-"+part+"-"+page+".png")))png.Save(output);
                    using var process=Process.GetProcessById(document.WorkerForVerification);
                    // Pixel changes are evidence for visual review, not an
                    // invented fidelity threshold or an automatic regression.
                    records.Add(new{name=group.Key,part,page,width=frame.Width,height=frame.Height,sha256=hash,identical,elapsedMs=watch.ElapsedMilliseconds,workerPeakBytes=process.PeakWorkingSet64});
                }
                workers=document.WorkersForVerification.ToArray();
            }
            finally{await document.DisposeAsync();}
            check(workers.All(w=>Exited(w.Pid))&&original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Reference comparison releases workers and preserves input: "+group.Key);
        }
        File.WriteAllText(Path.Combine(root,"office-core-comparison.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
