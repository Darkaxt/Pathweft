using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FileExplorer.Search;
using Microsoft.Data.Sqlite;

namespace FileExplorer;

// S48 (MCP-01..04): read-only Maintenance, file metadata, search and boot/change tools
// served by --performance-mcp. Nothing here applies, restores or deletes; reads only administrators may
// make use the approved administrator session (MCP-06), never a prompt.
internal static class McpTools
{
    internal static readonly string[] Tools=["get_maintenance","get_maintenance_changes","list_folder","get_file_details","get_folder_size","search_files","get_boots","get_system_changes","get_insights","get_index_status","get_content_status","get_content_state"];
    static readonly string[] Categories=["Cleanup","Startup","Services","Windows tweaks","Windows apps","Security check"];
    static readonly string[] Ranged=["last_minutes","from","to","limit"];
    static readonly Dictionary<string,string[]> Allowed=new()
    {
        ["get_maintenance"]=["category","target","offset","limit"],["get_maintenance_changes"]=["offset","limit"],
        ["list_folder"]=["path","offset","limit"],["get_file_details"]=["path"],["get_folder_size"]=["path","paths"],
        ["search_files"]=["query","match_path","match_case","whole_word","regex","sort","descending","visible_only","locations","offset","limit"],
        ["get_index_status"]=[],["get_content_status"]=[],["get_content_state"]=["folder","offset","limit"],
        ["get_boots"]=Ranged,["get_system_changes"]=Ranged,["get_insights"]=Ranged
    };
    // Bounded page of rows: the total is always reported, and whether more remain.
    // MCP-09 (owner, 2026-11-24: "If you, the owner of this product, couldn't tell the pagination system in the MCP...
    // something needs improvement there"): every paged tool answers in one shape that says which rows it returned
    // and where the next page starts, so a full page of `limit` rows is never mistaken for a cap.
    internal sealed record Page(string Status,object[] Rows,int Total,int Offset,int Limit)
    {
        [JsonPropertyName("status")] public string Status{get;}=Paging(Status,Total,Offset,Rows.Length,Limit);
        [JsonPropertyName("rows")] public object[] Rows{get;}=Rows;
        [JsonPropertyName("total")] public int Total{get;}=Total;
        [JsonPropertyName("offset")] public int Offset{get;}=Offset;
        [JsonPropertyName("limit")] public int Limit{get;}=Limit;
        [JsonPropertyName("returned")] public int Returned=>Rows.Length;
        [JsonPropertyName("has_more")] public bool HasMore=>Offset+Rows.Length<Total;
        [JsonPropertyName("next_offset")] public int? NextOffset=>HasMore?Offset+Rows.Length:null;
    }
    // "Rows 1-100 of 247. More rows: call again with offset 100 (limit up to 500)."
    internal static string Paging(string status,long total,long offset,int returned,int limit)
    {
        string page=returned==0?(total==0?"No rows.":$"No rows at offset {offset}; there are {total}."):$"Rows {offset+1}-{offset+returned} of {total}.";
        if(offset+returned<total&&returned>0)page+=$" More rows: call again with offset {offset+returned}"+(limit<500?" (limit up to 500).":".");
        else if(returned>0)page+=" This is the last page.";
        return string.IsNullOrEmpty(status)?page:status.TrimEnd()+" "+page;
    }

