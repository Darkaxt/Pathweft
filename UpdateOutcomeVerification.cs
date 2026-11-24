#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

// UPDATE-04: setup's log and the update it was started for are read back on the next start.
public partial class MainWindow
{
    internal async Task VerifyUpdateOutcome68(string root,Action<bool,string> check)
    {
        string[] arguments=Updater.InstallationArguments(123,456,@"D:\Tools\Pathweft\");
        check(arguments.Contains("/LOG="+Updater.SetupLog)&&Path.GetDirectoryName(Updater.SetupLog)==UpdateDownloads.Root,"Setup is started with its log in the updates folder");

        // A file in use, answered Abort because message boxes are suppressed, then the failure Pathweft's setup logs.
        string[] log=
        {
            "2026-11-24 14:41:19.101   Log opened. (Time zone: UTC+01:00)",
            "2026-11-24 14:41:38.410   -- File entry --",
            @"2026-11-24 14:41:38.411   Dest filename: C:\Program Files\Pathweft\Pathweft.dll",
            "2026-11-24 14:41:38.420   DeleteFile: The existing file appears to be in use (5). Retrying.",
            "2026-11-24 14:41:48.500   Defaulting to Abort for suppressed message box (Abort/Retry/Ignore):",
            @"C:\Program Files\Pathweft\Pathweft.dll",
            "",
            "An error occurred while trying to replace the existing file:",
            "DeleteFile failed; code 5.",
            "Access is denied.",
            "2026-11-24 14:41:48.510   User canceled the installation process.",
            "2026-11-24 14:41:48.600   Pathweft update failure: The update could not replace its application files. Close any app using them, then try the update again.",
            "2026-11-24 14:41:48.700   Log closed.",
        };
        string failure=Updater.Failure(log);
        check(failure!=null&&failure.StartsWith("The update could not replace its application files.")&&failure.Contains(@"(C:\Program Files\Pathweft\Pathweft.dll An error occurred while trying to replace the existing file: DeleteFile failed; code 5. Access is denied)"),
            "Setup's reason is the failure it logged with the error behind it: "+failure);
        check(Updater.Failure(log.Take(4).ToArray())==null,"A log without a failure gives no reason");
        string[] raised={"2026-11-24 14:41:48.600   Exception message:","2026-11-24 14:41:48.601   Message box (OK):","Pathweft was installed, but its Win+E helper could not restart.",
            "2026-11-24 14:41:48.700   Pathweft update failure: Pathweft was installed, but its Win+E helper could not restart."};
        check(Updater.Failure(raised)=="Pathweft was installed, but its Win+E helper could not restart.","Setup's own window repeating the failure is not reported as its cause");

        // The update recorded when setup was started, read back once on the next start.
        Directory.CreateDirectory(UpdateDownloads.Root);
        string record=Path.Combine(UpdateDownloads.Root,"started-update.json");
        var newer=new Version(Updater.Current.Major,Updater.Current.Minor,Updater.Current.Build+1);
        File.WriteAllText(record,JsonSerializer.Serialize(new Updater.StartedUpdate(newer.ToString(3),0,0)));
        File.WriteAllLines(Updater.SetupLog,log);
        var outcome=await Updater.TakeOutcomeAsync(CancellationToken.None);
        check(outcome is {Installed:false}&&outcome.Version==newer&&outcome.Failure==failure&&outcome.Log==Updater.SetupLog&&outcome.Excerpt.Contains("Access is denied.")&&!outcome.Excerpt.Contains("14:41:"),
            "An update that left the older version is reported with setup's reason, the end of its log without times, and the log");
        check(!File.Exists(record)&&await Updater.TakeOutcomeAsync(CancellationToken.None)==null,"An outcome is reported once");
        File.WriteAllText(record,JsonSerializer.Serialize(new Updater.StartedUpdate(Updater.Current.ToString(3),0,0)));
        File.WriteAllLines(Updater.SetupLog,log.Take(4).Append("2026-11-24 14:41:50.000   Log closed."));
        check(await Updater.TakeOutcomeAsync(CancellationToken.None)==null&&!File.Exists(record),"An update that installed without a problem reports nothing");
        File.WriteAllText(record,JsonSerializer.Serialize(new Updater.StartedUpdate(Updater.Current.ToString(3),0,0)));
        File.WriteAllLines(Updater.SetupLog,raised);
        var problem=await Updater.TakeOutcomeAsync(CancellationToken.None);
        check(problem is {Installed:true}&&problem.Failure.StartsWith("Pathweft was installed, but its Win+E helper"),"An update installed with a problem is reported as installed");

        var dialog=new UpdateDialog(this);HiddenTrialPresentation.Prepare(dialog);
        try
        {
            dialog.Show();dialog.ShowInstallFailure(outcome);
            await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            string Text(string name)=>((TextBlock)dialog.FindName(name)).Text;
            Button Named(string name)=>(Button)dialog.FindName(name);
            check(dialog.CheckState=="install-failed"&&Text("Heading")==$"Pathweft {newer} wasn't installed"&&Text("Sub")==failure&&Text("Status")=="Setup's log: "+Updater.SetupLog,
                "The update window names the version, setup's reason and its log");
            check(Named("LogButton").IsVisible&&Named("UpdateButton").IsVisible&&(string)Named("UpdateButton").Content=="Try again"&&(string)Named("LaterButton").Content=="Close"&&((TextBox)dialog.FindName("Notes")).Text==outcome.Excerpt,
                "It offers Open log, Try again and Close, and shows the end of setup's log");
            dialog.ShowInstallFailure(problem);
            await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(Text("Heading")==$"Pathweft {Updater.Current} was installed with a problem"&&!Named("UpdateButton").IsVisible,"An installed update with a problem offers no retry");
            dialog.ShowChecking();
            check(!Named("LogButton").IsVisible,"Checking again hides Open log");
            check(HiddenTrialPresentation.ForegroundViolations==0,"The update failure window performs no foreground takeover");
        }
        finally{dialog.Close();File.Delete(Updater.SetupLog);}
    }
}
#endif
