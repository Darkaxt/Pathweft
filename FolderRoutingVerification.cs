#if DEBUG
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
namespace FileExplorer;
public partial class MainWindow
{
    void VerifyFolderRouting112(Action<bool,string> check)
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
                foreach(int version in new[]{1,2,3})
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
                    var old=new WindowsIntegration.Registration(version,prior,current.CreatedKeys);
                    File.WriteAllText(WindowsIntegration.RecoveryPath,JsonSerializer.Serialize(old));
                    WindowsIntegration.UpgradeOwnedRegistration();
                    check(WindowsIntegration.Enabled&&JsonSerializer.Deserialize<WindowsIntegration.Registration>(File.ReadAllText(WindowsIntegration.RecoveryPath)).Version==3,"Intact opt-in version"+version+" upgrades to Folder/explore without losing recovery state");
                    WindowsIntegration.SetEnabled(false);Restored();
                }
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
}
#endif
