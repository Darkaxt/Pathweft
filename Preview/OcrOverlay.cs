using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FileExplorer.Preview;

// Image preview with an opt-in Text Extractor (TOOLS-04): drag a region or take the whole image,
// then copy the recognized text explicitly. Nothing reaches the clipboard without the Copy action.
internal sealed class OcrOverlay : Grid
{
    readonly ContextObject context;
    readonly BitmapSource bitmap;
    readonly Grid frame=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new(16)};
    readonly Canvas surface=new();
    readonly Rectangle marquee=new(){StrokeThickness=1.5,StrokeDashArray=new DoubleCollection{4,2},Visibility=Visibility.Collapsed,IsHitTestVisible=false};
    readonly Button toggle,copy,whole,close;
    readonly Border panel=new(){Visibility=Visibility.Collapsed,Padding=new(8,6,8,8)};
    readonly TextBlock status;
    readonly TextBox result;
    readonly ComboBox language;
    readonly IReadOnlyList<(string Tag,string Name)> languages;
    CancellationTokenSource request;
    Point? anchor;
    bool active;
    internal Task Recognition { get; private set; } = Task.CompletedTask;
#if DEBUG
    internal static Action<string> ClipboardForVerification;
    internal FrameworkElement FrameForVerification=>frame;
    internal Button ToggleForVerification=>toggle;
    internal Button CopyForVerification=>copy;
    internal ComboBox LanguageForVerification=>language;
    internal string StatusText=>status.Text;
    internal BitmapSource BitmapForVerification=>bitmap;
