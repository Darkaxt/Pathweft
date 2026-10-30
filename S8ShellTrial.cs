#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FileExplorer;

// Finite local launch-boundary trial. The marker is recognized only by Debug
// copies inside the registered S8 scratch, never by a delivered application.
internal static class S8ShellTrial
{
    internal static bool Enabled=>Program.PortableDir?.StartsWith(@"D:\Temp\pathweft-s8-delivery\shell-trial-",StringComparison.OrdinalIgnoreCase)==true
        &&File.Exists(Path.Combine(Program.PortableDir,"s8-shell-trial.txt"));
    internal static string EventName=>"Local\\Pathweft-S8-Shell-"+Path.GetFileName(Program.PortableDir);
}

public partial class MainWindow
{
    internal void CaptureShellRequest(string request)
    {
        var pane=active;
        string expected=string.IsNullOrEmpty(request)?pane.Dir:File.Exists(request)?Path.GetDirectoryName(Path.GetFullPath(request)):Path.GetFullPath(request);
        bool completed=false;
        void Done()
        {
            if(completed||!pane.Listed||!string.Equals(pane.Dir,expected,StringComparison.OrdinalIgnoreCase))return;
            completed=true;pane.StatsChanged-=Done;
            Dispatcher.BeginInvoke(()=>
            {
                File.WriteAllText(Path.Combine(Program.PortableDir,"shell-receipt.json"),JsonSerializer.Serialize(new
                {
                    pid=Environment.ProcessId,request,directory=pane.Dir,selected=pane.Selected.Select(e=>e.Path).ToArray(),
                    offscreen=HiddenTrialPresentation.IsOutsideDisplays(this),foregroundViolations=HiddenTrialPresentation.ForegroundViolations,
                    native=Settings.NativeTheme,light=Appearance.Light
                }));
                using var signal=EventWaitHandle.OpenExisting(S8ShellTrial.EventName);signal.Set();
            },DispatcherPriority.ApplicationIdle);
        }
        pane.StatsChanged+=Done;Done();
    }
}
#endif
