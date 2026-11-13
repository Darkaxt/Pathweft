#if DEBUG
using System.IO;
using System.Text.Json;

namespace FileExplorer;

// MAINT-45 guest qualification of the S47c hardening tweaks (MAINT-42) and the typed component action (MAINT-43).
// Runs only in a disposable Windows Sandbox guest; every change is restored and read back.
internal static class MaintenanceHardeningVerification
{
    internal static readonly string[] Names=["Network protection in Microsoft Defender","Potentially unwanted app blocking","Microsoft Defender sandbox","LSA protection",
        "Software installed by firmware (WPBT)","PowerShell 2.0 engine","WMIC command-line tool","VBScript"];
    // Present in a default Windows 11 guest and safe to toggle there; the first one enabled is used.
    static readonly string[] FeatureCandidates=["WorkFolders-Client","Printing-XPSServices-Features","SearchEngine-Client-Package","MSRDC-Infrastructure"];

    internal static async Task<int> Run(string root)
    {
        if(Environment.GetEnvironmentVariable("PATHWEFT_MAINTENANCE_GUEST")!="1"||!Path.GetFullPath(root).StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase)||
            !Path.GetFullPath(Maintenance.Journal).StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase))return 2;
        Directory.CreateDirectory(root);var checks=new List<object>();var qualified=new List<string>();int result=0;
        void Save(){lock(checks)File.WriteAllText(Path.Combine(root,"hardening-results.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,build=MaintenanceGuidance.Build,result,qualified,checks},new JsonSerializerOptions{WriteIndented=true}));}
        void Check(bool pass,string criterion){checks.Add(new{criterion,passed=pass});if(!pass)result=1;Save();}
        void Note(string name,object value){checks.Add(new{note=name,value});Save();}
        try
        {
            foreach(string name in Names)
            {
                var tweak=Maintenance.Tweaks.Single(t=>t.Name==name);string id="tweak:"+Maintenance.Id("tweak",name);var rule=MaintenanceGuidance.Find("tweak",name);
                if(!rule.Supports(MaintenanceGuidance.Build)){Note(rule.Id,"not applicable on build "+MaintenanceGuidance.Build);continue;}
                var row=Maintenance.Read("Windows tweaks",default).Single(r=>r.Id==id);
                var states=tweak.Components.Select(MaintenanceFeatures.Installed).ToArray();
                if(tweak.Components.Length>0&&!states.Any(s=>s==true))
                {
                    Check(!row.CanChange&&row.State==(states.All(s=>s==null)?"Not present on this Windows":"Not installed"),"An absent component reads as not present and cannot be selected: "+rule.Id+" ("+row.State+")");
                    Note(rule.Id,"component absent in this guest; Apply and Restore not exercised");continue;
                }
                var edits=Maintenance.TweakEdits(id);var before=edits.Select(x=>x.Read()).ToArray();
                // The guest profile's own journal: the list reads it, so a policy-only tweak reads as Applied rather than set outside Pathweft.
                string journal=Maintenance.Journal;
                var componentsBefore=tweak.Components.Length>0?MaintenanceFeatures.Read(tweak.Components):[];
                bool restored=false;
                try
                {
                    MaintenanceReply applied;
                    await using(var client=new MaintenanceClient())applied=await client.Start(new("apply",id,journal,null),false);
                    Check(applied.Ok,"Native worker Apply "+rule.Id+": "+applied.Message);
                    Check(edits.All(x=>x.Same(x.Read())),"Every target has the requested kind and value: "+rule.Id);
                    Check(Maintenance.Undo(journal)[id].Before.Zip(before,(a,b)=>a.Same(b)).All(x=>x),"Saved original values are exact: "+rule.Id);
                    if(tweak.Components.Length>0)
                    {
                        Check(MaintenanceFeatures.Read(tweak.Components).All(c=>!c.Installed)&&(Maintenance.Undo(journal)[id].Components?.Length??0)==componentsBefore.Length,
                            "Components are removed and their original states journaled: "+rule.Id);
                        Note(rule.Id+":apply",applied.Message);
                    }
                    Check(Maintenance.Read("Windows tweaks",default).Single(r=>r.Id==id).State=="Applied","The list reads the change back as Applied: "+rule.Id);
                }
                finally
                {
                    if(Maintenance.Undo(journal).ContainsKey(id))
                    {
                        MaintenanceReply back;
                        await using(var client=new MaintenanceClient())back=await client.Start(new("restore",id,journal,null),false);
                        // A removed capability comes back from Windows Update; without network that is recorded, not claimed.
                        if(tweak.Components.Any(c=>c.Capability)&&!back.Ok)Note(rule.Id+":restore",back.Message);
                        else Check(back.Ok,"Native worker Restore "+rule.Id+": "+back.Message);
                        restored=back.Ok;
                    }
                    Check(before.All(x=>x.Same(x.Read())),"Registry values returned exactly to their prior state: "+rule.Id);
                    if(restored&&tweak.Components.Length>0)
                        Check(MaintenanceFeatures.Read(tweak.Components).Select(c=>(c.Name,c.Installed)).SequenceEqual(componentsBefore.Select(c=>(c.Name,c.Installed))),"Components returned to their journaled states: "+rule.Id);
                }
                if(restored||tweak.Components.Length==0)qualified.Add(rule.Id);
            }
            // Typed feature action on a feature this guest has enabled: disable keeping the payload, then re-enable offline.
            var feature=FeatureCandidates.Select(n=>new WindowsComponent(n,false)).FirstOrDefault(c=>MaintenanceFeatures.Installed(c)==true);
            if(feature==null)Note("feature","None of "+string.Join(", ",FeatureCandidates)+" is enabled in this guest");
            else
            {
                bool off=MaintenanceFeatures.Set([new ComponentState(feature.Name,false,false)]);
                var state=MaintenanceFeatures.Read([feature]).Single();
                Check(!state.Installed&&MaintenanceFeatures.Installed(feature)!=true,"Servicing API disables optional feature "+feature.Name+(off?" (restart pending)":""));
                bool on=MaintenanceFeatures.Set([new ComponentState(feature.Name,false,true)]);
                Check(MaintenanceFeatures.Read([feature]).Single().Installed,"Servicing API re-enables "+feature.Name+" from its retained payload"+(on?" (restart pending)":""));
                Note("feature",new{feature.Name,disableRestart=off,enableRestart=on});qualified.Add("feature:"+feature.Name);
            }
            Check(!MaintenanceFeatures.Read([new WindowsComponent("Pathweft-Fixture-Not-A-Feature",false)]).Any(),"An unknown feature is left out rather than guessed");
        }
        catch(Exception ex){checks.Add(new{failure=ex.ToString()});result=1;}
        Save();return result;
    }
}
#endif
