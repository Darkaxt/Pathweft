using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Css.Dom;
using Ganss.Xss;

namespace FileExplorer.Preview;

// A preview origin serves only this document's checked static resources. It is
// not a folder mapping, file:// navigation or a native application bridge.
internal sealed class HtmlResources
{
    internal sealed record Resource(byte[] Bytes,string Mime);
    const long DocumentLimit=16*1024*1024,ResourceLimit=64*1024*1024,Budget=128*1024*1024;
    readonly string folder,archive,documentPath;
    readonly CancellationToken token;
    readonly byte[] generatedDocument;
    readonly Dictionary<string,Task<Resource>> cache=new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim reading=new(1,1);
    Dictionary<string,Archive.Raw> entries;
    long consumed;
    internal Uri Document {get;}
    internal string Host=>Document.Host;
    internal HtmlResources(string original,string extracted,CancellationToken cancellation)
    {
        token=cancellation;
        if(PaneView.ArchiveOf(original) is {Inner.Length:>0} at)
        {archive=at.Archive;documentPath=Archive.Clean(at.Inner);}
        else{folder=Path.GetDirectoryName(Path.GetFullPath(extracted));documentPath=Path.GetFileName(extracted);}
        Document=new("https://"+Guid.NewGuid().ToString("N")+".preview.pathweft.invalid/"+string.Join('/',documentPath.Split('/').Select(Uri.EscapeDataString)));
    }
    internal HtmlResources(byte[] markup,CancellationToken cancellation)
    {
        token=cancellation;generatedDocument=markup;documentPath="message.html";
        Document=new("https://"+Guid.NewGuid().ToString("N")+".preview.pathweft.invalid/message.html");
    }
    internal static HtmlResources CompiledHelp(string container,string document,CancellationToken cancellation)=>new(container,document,cancellation,true);
    HtmlResources(string container,string document,CancellationToken cancellation,bool compiledHelp)
    {
        token=cancellation;archive=container;documentPath=Archive.Clean(document);
        Document=new("https://"+Guid.NewGuid().ToString("N")+".preview.pathweft.invalid/"+string.Join('/',documentPath.Split('/').Select(Uri.EscapeDataString)));
    }
    internal bool LocalDocument(Uri uri)=>uri.Scheme==Uri.UriSchemeHttps&&uri.Host==Host&&uri.IsDefaultPort&&uri.UserInfo==""&&uri.Query==""&&Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() is ".html" or ".htm";
    internal bool SameDocument(Uri uri)=>uri.Scheme==Uri.UriSchemeHttps&&uri.Host==Host&&uri.IsDefaultPort&&uri.UserInfo==""&&uri.AbsolutePath==Document.AbsolutePath&&uri.Query=="";
    internal Task<Resource> Read(Uri uri)
    {
        if(uri.Scheme!=Uri.UriSchemeHttps||uri.Host!=Host||!uri.IsDefaultPort||uri.UserInfo!="")throw new IOException("Online content is disabled in HTML preview.");
        string relative=Archive.Clean(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
        string mime=relative.Equals(documentPath,StringComparison.OrdinalIgnoreCase)?"text/html":Path.GetExtension(relative).ToLowerInvariant() switch
        {
            ".css"=>"text/css", ".png"=>"image/png", ".jpg" or ".jpeg"=>"image/jpeg", ".gif"=>"image/gif",
            ".webp"=>"image/webp", ".bmp"=>"image/bmp", ".ico"=>"image/x-icon", ".svg"=>"image/svg+xml",
            ".woff"=>"font/woff", ".woff2"=>"font/woff2", ".ttf"=>"font/ttf", ".otf"=>"font/otf",
            _=>throw new IOException("This resource is not part of the static HTML preview.")
        };
        lock(cache)
        {
            if(cache.TryGetValue(relative,out var existing))return existing;
            var result=ReadChecked(relative,mime);cache.Add(relative,result);return result;
        }
    }
    async Task<Resource> ReadChecked(string relative,string mime)
    {
        await reading.WaitAsync(token);
        try
        {
            return await Task.Run(()=>
            {
                token.ThrowIfCancellationRequested();long limit=Math.Min(mime=="text/html"?DocumentLimit:ResourceLimit,Budget-consumed);
                byte[] bytes;
                if(generatedDocument!=null)
                {
                    if(!relative.Equals(documentPath,StringComparison.Ordinal))throw new IOException("Only the selected message body is available.");
                    bytes=generatedDocument;if(bytes.LongLength>limit)throw new IOException("Message body exceeds its preview budget.");
                }
                else if(archive!=null)
                {
                    entries??=Archive.Read(archive).ToDictionary(item=>item.Path,StringComparer.OrdinalIgnoreCase);
                    if(!entries.TryGetValue(relative,out var entry)||entry.IsDir)throw new FileNotFoundException("HTML resource is absent from its archive.");
                    if(entry.Size>limit)throw new IOException("The HTML preview resource exceeds its size budget.");
                    using var memory=new MemoryStream();Archive.CopyEntry(archive,entry,memory);token.ThrowIfCancellationRequested();bytes=memory.ToArray();
                }
                else
                {
                    string path=Path.GetFullPath(Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar)));
                    string prefix=Path.EndsInDirectorySeparator(folder)?folder:folder+Path.DirectorySeparatorChar;
                    if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("HTML resource leaves its document folder.");
                    for(string current=path;current!=null;current=Path.GetDirectoryName(current))
                        if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("HTML preview does not follow filesystem links.");
                    // Resolve the drive/share alias, not the document directory:
                    // resolving the latter could bless a substituted directory.
                    // All remaining components must still match the checked path.
                    string root=Path.GetPathRoot(path);
                    using var volume=CreateFile(root,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
                    if(volume.IsInvalid)throw new IOException("Cannot verify the HTML resource volume.",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                    string expected=FinalPath(volume).TrimEnd('\\')+"\\"+path[root.Length..];
                    using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    // Check the opened handle too; a concurrent directory replacement
                    // must not turn a previously checked resource into an outside file.
                    if(!FinalPath(input.SafeFileHandle).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("HTML resource resolved outside its checked path.");
                    if(input.Length>limit)throw new IOException("The HTML preview resource exceeds its size budget.");
                    using var memory=new MemoryStream();var buffer=new byte[64*1024];int count;
                    while((count=input.Read(buffer))!=0){token.ThrowIfCancellationRequested();if(memory.Length+count>limit)throw new IOException("The HTML preview resource grew beyond its size budget.");memory.Write(buffer,0,count);}
                    bytes=memory.ToArray();
                }
                long originalLength=bytes.Length;
                if(mime is "text/html" or "text/css")bytes=StaticMarkup(bytes,mime,new Uri(Document,"/"+string.Join('/',relative.Split('/').Select(Uri.EscapeDataString))));
                if(bytes.LongLength>limit)throw new IOException("Sanitized HTML exceeds its resource budget.");
                consumed+=Math.Max(originalLength,bytes.LongLength);
                return new Resource(bytes,mime+((mime is "text/html" or "text/css")?"; charset=utf-8":""));
            },token);
        }
        finally{reading.Release();}
    }
    byte[] StaticMarkup(byte[] bytes,string mime,Uri source)
    {
        // Parse without a loader or script engine. Remove remote URLs before
        // Chromium's speculative scanner sees them, including CSS url() values.
        var sanitizer=new HtmlSanitizer();
        sanitizer.AllowedTags.UnionWith(new[]{"style","link"});
        sanitizer.AllowedAttributes.UnionWith(new[]{"class","id","rel"});
        foreach(string attribute in new[]{"srcset","ping","srcdoc","action","formaction"})sanitizer.AllowedAttributes.Remove(attribute);
        sanitizer.AllowedAtRules.Add(CssRuleType.FontFace);
        sanitizer.AllowedCssProperties.UnionWith(new[]{"src","unicode-range","font-display"});
        sanitizer.AllowedSchemes.Clear();sanitizer.AllowedSchemes.Add("https");sanitizer.AllowedSchemes.Add("data");
        sanitizer.FilterUrl+=(_,args)=>
        {
            if(!Uri.TryCreate(args.SanitizedUrl,UriKind.Absolute,out var uri)||
                !(uri.Scheme=="https"&&uri.Host==Host&&uri.IsDefaultPort&&uri.UserInfo==""||
                  uri.Scheme=="data"&&(uri.OriginalString.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)||uri.OriginalString.StartsWith("data:font/",StringComparison.OrdinalIgnoreCase))))args.SanitizedUrl=null;
        };
        using var input=new MemoryStream(bytes,false);
        using var document=sanitizer.HtmlParserFactory().ParseDocument(mime=="text/html"?input:new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><html><head></head><body></body>")));
        if(mime=="text/css")
        {
            using var reader=new StreamReader(input,Encoding.UTF8,true);var style=document.CreateElement("style");style.TextContent=reader.ReadToEnd();document.Head.AppendChild(style);
        }
        sanitizer.SanitizeDom(document,baseUrl:source.AbsoluteUri);
        foreach(var link in document.QuerySelectorAll("link").Where(link=>!string.Equals(link.GetAttribute("rel"),"stylesheet",StringComparison.OrdinalIgnoreCase)).ToArray())link.Remove();
        string result=mime=="text/html"?document.ToHtml():string.Join('\n',document.QuerySelectorAll("style").Select(style=>style.TextContent));
        return Encoding.UTF8.GetBytes(result);
    }
    static string FinalPath(SafeFileHandle file)
    {
        var name=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(file,name,(uint)name.Capacity,0);
        if(length==0||length>=name.Capacity)throw new IOException("Cannot verify the HTML resource path.");
        string final=name.ToString();
        if(final.StartsWith(@"\\?\UNC\",StringComparison.Ordinal))return @"\\"+final[8..];
        return final.StartsWith(@"\\?\",StringComparison.Ordinal)?final[4..]:final;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder name,uint size,uint flags);
}
