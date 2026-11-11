using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace FileExplorer;

// LAUNCH-01 and ACTIVITY-01: current-user Task Scheduler logon tasks. A logon
// task starts with no delay and at normal priority (the Task Scheduler default
// is below normal), earlier than Explorer's delayed Run entries. Actions name
// only the protected Program Files executable; other copies keep the Run value.
internal static class StartupTasks
{
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run",RunValue="Pathweft";
    static string Sid=>WindowsIdentity.GetCurrent().User.Value;
    internal static string LaunchName=>"Pathweft startup "+Sid;
    internal static string RecorderName=>"Pathweft activity recorder "+Sid;
    internal const string LaunchArguments="--tray",RecorderArguments="--activity-recorder";
    static string RunCommand=>$"\"{Environment.ProcessPath}\" --tray";

    internal static bool ProtectedInstall
    {
        get
        {
            if(Settings.Profile!=null||Program.PortableDir!=null)return false;
            string root=Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))+"\\";
            return Program.Executable.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&File.Exists(Program.Executable);
        }
    }
    internal const string UnprotectedLaunch="This copy is not the Program Files installation, so it starts through a standard sign-in entry, which Windows runs later than a scheduled task.";
    internal const string UnprotectedRecorder="App activity recording needs the Program Files installation: the elevated task may only start a protected executable.";

    // ---- Start with Windows ----
    internal static bool StartWithWindows
    {
        get
        {
            if(ProtectedInstall&&Owned(LaunchName,LaunchArguments)is true)return true;
            using var k=Registry.CurrentUser.OpenSubKey(RunKey);return k?.GetValue(RunValue) is string;
        }
        set
        {
            if(Settings.Profile!=null)return;
            if(value&&ProtectedInstall){Register(LaunchName,LaunchArguments,elevated:false);RemoveRunValue();return;}
            if(value){using var k=Registry.CurrentUser.CreateSubKey(RunKey);k.SetValue(RunValue,RunCommand);return;}
            RemoveTask(LaunchName,LaunchArguments);RemoveRunValue();
        }
    }
    static void RemoveRunValue()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RunKey,true);
        if(key?.GetValue(RunValue) as string==RunCommand)key.DeleteValue(RunValue,false);
    }
    /// An installed copy started by an earlier Run value moves to the logon task once.
    internal static void MigrateRunValue()
    {
        if(!ProtectedInstall)return;
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){if(key?.GetValue(RunValue) as string!=RunCommand)return;}
        Register(LaunchName,LaunchArguments,elevated:false);RemoveRunValue();
    }
    /// Uninstall: every startup entry this executable owns. The recorder task needs the uninstaller's elevation.
    internal static void RemoveOwned()
    {
        if(Settings.Profile!=null)return;
        RemoveRunValue();RemoveTask(LaunchName,LaunchArguments);
        StopRecorder();
        try{RemoveTask(RecorderName,RecorderArguments);}
        catch(UnauthorizedAccessException){}
    }

    // ---- Activity recorder (elevated; called from the administrator session or the uninstaller) ----
    internal static bool? RecorderRegistered=>ProtectedInstall?Owned(RecorderName,RecorderArguments):false;
    internal static void SetRecorder(bool on)
    {
        if(!ProtectedInstall)throw new InvalidOperationException(UnprotectedRecorder);
        if(on)
        {
            Register(RecorderName,RecorderArguments,elevated:true);
            using var session=new MaintenanceStartup.TaskSession();dynamic task=session.Own(session.Root.GetTask(RecorderName));
            if((int)task.State!=4)session.Own(task.Run(null)); // 4 = running; start now instead of at the next sign-in
        }
        else{StopRecorder();RemoveTask(RecorderName,RecorderArguments);}
    }
    internal static string StopEventName=>"Local\\Pathweft-ActivityStop-"+Sid;
    internal static string RecorderMutexName=>"Local\\Pathweft-Activity-"+Sid;
    internal static void StopRecorder()
    {
        if(!EventWaitHandle.TryOpenExisting(StopEventName,out var stop))return;
        using(stop)stop.Set();
        // The recorder flushes its last batch and releases its mutex as it exits;
        // the executable is then free for update or removal.
        if(!Mutex.TryOpenExisting(RecorderMutexName,out var running))return;
        using(running)
        {
            try{if(running.WaitOne(15000))running.ReleaseMutex();}
            catch(AbandonedMutexException){running.ReleaseMutex();}
        }
    }
    internal static void ResumeRecorder()
    {
        if(RecorderRegistered!=true)return;
        try{using var session=new MaintenanceStartup.TaskSession();dynamic task=session.Own(session.Root.GetTask(RecorderName));if((int)task.State!=4)session.Own(task.Run(null));}
        catch(Exception error)when(error is COMException or UnauthorizedAccessException){} // starts at the next sign-in
    }

    // ---- Task Scheduler ----
    /// true: present with this executable's exact action; false: absent; null: present but unreadable or foreign.
    internal static bool? Owned(string name,string arguments)
    {
        try
        {
            using var session=new MaintenanceStartup.TaskSession();dynamic task=session.Own(session.Root.GetTask(name));
            dynamic actions=session.Own(session.Own(task.Definition).Actions);
            if((int)actions.Count!=1)return null;dynamic action=session.Own(actions[1]);
            return (int)action.Type==0&&string.Equals((string)action.Path,Program.Executable,StringComparison.OrdinalIgnoreCase)&&(string)action.Arguments==arguments?true:null;
        }
        catch(COMException error)when(error.HResult==unchecked((int)0x80070002)){return false;}
        catch(UnauthorizedAccessException){return null;}
        catch(COMException){return null;} // access denied or Task Scheduler unavailable: unknown, never assumed ours
    }
    static void Register(string name,string arguments,bool elevated)
    {
        using var session=new MaintenanceStartup.TaskSession();
        // 6 = create or update, 3 = interactive token: runs in the signed-in session, no stored password.
        session.Own(session.Root.RegisterTask(name,Definition(name,arguments,elevated),6,null,null,3,null));
    }
    static void RemoveTask(string name,string arguments)
    {
        if(Owned(name,arguments)!=true)return; // never delete a task this executable does not own
        using var session=new MaintenanceStartup.TaskSession();session.Root.DeleteTask(name,0);
    }
    internal static string Definition(string name,string arguments,bool elevated)
    {
        string sid=SecurityElement.Escape(Sid),exe=SecurityElement.Escape(Program.Executable);
        string description=elevated?"Records app activity for Pathweft's performance history (Settings > Record app activity).":"Starts Pathweft in the notification area at sign-in (Settings > Start with Windows).";
        // No delay, priority 4 (normal CPU, I/O and memory priority), one instance, no time limit:
        // both run for the signed-in session and end at sign-out.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>Pathweft</Author><Description>{SecurityElement.Escape(description)}</Description><URI>\{SecurityElement.Escape(name)}</URI></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{sid}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>{(elevated?"HighestAvailable":"LeastPrivilege")}</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>{(elevated?"true":"false")}</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>{SecurityElement.Escape(arguments)}</Arguments></Exec></Actions>
            </Task>
            """;
    }
}
