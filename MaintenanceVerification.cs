#if DEBUG
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FileExplorer;

public partial class MainWindow
{
    internal async Task VerifyMaintenance32(string root,Action<bool,string> check)
    {
        performance.AutomaticIo=false;SetSinglePane(true);SetActive(Left);await S1Verification.Navigate(active,root);
        File.WriteAllText(Path.Combine(root,"selection.txt"),"Maintenance must preserve this browser");await S1Verification.Navigate(active,root);
        active.ListForVerification.SelectedItem=active.Items.First(x=>!x.IsUp);string path=active.Dir;var selection=active.Selected.Select(x=>x.Path).ToArray();
        MaintenanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await maintenance.Ready;UpdateLayout();
        check(maintenance.IsVisible&&!PaneArea.IsVisible&&AnalysisVisible&&!TerminalCommand.IsEnabled&&Equals(MaintenanceCommand.Tag,"on"),"Actual brush opens Maintenance in the main pane and disables hidden file actions");
        check(maintenance.Rows.Items.Count==4&&maintenance.State.Contains("no changes"),"Opening Cleanup is read-only and lists only curated categories");
        check(maintenance.Cards.IsVisible&&!StatsRow.IsVisible&&!performance.IsVisible&&((System.Windows.Controls.Primitives.UniformGrid)maintenance.Cards).Children.Count==5,"Brush replaces the bottom statistics with exactly five maintenance summary tabs");
        foreach(string category in new[]{"Startup","Services","Windows tweaks","Windows apps"})
        {
            await maintenance.CategoryForVerification(category);check(!maintenance.State.Contains("failed",StringComparison.OrdinalIgnoreCase),"Native read-only category opens: "+category);
            check(category=="Windows apps"||maintenance.Rows.Items.Count>0,"Category exposes actual Windows rows: "+category);
            var selected=((System.Windows.Controls.Primitives.UniformGrid)maintenance.Cards).Children.Cast<Button>().Where(x=>Equals(x.Tag,"on")).ToArray();
            check(selected.Length==1&&System.Windows.Automation.AutomationProperties.GetName(selected[0])==category+" maintenance","Bottom summary tab selects the actual main section: "+category);
        }
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1400;Height=850;UpdateLayout();
            await maintenance.CategoryForVerification("Windows tweaks");UpdateLayout();
            await S1Verification.Snapshot(this,root,$"maintenance-{theme}-{scale:0.0}");
            check(maintenance.Rows.ActualHeight>120&&maintenance.Rows.ActualWidth>400,"Maintenance table remains usable at "+theme+" / "+scale);
        }
        Width=820;UpdateLayout();await S1Verification.Snapshot(this,root,"maintenance-narrow");
        check(maintenance.Rows.ActualHeight>100&&maintenance.Rows.ActualWidth>=Math.Min(250,maintenance.ActualWidth-70),"Narrow main-pane maintenance keeps navigation and table visible within the available scaled width");
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();Width=1400;
        await maintenance.CategoryForVerification("Windows tweaks");
        var exported=maintenance.Rows.Items.Cast<MaintenanceRow>().First();string selectedFile=Path.Combine(root,"maintenance-selection.json");
        MaintenancePanel.WriteSelection(selectedFile,exported.Id);maintenance.ImportForVerification(selectedFile);
        check((maintenance.Rows.SelectedItem as MaintenanceRow)?.Id==exported.Id&&maintenance.State.Contains("Review and Apply"),"Export/import round-trip selects the supported tweak without applying it");
        File.WriteAllText(selectedFile,"{}");bool rejected=false;
        try{maintenance.ImportForVerification(selectedFile);}catch(InvalidDataException){rejected=true;}
        check(rejected,"Missing selection fields are rejected as a readable validation error rather than an unhandled UI task failure");

