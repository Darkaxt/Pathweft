#if DEBUG
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FileExplorer;
public partial class MainWindow
{
    internal async Task VerifyCloudQuota(string root,Action<bool,string> check)
    {
        byte[] N(ulong n){var b=new List<byte>();while(n>=128){b.Add((byte)((n&127)|128));n>>=7;}b.Add((byte)n);return b.ToArray();}
        byte[] V(int field,ulong n)=>N((ulong)field<<3).Concat(N(n)).ToArray();
        byte[] B(int field,byte[] data)=>N(((ulong)field<<3)|2).Concat(N((ulong)data.Length)).Concat(data).ToArray();
        byte[] Join(params byte[][] data)=>data.SelectMany(b=>b).ToArray();
        string mask=string.Join(',',new[]{"quota_type","bytes_limit","bytes_used_by_all_services","bytes_remaining"}.Select(n=>"account.metadata.quota."+n));
        byte[] Account(ulong total,ulong used,ulong remaining,ulong kind=1,ulong version=1)=>Join(V(1,version),B(2,B(3,B(1,Join(V(6,kind),V(7,total),V(8,remaining),V(10,used))))));
        const long total=5497558138880,used=2148148553145;
        var valid=Account(total,used,total-used);
        check(CloudVolumes.ReadGoogleQuota(valid,mask)==new CloudVolumes.Quota(total,used),"Quota decoder preserves 64-bit cloud byte counters and validates remaining space");
        check(CloudVolumes.ReadGoogleQuota(Account(100,120,0),mask)?.Free==0,"An over-quota account retains actual usage and has zero available space");
        check(new[]{Account(0,0,0),Account(100,20,80,0),Account(100,20,80,2),Account(100,20,80,1,2),Account(100,20,79),valid[..^1]}.All(b=>CloudVolumes.ReadGoogleQuota(b,mask)==null)&&CloudVolumes.ReadGoogleQuota(valid,"")==null,"Unlimited, unknown version/type, inconsistent and truncated records never become fabricated finite capacity");
        byte[] Mount(string drive,string id)=>B(1,Join(B(1,B(1,Encoding.UTF8.GetBytes(id))),B(2,Encoding.UTF8.GetBytes(drive))));
        var mounts=CloudVolumes.ReadGoogleMounts(Join(Mount("X:","123"),Mount("Y:","456")));
        check(mounts["X:\\"]=="123"&&mounts["Y:\\"]=="456"&&CloudVolumes.ReadGoogleMounts(Mount("Z:","123"))["Z:\\"]==mounts["X:\\"],"Provider account mapping follows letter reassignment and distinguishes accounts");
        check(CloudVolumes.ReadGoogleMounts(Join(Mount("X:","123"),Mount("X:","456")))["X:\\"]==""&&CloudVolumes.ReadGoogleMounts(Mount("X:","../outside"))["X:\\"]=="","Ambiguous and unsafe account bindings retain a recognized mount without reading another path");
        string absent=Path.Combine(root,"absent-metadata");
        check(CloudVolumes.ReadQuotaDatabase(absent)==null&&!File.Exists(absent),"Read-only SQLite rejects an absent database without creating it");
        async Task Settle()=>await Dispatcher.InvokeAsync(UpdateLayout,DispatcherPriority.ApplicationIdle);
        SetSinglePane(true);SetActive(Left);Settings.Stats=2;ApplyStats();
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Left.driveListingWork;
        RefreshDrives();await driveRefreshWork;
        await Settle();
        var actual=shownDrives.ToArray();
        var cloud=actual.Where(d=>d.Connection=="Google Drive").ToArray();
        check(cloud.Length==2&&cloud.All(d=>d.CloudQuota&&d.Total>0)&&cloud.Select(d=>d.Total).Order().SequenceEqual(new[]{16106127360L,total}),"Both real mounted Google accounts expose their respective 15 GiB and 5 TiB cloud quota through read-only metadata");
        foreach(var d in actual)
        {
            var entry=Left.Items.Single(e=>e.Path==d.Name);
            check(entry.DriveTotal==d.Total&&entry.DriveFree==d.Free,"This PC and sidebar share one capacity snapshot: "+d.Name);
            if(d.CloudQuota)
            {
                var quota=CloudVolumes.GoogleQuota(CloudVolumes.GoogleMounts()[d.Name]);
                check(quota.Total==d.Total&&quota.Free==d.Free&&entry.TotalText.EndsWith(d.Total==total?"TiB":"GiB")&&entry.Tip.Contains("Mount-reported space (cache constrained)"),"Cloud units and separately labelled mount capacity agree with the account metadata: "+d.Name);
                var text=((Grid)DriveButton(d).Content).Children.OfType<TextBlock>().Last().Text;
                check(text==$"{quota.Used/1073741824.0:N0} / {quota.Total/1073741824.0:N0} GiB","Sidebar reports account usage/quota rather than cache-volume usage: "+d.Name);
            }
            else
            {
                var volume=new DriveInfo(d.Name);
                check(d.Total==volume.TotalSize&&!entry.DriveCloudQuota,"Local/network reported capacity retains its existing source: "+d.Name);
            }
        }
        File.WriteAllText(Path.Combine(root,"cloud-capacity.json"),JsonSerializer.Serialize(cloud.Select(d=>new{d.Name,d.Total,d.Free,d.CloudUsed,d.MountTotal,d.MountFree}),new JsonSerializerOptions{WriteIndented=true}));
        foreach(string theme in new[]{"dark","light"})foreach(double scale in new[]{1.0,1.5})
        {
            Settings.NativeTheme=true;Settings.NativeMode=theme;Settings.TextScale=scale;ApplyAppearance();Width=1280;Height=850;
            await S1Verification.Navigate(Left,PaneView.ThisPC);await Left.driveListingWork;await Settle();
            check(StatsRow.Visibility==Visibility.Collapsed,"The cloud drive view retains the reclaimed statistics space: "+theme+scale);
            await S1Verification.Snapshot(this,root,$"cloud-thispc-{theme}-{scale*100:0}");
        }
        Settings.TextScale=1;ApplyAppearance();
        foreach(var d in cloud)
        {
            await S1Verification.Navigate(Left,d.Name);await Settle();
            check(((TextBlock)Left.FindName("DriveInfoText")).Text==DrivePresentation.Capacity(d.Total,d.Free,true)&&StatFree.Text==DrivePresentation.Size(d.Free,true)&&StatUsed.Text.Contains(DrivePresentation.Size(d.Used,true)),"Actual cloud folder header and capacity card use the account quota: "+d.Name);
        }
        await S1Verification.Navigate(Left,PaneView.ThisPC);await Left.driveListingWork;
        var unknown=WithGoogleQuota(new("X:\\","Streamed account","FAT32",2000000000000,851300000000,false,"Streamed account",DriveType.Fixed),null);
        Left.RefreshThisPC(new[]{unknown});await Settle();
        var missing=Left.Items.Single();
        check(missing.Path=="X:\\"&&!missing.HasDriveCapacity&&missing.CapacityText=="Cloud quota unavailable"&&missing.TotalText=="Unavailable"&&missing.FreeText=="","A ready account with unavailable quota remains navigable without a cache-sized bar or fake percentage");
        var button=DriveButton(unknown);
        check(((Grid)button.Content).Children.OfType<TextBlock>().Last().Text=="Quota unavailable"&&((Grid)button.Content).Children.OfType<Grid>().Single(g=>Grid.GetRow(g)==1).Visibility==Visibility.Collapsed,"Sidebar retains an unknown-quota mount and hides its misleading capacity bar");
        Left.RefreshThisPC(actual);await Settle();
        check(HiddenTrialPresentation.ForegroundViolations==0,"Cloud quota verification uses no foreground activation, token access or client settings change");
    }
}
#endif
