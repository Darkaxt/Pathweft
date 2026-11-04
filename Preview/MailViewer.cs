using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MimeKit;
using OpenMcdf;

namespace FileExplorer.Preview;

internal sealed record MailPreviewData(string Subject,string From,string To,string Date,string Html,string[] Attachments);

internal sealed class MailViewer(Entry entry):IViewer
{
    static MailViewer()=>Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    HtmlViewer html;
#if DEBUG
    internal HtmlViewer HtmlForVerification=>html;
#endif
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject context)=>context.Title=entry.Name+" · Mail";
    public async Task View(string path,ContextObject context)
    {
        var mail=await Task.Run(()=>Read(path,context.Token),context.Token);context.Token.ThrowIfCancellationRequested();
        var panel=new DockPanel();var header=new StackPanel{Margin=new Thickness(12)};DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        foreach(var (label,value) in new[]{("Subject",mail.Subject),("From",mail.From),("To",mail.To),("Date",mail.Date),("Attachments",string.Join(" · ",mail.Attachments))})
        {
            if(string.IsNullOrWhiteSpace(value))continue;
            var text=new TextBox{Text=label+": "+value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent,Padding=new Thickness(0,2,0,2),MaxHeight=90,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            text.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");System.Windows.Automation.AutomationProperties.SetName(text,label);header.Children.Add(text);
        }
        var body=new ContentControl();panel.Children.Add(body);context.ViewerContent=panel;
        html=new HtmlViewer(entry,new HtmlResources(Encoding.UTF8.GetBytes(mail.Html),context.Token),"Mail");
        var child=new ContextObject(context.Token,()=>context.IsCurrent,content=>body.Content=content,context.Track,false);
        html.Prepare(path,child);await html.View(path,child);
    }
    internal static MailPreviewData Read(string path,CancellationToken token)
    {
        byte[] bytes=PreviewInput.Read(path,32L*1024*1024,token);
        return Path.GetExtension(path).Equals(".eml",StringComparison.OrdinalIgnoreCase)?ReadMime(bytes,token):ReadMsg(bytes,token);
    }
    static string Plain(string text)=>"<!doctype html><html><head><meta charset=utf-8></head><body><pre style='white-space:pre-wrap;overflow-wrap:anywhere'>"+WebUtility.HtmlEncode(text)+"</pre></body></html>";
    static MailPreviewData ReadMime(byte[] bytes,CancellationToken token)
    {
        using var input=new MemoryStream(bytes,false);using var message=MimeMessage.Load(input,token);
        if(!message.Headers.Any())throw new InvalidDataException("No MIME message headers were found.");
        string body=message.HtmlBody??Plain(message.TextBody??"No readable message body.");
        int embedded=0;
        foreach(var part in message.BodyParts.OfType<MimePart>().Take(1000))
        {
            token.ThrowIfCancellationRequested();
            if(part.ContentId==null||part.Content==null||!part.ContentType.MediaType.Equals("image",StringComparison.OrdinalIgnoreCase)||!body.Contains("cid:"+part.ContentId,StringComparison.OrdinalIgnoreCase))continue;
            using var decoded=new MemoryStream();part.Content.DecodeTo(decoded,token);
            if(decoded.Length>8*1024*1024||embedded+decoded.Length>8*1024*1024)continue;
            embedded+=(int)decoded.Length;body=body.Replace("cid:"+part.ContentId,"data:"+part.ContentType.MimeType+";base64,"+Convert.ToBase64String(decoded.ToArray()),StringComparison.OrdinalIgnoreCase);
        }
        string[] attachments=message.Attachments.Take(1000).Select(part=>part.ContentDisposition?.FileName??(part as MimePart)?.FileName??"Attached message").ToArray();
        return new(message.Subject,message.From.ToString(),message.To.ToString(),message.Date.ToString("g"),body,attachments);
    }
    static MailPreviewData ReadMsg(byte[] bytes,CancellationToken token)
    {
        using var input=new MemoryStream(bytes,false);using var storage=RootStorage.Open(input);
        string Text(Storage source,string id)
        {
            string name="__substg1.0_"+id+"001F";bool unicode=source.ContainsEntry(name);
            if(!unicode)name="__substg1.0_"+id+"001E";
            if(!source.ContainsEntry(name))return "";
            using var stream=source.OpenStream(name);var data=PreviewInput.Read(stream,4*1024*1024,token);
            return(unicode?Encoding.Unicode:Encoding.GetEncoding(1252)).GetString(data).TrimEnd('\0');
        }
        string subject=Text(storage,"0037"),from=Text(storage,"0C1A"),email=Text(storage,"0C1F"),to=Text(storage,"0E04");
        if(email.Length>0)from+=" <"+email+">";
        string body;
        if(storage.ContainsEntry("__substg1.0_10130102"))
        {using var stream=storage.OpenStream("__substg1.0_10130102");byte[] data=PreviewInput.Read(stream,16*1024*1024,token);body=Encoding.UTF8.GetString(data);}
        else if(storage.ContainsEntry("__substg1.0_10090102"))
        {using var stream=storage.OpenStream("__substg1.0_10090102");body=RtfPipe.Rtf.ToHtml(Encoding.ASCII.GetString(DecompressRtf(PreviewInput.Read(stream,16*1024*1024,token),token)));}
        else body=Plain(Text(storage,"1000"));
        var attachments=new List<string>();
        foreach(var item in storage.EnumerateEntries().Take(4096))
        {
            token.ThrowIfCancellationRequested();if(!item.Name.StartsWith("__attach_version1.0_",StringComparison.Ordinal))continue;
            var attachment=storage.OpenStorage(item.Name);string name=Text(attachment,"3707");if(name.Length==0)name=Text(attachment,"3704");attachments.Add(name.Length>0?name:"Unnamed attachment");
        }
        string date="";
        if(storage.ContainsEntry("__properties_version1.0"))
        {
            using var properties=storage.OpenStream("__properties_version1.0");var data=PreviewInput.Read(properties,1024*1024,token);
            for(int at=32;at+16<=data.Length;at+=16)
                if(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) is 0x00390040 or 0x0E060040)
                {try{date=DateTime.FromFileTimeUtc(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at+8))).ToLocalTime().ToString("g");}catch(ArgumentOutOfRangeException){}break;}
        }
        return new(subject,from,to,date,body,attachments.ToArray());
    }
    internal static byte[] DecompressRtf(byte[] data,CancellationToken token)
    {
        if(data.Length<16)throw new InvalidDataException("Truncated compressed RTF body.");
        uint size=BinaryPrimitives.ReadUInt32LittleEndian(data),length=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)),magic=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if(size!=data.Length-4||length>16*1024*1024)throw new InvalidDataException("Invalid compressed RTF size.");
        uint expected=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        if(magic==0x414c454d){if(length>data.Length-16||expected!=0)throw new InvalidDataException("Invalid uncompressed RTF body.");return data.AsSpan(16,(int)length).ToArray();}
        if(magic!=0x75465a4c)throw new InvalidDataException("Unknown RTF compression format.");
        // MS-OXRTFCP uses the reflected CRC32 polynomial with zero initial
        // state and no final complement, unlike the usual ZIP CRC32 API.
        uint crc=0;
        foreach(byte value in data.AsSpan(16))
        {token.ThrowIfCancellationRequested();crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
        if(crc!=expected)throw new InvalidDataException("Compressed RTF checksum does not match its body.");
        byte[] dictionary=new byte[4096];byte[] initial=Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\mac\\deff0\\deftab720{\\fonttbl;}{\\f0\\fnil \\froman \\fswiss \\fmodern \\fscript \\fdecor MS Sans SerifSymbolArialTimes New RomanCourier{\\colortbl\\red0\\green0\\blue0\r\n\\par \\pard\\plain\\f0\\fs20\\b\\i\\u\\tab\\tx");
        initial.CopyTo(dictionary,0);int write=initial.Length,read=16,output=0;byte[] result=new byte[length];
        while(output<result.Length)
        {
            token.ThrowIfCancellationRequested();if(read>=data.Length)throw new InvalidDataException("Truncated compressed RTF tokens.");int flags=data[read++];
            for(int bit=0;bit<8&&output<result.Length;bit++)
            {
                if(read>=data.Length)throw new InvalidDataException("Truncated compressed RTF token.");
                if((flags&(1<<bit))==0){byte value=data[read++];result[output++]=value;dictionary[write++&4095]=value;}
                else
                {
                    if(read+1>=data.Length)throw new InvalidDataException("Truncated compressed RTF reference.");int offset=data[read]<<4|data[read+1]>>4,count=(data[read+1]&15)+2;read+=2;
                    if(offset==(write&4095))throw new InvalidDataException("RTF stream ended before its declared body size.");
                    for(int i=0;i<count&&output<result.Length;i++){byte value=dictionary[offset++&4095];result[output++]=value;dictionary[write++&4095]=value;}
                }
            }
        }
        return result;
    }
    public void Cleanup(){html?.Cleanup();html=null;}
}
