using System.Windows;
using System.Windows.Controls;
using FileExplorer.Preview;

namespace FileExplorer;

/// PREVIEW-41: one preview card and the state of what it shows. The card belongs to a pane and follows that pane's
/// selection; MainWindow places it (docked in the pane, picture in picture over its list, or its own window).
public partial class PreviewCard : Border
{
    readonly MainWindow owner;
    internal PaneView Pane { get; }
    internal PreviewSession Session { get; }
    internal bool Pinned;
    internal string Shown;
    internal MainWindow.PreviewMode Mode;
    internal Window Window;

    internal PreviewCard(MainWindow owner, PaneView pane, Action<PreviewCard, string> heading)
    {
        InitializeComponent();
        this.owner = owner; Pane = pane;
        Session = new(owner, PreviewBody, text => heading(this, text));
        PreviewTitle.ContextMenu = Session.Menu; PreviewTitle.ToolTip = "Right-click to choose a preview module";
    }

    void Close_Click(object sender, RoutedEventArgs e) => owner.ClosePreviewFrom(this);
    void Window_Click(object sender, RoutedEventArgs e) => owner.ToggleCardWindow(this);
    void Pip_Click(object sender, RoutedEventArgs e) => owner.ToggleCardPip(this);
    void Pin_Click(object sender, RoutedEventArgs e) => owner.TogglePin(this);
    void Mark_Click(object sender, RoutedEventArgs e) => owner.ToggleMark(this);
    void MarkPrev_Click(object sender, RoutedEventArgs e) => owner.StepMarked(Pane, -1);
    void MarkNext_Click(object sender, RoutedEventArgs e) => owner.StepMarked(Pane, 1);
}
