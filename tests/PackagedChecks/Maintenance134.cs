using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

sealed partial class PackageProbe
{
    async Task VerifyMaintenance134()
    {
        var command=(Button)owner.FindName("MaintenanceCommand");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var panel=owner.GetType().GetField("maintenance",Flags).GetValue(owner);
        var type=panel.GetType();
        await (Task)type.GetProperty("Ready",Flags).GetValue(panel);
        var cards=(UniformGrid)type.GetProperty("Cards",Flags).GetValue(panel);
        var rows=(ListView)type.GetProperty("Rows",Flags).GetValue(panel);
        Check(cards.Columns==5&&cards.Children.Count==5&&cards.IsVisible,
            "Delivered brush opens five bottom Maintenance summary tabs");
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
        }
        Check(File.Exists(Path.Combine(Path.GetDirectoryName(Product.Location),"Maintenance-Windows.ps1"))&&
            rows.Items.Cast<object>().All(r=>(string)Get(r,"Scope")=="Current user"),"Delivered optional Windows-app query uses its bundled supervised script");
        await (Task)Call(owner.GetType(),owner,"CloseMaintenance");
        Check(!cards.IsVisible&&!(bool)owner.GetType().GetField("maintenanceVisible",Flags).GetValue(owner),
            "Delivered Back to files retires Maintenance and restores browser layout");
        Check(Type("PerformanceHistory").GetMethod("AppSeries",Flags)!=null&&
            Type("BootService")!=null&&Type("PerformanceChanges")!=null,
            "Delivered local investigation includes app history, boot and change readers");
    }
}
