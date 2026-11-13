#if DEBUG
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FileExplorer;

// S47c Security check (MAINT-40/41/45): shipped list, coverage, evaluation, grouping and no writes.
public partial class MainWindow
{
    static void VerifySecurityCheck(Action<bool,string> check)
    {
        byte[] list;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(MaintenanceSecurity.ResourceName)!){using var copy=new MemoryStream();stream.CopyTo(copy);list=copy.ToArray();}
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"vendor","HardeningKitty","source-manifest.json")));
        string pinned=manifest.RootElement.GetProperty("files")[0].GetProperty("sha256").GetString();
        check(Convert.ToHexString(SHA256.HashData(list)).Equals(pinned,StringComparison.OrdinalIgnoreCase),"Embedded security list is byte-identical to the pinned HardeningKitty file");
        var rules=MaintenanceSecurity.Rules();var skipped=MaintenanceSecurity.NotEvaluated(rules);
        check(rules.Length==418&&rules.Select(r=>r.Id).Distinct().Count()==418&&rules.Count(r=>MaintenanceSecurity.Evaluated.Contains(r.Method))==335,"All 418 list rows parse and 335 use evaluated methods");
        check(skipped.Values.Sum()==83&&skipped.Keys.Order().SequenceEqual(new[]{"accesschk","accountpolicy","auditpol","bcdedit","CimInstance","FirewallRule","Processmitigation","secedit"}.Order()),
            "Exactly the 83 rows needing policy export, user rights, firewall, mitigation, CIM or boot tools are left out");
        check(MaintenanceSecurity.Compare("2","=","2")&&MaintenanceSecurity.Compare("2.0","=","2")&&!MaintenanceSecurity.Compare("1","=","2")&&MaintenanceSecurity.Compare("Enabled","=","enabled")&&
            MaintenanceSecurity.Compare("x","!=","y")&&!MaintenanceSecurity.Compare("2","!=","2")&&MaintenanceSecurity.Compare("5",">=","4")&&!MaintenanceSecurity.Compare("3",">=","4")&&
            MaintenanceSecurity.Compare("30","<=","30")&&!MaintenanceSecurity.Compare("abc",">=","1")&&MaintenanceSecurity.Compare("a;Block;b","contains","block")&&
            MaintenanceSecurity.Compare("0","=|0","1")&&MaintenanceSecurity.Compare("1","=|0","1")&&!MaintenanceSecurity.Compare("2","=|0","1")&&!MaintenanceSecurity.Compare("1","~","1"),
            "Operators compare numerically when both sides are numbers and otherwise as case-insensitive text");
        check(MaintenanceSecurity.Compare("Not installed","=","Disabled")&&MaintenanceSecurity.Compare("Not present","=","Disabled")&&!MaintenanceSecurity.Compare("Not installed","=","Automatic"),
            "A missing service, task or feature meets only a Disabled recommendation");
        var fixture=new SecurityRule("9999","Fixture","Fixture rule","Registry","",@"HKLM:\SOFTWARE\Fixture","Value","","","1","1","=","High",2);
        var byDefault=MaintenanceSecurity.Evaluate(fixture,new(null));var unset=MaintenanceSecurity.Evaluate(fixture with{Default=""},new(null));
        var denied=MaintenanceSecurity.Evaluate(fixture,new(null,false,MaintenanceSecurity.NeedsAdmin));var set=MaintenanceSecurity.Evaluate(fixture,new("0"));
        check(byDefault.Result==MaintenanceSecurity.Met&&byDefault.WindowsDefault&&byDefault.Current=="1"&&unset.Result==MaintenanceSecurity.NotMet&&unset.Current=="Not configured"&&
            set.Result==MaintenanceSecurity.NotMet&&!set.WindowsDefault&&denied.Result==MaintenanceSecurity.NeedsAdmin,"A missing value uses the Windows default; unreadable values are never guessed");
        var rows=new[]{byDefault,set,set with{Severity="Low"},denied}.Select(MaintenancePanel.SecurityRow).ToArray();
        check(rows[0].Group=="Met"&&rows[0].Current=="1 (Windows default)"&&rows[1].Group=="High · not met"&&rows[2].Group=="Low · not met"&&rows[3].Group=="Not checked"&&
            rows.All(r=>!r.CanChange&&r.Details.Contains(@"HKLM:\SOFTWARE\Fixture\Value")&&r.Details.Contains("HardeningKitty 9999")),"Rows carry severity groups, exact target and list ID and cannot be changed");
        // The check reads; every registry target it evaluates must be unchanged afterwards.
        string Snapshot()=>string.Join("\n",rules.Where(r=>r.Method=="Registry").Select(r=>
        {
            int colon=r.Path.IndexOf(":\\",StringComparison.Ordinal);
            using var root=Microsoft.Win32.RegistryKey.OpenBaseKey(r.Path[..colon].ToUpperInvariant()=="HKLM"?Microsoft.Win32.RegistryHive.LocalMachine:Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64);
            try{using var key=root.OpenSubKey(r.Path[(colon+2)..]);return r.Path+"|"+r.Item+"|"+JsonSerializer.Serialize(key?.GetValue(r.Item,null,Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames));}
            catch(System.Security.SecurityException){return r.Path+"|denied";}
        }));
        string before=Snapshot();string journal=File.Exists(Maintenance.Journal)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Maintenance.Journal))):"";
        var findings=MaintenanceSecurity.Check();
        check(findings.Length==335&&findings.All(f=>f.Result is MaintenanceSecurity.Met or MaintenanceSecurity.NotMet or MaintenanceSecurity.NeedsAdmin or MaintenanceSecurity.Unavailable)&&
            findings.Count(f=>f.Result==MaintenanceSecurity.Met)>0,"Actual check evaluates every listed row on this PC as the user");
        check(Snapshot()==before&&(File.Exists(Maintenance.Journal)?Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Maintenance.Journal))):"")==journal,"The check leaves every evaluated registry value and the maintenance journal unchanged");
    }
    internal async Task VerifySecurityCheckUi(string root,Action<bool,string> check)
    {
        await maintenance.CategoryForVerification("Security check");UpdateLayout();
        var rows=maintenance.Rows.Items.Cast<MaintenanceRow>().ToArray();
        check(rows.Length==335&&!maintenance.VisibleActionsForVerification.Any(a=>a is "Apply" or "Restore" or "Restore Windows default"),"Security check lists the evaluated rows and offers no Apply or Restore");
        var headers=((GridView)maintenance.Rows.View).Columns.Select(x=>System.Windows.Automation.AutomationProperties.GetName((GridViewColumnHeader)x.Header)).ToArray();
        check(headers.SequenceEqual(new[]{"Setting","Category","Current","Recommended","Result"}),"Security check shows Setting, Category, Current, Recommended and Result");
        var groups=CollectionViewSource.GetDefaultView(maintenance.Rows.ItemsSource).Groups.Cast<CollectionViewGroup>().Select(g=>(string)g.Name).ToArray();
        string[] order=["High · not met","Medium · not met","Low · not met","Met","Not checked"];
        check(groups.Length>=2&&groups.SequenceEqual(order.Where(groups.Contains)),"Groups read High, Medium and Low not met, then Met, then Not checked");
        var card=(Button)((System.Windows.Controls.Primitives.UniformGrid)maintenance.Cards).Children[5];
        check(((StackPanel)card.Content).Children.OfType<TextBlock>().ElementAt(1).Text==rows.Count(r=>r.State==MaintenanceSecurity.NotMet).ToString("N0")&&card.ToolTip.ToString().Contains("high"),
            "Security card counts not-met findings and the high ones");
        var linked=rows.FirstOrDefault(r=>r.Details.Contains(@"Windows Defender\PUAProtection",StringComparison.OrdinalIgnoreCase));
        maintenance.SearchForVerification("");maintenance.Rows.SelectedItem=linked;UpdateLayout();
        check(linked!=null&&maintenance.ExplanationForVerification.Contains("Potentially unwanted app blocking")&&maintenance.ExplanationForVerification.Contains("HardeningKitty "+linked.Details.Split("HardeningKitty ")[1])&&
            maintenance.VisibleActionsForVerification.Contains("Open in Windows tweaks"),"A finding Pathweft can change names its Windows tweak and offers Open in Windows tweaks");
        await S1Verification.Snapshot(this,root,"security-linked");
        await maintenance.ClickForVerification("Open in Windows tweaks");Task observed;do{observed=maintenance.Ready;await observed;}while(maintenance.Ready!=observed);UpdateLayout();
        check(maintenance.Category=="Windows tweaks"&&(maintenance.Rows.SelectedItem as MaintenanceRow)?.Name=="Potentially unwanted app blocking","Open in Windows tweaks selects the matching Security tweak");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1500;Height=950;UpdateLayout();
            await maintenance.CategoryForVerification("Security check");maintenance.Rows.SelectedIndex=0;UpdateLayout();
            check(maintenance.Rows.ActualHeight>120&&maintenance.ExplanationShownForVerification,"Security check table and details remain usable at "+theme+" / "+scale);
            await S1Verification.Snapshot(this,root,$"security-{theme}-{scale:0.0}");
        }
        Width=820;UpdateLayout();await S1Verification.Snapshot(this,root,"security-narrow");
        check(maintenance.Rows.ActualHeight>100,"Narrow Security check keeps its table visible");
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();Width=1400;
    }
}
#endif