    sealed class Arguments(JsonElement value)
    {
        internal string Text(string key,bool required=false,int max=32767)
        {
            if(!value.TryGetProperty(key,out var item)){if(required)throw new ArgumentException(key+" is required");return null;}
            if(item.ValueKind!=JsonValueKind.String||item.GetString() is not {} text||text.Length>max)throw new ArgumentException($"{key} must be a string of at most {max} characters");
            return text;
        }
        internal bool Flag(string key)
        {
            if(!value.TryGetProperty(key,out var item))return false;
            return item.ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new ArgumentException(key+" must be true or false")};
        }
        internal int Integer(string key,int fallback,int min,int max)
        {
            if(!value.TryGetProperty(key,out var item))return fallback;
            if(item.ValueKind!=JsonValueKind.Number||!item.TryGetInt32(out int number)||number<min||number>max)throw new ArgumentException($"{key} must be an integer from {min} to {max}");
            return number;
        }
        internal string[] Paths(string key,int max)
        {
            if(!value.TryGetProperty(key,out var item))return null;
            if(item.ValueKind!=JsonValueKind.Array||item.GetArrayLength()<1||item.GetArrayLength()>max||item.EnumerateArray().Any(x=>x.ValueKind!=JsonValueKind.String))throw new ArgumentException($"{key} must be a list of 1 to {max} paths");
            return item.EnumerateArray().Select(x=>x.GetString()).ToArray();
        }
    }

    internal static async Task<object> Query(string tool,JsonElement value,CancellationToken cancel)
    {
        if(!Allowed.TryGetValue(tool,out var allowed)||value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Unknown tool or invalid arguments");
        if(value.EnumerateObject().Select(x=>x.Name).FirstOrDefault(x=>!allowed.Contains(x)) is {} unknown)throw new ArgumentException("Unknown argument: "+unknown);
        var arguments=new Arguments(value);
        int offset=arguments.Integer("offset",0,0,int.MaxValue),limit=arguments.Integer("limit",100,1,500);
        switch(tool)
        {
            case "get_maintenance":return await Maintenance(arguments.Text("category",true,40),arguments.Text("target",max:200),offset,limit,cancel);
            case "get_maintenance_changes":return Changes(offset,limit);
            case "list_folder":return await ListFolder(FullPath(arguments.Text("path",true)),offset,limit,cancel);
            case "get_file_details":return Details(FullPath(arguments.Text("path",true)));
            case "get_folder_size":
            {
                // MCP-08: paths asks the index for many folders at once.
                string one=arguments.Text("path");string[] many=arguments.Paths("paths",1024);
                if((one==null)==(many==null))throw new ArgumentException("Give either path or paths");
                if(one!=null)return (await FolderSizes([FullPath(one)],cancel))[0];
                var rows=await FolderSizes(many.Select(FullPath).ToArray(),cancel);
                return new{rows,total=rows.Length};
            }
            case "get_index_status":return await IndexedSearch.ReadAsync(new{op="status"},"status",cancel);
            case "get_content_status":return await ContentStatus(cancel);
            case "get_content_state":return await ContentState(FullPath(arguments.Text("folder",true)),offset,limit,cancel);
            case "search_files":
            {
                string query=arguments.Text("query",true,8192);
                var switches=new SearchSwitches(arguments.Flag("match_path"),arguments.Flag("match_case"),arguments.Flag("whole_word"),arguments.Flag("regex"));
                return await Search(query,switches,arguments.Text("sort",max:20)??"name",arguments.Flag("descending"),arguments.Flag("visible_only"),arguments.Integer("locations",0,0,10),offset,limit,cancel);
            }
            default:
            {
                // Same range rules as the performance tools; boots and changes default to the last 30 days.
                var parsed=PerformanceMcp.Parse("get_events",JsonSerializer.SerializeToElement(new Dictionary<string,object>
                {
                    ["last_minutes"]=arguments.Integer("last_minutes",43200,1,43200),["limit"]=limit,
                    ["from"]=arguments.Text("from",max:40),["to"]=arguments.Text("to",max:40)
                }.Where(x=>x.Value!=null).ToDictionary()));
                var to=parsed.To??DateTimeOffset.UtcNow;var query=parsed with{From=parsed.From??to.AddMinutes(-parsed.LastMinutes),To=to};
                int kind=tool switch{"get_boots"=>6,"get_system_changes"=>7,_=>8};
                var notes=new List<string>();var boots=kind is 6 or 8?await ElevatedReads.Boots(notes,cancel):null;
                return await Task.Run(()=>PerformanceAdoptionFacts.Read(kind,query,cancel,elevatedBoots:boots,notes:notes),cancel);
            }
        }
    }

    static string FullPath(string path)
    {
        if(path.Length==0||!Path.IsPathFullyQualified(path)||path.Contains('\0'))throw new ArgumentException("Use an absolute local or UNC path");
        return Path.GetFullPath(path);
    }
    static Page Bound(IEnumerable<object> rows,int offset,int limit,string status)
    {
        var all=rows.ToArray();var page=all.Skip(offset).Take(limit).ToArray();
        return new(status,page,all.Length,offset,limit);
    }
    static string Time(DateTime value)=>value==default||value.Year<1602?null:new DateTimeOffset(value.ToUniversalTime()).ToString("O",CultureInfo.InvariantCulture);

    // MCP-02 Maintenance: the rows the tab shows; Cleanup sizes from the Pathweft index.
    static async Task<object> Maintenance(string category,string target,int offset,int limit,CancellationToken cancel)
    {
        category=Categories.FirstOrDefault(x=>x.Equals(category,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Category must be one of: "+string.Join(", ",Categories));
        if(target!=null&&category!="Cleanup")throw new ArgumentException("target applies to Cleanup only");
        if(category=="Cleanup")
        {
            var targets=CleanupReview.Targets.Where(t=>target==null||t.Id==target).Select(SignedInUser).ToArray();
            if(target!=null&&targets.Length==0)throw new ArgumentException("Unknown Cleanup target. Targets: "+string.Join(", ",CleanupReview.Targets.Select(t=>t.Id)));
            var rows=new List<object>();
            foreach(var item in targets.Skip(offset).Take(limit))rows.Add(await CleanupSize(item,cancel));
            return new Page("Cleanup size report; the Maintenance tab still reviews the exact files before cleaning. Nothing was removed.",rows.ToArray(),targets.Length,offset,limit);
        }
        string status=category=="Security check"?"Read as the signed-in user; values only administrators can read are marked, never guessed.":"Read as the signed-in user, as the Maintenance tab shows it.";
        SecurityFinding[] elevated=null;
        if(category=="Security check")
            try{if(await ElevatedReads.Read("security-check",cancel) is {} json){elevated=JsonSerializer.Deserialize<SecurityFinding[]>(json);status="Read with the administrator session; nothing was changed.";}}
            catch(Exception error)when(ElevatedReads.Expected(error)){status="The administrator session could not read the list ("+error.Message+"); read as the signed-in user, and values only administrators can read are marked.";}
        var found=await Task.Run(()=>category switch
        {
            "Windows apps"=>MaintenanceApps.Read(cancel).Select(a=>a.Row()).ToArray(),
            "Security check"=>(elevated??MaintenanceSecurity.Check(cancel)).Select(MaintenancePanel.SecurityRow).ToArray(),
            _=>FileExplorer.Maintenance.Read(category,cancel)
        },cancel);
        if(category!="Security check")found=found.Select(r=>MaintenanceGuidance.Describe(category,r)).ToArray();
        return Bound(found.Select(r=>(object)new
        {
            id=r.Id,name=r.Name,scope=r.Scope,state=r.State,recommendation=r.Recommendation,group=Empty(r.Group),type=Empty(r.Type),
            goal=Empty(r.Goal),risk=Empty(r.Risk),startup_mode=Empty(r.StartupMode),publisher=Empty(r.Publisher),version=Empty(r.Version),
            installed=Empty(r.Installed),size_bytes=r.Bytes,current=Empty(r.Current),expected=Empty(r.Expected),source=Empty(r.Source),details=r.Details
        }),offset,limit,status);
    }
    static string Empty(string value)=>string.IsNullOrEmpty(value)?null:value;
    // An MCP client may launch this process with its own TEMP; report the signed-in user's temporary folder, as the tab does.
    static CleanupTarget SignedInUser(CleanupTarget target)
    {
        if(target.Id!="user-temp")return target;
        using var environment=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment");
        return environment?.GetValue("TEMP") is string temp&&Path.IsPathFullyQualified(temp)?target with{Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp))+Path.DirectorySeparatorChar}:target;
    }

    static async Task<object> CleanupSize(CleanupTarget target,CancellationToken cancel)
    {
        object Row(long? bytes,long? files,string method,string issue=null)=>new{id=target.Id,name=target.Name,description=target.Description,location=Empty(target.Root),size_bytes=bytes,files,method,issue};
        if(target.Id is "user-temp" or "windows-temp" or "error-reports" or "thumbnail-cache")
        {
            if(!Directory.Exists(target.Root))return Row(0,0,"Folder does not exist");
            string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Root));
            try
            {
                var plain=new SearchSwitches(false,false,false,false);
                var indexed=await IndexedSearch.SearchAsync(IndexedSearch.FolderExpression([root]),0,1,"name",false,plain,cancel);
                if(indexed.Total>0)
                {
                    string expression=$"file: path:\"{root}\\\""+(target.Pattern=="*"?"":" "+target.Pattern);
                    var first=await IndexedSearch.SearchAsync(expression,0,1000,"name",false,plain,cancel);
                    if(target.Pattern=="*"&&(await IndexedSearch.FolderSizesAsync([root],cancel)).TryGetValue(root,out long folder))
                        return Row(folder,first.Total,first.Engine.Split(' ')[0]+" index (folder size)");
                    long sum=0;bool unknown=false;var page=first;
                    for(uint at=0;;)
                    {
                        foreach(var hit in page.Hits)if(hit.Size>=0)sum+=hit.Size;else unknown=true;
                        at+=(uint)page.Hits.Count;if(page.Hits.Count==0||at>=page.Total)break;
                        page=await IndexedSearch.SearchAsync(expression,at,1000,"name",false,plain,cancel);
                    }
                    return Row(sum,first.Total,first.Engine.Split(' ')[0]+" index (sum of indexed files)",unknown?"Some indexed files have no size; the total is a lower bound.":null);
                }
            }
            catch(InvalidOperationException ex){return await Measured(target,"Measured directly; index: "+ex.Message,cancel);}
            return await Measured(target,"Measured directly; the Pathweft index does not cover this folder",cancel);
        }
        string how=ManagerCleanup.IsManager(target)?"The package manager's own cache report":target.Id=="scoop-versions"?null:"Measured directly";
        return await Measured(target,how,cancel);
    }
    // The tab's own analysis; its temporary review list is deleted on Dispose.
    static async Task<object> Measured(CleanupTarget target,string method,CancellationToken cancel)
    {
        using var review=new CleanupReview(target);
        try{await Task.Run(()=>review.Analyze(cancel),cancel);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {return new{id=target.Id,name=target.Name,description=target.Description,location=Empty(target.Root),size_bytes=(long?)null,files=(long?)null,method=method??"Measured directly",issue=ex.Message};}
        method??=review.IndexedVersions?IndexedSearch.SizeSource.Split(' ')[0]+" index (version folder sizes)":"Measured directly";
        string issue=review.Unreadable>0?$"{review.Unreadable:N0} items could not be read and are not counted.":review.UnknownVersionSizes>0?$"{review.UnknownVersionSizes:N0} versions have no indexed size.":null;
        return new{id=target.Id,name=target.Name,description=target.Description,location=Empty(target.Root),size_bytes=(long?)review.Bytes,files=(long?)review.Count,method,issue};
    }

    static Page Changes(int offset,int limit)
    {
        var journal=FileExplorer.Maintenance.Undo();
        return Bound(journal.Values.OrderBy(x=>x.Id,StringComparer.Ordinal).Select(x=>(object)new
        {
            id=x.Id,kind=x.Id.Split(':')[0],phase=x.Phase,registry_values=x.Before?.Length??0,service=x.Service,
            startup_file=x.StartupFile!=null,startup_task=x.StartupTask!=null,scheduled_tasks=x.Tasks?.Length??0,components=x.Components?.Length??0,
            restorable=x.Phase!="prepared"
        }),offset,limit,"Pathweft's Maintenance change journal: the original settings it saved and can restore.");
    }

    // MCP-03 files, metadata only.
    static async Task<Page> ListFolder(string path,int offset,int limit,CancellationToken cancel)
    {
        var folder=new DirectoryInfo(path);if(!folder.Exists)throw new ArgumentException("Folder not found: "+path);
        var entries=new List<FileSystemInfo>();
        bool denied=false;
        try{await Task.Run(()=>{foreach(var entry in folder.EnumerateFileSystemInfos("*",new EnumerationOptions{AttributesToSkip=0,IgnoreInaccessible=false,RecurseSubdirectories=false})){cancel.ThrowIfCancellationRequested();entries.Add(entry);}},cancel);}
        catch(UnauthorizedAccessException){denied=true;}
        if(denied)
        {
            if(!await ElevatedReads.Ensure(cancel))throw new UnauthorizedAccessException("Only administrators can list "+path+", and no administrator session is available (start Pathweft once and approve administrator access).");
            // MCP-06: a folder only administrators can list is read through the administrator session.
            var rows=await MaintenanceClient.ReadFolder(path,cancel);
            return Bound(rows.OrderBy(x=>!x.Attributes.HasFlag(FileAttributes.Directory)).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).Select(x=>(object)new
            {
                name=x.Name,type=x.Attributes.HasFlag(FileAttributes.Directory)?"folder":"file",size_bytes=x.Attributes.HasFlag(FileAttributes.Directory)?(long?)null:x.Bytes,modified=Time(x.Modified),attributes=x.Attributes.ToString(),link_target=(string)null
            }),offset,limit,"Folder entries read with the administrator session, folders first; contents are not read.");
        }
        return Bound(entries.OrderBy(x=>x is FileInfo).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).Select(x=>(object)new
        {
            name=x.Name,type=x is DirectoryInfo?"folder":"file",size_bytes=x is FileInfo file?file.Length:(long?)null,modified=Time(x.LastWriteTimeUtc),attributes=x.Attributes.ToString(),link_target=x.LinkTarget
        }),offset,limit,"Folder entries, folders first; contents are not read.");
    }
    static object Details(string path)
    {
        FileSystemInfo item=File.Exists(path)?new FileInfo(path):Directory.Exists(path)?new DirectoryInfo(path):throw new ArgumentException("File or folder not found: "+path);
        FileVersionInfo version=null;
        if(item is FileInfo&&Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".sys" or ".ocx" or ".cpl" or ".scr" or ".msi" or ".mui")
            try{version=FileVersionInfo.GetVersionInfo(path);}catch(FileNotFoundException){}
        return new
        {
            path=item.FullName,type=item is DirectoryInfo?"folder":"file",size_bytes=item is FileInfo file?file.Length:(long?)null,
            created=Time(item.CreationTimeUtc),modified=Time(item.LastWriteTimeUtc),accessed=Time(item.LastAccessTimeUtc),attributes=item.Attributes.ToString(),link_target=item.LinkTarget,
            file_version=Empty(version?.FileVersion),product_version=Empty(version?.ProductVersion),product=Empty(version?.ProductName),company=Empty(version?.CompanyName),description=Empty(version?.FileDescription)
        };
    }
    // One index request for every folder; each folder the index has no size for falls back to Pathweft's cache.
    static async Task<object[]> FolderSizes(string[] paths,CancellationToken cancel)
    {
        var folders=paths.Select(x=>new DirectoryInfo(x)).ToArray();
        if(folders.FirstOrDefault(x=>!x.Exists) is {} missing)throw new ArgumentException("Folder not found: "+missing.FullName);
        string[] keys=folders.Select(x=>Path.TrimEndingDirectorySeparator(x.FullName)).ToArray();
        Dictionary<string,long> sizes=null;string engine=IndexedSearch.SizeSource.Split(' ')[0],index=null;
        try{sizes=await IndexedSearch.FolderSizesAsync(keys,cancel);}
        catch(InvalidOperationException ex){index="Index: "+ex.Message;}
        // Pathweft's own cache, opened read-only; valid only for the folder's current modification time.
        SqliteConnection connection=null;
        try
        {
            var rows=new object[keys.Length];
            for(int i=0;i<keys.Length;i++)
            {
                string key=keys[i];
                if(sizes!=null&&sizes.TryGetValue(key,out long size)){rows[i]=new{path=key,size_bytes=(long?)size,method=engine+" index"};continue;}
                string why=index??engine+" index has no size for this folder";
                if(connection==null&&File.Exists(SizeCache.FilePath))
                {
                    SqliteRuntime.Ensure();
                    connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=SizeCache.FilePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());connection.Open();
                }
                if(connection!=null)
                {
                    using var command=connection.CreateCommand();command.CommandText="SELECT bytes,stamp FROM sizes WHERE path=$path";command.Parameters.AddWithValue("$path",key.ToUpperInvariant());
                    using var reader=command.ExecuteReader();
                    if(reader.Read()&&reader.GetInt64(1)==folders[i].LastWriteTimeUtc.Ticks){rows[i]=new{path=key,size_bytes=(long?)reader.GetInt64(0),method="Pathweft's cached folder size ("+why+")"};continue;}
                }
                rows[i]=new{path=key,size_bytes=(long?)null,method="Unknown: "+why+" and Pathweft has no current cached size. No scan was started."};
            }
            return rows;
        }
        finally{connection?.Dispose();}
    }
    static async Task<object> Search(string query,SearchSwitches switches,string sort,bool descending,bool visibleOnly,int locations,int offset,int limit,CancellationToken cancel)
    {
        if(switches.Regex)_=new Regex(query.StartsWith("regex:",StringComparison.OrdinalIgnoreCase)?query[6..]:query);
        var page=await IndexedSearch.SearchAsync(query,(uint)offset,(uint)limit,sort.ToLowerInvariant(),descending,switches,cancel,visibleOnly,locations);
        return new
        {
            status=Paging(page.Engine+(page.Updating?" · index updating, results may be incomplete.":"."),page.Total,offset,page.Hits.Count,limit),
            // MCP-08: the service's own fields, so a client can tell "no match" from a store still loading.
            updating=page.Updating,updating_stores=page.UpdatingStores??[],elapsed_us=page.ElapsedUs,
            // MCP-09: the same paging fields as every other paged tool.
            total=page.Total,offset,limit,returned=page.Hits.Count,has_more=offset+page.Hits.Count<page.Total,
            next_offset=offset+page.Hits.Count<page.Total?offset+page.Hits.Count:(long?)null,
            rows=page.Hits.Select(x=>new
            {
                path=x.Path,type=x.IsFolder?"folder":"file",size_bytes=x.Size>=0?x.Size:(long?)null,created=Time(x.Created),modified=Time(x.Modified),attributes=x.Attributes is {} a?((FileAttributes)a).ToString():null,visible=x.Visible,
                // Where each matching chunk starts, as the content worker stored it; never the text (IDX-18).
                locations=locations>0&&!x.IsFolder?(x.Locations?.Select(l=>JsonSerializer.Deserialize<JsonElement>(l)).ToArray()??[]):null
            }).ToArray()
        };
    }

    // MCP-08: content index coverage, read-only; folders are added and removed only in Pathweft's Settings.
    static async Task RequireContent(CancellationToken cancel)
    {
        var status=await IndexedSearch.ReadAsync(new{op="status"},"status",cancel);
        if(!status.GetProperty("status").TryGetProperty("protocol",out var protocol)||protocol.GetInt32()<2)throw new InvalidOperationException("This index service has no content index (protocol 1); content: is unavailable.");
    }
    static async Task<object> ContentStatus(CancellationToken cancel)
    {
        await RequireContent(cancel);
        var status=await IndexedSearch.ReadAsync(new{op="content_status"},"content_status",cancel);
        var scopes=await IndexedSearch.ReadAsync(new{op="content_scopes"},"content_scopes",cancel);
        var content=status.GetProperty("content");
        return new
        {
            status=content.GetProperty("scopes").GetArrayLength()==0?"No folder is content-indexed, so content: queries match nothing. Folders are added in Pathweft's Settings.":"Content index coverage per folder; waiting counts pending and stale files without an error. Times are Unix seconds, 0 if never.",
            updating=status.TryGetProperty("updating",out var u)&&u.GetBoolean(),elapsed_us=status.TryGetProperty("elapsed_us",out var e)?e.GetInt64():(long?)null,
            content,scopes=scopes.GetProperty("scopes")
        };
    }
    static async Task<Page> ContentState(string folder,int offset,int limit,CancellationToken cancel)
    {
        if(!Directory.Exists(folder))throw new ArgumentException("Folder not found: "+folder);
        await RequireContent(cancel);
        var reply=await IndexedSearch.ReadAsync(new{op="content_state",folder=Path.TrimEndingDirectorySeparator(folder)},"content_state",cancel);
        return Bound(reply.GetProperty("files").EnumerateArray().Select(x=>(object)x),offset,limit,
            "Content index state of the files directly in this folder (not subfolders): pending, current, stale, failed, unsupported or unavailable; indexed is Unix seconds, 0 if never. Files outside content-indexed folders are not listed.");
    }

    internal static object Definition(string name)=>new
    {
        name,
        description=name switch
        {
            "get_maintenance"=>"Rows of a Maintenance category as Pathweft's Maintenance tab shows them: Startup, Services, Windows tweaks, Windows apps, Security check, or Cleanup (each target's size and file count, from the Pathweft index where possible; target selects one). Read-only; nothing is applied, cleaned or uninstalled.",
            "get_maintenance_changes"=>"Pathweft's Maintenance change journal: the items whose original settings it saved and can restore.",
            "list_folder"=>"Entries of one folder (name, file/folder, size, modified, attributes, link target), folders first. Metadata only.",
            "get_file_details"=>"Size, created/modified/accessed times, attributes, link target and, for executables, version, product, company and description.",
            "get_folder_size"=>"A folder's total size from the Pathweft index or Pathweft's current cached size; never starts a scan, unknown when neither has it. Give path for one folder or paths for up to 1,024 at once.",
            "search_files"=>"Search all indexed files and folders with the Pathweft index service, in Everything-style syntax (wildcards, quotes, AND by space, | OR, ! NOT, < > groups, path:, nopath:, parent:, file:, folder:, ext:, size:, dm:, dc:, case:, ww:, wfn:, regex:). Unsupported functions are refused with a message. Paged: each answer has total, returned, has_more and next_offset (default limit 100, at most 500). updating and updating_stores tell a real no-match from a store still loading or scanning. visible_only keeps only what the signed-in user can open. locations (content: queries) returns where up to that many matching chunks start in each file (line, column and byte offset for text; page, sheet or similar for documents), never the text.",
            "get_index_status"=>"The Pathweft index service's own status: version, protocol, memory, and per store (drive or folder) its kind, state (loading, scanning, reloading, ready, offline, error, waiting-for-user), items, folders, last error and owner. Use it to tell an empty result from an offline or still-loading store.",
            "get_content_status"=>"Content index coverage: per content-indexed folder the files that are current, pending, stale, failed, unsupported or unavailable, the store size and the files waiting, with each folder's added and reconciled times. An empty list means content: queries match nothing. Read-only.",
            "get_content_state"=>"Content index state of each file directly in one folder (not subfolders): pending, current, stale, failed, unsupported or unavailable, when it was last indexed and its error. Paged: total, returned, has_more and next_offset.",
            "get_boots"=>"Recorded Windows starts from Windows' boot diagnostics: seconds to desktop, settled seconds, startup apps, and what slowed it. Defaults to the last 30 days.",
            "get_system_changes"=>"Windows updates and driver/feature changes from Windows' logs plus installed-app changes Pathweft already saved (no new baseline is taken). Defaults to the last 30 days.",
            _=>"Derived findings as in Pathweft's Insights view: memory pressure, large memory holders, sustained CPU, slower Windows startup. Defaults to the last 30 days."
        },
        inputSchema=new{type="object",properties=Allowed[name].ToDictionary(x=>x,x=>(object)(x switch
        {
            "category"=>new{type="string",@enum=Categories},
            "target"=>new{type="string",maxLength=200,description="Cleanup target id, e.g. user-temp"},
            "path" or "folder"=>new{type="string",maxLength=32767,description="Absolute local or UNC path"},
            "paths"=>new{type="array",minItems=1,maxItems=1024,items=new{type="string",maxLength=32767},description="Absolute local or UNC folder paths"},
            "locations"=>new{type="integer",minimum=0,maximum=10,description="Match locations per content hit (default 0)"},
            "query"=>new{type="string",maxLength=8192,description="Everything-style search syntax as accepted by the Pathweft index (docs/index-service/PROTOCOL.md)"},
            "sort"=>new{type="string",@enum=new[]{"name","path","size","extension","created","modified"}},
            "match_path" or "match_case" or "whole_word" or "regex" or "descending" or "visible_only"=>new{type="boolean"},
            "offset"=>new{type="integer",minimum=0,description="Rows to skip (default 0). Continue a listing with the previous answer's next_offset"},
            "limit"=>new{type="integer",minimum=1,maximum=500,description="Rows per page (default 100, at most 500). A full page is not a cap: total, has_more and next_offset tell whether more rows follow"},
            "last_minutes"=>new{type="integer",minimum=1,maximum=43200},
            _=>(object)new{type="string",format="date-time"}
        })),required=name switch{"get_maintenance"=>new[]{"category"},"list_folder" or "get_file_details"=>new[]{"path"},"get_content_state"=>new[]{"folder"},"search_files"=>new[]{"query"},_=>Array.Empty<string>()},additionalProperties=false},
        annotations=new{readOnlyHint=true,destructiveHint=false,idempotentHint=true,openWorldHint=false}
    };
}
