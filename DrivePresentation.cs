namespace FileExplorer;

internal static class DrivePresentation
{
    internal static double UsedPercent(long total,long free)=>total>0?Math.Clamp(100.0*(total-free)/total,0,100):0;
    internal static int Warning(long total,long free)=>total<=0?0:UsedPercent(total,free)>=90?2:UsedPercent(total,free)>=80?1:0;
    internal static string Size(long value,bool cloud=false)
    {
        if(!cloud)return MainWindow.Fmt(value);
        return value>=1099511627776L?$"{value/1099511627776.0:0.#} TiB":$"{value/1073741824.0:0.#} GiB";
    }
    internal static string FreeText(long total,long free,bool cloud=false)=>total>0?$"{Size(free,cloud)} ({Math.Clamp(100.0*free/total,0,100):0}%)":"";
    internal static string Capacity(long total,long free,bool cloud=false)=>total>0?$"{Size(free,cloud)} free of {Size(total,cloud)}":cloud?"Cloud quota unavailable":"No media or unavailable";
    internal static string CloudSource=>"Cloud account quota from Google Drive’s local metadata; refreshed by Google Drive.";
}
