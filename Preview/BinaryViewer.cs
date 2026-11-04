using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Preview;

internal sealed class BinaryViewer(MainWindow owner):IViewer
{
    const int PageBytes=4096;
    string path;ContextObject context;long offset;
    TextBox content,input;TextBlock position;Button previous,next;
    public bool CanHandle(string value)=>true;
    public void Init(){}
    public void Prepare(string value,ContextObject current){path=value;context=current;current.Title=Path.GetFileName(value)+" · Hex and text";}
    public async Task View(string value,ContextObject current)
    {
        var panel=new DockPanel{Margin=new Thickness(12)};
        var controls=new DockPanel{Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(controls,Dock.Top);panel.Children.Add(controls);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);controls.Children.Add(actions);
        previous=PreviewControls.Icon(owner,"Previous bytes","\uE76B");next=PreviewControls.Icon(owner,"Next bytes","\uE76C");
        var go=PreviewControls.Icon(owner,"Go to hexadecimal byte offset","\uE72A");actions.Children.Add(previous);actions.Children.Add(next);actions.Children.Add(go);
        input=new TextBox{Text="0",MinWidth=70,ToolTip="Hexadecimal byte offset",VerticalContentAlignment=VerticalAlignment.Center};
        System.Windows.Automation.AutomationProperties.SetName(input,"Hexadecimal byte offset");controls.Children.Add(input);
        position=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};DockPanel.SetDock(position,Dock.Bottom);panel.Children.Add(position);
        content=BuiltinViewer.Text("");System.Windows.Automation.AutomationProperties.SetName(content,"Read-only hexadecimal and ASCII bytes");panel.Children.Add(content);
        async Task Move(long at){if(!context.IsCurrent)return;offset=Math.Max(0,at);var work=ShowPage();context.Track(work);await work;}
        previous.Click+=async(_,_)=>await Move(offset-PageBytes);next.Click+=async(_,_)=>await Move(offset+PageBytes);
        go.Click+=async(_,_)=>
        {
            if(long.TryParse(input.Text.Replace("0x","",StringComparison.OrdinalIgnoreCase),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out long at)&&at>=0)await Move(at);
            else position.Text="Enter a valid hexadecimal byte offset.";
        };
        current.ViewerContent=panel;await ShowPage();
    }
    async Task ShowPage()
    {
        long requested=offset;
        try
        {
            var page=await Task.Run(()=>ReadPage(path,requested,context.Token),context.Token);
            if(!context.IsCurrent||offset!=requested)return;
            content.Text=page.Text;position.Text=$"{page.Offset:X}–{page.Offset+page.Count:X} · {MainWindow.Fmt(page.Length)}";
            input.Text=page.Offset.ToString("X");offset=page.Offset;previous.IsEnabled=offset>0;next.IsEnabled=offset+page.Count<page.Length;
        }
        catch(OperationCanceledException)when(context.Token.IsCancellationRequested){}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){if(context.IsCurrent)position.Text=error.Message;}
    }
    internal static (string Text,long Offset,int Count,long Length) ReadPage(string path,long requested,CancellationToken token)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        long offset=Math.Min(requested,Math.Max(0,file.Length-1));file.Position=offset;
        var bytes=new byte[(int)Math.Min(PageBytes,file.Length-offset)];int read=0;
        while(read<bytes.Length){token.ThrowIfCancellationRequested();int n=file.Read(bytes,read,bytes.Length-read);if(n==0)break;read+=n;}
        var text=new StringBuilder();
        for(int row=0;row<read;row+=16)
        {
            text.Append((offset+row).ToString("X12")).Append("  ");
            for(int i=0;i<16;i++)text.Append(row+i<read?bytes[row+i].ToString("X2"):"  ").Append(' ');
            text.Append(" |");for(int i=row;i<Math.Min(row+16,read);i++)text.Append(bytes[i]>=32&&bytes[i]<=126?(char)bytes[i]:'.');text.AppendLine("|");
        }
        return(text.ToString(),offset,read,file.Length);
    }
    public void Cleanup(){}
}
