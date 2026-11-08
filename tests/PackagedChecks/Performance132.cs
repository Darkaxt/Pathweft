using System.Collections;
using System.Windows;
using System.Windows.Controls;

sealed partial class PackageProbe
{
    async Task VerifyPerformance132()
    {
        var panel=owner.GetType().GetField("performance",Flags).GetValue(owner);var type=panel.GetType();
        Call(type,panel,"Select",0);
        var range=(ComboBox)type.GetField("range",Flags).GetValue(panel);range.SelectedIndex=2;
        var kind=(ComboBox)type.GetField("analysisKind",Flags).GetValue(panel);kind.SelectedIndex=1;
        async Task Ready()=>await(Task)type.GetField("historyQuery",Flags).GetValue(panel);
        await Ready();
        var chart=type.GetField("analysisGraph",Flags).GetValue(panel);
        Check(chart.GetType().Name=="PerformanceTimeline","Delivered expanded analysis hosts the adapted donor History timeline");
        var history=Type("PerformanceHistory");var query=Activator.CreateInstance(Type("PerformanceQuery"),Flags,null,new object[]{"get_top_processes",60,"cpu",null,null,80d,100,null,null},null);
        var result=Call(history,null,"Query",query);
        var rows=((IEnumerable)Get(result,"Rows")).Cast<object>().ToArray();
        Check(rows.Length>0,"Delivered investigation has actual recorded app observations");
        var at=(DateTimeOffset)Get(rows[0],"Time");
        Call(chart.GetType(),chart,"ChooseMoment",at);await Ready();
        var table=(ListView)type.GetField("table",Flags).GetValue(panel);
        Check(table.Items.Count>0&&table.Items.Cast<object>().All(r=>Get(r,"Pid")!=null),"Delivered graph moment action binds contemporaneous observed apps to the real table");
        Call(chart.GetType(),chart,"ChooseRange",at.AddSeconds(-60),at.AddSeconds(1));await Ready();
        var state=(TextBlock)type.GetField("detailState",Flags).GetValue(panel);
        Check(state.Text.StartsWith("Selected")&&table.Items.Count>0,"Delivered overview/range action binds its selected interval to the table");
        Call(type,panel,"ResetInvestigation");await Ready();
        Check(!state.Text.StartsWith("Selected"),"Delivered Now action clears moment/range investigation");
        // No private DEBUG hooks: invoke the same delivered query with the
        // real shared ring and a nonexistent recording, then exercise its table.
        var monitor=Type("PerformanceMonitor").GetProperty("Current",Flags).GetValue(null);
        var live=Call(monitor.GetType(),monitor,"History");
        var liveResult=Call(history,null,"Query",Activator.CreateInstance(Type("PerformanceQuery"),Flags,null,new object[]{"get_usage_summary",60,"cpu",null,null,80d,100,null,null},null),CancellationToken.None,Path.Combine(root,"not-recorded.sqlite"),live);
        Check(((IEnumerable)Get(liveResult,"Points")).Cast<object>().Any(),"Delivered query inspects real live observations without a recording database");
        range.SelectedIndex=0;kind.SelectedIndex=0;Call(type,panel,"Refresh");
        var menu=(ContextMenu)type.GetField("processColumnsMenu",Flags).GetValue(panel);
        Check(menu.Items.Count==7&&menu.Items.Cast<MenuItem>().Any(x=>Equals(x.Header,"Command line")),"Delivered CPU column chooser includes path and command line");
        var metricsType=Type("ProcessMetrics");var metrics=Activator.CreateInstance(metricsType,true);
        Call(metricsType,metrics,"Read");var samples=Call(metricsType,metrics,"Read");
        Call(type,panel,"RefreshProcessRows",samples);
        Check(table.Items.Count>0,"Delivered process table accepts actual Windows PID/start observations");
        {
            var processRows=(IDictionary)type.GetField("processRows",Flags).GetValue(panel);
            var row=processRows.Values.Cast<object>().First();
            Call(type,panel,"ToggleProcessPin",row);Call(type,panel,"RefreshProcessRows",samples);
            Check((bool)Get(row,"Pinned")&&ReferenceEquals(table.Items[0],row),"Delivered CPU pin stays above live rows through refresh");
            var filter=(TextBox)type.GetField("processFilter",Flags).GetValue(panel);filter.Text="no matching process";
            Check(table.Items.Count==1&&ReferenceEquals(table.Items[0],row),"Delivered local filtering preserves an explicitly pinned process");
            filter.Text="";Call(type,panel,"ToggleProcessPin",row);
        }
        Call(type,panel,"Select",-1);
    }
}
