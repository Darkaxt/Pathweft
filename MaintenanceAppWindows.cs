using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace FileExplorer;

// Uninstall confirmation (MAINT-34/35): names every app, says there is no Restore, and offers a
// restore point first (on when a desktop program is included) and quiet uninstall where declared.
internal sealed class UninstallConfirmWindow : Window
{
    readonly CheckBox restorePoint=new(){Content="Create a Windows restore point first (administrator permission)",Margin=new(0,12,0,0)};
    readonly CheckBox quiet=new(){Content="Uninstall quietly where the app supports it",Margin=new(0,6,0,0),IsChecked=true};
    internal bool RestorePoint=>restorePoint.IsChecked==true;
    internal bool Quiet=>quiet.IsChecked==true;
#if DEBUG
    internal static Func<IReadOnlyList<InstalledApp>,(bool Accept,bool Quiet,bool RestorePoint)> AnswerForVerification;
#endif
    internal static (bool Accept,bool Quiet,bool RestorePoint) Ask(Window owner,IReadOnlyList<InstalledApp> apps)
    {
#if DEBUG
        if(AnswerForVerification!=null)return AnswerForVerification(apps);
#endif
        var dialog=new UninstallConfirmWindow(owner,apps);
        return dialog.ShowDialog()==true?(true,dialog.Quiet,dialog.RestorePoint):(false,false,false);
    }
    UninstallConfirmWindow(Window owner,IReadOnlyList<InstalledApp> apps)
    {
        Owner=owner;Title="Uninstall — Pathweft";Width=520;MinWidth=360;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();var surface=new Border{Background=MainWindow.Tint(),Padding=new(20),Child=body};Content=surface;
        body.Children.Add(new TextBlock{Text=apps.Count==1?"Uninstall "+apps[0].Name+"?":$"Uninstall {apps.Count} apps?",FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var note=new TextBlock{Text="Each app's own uninstaller runs, one at a time, in this order. Pathweft cannot undo an uninstall; reinstall from the app's source if you need it again.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,10)};
        note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(note);
        var list=new StackPanel();
        foreach(var app in apps)
        {
            var line=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,2,0,2)};line.Inlines.Add(new System.Windows.Documents.Run(app.Name));
            var detail=new System.Windows.Documents.Run(" · "+app.Source+(app.Source==MaintenanceApps.Desktop&&app.Quiet.Length>0?" · quiet available":""));
            detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Muted");line.Inlines.Add(detail);list.Children.Add(line);
        }
        var scroll=new ScrollViewer{Content=list,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};AutomationProperties.SetName(scroll,"Apps to uninstall");body.Children.Add(scroll);
        restorePoint.IsChecked=apps.Any(a=>a.Source==MaintenanceApps.Desktop);body.Children.Add(restorePoint);
        quiet.Visibility=apps.Any(a=>a.Source==MaintenanceApps.Desktop&&a.Quiet.Length>0)?Visibility.Visible:Visibility.Collapsed;body.Children.Add(quiet);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,18,0,0)};
        var accept=new Button{Content=apps.Count==1?"Uninstall":"Uninstall all",MinHeight=34,MinWidth=110,Margin=new(0,0,8,0)};
        var cancel=new Button{Content="Cancel",MinHeight=34,MinWidth=92,IsCancel=true,IsDefault=true};
        accept.Style=cancel.Style=(Style)owner.FindResource("JobAction");
        accept.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(accept);buttons.Children.Add(cancel);body.Children.Add(buttons);
        AuxiliaryWindows.Prepare(this,surface);
        Loaded+=(_,_)=>cancel.Focus();
    }
}

// Leftovers of a removed app (MAINT-36). Likely findings are preselected; registry items are exported to a
// .reg backup before removal and Restore imports it; files and folders go to the Recycle Bin.
internal sealed class LeftoversWindow : Window
{
    internal sealed class Item(Leftover leftover)
    {
        internal Leftover Leftover{get;}=leftover;
        public bool Selected{get;set;}=leftover.Confidence==MaintenanceLeftovers.Likely;
        public string Confidence=>Leftover.Confidence;
        public string Kind=>Leftover.Kind+(Leftover.Administrator?" · admin":"");
        public string Location=>Leftover.Display;
        public string Reason=>Leftover.Reason;
    }
    readonly InstalledApp app;readonly Item[] items;readonly string backupFolder;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,10,0,0)};
    readonly Button remove=new(){Content="Remove selected",MinHeight=34,MinWidth=130,Margin=new(0,0,8,0)},restore=new(){Content="Restore registry backup",MinHeight=34,Margin=new(0,0,8,0),Visibility=Visibility.Collapsed};
    readonly List<string> backups=[];
    readonly ListView list=new(){Height=300};
    internal string Result{get;private set;}
#if DEBUG
    internal static Func<LeftoversWindow,Task> DriveForVerification;
    internal Item[] ItemsForVerification=>items;
    internal Task RemoveForVerification()=>RemoveSelected();
    internal Task RestoreForVerification()=>RestoreBackups();
    internal string StatusForVerification=>status.Text;
