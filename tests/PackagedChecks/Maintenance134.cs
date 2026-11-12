using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

sealed partial class PackageProbe
{
    async Task VerifyMaintenance134()
    {
        string application=Path.GetDirectoryName(owner.GetType().Assembly.Location);
        foreach(string donor in new[]{"Scoop","Win11Debloat"})
            Check(File.Exists(Path.Combine(application,"vendor",donor,"License.txt"))&&File.Exists(Path.Combine(application,"vendor",donor,"source-manifest.json")),"Delivered Maintenance donor provenance and notices: "+donor);
        var shield=(Button)owner.FindName("AdministratorCommand");var hidden=(Button)owner.FindName("HiddenToggle");
        var commands=((Panel)shield.Parent).Children.OfType<Button>().ToArray();
        Check(Array.IndexOf(commands,shield)==Array.IndexOf(commands,hidden)+1&&shield.Content is Image{Width:16,Height:16}&&shield.Tag==null&&AutomationProperties.GetItemStatus(shield)=="Standard", "Delivered administrator shield is compact, starts Standard and sits beside Hidden");
        var command=(Button)owner.FindName("MaintenanceCommand");
        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var panel=owner.GetType().GetField("maintenance",Flags).GetValue(owner);
        var type=panel.GetType();
        Check(hidden.Visibility==Visibility.Visible&&(hidden.Tag as string=="on")==(AutomationProperties.GetItemStatus(hidden)=="On")&&((Border)panel).Visibility==Visibility.Collapsed&&((FrameworkElement)owner.FindName("PaneArea")).Visibility==Visibility.Visible,
            "Delivered Maintenance starts with cards and browser; Hidden stays visible with its own setting");
        await (Task)owner.GetType().GetField("paneModeWork",Flags).GetValue(owner);
        var cards=(UniformGrid)type.GetProperty("Cards",Flags).GetValue(panel);
        var rows=(ListView)type.GetProperty("Rows",Flags).GetValue(panel);
        // The independent consumer never shows its host window. Rendered
        // visibility is qualified by the source UI scenario; here inspect
        // the delivered layout state and real navigation/readers.
        Check(cards.Columns==5&&cards.Children.Count==5&&cards.Visibility==Visibility.Visible,
            "Delivered brush opens five bottom Maintenance summary tabs");
        var actions=(WrapPanel)type.GetField("actions",Flags).GetValue(panel);
        Check(actions.Children.OfType<Button>().All(button=>!Equals(button.Content,"Refresh")&&!Equals(button.Content,"Analyze")),
            "Delivered Maintenance has no manual Refresh or generic Analyze button");
        var progressType=Product.GetType("FileExplorer.CleanupProgressWindow",true);
        var progressTypeValue=Product.GetType("FileExplorer.CleanupProgress",true);
        var popup=(Window)Activator.CreateInstance(progressType,Flags,null,[owner,"Npm fixture",(Action)(()=>{})],null);
        var managerProgress=Activator.CreateInstance(progressTypeValue,Flags,null,["Cleaning package cache","fixture-npm-cache",0L,1347L,0L,0L,0L],null);
        progressType.GetMethod("Report",Flags).Invoke(popup,[managerProgress]);
        await owner.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        Check(((ProgressBar)progressType.GetField("bar",Flags).GetValue(popup)).IsIndeterminate&&
            ((TextBlock)progressType.GetField("completed",Flags).GetValue(popup)).Text=="Manager command running",
            "Delivered native package-manager cleanup shows activity without pretending to count completed entries");
        progressType.GetMethod("Finish",Flags).Invoke(popup,["Package-manager cache changed",true,"",false,true]);
        Check(((TextBlock)progressType.GetField("phase",Flags).GetValue(popup)).Text=="Cleanup failed"&&
            ((TextBlock)progressType.GetField("total",Flags).GetValue(popup)).Text=="",
            "Delivered popup labels rejected cache cleanup as a failure without a false total");
        popup.Close();
        var performanceButton=(Button)owner.FindName("PerformanceCommand");
        bool panesShown=((FrameworkElement)owner.FindName("PaneArea")).Visibility==Visibility.Visible;
        Check(performanceButton.IsEnabled==panesShown&&shield.IsEnabled==panesShown&&command.IsEnabled&&
            AutomationProperties.GetItemStatus(performanceButton)=="Off"&&(AutomationProperties.GetItemStatus(shield)=="Explorer only")==!panesShown,
            "Delivered Maintenance toolbar follows the file panes: usable with the cards, only the Maintenance button with a category open");
        Check(shield.ContextMenu==null,"Delivered shield manages file browsing only, with no session-end action that would also end Performance's elevated work");
        string[] reviewedCleanup=["error-reports","scoop-cache","scoop-versions","thumbnail-cache","user-temp","windows-temp"];
        var cleanupRows=rows.Items.Cast<object>().ToArray();
        string[] actionableManagers=["manager-chocolatey","manager-npm","manager-pip","manager-dotnet-tool"];
        Check(cleanupRows.Length==reviewedCleanup.Length+actionableManagers.Length&&
            reviewedCleanup.All(id=>cleanupRows.Any(r=>(string)Get(r,"Id")==id&&Get(r,"Bytes")!=null))&&
            actionableManagers.All(id=>cleanupRows.Any(r=>(string)Get(r,"Id")==id&&
                !((string)Get(r,"State")).Equals("No safe cleanup",StringComparison.Ordinal)&&
                !((string)Get(r,"State")).Equals("HTTP cache present",StringComparison.Ordinal)))&&
            cleanupRows.Count(r=>((string)Get(r,"Id")).StartsWith("manager-",StringComparison.Ordinal))==actionableManagers.Length&&
            (string)type.GetProperty("State",Flags).GetValue(panel)=="All categories reviewed. Nothing has been removed.","Delivered Maintenance automatically reviews all Cleanup categories without row selection or UAC");
        Check(cleanupRows.All(row=>!((string)Get(row,"Summary")).Contains(":\\",StringComparison.Ordinal)&&
                !((string)Get(row,"Summary")).Contains("unavailable entries",StringComparison.OrdinalIgnoreCase))&&
            cleanupRows.Where(row=>new[]{"manager-npm","manager-pip","manager-dotnet-tool"}.Contains((string)Get(row,"Id")))
                .All(row=>((string)Get(row,"Details")).Contains(":\\",StringComparison.Ordinal)),
            "Delivered Cleanup table uses concise summaries while exact cache paths remain in selected details");
        rows.SelectedItem=rows.Items.Cast<object>().Single(row=>(string)Get(row,"Id")=="windows-temp");
        var protectedAction=(Button)type.GetField("protectedReview",Flags).GetValue(panel);
        var cleanAction=(Button)type.GetField("change",Flags).GetValue(panel);
        Check(protectedAction.Visibility==Visibility.Visible&&protectedAction.IsEnabled&&!cleanAction.IsEnabled,
            "Delivered protected Cleanup requires its own review before Clean and does not depend on the Explorer shield");
        rows.SelectedItem=null;
        string[] categories=["Cleanup","Startup","Services","Windows tweaks","Windows apps"];
        for(int i=0;i<categories.Length;i++)
        {
            var card=(Button)cards.Children[i];
            card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await (Task)type.GetProperty("Ready",Flags).GetValue(panel);
            Check(((Border)panel).Visibility==Visibility.Visible&&hidden.Visibility==Visibility.Visible&&hidden.Tag==null,"Delivered card opens the main analysis without hiding or selecting the eye");
            Check((string)type.GetProperty("Category",Flags).GetValue(panel)==categories[i]&&
                AutomationProperties.GetItemStatus(card)=="Selected"&&card.ToolTip!=null,
                "Delivered Maintenance card selects its real category and summary: "+categories[i]);
            string state=(string)type.GetProperty("State",Flags).GetValue(panel);
            Check(state=="No items found."||state.Contains("items · no changes made",StringComparison.Ordinal),
                "Delivered category completes its actual read-only Windows query: "+categories[i]);
            var headers=((GridView)rows.View).Columns.Select(x=>AutomationProperties.GetName((GridViewColumnHeader)x.Header)).ToArray();
            foreach(var column in ((GridView)rows.View).Columns)
            {
                var header=(GridViewColumnHeader)column.Header;
                // This independent host never renders its headers. Route the
                // real header's click through its owning table; source/native
                // acceptance separately qualifies rendered header bubbling.
                rows.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,header));
                string direction=AutomationProperties.GetItemStatus(header);
                var sorted=System.Windows.Data.CollectionViewSource.GetDefaultView(rows.ItemsSource).SortDescriptions;
                Check(sorted.Count>0&&sorted[0].PropertyName==(string)header.Tag&&direction.Length>0,"Delivered header sorts "+categories[i]+" / "+AutomationProperties.GetName(header));
                var grouped=rows.Items.Cast<object>().GroupBy(row=>categories[i]=="Windows tweaks"?(string)Get(row,"Group"):"");
                Check(grouped.All(group=>{var values=group.Select(row=>Get(row,(string)header.Tag)).ToArray();return values.Zip(values.Skip(1),(a,b)=>direction=="Ascending"?System.Collections.Comparer.Default.Compare(a,b)<=0:System.Collections.Comparer.Default.Compare(a,b)>=0).All(x=>x);}),"Delivered rows follow selected header ordering within setting groups: "+AutomationProperties.GetName(header));
                rows.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,header));
                Check(AutomationProperties.GetItemStatus(header)!=direction,"Delivered header reverses "+AutomationProperties.GetName(header));
            }
            if(categories[i]=="Startup")Check(headers.Contains("Type")&&!headers.Contains("Size"),"Delivered Startup identifies its persistence source type");
            if(categories[i]=="Services")Check(headers.Contains("Startup type")&&rows.Items.Count>10,"Delivered Services reads the complete installed Win32 inventory");
            if(categories[i]=="Services")Check(rows.Items.Cast<object>().Any(row=>(string)Get(row,"Type")=="AppXSvc"&&(string)Get(row,"Recommendation")=="Keep enabled")&&
                rows.Items.Cast<object>().All(row=>((string)Get(row,"Summary")).IndexOfAny(['\r','\n'])<0),"Delivered Services includes matched guidance and compact details");
            if(i>0)Check(headers.Contains("Recommendation")== (categories[i]=="Windows tweaks")&&
                rows.Items.Cast<object>().All(row=>!string.IsNullOrEmpty((string)Get(row,"Guidance"))),
                "Delivered Recommendation column appears only with full reviewed tweak coverage: "+categories[i]);
            if(categories[i]=="Windows tweaks")
            {
                Check(rows.Items.Count==40&&rows.Items.Cast<object>().Count(row=>((string)Get(row,"RuleId")).StartsWith("donor."))==24&&
                    rows.Items.Cast<object>().Where(row=>((string)Get(row,"RuleId")).StartsWith("donor.")).All(row=>(string)Get(row,"Recommendation")!="Not assessed"),"Delivered catalog contains all qualified adopted donor actions");
                int pending=rows.Items.Cast<object>().Count(row=>(bool)Get(row,"CanChange")&&
                    ((string)Get(row,"Recommendation")=="Optional"&&(string)Get(row,"State")=="Not applied"||
                     (string)Get(row,"Recommendation")=="Keep enabled"&&(string)Get(row,"State")=="Applied"));
                Check(((StackPanel)card.Content).Children.OfType<TextBlock>().ElementAt(1).Text==pending.ToString("N0")&&
                    card.ToolTip.ToString().Contains("recommended actions pending"),"Delivered Tweaks card counts applicable pending recommendations");
                var search=(TextBox)type.GetField("search",Flags).GetValue(panel);var filter=(ComboBox)type.GetField("groups",Flags).GetValue(panel);
                int all=rows.Items.Count;search.Text="Storage Sense";
                Check(rows.Items.Count==1&&(string)Get(rows.Items[0],"Recommendation")=="Keep enabled","Delivered search and exact guidance match the bundled catalog");
                rows.SelectedIndex=0;
                Check(((Border)type.GetField("explanation",Flags).GetValue(panel)).Visibility==Visibility.Visible,"Delivered selected setting exposes effect/source details");
                var assessment=(TextBox)type.GetField("explanationText",Flags).GetValue(panel);
                Check(assessment.IsReadOnly&&assessment.Text.Contains((string)Get(rows.Items[0],"Details")),"Delivered assessment retains selectable complete source details");
                search.Clear();filter.SelectedItem="File Explorer";
                Check(rows.Items.Count>0&&rows.Items.Cast<object>().All(row=>(string)Get(row,"Group")=="File Explorer"),"Delivered category selector narrows existing settings");
                Check(((StackPanel)card.Content).Children.OfType<TextBlock>().ElementAt(1).Text==pending.ToString("N0"),"Delivered filtering preserves the whole-category recommendation count");
                filter.SelectedIndex=0;Check(rows.Items.Count==all,"Delivered All categories restores the full setting list");
            }
        }
        Check(File.Exists(Path.Combine(Path.GetDirectoryName(Product.Location),"Maintenance-Windows.ps1"))&&
            rows.Items.Cast<object>().All(r=>(string)Get(r,"Scope")=="Current user"),"Delivered optional Windows-app query uses its bundled supervised script");
        await (Task)Call(owner.GetType(),owner,"CloseMaintenance");
        Check(cards.Visibility==Visibility.Collapsed&&!(bool)owner.GetType().GetField("maintenanceVisible",Flags).GetValue(owner),
            "Delivered Back to files retires Maintenance and restores browser layout");
        Check(hidden.Visibility==Visibility.Visible,"Delivered browser restores its original file-visibility command after Maintenance");
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
