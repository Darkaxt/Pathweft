using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Preview;

internal sealed class OfficeViewer(MainWindow owner,Entry entry,string nativeCore=null):IViewer
{
    readonly OfficeDocument document=new();
    readonly List<Task> work=new();
    ContextObject context;
    CancellationTokenSource rendering;
    Image image;
    TextBlock label,notes,warning;
    TextBox number;
    ComboBox sheets;
    Button previous,next;
    ScrollViewer importNotes;
    int revision,currentPart,currentPage;
    bool disposed,updating;
    internal Task Completion {get;private set;}=Task.CompletedTask;
#if DEBUG
    internal IReadOnlyList<(string Engine,int Pid)> WorkersForVerification=>document.WorkersForVerification;
    internal string SourceForVerification=>document.SourceForVerification;
    internal int WorkerForVerification=>document.WorkerForVerification;
    internal bool FallbackForVerification=>document.FallbackForVerification;
#endif
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject current){context=current;current.Title=entry.Name+" · Office document";}
    public Task View(string path,ContextObject current)
    {
        var task=Completion=Open(path);work.Add(task);return task;
    }
    async Task Open(string path)
    {
        if(nativeCore==null)await document.Open(path,context.Token);else await document.OpenCore(path,nativeCore,context.Token);
        context.Token.ThrowIfCancellationRequested();
        var panel=new DockPanel();
        warning=new TextBlock{Margin=new Thickness(12,2,12,8),FontSize=11,HorizontalAlignment=HorizontalAlignment.Right,TextTrimming=TextTrimming.CharacterEllipsis,Visibility=Visibility.Collapsed};
        warning.SetResourceReference(TextBlock.ForegroundProperty,"PreviewWarning");AutomationProperties.SetName(warning,"Preview warning");
        DockPanel.SetDock(warning,Dock.Bottom);panel.Children.Add(warning);
        notes=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,8,12,0)};
        importNotes=new ScrollViewer{Content=notes,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};
        AutomationProperties.SetName(importNotes,"Document import notes");
        DockPanel.SetDock(importNotes,Dock.Bottom);panel.Children.Add(importNotes);
        var controls=new WrapPanel{Margin=new Thickness(8),Visibility=document.Info.Kind is "image" or "artboards" or "frames"?Visibility.Collapsed:Visibility.Visible};
        previous=PreviewControls.Icon(owner,"Previous page","\uE76B");next=PreviewControls.Icon(owner,"Next page","\uE76C");
        label=new TextBlock{Margin=new Thickness(10,5,10,5),VerticalAlignment=VerticalAlignment.Center};
        number=new TextBox{Width=48,MinHeight=30,Margin=new Thickness(4),VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Enter a page number and press Enter"};
        number.SetResourceReference(Control.BackgroundProperty,"Palette.08FFFFFF");number.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");AutomationProperties.SetName(number,"Page number");
        previous.Click+=(_,_)=>Go(currentPart,currentPage-1);next.Click+=(_,_)=>Go(currentPart,currentPage+1);
        number.PreviewKeyDown+=(_,args)=>
        {
            if(args.Key!=System.Windows.Input.Key.Enter)return;args.Handled=true;
            int count=document.Info.Parts[currentPart].Pages;
            if(int.TryParse(number.Text,out int page)&&page>0&&page<=count)Go(currentPart,page-1);
            else label.Text=$"Enter a page from 1 to {count}";
        };
        controls.Children.Add(previous);controls.Children.Add(label);controls.Children.Add(next);controls.Children.Add(number);
        DockPanel.SetDock(controls,Dock.Bottom);panel.Children.Add(controls);
        sheets=new ComboBox{Margin=new Thickness(12,8,12,0),Style=(Style)owner.FindResource("PreviewClipCombo"),MaxDropDownHeight=300};
        AutomationProperties.SetName(sheets,document.Info.Kind switch {"artboards"=>"Artboard","frames"=>"Design frame",_=>"Workbook sheet"});DockPanel.SetDock(sheets,Dock.Top);panel.Children.Add(sheets);
        sheets.SelectionChanged+=(_,_)=>{if(!disposed&&!updating&&sheets.SelectedIndex>=0)Go(sheets.SelectedIndex,0);};
        image=new Image{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Margin=new Thickness(12)};
        panel.Children.Add(new ScrollViewer{Content=image,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        context.ViewerContent=panel;
        await Render(0,0);
    }
    void Go(int part,int page)
    {
        if(disposed||part<0||part>=document.Info.Parts.Length||page<0||(document.Info.Parts[part].Pages>0&&page>=document.Info.Parts[part].Pages))return;
        var task=Completion=Render(part,page);work.Add(task);context.Track(task);
    }
    async Task Render(int part,int page)
    {
        if(disposed)return;
        int current=++revision;
        rendering?.Cancel();rendering?.Dispose();rendering=CancellationTokenSource.CreateLinkedTokenSource(context.Token);var token=rendering.Token;
        previous.IsEnabled=next.IsEnabled=number.IsEnabled=false;label.Text="Rendering document…";
        try
        {
            var result=await document.RenderPage(part,page,token);
            if(disposed||current!=revision||!context.IsCurrent)return;
            var raster=result.Raster;
            // WPF Pbgra32 and the cores' premultiplied RGBA differ only in R/B.
            for(int i=0;i<raster.Pixels.Length;i+=4)(raster.Pixels[i],raster.Pixels[i+2])=(raster.Pixels[i+2],raster.Pixels[i]);
            var bitmap=BitmapSource.Create(raster.Width,raster.Height,96,96,PixelFormats.Pbgra32,null,raster.Pixels,raster.Width*4);bitmap.Freeze();
            image.Source=bitmap;currentPart=result.Part;currentPage=result.Page;
            int count=document.Info.Parts[currentPart].Pages;
            label.Text=$"Page {currentPage+1} of {count}";number.Text=(currentPage+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            previous.IsEnabled=currentPage>0;next.IsEnabled=currentPage+1<count;number.IsEnabled=true;
            updating=true;
            try{sheets.ItemsSource=document.Sheets;sheets.SelectedIndex=currentPart;sheets.Visibility=document.Sheets.Length>0?Visibility.Visible:Visibility.Collapsed;}
            finally{updating=false;}
            notes.Text=document.Warnings;importNotes.Visibility=notes.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
            ShowWarning(document.PrimaryFailure);
            context.Title=entry.Name+" · "+document.Info.Engine;
            context.SetRenderer(document.Info.Engine,document.RendererDetails);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if(!disposed&&current==revision&&context.IsCurrent){image.Source=null;label.Text="Document preview unavailable";ShowWarning(error.Message);}
            if(current==1)throw;
        }
    }
    void ShowWarning(string detail)
    {
        warning.Text=string.IsNullOrWhiteSpace(detail)?"":"Error loading: "+entry.Name;
        warning.ToolTip=detail;warning.Visibility=warning.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    public void Cleanup()
    {
        if(disposed)return;disposed=true;++revision;rendering?.Cancel();context?.Track(Close());
    }
    async Task Close()
    {
        try{await Task.WhenAll(work.ToArray());}
        catch(Exception error)when(error is IOException or OperationCanceledException or NotSupportedException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException){}
        finally{rendering?.Dispose();await document.DisposeAsync();}
    }
}
