// Embedded preview entry selection adapted from QuickLook ThumbnailViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Preview;

internal sealed class DesignThumbnailViewer:IViewer
{
    const long ImageLimit=32L*1024*1024;
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject context)=>context.Title=Path.GetFileName(path)+" · Design preview";
    public async Task View(string path,ContextObject context)
    {
        var bitmap=await Task.Run(()=>Decode(Read(path,context.Token),context.Token),context.Token);
        context.Token.ThrowIfCancellationRequested();
        var image=new Image{Source=bitmap,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(12)};
        System.Windows.Automation.AutomationProperties.SetName(image,"Embedded design preview");context.ViewerContent=image;
    }
    internal static BitmapSource Decode(byte[] bytes,CancellationToken token)
    {
        using var stream=new MemoryStream(bytes,false);token.ThrowIfCancellationRequested();
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);var frame=decoder.Frames[0];
        if((long)frame.PixelWidth*frame.PixelHeight>100_000_000)throw new IOException("Embedded design preview exceeds100 megapixels.");
        token.ThrowIfCancellationRequested();stream.Position=0;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;
        if(frame.PixelWidth>2048)image.DecodePixelWidth=2048;
        image.EndInit();image.Freeze();return image;
    }
    internal static byte[] Read(string path,CancellationToken token)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(extension==".pdn")
        {
            // Paint.NET header: PDN plus 24-bit little-endian XML length.
            Span<byte> header=stackalloc byte[6];input.ReadExactly(header);
            if(header[0]!='P'||header[1]!='D'||header[2]!='N')throw new InvalidDataException("Not a Paint.NET document.");
            int length=header[3]|header[4]<<8|header[5]<<16;
            if(length>4*1024*1024||length>input.Length-input.Position)throw new InvalidDataException("Invalid Paint.NET preview header.");
            byte[] xml=new byte[length];input.ReadExactly(xml);
            using var reader=XmlReader.Create(new MemoryStream(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
            string encoded=XDocument.Load(reader).Root?.Element("custom")?.Element("thumb")?.Attribute("png")?.Value;
            if(string.IsNullOrEmpty(encoded))throw new IOException("This document has no embedded preview.");
            return Convert.FromBase64String(encoded);
        }
        using var archive=new ZipArchive(input,ZipArchiveMode.Read,false);
        bool Matches(string name)=>extension switch
        {
            ".cdr"=>name.Equals("previews/thumbnail.png",StringComparison.OrdinalIgnoreCase),
            ".fig"=>name.Equals("thumbnail.png",StringComparison.OrdinalIgnoreCase),
            ".kra"=>name.Equals("mergedimage.png",StringComparison.OrdinalIgnoreCase)||name.Equals("preview.png",StringComparison.OrdinalIgnoreCase),
            ".pip" or ".pix"=>name.EndsWith(".thumb.png",StringComparison.OrdinalIgnoreCase),
            ".sketch"=>name.EndsWith("previews/preview.png",StringComparison.OrdinalIgnoreCase),
            ".xd"=>name.Equals("preview.png",StringComparison.OrdinalIgnoreCase)||name.Equals("thumbnail.png",StringComparison.OrdinalIgnoreCase),
            ".xmind"=>name.Equals("Thumbnails/thumbnail.png",StringComparison.OrdinalIgnoreCase),_=>false
        };
        if(archive.Entries.Count>100_000)throw new IOException("Design package has too many entries for preview.");
        var entry=archive.Entries.FirstOrDefault(e=>Matches(e.FullName.Replace('\\','/')));
        if(entry==null)throw new IOException("This document has no supported embedded preview.");
        if(entry.Length>ImageLimit)throw new IOException("Embedded preview exceeds32MiB.");
        using var stream=entry.Open();return PreviewInput.Read(stream,ImageLimit,token);
    }
    public void Cleanup(){}
}
