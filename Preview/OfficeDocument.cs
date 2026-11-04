using System.IO;

namespace FileExplorer.Preview;

// One immutable snapshot, one current worker, and at most one fallback attempt.
// Input/security-policy checks precede the renderer failure boundary.
internal sealed class OfficeDocument : IAsyncDisposable
{
    readonly string identity=Guid.NewGuid().ToString("N");
    string folder,profile;
    readonly SemaphoreSlim serial=new(1,1);
    CancellationTokenSource lifetime;
    OfficeWorkerProcess worker;
    string source,engine,primaryError;
    bool fallbackAttempted,disposed;
    internal OfficeWorker.Information Info {get;private set;}
    internal string[] Sheets=>Info?.Kind=="spreadsheet"?Info.Parts.Select(p=>p.Name).ToArray():Array.Empty<string>();
    internal string Warnings=>string.Join("\n",Info?.Notes??Array.Empty<string>());
    internal string PrimaryFailure=>primaryError;
    internal string RendererDetails=>string.Join("\n",new[]{primaryError==null?null:"Fallback after "+primaryError}.Concat(Info?.Notes??Array.Empty<string>()).Where(s=>!string.IsNullOrWhiteSpace(s)));
#if DEBUG
    internal List<(string Engine,int Pid)> WorkersForVerification {get;}=new();
    internal string SourceForVerification=>source;
    internal int WorkerForVerification=>worker?.ProcessId??0;
    internal bool FallbackForVerification=>fallbackAttempted;
#endif

    static string Primary(string extension)=>extension.ToLowerInvariant() switch
    {
        ".doc" or ".docx" or ".docm" or ".odt"=>"wordcraft",
        ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".ods"=>"gridcraft",
        ".pptx"=>"deckcraft",_=>null
    };

    internal async Task Open(string path,CancellationToken token)
    {
        folder=Path.Combine(DataFolder.Cache,"office-preview",identity);
        // LibreOffice's extension registry appends long component names to its
        // user profile. Keep that owned profile flat under the configured cache,
        // rather than nesting it below the document workspace as well.
        profile=Path.Combine(DataFolder.Cache,"lo-"+identity);
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);
        Directory.CreateDirectory(folder);
        source=Path.Combine(folder,"document"+Path.GetExtension(path));
        await using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true))
        {
            if(input.Length is <1 or >64L*1024*1024)throw new OfficePolicyException("Office input must contain content within the 64 MiB preview limit.");
            await using var output=new FileStream(source,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
            await input.CopyToAsync(output,token);
            // Carry the security marker onto the owned snapshot; never unblock the original.
            FileStream zone=null;
            try{zone=new FileStream(path+":Zone.Identifier",FileMode.Open,FileAccess.Read,FileShare.Read);}
            catch(FileNotFoundException){} // A local document need not have a security marker.
            if(zone!=null)
            {
                using(zone)
                {
                    byte[] marker=PreviewInput.Read(zone,1<<20,token);
                    await File.WriteAllBytesAsync(source+":Zone.Identifier",marker,token);
                }
            }
        }
        token.ThrowIfCancellationRequested();engine=Primary(Path.GetExtension(path));
        if(engine==null){fallbackAttempted=true;engine="libreoffice";await Start();return;}
        try{await Start();}
        catch(Exception error)when(RendererFailure(error)&&!token.IsCancellationRequested){await Fallback(error,token);}
    }

    async Task Start()
    {
        lifetime.Token.ThrowIfCancellationRequested();worker=new OfficeWorkerProcess(folder,lifetime.Token);
#if DEBUG
        WorkersForVerification.Add((engine,worker.ProcessId));
#endif
        Info=(await worker.Request(new("open",Source:source,Engine:engine,Profile:profile),lifetime.Token)).Reply.Info;
        if(Info?.Parts is not {Length:>0}||Info.Parts.Any(p=>p.Pages<(engine=="libreoffice"?0:1)))throw new IOException("Office renderer returned invalid page/sheet information.");
        if(Info.Parts.Length>1000||Info.Parts.Any(p=>p.Pages>100000))throw new OfficePolicyException("Office page/sheet counts exceed preview limits.");
    }

    static bool RendererFailure(Exception error)=>error is not OfficePolicyException&&(error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException);
    async Task Fallback(Exception failure,CancellationToken token)
    {
        if(worker!=null){await worker.DisposeAsync();worker=null;}
        token.ThrowIfCancellationRequested();lifetime.Token.ThrowIfCancellationRequested();
        if(fallbackAttempted)throw new IOException("LibreOffice fallback failed: "+failure.Message,failure);
        primaryError=engine+" failed: "+failure.Message;fallbackAttempted=true;engine="libreoffice";
        try{await Start();}
        catch(Exception error)when(RendererFailure(error)&&!token.IsCancellationRequested)
        {
            if(worker!=null){await worker.DisposeAsync();worker=null;}
            throw new IOException(primaryError+"\nLibreOffice fallback failed: "+error.Message,error);
        }
    }

    internal sealed record PageResult(int Part,int Page,OfficeWorker.Raster Raster);
    internal async Task<PageResult> RenderPage(int part,int page,CancellationToken token)
    {
        await serial.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();lifetime.Token.ThrowIfCancellationRequested();
            if(disposed)throw new ObjectDisposedException(nameof(OfficeDocument));
            if(part<0||part>=Info.Parts.Length||page<0||(Info.Parts[part].Pages>0&&page>=Info.Parts[part].Pages))throw new ArgumentOutOfRangeException(nameof(page));
            string selectedName=Info.Parts[part].Name;
            try{return await Render(part,page,token);}
            catch(Exception error)when(RendererFailure(error)&&!token.IsCancellationRequested&&!lifetime.IsCancellationRequested)
            {
                await Fallback(error,token);
                int match=Array.FindIndex(Info.Parts,p=>p.Name.Equals(selectedName,StringComparison.OrdinalIgnoreCase));
                part=match>=0?match:Math.Min(part,Info.Parts.Length-1);
                if(Info.Parts[part].Pages>0)page=Math.Min(page,Info.Parts[part].Pages-1);
                try{return await Render(part,page,token);}
                catch(Exception fallback)when(RendererFailure(fallback)&&!token.IsCancellationRequested)
                {
                    if(worker!=null){await worker.DisposeAsync();worker=null;}
                    throw new IOException(primaryError+"\nLibreOffice fallback failed: "+fallback.Message,fallback);
                }
            }
        }
        finally{serial.Release();}
    }
    async Task<PageResult> Render(int part,int page,CancellationToken token)
    {
        var result=await worker.Request(new("render",Part:part,Page:page),token);
        if(result.Reply.Info!=null)Info=result.Reply.Info;
        return new(result.Reply.Part,result.Reply.Page,new(result.Reply.Width,result.Reply.Height,result.Pixels));
    }

    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;lifetime?.Cancel();await serial.WaitAsync();
        try
        {
            if(worker!=null){await worker.DisposeAsync();worker=null;}
            lifetime?.Dispose();if(folder!=null&&Directory.Exists(folder))Directory.Delete(folder,true);
            if(profile!=null&&Directory.Exists(profile))Directory.Delete(profile,true);
        }
        finally{serial.Release();serial.Dispose();}
    }
}
