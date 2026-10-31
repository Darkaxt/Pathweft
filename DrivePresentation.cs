namespace FileExplorer;

internal static class DrivePresentation
{
    internal static double UsedPercent(long total,long free)=>total>0?Math.Clamp(100.0*(total-free)/total,0,100):0;
    internal static int Warning(long total,long free)=>total<=0?0:UsedPercent(total,free)>=90?2:UsedPercent(total,free)>=80?1:0;
    internal static string FreeText(long total,long free)=>total>0?$"{MainWindow.Fmt(free)} ({Math.Clamp(100.0*free/total,0,100):0}%)":"";
}
