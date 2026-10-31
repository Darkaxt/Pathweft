using System.Text.Json;
using System.Windows;

namespace FileExplorer;

// Internal drops carry archive identities straight to the existing transfer
// queue. A Windows consumer can still request ordinary extracted file paths,
// materialized only at that synchronous OLE data boundary, never before drag.
internal sealed class ArchiveDragData : IDataObject
{
    const string Format="Pathweft.ArchiveEntries";
    readonly DataObject metadata=new();
    readonly Lazy<string[]> external;
    internal ArchiveDragData(string[] paths,Func<string[]> export)
    {
        metadata.SetData(Format,JsonSerializer.Serialize(paths),false);
        external=new(export);
    }
    internal static string[] Files(IDataObject data)
    {
        if(data.GetDataPresent(Format,false))
        {
            try{return JsonSerializer.Deserialize<string[]>(data.GetData(Format,false) as string??"");}
            catch(JsonException){return null;}
        }
        return data.GetData(DataFormats.FileDrop) as string[];
    }
    internal static bool HasFiles(IDataObject data)=>data.GetDataPresent(Format,false)||data.GetDataPresent(DataFormats.FileDrop);
    public object GetData(string format,bool autoConvert)=>format==DataFormats.FileDrop?external.Value:metadata.GetData(format,autoConvert);
    public object GetData(string format)=>GetData(format,true);
    public object GetData(Type format)=>format==typeof(string[])?external.Value:metadata.GetData(format);
    public bool GetDataPresent(string format,bool autoConvert)=>format==DataFormats.FileDrop||metadata.GetDataPresent(format,autoConvert);
    public bool GetDataPresent(string format)=>GetDataPresent(format,true);
    public bool GetDataPresent(Type format)=>format==typeof(string[])||metadata.GetDataPresent(format);
    public string[] GetFormats(bool autoConvert)=>metadata.GetFormats(autoConvert).Append(DataFormats.FileDrop).Distinct().ToArray();
    public string[] GetFormats()=>GetFormats(true);
    public void SetData(string format,object data,bool autoConvert)=>metadata.SetData(format,data,autoConvert);
    public void SetData(string format,object data)=>metadata.SetData(format,data);
    public void SetData(Type format,object data)=>metadata.SetData(format,data);
    public void SetData(object data)=>metadata.SetData(data);
}
