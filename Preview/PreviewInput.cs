using System.IO;

namespace FileExplorer.Preview;

internal static class PreviewInput
{
    internal static byte[] Read(string path,long maximum,CancellationToken token)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return Read(input,maximum,token);
    }
    internal static byte[] Read(Stream input,long maximum,CancellationToken token)
    {
        if(input.CanSeek&&input.Length-input.Position>maximum)throw new IOException($"This preview supports up to {MainWindow.Fmt(maximum)} of content.");
        using var output=new MemoryStream();var buffer=new byte[64*1024];int count;
        while((count=input.Read(buffer,0,buffer.Length))>0)
        {
            token.ThrowIfCancellationRequested();
            if(output.Length+count>maximum)throw new IOException("Preview content exceeds its size limit.");
            output.Write(buffer,0,count);
        }
        return output.ToArray();
    }
}
