#if DEBUG
using System.IO;
using System.Text.Json.Nodes;
using FileExplorer.Search;

namespace FileExplorer;

public partial class MainWindow
{
    // CORE-20/SEARCH-09: indexed folder sizes from the installed Pathweft index service,
    // read-only (the fixture lives on an NTFS drive the service follows through its journal).
    internal async Task VerifyLiveIndexedSizes15(string root,Action<bool,string> check)
    {
        if(!root.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned indexed-size fixture required");
        string neutral=Directory.CreateDirectory(Path.Combine(root,"neutral")).FullName;
        await S1Verification.Navigate(Left,neutral);await S1Verification.Navigate(Right,neutral);await Task.WhenAll(Left.StopSizingAsync(),Right.StopSizingAsync());
        string fixture=Directory.CreateDirectory(Path.Combine(root,"live indexed fixture é 子")).FullName;
        string folder=Directory.CreateDirectory(Path.Combine(fixture,"child")).FullName;
        File.WriteAllBytes(Path.Combine(folder,"one.bin"),new byte[123]);File.WriteAllBytes(Path.Combine(folder,"two.bin"),new byte[456]);
        // The service applies journal changes asynchronously; poll its answers for up to two minutes
        // (no rescan, no settings change), so a wrong answer fails with what the service said.
        Dictionary<string,long> totals;
        var deadline=DateTime.UtcNow.AddMinutes(2);
        using(var monitor=new PeriodicTimer(TimeSpan.FromMilliseconds(100)))
        {
            do{totals=await IndexedSearch.FolderSizesAsync([folder,Path.Combine(fixture,"absent")],CancellationToken.None);
                if(totals.TryGetValue(folder,out long size)&&size==579)break;
            }while(DateTime.UtcNow<deadline&&await monitor.WaitForNextTickAsync());
        }
        var neutralTotal=await IndexedSearch.FolderSizesAsync([neutral],CancellationToken.None);
        check(totals.Count==1&&totals[folder]==579,"The installed index service reports the exact recursive total and leaves an absent path unknown: "+string.Join(", ",totals.Select(pair=>pair.Value)));
        check(neutralTotal.TryGetValue(neutral,out long empty)&&empty==0,"A new empty folder has an indexed total of 0: "+string.Join(", ",neutralTotal.Select(pair=>pair.Value)));
        var page=await IndexedSearch.QueryAsync(IndexedSearch.FolderExpression([folder]),0,2,"Name",false,CancellationToken.None);
        check(page.Engine==IndexServiceClient.Engine&&page.Total==1&&page.Hits.Single().IsFolder,"The production folder expression finds exactly the fixture folder in the installed index");
        await IndexedRows15(root,fixture,folder,check);
    }

    // The same pane and analysis paths against a substituted service, for the private-desktop profile.
    internal async Task VerifyIndexedSizes15(string root,Action<bool,string> check)
    {
        // The substituted service answers as an index that has read the change journal: the folder's
        // current total (CORE-28: a changed row takes the index total once the index has caught up).
        // No pane watches the fixture while it is created: their notifications would
        // correctly mark it changed, and a changed folder is measured, not indexed.
        string neutral=Directory.CreateDirectory(Path.Combine(root,"neutral")).FullName;
        await S1Verification.Navigate(Left,neutral);await S1Verification.Navigate(Right,neutral);await Task.WhenAll(Left.StopSizingAsync(),Right.StopSizingAsync());
        string fixture=Directory.CreateDirectory(Path.Combine(root,"indexed fixture é 子")).FullName;
        string folder=Directory.CreateDirectory(Path.Combine(fixture,"child")).FullName;
        File.WriteAllBytes(Path.Combine(folder,"one.bin"),new byte[123]);File.WriteAllBytes(Path.Combine(folder,"two.bin"),new byte[456]);
        using var fake=new FakeIndexService();
        fake.Reply=request=>(string)request["op"]=="folder_sizes"
            ?new JsonObject{["ok"]=true,["sizes"]=new JsonArray(request["paths"].AsArray().Select(p=>(JsonNode)((string)p==folder?new JsonObject{["path"]=folder,["size"]=Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length)}:new JsonObject{["path"]=(string)p})).ToArray())}
            // CORE-32: Analyze space asks for the store's state first; this index has the fixture's drive ready.
            :(string)request["op"]=="status"?new JsonObject{["ok"]=true,["status"]=new JsonObject{["protocol"]=2,["stores"]=new JsonArray(new JsonObject{["name"]=Path.GetPathRoot(folder).TrimEnd(Path.DirectorySeparatorChar),["kind"]="ntfs",["state"]="ready"})}}
            :new JsonObject{["ok"]=true,["total"]=0,["hits"]=new JsonArray()};
        var previous=IndexedSearch.ServiceOverride;IndexedSearch.ServiceOverride=new IndexServiceClient(fake.Name);
        try
        {
            // SEARCH-10 (revised by CORE-32): the folder was just written, so the index is asked once it has had
            // CatchUp to read the write, and its total is taken from the index, not measured on disk
            Directory.SetLastWriteTimeUtc(folder,DateTime.UtcNow);var due=DateTime.UtcNow+IndexedSearch.CatchUp-TimeSpan.FromMilliseconds(20);
            var fresh=await IndexedSearch.CurrentFolderSizesAsync([(folder,Directory.GetLastWriteTimeUtc(folder))],CancellationToken.None);
            check(DateTime.UtcNow>=due&&fresh.TryGetValue(folder,out long caught)&&caught==579,"Space analysis asks the index for a folder written just now once the index has had time to read the write, and takes its total (SEARCH-10, CORE-32)");
            Directory.SetLastWriteTimeUtc(folder,DateTime.UtcNow-TimeSpan.FromMinutes(1)); // settled, as after the index caught up
            var totals=await IndexedSearch.FolderSizesAsync([folder,Path.Combine(fixture,"absent")],CancellationToken.None);
            check(totals.Count==1&&totals[folder]==579,"Batched folder-size lookup distinguishes indexed data from absent paths");
            await IndexedRows15(root,fixture,folder,check);
            // CORE-28 addendum: an analysis opened while the service is still loading the drive waits for it
            // and then takes the indexed total; without the wait the unknown answer would send the folder to a walk.
            var loadingUntil=DateTime.UtcNow.AddSeconds(2);var current=fake.Reply;
            fake.Reply=request=>(string)request["op"] switch
            {
                "status"=>new JsonObject{["ok"]=true,["status"]=new JsonObject{["protocol"]=2,["stores"]=new JsonArray(new JsonObject{["name"]=Path.GetPathRoot(folder).TrimEnd('\\'),["kind"]="ntfs",["state"]=DateTime.UtcNow<loadingUntil?"loading":"ready"})}},
                "folder_sizes" when DateTime.UtcNow<loadingUntil=>new JsonObject{["ok"]=true,["sizes"]=new JsonArray(request["paths"].AsArray().Select(p=>(JsonNode)new JsonObject{["path"]=(string)p}).ToArray())},
                _=>current(request),
            };
            await S1Verification.Navigate(Left,fixture);await Left.StopSizingAsync();
            var opening=Left.ToggleSpaceAsync();bool waited=false;
            for(var until=DateTime.UtcNow.AddSeconds(5);!waited&&DateTime.UtcNow<until;await Task.Delay(50))waited=Left.Footer.Text.StartsWith("Waiting for the index service");
            await opening;await CompletesWithin(Left.SpaceCompletion,TimeSpan.FromSeconds(30));
            check(waited&&DateTime.UtcNow>=loadingUntil&&Left.Items.Single() is SpaceEntry{State:SpaceState.Indexed,Size:580},"Analyze space opened while the index service loads the drive waits for it and then shows the indexed total ("+Left.Footer.Text+")");
            await Left.EndSpaceAsync();fake.Reply=current;
        }
        finally{IndexedSearch.ServiceOverride=previous;File.WriteAllLines(Path.Combine(root,"indexed-requests.txt"),fake.Requests.Select(request=>request.ToJsonString()));}
        await VerifyMappedDrive21(root,fixture,folder,check);
    }

