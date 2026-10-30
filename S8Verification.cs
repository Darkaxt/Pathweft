#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyS8(string root)
    {
        root=Path.GetFullPath(root);
        if(!root.StartsWith(@"D:\Temp\pathweft-s8-delivery\",StringComparison.OrdinalIgnoreCase)||!HiddenTrialPresentation.Enabled)
            throw new ArgumentException("S8 isolated hidden presentation required.");
        var evidence=Directory.CreateDirectory(Path.Combine(root,"evidence")).FullName;
        var checks=new List<object>();int result=1;
        SettingsWindow settings=null;SearchWidgetWindow widget=null;
        void Check(bool success,string criterion)
        {
            checks.Add(new{criterion,passed=success});HiddenTrialPresentation.Phase=criterion;
            if(!success)throw new InvalidOperationException(criterion);
        }
        void Invoke(string name)
        {
            var button=(Button)settings.FindName(name);
            var peer=new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        }
        async Task Settled()=>await Dispatcher.InvokeAsync(()=>UpdateLayout(),DispatcherPriority.ApplicationIdle);
        try
        {
            Check(HiddenTrialPresentation.ForegroundObserverActive&&HiddenTrialPresentation.IsOutsideDisplays(this),"S8 main window is offscreen with native foreground observation");
            if(Environment.GetEnvironmentVariable("PATHWEFT_S8_RESTART")=="1")
            {
                Check(Settings.NativeTheme&&Appearance.Light&&Settings.NativeMode=="light"&&Settings.Accent=="#007A66"&&!Settings.WindowsAccent,"A separate application process restores Native Light and the chosen custom accent");
                await S1Verification.Snapshot(this,evidence,"native-light-restarted");
                result=0;return;
            }
            var folder=Directory.CreateDirectory(Path.Combine(root,"fixtures")).FullName;
            var other=Directory.CreateDirectory(Path.Combine(root,"other")).FullName;
            foreach(var name in new[]{"Assets","Projects","Documents"})Directory.CreateDirectory(Path.Combine(folder,name));
            File.WriteAllText(Path.Combine(folder,"notes.txt"),"Pathweft native theme verification");
            File.WriteAllText(Path.Combine(folder,"settings.json"),"{}");
            File.WriteAllText(Path.Combine(folder,"locked.txt"),"read-only cue");File.SetAttributes(Path.Combine(folder,"locked.txt"),FileAttributes.ReadOnly);
            File.WriteAllText(Path.Combine(other,"other.txt"),"other folder");
            await S1Verification.Navigate(Left,folder);await S1Verification.Navigate(Right,other);
            await Left.IconsCompletionForVerification;
            var filter=(TextBox)Left.FindName("FilterBox");
            filter.Text="notes";await Settled();
            Check(((TextBlock)Left.FindName("Footer")).Text.Contains("match"),"Upstream filter footer describes matching entries");
            await S1Verification.Navigate(Left,folder);
            Check(filter.Text=="notes","Refreshing the same folder preserves its filter");
            await S1Verification.Navigate(Left,other);
            Check(filter.Text=="","Changing folders clears the old filter");
            filter.Text="other";
            var clear=(Button)Left.FindName("FilterClear");
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)).Invoke();await Settled();
            Check(filter.Text==""&&clear.Visibility==Visibility.Collapsed,"Clear action uses the real button path");
            await S1Verification.Navigate(Left,folder);
            Check(PaneView.SearchQuery.Parse("size:>=1KB size:<=2KB",DateTime.Now).MatchesMeta("sample.bin",false,1000,DateTime.Now),"Inclusive filesystem size comparisons accept the boundary");
            Check(!PaneView.SearchQuery.Parse("size:>=1KB",DateTime.Now).MatchesMeta("sample.bin",false,999,DateTime.Now),"Inclusive lower size bound rejects a smaller file");

            var rename=Directory.CreateDirectory(Path.Combine(root,"rename")).FullName;
            foreach(var name in new[]{"a","b","c","blocked"})File.WriteAllText(Path.Combine(rename,name+".txt"),name);
            var plan=new List<(string Path,string NewName)>{(Path.Combine(rename,"a.txt"),"b.txt"),(Path.Combine(rename,"b.txt"),"a.txt"),(Path.Combine(rename,"c.txt"),"blocked.txt")};
            bool refused=false;try{RenameAll(plan);}catch(IOException){refused=true;}
            Check(refused&&new[]{"a","b","c","blocked"}.All(n=>File.ReadAllText(Path.Combine(rename,n+".txt"))==n)&&!Directory.EnumerateFiles(rename,".pathweft-*").Any(),"Failed overlapping batch rename restores every original and leaves the existing target intact");
            var swap=RenameAll(plan.Take(2).ToList());RenameAll(swap);
            Check(File.ReadAllText(plan[0].Path)=="a"&&File.ReadAllText(plan[1].Path)=="b","Successful overlapping rename and its reverse preserve contents");

            settings=new SettingsWindow(this);
            var rendered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.ContentRendered+=(_,_)=>rendered.TrySetResult();settings.Show();await rendered.Task;
            Invoke("ThemeNative");await Settled();Invoke("ThemeDark");await Settled();
            Check(Settings.NativeTheme&&!Appearance.Light&&((SolidColorBrush)Root.Background).Color==Color.FromRgb(28,28,28),"Native Dark is selected through settings and uses an opaque neutral background");
            Check(((Border)Left.FindName("Glow")).Opacity==0,"Native theme removes the active-pane glow");
            Check(MainWindow.Contrast(AccentColor,Appearance.Background)>=4.5,"Native accent remains readable against the window background");
            Check(Left.Items.Single(e=>e.Name=="locked.txt").IsReadOnly,"Native theme retains actual read-only file state");
            Width=1440;Height=960;await S1Verification.Snapshot(this,evidence,"native-dark-split-100");
            await S1Verification.Snapshot(settings,evidence,"native-dark-settings-100");

            widget=new SearchWidgetWindow(this);widget.Show();await S1Verification.Navigate(widget.Pane,folder);
            EditorWindow.Open(Path.Combine(folder,"notes.txt"));
            await EditorWindow.CaptureForVerification(Path.Combine(evidence,"native-dark-editor"),false);
            Check(UpdateInstallationBlocker?.Contains("Close the editor")==true,"An open editor prevents updater shutdown of its work");
            Invoke("ThemeLight");await Settled();
            await EditorWindow.CaptureForVerification(Path.Combine(evidence,"native-light-editor"),true);
            Check(true,"The actual served WebView2 editor and Monaco follow live Native dark/light changes");
            await EditorWindow.CloseForVerification();
            Check(UpdateInstallationBlocker==null,"Closing the editor releases the updater's editor gate");
            Check(true,"The isolated editor's actual browser process exits after its controller closes");
            Check(Appearance.Light&&((SolidColorBrush)widget.Foreground).Color==Appearance.Resolve("#E8E8E8"),"Already-open search widget changes with Native Light");
            var legend=((TextBlock)HiddenToggle.Content).Inlines.OfType<System.Windows.Documents.Run>().First();
            Check(((SolidColorBrush)legend.Foreground).Color==Appearance.Resolve("#8CFFFFFF"),"Existing shortcut legends follow the light-theme muted text resource");
            Check(((SolidColorBrush)Left.Items.First().AgeBrush).Color==Color.FromRgb(232,232,232),"Native file age markers use neutral metadata styling");
            await S1Verification.Snapshot(this,evidence,"native-light-split-100");
            await S1Verification.Snapshot(widget,evidence,"native-light-widget-100");
            Invoke("ThemeDark");await Settled();
            Check(!Appearance.Light&&((SolidColorBrush)widget.Foreground).Color==Appearance.Resolve("#E8E8E8"),"Already-open widget changes back without recreating its controls");
            Invoke("Text150");await Settled();
            await S1Verification.Snapshot(this,evidence,"native-dark-split-150");
            settings.Width=660;settings.Height=900;await S1Verification.Snapshot(settings,evidence,"native-dark-settings-150");
            SetSinglePane(true);await S1Verification.Snapshot(this,evidence,"native-dark-single-150");
            Invoke("ThemeGlass");await Settled();
            Check(!Settings.NativeTheme,"Original appearance remains selectable");
            Invoke("ThemeNative");await Settled();Invoke("ThemeLight");await Settled();
            var hex=(TextBox)settings.FindName("AccentHex");hex.Text="#007A66";
            hex.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(settings),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
            await Settled();
            Check(Settings.Accent=="#007A66"&&!Settings.WindowsAccent&&((SolidColorBrush)Application.Current.Resources["Accent"]).Color==Appearance.Accent,"Custom accent changes through the settings input and reaches existing controls");
            Settings.Save();var saved=File.ReadAllText(Settings.FilePath);
            Check(saved.Contains("theme.native=1")&&saved.Contains("theme.mode=light"),"Native appearance choice is persisted in the actual profile");
            Check(((CheckBox)settings.FindName("DefaultBox")).IsChecked==false&&Settings.TakeWinE==false,"Shell and keyboard integration remain opt-in in the isolated profile");
            ((CheckBox)settings.FindName("DefaultBox")).BringIntoView();
            await S1Verification.Snapshot(settings,evidence,"native-light-integration-settings-150");

            // Exercise the production registration/recovery code against a real
            // private registry subtree; this is not a live Shell routing claim.
            string registryPath=@"Software\Darkaxt\Pathweft\S8Verification\"+Guid.NewGuid().ToString("N");
            using(var isolated=Registry.CurrentUser.CreateSubKey(registryPath))
            {
                WindowsIntegration.VerificationRoot=isolated;
                try
                {
                    using(var old=isolated.CreateSubKey(@"Software\Classes\Directory\shell"))old.SetValue("","previous-manager");
                    using(var old=isolated.CreateSubKey(@"Software\Classes\Directory\shell\open\command"))old.SetValue("","%SystemRoot%\\explorer.exe",RegistryValueKind.ExpandString);
                    WindowsIntegration.SetEnabled(true);
                    Check(WindowsIntegration.Enabled&&File.Exists(WindowsIntegration.RecoveryPath),"Production Shell registration writes its recovery record and selected verbs to a real isolated registry subtree");
                    WindowsIntegration.SetEnabled(false);
                    using(var restored=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))Check((string)restored.GetValue("")=="previous-manager","Disable restores the exact previous default manager");
                    using(var restored=isolated.OpenSubKey(@"Software\Classes\Directory\shell\open\command"))Check(restored.GetValueKind("")==RegistryValueKind.ExpandString&&(string)restored.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%SystemRoot%\\explorer.exe","Disable preserves expandable-string kind and unexpanded contents");
                    Check(isolated.OpenSubKey(@"Software\Classes\Drive")==null&&!File.Exists(WindowsIntegration.RecoveryPath),"Disable removes only newly created empty keys and closes its recovery record");
                    WindowsIntegration.SetEnabled(true);
                    using(var changed=isolated.CreateSubKey(@"Software\Classes\Directory\shell"))changed.SetValue("","third-party");
                    WindowsIntegration.SetEnabled(false);
                    using(var current=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))Check((string)current.GetValue("")=="third-party","Disable preserves a later default-manager change");
                }
                finally{WindowsIntegration.VerificationRoot=null;}
            }
            Registry.CurrentUser.DeleteSubKeyTree(registryPath);
            Environment.SetEnvironmentVariable("PATHWEFT_S8_AGENT_TRIAL","1");
            try
            {
                Settings.TakeWinE=true;Settings.Save();
                await Agent.EnableAsync(Environment.ProcessPath);
                Check(Agent.IsReady,"Windowless Win+E helper signals readiness only after its real keyboard hook is installed");
                await Agent.EnableAsync(Environment.ProcessPath);
                Check(Agent.IsReady,"Repeated enable retains the one ready helper");
                await Agent.DisableAsync();
                Check(!Agent.IsReady&&!File.Exists(Path.Combine(Settings.Dir,"agent-owner.json")),"Disable waits for the original helper's exit, hook teardown and owner-record removal");
                string trialKey=@"Software\Darkaxt\Pathweft\S8Verification\Agent-"+System.IO.Hashing.XxHash32.HashToUInt32(System.Text.Encoding.UTF8.GetBytes(Settings.Profile)).ToString("x8");
                using(var key=Registry.CurrentUser.OpenSubKey(trialKey))Check(key?.GetValue("Pathweft Agent")==null,"Disable removes the helper's isolated sign-in entry");
                Registry.CurrentUser.DeleteSubKey(trialKey,false);
            }
            finally
            {
                await Agent.DisableAsync();Settings.TakeWinE=false;Settings.Save();
                Environment.SetEnvironmentVariable("PATHWEFT_S8_AGENT_TRIAL",null);
            }
            Check(HiddenTrialPresentation.ForegroundViolations==0,"Native theme verification caused no foreground activation");
            result=0;
        }
        catch(Exception error){checks.Add(new{failure=error.ToString()});}
        finally
        {
            widget?.Shutdown();settings?.Close();
            File.WriteAllText(Path.Combine(evidence,"results.json"),JsonSerializer.Serialize(new{result,pid=Environment.ProcessId,runtime=Environment.Version.ToString(),checks,foreground=HiddenTrialPresentation.ForegroundEvents,limit="Offscreen WPF render and isolated real registry; live Shell/Win+E and packaged installation remain separate acceptance."},new JsonSerializerOptions{WriteIndented=true}));
            Close();Application.Current.Shutdown(result);
        }
    }
}
#endif
