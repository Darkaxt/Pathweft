using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileExplorer;

sealed partial class PackageProbe
{
    async Task VerifyWidget(App app)
    {
        var widgetType=Type("SearchWidgetWindow");
        var widget=(Window)Activator.CreateInstance(widgetType,Flags,null,new object[]{owner},null);
        try
        {
            var pane=(PaneView)Get(widget,"Pane");var list=(ListView)pane.FindName("List");
            Check(widget.WindowStyle==WindowStyle.None&&!widget.ShowInTaskbar&&pane.Dir==PaneView.ThisPC&&!pane.Items.Any()&&((GridView)list.View).Columns.Count==4&&widgetType.GetField("Destination",Flags)==null,"Delivered popup has only its global query and four-column result view, without the former title/destination/actions");
            var input=(TextBox)pane.FindName("FilterBox");
            input.Text="path:\""+Path.GetDirectoryName(known)+"\" \""+Path.GetFileName(known)+"\"";
            await (Task)Get(pane,"SearchCompletion");
            checks.Add(new{widgetQuery=input.Text,notice=Get(pane,"SearchNotice"),total=Get(pane,"IndexedTotal"),count=pane.Items.Count(),matchOptions=Get(pane,"MatchOptions").ToString(),styleMatches=ReferenceEquals(list.ItemContainerStyle,pane.FindResource("WidgetResultRow"))});
            Check(pane.Items.Any(entry=>entry.Path.Equals(known,StringComparison.OrdinalIgnoreCase))&&ReferenceEquals(list.ItemContainerStyle,pane.FindResource("WidgetResultRow")),"Delivered compact popup queries the actual index as text changes and retains its plain result style");
            var row=pane.Items.Single(entry=>entry.Path.Equals(known,StringComparison.OrdinalIgnoreCase));row.Message="Could not open: packaged widget fixture";
            await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(((GridView)list.View).Columns.Count==5,"Delivered popup adds the Message column only for an actual row failure");
            row.Message="";await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(((GridView)list.View).Columns.Count==4,"Delivered popup removes the empty Message column again");
            input.Text="";await (Task)Get(pane,"SearchCompletion");
            Check(!pane.Items.Any(),"Clearing the delivered popup empties results instead of listing drives");
        }
        finally{Call(widgetType,widget,"Shutdown");}
    }
}