        // One newly created, task-owned Run value. No existing startup entry or
        // user-chosen setting is touched. Finally removes exactly this value.
        string name="Pathweft-verification-"+Guid.NewGuid().ToString("N"),id=Maintenance.StartupId("HKEY_CURRENT_USER",RegistryView.Registry64,name);
        using(var key=Registry.CurrentUser.CreateSubKey(Maintenance.RunPath))key.SetValue(name,"%TEMP%\\Pathweft-missing-fixture.exe",RegistryValueKind.ExpandString);
        try
        {
            await maintenance.CategoryForVerification("Startup");maintenance.Rows.SelectedItem=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id==id);
            await maintenance.ClickForVerification("Disable");
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath))check(key.GetValueNames().Contains(name),"Selecting Disable only opens inline confirmation; no early mutation");
            await maintenance.ClickForVerification("Confirm");
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath))check(!key.GetValueNames().Contains(name)&&Maintenance.Undo().ContainsKey(id),"Confirmed native worker disables the task-owned startup entry and journals its exact prior state");
            maintenance.Rows.SelectedItem=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id==id);
            // A later external value must not be overwritten by Restore.
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath,true))key.SetValue(name,"external-fixture",RegistryValueKind.String);
            await maintenance.ClickForVerification("Restore");await maintenance.ClickForVerification("Confirm");
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath))check((string)key.GetValue(name)=="external-fixture"&&maintenance.State.Contains("outside"),"Actual Restore refuses to overwrite a newer independently changed value");
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath,true))key.DeleteValue(name);
            await maintenance.ClickForVerification("Refresh");maintenance.Rows.SelectedItem=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id==id);
            await maintenance.ClickForVerification("Restore");await maintenance.ClickForVerification("Confirm");
            using(var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath))check(key.GetValueKind(name)==RegistryValueKind.ExpandString&&(string)key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%TEMP%\\Pathweft-missing-fixture.exe"&&!Maintenance.Undo().ContainsKey(id),"Native restore preserves the raw expandable value, hive and view, and removes its completed journal record");
        }
        finally{using var key=Registry.CurrentUser.OpenSubKey(Maintenance.RunPath,true);key?.DeleteValue(name,false);}

        string temp=Path.GetTempPath(),candidate=Path.Combine(temp,"reviewed.txt"),changed=Path.Combine(temp,"changed.txt"),busy=Path.Combine(temp,"busy.txt"),added=Path.Combine(temp,"new.txt");
        File.WriteAllText(candidate,"remove only this unchanged task file");File.WriteAllText(changed,"original");File.WriteAllText(busy,"in use");
        string linked=Path.Combine(temp,"linked.txt"),linkedAlias=Path.Combine(root,"linked-alias.txt"),laterLinked=Path.Combine(temp,"later-linked.txt"),laterAlias=Path.Combine(root,"later-alias.txt");
        File.WriteAllText(linked,"preserve reviewed links");File.WriteAllText(laterLinked,"preserve links created after review");
        if(!MaintenanceFixtureLink(linkedAlias,linked,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await maintenance.CategoryForVerification("Cleanup");maintenance.Rows.SelectedItem=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id=="user-temp");
        await maintenance.ClickForVerification("Analyze");
        long? reviewed=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id=="user-temp").Bytes;
        await maintenance.CategoryForVerification("Startup");await maintenance.CategoryForVerification("Cleanup");
        check(reviewed.HasValue&&maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id=="user-temp").Bytes==reviewed,"Cleanup summary and exact review survive switching bottom tabs");
        maintenance.Rows.SelectedItem=maintenance.Rows.Items.Cast<MaintenanceRow>().Single(x=>x.Id=="user-temp");
        await S1Verification.Snapshot(this,root,"maintenance-cleanup-reviewed");
        File.WriteAllText(changed,"changed since review");File.WriteAllText(added,"not reviewed");
        if(!MaintenanceFixtureLink(laterAlias,laterLinked,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        using(var held=new FileStream(busy,FileMode.Open,FileAccess.ReadWrite,FileShare.Read))
        {
            await maintenance.ClickForVerification("Clean reviewed");await maintenance.ClickForVerification("Confirm");
            check(!File.Exists(candidate)&&File.Exists(changed)&&File.Exists(busy)&&File.Exists(added),"Actual cleanup confirmation deletes reviewed unchanged files and preserves new, changed and active files");
            check(File.Exists(linked)&&File.Exists(linkedAlias)&&File.Exists(laterLinked)&&File.Exists(laterAlias),"Cleanup skips hard links present during analysis and links created after its review");
            check(maintenance.State.Contains("removed")&&maintenance.State.Contains("skipped"),"Cleanup reports its actual removed and skipped counts");
        }
        await using(var client=new MaintenanceClient())
        {var bad=await client.Start(new("apply","not-a-maintenance-action",Maintenance.Journal,null),false);check(!bad.Ok,"Authenticated worker rejects unsupported actions without changing Windows");}
        await using(var client=new MaintenanceClient())
        {
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool stopped=false;
            try{await client.Start(new("analyze","user-temp",Maintenance.Journal,Path.Combine(root,"cancelled-review")),false,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped,"Cancelling before admission does not leave a worker waiting for a disconnected owner");
        }
        await CloseMaintenance();UpdateLayout();
        check(PaneArea.IsVisible&&active.Dir==path&&active.Selected.Select(x=>x.Path).SequenceEqual(selection)&&TerminalCommand.IsEnabled,"Back/toggle restores the existing browser path, selection and file commands");
        string reviews=Path.Combine(DataFolder.Cache,"Maintenance");check(!Directory.Exists(reviews)||!Directory.EnumerateFiles(reviews).Any(),"Closing Maintenance retires transient cleanup review manifests");
        File.WriteAllText(Path.Combine(root,"maintenance-native.json"),JsonSerializer.Serialize(new{journal=Maintenance.Undo().Count,services=Maintenance.Services.Length,tweaks=Maintenance.Tweaks.Length,processOwned=true}));
    }
    [DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool MaintenanceFixtureLink(string alias,string existing,IntPtr security);
}
#endif