    // IDX-21: a mapped network drive is answered from the store of its share. The fixture's drive letter stands in
    // for a mapped letter (NetworkDrives.MappedOverride); the substituted service indexes only the share's UNC root.
    async Task VerifyMappedDrive21(string root,string fixture,string folder,Action<bool,string> check)
    {
        char letter=char.ToUpperInvariant(fixture[0]);const string share=@"\\pathweft-verify\share";
        string Unc(string path)=>share+path[2..];
        string[] onlyShare=[share],withLetter=[letter+":",share];
        NetworkDrives.MappedOverride=new(){[letter]=share};
        using var fake=new FakeIndexService();
        string asked=null;
        fake.Reply=request=>(string)request["op"] switch
        {
            "status"=>new JsonObject{["ok"]=true,["status"]=new JsonObject{["protocol"]=2,["stores"]=new JsonArray(new JsonObject{["name"]=share,["kind"]="folder",["state"]="scanning"})}},
            "folder_sizes"=>new JsonObject{["ok"]=true,["sizes"]=new JsonArray(request["paths"].AsArray().Select(p=>(JsonNode)((string)p==Unc(folder)?new JsonObject{["path"]=Unc(folder),["size"]=4242}:new JsonObject{["path"]=(string)p})).ToArray())},
            "query"=>Query(request),
            _=>new JsonObject{["ok"]=false,["error"]="unexpected"},
        };
        JsonObject Query(JsonObject request){asked=(string)request["query"];return new JsonObject{["ok"]=true,["total"]=1,["hits"]=new JsonArray(new JsonObject{["path"]=Unc(folder),["folder"]=true,["size"]=4242,["modified"]=0})};}
        var previous=IndexedSearch.ServiceOverride;IndexedSearch.ServiceOverride=new IndexServiceClient(fake.Name);
        try
        {
            check(NetworkDrives.ToService(folder,onlyShare)==Unc(folder)&&NetworkDrives.FromService(Unc(folder),onlyShare)==folder&&NetworkDrives.FromService(share,onlyShare)==letter+@":\",
                "A path on a mapped drive is sent as the share's UNC path and the service's answer comes back under the letter (IDX-21)");
            check(NetworkDrives.IsNetwork(folder)&&NetworkDrives.IsNetwork(Unc(folder))&&!NetworkDrives.IsNetwork(@"\\?\"+Environment.SystemDirectory)&&!NetworkDrives.IsNetwork(Environment.SystemDirectory)
                &&!AdministratorSession.BrowsingFor(folder),
                "The mapped letter and its share are network folders, read with standard access in administrator view; local and device paths are not (ACCESS-09)");
            check(NetworkDrives.ToService(folder,withLetter)==folder&&NetworkDrives.Expression("path:\""+folder+"\"",withLetter)=="path:\""+folder+"\"",
                "A drive letter the index has its own store for (a Tresorit drive) is not translated (IDX-21)");
            var sizes=await IndexedSearch.FolderSizesAsync([folder],CancellationToken.None);
            check(sizes.TryGetValue(folder,out long size)&&size==4242,"Folder sizes on the mapped letter come from the share's store, keyed by the letter path (IDX-21)");
            var page=await IndexedSearch.QueryAsync("path:\""+fixture+"\\\" child",0,10,"Name",false,CancellationToken.None);
            check(asked=="path:\""+Unc(fixture)+"\\\" child"&&page.Hits.Single().Path==folder,"A search scoped to the mapped letter is sent with the UNC path and its hits return under the letter: "+asked);
            var coverage=await IndexedSearch.WaitForStoreAsync(fixture,_=>{},CancellationToken.None);
            check(coverage.Coverage==IndexedSearch.Coverage.Ready&&coverage.Detail==share,"Analyze space on the mapped letter is covered by the share's store, ready while it is rescanned (IDX-20, IDX-21): "+coverage);
            // The pane row of the mapped letter's folder takes the share's indexed total.
            if(typeof(PaneView).GetField("folderRefresh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(Left) is System.Windows.Threading.DispatcherOperation pending)await pending.Task;
            Directory.SetLastWriteTimeUtc(folder,DateTime.UtcNow-TimeSpan.FromMinutes(1));
            var settle=DateTime.UtcNow.AddSeconds(30);
            do{await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;}
            while(Left.Items.Single().Size!=4242&&DateTime.UtcNow<settle&&await Task.Delay(250).ContinueWith(_=>true));
            check(Left.Items.Single().Size==4242&&Left.Items.Single().SizeSource==IndexedSearch.SizeSource,"A pane on the mapped letter shows the share's indexed folder size, not a network walk (IDX-21): "+Left.Items.Single().Size);
            await Left.StopSizingAsync();
        }
        finally{NetworkDrives.MappedOverride=null;IndexedSearch.ServiceOverride=previous;File.WriteAllLines(Path.Combine(root,"mapped-requests.txt"),fake.Requests.Select(request=>request.ToJsonString()));}
    }

    async Task IndexedRows15(string root,string fixture,string folder,Action<bool,string> check)
    {
        // Finish a notification refresh already queued while the fixture was created: a genuinely
        // changed folder is measured from the filesystem; a settled one takes the indexed total.
        // NTFS can report the fixture's writes late (lazy metadata flush), so reopen until no
        // notification is pending and the settled row shows, bounded.
        // Settle the fixture as time would, before the pane watches it, so its notification is not mistaken for a change
        if(DateTime.UtcNow-Directory.GetLastWriteTimeUtc(folder)<TimeSpan.FromSeconds(2))Directory.SetLastWriteTimeUtc(folder,DateTime.UtcNow-TimeSpan.FromMinutes(1));
        var settle=DateTime.UtcNow.AddSeconds(30);
        do
        {
            await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;
            if(typeof(PaneView).GetField("folderRefresh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(Left) is System.Windows.Threading.DispatcherOperation queued){await queued.Task;await Left.SizingCompletion;}
        }while(Left.Items.Single().SizeSource!=IndexedSearch.SizeSource&&DateTime.UtcNow<settle&&await Task.Delay(250).ContinueWith(_=>true));
        File.WriteAllText(Path.Combine(root,"indexed-row.json"),System.Text.Json.JsonSerializer.Serialize(new{Left.Items.Single().Size,Left.Items.Single().SizeSource}));
        check(Left.Items.Single().Size==579&&Left.Items.Single().SizeSource==IndexedSearch.SizeSource,"The ordinary pane uses the indexed total with an explicit coverage label");
        // Finish a notification refresh already queued while the fixture was
        // created. A genuine changed snapshot correctly requests traversal;
        // this separate check exercises an idle indexed analysis entry.
        if(typeof(PaneView).GetField("folderRefresh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(Left) is System.Windows.Threading.DispatcherOperation pending)await pending.Task;
        await S1Verification.Navigate(Left,fixture);await Left.SizingCompletion;
        SpaceScan.IndexObserved=(path,result)=>File.AppendAllText(Path.Combine(root,"indexed-scan.txt"),path+"\n"+result+"\n");
        await Left.ToggleSpaceAsync();await Left.SpaceCompletion;SpaceScan.IndexObserved=null;
        check(Left.Items.Single().Size==579&&Left.Items.Single() is SpaceEntry{State:SpaceState.Indexed},"The analysis pane uses the same indexed folder size without traversing the covered child");
        await Left.EndSpaceAsync();
        // CORE-28 (revised criterion): the changed row takes the index total once the service has read
        // the write, or a measurement where the index does not answer; never the lagging old total.
        var changed=Sizes(Left,()=>Left.Items.Single().Size==580);
        await Task.Run(()=>File.AppendAllText(Path.Combine(folder,"one.bin"),"x"));await CompletesWithin(changed,TimeSpan.FromSeconds(30));
        check(Left.Items.Single().Size==580,"A real write refreshes the row to its new total, never a lagging indexed size ("+Left.Items.Single().SizeSource+")");
        await Left.StopSizingAsync();
    }
}
#endif
