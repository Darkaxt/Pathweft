using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace FileExplorer;

// A second launch (a folder double-click, the Start menu, Win+E without the agent) only hands its folder to the running
// window. Done here, before WPF and App.xaml are loaded, that costs 86 ms instead of 185 (190 instead of 381 on a
// throttled laptop-like CPU). Switches (--agent, --selftest, …) always take the full start.
static class Program
{
    internal static string Executable=>Path.Combine(AppContext.BaseDirectory,"Pathweft.exe");
    /// The folder of a portable copy: the exe's own, when a portable.txt sits beside it (the portable zip has one).
    /// Before PipeName, which reads it: static fields start in the order they are written.
    internal static readonly string PortableDir = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt")) ? AppContext.BaseDirectory.TrimEnd('\\') : null;

    // a test copy (PATHWEFT_PROFILE) gets a single instance per profile folder, so two test copies don't hand off to each
    // other. Read from the environment here, not from Settings, whose types would load WPF.
    // A portable copy (portable.txt beside the exe) is its own instance too: started while the installed one runs, it
    // would otherwise hand its start over and never open.
    internal static readonly string PipeName = "Pathweft-" + Environment.UserName
        + ((Environment.GetEnvironmentVariable("PATHWEFT_PROFILE") is { Length: > 0 } p ? p : PortableDir) is { } own
            ? "-profile-" + System.IO.Hashing.XxHash32.HashToUInt32(System.Text.Encoding.UTF8.GetBytes(own.ToLowerInvariant())).ToString("x8") : "");

