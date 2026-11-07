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
        Call(type,panel,"Select",-1);
    }
}
