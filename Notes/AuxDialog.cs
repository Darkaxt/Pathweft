using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

/// The glass dialog frame the S21 dialogs share (notes, links, saved searches, actions, comparison):
/// the same tint, margins, fields and footer buttons as PromptDialog and the column dialogs.
internal class AuxDialog : Window
{
    protected readonly StackPanel Body=new();
    protected readonly DockPanel Footer=new(){Margin=new Thickness(0,14,0,0)};
    protected readonly Button Accept=new(){IsDefault=true,Tag="on"};
    readonly Grid root=new();
    internal AuxDialog(Window owner,string title,string accept,double width=460)
    {
        Title=title;Width=width;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=owner!=null?WindowStartupLocation.CenterOwner:WindowStartupLocation.CenterScreen;ShowInTaskbar=false;Background=Brushes.Transparent;
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");FontSize=12;SetResourceReference(ForegroundProperty,"Palette.E8EEF6");
        var dock=new DockPanel();dock.SetResourceReference(MarginProperty,"AuxiliaryMargin");
        var heading=new TextBlock{Text=title,FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(2,0,0,12)};DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
        Accept.Content=accept;Accept.SetResourceReference(StyleProperty,"AuxiliaryAction");Accept.Click+=(_,_)=>{if(Validate())DialogResult=true;};
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,6,0)};cancel.SetResourceReference(StyleProperty,"AuxiliaryAction");
        DockPanel.SetDock(Accept,Dock.Right);DockPanel.SetDock(cancel,Dock.Right);Footer.Children.Add(Accept);Footer.Children.Add(cancel);Footer.Children.Add(new Border());
        DockPanel.SetDock(Footer,Dock.Bottom);dock.Children.Add(Footer);dock.Children.Add(Body);
        root.Children.Add(dock);Content=root;
        AuxiliaryWindows.Prepare(this,root);Owner=owner;
        SourceInitialized+=(_,_)=>MainWindow.ApplyAcrylic(this);root.Background=MainWindow.Tint();
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    /// Accept runs this first; false keeps the dialog open (the message is already shown).
    protected virtual bool Validate()=>true;
    internal bool Present()
    {
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        return ShowDialog()==true;
    }
    internal static TextBlock Label(string text,double top=8)
    {
        var label=new TextBlock{Text=text,Margin=new Thickness(2,top,0,5),TextWrapping=TextWrapping.Wrap};label.SetResourceReference(TextBlock.ForegroundProperty,"Palette.8A97AA");return label;
    }
    internal static TextBox Field(string text,string name,out Border frame,bool multiline=false)
    {
        var box=new TextBox{Text=text??"",FontSize=13,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?60:0};
        box.SetResourceReference(StyleProperty,"Bare");System.Windows.Automation.AutomationProperties.SetName(box,name);
        frame=new Border{Child=box};frame.SetResourceReference(StyleProperty,"AuxiliaryField");return box;
    }
    internal static ComboBox Choice(string name,params string[] items)
    {
        var box=new ComboBox{ItemsSource=items,SelectedIndex=0,MinHeight=30};box.SetResourceReference(StyleProperty,"PreviewClipCombo");
        System.Windows.Automation.AutomationProperties.SetName(box,name);return box;
    }
    internal static CheckBox Check(string text,bool on)=>new(){Content=text,IsChecked=on,Margin=new Thickness(2,6,0,0)};
    internal static Button Action(string text,Action run)
    {
        var button=new Button{Content=text,Margin=new Thickness(0,0,6,0)};button.SetResourceReference(StyleProperty,"AuxiliaryAction");button.Click+=(_,_)=>run();return button;
    }
    /// A muted line for a reason or an error inside the dialog.
    internal static TextBlock Note(string text)
    {
        var note=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,8,0,0),FontSize=11};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return note;
    }
}
