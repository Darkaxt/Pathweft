using System.IO;
using System.Reflection;
using System.Text.Json;

namespace FileExplorer;

// Descriptive data only. Typed Maintenance actions remain the sole write allowlist.
internal sealed record MaintenanceRule(string Id,string Kind,string Target,string Category,string Recommendation,
    string Action,string Reason,string FeatureLoss,string Source,int MinBuild,int MaxBuild,string Reviewed,
    string Qualified="",int QualifiedBuild=0)
{
    internal bool Supports(int build)=>build>=MinBuild&&build<=MaxBuild;
    internal string Applicability=>$"Windows builds {MinBuild}–{MaxBuild}";
}
internal static class MaintenanceGuidance
{
    internal sealed record Catalog(int Version,MaintenanceRule[] Rules);
    internal static readonly Catalog Data=Read();
    internal static int Build=>Environment.OSVersion.Version.Build;
    static Catalog Read()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("FileExplorer.MaintenanceGuidance.json")
            ??throw new InvalidDataException("Maintenance guidance resource is missing");
        var catalog=JsonSerializer.Deserialize<Catalog>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(catalog?.Version!=1||catalog.Rules.Length==0||catalog.Rules.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=catalog.Rules.Length||
           catalog.Rules.Select(r=>r.Kind+":"+r.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=catalog.Rules.Length)
            throw new InvalidDataException("Invalid maintenance guidance catalog");
        foreach(var r in catalog.Rules)
            if(r.Kind is not ("tweak" or "service" or "app")||r.Recommendation is not ("Keep enabled" or "Optional" or "Advanced")||
               string.IsNullOrWhiteSpace(r.Target)||string.IsNullOrWhiteSpace(r.Category)||string.IsNullOrWhiteSpace(r.Reason)||
               string.IsNullOrWhiteSpace(r.FeatureLoss)||!Uri.TryCreate(r.Source,UriKind.Absolute,out var uri)||uri.Scheme!="https"||
               r.MinBuild>r.MaxBuild||!DateOnly.TryParseExact(r.Reviewed,"yyyy-MM-dd",out _)||
               (r.Qualified.Length>0&&(!DateOnly.TryParseExact(r.Qualified,"yyyy-MM-dd",out _)||!r.Supports(r.QualifiedBuild))))
                throw new InvalidDataException("Invalid maintenance rule: "+r.Id);
        return catalog;
    }
    internal static MaintenanceRule Find(string kind,string target)=>Data.Rules.SingleOrDefault(r=>r.Kind==kind&&r.Target.Equals(target,StringComparison.OrdinalIgnoreCase));
    internal static MaintenanceRule For(string category,MaintenanceRow row)=>category switch
    {
        "Windows tweaks"=>Find("tweak",row.Name),"Services"=>Find("service",row.Type),"Windows apps"=>Find("app",row.Id),_=>null
    };
    internal static void RequireSupported(string id,int? build=null)
    {
        var tweak=Maintenance.Tweaks.SingleOrDefault(t=>"tweak:"+Maintenance.Id("tweak",t.Name)==id)
            ??throw new ArgumentException("Unsupported Windows tweak");
        var rule=Find("tweak",tweak.Name);
        if(rule==null||!rule.Supports(build??Build))throw new InvalidOperationException("This setting has not been qualified for this Windows build. Saved original settings can still be restored.");
    }
    internal static MaintenanceRow Describe(string category,MaintenanceRow row,int? build=null)
    {
        var rule=For(category,row);
        string group=rule?.Category??(category=="Startup"?row.Type:category=="Services"?"Other services":"Other apps");
        bool supported=rule?.Supports(build??Build)==true;
        if(row.State is "Access denied" or "Unavailable" or "Approval unknown")supported=false;
        string advice=supported&&rule.Qualified.Length>0?rule.Recommendation:"Not assessed";
        string explanation=rule==null?"No reviewed rule matches this exact entry. Its current state or recent resource use does not establish whether it can be disabled.":
            (supported?"":$"Not assessed for the current build/access state ({rule.Applicability}). ")+rule.Reason+"\nEffect: "+rule.FeatureLoss+"\nSuggested action: "+rule.Action+
            "\n"+rule.Applicability+" · reviewed "+rule.Reviewed+
            (rule.Qualified.Length>0?$" · configuration and Restore checked {rule.Qualified}, build {rule.QualifiedBuild}":" · configuration check pending");
        // Match the complete app-owned command; a familiar display name is not identity.
        if(category=="Startup"&&row.Type=="Registry Run"&&row.Details is {} command&&
           new[]{"--tray","--agent"}.Any(arg=>command.Equals("Registry64 · \""+Program.Executable+"\" "+arg,StringComparison.OrdinalIgnoreCase)))
        {advice="Keep enabled";explanation="This exact command starts Pathweft's tray or background agent at sign-in. Disabling it prevents that component from starting automatically; manual launch remains available.";}
        return row with{Group=group,Recommendation=advice,Guidance=explanation,Source=rule?.Source??"",RuleId=rule?.Id??"",
            CanChange=row.CanChange&&(category!="Windows tweaks"||supported)};
    }
}
