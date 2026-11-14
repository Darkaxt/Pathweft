#if DEBUG
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace FileExplorer;
public partial class MainWindow
{
    // ACCESS-08: the task definition and the standard side's refusal of a foreign pipe client.
    // No real task is registered or run: the run is substituted and the helper is an impostor.
    internal async Task VerifyAutomaticAdministrator08(Action<bool,string> check)
    {
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        var session=XDocument.Parse(StartupTasks.Definition(StartupTasks.SessionName,StartupTasks.SessionArguments,elevated:true,trigger:false));
        var recorder=XDocument.Parse(StartupTasks.Definition(StartupTasks.RecorderName,StartupTasks.RecorderArguments,elevated:true));
        string Value(XDocument task,string name)=>task.Descendants(ns+name).Single().Value;
        check(!session.Descendants(ns+"Triggers").Any()&&Value(session,"RunLevel")=="HighestAvailable"&&Value(session,"MultipleInstancesPolicy")=="Parallel"&&Value(session,"Hidden")=="true"&&
              Value(session,"Command")==Program.Executable&&Value(session,"Arguments")=="--administrator-session $(Arg0) $(Arg1) $(Arg2)",
              "The administrator task starts only the installed executable on demand, elevated, with the owner identity substituted at run time");
        check(recorder.Descendants(ns+"LogonTrigger").Count()==1&&Value(recorder,"MultipleInstancesPolicy")=="IgnoreNew","The activity recorder keeps its sign-in trigger and single instance");

        int launches=0;Process impostor=null;
        AdministratorSession.LaunchForVerification=_=>{Interlocked.Increment(ref launches);throw new Win32Exception(1223);};
        AdministratorSession.TaskRegisteredForVerification=true;
        AdministratorSession.TaskRunForVerification=arguments=>
        {
            // Another program of the same user connects to the session pipe first.
            string script=$"$p=[System.IO.Pipes.NamedPipeClientStream]::new('.','{arguments[0]}','InOut');$p.Connect(20000);$p.ReadByte()|Out-Null";
            impostor=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),["-NoProfile","-NonInteractive","-Command",script]){UseShellExecute=false,CreateNoWindow=true});
            return impostor!=null;
        };
        try
        {
            bool silent=await AdministratorSession.Enable(prompt:false);
            check(!silent&&!AdministratorSession.Available&&launches==0&&impostor!=null,"A pipe client that is not the installed executable is refused, and a sign-in start does not fall back to a prompt");
            impostor?.WaitForExit(20000);
            check(impostor?.HasExited==true,"The refused client is disconnected");
            impostor?.Dispose();impostor=null;
            bool prompted=await AdministratorSession.Enable();
            check(!prompted&&!AdministratorSession.Available&&launches==1,"An interactive start falls back to the one Windows prompt when the task route fails");
        }
        finally
        {
            AdministratorSession.TaskRunForVerification=null;AdministratorSession.TaskRegisteredForVerification=null;
            AdministratorSession.LaunchForVerification=null;
            if(impostor is {HasExited:false}){impostor.Kill();impostor.WaitForExit();}
            impostor?.Dispose();
        }
    }
}
#endif
