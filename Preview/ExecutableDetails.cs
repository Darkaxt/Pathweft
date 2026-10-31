using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Preview;

internal static class ExecutableDetails
{
    internal static List<(string Name,string Value)> Read(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var file=new FileInfo(path);
        var version=FileVersionInfo.GetVersionInfo(path);
        var rows=new List<(string,string)>();
        void Add(string name,string value){if(!string.IsNullOrWhiteSpace(value))rows.Add((name,value.Trim()));}
        Add("Name",file.Name);Add("File description",version.FileDescription);
        Add("Type","Application");Add("Company",version.CompanyName);
        Add("File version",version.FileVersion);Add("Product name",version.ProductName);
        Add("Product version",version.ProductVersion);Add("Copyright",version.LegalCopyright);
        Add("Original filename",version.OriginalFilename);Add("Language",version.Language);
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        {
            try
            {
                using var pe=new PEReader(stream);
                string machine=pe.PEHeaders.CoffHeader.Machine switch{Machine.Amd64=>"x64",Machine.I386=>"x86",Machine.Arm64=>"ARM64",var other=>other.ToString()};
                Add("Architecture",machine);Add("Runtime",pe.HasMetadata?".NET":"Native");
            }
            catch(BadImageFormatException){Add("Executable header","Unrecognized or incomplete");}
        }
        Add("Size",$"{MainWindow.Fmt(file.Length)} ({file.Length:N0} bytes)");
        Add("Date modified",file.LastWriteTime.ToString("g"));Add("Date created",file.CreationTime.ToString("g"));
        Add("Location",file.DirectoryName);token.ThrowIfCancellationRequested();return rows;
    }
    internal static UIElement View(List<(string Name,string Value)> rows)
    {
        var grid=new Grid{Margin=new(16)};
        grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto,MaxWidth=145});grid.ColumnDefinitions.Add(new());
        foreach(var (name,value) in rows)
        {
            int row=grid.RowDefinitions.Count;grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var label=new TextBlock{Text=name,TextWrapping=TextWrapping.Wrap,Margin=new(0,4,16,8)};
            label.SetResourceReference(TextBlock.ForegroundProperty,"Palette.8A97AA");
            var text=new TextBox{Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new(0),Background=System.Windows.Media.Brushes.Transparent,Padding=new(0),Margin=new(0,4,0,8)};
            text.SetResourceReference(TextBox.ForegroundProperty,"Palette.E8EEF6");
            System.Windows.Automation.AutomationProperties.SetName(text,name);
            Grid.SetRow(label,row);Grid.SetRow(text,row);Grid.SetColumn(text,1);grid.Children.Add(label);grid.Children.Add(text);
        }
        return new ScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
}
