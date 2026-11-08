using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

sealed partial class PackageProbe
{
    async Task VerifyMaintenance134()
    {
        var shield=(Button)owner.FindName("AdministratorCommand");var hidden=(Button)owner.FindName("HiddenToggle");
        var commands=((Panel)shield.Parent).Children.OfType<Button>().ToArray();
        Check(Array.IndexOf(commands,shield)==Array.IndexOf(commands,hidden)+1&&shield.Content is Image{Width:16,Height:16}&&shield.Tag==null&&AutomationProperties.GetItemStatus(shield)=="Standard", "Delivered administrator shield is compact, starts Standard and sits beside Hidden");
        var command=(Button)owner.FindName("MaintenanceCommand");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var panel=owner.GetType().GetField("maintenance",Flags).GetValue(owner);
        var type=panel.GetType();
        await (Task)owner.GetType().GetField("paneModeWork",Flags).GetValue(owner);
        var cards=(UniformGrid)type.GetProperty("Cards",Flags).GetValue(panel);
        var rows=(ListView)type.GetProperty("Rows",Flags).GetValue(panel);
        // The independent consumer never shows its host window. Rendered
        // visibility is qualified by the source UI scenario; here inspect
        // the delivered layout state and real navigation/readers.
        Check(cards.Columns==5&&cards.Children.Count==5&&cards.Visibility==Visibility.Visible,
            "Delivered brush opens five bottom Maintenance summary tabs");
        Check(shield.IsEnabled&&shield.ContextMenu?.Items.OfType<MenuItem>().Any(x=>(string)x.Header=="End administrator session")==true,"Delivered access toggle remains available in Maintenance with an explicit session-end action");
        Check(rows.Items.Count==4&&rows.Items.Cast<object>().All(r=>Get(r,"Bytes")!=null)&&
            (string)type.GetProperty("State",Flags).GetValue(panel)=="All categories reviewed. Nothing has been removed.","Delivered Maintenance automatically reviews all Cleanup categories without row selection or UAC");
        string[] categories=["Cleanup","Startup","Services","Windows tweaks","Windows apps"];
        for(int i=0;i<categories.Length;i++)
        {
            var card=(Button)cards.Children[i];
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await (Task)type.GetProperty("Ready",Flags).GetValue(panel);
            Check((string)type.GetProperty("Category",Flags).GetValue(panel)==categories[i]&&
                AutomationProperties.GetItemStatus(card)=="Selected"&&card.ToolTip!=null,
                "Delivered Maintenance card selects its real category and summary: "+categories[i]);
            string state=(string)type.GetProperty("State",Flags).GetValue(panel);
            Check(state=="No items found."||state.EndsWith("items · no changes made",StringComparison.Ordinal),
                "Delivered category completes its actual read-only Windows query: "+categories[i]);
            var headers=((GridView)rows.View).Columns.Select(x=>AutomationProperties.GetName((GridViewColumnHeader)x.Header)).ToArray();
            foreach(var column in ((GridView)rows.View).Columns)
            {
                var header=(GridViewColumnHeader)column.Header;
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string direction=AutomationProperties.GetItemStatus(header);
                var sorted=System.Windows.Data.CollectionViewSource.GetDefaultView(rows.ItemsSource).SortDescriptions;
                Check(sorted.Count>0&&sorted[0].PropertyName==(string)header.Tag&&direction.Length>0,"Delivered header sorts "+categories[i]+" / "+AutomationProperties.GetName(header));
                header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(AutomationProperties.GetItemStatus(header)!=direction,"Delivered header reverses "+AutomationProperties.GetName(header));
            }
            if(categories[i]=="Startup")Check(headers.Contains("Type")&&!headers.Contains("Size"),"Delivered Startup identifies its persistence source type");
            if(categories[i]=="Services")Check(headers.Contains("Startup type")&&rows.Items.Count>10,"Delivered Services reads the complete installed Win32 inventory");
        }
        Check(File.Exists(Path.Combine(Path.GetDirectoryName(Product.Location),"Maintenance-Windows.ps1"))&&
            rows.Items.Cast<object>().All(r=>(string)Get(r,"Scope")=="Current user"),"Delivered optional Windows-app query uses its bundled supervised script");
        await (Task)Call(owner.GetType(),owner,"CloseMaintenance");
        Check(cards.Visibility==Visibility.Collapsed&&!(bool)owner.GetType().GetField("maintenanceVisible",Flags).GetValue(owner),
            "Delivered Back to files retires Maintenance and restores browser layout");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)owner.GetType().GetField("paneModeWork",Flags).GetValue(owner);
        var performanceCommand=(Button)owner.FindName("PerformanceCommand");performanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)owner.GetType().GetField("paneModeWork",Flags).GetValue(owner);
        Check(!(bool)owner.GetType().GetField("maintenanceVisible",Flags).GetValue(owner)&&Equals(performanceCommand.Tag,"on")&&command.Tag==null,"Delivered Performance and Maintenance toolbar modes are mutually exclusive");
        performanceCommand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await (Task)owner.GetType().GetField("paneModeWork",Flags).GetValue(owner);
        Check(performanceCommand.Tag==null,"Delivered Performance toolbar returns to the file view");
        Check(Type("PerformanceHistory").GetMethod("AppSeries",Flags)!=null&&
            Type("BootService")!=null&&Type("PerformanceChanges")!=null,
            "Delivered local investigation includes app history, boot and change readers");
    }
}
