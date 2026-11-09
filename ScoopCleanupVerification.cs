#if DEBUG
using System.Diagnostics;
using System.IO;

namespace FileExplorer;

internal static class ScoopCleanupVerification
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        string before=Environment.GetEnvironmentVariable("SCOOP"),cacheBefore=Environment.GetEnvironmentVariable("SCOOP_CACHE");
        try
        {
            ScoopCleanup.DeleteFailureForVerification=error=>File.AppendAllText(Path.Combine(root,"scoop-delete-errors.txt"),error+Environment.NewLine);
            // Read-only qualification against the real inventory, before the
            // fixture environment is selected. Never clean a host package.
            var live=CleanupReview.Targets.Single(t=>t.Id=="scoop-versions");
            if(Directory.Exists(live.Root))
            {
                int registrations=0;ScoopCleanup.ResourcesRegisteredForVerification=_=>registrations++;
                try
                {
                    var clock=Stopwatch.StartNew();
                    using var estimate=new CleanupReview(live,Path.Combine(root,"live-estimate"));
                    await Task.Run(()=>estimate.Analyze(default));clock.Stop();
                    File.WriteAllText(Path.Combine(root,"scoop-live-estimate.json"),System.Text.Json.JsonSerializer.Serialize(new{live.Root,estimate.Count,estimate.Bytes,estimate.Unreadable,estimate.IndexedVersions,milliseconds=clock.Elapsed.TotalMilliseconds,registrations}));
                    check(registrations==0&&Directory.GetFiles(Path.Combine(root,"live-estimate")).Length==0,"Real Scoop estimation performs no consumer registration or per-file deletion manifest");
                }
                finally{ScoopCleanup.ResourcesRegisteredForVerification=null;}
                int executablePaths=0,liveQueries=0;bool executableOnly=true;
                ScoopCleanup.ResourcesRegisteredForVerification=files=>{executablePaths+=files.Count;executableOnly&=files.All(file=>file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase));};
                ScoopCleanup.ConsumerQueryForVerification=()=>liveQueries++;
                try
                {
                    var candidates=ScoopCleanup.Candidates(live,default).ToArray();var clock=Stopwatch.StartNew();
                    var blocked=ScoopCleanup.Consumers(candidates,default);clock.Stop();
                    File.WriteAllText(Path.Combine(root,"scoop-live-consumers.json"),System.Text.Json.JsonSerializer.Serialize(new{groups=candidates.Length,executablePaths,queries=liveQueries,blocked=blocked.Count,milliseconds=clock.Elapsed.TotalMilliseconds}));
                    check(executableOnly,"Real Scoop consumer preflight registers only executable names and does not delete host packages");
                }
                finally{ScoopCleanup.ResourcesRegisteredForVerification=null;ScoopCleanup.ConsumerQueryForVerification=null;}
            }
            string fixture=Fixture("normal");var version=Package(fixture,"sample");
            string persistent=Path.Combine(fixture,"persist","sample");Directory.CreateDirectory(persistent);File.WriteAllText(Path.Combine(persistent,"user.txt"),"user data");
            await Junction(Path.Combine(version.Old,"data"),persistent);
            Package(fixture,"scoop");string unknown=Path.Combine(fixture,"apps","unknown","1.0");Directory.CreateDirectory(unknown);File.WriteAllText(Path.Combine(unknown,"user.txt"),"not an installed package");
            using(var review=Review(fixture,false))
            {
                review.Analyze(default);check(review.Count>0&&review.Row.Details.Contains(version.Old),"Scoop review exposes exact old version candidates in its selectable assessment");
                var outcome=review.Clean(default);
                check(outcome.Deleted>0&&outcome.Skipped==0&&!Directory.Exists(version.Old)&&File.Exists(Path.Combine(version.Active,"payload.txt"))&&
                    File.ReadAllText(Path.Combine(persistent,"user.txt"))=="user data"&&Directory.Exists(Path.Combine(fixture,"apps","scoop","1.0"))&&Directory.Exists(unknown),
                    "Real Scoop cleanup removes reviewed obsolete version and persistent link name, preserving current, persisted data, manager and unknown folders");
            }
            string cache=Path.Combine(fixture,"cache");Directory.CreateDirectory(cache);
            foreach(string name in new[]{"sample#1.0#old.zip","sample#2.0#current.zip","sample#3.0#future.zip","sample#1.0#partial.zip.download","unknown#1.0#unowned.zip"})File.WriteAllText(Path.Combine(cache,name),name);
            using(var review=Review(fixture,true))
            {review.Analyze(default);var outcome=review.Clean(default);check(outcome.Deleted==1&&!File.Exists(Path.Combine(cache,"sample#1.0#old.zip"))&&Directory.GetFiles(cache).Length==4,"Scoop cache removes only completed older downloads for the identified installed app");}

            foreach(string change in new[]{"retarget","new","changed","busy","hardlink"})
            {
                fixture=Fixture(change);version=Package(fixture,"sample");
                using var review=Review(fixture,false);review.Analyze(default);
                string file=Path.Combine(version.Old,"payload.txt");FileStream busy=null;
                try
                {
                    if(change=="retarget"){Directory.Delete(version.Current);await Junction(version.Current,version.Old);}
                    if(change=="new")File.WriteAllText(Path.Combine(version.Old,"new.txt"),"new after review");
                    if(change=="changed")File.AppendAllText(file,"changed after review");
                    if(change=="busy")busy=new(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                    if(change=="hardlink")check(CreateHardLink(Path.Combine(fixture,"alias.txt"),file,IntPtr.Zero),"Owned Scoop hardlink fixture created");
                    var outcome=review.Clean(default);
                    if(change is "retarget" or "busy")
                        check(outcome.Skipped>0&&Directory.Exists(version.Old)&&File.Exists(file)&&File.Exists(Path.Combine(version.Active,"payload.txt")),"Scoop whole-version cleanup preserves a current or busy version: "+change);
                    if(change=="busy")check(outcome.Details.Contains("may be partially removed"),"A locked non-executable reports incomplete removal rather than claiming a clean version");
                    if(change is not "retarget" and not "busy")
                        check(outcome.Deleted==1&&!Directory.Exists(version.Old)&&File.Exists(Path.Combine(version.Active,"payload.txt"))&&
                            (change!="hardlink"||File.ReadAllText(Path.Combine(fixture,"alias.txt"))=="original package 1.0"),"Scoop deletes the entire obsolete version, including added/changed contents and only the owned hardlink name: "+change);
                }
                finally{busy?.Dispose();}
            }
            fixture=Fixture("running");version=Package(fixture,"sample");string executable=Path.Combine(version.Old,"owned-cmd.exe");File.Copy(Path.Combine(Environment.SystemDirectory,"cmd.exe"),executable);
            var start=new ProcessStartInfo(executable,"/d /q"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start))
            {
                try
                {
                    await process.StandardInput.WriteLineAsync("echo PATHWEFT_READY");await process.StandardInput.FlushAsync();
                    // cmd writes its prompt before reading redirected stdin;
                    // the first output line can therefore include that prefix.
                    while(await process.StandardOutput.ReadLineAsync() is {} line)if(line.TrimEnd().EndsWith("PATHWEFT_READY",StringComparison.Ordinal))break;
                    using var review=Review(fixture,false);review.Analyze(default);var outcome=review.Clean(default);
                    check(!process.HasExited&&review.Count==1&&outcome.Deleted==0&&outcome.Skipped==1&&File.Exists(executable)&&File.Exists(Path.Combine(version.Old,"manifest.json")),"Fast estimate includes the old version; explicit Clean detects its running executable and preserves the entire version");
                }
                finally{if(!process.HasExited){await process.StandardInput.WriteLineAsync("exit");process.StandardInput.Close();await process.WaitForExitAsync();}}
            }
            fixture=Fixture("ambiguous");version=Package(fixture,"sample");Directory.Delete(version.Current);
            using(var review=Review(fixture,false)){review.Analyze(default);check(review.Count==0&&review.Clean(default).Deleted==0&&Directory.Exists(version.Old),"Missing current binding preserves ambiguous/no-junction installations");}
            fixture=Fixture("cancel-registration");version=Package(fixture,"sample");
            for(int i=0;i<260;i++)File.WriteAllText(Path.Combine(version.Old,$"member-{i}.txt"),"owned");
            File.Copy(Path.Combine(Environment.SystemDirectory,"cmd.exe"),Path.Combine(version.Old,"owned-cmd.exe"));
            var target=new CleanupTarget("scoop-versions","Owned Scoop","Cancellation fixture",Path.Combine(fixture,"apps"));
            using(var cancellation=new CancellationTokenSource())
            {
                int registered=0;bool onlyExecutables=true;bool cancelled=false;
                ScoopCleanup.ResourcesRegisteredForVerification=files=>{registered++;onlyExecutables&=files.All(file=>file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase));cancellation.Cancel();};
                try
                {
                    using var review=Review(fixture,false);
                    review.Analyze(cancellation.Token);
                    check(registered==0&&review.Count==1,"Version estimation never performs Restart Manager file registration");
                    cancelled=review.Clean(cancellation.Token).Cancelled;
                    check(cancelled&&registered==1&&onlyExecutables&&File.Exists(Path.Combine(version.Old,"payload.txt")),"Scoop registers only executable names and cancellation stops before deleting any package");
                }
                finally{ScoopCleanup.ResourcesRegisteredForVerification=null;}
                string moved=Path.Combine(fixture,"apps-renamed");Directory.Move(target.Root,moved);Directory.Move(moved,target.Root);
                check(Directory.Exists(version.Old),"Cancelled Scoop registration releases its directory leases before completion");
            }
            using(var review=Review(fixture,false))
            {
                review.Analyze(default);using var cancellation=new CancellationTokenSource();
                ScoopCleanup.ResourcesRegisteredForVerification=_=>cancellation.Cancel();
                try{check(review.Clean(cancellation.Token).Cancelled&&File.Exists(Path.Combine(version.Old,"payload.txt")),"Cancelling consumer recheck during Clean returns a cancelled outcome and preserves the package");}
                finally{ScoopCleanup.ResourcesRegisteredForVerification=null;}
            }
            fixture=Fixture("unindexed-batch");version=Package(fixture,"sample");var secondVersion=Package(fixture,"second");var thirdVersion=Package(fixture,"third");
            foreach(var group in new[]{version,secondVersion,thirdVersion})File.Copy(Path.Combine(Environment.SystemDirectory,"cmd.exe"),Path.Combine(group.Old,"owned-cmd.exe"));
            int queries=0,registeredExecutables=0;bool onlyExecutablePaths=true;ScoopCleanup.ConsumerQueryForVerification=()=>queries++;
            ScoopCleanup.ResourcesRegisteredForVerification=files=>{registeredExecutables+=files.Count;onlyExecutablePaths&=files.All(file=>file.EndsWith(".exe",StringComparison.OrdinalIgnoreCase));};
            ScoopCleanup.VersionSizesForVerification=(_,_)=>new(new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase),false);
            try
            {
                using var review=Review(fixture,false);review.Analyze(default);
                check(review.Count==3&&review.UnknownVersionSizes==3&&review.Row.Bytes==null&&review.Row.Details.Contains(version.Old),"Missing Everything estimates retain all obsolete folders without recursive sizing or pretending their size is zero");
                var updates=new List<CleanupProgress>();var outcome=review.Clean(default,updates.Add);
                check(outcome.Deleted==3&&queries==1&&registeredExecutables==3&&onlyExecutablePaths&&updates.First().Phase.StartsWith("Checking")&&updates.Last().Completed==3&&updates.All(x=>x.Total==3),"Unindexed obsolete folders register only executable names in one native query with real completed/total progress");
            }
            finally{ScoopCleanup.VersionSizesForVerification=null;ScoopCleanup.ConsumerQueryForVerification=null;ScoopCleanup.ResourcesRegisteredForVerification=null;}
            fixture=Fixture("cancel-inflight");var first=Package(fixture,"first");var second=Package(fixture,"second");
            using(var review=Review(fixture,false))
            using(var cancellation=new CancellationTokenSource())
            {
                review.Analyze(default);string started=null;
                ScoopCleanup.VersionDeleteStartedForVerification=folder=>{started=folder;cancellation.Cancel();};
                try
                {
                    var outcome=review.Clean(cancellation.Token);
                    check(outcome.Cancelled&&outcome.Deleted==1&&started!=null&&!Directory.Exists(started)&&
                        new[]{first.Old,second.Old}.Count(Directory.Exists)==1,
                        "Cancellation during a started version finishes that folder and stops before the next version");
                }
                finally{ScoopCleanup.VersionDeleteStartedForVerification=null;}
            }
        }
        finally{ScoopCleanup.DeleteFailureForVerification=null;Environment.SetEnvironmentVariable("SCOOP",before);Environment.SetEnvironmentVariable("SCOOP_CACHE",cacheBefore);}

        string Fixture(string name)
        {string path=Path.Combine(root,"scoop-fixtures",name);Directory.CreateDirectory(path);Environment.SetEnvironmentVariable("SCOOP",path);Environment.SetEnvironmentVariable("SCOOP_CACHE",Path.Combine(path,"cache"));return path;}
        CleanupReview Review(string fixture,bool cache)=>new(new(cache?"scoop-cache":"scoop-versions","Owned Scoop","Only owned package fixtures",Path.Combine(fixture,cache?"cache":"apps")),Path.Combine(root,"scoop-reviews"));
    }
    internal static (string Old,string Active,string Current) Package(string root,string app)
    {
        string folder=Path.Combine(root,"apps",app);foreach(string v in new[]{"1.0","2.0"})
        {
            string path=Path.Combine(folder,v);Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"manifest.json"),"{\"version\":\""+v+"\"}");File.WriteAllText(Path.Combine(path,"install.json"),"{\"architecture\":\"64bit\"}");
            File.WriteAllText(Path.Combine(path,"payload.txt"),"original package "+v);
        }
        Junction(Path.Combine(folder,"current"),Path.Combine(folder,"2.0")).GetAwaiter().GetResult();
        return(Path.Combine(folder,"1.0"),Path.Combine(folder,"2.0"),Path.Combine(folder,"current"));
    }
    static async Task Junction(string link,string target)
    {
        var start=new ProcessStartInfo("cmd.exe","/d /c mklink /J \""+link+"\" \""+target+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        using var process=Process.Start(start);await process.WaitForExitAsync().ConfigureAwait(false);if(process.ExitCode!=0)throw new IOException(await process.StandardError.ReadToEndAsync().ConfigureAwait(false));
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string link,string existing,IntPtr security);
}
#endif