#endif
    internal string ResultText=>result.Text;
    internal double Scale=>frame.ActualWidth>0?bitmap.PixelWidth/frame.ActualWidth:1;

    internal OcrOverlay(MainWindow owner,Image image,ContextObject context)
    {
        this.context=context;bitmap=(BitmapSource)image.Source;
        RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        image.Margin=new(0);
        frame.Children.Add(image);frame.Children.Add(surface);surface.Children.Add(marquee);
        marquee.SetResourceReference(Shape.StrokeProperty,"AccentSelect");
        Children.Add(frame);

        toggle=PreviewControls.Icon(owner,"Recognize text in this image","\uE8C1");
        toggle.HorizontalAlignment=HorizontalAlignment.Right;toggle.VerticalAlignment=VerticalAlignment.Top;toggle.Margin=new(6);
        toggle.Click+=(_,_)=>SetActive(!active);
        Children.Add(toggle);

        languages=SafeLanguages();
        language=new ComboBox{Style=(Style)owner.FindResource("PreviewClipCombo"),MinWidth=140,Margin=new(2),VerticalAlignment=VerticalAlignment.Center,
            Visibility=languages.Count>1?Visibility.Visible:Visibility.Collapsed,ToolTip="OCR language"};
        AutomationProperties.SetName(language,"OCR language");
        language.SelectionChanged+=(_,_)=>{if(active)SetActive(true);};
        foreach(var item in languages)language.Items.Add(new ComboBoxItem{Content=item.Name,Tag=item.Tag});
        if(languages.Count>0)
        {
            string ui=System.Globalization.CultureInfo.CurrentUICulture.Name;
            int match=Enumerable.Range(0,languages.Count).FirstOrDefault(i=>languages[i].Tag.Equals(ui,StringComparison.OrdinalIgnoreCase)||ui.StartsWith(languages[i].Tag+"-",StringComparison.OrdinalIgnoreCase));
            language.SelectedIndex=match;
        }
        whole=PreviewControls.Icon(owner,"Recognize text in the whole image","\uE740");
        whole.Click+=(_,_)=>Start(null);
        copy=PreviewControls.Icon(owner,"Copy text","\uE8C8");copy.IsEnabled=false;
        copy.Click+=(_,_)=>Copy();
        close=PreviewControls.Icon(owner,"Close text recognition","\uE711");
        close.Click+=(_,_)=>SetActive(false);
        status=new TextBlock{TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new(6,0,6,0)};
        status.SetResourceReference(TextBlock.ForegroundProperty,"Palette.8A97AA");
        AutomationProperties.SetName(status,"Text recognition status");
        result=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=44,MaxHeight=160,Margin=new(2,6,2,0),Padding=new(6),
            BorderThickness=new(0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};
        result.SetResourceReference(Control.BackgroundProperty,"Palette.08FFFFFF");result.SetResourceReference(Control.ForegroundProperty,"Palette.E8EEF6");
        AutomationProperties.SetName(result,"Recognized text");
        var bar=new DockPanel();
        foreach(var button in new[]{close,copy,whole}){DockPanel.SetDock(button,Dock.Right);bar.Children.Add(button);}
        DockPanel.SetDock(language,Dock.Left);bar.Children.Add(language);bar.Children.Add(status);
        var stack=new StackPanel();stack.Children.Add(bar);stack.Children.Add(result);
        panel.Child=stack;panel.SetResourceReference(Border.BackgroundProperty,"Palette.0DFFFFFF");
        SetRow(panel,1);Children.Add(panel);

        surface.MouseLeftButtonDown+=(_,e)=>
        {
            if(!active)return;
            anchor=e.GetPosition(frame);surface.CaptureMouse();
            Place(new Rect(anchor.Value,anchor.Value));marquee.Visibility=Visibility.Visible;e.Handled=true;
        };
        surface.MouseMove+=(_,e)=>{if(anchor is {} start&&surface.IsMouseCaptured)Place(new Rect(start,Clamp(e.GetPosition(frame))));};
        surface.MouseLeftButtonUp+=(_,e)=>
        {
            if(anchor is not {} start)return;
            anchor=null;surface.ReleaseMouseCapture();e.Handled=true;
            var region=new Rect(start,Clamp(e.GetPosition(frame)));
            // a click without a drag means the whole image
            if(region.Width<4||region.Height<4){marquee.Visibility=Visibility.Collapsed;Start(null);}
            else{Place(region);Start(region);}
        };
    }

    static IReadOnlyList<(string Tag,string Name)> SafeLanguages()
    {
        try{return TextExtractor.Languages();}
        catch(Exception error)when(error is not OutOfMemoryException){return Array.Empty<(string,string)>();}
    }

    Point Clamp(Point p)=>new(Math.Clamp(p.X,0,frame.ActualWidth),Math.Clamp(p.Y,0,frame.ActualHeight));
    void Place(Rect r){Canvas.SetLeft(marquee,r.X);Canvas.SetTop(marquee,r.Y);marquee.Width=r.Width;marquee.Height=r.Height;}

    void SetActive(bool value)
    {
        active=value;toggle.Tag=value?"on":null;
        panel.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        surface.Background=value?Brushes.Transparent:null;surface.Cursor=value?Cursors.Cross:null;
        if(!value){request?.Cancel();marquee.Visibility=Visibility.Collapsed;return;}
        string tag=LanguageTag();string missing=Unusable(tag);
        Report(missing??"Drag over the image to choose a region, or recognize the whole image.",missing!=null);
        whole.IsEnabled=missing==null;
    }

    string LanguageTag()=>language.SelectedItem is ComboBoxItem{Tag:string tag}?tag:"";
    static string Unusable(string tag)
    {
        try{return TextExtractor.Unavailable(tag);}
        catch(Exception error)when(error is not OutOfMemoryException){return "Windows OCR is not available on this system. "+error.Message;}
    }

    void Report(string text,bool warning)
    {
        status.Text=text;
        if(warning)status.SetResourceReference(TextBlock.ForegroundProperty,"PreviewWarning");
        else status.SetResourceReference(TextBlock.ForegroundProperty,"Palette.8A97AA");
    }

    void Start(Rect? region){Recognition=Recognize(region);context.Track(Recognition);}

    // region is in DIPs relative to the displayed image; null is the whole image
    internal async Task Recognize(Rect? region)
    {
        if(!active)SetActive(true);
        request?.Cancel();
        var mine=request=CancellationTokenSource.CreateLinkedTokenSource(context.Token);
        var token=mine.Token;
        copy.IsEnabled=false;result.Text="";result.Visibility=Visibility.Collapsed;
        string tag=LanguageTag();
        if(Unusable(tag) is {} missing){Report(missing,true);return;}
        Int32Rect? pixels=null;
        if(region is {} r)
        {
            var mapped=TextExtractor.RegionToPixels(r,Scale,bitmap.PixelWidth,bitmap.PixelHeight);
            if(mapped.Width==0||mapped.Height==0){Report("The selected region is outside the image.",true);return;}
            pixels=mapped;
        }
        Report("Recognizing text…",false);
        try
        {
            string text=await Task.Run(()=>TextExtractor.RecognizeAsync(bitmap,pixels,tag,token),token);
            if(!context.IsCurrent||request!=mine)return;
            if(string.IsNullOrWhiteSpace(text)){Report("No text was recognized"+(pixels==null?" in this image.":" in the selected region."),false);return;}
            result.Text=text.Trim();result.Visibility=Visibility.Visible;copy.IsEnabled=true;
            Report("Recognized text; check it before relying on it.",false);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {if(context.IsCurrent&&request==mine)Report("Text recognition unavailable. "+error.Message,true);}
    }

    internal void Copy()
    {
        string text=result.Text;if(string.IsNullOrEmpty(text))return;
#if DEBUG
        if(ClipboardForVerification!=null){ClipboardForVerification(text);Report("Text copied.",false);return;}
#endif
        string error=ClipboardText.TrySet(text);Report(error??"Text copied.",error!=null);
    }
}