    [STAThread]
    static void Main(string[] args)
    {
        // Five arguments from the UAC launch; four from the on-demand task, which only starts the installed copy (no profile).
        if((args.Length is 4 or 5)&&args[0]=="--administrator-session")
        {Environment.ExitCode=AdministratorSession.Run(args[1],int.Parse(args[2]),long.Parse(args[3]),args.Length==5?args[4]:"").GetAwaiter().GetResult();return;}
        if(args.Length==4&&args[0]=="--maintenance-worker")
        {Environment.ExitCode=MaintenanceWorker.Run(args[1],int.Parse(args[2]),long.Parse(args[3])).GetAwaiter().GetResult();return;}
        if((args.Length is 3 or 4)&&args[0]=="--manager-clean-worker")
        {Environment.ExitCode=ManagerCleanup.RunWorker(args[1],args[2],args.Length==4?args[3]:"").GetAwaiter().GetResult();return;}
        if(args.Length==1&&args[0]=="--activity-recorder")
        {Environment.ExitCode=ActivityRecorder.Run();return;}
        if(args.Length==1&&args[0]=="--performance-mcp")
        {Environment.ExitCode=PerformanceMcp.Run().GetAwaiter().GetResult();return;}
        if(args.Length==4&&args[0]=="--performance-io-worker")
        {Environment.ExitCode=PerformanceIoWorker.Run(args[1],int.Parse(args[2]),long.Parse(args[3])).GetAwaiter().GetResult();return;}
        if(args.Length==1&&args[0]=="--office-preview-worker")
        {Environment.ExitCode=Preview.OfficeWorker.Run().GetAwaiter().GetResult();return;}
        if(args.FirstOrDefault(a=>a.StartsWith("--relocate-profile=")) is { } relocation)
        { Environment.ExitCode=StorageLocation.Run(relocation["--relocate-profile=".Length..]);return; }
#if DEBUG
        if(args.Length==3&&args[0]=="--verify-administrator-owner")
        {Environment.ExitCode=AdministratorOwnerVerification.Run(args[1],args[2]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-maintenance-guidance")
        {Environment.ExitCode=MaintenanceNativeVerification.Guidance(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-maintenance-apps")
        {Environment.ExitCode=MaintenanceNativeVerification.Apps(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-maintenance-hardening")
        {Environment.ExitCode=MaintenanceHardeningVerification.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-maintenance-native")
        {Environment.ExitCode=MaintenanceNativeVerification.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-performance-bridge")
        {Environment.ExitCode=PerformanceBridgeVerification.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--verify-performance-budget")
        {Environment.ExitCode=PerformanceBudgetVerification.Run(args[1]).GetAwaiter().GetResult();return;}
        if(args.Contains("--verify-jump-cleanup"))
        {
            if(Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase)!=true)throw new ArgumentException("Owned Jump List cleanup profile required");
            NativeJumpList.RemoveOwned();return;
        }
        if(args.FirstOrDefault(a=>a.StartsWith("--verify-jump-args=")) is {} argumentProbe)
        {
            string receipt=Path.GetFullPath(argumentProbe["--verify-jump-args=".Length..]);
            if(Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase)!=true||!receipt.StartsWith(Settings.Profile+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned Jump List argument receipt required.");
            File.WriteAllText(receipt,System.Text.Json.JsonSerializer.Serialize(new{path=PathArg(args),pid=Environment.ProcessId}));return;
        }
        const string specialistSettings="--verify-specialist-settings=";
        if(args.FirstOrDefault(a=>a.StartsWith(specialistSettings)) is {} settingsRequest)
        {
            string receipt=Path.GetFullPath(settingsRequest[specialistSettings.Length..]);
            if(Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s13-s9-s11\specialists-",StringComparison.OrdinalIgnoreCase)!=true||!receipt.StartsWith(Settings.Profile+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned specialist settings receipt required.");
            Settings.Load();File.WriteAllText(receipt,System.Text.Json.JsonSerializer.Serialize(Settings.DisabledPreviewModules.Order().ToArray()));return;
        }
        if(args.FirstOrDefault(a=>a.StartsWith("--storage-parent=")) is { } storageParent)
        {StorageLocation.StartVerificationParent(storageParent["--storage-parent=".Length..],args.Single(a=>a.StartsWith("--storage-parent-pipe="))["--storage-parent-pipe=".Length..]);return;}
        if(args.FirstOrDefault(a=>a.StartsWith("--desktop-test-profile=")) is { } testProfile)
        {
            string profile=Path.GetFullPath(testProfile["--desktop-test-profile=".Length..]);
            if(!profile.StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase)||!args.Contains("--resume-agent"))throw new ArgumentException("Owned resume fixture required.");
            Environment.SetEnvironmentVariable("PATHWEFT_PROFILE",profile);
        }
#endif
        if(args.Contains("--desktop-launch"))
        {
            try{Environment.ExitCode=DesktopLaunch.FromSetup(args.Contains("--resume-agent"),args.Contains("--set-default"));}
            catch(Exception error)when(error is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException){Environment.ExitCode=1;}
            return;
        }
        if(args.FirstOrDefault(a=>a.StartsWith("--desktop-result=")) is { } desktopResult)
        {
            if(!args.Contains("--resume-agent")&&!args.Contains("--set-default")){Environment.ExitCode=1;return;}
            Environment.ExitCode=DesktopLaunch.RunAndReport(desktopResult["--desktop-result=".Length..],()=>
            {
                var application=new App();application.InitializeComponent();return application.Run();
            });
            return;
        }
#if DEBUG
        const string openReceiver="--open1111-receiver=";
        if(args.FirstOrDefault(a=>a.StartsWith(openReceiver)) is { } openRequest)
        {
            string target=System.IO.Path.GetFullPath(openRequest[openReceiver.Length..]);
            if(!target.StartsWith(@"D:\Temp\pathweft-s8-delivery\appearance-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Owned open fixture required.");
            System.IO.File.WriteAllText(target+".receipt.json",System.Text.Json.JsonSerializer.Serialize(new{path=target,directory=Environment.CurrentDirectory,pid=Environment.ProcessId}));
            return;
        }
        if(S8ShellTrial.Enabled)
        {
            var scratch=Directory.CreateDirectory(Path.Combine(Program.PortableDir,"temp")).FullName;
            Environment.SetEnvironmentVariable("TEMP",scratch);Environment.SetEnvironmentVariable("TMP",scratch);
        }
        if(S8ShellTrial.Enabled&&args.Contains("--s8-stop")){if(Running)HandOff("--s8-close");return;}
        const string transferBenchmark = "--benchmark-s5=";
        if (args.FirstOrDefault(a => a.StartsWith(transferBenchmark)) is { } benchmarkRequest)
        { Environment.ExitCode = S5Benchmark.Run(benchmarkRequest[transferBenchmark.Length..]); return; }
        const string fileUiSettings = "--verify-fileui-settings=";
        if (args.FirstOrDefault(a => a.StartsWith(fileUiSettings)) is { } settingsReceipt)
        {
            if (Settings.Profile?.StartsWith(@"D:\Temp\pathweft-s4-ui\", StringComparison.OrdinalIgnoreCase) != true || !Path.GetFullPath(settingsReceipt[fileUiSettings.Length..]).StartsWith(@"D:\Temp\pathweft-s4-ui\", StringComparison.OrdinalIgnoreCase)) { Environment.ExitCode = 2; return; }
            Settings.Load(); File.WriteAllText(settingsReceipt[fileUiSettings.Length..], Settings.ShowHidden ? "shown" : "hidden"); return;
        }
        if (args.Contains("--diagnose-s4-desktop")) { Console.WriteLine(S4Verification.DesktopDiagnostic()); return; }
        const string externalFile = "--verify-s4-external-file=";
        if (args.FirstOrDefault(a => a.StartsWith(externalFile)) is { } transferFile)
        { Environment.ExitCode = S4Verification.ReadExternalFileClipboard(transferFile[externalFile.Length..], args.Contains("--clipboard-cut")); return; }
        const string snapshotClipboard = "--snapshot-s4-clipboard=";
        if (args.FirstOrDefault(a => a.StartsWith(snapshotClipboard)) is { } capture)
        { const string expectedSnapshot = "--clipboard-expected="; Environment.ExitCode = S4Verification.CaptureClipboard(capture[snapshotClipboard.Length..], args.Single(a => a.StartsWith(expectedSnapshot))[expectedSnapshot.Length..]); return; }
        const string restoreClipboard = "--restore-s4-clipboard=";
        if (args.FirstOrDefault(a => a.StartsWith(restoreClipboard)) is { } snapshot)
        { Environment.ExitCode = S4Verification.RestoreClipboard(snapshot[restoreClipboard.Length..], uint.Parse(args.Single(a => a.StartsWith("--clipboard-sequence="))[21..])); return; }
        const string archiveClipboard = "--verify-s4-clipboard=";
        if (args.FirstOrDefault(a => a.StartsWith(archiveClipboard)) is { } savedClipboard)
        { Environment.ExitCode = S4Verification.ReadClipboard(savedClipboard[archiveClipboard.Length..]); return; }
        const string archiveConsumer = "--archive-s4-consumer=";
        if (args.FirstOrDefault(a => a.StartsWith(archiveConsumer)) is { } content)
        { Environment.ExitCode = S4Verification.RunConsumer(content[archiveConsumer.Length..]); return; }
        const string clipboardCheck = "--verify-s1-clipboard=";
        if (args.FirstOrDefault(a => a.StartsWith(clipboardCheck)) is { } expected)
        {
            Environment.ExitCode = S1Verification.ReadClipboard(expected[clipboardCheck.Length..]);
            return;
        }
#endif
        if (!args.Any(a => a.StartsWith("--")) && Running)
        {
            AllowSetForegroundWindow(-1); // we were just launched by the user, so we may pass focus on
            if (!HandOff(PathArg(args))) Environment.ExitCode = 1;
            return; // the mutex owner stays authoritative; never create a duplicate window
        }
        App.Main();
    }

    /// True while a Pathweft window is open for this user (it holds the single-instance mutex).
    internal static bool Running
    {
        get
        {
            if (!Mutex.TryOpenExisting(PipeName, out var m)) return false;
            m.Dispose();
            return true;
        }
    }

    /// The path a launch asks for, "" if none. Explorer's own form too, which "Open path" in Everything and other
    /// programs pass to a file manager: `/select,"C:\x\file.txt"` (also behind `/n,` and the like). Windows keeps
    /// `/select,"C:\a b\f.txt"` as one argument, but a space after the comma, or a path with spaces and no
    /// quotes, arrives in pieces: they are put back together.
    internal static string PathArg(IEnumerable<string> args)
    {
        var s = string.Join(" ", args.Where(a => !a.StartsWith("--"))).Trim();
        while (s.StartsWith('/') && s.IndexOf(',') is > 0 and var comma) s = s[(comma + 1)..].TrimStart();
        s=s.Trim('"').Trim();
        // A quoted drive root may arrive as D:" after Windows consumes the
        // trailing backslash/quote. A Shell drive request always means its root.
        return s.Length==2&&char.IsLetter(s[0])&&s[1]==':'?s+"\\":s;
    }

    /// Passes a folder to the running window ("" = just come to the front). False if it didn't answer.
    internal static bool HandOff(string path)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(); // wait for the owning listener, without a timeout-driven second instance
            // The pipe name is predictable; never hand a path to a listener another
            // account created first. An elevated window of this user is owned by
            // Administrators (UAC changes the token owner, not the user).
            if (pipe.GetAccessControl().GetOwner(typeof(System.Security.Principal.SecurityIdentifier)) is not System.Security.Principal.SecurityIdentifier owner) return false;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                if (!identity.User.Equals(owner) && !owner.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid)) return false;
            using var w = new StreamWriter(pipe);
            w.WriteLine(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(int pid);
}

public partial class App : Application
{
    public App() => Motion.Hook(); // before any window is made

    // `Pathweft.exe --selftest`: runs the Ferry engine on real files in a temp dir, exit code 0 = pass.
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest")) { Shutdown(SelfTest()); return; }

        // Window-less Win+E catcher (see Agent.cs); never opens the main window.
        if (e.Args.Contains("--agent"))
        {
            int code = Agent.Run(this);
            if (code >= 0) Shutdown(code);
            return;
        }

        // Called by the installer / uninstaller: same code path as the switch in Settings.
        if(e.Args.Contains("--prepare-update")||e.Args.Contains("--prepare-uninstall")||e.Args.Contains("--resume-agent"))
        {
            Settings.Load();
            try
            {
                if(e.Args.Contains("--resume-agent"))
                {
                    WindowsIntegration.UpgradeOwnedRegistration();
                    if(Settings.TakeWinE)Agent.EnableAsync(Environment.ProcessPath).GetAwaiter().GetResult();
                    StartupTasks.ResumeRecorder();
                }
                else
                {
                    // Never force-close an app, active transfer or editor.
                    // The updater supplies its actual caller PID to Setup;
                    // manual installers report that the window must be closed.
                    if(Program.Running){Shutdown(2);return;}
                    Agent.StopForUpdate();
                    StartupTasks.StopRecorder(); // Setup is elevated; the recorder releases the executable
                    if(e.Args.Contains("--prepare-uninstall"))
                    {
                        Settings.SetDefaultFileManager(false);
                        if(Agent.OwnsStartup)Agent.Disable();
                        Settings.RemoveOwnedStartup();
                        NativeJumpList.RemoveOwned();
                    }
                }
                Shutdown(0);
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException){Shutdown(1);}
            return;
        }
#if DEBUG
        if(S8ShellTrial.Enabled&&(e.Args.Contains("--s8-wine-on")||e.Args.Contains("--s8-wine-off")))
        {
            Settings.Load();Settings.TakeWinE=e.Args.Contains("--s8-wine-on");Settings.Save();
            Settings.SetWinE(Settings.TakeWinE);Shutdown(0);return;
        }
#endif
        if (e.Args.Contains("--set-default") || e.Args.Contains("--unset-default"))
        {
            Settings.Load(); // "Win+E opens Pathweft" may be off
            try
            {
                Settings.SetDefaultFileManager(e.Args.Contains("--set-default"));
                if(e.Args.Contains("--unset-default")) Agent.Disable();
                Shutdown(0);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) { Shutdown(1); }
            return;
        }

        // Single instance: a second launch (folder double-click, Win+E) hands its path to the
        // running window, which opens it as a new tab, then exits.
        instance = new Mutex(true, Program.PipeName, out bool first);
        bool hidden = e.Args.Contains("--tray"); // started at sign-in (Settings → Windows integration)
        if (!first && hidden) { Shutdown(); return; } // already running: nothing to start
        if (!first)
        {
            Program.AllowSetForegroundWindow(-1); // we were just launched by the user, so we may pass focus on
            Shutdown(Program.HandOff(Program.PathArg(e.Args)) ? 0 : 1);
            return;
        }
        else
        {
            listening = Listen();
        }
        Settings.Load(); // before any window: the panes read column widths while being built
        ShellIdentity.Apply(); // stable process/shortcut identity, before creating any HWND
        if(Settings.Profile==null)
        {
            // This app and its children own their runtime scratch on the data drive.
            // Explicit verification profiles keep their caller's isolated scratch.
            string runtimeTemp;
            try{runtimeTemp=Directory.CreateDirectory(Path.Combine(DataFolder.Bulk,"Temp")).FullName;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException)
            {
                // A chosen data drive that is missing (an unplugged external disk): say so instead of crashing.
                if(!hidden)MessageBox.Show(error.Message+"\n\nReconnect the drive, or edit data-root.txt in "+Settings.Dir+".","Pathweft",MessageBoxButton.OK,MessageBoxImage.Error);
                Shutdown(1);return;
            }
            Environment.SetEnvironmentVariable("TEMP",runtimeTemp);Environment.SetEnvironmentVariable("TMP",runtimeTemp);
        }
        // LAUNCH-01: an installed copy started by the earlier Run value moves to the logon task.
        _ = Task.Run(()=>{try{StartupTasks.MigrateRunValue();}catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Security.SecurityException){}});
        string folderUpgradeIssue=null;
        try{WindowsIntegration.UpgradeOwnedRegistration();}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {folderUpgradeIssue="Folder integration could not be updated. Review Settings > Windows integration.";}
        Appearance.Initialize();
        updateRetirement=Task.Run(()=>UpdateDownloads.RetireAsync(updateRetirementCancellation.Token));
#if DEBUG
        if (e.Args.Any(a => a.StartsWith("--verify-s4=") || a.StartsWith("--verify-fileui=") || a.StartsWith("--verify-s5=") || a.StartsWith("--verify-s8=") || a.StartsWith("--verify-s8-appearance=")) && Environment.GetEnvironmentVariable("PATHWEFT_TEST_HIDDEN") == "1" && !HiddenTrialPresentation.Enabled)
        { Shutdown(2); return; } // fail before constructing a window when a hidden trial profile is unrecognized
#endif
        UseTipDelay();
        ShellMenu.FollowSystemTheme(); // the Windows right-click menu dark in dark mode
        // folder sizes from previous sessions are read after the first frame, or on first need (SizeCache.StartLoading)
        Exit += (_, _) => SizeCache.Save();
        var sizeSaver = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        sizeSaver.Tick += (_, _) => Task.Run(SizeCache.SaveIfChanged); // off the UI thread: up to 50 000 lines
        sizeSaver.Start();
        base.OnStartup(e);
        var window = new MainWindow(); // opened here, not via StartupUri, so --agent etc. never build it
        if(e.Args.Contains("--storage-result"))window.ContentRendered+=(_,_)=>window.OpenStorageSettings();
        if(folderUpgradeIssue!=null&&window.FindName("Status") is System.Windows.Controls.TextBlock status)status.Text=folderUpgradeIssue;
#if DEBUG
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-fileui=")) is { } fileUiVerification)
            window.ContentRendered += async (_, _) => await window.VerifyFileUi(fileUiVerification["--verify-fileui=".Length..]);
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s5=")) is { } transferVerification)
            window.ContentRendered += async (_, _) => await window.VerifyS5(transferVerification["--verify-s5=".Length..]);
        HiddenTrialPresentation.Prepare(window);
        if(e.Args.FirstOrDefault(a=>a.StartsWith("--verify-jump-cold=")) is {} coldJump)
            window.ContentRendered+=async(_,_)=>await window.VerifyColdJump11(coldJump["--verify-jump-cold=".Length..]);
        if(e.Args.FirstOrDefault(a=>a.StartsWith("--verify-s8-appearance=")) is { } appearanceCorrection)
            window.ContentRendered+=async(_,_)=>await window.VerifyAppearanceCorrection(appearanceCorrection["--verify-s8-appearance=".Length..]);
        if(S8ShellTrial.Enabled)
            window.ContentRendered+=(_,_)=>window.CaptureShellRequest(Program.PathArg(e.Args));
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s8=")) is { } coreVerification)
            window.ContentRendered += async (_, _) => await window.VerifyS8(coreVerification["--verify-s8=".Length..]);
#endif
        if (hidden) window.PrepareHiddenStart();
        window.Show();
#if DEBUG
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s1=")) is { } verification)
            window.ContentRendered += async (_, _) => await S1Verification.Run(window, verification[12..]);
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s2=")) is { } widgetVerification)
            window.ContentRendered += async (_, _) => await window.VerifyS2(widgetVerification[12..]);
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s3=")) is { } previewVerification)
            window.ContentRendered += async (_, _) => await window.VerifyS3(previewVerification[12..]);
        if (e.Args.FirstOrDefault(a => a.StartsWith("--verify-s4=")) is { } archiveVerification)
            window.ContentRendered += async (_, _) => await window.VerifyS4(archiveVerification[12..]);
#endif
    }

    Mutex instance;
    readonly CancellationTokenSource listenCancellation = new();
    Task listening;
    readonly CancellationTokenSource updateRetirementCancellation=new();
    Task updateRetirement=Task.CompletedTask;

    protected override void OnExit(ExitEventArgs e)
    {
        updateRetirementCancellation.Cancel();
        try{updateRetirement.GetAwaiter().GetResult();}catch(OperationCanceledException)when(updateRetirementCancellation.IsCancellationRequested){}
        updateRetirementCancellation.Dispose();
        PerformanceMonitor.Stop();
        AdministratorSession.End().GetAwaiter().GetResult();
#if DEBUG
        HiddenTrialPresentation.StopObserver();
#endif
        listenCancellation.Cancel();
        listening?.GetAwaiter().GetResult();
        Search.EverythingSearchClient.StopIfStartedAsync().GetAwaiter().GetResult();
        instance?.Dispose();
        listenCancellation.Dispose();
        base.OnExit(e);
    }

    /// Settings: every tooltip waits this long, 1.5 s unless changed. Up to 1.0.50 it was WPF's own 1 s, and once one tooltip
    /// had shown, the next came after 0.1 s (BetweenShowDelay): running the pointer down a list, every row popped one. Now
    /// each waits the whole time. A tooltip reads its wait from its own element, so the default for all of them is changed,
    /// once and before the first window: it can't be changed again while running, hence "from the next start".
    internal static void UseTipDelay()
    {
        foreach (var type in new[] { typeof(FrameworkElement), typeof(FrameworkContentElement) })
        {
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(Settings.TipDelay));
            System.Windows.Controls.ToolTipService.BetweenShowDelayProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(0));
        }
    }

    async Task Listen()
    {
        try
        {
            while (!listenCancellation.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                string path;
                // A client that drops between connecting and writing must not end the listener: later launches
                // wait for it in HandOff's untimed Connect.
                try
                {
                    await server.WaitForConnectionAsync(listenCancellation.Token).ConfigureAwait(false);
                    path = await new StreamReader(server).ReadLineAsync(listenCancellation.Token).ConfigureAwait(false);
                }
                catch (IOException) { continue; }
                _ = Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow w) w.OpenFromOutside(path); });
            }
        }
        catch (OperationCanceledException) when (listenCancellation.IsCancellationRequested) { }
    }

    static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pathweft-test-" + Guid.NewGuid().ToString("N")[..8]);
        static bool Junction(string link, string target)
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
                CreateNoWindow = true, UseShellExecute = false,
            });
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(root, "src", "tree", "sub")).Parent.Parent.FullName;
            var dst = Directory.CreateDirectory(Path.Combine(root, "dst")).FullName;
            File.WriteAllText(Path.Combine(src, "tree", "a.txt"), "hello");
            File.WriteAllBytes(Path.Combine(src, "tree", "sub", "b.bin"), new byte[3_000_000]);

            // copy a folder tree
            var j = Run(Ferry.Submit(new[] { Path.Combine(src, "tree") }, dst, move: false));
            Check(j.Phase == Phase.Done && j.DoneFiles == 2 && j.Errors.IsEmpty, "copy tree");
            Check(File.ReadAllText(Path.Combine(dst, "tree", "a.txt")) == "hello", "copied content");
            Check(new FileInfo(Path.Combine(dst, "tree", "sub", "b.bin")).Length == 3_000_000, "copied size");

            // same copy again → conflicts; "keep both" sets the existing file aside as name.old.ext
            File.WriteAllText(Path.Combine(dst, "tree", "a.txt"), "existing");
            File.WriteAllText(Path.Combine(dst, "tree", "a.old.txt"), "taken");
            j = Ferry.Submit(new[] { Path.Combine(src, "tree") }, dst, move: false);
            j.Resolve(Choice.Rename);
            Run(j);
            Check(j.Conflicts.Count == 2, "two conflicts found in one pass");
            Check(File.ReadAllText(Path.Combine(dst, "tree", "a.txt")) == "hello" && File.ReadAllText(Path.Combine(dst, "tree", "a.old (2).txt")) == "existing" && File.ReadAllText(Path.Combine(dst, "tree", "a.old.txt")) == "taken", "keep both → existing a.txt kept as a.old (2).txt, incoming as a.txt");
            Check(File.Exists(Path.Combine(dst, "tree", "sub", "b.old.bin")) && j.Kept.Count == 2 && j.KeptFiles == 2, "keep both records each file set aside for Undo");
#if DEBUG
            // a Keep both arrival that does not complete gives the existing file its name back
            var keepDir = Directory.CreateDirectory(Path.Combine(root, "keep-cancel")).FullName;
            var keepSource = Path.Combine(root, "keep-cancel-incoming.txt"); File.WriteAllText(keepSource, "incoming");
            File.WriteAllText(Path.Combine(keepDir, "keep-cancel-incoming.txt"), "existing");
            Ferry.VerificationBoundary = (phase, job, _, _) => { if (phase == "copy-written") job.Cancel(); };
            try { j = Ferry.Submit(new[] { keepSource }, keepDir, move: false); j.Resolve(Choice.Rename); Run(j); }
            finally { Ferry.VerificationBoundary = null; }
            Check(j.IsCancelled && Directory.GetFiles(keepDir).Length == 1 && File.ReadAllText(Path.Combine(keepDir, "keep-cancel-incoming.txt")) == "existing" && j.KeptFiles == 0, "cancelled keep both puts the existing file back under its name");
#endif

            // move within the same volume is a rename; source is gone afterwards
            var mv = Directory.CreateDirectory(Path.Combine(root, "moved")).FullName;
            j = Run(Ferry.Submit(new[] { Path.Combine(src, "tree") }, mv, move: true));
            Check(j.Phase == Phase.Done && !Directory.Exists(Path.Combine(src, "tree")) && File.Exists(Path.Combine(mv, "tree", "a.txt")), "same-volume move");
            Check(j.RenamedFiles == j.TotalFiles && j.CopiedFiles == 0 && j.VerifiedFiles == 0, "move preserves identity through the native rename path without claiming copied/hash-verified bytes");

            var named = Directory.CreateDirectory(Path.Combine(root, "named")).FullName;
            var namedSource = Path.Combine(root, "renamed-input.txt");File.WriteAllText(namedSource,"undo exact name");
            j = Run(Ferry.Submit(new[]{namedSource}, named, true, names: new Dictionary<string,string>{{namedSource,"original.txt"}}));
            Check(j.Successful && File.ReadAllText(Path.Combine(named,"original.txt"))=="undo exact name" && !File.Exists(namedSource), "Undo transfer mapping restores the exact requested basename");
            Check(j.Completed.Count==1 && j.Completed.Single().Moved && j.Completed.Single().Stamp==Ferry.Stamp(Path.Combine(named,"original.txt")), "Native move records the arrived file and its actual identity");
            var beforeReplacement = Ferry.Stamp(Path.Combine(named,"original.txt"));
            var oldTime = File.GetLastWriteTimeUtc(Path.Combine(named,"original.txt"));
            File.Move(Path.Combine(named,"original.txt"),Path.Combine(named,"retained.txt"));
            File.WriteAllText(Path.Combine(named,"original.txt"),"undo exact name");File.SetLastWriteTimeUtc(Path.Combine(named,"original.txt"),oldTime);
            Check(Ferry.Stamp(Path.Combine(named,"original.txt"))!=beforeReplacement,"Undo detects a replacement even when size and modification time match");

            var batchOne=Directory.CreateDirectory(Path.Combine(root,"batch-one")).FullName;
            var batchTwo=Directory.CreateDirectory(Path.Combine(root,"batch-two")).FullName;
            var batchTarget=Directory.CreateDirectory(Path.Combine(root,"batch-target")).FullName;
            File.WriteAllText(Path.Combine(batchOne,"same.txt"),"first");File.WriteAllText(Path.Combine(batchTwo,"same.txt"),"second");
            j=Ferry.Submit(new[]{Path.Combine(batchOne,"same.txt"),Path.Combine(batchTwo,"same.txt")},batchTarget,false);j.Resolve(Choice.Rename);Run(j);
            Check(j.Successful&&j.Conflicts.Count==1&&Directory.GetFiles(batchTarget).Select(File.ReadAllText).Order().SequenceEqual(new[]{"first","second"}),"Keep both preserves duplicate destination names from separate source folders");

            var journal=Path.Combine(root,"undo-recovery.txt");
            File.WriteAllLines(journal,new[]{"copy\t"+batchTarget,"came\t"+Path.Combine(batchOne,"same.txt")+"\t"+Path.Combine(batchTarget,"same.txt")+"\t"+Ferry.Stamp(Path.Combine(batchTarget,"same.txt"))});
            var recovered=FileExplorer.MainWindow.ReadJournal(journal);
            Check(recovered.Journal==Path.GetFileName(journal)&&recovered.Steps.Single().Kind=="recycle", "Interrupted copy journal restores identified arrivals with a deduplication identity");

            // move a folder onto a folder of the same name: merged file by file, never renamed over it,
            // and not undoable (undo would take the files that were already there with it)
            var merge = Directory.CreateDirectory(Path.Combine(root, "merge")).FullName;
            Directory.CreateDirectory(Path.Combine(merge, "tree"));
            File.WriteAllText(Path.Combine(merge, "tree", "kept.txt"), "was here");
            var incoming = Directory.CreateDirectory(Path.Combine(root, "incoming", "tree")).FullName;
            File.WriteAllText(Path.Combine(incoming, "new.txt"), "arrived");
            // a junction inside the moved folder, pointing at a folder with an empty subfolder
            var outside = Directory.CreateDirectory(Path.Combine(root, "outside", "empty")).Parent.FullName;
            var link = Path.Combine(incoming, "link");
            bool junction = Junction(link, outside);
            j = Run(Ferry.Submit(new[] { incoming }, merge, move: true));
            Check(j.Phase == Phase.Done && j.Merged && j.Errors.IsEmpty, "merge move finished");
            Check(File.Exists(Path.Combine(merge, "tree", "kept.txt")) && File.Exists(Path.Combine(merge, "tree", "new.txt")), "merge keeps both folders' files");
            if (junction)
            {
                Check(Directory.Exists(Path.Combine(outside, "empty")), "a junction's target is never emptied");
                Check(Directory.Exists(link), "the junction itself stays where it was");
                // Retained for the helper's exact leaf-reparse cleanup contract.
            }

            // folder sizes: every folder a scan passes through is remembered, and a change drops the
            // totals above it
            var sizes = Path.Combine(root, "sizes");
            var deep = Directory.CreateDirectory(Path.Combine(sizes, "a", "b")).FullName;
            File.WriteAllBytes(Path.Combine(deep, "x.bin"), new byte[1000]);
            // what a listing compares against: the folder's entry in its parent
            DateTime Stamp(string p) => new DirectoryInfo(Path.GetDirectoryName(p)).EnumerateDirectories(Path.GetFileName(p)).First().LastWriteTimeUtc;
            Check(PaneView.Measure(new DirectoryInfo(sizes), default) == 1000, "folder size");
            Check(SizeCache.Get(deep, Stamp(deep)) == 1000, "subfolders remembered");
            Check(SizeCache.Get(deep, DateTime.UtcNow) == -1, "a changed folder is forgotten");
            var mid = Path.Combine(sizes, "a");
            Check(SizeCache.Get(mid, Stamp(mid)) == -1, "and so is every total above it");
            File.WriteAllBytes(Path.Combine(deep, "y.bin"), new byte[500]);
            Check(PaneView.Measure(new DirectoryInfo(sizes), default) == 1500, "measured again after the change");

            // names sort the way Explorer sorts them: symbols first, numbers by value
            var order = string.Join(",", new[] { "b", "file10", "_drafts", "file2", "A", "(old)" }.OrderBy(x => x, ExplorerOrder.Instance));
            Check(order == "(old),_drafts,A,b,file2,file10", "Explorer name order, got " + order);

            // copy with verification on
            Settings.Verify = true;
            var vdst = Directory.CreateDirectory(Path.Combine(root, "verified")).FullName;
            j = Run(Ferry.Submit(new[] { Path.Combine(mv, "tree") }, vdst, move: false));
            Settings.Verify = false;
            Check(j.Phase == Phase.Done && j.Errors.IsEmpty && j.DoneBytes == j.TotalBytes && j.TotalBytes == 5 + 3_000_000 && j.VerificationBytes == j.TotalBytes && j.VerifiedFiles == j.TotalFiles, "hash-during-copy reads only the destination for verification and keeps payload accounting separate");

            // cancel before it starts
            j = Ferry.Submit(new[] { Path.Combine(mv, "tree") }, dst, move: false);
            j.Cancel();
            Run(j);
            Check(j.Phase == Phase.Cancelled, "cancel");

            // Recycle Bin record ($I file, Windows 10+ layout): version 2, size, FILETIME, length, path
            var when = new DateTime(2026, 9, 21, 7, 30, 0, DateTimeKind.Local);
            var original = @"C:\Users\someone\Documents\report final.docx";
            var info = Path.Combine(root, "$IABC123.docx");
            File.WriteAllBytes(info, BitConverter.GetBytes(2L).Concat(BitConverter.GetBytes(12345L)).Concat(BitConverter.GetBytes(when.ToFileTime()))
                .Concat(BitConverter.GetBytes(original.Length + 1)).Concat(System.Text.Encoding.Unicode.GetBytes(original + "\0")).ToArray());
            Check(RecycleBinWindow.ParseInfo(info) is { } rec && rec.Path == original && rec.Size == 12345 && rec.Deleted == when, "recycle bin record");

            var card = Path.Combine(root, "card.json");
            // QuickLook is handed what a shortcut points at; anything else, or a shortcut that can't be read, as it is
            var lnk = Path.Combine(root, "record.lnk");
            FileExplorer.MainWindow.SaveShortcut(lnk, info, root);
            Check(FileExplorer.MainWindow.ShortcutTarget(lnk) == info && FileExplorer.MainWindow.ShortcutTarget(info) == info
                && FileExplorer.MainWindow.ShortcutTarget(card) == card && FileExplorer.MainWindow.ShortcutTarget(Path.Combine(root, "gone.lnk")) == Path.Combine(root, "gone.lnk"), "shortcut target");
            // a shortcut to a folder is where a middle-click opens and a drop lands; one to a file is not
            var folderLnk = Path.Combine(root, "dst.lnk");
            FileExplorer.MainWindow.SaveShortcut(folderLnk, dst, dst);
            Check(FileExplorer.MainWindow.ShortcutFolder(folderLnk) == dst && FileExplorer.MainWindow.ShortcutFolder(lnk) == null
                && FileExplorer.MainWindow.ShortcutFolder(dst) == null && new Entry { Name = "dst.lnk", Path = folderLnk }.FolderTarget == dst, "shortcut to a folder");
            // Settings → Files → "Sort shortcuts to folders with the folders": off, the shortcut sorts with the files
            string Sorted(bool shortcutsWithFolders)
            {
                Settings.ShortcutsWithFolders = shortcutsWithFolders;
                var rows = new List<Entry> { new() { Name = "record.lnk", Path = lnk }, new() { Name = "a.txt", Path = info }, new() { Name = "dst.lnk", Path = folderLnk },
                                             new() { Name = "zz", Path = dst, IsDir = true }, new() { Name = "..", Path = root, IsDir = true } };
                rows.Sort((x, y) => new EntrySort("Name", false).Compare(x, y));
                return string.Join(",", rows.Select(r => r.Name));
            }
            Check(Sorted(false) == "..,zz,a.txt,dst.lnk,record.lnk" && Sorted(true) == "..,dst.lnk,zz,a.txt,record.lnk", "shortcuts to folders sorted with the folders, or not");
            Settings.ShortcutsWithFolders = false;
            // the Windows menu brings forward the app that took the file: its title has to name the file
            Check(ShellMenu.ShowsFile("note.txt - Notepad", new[] { "note.txt" }) && ShellMenu.ShowsFile("*NOTE.TXT - Notepad", new[] { "x.png", "note.txt" })
                && !ShellMenu.ShowsFile("Untitled - Notepad", new[] { "note.txt" }), "a window showing the file");
            // 7-Zip / WinRAR / Open with items in Pathweft' menu: their access keys in WPF's form, stray separators dropped
            Check(ShellMenu.Label("Add &to \"a_b.rar\"\tCtrl+T") == ("Add _to \"a__b.rar\"", "Ctrl+T") && ShellMenu.Label("Tom && Jerry").Label == "Tom & Jerry", "shell menu labels");
            ShellMenu.Node Sep() => new(null, null, null, null, null);
            ShellMenu.Node Cmd(string t) => new(t, null, null, null, () => { });
            Check(string.Join(",", ShellMenu.Tidy(new() { Sep(), Cmd("a"), Sep(), Sep(), Cmd("b"), Sep() }).Select(n => n.Text ?? "-")) == "a,-,b", "shell menu separators tidied");

            // a silent permanent delete (Shift+Delete with the sound off): read-only files go too, and a
            // junction inside is removed without emptying the folder it points at
            var doomed = Directory.CreateDirectory(Path.Combine(root, "doomed", "sub")).Parent.FullName;
            var ro = Path.Combine(doomed, "sub", "locked.txt");
            File.WriteAllText(ro, "read-only");
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            var kept = Directory.CreateDirectory(Path.Combine(root, "kept")).FullName;
            File.WriteAllText(Path.Combine(kept, "stays.txt"), "here");
            bool doomedLink = Junction(Path.Combine(doomed, "link"), kept);
            FileExplorer.MainWindow.DeleteQuietly(doomed);
            Check(!Directory.Exists(doomed), "silent delete removes the folder, read-only file included");
            Check(!doomedLink || File.Exists(Path.Combine(kept, "stays.txt")), "silent delete never empties a junction's target");

            // a failed update check is a failure, never "nothing newer": offline, 403 (rate limit), 5xx,
            // an unreadable answer all throw; a readable answer with nothing newer returns null
            Check(UpdateCheckFails("http://127.0.0.1:9/"), "update check offline fails");
            Check(UpdateCheckFails(Serve(403, "{\"message\":\"API rate limit exceeded\"}")), "update check 403 fails");
            Check(UpdateCheckFails(Serve(502, "")), "update check 5xx fails");
            Check(UpdateCheckFails(Serve(200, "<html>")), "update check unreadable answer fails");
            Updater.Feed = Serve(200, "{\"tag_name\":\"v0.0.1\",\"body\":\"\",\"assets\":[{\"name\":\"Pathweft-0.0.1-setup.exe\",\"browser_download_url\":\"x\",\"size\":1}]}");
            Check(Task.Run(() => Updater.Check(manual: true)).GetAwaiter().GetResult() == null, "update check: nothing newer is not a failure");

            // a copy into the folder it is in duplicates beside it, as in Explorer; folders keep their dots
            var dup = Directory.CreateDirectory(Path.Combine(root, "dup", "v1.2", "inner")).Parent.Parent.FullName;
            File.WriteAllText(Path.Combine(dup, "v1.2", "inner", "x.txt"), "x");
            File.WriteAllText(Path.Combine(dup, "notes.txt"), "n");
            Run(Ferry.Submit(new[] { Path.Combine(dup, "notes.txt"), Path.Combine(dup, "v1.2") }, dup, move: false));
            j = Run(Ferry.Submit(new[] { Path.Combine(dup, "notes.txt") }, dup, move: false));
            Check(File.ReadAllText(Path.Combine(dup, "notes - Copy.txt")) == "n" && File.Exists(Path.Combine(dup, "notes - Copy (2).txt"))
                && File.Exists(Path.Combine(dup, "v1.2 - Copy", "inner", "x.txt")) && File.ReadAllText(Path.Combine(dup, "notes.txt")) == "n"
                && j.Conflicts.Count == 0 && j.Duplicates[Path.Combine(dup, "notes.txt")] == Path.Combine(dup, "notes - Copy (2).txt"), "duplicate in the same folder");

            // the path a launch asks for (1.0.46): Explorer's /select, as "Open path" programs send it, or a plain path
            Check(Program.PathArg(new[] { @"/select,C:\a b\f.txt" }) == @"C:\a b\f.txt", "launch: /select,\"path\"");
            Check(Program.PathArg(new[] { "/select,", @"C:\a", @"b\f.txt" }) == @"C:\a b\f.txt", "launch: /select, then a path in pieces");
            Check(Program.PathArg(new[] { @"/n,/select,C:\f.txt""" }) == @"C:\f.txt", "launch: two switches, a stray quote");
            Check(Program.PathArg(new[] { "--front", @"C:\Users" }) == @"C:\Users" && Program.PathArg(new[] { "--front" }) == "", "launch: plain path, none");
            Check(Program.PathArg(new[]{"D:\""})==@"D:\", "launch: Windows-quoted drive root remains absolute");

            // Real shutdown found a Count/CopyTo race while sizing workers
            // added cache entries. Exercise Save while another worker mutates
            // the actual cache; a snapshot must remain a valid persisted file.
            using(var barrier=new Barrier(2))
            {
                var sizing=Task.Run(()=>
                {
                    for(int round=0;round<40;round++)
                    {
                        barrier.SignalAndWait();
                        for(int item=0;item<100;item++)SizeCache.Set(Path.Combine(root,"concurrent-cache",round.ToString(),item.ToString()),DateTime.UtcNow,item);
                        barrier.SignalAndWait();
                    }
                });
                for(int round=0;round<40;round++){barrier.SignalAndWait();SizeCache.Save();barrier.SignalAndWait();}
                sizing.GetAwaiter().GetResult();
            }
            SizeCache.Save();
            Check(SizeCache.Get(Path.Combine(root,"concurrent-cache","39","99"),DateTime.UtcNow)==-1&&File.Exists(SizeCache.FilePath),"size cache: concurrent writes/checkpoints retain a valid database and reject stale timestamps");

            // selected text: white on the default accent at 8:1 or better (1.0.38's 6.97:1 still read as grey), and
            // readable on other accents and strengths
            var (fill, text) = FileExplorer.MainWindow.Selection(System.Windows.Media.Color.FromRgb(0x4C, 0x8D, 0xFF), 1.0);
            Check(text == System.Windows.Media.Colors.White && FileExplorer.MainWindow.Contrast(text, fill) >= 8, "selection text on the default accent");
            foreach (var hex in new[] { "#FFD23F", "#FF4FA3", "#E5484D", "#30D158", "#FFFFFF" })
                foreach (var strength in new[] { 0.3, 1.0, 2.5 })
                {
                    var (f, t) = FileExplorer.MainWindow.Selection((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex), strength);
                    Check(FileExplorer.MainWindow.Contrast(t, f) >= 4.5, $"selection text on {hex} at {strength}");
                }

            // Legacy File Labs migration is intentionally absent from Pathweft.

            // What's new: every release parses, newest first, with notes, the running version included (a release
            // without its entry in whatsnew.txt fails here)
            var notes = WhatsNewWindow.Load();
            Check(notes.Count >= 44 && notes[0].Version == Updater.Current.ToString() && notes.All(n => n.Lines.Count > 0)
                  && notes.Zip(notes.Skip(1)).All(p => Version.Parse(p.First.Version) > Version.Parse(p.Second.Version)), "what's new: every release, newest first, this one included");
            Check(notes.All(n => { using var pic = WhatsNewWindow.Picture(n); return pic != null; }), "what's new: every release has the picture of its month built in");
            var monthly=new List<string>();
            foreach(int month in Enumerable.Range(1,12))
            {
                using var pic=WhatsNewWindow.Picture(new("test",new DateTime(2027,month,1),"test",new()));
                monthly.Add(pic==null?"missing":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pic)));
            }
            Check(monthly.Count==12&&!monthly.Contains("missing")&&monthly.Distinct().Count()==12,"what's new: twelve distinct embedded monthly banners cover year rollover");
            var recent = new List<string> { @"C:\A", @"C:\B" };
            Settings.Bump(recent, @"c:\b", 2); Settings.Bump(recent, @"C:\C", 2);
            Check(recent.SequenceEqual(new[] { @"C:\C", @"c:\b" }), "recent folders: newest first, once, capped");
            Check(FileExplorer.MainWindow.ParentForMenu(@"C:\Users\x\file") == @"C:\Users\x"
                  && FileExplorer.MainWindow.ParentForMenu(@"C:\Users\someone\AppData\Local\Temp\claude\deep\folder\x") == @"C:\…\deep\folder", "menu paths shortened past 40 characters");

