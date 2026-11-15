#if DEBUG
using System.IO;

namespace FileExplorer;

public partial class MainWindow
{
    // S12b on the real Tresorit Drive, read-only: the drive row, the storage label, which folders count as tresors, and
    // the Tresorit extension's commands for a tresor and an item in it, listed and never invoked. Nothing is created,
    // changed or deleted there, and no real names are reported.
    internal async Task VerifyTresorit(string root,Action<bool,string> check)
    {
        var drives=await Task.Run(()=>ReadDrives());
        var drive=drives.FirstOrDefault(d=>d.Connection=="Tresorit");
        check(drive!=null,"A Tresorit Drive is mounted for S12b qualification");
        if(drive==null)return;
        check(drive.Format=="Cloud"&&drive.RawFormat.Length>0,"Tresorit Drive shows as a cloud drive and keeps its reported filesystem");
        check(DrivePresentation.SpaceSource(drive.Connection,drive.CloudQuota,drive.MountTotal,drive.MountFree)==DrivePresentation.TresoritSource
            &&PaneView.DriveEntries(new[]{drive}).Single().Tip.Contains(DrivePresentation.TresoritSource),"TRES-08: the drive's space reads as the Tresorit account's storage, not a disk");
        check(DrivePresentation.SpaceSource("Network",false,0,0)==null,"Other network drives get no provider storage label");
        var tresor=Directory.EnumerateDirectories(drive.Name).FirstOrDefault();
        check(tresor!=null,"Tresorit Drive lists at least one tresor");
        if(tresor==null)return;
        var child=Directory.EnumerateDirectories(tresor).FirstOrDefault()??Directory.EnumerateFiles(tresor).FirstOrDefault();
        check(CloudVolumes.IsTresor(tresor)&&CloudVolumes.OnTresorit(tresor),"A folder directly on the drive is a tresor");
        check(!CloudVolumes.OnTresorit(root)&&!CloudVolumes.IsTresor(Path.GetPathRoot(root))&&!CloudVolumes.OnTresorit(@"C:\Windows"),"Local folders are not on Tresorit Drive");
        check(File.GetAttributes(tresor).HasFlag(FileAttributes.ReadOnly),"The tresor keeps the ReadOnly attribute Tresorit gives it");
        foreach(var (kind,path) in new[]{("main folder",tresor),(child==null?null:Directory.Exists(child)?"child folder":"child file",child)})
        {
            if(path==null){check(false,"The tresor has an item to list the Tresorit commands for");continue;}
            if(kind!="main folder")check(CloudVolumes.OnTresorit(path)&&!CloudVolumes.IsTresor(path),"An item inside a tresor is on Tresorit Drive but not a tresor");
            using var shell=ShellMenu.LoadExtensions(this,new[]{path});
            var texts=shell.Found.TryGetValue("tresorit",out var nodes)?nodes.Where(n=>n.Text!=null).Select(n=>n.Text.Replace("&","")).ToList():new List<string>();
            File.AppendAllText(Path.Combine(root,"tresorit-verbs.txt"),kind+": "+string.Join(" | ",texts)+Environment.NewLine);
            check(texts.Contains("Sharing and members")&&texts.Contains("Copy Tresorit Path"),"TRES-05: Pathweft's menu lists Tresorit's Sharing and members and Copy Tresorit Path for a "+kind+" (not invoked)");
            check(shell.Found.Keys.All(k=>k=="tresorit"),"TRES-05: only the Tresorit extension is loaded on Tresorit Drive for a "+kind);
        }
        using(var local=ShellMenu.LoadExtensions(this,new[]{root}))
            check(!local.Found.ContainsKey("tresorit"),"TRES-05: the Tresorit extension is not adopted off Tresorit Drive");
    }
}
#endif
