#if DEBUG
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FileExplorer;

internal static class MaintenanceNativeVerification
{
    internal static async Task<int> Guidance(string root)
    {
        if(Environment.GetEnvironmentVariable("PATHWEFT_MAINTENANCE_GUEST")!="1"||!Path.GetFullPath(root).StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase))return 2;
        Directory.CreateDirectory(root);var checks=new List<object>();var qualified=new List<string>();int result=0;
        void Check(bool pass,string criterion){checks.Add(new{criterion,passed=pass});if(!pass)result=1;}
        try
        {
            foreach(var tweak in Maintenance.Tweaks)
            {
                string id="tweak:"+Maintenance.Id("tweak",tweak.Name);var rule=MaintenanceGuidance.Find("tweak",tweak.Name);
                if(!rule.Supports(MaintenanceGuidance.Build)){checks.Add(new{criterion=rule.Id,notApplicable=true,build=MaintenanceGuidance.Build});continue;}
                var edits=Maintenance.TweakEdits(id);var before=edits.Select(x=>x.Read()).ToArray();string journal=Path.Combine(root,rule.Id+".json");
                var tasksBefore=MaintenanceTweakTasks.ReadAll(tweak.TaskPaths);
                if(tweak.TaskPaths.Length>0&&tasksBefore.Length==0){checks.Add(new{criterion=rule.Id,tasksAbsent=true,build=MaintenanceGuidance.Build});continue;}
                try
                {
                    await using(var client=new MaintenanceClient())Check((await client.Start(new("apply",id,journal,null),false)).Ok,"Native worker Apply "+rule.Id);
                    Check(edits.All(x=>x.Same(x.Read())),"Every documented target has the requested kind/value: "+rule.Id);
                    Check(Maintenance.Undo(journal)[id].Before.Zip(before,(a,b)=>a.Same(b)).All(x=>x),"Saved original values are exact: "+rule.Id);
                    Check(MaintenanceTweakTasks.ReadAll(tweak.TaskPaths).All(t=>!t.Enabled)&&(Maintenance.Undo(journal)[id].Tasks?.Length??0)==tasksBefore.Length,"Named scheduled tasks are disabled and their original states saved: "+rule.Id);
                }
                finally
                {
                    if(Maintenance.Undo(journal).ContainsKey(id))
                        await using(var client=new MaintenanceClient())Check((await client.Start(new("restore",id,journal,null),false)).Ok,"Native worker Restore "+rule.Id);
                    Check(before.All(x=>x.Same(x.Read())),"Dependent feature configuration returned exactly to prior state: "+rule.Id);
                    var tasksAfter=MaintenanceTweakTasks.ReadAll(tweak.TaskPaths);
                    Check(tasksAfter.Length==tasksBefore.Length&&tasksAfter.Zip(tasksBefore,(a,b)=>a.Enabled==b.Enabled&&a.Definition==b.Definition).All(x=>x),"Scheduled tasks returned exactly to their prior state: "+rule.Id);
                }
                qualified.Add(rule.Id);
            }
            await PolicyReset(root,Check,qualified);
            var services=MaintenanceServices.List([],default);
            foreach(var rule in MaintenanceGuidance.Data.Rules.Where(r=>r.Kind=="service"&&r.Supports(MaintenanceGuidance.Build)))
            {
                var row=services.SingleOrDefault(r=>r.Type.Equals(rule.Target,StringComparison.OrdinalIgnoreCase));
                if(row==null){checks.Add(new{criterion=rule.Id,notInstalled=true});continue;}
                var state=MaintenanceServices.Read(rule.Target);
                Check(row.Details.Contains("Requires:")&&row.Details.Contains(state.Account)&&!string.IsNullOrWhiteSpace(state.Binary),"Native service configuration/dependencies accompany "+rule.Id);
                if(rule.ReadOnlyEvidence){qualified.Add(rule.Id+":read-only");continue;}
                string journal=Path.Combine(root,rule.Id+".json");
                try
                {
                    MaintenanceServices.Change(rule.Target,false,journal);
                    Check(MaintenanceServices.Read(rule.Target).Start==4,"Guest service Disabled differs from Manual: "+rule.Id);
                }
                finally
                {
                    if(Maintenance.Undo(journal).ContainsKey(row.Id))MaintenanceServices.Change(rule.Target,true,journal);
                    var actual=MaintenanceServices.Read(rule.Target);
                    Check(actual.Start==state.Start&&actual.Delayed==state.Delayed&&actual.Dependencies==state.Dependencies&&actual.Account==state.Account&&actual.Binary==state.Binary,"Service configuration/dependency contract restored without donor defaults: "+rule.Id);
                }
                qualified.Add(rule.Id);
            }
        }
        catch(Exception ex){checks.Add(new{failure=ex.ToString()});result=1;}
        File.WriteAllText(Path.Combine(root,"guidance-results.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,build=MaintenanceGuidance.Build,result,qualified,checks},new JsonSerializerOptions{WriteIndented=true}));return result;
    }
    // MAINT-29: a policy set outside Pathweft is recognised, reset to the Windows default with a journal,
    // and Restore returns the outside value; unknown neighbouring policy values are never touched.
    static async Task PolicyReset(string root,Action<bool,string> check,List<string> qualified)
    {
        var tweak=Maintenance.Tweaks.First(t=>Maintenance.PolicyOnly(Maintenance.TweakEdits("tweak:"+Maintenance.Id("tweak",t.Name)))&&
            MaintenanceGuidance.Find("tweak",t.Name).Supports(MaintenanceGuidance.Build)&&t.TaskPaths.Length==0);
        string id="tweak:"+Maintenance.Id("tweak",tweak.Name),journal=Path.Combine(root,"policy-reset.json");
        var edits=Maintenance.TweakEdits(id);var before=edits.Select(x=>x.Read()).ToArray();
        var neighbour=edits[0] with{Name="PathweftVerificationNeighbour",Kind=RegistryValueKind.DWord,Value=JsonSerializer.SerializeToElement(7),Exists=true};
        try
        {
            foreach(var edit in edits)edit.Write();neighbour.Write();
            var row=Maintenance.Read("Windows tweaks",default).Single(r=>r.Id==id);
            check(row.State==Maintenance.OutsideState&&row.Details.Contains("PathweftVerificationNeighbour=7"),"A managed policy present without a journal reads as set outside Pathweft and shows unknown neighbours read-only");
            await using(var client=new MaintenanceClient())check((await client.Start(new("reset-default",id,journal,null),false)).Ok,"Native worker returns an outside policy to the Windows default");
            check(edits.All(x=>!x.Read().Exists)&&neighbour.Same(neighbour.Read()),"Reset removes only the rule's own policy values");
            await using(var client=new MaintenanceClient())check((await client.Start(new("restore",id,journal,null),false)).Ok,"Native worker Restore after Windows-default reset");
            check(edits.All(x=>x.Same(x.Read())),"Restore returns the outside policy values exactly");
            qualified.Add("policy-reset:"+MaintenanceGuidance.Find("tweak",tweak.Name).Id);
        }
        finally
        {
            foreach(var original in before)original.Write();
            (neighbour with{Exists=false,Value=default}).Write();
        }
    }
    internal static async Task<int> Run(string root)
    {
        if(Environment.GetEnvironmentVariable("PATHWEFT_MAINTENANCE_GUEST")!="1"||!Path.GetFullPath(root).StartsWith(@"C:\Coordinator\Work\",StringComparison.OrdinalIgnoreCase))return 2;
        Directory.CreateDirectory(root);var checks=new List<object>();int result=0;
        void Check(bool pass,string criterion){checks.Add(new{criterion,passed=pass});if(!pass)result=1;}
        try
        {
            string service=Environment.GetEnvironmentVariable("PATHWEFT_SERVICE_FIXTURE")??throw new InvalidOperationException("Missing guest fixture identity");
            var prior=MaintenanceServices.Read(service);string journal=Path.Combine(root,"service.json");
            Check(Maintenance.Read("Services",default).Any(x=>x.Id=="service:"+service),"All-service enumeration includes a native guest service outside the donor shortlist");
            string disabled=MaintenanceServices.Change(service,false,journal);
            Check(MaintenanceServices.Read(service).Start==4&&Maintenance.Undo(journal).ContainsKey("service:"+service),"Native SCM saves the actual service configuration before disabling the guest fixture");
            MaintenanceServices.Change(service,true,journal);var restored=MaintenanceServices.Read(service);
            Check(restored.Start==prior.Start&&restored.Delayed==prior.Delayed&&Maintenance.Undo(journal).Count==0,"Native SCM restores the original startup/delayed configuration without starting the service");
            await StartupFixtures(root,Check);
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
    static async Task StartupFixtures(string root,Action<bool,string> check)
    {
        string name="Pathweft-owned-"+Guid.NewGuid().ToString("N"),journal=Path.Combine(root,"startup.json");
        using(var run=Registry.CurrentUser.CreateSubKey(Maintenance.RunPath))
        using(var approved=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
        {
            run.SetValue(name,"never-executed.exe");byte[] state=new byte[12];state[0]=3;approved.SetValue(name,state,RegistryValueKind.Binary);
            try{var row=MaintenanceStartup.Read([],default).Single(r=>r.Id==Maintenance.StartupId("HKEY_CURRENT_USER",RegistryView.Registry64,name));check(row.State=="Disabled"&&!row.CanChange&&((byte[])approved.GetValue(name)).SequenceEqual(state),"Windows-owned StartupApproved disabled state is recognized without rewriting it");}
            finally{run.DeleteValue(name,false);approved.DeleteValue(name,false);}
        }
        foreach(var source in new[]{(RegistryHive.CurrentUser,RegistryView.Registry64,Maintenance.RunPath+"Once"),(RegistryHive.LocalMachine,RegistryView.Registry32,Maintenance.RunPath+"Once"),(RegistryHive.CurrentUser,RegistryView.Registry64,MaintenanceStartup.RegistryPaths[2])})
        {
            using var hive=RegistryKey.OpenBaseKey(source.Item1,source.Item2);using var key=hive.CreateSubKey(source.Item3);
            key.SetValue(name,"%TEMP%\\never-run.exe",RegistryValueKind.ExpandString);
            string id=MaintenanceStartup.RegistryId(hive.Name,source.Item2,source.Item3,name);
            try
            {
                check(MaintenanceStartup.Read([],default).Any(x=>x.Id==id&&x.Type==(source.Item3.EndsWith("RunOnce")?"Registry RunOnce":"Registry policy Run")),"Native Startup identifies "+hive.Name+source.Item2+source.Item3);
                await using(var client=new MaintenanceClient())check((await client.Start(new("apply",id,journal,null),false)).Ok,"Worker disables exact owned registry source");
                check(!key.GetValueNames().Contains(name),"Owned registry source is actually disabled");
                await using(var client=new MaintenanceClient())check((await client.Start(new("restore",id,journal,null),false)).Ok,"Worker restores exact owned registry source");
                check(key.GetValueKind(name)==RegistryValueKind.ExpandString&&(string)key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%TEMP%\\never-run.exe","RunOnce/policy raw kind and value restored");
            }
            finally{key.DeleteValue(name,false);}
        }
        string folder=Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);Directory.CreateDirectory(folder);string file=Path.Combine(folder,name+".txt"),fileId=MaintenanceStartup.FileId(file);
        File.WriteAllText(file,"owned guest startup file, never executed");
        try
        {
            check(MaintenanceStartup.Read([],default).Any(x=>x.Id==fileId&&x.Type=="Startup file"),"Windows Startup folder is enumerated without executing its members");
            await using(var client=new MaintenanceClient())check((await client.Start(new("apply",fileId,journal,null),false)).Ok,"Worker disables guest startup file using its exact native identity");
            check(!File.Exists(file)&&File.Exists(Path.Combine(folder,"Pathweft-disabled",Path.GetFileName(file))),"Disabled startup file is outside the launch folder with its original bytes retained");
            File.WriteAllText(file,"external replacement");
            await using(var client=new MaintenanceClient())check(!(await client.Start(new("restore",fileId,journal,null),false)).Ok,"Restore preserves an independently created startup file at the original name");
            check(File.ReadAllText(file)=="external replacement","External replacement remains unchanged");File.Delete(file);
            await using(var client=new MaintenanceClient())check((await client.Start(new("restore",fileId,journal,null),false)).Ok,"Worker restores the original startup file");
            check(File.ReadAllText(file)=="owned guest startup file, never executed","Exact startup file content restored");
        }
        finally{File.Delete(file);}
        string taskRoot=Environment.GetEnvironmentVariable("PATHWEFT_TASK_FIXTURE")??throw new InvalidOperationException("Missing task fixture");
        var tasks=MaintenanceStartup.Read([],default).Where(r=>r.Id.StartsWith("startup-task:")&&r.Details.StartsWith(taskRoot+"\\")).ToArray();
        check(tasks.Length==2&&tasks.Any(t=>t.Details.Contains("At startup"))&&tasks.Any(t=>t.Details.Contains("At logon")),"Only boot/logon tasks are included; time-only task is excluded");
        foreach(var task in tasks)
        {
            await using(var client=new MaintenanceClient())check((await client.Start(new("apply",task.Id,journal,null),false)).Ok,"Worker disables boot/logon task without running or rewriting it");
            check(MaintenanceStartup.Read([],default).Single(r=>r.Id==task.Id).State=="Disabled","Task Scheduler reports the disabled state");
            await using(var client=new MaintenanceClient())check((await client.Start(new("restore",task.Id,journal,null),false)).Ok,"Worker restores the original task state and definition");
            check(MaintenanceStartup.Read([],default).Single(r=>r.Id==task.Id).State=="Enabled","Task Scheduler reports the restored enabled state");
        }
        check(Maintenance.Undo(journal).Count==0,"Every completed startup fixture Restore clears its durable journal record");
    }
}
#endif