            // the size cache drops paths that go round a junction loop (563,000 of them made a 277 MB cache), not long real ones
            Check(SizeCache.Loops(@"G:\Claude Projects\automaton\repo\packages\cli\node_modules\@conway\automaton\packages\cli\node_modules\@conway\automaton\packages\cli\node_modules\@conway\automaton\packages\cli\node_modules\viem\_types")
                  && !SizeCache.Loops(@"G:\Claude Projects\automaton\repo\packages\cli\node_modules\.pnpm\@typescript-eslint+typescript-estree@8.8.0_typescript@5.6.2\node_modules\@typescript-eslint\typescript-estree\dist\create-program\shared\x")
                  && !SizeCache.Loops(@"C:\short\a\b\c\a\b\c"), "size cache: loop paths dropped, long real ones and short ones kept");

            // 1.0.57 Group by date: Explorer's buckets, from a fixed "now" (Wednesday 28 Oct 2026, noon)
            var at = new DateTime(2026, 10, 28, 12, 0, 0);
            Check(Entry.DateGroupOf(at.AddHours(3), at) == 0 && Entry.DateGroupOf(at.Date, at) == 0 && Entry.DateGroupOf(at.Date.AddMinutes(-1), at) == 1
                  && Entry.DateGroupOf(new DateTime(2026, 10, 8), at) == 4 && Entry.DateGroupOf(new DateTime(2026, 9, 18), at) == 5
                  && Entry.DateGroupOf(new DateTime(2026, 2, 1), at) == 6 && Entry.DateGroupOf(new DateTime(2025, 12, 31), at) == 7,
                  "group by date: today (and later), yesterday, this month, last month, this year, long ago");

