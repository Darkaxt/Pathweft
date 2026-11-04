using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FileExplorer.Preview;

// CSV and database previews share only their actual read-only row presentation.
internal sealed record PreviewTable(string[] Columns, IReadOnlyList<string[]> Rows, string Notice="");

internal sealed class TabularPreview : DockPanel
{
    const int PageSize=200;
    readonly PreviewTable table;
    readonly DataGrid grid;
    readonly TextBlock position;
    readonly Button previous,next;
    readonly CheckBox headers;
    int page;
    internal DataGrid Grid=>grid;
    internal int Page=>page;
    internal Task Completion=>Task.CompletedTask;

    internal TabularPreview(MainWindow owner,PreviewTable data,bool canToggleHeaders=false)
    {
        table=data;Margin=new Thickness(12);LastChildFill=true;
        var controls=new DockPanel{LastChildFill=true,Margin=new Thickness(0,0,0,8)};
        SetDock(controls,Dock.Top);Children.Add(controls);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal};
        previous=PreviewControls.Icon(owner,"Previous rows","\uE76B");
        next=PreviewControls.Icon(owner,"Next rows","\uE76C");
        navigation.Children.Add(previous);navigation.Children.Add(next);
        SetDock(navigation,Dock.Right);controls.Children.Add(navigation);
        position=new TextBlock{VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        AutomationProperties.SetName(position,"Visible table rows");controls.Children.Add(position);
        previous.Click+=(_,_)=>{if(page>0){page--;ShowPage();}};
        next.Click+=(_,_)=>{if((page+1)*PageSize<Count){page++;ShowPage();}};
        headers=new CheckBox{Content="First row contains headers",IsChecked=canToggleHeaders,Margin=new Thickness(0,0,0,8),Visibility=canToggleHeaders?Visibility.Visible:Visibility.Collapsed};
        SetDock(headers,Dock.Top);Children.Add(headers);
        if(data.Notice.Length>0)
        {
            var notice=new TextBlock{Text=data.Notice,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};
            SetDock(notice,Dock.Bottom);Children.Add(notice);
        }
        grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            EnableRowVirtualization=true,EnableColumnVirtualization=true,HeadersVisibility=DataGridHeadersVisibility.Column,
            GridLinesVisibility=DataGridGridLinesVisibility.None,SelectionUnit=DataGridSelectionUnit.CellOrRowHeader,
            Background=Brushes.Transparent,BorderThickness=new Thickness(0),RowHeaderWidth=0,MinColumnWidth=80};
        grid.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty,"Palette.08FFFFFF");
        grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"Palette.0DFFFFFF");
        var heading=new Style(typeof(DataGridColumnHeader));
        heading.Setters.Add(new Setter(BackgroundProperty,new System.Windows.DynamicResourceExtension("Palette.14FFFFFF")));
        heading.Setters.Add(new Setter(Control.ForegroundProperty,new System.Windows.DynamicResourceExtension("Palette.E8EEF6")));
        heading.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,6,8,6)));
        grid.ColumnHeaderStyle=heading;
        AutomationProperties.SetName(grid,"Read-only preview table");Children.Add(grid);
        headers.Checked+=(_,_)=>{page=0;ShowPage();};headers.Unchecked+=(_,_)=>{page=0;ShowPage();};
        ShowPage();
    }
    bool UseHeaders=>headers.Visibility==Visibility.Visible&&headers.IsChecked==true;
    int Count=>table.Rows.Count+(headers.Visibility==Visibility.Visible&&!UseHeaders?1:0);
    void ShowPage()
    {
        grid.Columns.Clear();
        for(int i=0;i<table.Columns.Length;i++)
            grid.Columns.Add(new DataGridTextColumn{Header=UseHeaders||headers.Visibility!=Visibility.Visible?table.Columns[i]:$"Column {i+1}",Binding=new Binding($"[{i}]"){Mode=BindingMode.OneWay},Width=DataGridLength.SizeToHeader,MaxWidth=450});
        var rows=headers.Visibility==Visibility.Visible&&!UseHeaders?new[]{table.Columns}.Concat(table.Rows):table.Rows;
        grid.ItemsSource=rows.Skip(page*PageSize).Take(PageSize).ToArray();
        previous.IsEnabled=page>0;next.IsEnabled=(page+1)*PageSize<Count;
        position.Text=Count==0?"No rows":$"Rows {page*PageSize+1:N0}–{Math.Min((page+1)*PageSize,Count):N0} of {Count:N0}";
    }
}
