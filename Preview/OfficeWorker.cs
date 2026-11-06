using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FileExplorer.Preview;

// Same packaged executable, selected before WPF starts. This process owns the
// native model and raster memory; only bounded replies cross to the browser.
internal static class OfficeWorker
{
    internal sealed record Part(string Name,int Pages);
    internal sealed record Information(string Engine,string Kind,Part[] Parts,string[] Notes);
    internal sealed record Request(string Action,string Source=null,string Engine=null,int Part=0,int Page=0,int Width=1400,string Profile=null,ulong Unit=0,ulong Offset=0,int Limit=16384);
    internal sealed record TextCursor(ulong Unit,ulong Offset);
    internal sealed record TextRecord(JsonElement Location,ulong Offset,string Text);
    internal sealed record TextBatch(string Status,string Coverage,TextRecord[] Records,TextCursor Next);
    internal sealed record Reply(bool Ok,string Error=null,Information Info=null,int Width=0,int Height=0,int Bytes=0,int Part=0,int Page=0,string Code=null);
    internal static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    const int HeaderLimit=64<<10;

    internal static async Task<int> Run()
    {
        using var input=Console.OpenStandardInput();
        using var output=Console.OpenStandardOutput();
        NativeDocument document=null;
        LibreOfficeDocument fallback=null;
        FileStream sourcePin=null;
        try
        {
            while(await ReadHeader(input) is {} header)
            {
                var request=JsonSerializer.Deserialize<Request>(header,Json)??throw new IOException("Empty Office worker request.");
                if(request.Action=="close")return 0;
                if(request.Action=="open")
                {
                    if(document!=null||fallback!=null)throw new IOException("The Office worker already owns a document.");
                    if(request.Engine is not ("wordcraft" or "gridcraft" or "deckcraft" or "photocraft" or "vectorcraft" or "figcraft" or "pdfcraft" or "libreoffice"))throw new IOException("Unknown document engine.");
                    sourcePin=new FileStream(request.Source,FileMode.Open,FileAccess.Read,FileShare.Read);
                    if(sourcePin.Length is <1 or >64L*1024*1024)throw new OfficePolicyException("Office input exceeds the preview limit.");
                    if(request.Engine=="libreoffice")
                    {
                        fallback=new LibreOfficeDocument(Path.GetDirectoryName(request.Source));
                        await Write(output,new Reply(true,Info:await fallback.Open(request.Source,request.Profile)));continue;
                    }
                    byte[] bytes=PreviewInput.Read(sourcePin,64L<<20,CancellationToken.None);
                    document=new NativeDocument(request.Engine,bytes,Path.GetExtension(request.Source).TrimStart('.').ToLowerInvariant());
                    await Write(output,new Reply(true,Info:document.Info));
                }
                else if(request.Action=="render"&&(document!=null||fallback!=null))
                {
                    var raster=fallback!=null?await fallback.Render(request.Part,request.Page,request.Width):await document.Render(request.Part,request.Page,request.Width);
                    await Write(output,new Reply(true,Info:fallback?.Info??document.Info,Width:raster.Width,Height:raster.Height,Bytes:raster.Pixels.Length,Part:request.Part,Page:fallback?.LastPage??request.Page));
                    await output.WriteAsync(raster.Pixels);await output.FlushAsync();
                }
                else if(request.Action=="extract"&&(document!=null||fallback!=null))
                {
                    if(request.Limit is <1024 or >48*1024)throw new OfficePolicyException("Invalid extraction reply limit.");
                    byte[] text=document!=null?document.Extract(request.Unit,request.Offset,request.Limit):JsonSerializer.SerializeToUtf8Bytes(new TextBatch("unsupported","LibreOffice recovery rendering does not provide the Craft text API.",Array.Empty<TextRecord>(),null),Json);
                    await Write(output,new Reply(true,Bytes:text.Length));
                    await output.WriteAsync(text);await output.FlushAsync();
                }
                else throw new IOException("Invalid Office worker state or command.");
            }
            return 0; // Parent closed its only request handle.
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or System.ComponentModel.Win32Exception)
        {
            try{await Write(output,new Reply(false,Error:error.Message,Code:error is OfficePolicyException?"policy":"renderer"));}catch(IOException){}
            return 1;
        }
        finally{document?.Dispose();if(fallback!=null)await fallback.DisposeAsync();sourcePin?.Dispose();}
    }

    internal static async Task<string> ReadHeader(Stream stream)
    {
        using var bytes=new MemoryStream();var one=new byte[1];
        while(await stream.ReadAsync(one)==1)
        {
            if(one[0]==10)return Encoding.UTF8.GetString(bytes.GetBuffer(),0,(int)bytes.Length);
            if(bytes.Length>=HeaderLimit)throw new OfficePolicyException("Office worker header exceeds the protocol limit.");
            bytes.WriteByte(one[0]);
        }
        if(bytes.Length!=0)throw new IOException("Office worker returned an incomplete header.");
        return null;
    }

