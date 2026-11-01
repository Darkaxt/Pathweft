#if DEBUG
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace FileExplorer;
public partial class MainWindow
{
    async Task VerifyFolderRouting112(string root,Action<bool,string> check)
    {
        string path=@"Software\Darkaxt\Pathweft\S8Verification\"+Guid.NewGuid().ToString("N");
        const string folder=@"Software\Classes\Folder\shell\explore\command";
        using(var isolated=Registry.CurrentUser.CreateSubKey(path))
        {
            WindowsIntegration.VerificationRoot=isolated;
            try
            {
                using(var prior=isolated.CreateSubKey(folder))
                {
                    prior.SetValue("","%SystemRoot%\\explorer.exe /e",RegistryValueKind.ExpandString);
                    prior.SetValue("DelegateExecute","previous-folder-delegate");
                }
                WindowsIntegration.UpgradeOwnedRegistration();
                check(!File.Exists(WindowsIntegration.RecoveryPath),"An opted-out installation is not enabled by upgrade");
                WindowsIntegration.SetEnabled(true);
                using(var key=isolated.OpenSubKey(folder))
                    check((string)key.GetValue("DelegateExecute")=="{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}"&&(string)key.GetValue("")==$"\"{Environment.ProcessPath}\" \"%1\"","Opt-in covers the explicit Folder/explore command and existing native delegate");
                WindowsIntegration.SetEnabled(false);
                void Restored()
                {
                    using var key=isolated.OpenSubKey(folder);
                    check(key.GetValueKind("")==RegistryValueKind.ExpandString&&(string)key.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%SystemRoot%\\explorer.exe /e"&&(string)key.GetValue("DelegateExecute")=="previous-folder-delegate","Disable restores the previous Folder command, value kind and delegate exactly");
                }
                Restored();
                foreach(int version in new[]{1,2,3,4})
                {
                    WindowsIntegration.SetEnabled(true);
                    var current=JsonSerializer.Deserialize<WindowsIntegration.Registration>(File.ReadAllText(WindowsIntegration.RecoveryPath));
                    var prior=current.Values.Where(v=>version>=3||v.Key!=folder).Where(v=>version>=2||!v.Key.Contains(@"\CLSID\",StringComparison.Ordinal)).Select(v=>
                    {
                        string written=v.Written;
                        if(version==1&&v.Name=="DelegateExecute")written="";
                        if(version>=2&&v.Key.EndsWith(@"\InprocServer32",StringComparison.Ordinal)&&v.Name=="")written=written.Replace("PathweftShell-1.1.12-","PathweftShell-1.1.11-");
                        using var key=isolated.CreateSubKey(v.Key);key.SetValue(v.Name,written);return v with{Written=written};
                    }).ToList();
                    foreach(var value in current.Values.Where(v=>!prior.Any(p=>p.Key==v.Key&&p.Name==v.Name)))
                    {
                        using var key=isolated.OpenSubKey(value.Key,true);
                        if(value.Existed)key.SetValue(value.Name,value.Value,value.Kind);else key.DeleteValue(value.Name,false);
                    }
                    var old=new WindowsIntegration.Registration(version,prior,current.CreatedKeys,version==4?current.Routes:null);
                    File.WriteAllText(WindowsIntegration.RecoveryPath,JsonSerializer.Serialize(old));
                    WindowsIntegration.UpgradeOwnedRegistration();
                    check(WindowsIntegration.Enabled&&JsonSerializer.Deserialize<WindowsIntegration.Registration>(File.ReadAllText(WindowsIntegration.RecoveryPath)).Version==4,"Intact opt-in version"+version+" upgrades with applied-route recovery");
                    WindowsIntegration.SetEnabled(false);Restored();
                }
                const string extra=@"Software\Classes\Folder\shell\opennewwindow\command";
                using(var original=isolated.CreateSubKey(extra))original.SetValue("","original-extra");
                using(var original=isolated.CreateSubKey(@"Software\Classes\Directory\shell"))original.SetValue("","original-default");
                var custom=FolderRoutes.Defaults();custom.Add(new("Folder","opennewwindow",true));
                WindowsIntegration.UpdateRoutes(custom);
                check(!WindowsIntegration.Enabled&&!File.Exists(WindowsIntegration.RecoveryPath)&&FolderRoutes.Read().SequenceEqual(custom),"Saving a route while opted out persists JSON without enabling integration");
                WindowsIntegration.SetEnabled(true);
                using(var key=isolated.OpenSubKey(extra))check((string)key.GetValue("DelegateExecute")=="{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}","A newly added verb uses the existing native handler without recompilation");
                var disabled=custom.Select(r=>r.Class=="Directory"&&r.Verb=="open"?r with{Enabled=false}:r).ToList();
                WindowsIntegration.UpdateRoutes(disabled);
                using(var key=isolated.OpenSubKey(@"Software\Classes\Directory\shell"))check((string)key.GetValue("")=="original-default","Disabling the open route restores the previous default verb");
                WindowsIntegration.UpdateRoutes(FolderRoutes.Defaults());
                using(var key=isolated.OpenSubKey(extra))check((string)key.GetValue("")=="original-extra"&&key.GetValue("DelegateExecute")==null,"Removing an applied custom route restores its original command");
                string priorJson=File.ReadAllText(FolderRoutes.FilePath);
                foreach(var invalid in new[]{FolderRoutes.Defaults().Append(new FolderRoutes.Route("Folder","explore",false)),new[]{new FolderRoutes.Route("File","open",true)},new[]{new FolderRoutes.Route("Folder","bad\\verb",true)}})
                {
                    bool rejected=false;try{WindowsIntegration.UpdateRoutes(invalid);}catch(InvalidDataException){rejected=true;}
                    check(rejected&&File.ReadAllText(FolderRoutes.FilePath)==priorJson&&WindowsIntegration.Enabled,"Invalid or duplicate routes are rejected before configuration or handlers change");
                }
                using(var key=isolated.CreateSubKey(extra))key.SetValue("",17,RegistryValueKind.DWord);
                bool failed=false;try{WindowsIntegration.UpdateRoutes(custom);}catch(InvalidOperationException){failed=true;}
                check(failed&&WindowsIntegration.Enabled&&File.ReadAllText(FolderRoutes.FilePath)==priorJson,"A failed apply restores the previous JSON and active registration");
                using(var key=isolated.OpenSubKey(extra,true))key.SetValue("","original-extra");
                WindowsIntegration.UpdateRoutes(custom);
                FolderRoutes.Write(FolderRoutes.Defaults()); // external edit after registration
                WindowsIntegration.SetEnabled(false);
                using(var key=isolated.OpenSubKey(extra))check((string)key.GetValue("")=="original-extra"&&key.GetValue("DelegateExecute")==null,"Recovery uses the applied snapshot even after external JSON removes that route");
                WindowsIntegration.SetEnabled(true);File.WriteAllText(FolderRoutes.FilePath,"{broken");
                WindowsIntegration.UpgradeOwnedRegistration();
                check(WindowsIntegration.Enabled,"Malformed edited JSON leaves the active registration intact");
                WindowsIntegration.SetEnabled(false);Restored();FolderRoutes.Write(FolderRoutes.Defaults());
                await VerifyFolderRoutesEditor112(root,check);
                WindowsIntegration.SetEnabled(true);
                using(var changed=isolated.OpenSubKey(folder,true))changed.SetValue("DelegateExecute","later-manager");
                WindowsIntegration.UpgradeOwnedRegistration();
                using(var changed=isolated.OpenSubKey(folder))check((string)changed.GetValue("DelegateExecute")=="later-manager","Upgrade preserves a later manager's Folder handler");
                WindowsIntegration.SetEnabled(false);
                using(var changed=isolated.OpenSubKey(folder))check((string)changed.GetValue("DelegateExecute")=="later-manager","Disable preserves a later manager's Folder handler");
            }
            finally{WindowsIntegration.VerificationRoot=null;Registry.CurrentUser.DeleteSubKeyTree(path,false);}
        }
        check(HiddenTrialPresentation.ForegroundViolations==0,"Folder integration checks never activate the desktop");
    }

    async Task VerifyFolderRoutesEditor112(string root,Action<bool,string> check)
    {
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        IEnumerable<T> Descendants<T>(DependencyObject parent)where T:DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);if(child is T found)yield return found;
                foreach(var nested in Descendants<T>(child))yield return nested;
            }
        }
        void Click(SettingsWindow window,string name)=>((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();
            var window=new SettingsWindow(this);window.Show();await Settle();
            try
            {
                var categories=(ListBox)window.FindName("SettingsCategories");
                categories.SelectedItem=categories.Items.Cast<ListBoxItem>().Single(item=>ReferenceEquals(item.Tag,window.FindName("IntegrationCard")));await Settle();
                var rows=(ItemsControl)window.FindName("FolderRouteRows");
                Click(window,"AddFolderRoute");await Settle();
                var verb=Descendants<TextBox>(rows).Last();verb.Text="opennewwindow";
                Click(window,"ApplyFolderRoutes");await Settle();
                check(FolderRoutes.Read().Last()==new FolderRoutes.Route("Folder","opennewwindow",true)&&!WindowsIntegration.Enabled,"The actual Add/verb/Apply controls persist a new route without changing the master opt-in: "+theme+scale);
                WindowsIntegration.SetEnabled(true);
                var toggle=Descendants<CheckBox>(rows).Last();toggle.IsChecked=false;
                Click(window,"ApplyFolderRoutes");await Settle();
                check(!FolderRoutes.Read().Last().Enabled&&WindowsIntegration.Enabled,"The actual checkbox applies disabled route state while preserving integration: "+theme+scale);
                Click(window,"ReloadFolderRoutes");await Settle();
                check(Descendants<CheckBox>(rows).Last().IsChecked==false,"Reload presents the persisted checkbox state: "+theme+scale);
                string opened=null;window.OpenFolderRoutesForVerification=path=>opened=path;
                Click(window,"OpenFolderRoutesJson");check(opened==FolderRoutes.FilePath,"Open JSON targets the selected settings profile without opening a test editor");
                var add=(Button)window.FindName("AddFolderRoute");add.BringIntoView();await Settle();
                check(Descendants<TextBox>(rows).All(input=>input.ActualWidth>=90)&&rows.ActualWidth<=((ScrollViewer)window.FindName("CategoryScroll")).ViewportWidth,"Folder route rows fit the content column at "+theme+scale);
                await S1Verification.Snapshot(window,root,$"folder-routes-{theme}-{scale*100:0}");
                Descendants<Button>(rows).Last(button=>button.Name=="RemoveRoute").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Click(window,"ApplyFolderRoutes");await Settle();
                check(FolderRoutes.Read().Count==5&&WindowsIntegration.Enabled,"Remove and Apply restore the remaining configured routes: "+theme+scale);
                WindowsIntegration.SetEnabled(false);
                string json=File.ReadAllText(FolderRoutes.FilePath);File.WriteAllText(FolderRoutes.FilePath,"{broken");Click(window,"ReloadFolderRoutes");
                check(((TextBlock)window.FindName("FolderRoutesStatus")).Text.Contains("invalid")&&rows.Items.Count==5,"Malformed JSON reload reports the error and preserves the draft rows");
                opened=null;Click(window,"OpenFolderRoutesJson");check(opened==FolderRoutes.FilePath,"Malformed JSON can still be opened for correction");
                File.WriteAllText(FolderRoutes.FilePath,json);
            }
            finally{window.Close();}
        }
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();
    }
}
#endif
