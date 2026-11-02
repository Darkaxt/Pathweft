#if DEBUG
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Controls;
using FileExplorer.Preview;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyHtml116(string root,Action<bool,string> check)
    {
        var paths=JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("PATHWEFT_HTML116_PATHS")??"[]");
        check(paths.Length==2,"Real mapped-drive and extended-path inputs are supplied privately");
        Settings.Preview=true;Settings.DisabledPreviewModules.Remove("html");ApplyPreview();
        string folder=Path.Combine(root,"folder spaced é 子");Directory.CreateDirectory(folder);
        string local=Path.Combine(folder,"static.html");
        File.WriteAllText(local,"<!doctype html><link rel='stylesheet' href='static.css'><h1>Checked HTML</h1><img id='image' src='static.svg'><script>window.executed=true</script>");
        File.WriteAllText(Path.Combine(folder,"static.css"),"h1 {color:rgb(32,96,160)}");
        File.WriteAllText(Path.Combine(folder,"static.svg"),"<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='32' height='32'/></svg>");
        string zip=Path.Combine(root,"static.zip");
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            foreach(var file in Directory.GetFiles(folder))archive.CreateEntryFromFile(file,"nested/"+Path.GetFileName(file));
        await S1Verification.Navigate(Left,root);SetActive(Left);
        foreach(string path in paths.Concat(new[]{local,zip+@"\nested\static.html"}))
        {
            bool reported=paths.Contains(path);
            byte[] original=reported?SHA256.HashData(File.ReadAllBytes(path)):null;
            sidePreview.Show(new Entry{Name=Path.GetFileName(path),Path=path});await sidePreview.Completion;
            check(sidePreview.State=="ready"&&sidePreview.ModuleId=="html","Actual HTML module renders "+(reported?(path.StartsWith(@"\\?\")?"extended provider input":"provider mapped input"):"local/archive fixture"));
            var viewer=(HtmlViewer)sidePreview.ViewerForVerification;var core=viewer.CoreForVerification;
            string expression=reported?"document.body!==null&&document.body.textContent.length>0":"document.querySelector('h1').textContent==='Checked HTML'&&getComputedStyle(document.querySelector('h1')).color==='rgb(32, 96, 160)'&&document.getElementById('image').naturalWidth===32&&typeof window.executed==='undefined'";
            string response=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new{expression,returnByValue=true}));
            using(var state=JsonDocument.Parse(response))check(state.RootElement.GetProperty("result").GetProperty("value").GetBoolean(),"Actual browser displays the checked document and local fixture assets without reporting private contents");
            check(!core.Settings.IsScriptEnabled&&!core.Settings.AreHostObjectsAllowed&&!core.Settings.IsWebMessageEnabled,"HTML still has no page script or native bridge");
            string profile=viewer.ProfileForVerification;sidePreview.Clear();await PreviewSession.DrainAsync();
            check(viewer.Closed.IsCompletedSuccessfully&&!Directory.Exists(profile),"Browser exit is observed and the owned profile is removed");
            if(reported)check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Reported provider document remains unchanged");
        }
        var resources=new HtmlResources(local,local,CancellationToken.None);
        foreach(string uri in new[]{"https://example.invalid/asset.css","file:///C:/Windows/win.ini","https://"+resources.Host+"/%2e%2e%5coutside.css"})
        {
            bool denied=false;try{await resources.Read(new Uri(uri));}catch(IOException){denied=true;}
            check(denied,"External and parent-traversal resources remain rejected");
        }
        string link=@"C:\Documents and Settings\static.html";var linked=new HtmlResources(link,link,CancellationToken.None);bool rejected=false;
        try{await linked.Read(linked.Document);}catch(IOException error){rejected=error.Message.Contains("filesystem links");}
        check(rejected,"A real filesystem junction remains rejected");
        check(HiddenTrialPresentation.ForegroundViolations==0,"Mapped-drive verification performs no foreground takeover");
    }
}
#endif