    internal static async Task Write<T>(Stream stream,T value)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,Json)+"\n");
        if(bytes.Length>HeaderLimit)throw new OfficePolicyException("Office worker metadata exceeds the protocol limit.");
        await stream.WriteAsync(bytes);await stream.FlushAsync();
    }

    internal sealed record Raster(int Width,int Height,byte[] Pixels);

    sealed class LibreOfficeDocument:IAsyncDisposable
    {
        readonly OfficeWorkerProcess process;
        Information info;
        PdfDocument pdf;
        MemoryStream bytes;
        IRandomAccessStream stream;
        int currentPart=-1;
        internal Information Info=>info;
        internal int LastPage {get;private set;}
        internal LibreOfficeDocument(string folder)=>process=new OfficeWorkerProcess(folder,CancellationToken.None,true);
        internal async Task<Information> Open(string source,string profile)
        {
            info=(await process.Request(new("open",Source:source,Engine:"libreoffice",Profile:profile),CancellationToken.None)).Reply.Info;
            if(info?.Parts is not {Length:>0})throw new IOException("Invalid fallback page/sheet information.");
            if(info.Parts.Length>1000||info.Parts.Any(p=>p.Pages is <0 or >100000))throw new OfficePolicyException("Fallback page/sheet information exceeds preview limits.");
            return info;
        }
        internal async Task<Raster> Render(int part,int page,int width)
        {
            if(part<0||part>=info.Parts.Length||page<0||width is <64 or >4096)throw new IOException("Invalid fallback page request.");
            if(currentPart!=part)
            {
                var result=await process.Request(new("render",Part:part,Page:page,Width:width),CancellationToken.None);
                pdf=null;stream?.Dispose();bytes?.Dispose();
                bytes=new MemoryStream(result.Pixels,false);stream=bytes.AsRandomAccessStream();
                pdf=await PdfDocument.LoadFromStreamAsync(stream);
                if(pdf.PageCount<1)throw new IOException("Invalid fallback display list page count.");
                if(pdf.PageCount>100000)throw new OfficePolicyException("Fallback display list exceeds the page limit.");
                info.Parts[part]=info.Parts[part] with {Pages=(int)pdf.PageCount};
                currentPart=part;
            }
            LastPage=Math.Min(page,(int)pdf.PageCount-1);
            return await PdfRaster(pdf,LastPage,width);
        }
        public async ValueTask DisposeAsync()
        {pdf=null;stream?.Dispose();bytes?.Dispose();await process.DisposeAsync();}
    }

    static async Task<Raster> PdfRaster(PdfDocument document,int index,int width)
    {
        using var page=document.GetPage((uint)index);
        if(page.Size.Width<=0||page.Size.Height<=0)throw new IOException("Invalid Office page geometry.");
        uint target=(uint)Math.Max(1,Math.Min(width,4096*page.Size.Width/page.Size.Height));
        using var rendered=new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered,new PdfPageRenderOptions{DestinationWidth=target});
        rendered.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(rendered);
        if(decoder.PixelWidth is <1 or >4096||decoder.PixelHeight is <1 or >4096)throw new IOException("Invalid Office raster geometry.");
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Premultiplied,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        return new((int)decoder.PixelWidth,(int)decoder.PixelHeight,data.DetachPixelData());
    }

    sealed class NativeDocument:IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate uint Abi();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Open(IntPtr bytes,nuint length,IntPtr extension,nuint extensionLength);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr Text(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr ErrorText();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr ExtractText(IntPtr document,ulong unit,nuint offset,nuint limit);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int RenderPage(IntPtr document,uint part,uint page,uint width);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr FramePointer(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Close(IntPtr document);
        [StructLayout(LayoutKind.Sequential)]struct Frame{internal uint Width,Height,Format;internal nuint Length;internal IntPtr Pixels;}
        IntPtr library,document;
        FileStream libraryPin;
        RenderPage render;
        FramePointer frame;
        Close close;
        ErrorText error;
        ExtractText extract;
        PdfDocument pdf;
        MemoryStream pdfBytes;
        IRandomAccessStream pdfStream;
        int pdfPart=-1;
        internal Information Info {get;}
        T Export<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));

        internal NativeDocument(string engine,byte[] bytes,string extension)
        {
            try
            {
                string module=Path.Combine(AppContext.BaseDirectory,"Office","cores",engine,"pathweft_"+engine+".dll");
                using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Office","runtime-manifest.json")));
                var pin=manifest.RootElement.GetProperty("apps").EnumerateArray().Single(app=>app.GetProperty("id").GetString()==engine);
                libraryPin=new FileStream(module,FileMode.Open,FileAccess.Read,FileShare.Read);
                string hash=Convert.ToHexString(SHA256.HashData(libraryPin));
                if(!hash.Equals(pin.GetProperty("nativeSha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new IOException("The packaged Office core differs from its verified version.");
                // Its app-local dependencies are resolved beside this exact DLL,
                // the app, or Windows; the working directory is not a DLL source.
                library=LoadLibraryEx(module,IntPtr.Zero,0x1100);
                if(library==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if(Export<Abi>("pw_office_abi")()!=1)throw new IOException("Incompatible Office native bridge.");
                render=Export<RenderPage>("pw_office_render");frame=Export<FramePointer>("pw_office_frame");close=Export<Close>("pw_office_close");error=Export<ErrorText>("pw_office_error");
                extract=Export<ExtractText>("pw_office_extract");
                var input=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                var ext=GCHandle.Alloc(Encoding.UTF8.GetBytes(extension),GCHandleType.Pinned);
                try{document=Export<Open>("pw_office_open")(input.AddrOfPinnedObject(),(nuint)bytes.Length,ext.AddrOfPinnedObject(),(nuint)Encoding.UTF8.GetByteCount(extension));}
                finally{ext.Free();input.Free();}
                if(document==IntPtr.Zero)throw Failure();
                string metadata=Marshal.PtrToStringUTF8(Export<Text>("pw_office_info")(document))??throw new IOException("Office core returned no metadata.");
                if(metadata.Length>HeaderLimit/2)throw new OfficePolicyException("Office metadata exceeds the protocol limit.");
                Info=JsonSerializer.Deserialize<Information>(metadata,Json)??throw new IOException("Office metadata is invalid.");
                if(Info.Parts is not {Length:>0}||Info.Parts.Any(p=>p.Pages<1))throw new IOException("Invalid Office page/sheet counts.");
                if(Info.Parts.Length>1000||Info.Parts.Any(p=>p.Pages>100000))throw new OfficePolicyException("Office page/sheet counts exceed preview limits.");
            }
            catch{Dispose();throw;}
        }
        IOException Failure()=>new(Marshal.PtrToStringUTF8(error())??"Native Office rendering failed.");
        internal byte[] Extract(ulong unit,ulong offset,int limit)
        {
            if(offset>nuint.MaxValue)throw new OfficePolicyException("Extraction cursor exceeds the native address range.");
            var address=extract(document,unit,(nuint)offset,(nuint)limit);
            if(address==IntPtr.Zero)throw Failure();
            // The hash-pinned bridge guarantees this C string is bounded. Still
            // reject a mismatched implementation before putting it on the pipe.
            byte[] bytes=Encoding.UTF8.GetBytes(Marshal.PtrToStringUTF8(address)??throw new IOException("No extraction reply."));
            if(bytes.Length>limit)throw new OfficePolicyException("Extraction reply exceeds its requested limit.");
            return bytes;
        }
        internal async Task<Raster> Render(int part,int page,int width)
        {
            if(part<0||part>=Info.Parts.Length||page<0||page>=Info.Parts[part].Pages||width is <64 or >4096)throw new IOException("Invalid Office page request.");
            if(pdf!=null&&pdfPart==part)return await RenderPdf(page,width);
            if(render(document,(uint)part,(uint)page,(uint)width)!=0)throw Failure();
            IntPtr address=frame(document);
            if(address==IntPtr.Zero)throw new IOException("Office core returned no raster.");
            var value=Marshal.PtrToStructure<Frame>(address);
            if(value.Length is 0 or >64*1024*1024||value.Pixels==IntPtr.Zero)throw new IOException("Office core returned an invalid buffer.");
            if(value.Format==0&&(value.Width is <1 or >4096||value.Height is <1 or >4096||value.Length!=(nuint)((ulong)value.Width*value.Height*4)))throw new IOException("Office core returned an invalid raster.");
            byte[] pixels=new byte[(int)value.Length];Marshal.Copy(value.Pixels,pixels,0,pixels.Length);
            if(value.Format==1)
            {
                pdf=null;pdfStream?.Dispose();pdfBytes?.Dispose();
                pdfBytes=new MemoryStream(pixels,false);pdfStream=pdfBytes.AsRandomAccessStream();
                pdf=await PdfDocument.LoadFromStreamAsync(pdfStream);
                if(pdf.PageCount!=Info.Parts[part].Pages)throw new IOException("Office worksheet page count differs from its renderer.");
                pdfPart=part;return await RenderPdf(page,width);
            }
            if(value.Format!=0)throw new IOException("Unknown Office core frame format.");
            return new((int)value.Width,(int)value.Height,pixels);
        }
        async Task<Raster> RenderPdf(int index,int width)
        {
            return await PdfRaster(pdf,index,width);
        }
        public void Dispose()
        {
            pdf=null;pdfStream?.Dispose();pdfBytes?.Dispose();pdfStream=null;pdfBytes=null;
            if(document!=IntPtr.Zero){close(document);document=IntPtr.Zero;}
            // The donor owns process-wide font caches and raster worker threads.
            // Keep its module mapped until this worker exits; unloading code while
            // those threads still exist would invalidate their return addresses.
            if(library==IntPtr.Zero){libraryPin?.Dispose();libraryPin=null;}
            library=IntPtr.Zero;
        }
        [DllImport("kernel32.dll",EntryPoint="LoadLibraryExW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    }
}
