#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer;

public partial class MainWindow
{
    static void VerifyMaintenanceGuidance(Action<bool,string> check)
    {
        var rules=MaintenanceGuidance.Data.Rules;
        check(Maintenance.Tweaks.All(t=>MaintenanceGuidance.Find("tweak",t.Name)!=null),"Every existing typed tweak has a versioned exact-target guidance rule");
        var unknown=new MaintenanceRow("service:WSearch-fake","Windows Search","This computer","Running","",Type:"WSearch-fake");
        check(MaintenanceGuidance.Describe("Services",unknown,26100).Recommendation=="Not assessed","A familiar display name or prefix does not classify an unknown service");
        check(MaintenanceGuidance.Describe("Startup",new("third-party","Pathweft","Current user","Enabled","Registry64 · fake.exe",Type:"Registry Run"),26100).Recommendation=="Not assessed","Third-party startup names never imply trusted publisher/product identity");
        check(rules.All(r=>r.MinBuild<=r.MaxBuild&&r.Source.StartsWith("https://")&&!string.IsNullOrWhiteSpace(r.FeatureLoss)),"Every bundled rule has bounded applicability, effect and source");
        check(rules.All(r=>r.Qualified.Length==0||DateOnly.TryParseExact(r.Qualified,"yyyy-MM-dd",out _)&&r.Supports(r.QualifiedBuild)),"Configuration qualification has an explicit date and actual guest build");
        var unqualified=rules.First(r=>r.Qualified.Length==0&&r.Kind=="app");
        check(MaintenanceGuidance.Describe("Windows apps",new(unqualified.Target,"App","Current user","Installed",""),26100).Recommendation=="Not assessed","A documented but unqualified package does not gain an assessed recommendation");
        var tweak=Maintenance.Tweaks.First();var row=new MaintenanceRow("tweak:"+Maintenance.Id("tweak",tweak.Name),tweak.Name,"Windows","Not applied",tweak.Description);
        check(!MaintenanceGuidance.Describe("Windows tweaks",row,99999).CanChange&&MaintenanceGuidance.Describe("Windows tweaks",row,99999).Recommendation=="Not assessed","Unsupported build retains explanation but cannot Apply");
        bool rejected=false;try{MaintenanceGuidance.RequireSupported(row.Id,99999);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Unsupported build is refused at the typed action boundary, independently of UI");
    }
    internal async Task VerifyMaintenanceGuidanceUi(string root,Action<bool,string> check)
    {
        await maintenance.CategoryForVerification("Windows tweaks");int all=maintenance.Rows.Items.Count;
        check(System.Windows.Data.CollectionViewSource.GetDefaultView(maintenance.Rows.ItemsSource).Groups?.Count>=5,"Actual settings table renders meaningful category groups");
        maintenance.SearchForVerification("Storage Sense");
        check(maintenance.Rows.Items.Count==1&&maintenance.Rows.Items.Cast<MaintenanceRow>().Single().Name=="Storage Sense","Actual search filters settings by name/effect");
        maintenance.Rows.SelectedIndex=0;check(maintenance.ExplanationForVerification.Contains("Automatic Storage Sense")&&maintenance.ExplanationForVerification.Contains("reviewed"),"Selected setting explains feature loss and review scope");
        maintenance.SearchForVerification("");maintenance.GroupForVerification("File Explorer");
        check(maintenance.Rows.Items.Count>0&&maintenance.Rows.Items.Cast<MaintenanceRow>().All(r=>r.Group=="File Explorer"),"Category selector filters existing settings without changing Windows");
        maintenance.GroupForVerification("All categories");check(maintenance.Rows.Items.Count==all,"Clearing category restores the complete typed setting list");
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1d,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1500;Height=950;UpdateLayout();
            maintenance.SearchForVerification("Storage Sense");maintenance.Rows.SelectedIndex=0;UpdateLayout();
            await S1Verification.Snapshot(this,root,$"guidance-{theme}-{scale:0.0}");
        }
        maintenance.SearchForVerification("");Width=900;UpdateLayout();await S1Verification.Snapshot(this,root,"guidance-narrow");
        check(maintenance.Rows.ActualHeight>100,"Filtered settings retain usable rows with details in narrow layout");
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();Width=1500;
    }
}
#endif
