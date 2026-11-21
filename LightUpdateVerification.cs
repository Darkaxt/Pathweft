#if DEBUG
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// LIGHT-02/LIGHT-03: the app side of light updates against a local feed and an owned fixture
// installation. It never runs an installer and never reads the real installation.
public partial class MainWindow
{
    internal async Task VerifyLightUpdate59(string root,Action<bool,string> check)
    {
        string installed=Path.Combine(root,"light-installed");Directory.CreateDirectory(Path.Combine(installed,"data"));
        var files=new List<object>();
        foreach(string name in new[]{"Pathweft.dll",@"data\nested.bin"})
        {
            var data=new byte[64*1024];RandomNumberGenerator.Fill(data);File.WriteAllBytes(Path.Combine(installed,name),data);
            files.Add(new{path=name.Replace('\\','/'),bytes=data.Length,sha256=Convert.ToHexString(SHA256.HashData(data))});
        }
        File.WriteAllText(Path.Combine(installed,"release-manifest.json"),JsonSerializer.Serialize(new{product="Pathweft",version=Updater.Current.ToString(),files}),new UTF8Encoding(true));
        string manifestSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(installed,"release-manifest.json"))));
        var package=new byte[256*1024];RandomNumberGenerator.Fill(package);
        string packageSha=Convert.ToHexString(SHA256.HashData(package));
        var next=new Version(Updater.Current.Major,Updater.Current.Minor,Updater.Current.Build+1);
        string Description(string baseVersion,string sha=null)=>JsonSerializer.Serialize(new{version=next.ToString(),@base=baseVersion,baseManifestSha256=(sha??manifestSha).ToLowerInvariant(),installer=$"Pathweft-{next}-update.exe",bytes=package.Length,sha256=packageSha.ToLowerInvariant()});
        var responses=new Dictionary<string,byte[]>();
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        string url=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var serving=Task.Run(async()=>
        {
            while(true)
            {
                TcpClient client;try{client=await listener.AcceptTcpClientAsync();}catch(Exception error)when(error is SocketException or ObjectDisposedException){return;}
                using(client)
                {
                    using var stream=client.GetStream();using var request=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                    string path=(await request.ReadLineAsync())?.Split(' ')[1]??"";while(await request.ReadLineAsync() is {Length:>0}){}
                    byte[] body;lock(responses)responses.TryGetValue(path,out body);
                    try
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(body==null?"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n":$"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                        if(body!=null)await stream.WriteAsync(body);
                    }
                    catch(IOException){}
                }
            }
        });
        void Serve(string path,byte[] body){lock(responses)responses[path]=body;}
        string previousRoot=Updater.InstalledRoot,previousFeed=Updater.Feed;
        Updater.InstalledRoot=installed;
        try
        {
            // the published release as the updater reads it from GitHub: both light assets and their digests
            Serve("/feed",Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{tag_name="v"+next,body="Notes.",assets=new object[]{
                new{name=$"Pathweft-{next}-setup.exe",browser_download_url=url+"/setup.exe",size=400_000_000L,digest="sha256:"+new string('a',64)},
                new{name=$"Pathweft-{next}-update.json",browser_download_url=url+"/info",size=300L,digest="sha256:"+new string('b',64)},
                new{name=$"Pathweft-{next}-update.exe",browser_download_url=url+"/update.exe",size=(long)package.Length,digest="sha256:"+packageSha.ToLowerInvariant()}}})));
            Updater.Feed=url+"/feed";
            var release=await Updater.Latest();
            check(release.Light is {} light&&light.InfoUrl==url+"/info"&&light.Url==url+"/update.exe"&&light.Size==package.Length&&light.Sha256.Equals(packageSha,StringComparison.OrdinalIgnoreCase)&&release.Full.FileName==$"Pathweft-{next}-setup.exe","The release feed yields the light package and its description beside the full installer");
            Serve("/info",Encoding.UTF8.GetBytes(Description(Updater.Current.ToString())));
            var reports=new List<Updater.DownloadProgress>();
            var chosen=await Updater.Choose(release,new Progress<Updater.DownloadProgress>(reports.Add),CancellationToken.None);
            check(chosen.Package.Light&&chosen.Reason==null&&chosen.Package.FileName==$"Pathweft-{next}-update.exe","An unchanged installation of the base release gets the light package");
            await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(reports.Count>0&&reports.All(item=>item.Stage=="Checking installed files"),"The installed-file check reports its own named progress");

            async Task Full(string because,Func<Task> arrange,Func<Task> restore=null)
            {
                await arrange();
                var result=await Updater.Choose(release,null,CancellationToken.None);
                check(!result.Package.Light&&result.Package.FileName==$"Pathweft-{next}-setup.exe"&&result.Reason!=null,"The full installer is chosen, with its reason, when "+because+": "+result.Reason);
                if(restore!=null)await restore();
            }
            string nested=Path.Combine(installed,"data","nested.bin");byte[] original=File.ReadAllBytes(nested);
            await Full("an installed file changed",()=>{File.WriteAllBytes(nested,new byte[original.Length]);return Task.CompletedTask;},()=>{File.WriteAllBytes(nested,original);return Task.CompletedTask;});
            await Full("an installed file is missing",()=>{File.Delete(nested);return Task.CompletedTask;},()=>{File.WriteAllBytes(nested,original);return Task.CompletedTask;});
            await Full("the light update is for another version",()=>{Serve("/info",Encoding.UTF8.GetBytes(Description("1.0.1")));return Task.CompletedTask;});
            await Full("the installed file list is another one",()=>{Serve("/info",Encoding.UTF8.GetBytes(Description(Updater.Current.ToString(),new string('c',64))));return Task.CompletedTask;});
            await Full("the description is missing",()=>{Serve("/info",null);return Task.CompletedTask;});
            Serve("/info",Encoding.UTF8.GetBytes(Description(Updater.Current.ToString())));
            await Full("the release has no light package",()=>{release=release with{Light=null};return Task.CompletedTask;});
            release=await Updater.Latest();

            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();bool stopped=false;
                try{await Updater.Choose(release,null,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
                check(stopped,"A cancelled installed-file check stops instead of choosing a package");
            }

            // the chosen light package downloads under its own name and its checksum is verified
            Serve("/update.exe",package);
            string downloaded=await Updater.Download(chosen.Package,null,CancellationToken.None);
            try{check(Path.GetFileName(downloaded)==$"Pathweft-{next}-update.exe"&&File.ReadAllBytes(downloaded).SequenceEqual(package),"The light package downloads under its own name with its checksum verified");}
            finally{UpdateDownloads.Discard(downloaded);}
            bool rejected=false;
            try{string wrong=await Updater.Download(chosen.Package with{Sha256=new string('0',64)},null,CancellationToken.None);UpdateDownloads.Discard(wrong);}catch(IOException){rejected=true;}
            check(rejected,"A light package with a different checksum is rejected");

            // the popup says which package it is and why the full one
            var dialog=new UpdateDialog(this);HiddenTrialPresentation.Prepare(dialog);dialog.Show();
            try
            {
                var sub=(TextBlock)dialog.FindName("Sub");
                dialog.ShowRelease(release);
                while(dialog.ChosenPackage==null)await Task.Delay(20);
                check(dialog.ChosenPackage.Package.Light&&sub.Text.Contains("only the files that changed"),"The update popup offers the light package and says so: "+sub.Text);
                File.WriteAllBytes(nested,new byte[original.Length]);
                dialog.ShowRelease(release);
                while(dialog.ChosenPackage==null)await Task.Delay(20);
                check(!dialog.ChosenPackage.Package.Light&&sub.Text.Contains("full installer: an installed file differs"),"The update popup names the reason for the full installer: "+sub.Text);
                File.WriteAllBytes(nested,original);
                dialog.ShowChecking();check(dialog.ChosenPackage==null,"Checking again drops the earlier choice");
                // LIGHT-03: reopened by a refused light update, the full installer continues at once (here its
                // download is refused by the feed, so nothing can run)
                dialog.ShowRelease(release,continueFull:true);
                check(dialog.ChosenPackage is {Package.Light:false,Reason:"the light update couldn't be used on this installation"},"Reopened after a refused light update, the full installer continues with its reason");
                var update=(Button)dialog.FindName("UpdateButton");
                while(!update.IsEnabled)await Task.Delay(20);
            }
            finally{dialog.Close();}
            check(HiddenTrialPresentation.ForegroundViolations==0,"Light update checks perform no foreground takeover");
        }
        finally
        {
            Updater.InstalledRoot=previousRoot;Updater.Feed=previousFeed;
            listener.Stop();await serving;
        }
    }
}
#endif
