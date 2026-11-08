#if DEBUG
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

internal static class MaintenanceNativeVerification
{
    internal static async Task<int> Run(string root)
    {
        if(Environment.GetEnvironmentVariable("PATHWEFT_MAINTENANCE_GUEST")!="1"||!Path.GetFullPath(root).StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase))return 2;
        Directory.CreateDirectory(root);var checks=new List<object>();int result=0;
        void Check(bool pass,string criterion){checks.Add(new{criterion,passed=pass});if(!pass)result=1;}
        try
        {
            var prior=MaintenanceServices.Read("Fax");string journal=Path.Combine(root,"service.json");
            string disabled=MaintenanceServices.Change("Fax",false,journal);
            Check(MaintenanceServices.Read("Fax").Start==4&&Maintenance.Undo(journal).ContainsKey("service:Fax"),"Native SCM saves the actual service configuration before disabling the guest fixture");
            MaintenanceServices.Change("Fax",true,journal);var restored=MaintenanceServices.Read("Fax");
            Check(restored.Start==prior.Start&&restored.Delayed==prior.Delayed&&Maintenance.Undo(journal).Count==0,"Native SCM restores the original startup/delayed configuration without starting the service");
            var tweak=Maintenance.Tweaks.First(x=>x.Name=="Windows telemetry");string id="tweak:"+Maintenance.Id("tweak",tweak.Name);var edits=Maintenance.TweakEdits(id);var before=edits.Select(x=>x.Read()).ToArray();
            await using(var client=new MaintenanceClient())Check((await client.Start(new("apply",id,Path.Combine(root,"tweak.json"),null),false)).Ok,"Authenticated worker applies the selected machine-wide tweak inside the guest");
            Check(edits.All(x=>x.Same(x.Read())),"Actual registry readback matches every requested machine-wide value");
            await using(var client=new MaintenanceClient())Check((await client.Start(new("restore",id,Path.Combine(root,"tweak.json"),null),false)).Ok,"Authenticated worker restores the machine-wide original state");
            Check(before.All(x=>x.Same(x.Read())),"Absent and existing machine-wide values are restored exactly");
            var bad=new MaintenanceUndo("startup:bad",[new("HKEY_LOCAL_MACHINE",@"Software\Pathweft-unapproved","value",RegistryValueKind.String,JsonSerializer.SerializeToElement("invalid"),true)],[]);
            Maintenance.Save(new(){[bad.Id]=bad},Path.Combine(root,"invalid.json"));
            await using(var client=new MaintenanceClient())Check(!(await client.Start(new("restore",bad.Id,Path.Combine(root,"invalid.json"),null),false)).Ok,"Privileged restore refuses a malformed or unapproved saved target");
            await using(var client=new MaintenanceClient())Check(!(await client.Start(new("remove-app","not.whitelisted",null,null),false)).Ok,"Built-in app worker refuses arbitrary package identities");
            var packages=JsonSerializer.Deserialize<string[]>(await MaintenanceWorker.WindowsAction("ListApps"));Check(packages!=null,"Owned PowerShell child reads current-user package state and retires its native process tree");
            string files=Path.Combine(root,"files");Directory.CreateDirectory(files);File.WriteAllText(Path.Combine(files,"old"),"reviewed");
            using(var review=new CleanupReview(new("fixture","Fixture","Guest-only cleanup",files),Path.Combine(root,"review")))
            {
                review.Analyze(default);File.WriteAllText(Path.Combine(files,"new"),"new since review");var cleaned=review.Clean(default);
                Check(cleaned.Deleted==1&&File.Exists(Path.Combine(files,"new")),"Native exact-handle cleanup removes only reviewed guest files");
            }
        }
        catch(Exception ex){checks.Add(new{failure=ex.ToString()});result=1;}
        File.WriteAllText(Path.Combine(root,"native-results.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,result,checks},new JsonSerializerOptions{WriteIndented=true}));return result;
    }
}
#endif
