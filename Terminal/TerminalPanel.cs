using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Terminal.Wpf;

namespace FileExplorer;

// BROWSE-08: a pane's embedded terminal. One session per pane, started the first time the panel is shown,
// kept across tab switches and hiding, ended by Dispose (pane closed or application exit).
// Browser -> terminal: FollowFolder sends Set-Location only while the shell sits at an empty prompt.
// Terminal -> browser: FolderReported carries the shell's OSC 9;9 folder. Both directions skip the folder the
// other side just reported, so neither echoes the other.
internal sealed class TerminalPanel : Border,IDisposable
{
    readonly Grid root=new();
    readonly TerminalControl terminal=new(){AutoResize=true,Focusable=true};
    readonly Border notice=new(){Visibility=Visibility.Collapsed,Padding=new Thickness(12,8,12,8),VerticalAlignment=VerticalAlignment.Top};
    readonly TextBlock noticeText=new(){TextWrapping=TextWrapping.Wrap,FontSize=12};
    readonly Button restart=new(){Content="Start a new session",Margin=new Thickness(0,6,0,0),HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed};
    ConPtyConnection session;
    string browserFolder,terminalFolder,unsupported;
    bool disposed;

    /// The shell changed folder (OSC 9;9), on the UI thread: the pane navigates there.
    internal event Action<string> FolderReported;
    internal bool Running=>session!=null;
    internal void FocusShell()=>terminal.Focus();
#if DEBUG
    internal string FolderForVerification=>terminalFolder;
    internal string UnsupportedForVerification=>unsupported;
    internal bool SendForVerification(string line)=>session?.SendCommand(line)==true;
#endif
    /// Whether the shell has reported folders and prompt marks yet (OSC 9;9 / 133 seen).
    internal bool Synchronized=>session?.ShellIntegrationSeen==true;

    internal TerminalPanel()
    {
        SetResourceReference(BackgroundProperty,"Palette.F2161B24");
        BorderThickness=new Thickness(0,1,0,0);SetResourceReference(BorderBrushProperty,"Palette.12FFFFFF");
        AutomationProperties.SetName(terminal,"Terminal");
        noticeText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        restart.SetResourceReference(StyleProperty,"AuxiliaryAction");restart.Click+=(_,_)=>Restart();
        var stack=new StackPanel();stack.Children.Add(noticeText);stack.Children.Add(restart);notice.Child=stack;
        notice.SetResourceReference(BackgroundProperty,"Palette.F2161B24");
        root.Children.Add(terminal);root.Children.Add(notice);Child=root;
        Loaded+=(_,_)=>ApplyTheme();
        IsVisibleChanged+=(_,_)=>{if(IsVisible&&!disposed)EnsureSession();};
    }

    /// The pane shows `path`. A local folder is followed; any other place shows why it is not.
    internal void FollowFolder(string path)
    {
        if(disposed)return;
        if(!IsLocalFolder(path)){ShowUnsupported("not a local folder");return;}
        unsupported=null;HideNotice();
        browserFolder=path;
        if(session==null){if(IsVisible)EnsureSession();return;}
        if(Same(path,terminalFolder))return; // the shell is there already (or just told us it went there)
        Follow();
    }

    /// Sends the browser's folder to the shell when it sits at an empty prompt; otherwise the next empty prompt does.
    void Follow()
    {
        if(disposed||session==null||unsupported!=null||browserFolder==null||Same(browserFolder,terminalFolder))return;
        if(session.SendCommand($"Set-Location -LiteralPath '{Quote(browserFolder)}' {ConPtyConnection.FollowMarker}"))terminalFolder=browserFolder;
    }

    /// Archive, search, remote and virtual places: the session stays, and says why it does not follow.
    internal void ShowUnsupported(string reason)
    {
        if(disposed)return;
        unsupported=reason;noticeText.Text=$"This place is {reason}; the terminal stays in {terminalFolder??"its folder"}.";
        restart.Visibility=Visibility.Collapsed;notice.Visibility=Visibility.Visible;
    }

