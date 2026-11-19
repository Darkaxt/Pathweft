using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace FileExplorer;

// BROWSE-09: Pathweft's own WebDAV client (RFC 4918 subset: PROPFIND depth 1, GET, PUT, MKCOL,
// MOVE, DELETE) on HttpClient. TLS uses Windows' validation; there is no bypass. Basic
// credentials are sent only over https, or to the local test endpoint.
internal sealed class WebDavFileSystem : IRemoteFileSystem
{
    static readonly XNamespace Dav="DAV:";
    readonly RemoteConnection connection;readonly HttpClient http;readonly Uri root;
    public RemoteConnection Connection=>connection;
    internal WebDavFileSystem(RemoteConnection connection,string secret)
    {
        this.connection=connection;
        bool secure=connection.Kind=="webdavs";
        root=new UriBuilder(secure?"https":"http",connection.Host,connection.EffectivePort,"/").Uri;
        http=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false,PooledConnectionLifetime=TimeSpan.FromMinutes(5)}){BaseAddress=root,Timeout=Timeout.InfiniteTimeSpan};
        if(connection.User.Length>0)
        {
            bool local=connection.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
            if(!secure&&!local)throw new InvalidOperationException("WebDAV sign-in needs webdavs:// (https); Pathweft does not send a password over plain http.");
            http.DefaultRequestHeaders.Authorization=new("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.User+":"+(secret??""))));
        }
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pathweft","1"));
    }

    static string Escape(string path)=>string.Join("/",RemotePath.Normalize(path).Split('/').Select(Uri.EscapeDataString));
    Uri UriOf(string path,bool folder=false){string escaped=Escape(path);if(folder&&!escaped.EndsWith('/'))escaped+="/";return new Uri(root,escaped);}

    static async Task Check(HttpResponseMessage response,string what)
    {
        if(response.IsSuccessStatusCode)return;
        // a refused sign-in is its own kind, so the caller can ask for the password again
        if(response.StatusCode==HttpStatusCode.Unauthorized){_=await response.Content.ReadAsByteArrayAsync();throw new UnauthorizedAccessException($"WebDAV {what}: the server refused the user name or password.");}
        string reason=response.StatusCode switch
        {
            HttpStatusCode.Unauthorized=>"the server refused the user name or password",
            HttpStatusCode.Forbidden=>"access is denied",
            HttpStatusCode.NotFound=>"it does not exist",
            HttpStatusCode.PreconditionFailed=>"the destination already exists",
            HttpStatusCode.Conflict=>"its parent folder does not exist",
            HttpStatusCode.InsufficientStorage=>"the server has no space left",
            _=>$"{(int)response.StatusCode} {response.ReasonPhrase}",
        };
        _=await response.Content.ReadAsByteArrayAsync();
        throw new IOException($"WebDAV {what}: {reason}.");
    }

    public async Task Connect(CancellationToken cancel)=>await List("/",cancel);

    public async Task<List<RemoteItem>> List(string path,CancellationToken cancel)
    {
        path=RemotePath.Normalize(path);
        using var request=new HttpRequestMessage(new HttpMethod("PROPFIND"),UriOf(path,true));
        request.Headers.Add("Depth","1");
        request.Content=new StringContent("<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>",Encoding.UTF8,"application/xml");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
        await Check(response,"listing "+path);
        var document=XDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var items=new List<RemoteItem>();
        foreach(var entry in document.Root?.Elements(Dav+"response")??[])
        {
            string href=entry.Element(Dav+"href")?.Value;if(href==null)continue;
            string itemPath=RemotePath.Normalize(Uri.UnescapeDataString(Uri.TryCreate(href,UriKind.Absolute,out var absolute)?absolute.AbsolutePath:href.Split('?')[0]));
            if(itemPath.Equals(path,StringComparison.Ordinal))continue; // the folder itself
            var prop=entry.Elements(Dav+"propstat").Where(x=>x.Element(Dav+"status")?.Value.Contains(" 200 ")!=false).Select(x=>x.Element(Dav+"prop")).FirstOrDefault(x=>x!=null);
            bool folder=prop?.Element(Dav+"resourcetype")?.Element(Dav+"collection")!=null;
            long size=long.TryParse(prop?.Element(Dav+"getcontentlength")?.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long length)?length:-1;
            var modified=DateTimeOffset.TryParse(prop?.Element(Dav+"getlastmodified")?.Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var when)?when.UtcDateTime:default;
            items.Add(new(RemotePath.Name(itemPath),itemPath,folder,folder?-1:size,modified));
        }
        return items;
    }

    public async Task Download(string remotePath,string localFile,IProgress<long> progress,CancellationToken cancel)
    {
        string partial=localFile+".pathweft-part";
        try
        {
            using(var response=await http.GetAsync(UriOf(remotePath),HttpCompletionOption.ResponseHeadersRead,cancel))
            {
                await Check(response,"download of "+remotePath);
                await using var input=await response.Content.ReadAsStreamAsync(cancel);
                await using var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1<<16,true);
                var buffer=new byte[1<<16];long done=0;int read;
                while((read=await input.ReadAsync(buffer,cancel))>0){await output.WriteAsync(buffer.AsMemory(0,read),cancel);done+=read;progress?.Report(done);}
            }
            File.Move(partial,localFile,true);
        }
        finally{if(File.Exists(partial))File.Delete(partial);}
    }

    sealed class ProgressContent(Stream source,IProgress<long> progress,CancellationToken cancel) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext context)
        {
            var buffer=new byte[1<<16];long done=0;int read;
            while((read=await source.ReadAsync(buffer,cancel))>0){await stream.WriteAsync(buffer.AsMemory(0,read),cancel);done+=read;progress?.Report(done);}
        }
        protected override bool TryComputeLength(out long length){length=source.Length;return true;}
    }

    public async Task Upload(string localFile,string remotePath,bool replace,IProgress<long> progress,CancellationToken cancel)
    {
        if(!replace)
        {
            // If-None-Match below is ignored by some servers (rclone among them): ask first, so nothing is overwritten
            using var probe=new HttpRequestMessage(HttpMethod.Head,UriOf(remotePath));
            using var found=await http.SendAsync(probe,cancel);
            if(found.StatusCode==HttpStatusCode.Unauthorized)await Check(found,"upload to "+remotePath);
            if(found.IsSuccessStatusCode)throw new IOException($"WebDAV upload to {remotePath}: the destination already exists.");
        }
        await using var input=new FileStream(localFile,FileMode.Open,FileAccess.Read,FileShare.Read,1<<16,true);
        using var request=new HttpRequestMessage(HttpMethod.Put,UriOf(remotePath)){Content=new ProgressContent(input,progress,cancel)};
        request.Content.Headers.ContentType=new("application/octet-stream");
        if(!replace)request.Headers.TryAddWithoutValidation("If-None-Match","*");
        using var response=await http.SendAsync(request,cancel);
        await Check(response,"upload to "+remotePath);
    }

    public async Task CreateFolder(string path,CancellationToken cancel)
    {
        using var request=new HttpRequestMessage(new HttpMethod("MKCOL"),UriOf(path,true));
        using var response=await http.SendAsync(request,cancel);
        if(response.StatusCode==HttpStatusCode.MethodNotAllowed)return; // already exists
        await Check(response,"new folder "+path);
    }

    // WebDAV DELETE of a collection is recursive by definition; a non-recursive request refuses a non-empty folder first.
    public async Task Delete(string path,bool isDir,bool recursive,CancellationToken cancel)
    {
        if(isDir&&!recursive&&(await List(path,cancel)).Count>0)throw new IOException($"{path} is not empty.");
        using var request=new HttpRequestMessage(HttpMethod.Delete,UriOf(path,isDir));
        using var response=await http.SendAsync(request,cancel);
        await Check(response,"delete of "+path);
    }

    public async Task Move(string from,string to,CancellationToken cancel)
    {
        using var request=new HttpRequestMessage(new HttpMethod("MOVE"),UriOf(from));
        request.Headers.Add("Destination",UriOf(to).AbsoluteUri);request.Headers.Add("Overwrite","F");
        using var response=await http.SendAsync(request,cancel);
        await Check(response,"move of "+from);
    }

    public void Dispose()=>http.Dispose();
}
