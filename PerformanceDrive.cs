using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer;

internal sealed partial class PerformancePanel
{
    readonly StackPanel driveHealthPanel=new(){Visibility=Visibility.Collapsed,Margin=new(0,9,0,1)};
    readonly TextBlock driveName=new(){FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,FontSize=12};
    readonly TextBlock driveFacts=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,3,0,0)};
    readonly Button drivePermission=new(){Content="Use administrator access",Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left};
    DriveHealth driveHealth;uint? healthDisk;bool healthElevated,healthReading;DateTimeOffset healthAt;
    Task healthWork=Task.CompletedTask;
    void BuildDriveHeader(StackPanel title)
    {
        driveHealthPanel.Children.Add(driveName);driveHealthPanel.Children.Add(driveFacts);driveHealthPanel.Children.Add(drivePermission);
        driveFacts.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        drivePermission.SetResourceReference(StyleProperty,"AuxiliaryAction");
        AutomationProperties.SetName(driveHealthPanel,"Current physical drive health");
        drivePermission.Click+=async(_,_)=>
        {
            drivePermission.IsEnabled=false;
            try{if(await AdministratorSession.Enable()){healthAt=default;Refresh();}}
            finally{drivePermission.IsEnabled=true;}
        };
        title.Children.Add(driveHealthPanel);
    }
    void RefreshDrive(PerformanceSample[] samples)
    {
        driveHealthPanel.Visibility=selected==Drive?Visibility.Visible:Visibility.Collapsed;
        bool activity=selected==Drive&&analysisKind.SelectedIndex==0;
        if(!activity)analysisGraph.SetDrive(null);
        else if(!Historical)analysisGraph.SetDrive(samples.Select(s=>
        {
            var d=WindowsPerformanceCounters.ForDisk(s,diskNumber);
            return new DrivePoint(s.Time,d?.Read,d?.Write,d?.LatencyMs,d?.Busy);
        }).ToArray());
        if(selected!=Drive)return;
        if(healthDisk!=diskNumber||healthElevated!=AdministratorSession.Available)
        {driveHealth=null;healthAt=default;healthDisk=diskNumber;healthElevated=AdministratorSession.Available;}
        if(diskNumber==null)
        {
            driveName.Text="Physical drive unavailable";
            driveFacts.Text="This path is virtual, remote, unavailable or spans physical drives. Its host drive's measurements are not substituted.";
            driveName.SetResourceReference(TextBlock.ForegroundProperty,"Muted");drivePermission.Visibility=Visibility.Collapsed;return;
        }
        var health=driveHealth;
        driveName.Text=health==null?$"Physical drive {diskNumber} · Reading health…":$"{health.Name} · {health.Status.Text}";
        driveName.SetResourceReference(TextBlock.ForegroundProperty,health?.Status.Level switch{"bad"=>"TransferError","warn"=>"TransferWarn",_=>"Palette.E8EEF6"});
        driveFacts.Text=health==null?"":health.Details.Length>0?health.Details:health.PermissionRequired?"Additional health details require administrator access.":"This device does not expose additional health details through Windows.";
        driveFacts.ToolTip=health==null?null:$"Physical drive {health.Disk} · {health.Source??"Windows Storage"} · {health.Observed.ToLocalTime():G}"+(health.Issue==null?"":"\n"+health.Issue)+"\nCurrent health; independent of the activity timeline. TB uses decimal bytes. All partitions on this physical unit contribute to I/O.";
        drivePermission.Visibility=health?.PermissionRequired==true&&!AdministratorSession.Available?Visibility.Visible:Visibility.Collapsed;
        if(!healthReading&&Showing&&(healthAt==default||DateTimeOffset.UtcNow-healthAt>=TimeSpan.FromMinutes(1)))ReadDriveHealth(diskNumber.Value,AdministratorSession.Available);
    }
    async void ReadDriveHealth(uint number,bool elevated)
    {
        healthReading=true;
        // One finite read in flight, no inventory watcher, timer, retained device
        // handle or unbounded cache. Hidden/tray views never poll SMART.
        var work=Task.Run(async()=>
        {
            if(!elevated)return WindowsDriveHealth.Read(number);
            await using var client=new MaintenanceClient();
            var reply=await client.Start(new("disk-health",number.ToString(System.Globalization.CultureInfo.InvariantCulture),null,null),true).ConfigureAwait(false);
            return reply.Ok&&reply.Health!=null?reply.Health:throw new IOException(reply.Message);
        });healthWork=work;
        DriveHealth health;
        try{health=await work;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or TypeLoadException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {health=new(number,$"Physical drive {number}",DateTimeOffset.UtcNow,Issue:error.Message,PermissionRequired:error is UnauthorizedAccessException);}
        finally{healthReading=false;}
        if(disposed)return;
        if(diskNumber==number&&AdministratorSession.Available==elevated){driveHealth=health;healthAt=DateTimeOffset.UtcNow;}
        Refresh();
    }
#if DEBUG
    internal Task DriveReadyForVerification=>healthWork;
    internal DriveHealth DriveHealthForVerification=>driveHealth;
    internal string DriveFactsForVerification=>driveName.Text+" · "+driveFacts.Text;
#endif
}
