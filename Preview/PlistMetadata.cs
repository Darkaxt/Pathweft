// Binary/XML plist inspection for IPA and signed provisioning metadata.
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileExplorer.Preview;

internal static class PlistMetadata
{
    internal static Dictionary<string,object> Read(byte[] bytes)
    {
        if(bytes.Length>16*1024*1024)throw new InvalidDataException("Property list exceeds16MiB.");
        if(bytes.AsSpan().StartsWith("bplist00"u8))return new BinaryPlist(bytes).Read();
        using var input=new MemoryStream(bytes,false);
        using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Ignore,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});
        var document=XDocument.Load(reader);
        object Parse(XElement item,int depth)
        {
            if(depth>32)throw new InvalidDataException("Property list nesting exceeds32 levels.");
            if(item.Name.LocalName=="dict")
            {
                var children=item.Elements().Take(20_001).ToArray();if(children.Length>20_000||children.Length%2!=0)throw new InvalidDataException("Invalid or oversized plist dictionary.");
                var values=new Dictionary<string,object>(StringComparer.Ordinal);
                for(int i=0;i<children.Length;i+=2){if(children[i].Name.LocalName!="key")throw new InvalidDataException("Invalid plist dictionary key.");values.Add(children[i].Value,Parse(children[i+1],depth+1));}return values;
            }
            if(item.Name.LocalName=="array")return item.Elements().Take(10_000).Select(value=>Parse(value,depth+1)).ToArray();
            return item.Name.LocalName switch{"true"=>"True","false"=>"False",_=>item.Value};
        }
        return Parse(document.Root?.Elements().FirstOrDefault()??throw new InvalidDataException("No plist dictionary."),0) as Dictionary<string,object>??throw new InvalidDataException("Expected a plist dictionary.");
    }
    internal static string Value(object value)=>value switch{object[] list=>string.Join(", ",list.Select(Value)),Dictionary<string,object> map=>string.Join(", ",map.Select(p=>p.Key+": "+Value(p.Value))),byte[] data=>$"{data.Length:N0} bytes",_=>Convert.ToString(value)};

    sealed class BinaryPlist(byte[] data)
    {
        int referenceSize,offsetSize,budget;ulong count,top,table;
        readonly HashSet<ulong> ancestry=new();
        ulong Number(long offset,int size)
        {
            if(size<1||size>8||offset<0||offset>data.Length-size)throw new InvalidDataException("Invalid binary plist number.");
            ulong result=0;for(int i=0;i<size;i++)result=(result<<8)|data[offset+i];return result;
        }
        internal Dictionary<string,object> Read()
        {
            if(data.Length<40)throw new InvalidDataException("Truncated binary plist.");
            offsetSize=data[^26];referenceSize=data[^25];count=Number(data.Length-24,8);top=Number(data.Length-16,8);table=Number(data.Length-8,8);
            if(count==0||count>100_000||offsetSize<1||offsetSize>8||referenceSize<1||referenceSize>8||table>(ulong)data.Length-32||count* (ulong)offsetSize>(ulong)data.Length-32-table)throw new InvalidDataException("Invalid binary plist trailer.");
            return Object(top,0) as Dictionary<string,object>??throw new InvalidDataException("Expected a plist dictionary.");
        }
        object Object(ulong index,int depth)
        {
            if(index>=count||depth>32||++budget>50_000||!ancestry.Add(index))throw new InvalidDataException("Invalid or excessively nested binary plist object.");
            try
            {
                ulong offset=Number(checked((long)(table+index*(ulong)offsetSize)),offsetSize);if(offset>=table)throw new InvalidDataException("Plist object leaves its object table.");
                int marker=data[offset],kind=marker>>4;long at=(long)offset+1;ulong length=(uint)(marker&15);
                if(length==15&&kind is 4 or 5 or 6 or 10 or 13)
                {int integer=data[at++];if(integer>>4!=1||(integer&15)>3)throw new InvalidDataException("Invalid plist object length.");length=Number(at,1<<(integer&15));at+=1<<(integer&15);}
                if(length>1_048_576)throw new InvalidDataException("Plist object exceeds its preview budget.");
                long Required(ulong items,int size){ulong bytes=items*(ulong)size;if((ulong)at>table||bytes>table-(ulong)at)throw new InvalidDataException("Plist object leaves its content table.");return (long)bytes;}
                if(kind==0)return marker==9?"True":marker==8?"False":"";
                if(kind==1){int size=1<<(marker&15);return Number(at,size).ToString();}
                if(kind==4){Required(length,1);return data.AsSpan((int)at,(int)length).ToArray();}
                if(kind is 5 or 6){Required(length,kind==6?2:1);return (kind==6?Encoding.BigEndianUnicode:Encoding.ASCII).GetString(data,(int)at,(int)length*(kind==6?2:1));}
                if(kind==10){if(length>10_000)throw new InvalidDataException("Oversized plist array.");Required(length,referenceSize);return Enumerable.Range(0,(int)length).Select(i=>Object(Number(at+i*referenceSize,referenceSize),depth+1)).ToArray();}
                if(kind==13)
                {
                    if(length>10_000)throw new InvalidDataException("Oversized plist dictionary.");Required(length*2,referenceSize);var values=new Dictionary<string,object>();
                    for(int i=0;i<(int)length;i++)values.Add(Convert.ToString(Object(Number(at+i*referenceSize,referenceSize),depth+1)),Object(Number(at+((long)length+i)*referenceSize,referenceSize),depth+1));return values;
                }
                if(kind is 2 or 3)
                {
                    int size=1<<(marker&15);Required((ulong)size,1);
                    double value=size==8?BitConverter.Int64BitsToDouble((long)Number(at,8)):size==4?BitConverter.Int32BitsToSingle((int)Number(at,4)):throw new InvalidDataException("Invalid plist floating-point size.");
                    return kind==3?new DateTimeOffset(2001,1,1,0,0,0,TimeSpan.Zero).AddSeconds(value).ToString("O"):value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return "Unsupported plist value";
            }
            finally{ancestry.Remove(index);}
        }
    }
}
