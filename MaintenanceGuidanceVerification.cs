#if DEBUG
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        string ownCommand="HKEY_CURRENT_USER\\"+Maintenance.RunPath+" · Registry64 · \""+Program.Executable+"\" --agent";
        check(MaintenanceGuidance.Describe("Startup",new("own","Pathweft Agent","Current user","Enabled",ownCommand,Type:"Registry Run"),26100).Recommendation=="Keep enabled"&&
            MaintenanceGuidance.Describe("Startup",new("fake","Pathweft Agent","Current user","Enabled",ownCommand+"-fake",Type:"Registry Run"),26100).Recommendation=="Not assessed","Complete source/view/raw command matches app-owned startup without accepting a similar suffix");
        check(MaintenanceDonorTweaks.All().Length==24&&Maintenance.Tweaks.Count()==121&&MaintenanceS47Tweaks.All().Length==81&&rules.Count(r=>r.Kind=="service"&&r.ReadOnlyEvidence)>=24,"Expanded catalog contains real typed donor actions and documented native service dependencies");
        check(rules.All(r=>r.MinBuild<=r.MaxBuild&&r.Source.StartsWith("https://")&&!string.IsNullOrWhiteSpace(r.FeatureLoss)),"Every bundled rule has bounded applicability, effect and source");
        check(rules.All(r=>r.Qualified.Length==0||DateOnly.TryParseExact(r.Qualified,"yyyy-MM-dd",out _)&&r.Supports(r.QualifiedBuild)),"Configuration qualification has an explicit date and actual guest build");
        var unqualified=rules.First(r=>r.Qualified.Length==0&&r.Kind=="app");
        check(MaintenanceGuidance.Describe("Windows apps",new(unqualified.Target,"App","Current user","Installed",""),26100).Recommendation=="Not assessed","A documented but unqualified package does not gain an assessed recommendation");
        var tweak=Maintenance.Tweaks.First();var row=new MaintenanceRow("tweak:"+Maintenance.Id("tweak",tweak.Name),tweak.Name,"Windows","Not applied",tweak.Description);
        check(!MaintenanceGuidance.Describe("Windows tweaks",row,99999).CanChange&&MaintenanceGuidance.Describe("Windows tweaks",row,99999).Recommendation=="Not assessed","Unsupported build retains explanation but cannot Apply");
        bool rejected=false;try{MaintenanceGuidance.RequireSupported(row.Id,99999);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Unsupported build is refused at the typed action boundary, independently of UI");
        var optional=new MaintenanceRow("fixture","Optional setting","Current user","Not applied","",Recommendation:"Optional");
        check(MaintenanceGuidance.PendingRecommendation(optional)&&!MaintenanceGuidance.PendingRecommendation(optional with{State="Applied"})&&
            !MaintenanceGuidance.PendingRecommendation(optional with{Recommendation="Advanced"})&&!MaintenanceGuidance.PendingRecommendation(optional with{Recommendation="Not assessed"})&&
            !MaintenanceGuidance.PendingRecommendation(optional with{CanChange=false}),"Pending recommendations exclude applied, advanced, unassessed and unavailable actions");
        check(MaintenanceGuidance.PendingRecommendation(optional with{Recommendation="Keep enabled",State="Applied"}),"A disabled keep-enabled setting remains a pending restore recommendation");
        // S47a: goal and risk (MAINT-25, MAINT-26) and outside-set policies (MAINT-29).
        check(rules.Where(r=>r.Kind=="tweak").All(r=>r.Goal is "Debloat" or "Privacy" or "Performance" or "Security" or "Personalize"&&r.Risk is "Low" or "Caution" or "High")&&
            !rules.Any(r=>r.Risk=="High"&&r.Recommendation=="Optional"),"Every tweak rule states one goal and one risk level, and no high-risk rule is merely optional");
        check(!MaintenanceGuidance.PendingRecommendation(optional with{Risk="Caution"})&&!MaintenanceGuidance.PendingRecommendation(optional with{Risk="High"})&&
            !MaintenanceGuidance.PendingRecommendation(optional with{State=Maintenance.OutsideState})&&
            MaintenanceGuidance.PendingRecommendation(optional with{Recommendation="Keep enabled",State="Applied",Risk="High"}),"Applying caution, high-risk and outside-set rules is never a pending suggestion; undoing advised-against changes still is");
        check(new MaintenanceRow("r","R","","","",Risk:"High").RiskLabel=="◆ High"&&new MaintenanceRow("r","R","","","",Risk:"Caution").RiskLabel=="▲ Caution"&&
            new MaintenanceRow("r","R","","","",Risk:"Low").RiskLabel=="","Risk markers carry a symbol and word, so the level reads without colour");
        var risky=rules.First(r=>r.Kind=="tweak"&&r.Risk!="Low");var riskyTweak=Maintenance.Tweaks.Single(t=>t.Name.Equals(risky.Target,StringComparison.OrdinalIgnoreCase));
        var described=MaintenanceGuidance.Describe("Windows tweaks",new("tweak:"+Maintenance.Id("tweak",riskyTweak.Name),riskyTweak.Name,"","Not applied",""),risky.MinBuild);
        check(described.Goal==risky.Goal&&described.Risk==risky.Risk&&described.Guidance.Contains(MaintenanceGuidance.RiskText(risky.Risk)),"Assessment shows the rule's goal, risk and its cost");
        RegistryEdit policy=new("HKEY_LOCAL_MACHINE",@"SOFTWARE\Policies\Microsoft\Windows\Fixture","Value",Microsoft.Win32.RegistryValueKind.DWord,default,true),
            preference=policy with{Path=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced"},legacy=policy with{Path=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"};
        check(Maintenance.PolicyOnly([policy,legacy])&&!Maintenance.PolicyOnly([policy,preference])&&!Maintenance.PolicyOnly([]),"Only rules made entirely of policy values can return to the Windows default");
        check(Maintenance.Tweaks.Where(t=>t.TaskPaths.Length>0).All(t=>t.TaskPaths.All(p=>p.StartsWith('\\')&&!p.EndsWith('\\'))),"Task-backed tweaks name exact absolute scheduled-task paths");
    }
    internal async Task VerifyMaintenanceGuidanceUi(string root,Action<bool,string> check)
    {
        await maintenance.CategoryForVerification("Services");maintenance.Rows.SelectedIndex=0;maintenance.Rows.ScrollIntoView(maintenance.Rows.SelectedItem);UpdateLayout();
        var service=(MaintenanceRow)maintenance.Rows.SelectedItem;
        var serviceContainer=(ListViewItem)maintenance.Rows.ItemContainerGenerator.ContainerFromItem(service);
        check(service.Summary.IndexOfAny(['\r','\n'])<0&&serviceContainer.ActualHeight<40&&maintenance.ExplanationForVerification.Contains(service.Details)&&
            FindMaintenanceAssessment(maintenance).IsReadOnly,"Services uses compact single-line rows and the same complete selectable detail well as Startup");
        var guided=maintenance.Rows.Items.Cast<MaintenanceRow>().First(r=>r.Type=="AppXSvc"&&r.Recommendation=="Keep enabled");
        maintenance.Rows.SelectedItem=guided;
        check(!string.IsNullOrWhiteSpace(guided.Guidance)&&maintenance.ExplanationForVerification.Contains(guided.Guidance)&&
            !((GridView)maintenance.Rows.View).Columns.Cast<GridViewColumn>().Any(column=>System.Windows.Automation.AutomationProperties.GetName((GridViewColumnHeader)column.Header)=="Recommendation"),
            "Exact service advice remains in the selected assessment without a sparse Recommendation column");
        await S1Verification.Snapshot(this,root,"services-compact");
        await maintenance.CategoryForVerification("Windows tweaks");int all=maintenance.Rows.Items.Count;
        check(all==121&&maintenance.Rows.Items.Cast<MaintenanceRow>().Count(r=>r.RuleId.StartsWith("donor."))==24,"Actual searchable grouped table contains all newly adopted donor actions");
        check(maintenance.Rows.Items.Cast<MaintenanceRow>().Where(r=>r.RuleId.StartsWith("donor.")).All(r=>r.Recommendation!="Not assessed"&&r.Guidance.Contains("configuration and Restore checked")),"Adopted donor settings expose their actual qualified guidance rather than empty recommendation scaffolding");
        string expected=maintenance.Rows.Items.Cast<MaintenanceRow>().Count(MaintenanceGuidance.PendingRecommendation).ToString("N0");
        var card=(Button)((System.Windows.Controls.Primitives.UniformGrid)maintenance.Cards).Children[3];
        check(((StackPanel)card.Content).Children.OfType<TextBlock>().ElementAt(1).Text==expected&&card.ToolTip.ToString().Contains("recommended actions pending"),"Tweaks summary counts pending recommendations rather than all unapplied settings");
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
            var filter=GuidanceVisuals(maintenance).OfType<ComboBox>().First();
            var text=GuidanceVisuals(filter).OfType<ContentPresenter>().Single(x=>x.Name=="SelectionText");
            var arrow=GuidanceVisuals(filter).OfType<TextBlock>().Single(x=>x.Name=="DropDownArrow");
            double center(FrameworkElement element)=>element.TranslatePoint(new Point(0,element.ActualHeight/2),filter).Y;
            check(Math.Abs(center(text)-center(arrow))<1,"Category selection text and arrow are vertically aligned at "+theme+scale);
            await S1Verification.Snapshot(this,root,$"guidance-{theme}-{scale:0.0}");
        }
        maintenance.SearchForVerification("");Width=900;UpdateLayout();await S1Verification.Snapshot(this,root,"guidance-narrow");
        check(maintenance.Rows.ActualHeight>100,"Filtered settings retain usable rows with details in narrow layout");
        Settings.TextScale=1;Settings.NativeMode="dark";ApplyAppearance();Width=1500;
    }
    static IEnumerable<DependencyObject> GuidanceVisuals(DependencyObject node)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var nested in GuidanceVisuals(child))yield return nested;}
    }
}
#endif