            // 1.0.58 search terms: name words with the terms taken out, sizes in 1000s, dates, a regex, a quoted content
            var sq = PaneView.SearchQuery.Parse("report size:1MB-100MB within:7d re:\"^q\\d\" content:\"two words\"", at);
            Check(sq.Error == null && sq.Name == "report" && sq.MinSize == 1_000_000 && sq.MaxSize == 100_000_000 && sq.After == at.AddDays(-7)
                  && sq.Pattern.IsMatch("Q3 report.pdf") && sq.Content == "two words" && sq.FilesOnly, "search terms: parsed");
            var overGb = PaneView.SearchQuery.Parse("size:>1GB", at);
            Check(!overGb.MatchesMeta("a.iso", false, 1_000_000_000, at) && overGb.MatchesMeta("a.iso", false, 1_000_000_001, at) && !overGb.MatchesMeta("dir", true, 0, at)
                  && PaneView.SearchQuery.Parse("*.txt before:2026-01-01", at).MatchesMeta("a.txt", false, 5, new DateTime(2025, 6, 1))
                  && !PaneView.SearchQuery.Parse("*.txt before:2026-01-01", at).MatchesMeta("a.txt", false, 5, at)
                  && PaneView.SearchQuery.Parse("re:\"(\"", at).Error != null && PaneView.SearchQuery.Parse("size:lots", at).Error != null,
                  "search terms: size, date, wildcard, bad regex and bad size refused");

