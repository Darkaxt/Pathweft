namespace FileExplorer;

internal static class DrivePresentation
{
    static readonly System.Windows.Media.Brush Healthy=MainWindow.Hex("#0078D4"),Amber=MainWindow.Hex("#D99024"),Red=MainWindow.Hex("#E5484D");
    internal static System.Windows.Media.Brush Fill(double usedPercent)=>usedPercent>=90?Red:usedPercent>=80?Amber:Healthy;
    internal static System.Windows.Media.Brush Fill(long total,long free)=>Fill(UsedPercent(total,free));
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
