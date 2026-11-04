// Delimiter selection/reader approach adapted from QuickLook CsvViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace FileExplorer.Preview;

internal sealed class CsvViewer(MainWindow owner):IViewer
{
    internal const long ByteLimit=16L*1024*1024;
    internal const int RowLimit=50_000,ColumnLimit=256;
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject context)=>context.Title=Path.GetFileName(path)+" · Delimited table";
    public async Task View(string path,ContextObject context)
    {
        var table=await Task.Run(()=>Read(path,context.Token),context.Token);
        context.Token.ThrowIfCancellationRequested();context.ViewerContent=new TabularPreview(owner,table,true);
    }
    public void Cleanup(){}
    internal static PreviewTable Read(string path,CancellationToken token)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(file.Length>ByteLimit)throw new IOException("Delimited-table preview supports files up to16MiB.");
        using var snapshot=new MemoryStream();byte[] buffer=new byte[64*1024];int count;
        while((count=file.Read(buffer))>0){token.ThrowIfCancellationRequested();if(snapshot.Length+count>ByteLimit)throw new IOException("Table grew beyond its16MiB preview limit.");snapshot.Write(buffer,0,count);}
        snapshot.Position=0;
        using var reader=new StreamReader(snapshot,Encoding.UTF8,true);
        string delimiter=Path.GetExtension(path).ToLowerInvariant() switch{".tsv"=>"\t",".psv"=>"|",_=>null};
        var config=new CsvConfiguration(CultureInfo.InvariantCulture){DetectDelimiter=delimiter==null,DetectDelimiterValues=new[]{",",";","\t","|"},HasHeaderRecord=false,IgnoreBlankLines=true,MaxFieldSize=1_048_576};
        if(delimiter!=null)config.Delimiter=delimiter;
        using var parser=new CsvParser(reader,config);
        var rows=new List<string[]>();int columns=0;bool truncated=false;
        while(parser.Read())
        {
            token.ThrowIfCancellationRequested();
            if(parser.Count>ColumnLimit)throw new IOException("Delimited-table preview supports up to256 columns.");
            if(rows.Count==RowLimit+1){truncated=true;break;}
            string[] row=parser.Record.ToArray();columns=Math.Max(columns,row.Length);rows.Add(row);
        }
        if(rows.Count==0)return new PreviewTable(Array.Empty<string>(),Array.Empty<string[]>(),"Empty delimited file.");
        var padded=rows.Select(row=>Enumerable.Range(0,columns).Select(i=>i<row.Length?row[i]:"").ToArray()).ToArray();
        return new PreviewTable(padded[0],padded.Skip(1).ToArray(),truncated?$"Preview limited to the first {RowLimit:N0} data rows.":"");
    }
}