            // 1.0.61 small files several at once: the same files arrive, whole, either way; the times are printed
            var manyDir = Directory.CreateDirectory(Path.Combine(root, "many", "files")).FullName;
            var seeded = new Random(7);
            for (int k = 0; k < 1200; k++)
            {
                var bytes = new byte[4096 + k % 60 * 1024];
                seeded.NextBytes(bytes);
                File.WriteAllBytes(Path.Combine(manyDir, $"f{k:D4}.bin"), bytes);
            }
            Job Timed(bool parallel, string to, out long ms)
            {
                var keep = Settings.ParallelCopy;
                Settings.ParallelCopy = parallel;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var job = Run(Ferry.Submit(new[] { manyDir }, Directory.CreateDirectory(to).FullName, move: false));
                ms = clock.ElapsedMilliseconds;
                Settings.ParallelCopy = keep;
                return job;
            }
            var oneByOne = Timed(false, Path.Combine(root, "many", "one"), out var msOne);
            var together = Timed(true, Path.Combine(root, "many", "four"), out var msFour);
            bool same = Directory.GetFiles(manyDir).All(f =>
                File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "many", "four", "files", Path.GetFileName(f)))));
            Check(oneByOne.DoneFiles == 1200 && together.DoneFiles == 1200 && together.Errors.IsEmpty && same,
                  $"parallel copy: 1200 small files arrive whole ({together.DoneFiles} copied, {together.Errors.Count} errors)");
            Console.WriteLine($"1200 small files on {Path.GetPathRoot(root)} (fast: {Ferry.Fast(root)}): one by one {msOne} ms, four at a time {msFour} ms");

            // 1.0.60 archives: a ZIP and a TAR.GZ listed by folder, extracted without overwriting, a "../" entry refused,
            // a ZIP added to and taken from
            var arch = Path.Combine(root, "arch");
            Directory.CreateDirectory(arch);
            var zipPath = Path.Combine(arch, "a.zip");
            using (var z = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(z.CreateEntry("top.txt").Open())) w.Write("top");
                using (var w = new StreamWriter(z.CreateEntry("docs/inner/deep.txt").Open())) w.Write("deep");
            }
            var tgz = Path.Combine(arch, "b.tar.gz");
            var tarSrc = Path.Combine(arch, "tarsrc");
            Directory.CreateDirectory(Path.Combine(tarSrc, "sub"));
            File.WriteAllText(Path.Combine(tarSrc, "sub", "t.txt"), "tar text");
            using (var fs = File.Create(tgz))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
                System.Formats.Tar.TarFile.CreateFromDirectory(tarSrc, gz, includeBaseDirectory: false);
            var topList = string.Join(",", Archive.List(zipPath, "").OrderBy(i => i.Name).Select(i => i.Name + (i.IsDir ? "/" : "")));
            var innerList = string.Join(",", Archive.List(zipPath, "docs").Select(i => i.Name + (i.IsDir ? "/" : "")));
            var outDir = Path.Combine(arch, "out");
            Directory.CreateDirectory(outDir);
            var first = Archive.Extract(zipPath, new[] { "docs" }, "", outDir);
            File.WriteAllText(Path.Combine(outDir, "top.txt"), "mine");
            var second = Archive.Extract(zipPath, new[] { "top.txt", "docs" }, "", outDir);
            var fromTar = Archive.Extract(tgz, new[] { "t.txt" }, "sub", outDir);
            Check(topList == "docs/,top.txt" && innerList == "inner/" && first == (1, 0) && second == (0, 2)
                  && File.ReadAllText(Path.Combine(outDir, "docs", "inner", "deep.txt")) == "deep" && File.ReadAllText(Path.Combine(outDir, "top.txt")) == "mine"
                  && Archive.List(tgz, "sub").Single().Name == "t.txt" && fromTar == (1, 0) && File.ReadAllText(Path.Combine(outDir, "t.txt")) == "tar text",
                  $"archives: listed by folder, extracted without overwriting ({topList} / {innerList} / {first} {second} {fromTar})");
            var evil = Path.Combine(arch, "evil.zip");
            using (var z = System.IO.Compression.ZipFile.Open(evil, System.IO.Compression.ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("../escaped.txt").Open())) w.Write("x");
            bool refused = false;
            try { Archive.Extract(evil, new[] { ".." }, "", outDir); } catch (IOException) { refused = true; }
            Check(refused && !File.Exists(Path.Combine(arch, "escaped.txt")), "archives: an entry leading out of the folder is refused");
            var addMe = Path.Combine(arch, "add me.txt");
            File.WriteAllText(addMe, "added");
            var addedOnce = Archive.Add(zipPath, "docs", new[] { addMe });
            var addedTwice = Archive.Add(zipPath, "docs", new[] { addMe });
            Archive.Delete(zipPath, new[] { "inner" }, "docs");
            Check(addedOnce == (1, 0) && addedTwice == (0, 1) && string.Join(",", Archive.List(zipPath, "docs").Select(i => i.Name)) == "add me.txt",
                  "archives: added to a ZIP once, a folder taken out of it");
            bool unsafeNameRejected = false;
            try { Archive.Clean("../out.txt"); } catch (IOException) { unsafeNameRejected = true; }
            Check(Archive.Clean("./docs/.gitignore") == "docs/.gitignore" && Archive.Clean("./") == "" && Archive.Clean(".hidden/") == ".hidden"
                  && unsafeNameRejected, "archives: tar's ./ dropped, a leading dot kept, traversal rejected before planning");
            Check(PaneView.ArchiveOf(Path.Combine(zipPath, "docs")) is ({ } za, "docs") && za == zipPath && PaneView.ArchiveOf(arch) == null,
                  "archives: a path running through a ZIP is found, a plain folder isn't");

            // 1.0.51 Reset to defaults: every setting belongs to a card that resets it, or is knowingly kept (Windows
            // integration, what Pathweft remembers by itself); a reset puts back the defaults and touches nothing else
            var resettable = SettingsWindow.AppearanceKeys.Concat(SettingsWindow.FilesKeys).Concat(SettingsWindow.TransfersKeys).ToList();
            var defaults = Settings.DefaultKeys.ToHashSet();
            Check(defaults.All(k => resettable.Contains(k) || SettingsWindow.NotReset.Contains(k)), "reset: every setting is in a card or kept on purpose, missing "
                  + string.Join(",", defaults.Where(k => !resettable.Contains(k) && !SettingsWindow.NotReset.Contains(k))));
            Check(resettable.All(defaults.Contains) && resettable.Distinct().Count() == resettable.Count, "reset: the cards name real settings, each once");
            Settings.PerformanceRetention=30;
            (Settings.Accent, Settings.TipDelay, Settings.Autoscroll, Settings.CloseEmptyPane, Settings.ShowHidden, Settings.Verify, Settings.CloseToTray, Settings.RowPad)
                = ("#FF0000", 2500, true, true, true, true, true, 6);
            Settings.Reset(SettingsWindow.AppearanceKeys);
            Check(Settings.Accent == "#4C8DFF" && Settings.RowPad == 3 && !Settings.Autoscroll && !Settings.CloseEmptyPane
                  && Settings.TipDelay == 2500 && Settings.ShowHidden && Settings.Verify && Settings.CloseToTray, "reset: one card, and only that card");
            Settings.Reset(resettable);
            Check(Settings.PerformanceRetention==30,"reset: unrelated cards preserve performance retention");
            Settings.PerformanceRetention=7;
            Check(Settings.TipDelay == 1500 && !Settings.ShowHidden && !Settings.Verify && Settings.CloseToTray && Settings.AutoscrollSideways, "reset: all three cards; Windows integration kept");
            Settings.CloseToTray = false;

            // 1.0.51 Quick access lists: the file of 1.0.50 and before is one list, written back exactly as it was
            var oldQuick = new[] { @"C:\Users\x\Projects", @"\\nas\share\photos", @"D:\Games" };
            var one = FileExplorer.MainWindow.ParseQuickLists(oldQuick.Append(""));
            Check(one is [{ Name: "Quick access" } q1] && q1.Folders.SequenceEqual(oldQuick) && FileExplorer.MainWindow.QuickListLines(one).SequenceEqual(oldQuick), "quick access: a file from before is one list, kept in its form");
            one.Add(new FileExplorer.MainWindow.QuickList { Name = "Work # 2", Folders = { @"E:\Work" } });
            var two = FileExplorer.MainWindow.ParseQuickLists(FileExplorer.MainWindow.QuickListLines(one));
            Check(two.Count == 2 && two[0].Name == "Quick access" && two[0].Folders.SequenceEqual(oldQuick) && two[1].Name == "Work # 2" && two[1].Folders.SequenceEqual(new[] { @"E:\Work" }), "quick access: two lists, names and folders, read back");
            two[0].Name = "Home";
            two.Add(new FileExplorer.MainWindow.QuickList { Name = "Empty" });
            Check(FileExplorer.MainWindow.ParseQuickLists(FileExplorer.MainWindow.QuickListLines(two)) is [{ Name: "Home" }, { Name: "Work # 2" }, { Name: "Empty", Folders.Count: 0 }]
                  && FileExplorer.MainWindow.ParseQuickLists(Array.Empty<string>()) is [{ Name: "Quick access", Folders.Count: 0 }], "quick access: renamed first list, an empty list, no file");

            // 1.0.51 Find duplicates: same size and content, in subfolders when asked; shortcuts by their target
            var dupes = Directory.CreateDirectory(Path.Combine(root, "dupes", "sub", "deeper")).Parent.Parent.FullName;
            var blob = new byte[200_000]; new Random(7).NextBytes(blob);
            var blobOther = (byte[])blob.Clone(); blobOther[150_000] ^= 1; // the same first 64 KB, then one byte apart
            File.WriteAllBytes(Path.Combine(dupes, "a.bin"), blob);
            File.WriteAllBytes(Path.Combine(dupes, "a copy.bin"), blob);
            File.WriteAllBytes(Path.Combine(dupes, "almost.bin"), blobOther);
            File.WriteAllBytes(Path.Combine(dupes, "sub", "deeper", "a again.bin"), blob);
            File.WriteAllText(Path.Combine(dupes, "small.txt"), "same words");
            File.WriteAllText(Path.Combine(dupes, "sub", "small too.txt"), "same words");
            File.WriteAllText(Path.Combine(dupes, "other.txt"), "diff words"); // same size, other content
            File.WriteAllText(Path.Combine(dupes, "empty1.txt"), ""); File.WriteAllText(Path.Combine(dupes, "empty2.txt"), "");
            FileExplorer.MainWindow.SaveShortcut(Path.Combine(dupes, "to a.lnk"), Path.Combine(dupes, "a.bin"), dupes);
            FileExplorer.MainWindow.SaveShortcut(Path.Combine(dupes, "sub", "also to a.lnk"), Path.Combine(dupes, "a.bin"), dupes);
            FileExplorer.MainWindow.SaveShortcut(Path.Combine(dupes, "to other.lnk"), Path.Combine(dupes, "other.txt"), dupes);
            string Shape(List<DuplicatesWindow.DupGroup> gs) => string.Join(" | ", gs.Select(g => (g.Target != null ? "lnk:" : "") + string.Join(",", g.Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n))));
            var top = DuplicatesWindow.Find(new[] { dupes }, subfolders: false, hidden: false, null, default);
            Check(Shape(top) == "a copy.bin,a.bin", "duplicates in one folder: " + Shape(top));
            var nested = DuplicatesWindow.Find(new[] { dupes }, subfolders: true, hidden: false, null, default);
            Check(Shape(nested) == "a again.bin,a copy.bin,a.bin | small too.txt,small.txt | lnk:also to a.lnk,to a.lnk" && nested[0].Wasted == 400_000, "duplicates with subfolders: " + Shape(nested));
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                bool cancelled = false;
                try { DuplicatesWindow.Find(new[] { dupes }, true, false, null, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "duplicates: Stop stops it");
            }

            // 1.0.59 several locations and looser matches: a file under two listed folders counts once; by name, sizes may differ
            File.WriteAllText(Path.Combine(dupes, "same name.txt"), "first");
            File.WriteAllText(Path.Combine(dupes, "sub", "same name.txt"), "second, longer");
            var sub = Path.Combine(dupes, "sub");
            var twice = DuplicatesWindow.Find(new[] { dupes, sub, dupes }, subfolders: true, hidden: false, null, default);
            Check(Shape(twice) == Shape(nested), "duplicates: overlapping locations count each file once: " + Shape(twice));
            var byName = DuplicatesWindow.Find(new[] { dupes, sub }, subfolders: false, hidden: false, null, default, DuplicatesWindow.Match.Name);
            var bySizeToo = DuplicatesWindow.Find(new[] { dupes, sub }, subfolders: false, hidden: false, null, default, DuplicatesWindow.Match.NameAndSize);
            var byBytes = DuplicatesWindow.Find(new[] { dupes, sub }, subfolders: false, hidden: false, null, default);
            Check(byName.Any(g => g.Name == "same name.txt" && g.Files.Count == 2) && !bySizeToo.Any(g => g.Files.Any(f => f.Path.EndsWith("same name.txt")))
                  && !byBytes.Any(g => g.Files.Any(f => f.Path.EndsWith("same name.txt"))) && byName.First(g => g.Name == "same name.txt").Wasted == 5,
                  "duplicates: by name finds the two, by name and size and by content don't: " + Shape(byName));

            Check(Agent.Matches(true, false, false, false) && !Agent.Matches(false, false, false, false)
                && !Agent.Matches(true, true, false, false) && !Agent.Matches(true, false, true, false)
                && !Agent.Matches(true, false, false, true), "Win+E: only the opted-in unmodified chord");

            Check(!FileExplorer.MainWindow.BinIsOff(@"\\server\share\x.txt") && !FileExplorer.MainWindow.BinIsOff("x.txt"), "a share or a relative path has no Recycle Bin setting to read");
            // 1.0.51 middle-click autoscroll: still inside the dead zone, then faster the further away, either way
            Check(PaneView.AutoscrollSpeed(0) == 0 && PaneView.AutoscrollSpeed(-12) == 0 && PaneView.AutoscrollSpeed(30) > 0 && PaneView.AutoscrollSpeed(-30) == -PaneView.AutoscrollSpeed(30)
                  && PaneView.AutoscrollSpeed(200) > PaneView.AutoscrollSpeed(100) && PaneView.AutoscrollSpeed(100000) == 8000, "autoscroll speed");

            Console.WriteLine("selftest: all passed");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "pathweft-selftest.txt"), ex.ToString());
            return 1;
        }
        finally { Console.WriteLine("Selftest fixtures retained for reviewed transactional cleanup: " + root); }
    }

    static bool UpdateCheckFails(string feed)
    {
        Updater.Feed = feed;
        try { Task.Run(() => Updater.Check(manual: true)).GetAwaiter().GetResult(); return false; }
        catch (Exception ex) when (Updater.Failed(ex)) { return true; }
    }

    // A one-request local server answering with this status and body; returns its address.
    static string Serve(int status, string body)
    {
        var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        port.Start(); int p = ((System.Net.IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{p}/");
        listener.Start();
        _ = Task.Run(() =>
        {
            var c = listener.GetContext();
            c.Response.StatusCode = status;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            c.Response.ContentLength64 = bytes.Length;
            c.Response.OutputStream.Write(bytes);
            c.Response.Close(); // the listener is left to the process: stopping it here can cut the answer off
        });
        return $"http://localhost:{p}/";
    }

    static Job Run(Job j) { j.Completion.GetAwaiter().GetResult(); return j; }
    static void Check(bool ok, string what) { if (!ok) throw new Exception("FAILED: " + what); }
}