#endif
    internal LeftoversWindow(Window owner,InstalledApp app,Leftover[] found)
    {
        this.app=app;items=found.Select(x=>new Item(x)).ToArray();
        backupFolder=Path.Combine(MaintenanceLeftovers.BackupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+string.Concat(app.Name.Where(char.IsLetterOrDigit).Take(40)));
        Owner=owner;Title="Leftovers — Pathweft";Width=860;MinWidth=420;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();var surface=new Border{Background=MainWindow.Tint(),Padding=new(20),Child=body};Content=surface;
        body.Children.Add(new TextBlock{Text="Leftovers of "+app.Name,FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var note=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,10),Text=items.Length==0?"Nothing it left behind was found.":
            "Likely items are selected. Files and folders go to the Recycle Bin; registry items are saved to a backup you can restore. Nothing inside Windows or another app is listed."};
        note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(note);
        var view=new GridView();
        var check=new FrameworkElementFactory(typeof(CheckBox));check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("Selected"){Mode=BindingMode.TwoWay});
        view.Columns.Add(new GridViewColumn{Header="",CellTemplate=new DataTemplate{VisualTree=check},Width=36});
        foreach(var (header,property,width) in new[]{("Confidence","Confidence",95d),("Kind","Kind",130d),("Location","Location",360d),("Why","Reason",240d)})
            view.Columns.Add(new GridViewColumn{Header=header,DisplayMemberBinding=new Binding(property),Width=width});
        list.View=view;list.ItemsSource=items;list.Background=System.Windows.Media.Brushes.Transparent;list.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");
        AutomationProperties.SetName(list,"Leftover findings");if(items.Length>0)body.Children.Add(list);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,16,0,0)};
        var close=new Button{Content="Close",MinHeight=34,MinWidth=92,IsCancel=true};
        remove.Style=restore.Style=close.Style=(Style)owner.FindResource("JobAction");remove.Visibility=items.Length>0?Visibility.Visible:Visibility.Collapsed;
        remove.Click+=async(_,_)=>await Guard(RemoveSelected);restore.Click+=async(_,_)=>await Guard(RestoreBackups);close.Click+=(_,_)=>Close();
        buttons.Children.Add(remove);buttons.Children.Add(restore);buttons.Children.Add(close);body.Children.Add(buttons);
        AuxiliaryWindows.Prepare(this,surface);
    }
    async Task Guard(Func<Task> action)
    {
        remove.IsEnabled=restore.IsEnabled=false;
        try{await action();}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or System.ComponentModel.Win32Exception)
        {status.Text=error.Message;}
        finally{remove.IsEnabled=true;restore.IsEnabled=true;}
    }
    async Task RemoveSelected()
    {
        var chosen=items.Where(x=>x.Selected).Select(x=>x.Leftover).ToArray();if(chosen.Length==0){status.Text="Select the items to remove.";return;}
        status.Text="Removing…";int removed=0,skipped=0;var details=new List<string>();
        var user=chosen.Where(x=>!x.Administrator).Select(x=>x.Identity).ToArray();
        if(user.Length>0)
        {
            var outcome=await Task.Run(()=>MaintenanceLeftovers.Remove(app,user,false,backupFolder,CancellationToken.None));
            removed+=outcome.Removed;skipped+=outcome.Skipped;if(outcome.Backup!=null)backups.Add(outcome.Backup);if(outcome.Details.Length>0)details.Add(outcome.Details);
        }
        var machine=chosen.Where(x=>x.Administrator).Select(x=>x.Identity).ToArray();
        if(machine.Length>0)
        {
            await using var client=new MaintenanceClient();
            var reply=await client.Start(new("remove-leftovers",MaintenanceLeftovers.Serialize(app,machine),backupFolder,null),true);
            if(!reply.Ok){skipped+=machine.Length;details.Add(reply.Message);}
            else
            {
                var outcome=JsonSerializer.Deserialize<LeftoverOutcome>(reply.Message)??throw new InvalidDataException("Invalid leftovers result");
                removed+=outcome.Removed;skipped+=outcome.Skipped;if(outcome.Backup!=null)backups.Add(outcome.Backup);if(outcome.Details.Length>0)details.Add(outcome.Details);
            }
        }
        foreach(var item in items.Where(x=>x.Selected))item.Selected=false;list.Items.Refresh();
        Result=$"{removed:N0} leftovers removed"+(skipped>0?$" · {skipped:N0} skipped":"");
        status.Text=Result+(backups.Count>0?" · registry backup saved in "+backupFolder:"")+(details.Count>0?"\n"+string.Join("\n",details):"");
        restore.Visibility=backups.Count>0?Visibility.Visible:Visibility.Collapsed;
    }
    async Task RestoreBackups()
    {
        foreach(string file in backups.ToArray())
        {
            if(Path.GetFileName(file).StartsWith("machine",StringComparison.OrdinalIgnoreCase))
            {
                await using var client=new MaintenanceClient();
                var reply=await client.Start(new("restore-leftovers","",file,null),true);if(!reply.Ok)throw new IOException(reply.Message);
            }
            else await MaintenanceLeftovers.Restore(file,CancellationToken.None);
            backups.Remove(file);
        }
        status.Text="Registry backup restored. Removed files and folders are in the Recycle Bin.";restore.Visibility=Visibility.Collapsed;Result+=" · registry restored";
    }
    internal static async Task<string> Show(Window owner,InstalledApp app,Leftover[] found)
    {
        var window=new LeftoversWindow(owner,app,found);
#if DEBUG
        if(DriveForVerification!=null){await DriveForVerification(window);return window.Result;}
#endif
        window.ShowDialog();return window.Result;
    }
}