    void EnsureSession()
    {
        if(session!=null||disposed)return;
        var start=IsLocalFolder(browserFolder)?browserFolder:Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var connection=new ConPtyConnection(start);
        connection.FolderReported+=folder=>Dispatcher.BeginInvoke(()=>Reported(connection,folder));
        connection.Exited+=code=>Dispatcher.BeginInvoke(()=>Ended(connection,code));
        connection.EmptyPromptChanged+=empty=>{if(empty)Dispatcher.BeginInvoke(()=>{if(ReferenceEquals(connection,session))Follow();});};
        session=connection;terminalFolder=start;
        try{terminal.Connection=connection;} // the control calls Start once it is ready
        catch(Exception ex)when(ex is IOException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {session=null;connection.Dispose();ShowEnded("The terminal could not start: "+ex.Message);}
        if(unsupported!=null)ShowUnsupported(unsupported);
    }

    void Reported(ConPtyConnection from,string folder)
    {
        if(disposed||!ReferenceEquals(from,session))return;
        bool moved=!Same(folder,terminalFolder); // a prompt repeating the same folder is not a move
        terminalFolder=folder;
        if(!moved||Same(folder,browserFolder))return; // the shell followed the browser: no echo back
        browserFolder=folder;FolderReported?.Invoke(folder);
    }

    void Ended(ConPtyConnection from,int? code)
    {
        if(disposed||!ReferenceEquals(from,session))return;
        session=null;from.Dispose();
        ShowEnded(code is {} exit?$"The shell ended (exit code {exit}).":"The shell ended.");
    }

    void ShowEnded(string text){noticeText.Text=text;restart.Visibility=Visibility.Visible;notice.Visibility=Visibility.Visible;}
    void HideNotice(){if(restart.Visibility!=Visibility.Visible)notice.Visibility=Visibility.Collapsed;}
    void Restart(){restart.Visibility=Visibility.Collapsed;notice.Visibility=Visibility.Collapsed;EnsureSession();terminal.Focus();}

    void ApplyTheme()
    {
        static uint Colorref(Color c)=>(uint)(c.R|c.G<<8|c.B<<16);
        Color Of(string key,Color fallback)=>TryFindResource(key) is SolidColorBrush brush?brush.Color:fallback;
        var background=Of("Palette.F2161B24",Color.FromRgb(0x16,0x1B,0x24));
        var theme=new TerminalTheme
        {
            DefaultBackground=Colorref(background),
            DefaultForeground=Colorref(Of("Palette.E8EEF6",Color.FromRgb(0xE8,0xEE,0xF6))),
            DefaultSelectionBackground=Colorref(Of("Accent",Color.FromRgb(0x4C,0x8D,0xFF))),
            CursorStyle=CursorStyle.BlinkingBar,
            // Campbell, Windows Terminal's default scheme
            ColorTable=new uint[]{0x0C0C0C,0x1F0FC5,0x0EA113,0x009CC1,0xDA3700,0x981788,0xDD963A,0xCCCCCC,0x767676,0x5648E7,0x0CC616,0xA5F1F9,0xFF783B,0x9E00B4,0xD6D661,0xF2F2F2},
        };
        terminal.SetTheme(theme,Fonts.SystemFontFamilies.Any(f=>f.Source=="Cascadia Mono")?"Cascadia Mono":"Consolas",11,background);
    }

    static bool IsLocalFolder(string path)=>!string.IsNullOrWhiteSpace(path)&&!path.Contains("://",StringComparison.Ordinal)&&Path.IsPathFullyQualified(path);
    static bool Same(string a,string b)=>a!=null&&b!=null&&string.Equals(a.TrimEnd('\\'),b.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);
    // PowerShell single-quoted literal: every quote character PowerShell accepts as a quote is doubled.
    internal static string Quote(string path)=>string.Concat(path.Select(c=>c is '\'' or '‘' or '’' or '‚' or '‛'?new string(c,2):c.ToString()));

    public void Dispose()
    {
        if(disposed)return;disposed=true;
        var ending=session;session=null;ending?.Dispose();
    }
}
