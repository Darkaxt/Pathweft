using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileExplorer.Preview;

// PREVIEW-39: what a .lnk or .url shortcut points to, read from the shortcut itself. Nothing is resolved,
// launched or repaired while previewing; a network target is reported without contacting the server.
internal sealed record ShortcutDetails(List<(string Name,string Value)> Rows,string Target,ShortcutKind Kind);
internal enum ShortcutKind{File,Folder,Missing,Network,Address,WindowsItem}

internal static class ShortcutInspection
{
    internal static readonly string[] Extensions=[".lnk",".url"];
    const int SldfRunAsUser=0x2000;

    internal static ShortcutDetails Read(string path,CancellationToken token)=>
        Path.GetExtension(path).Equals(".url",StringComparison.OrdinalIgnoreCase)?ReadInternet(path,token):ReadLink(path,token);

    static ShortcutDetails ReadLink(string path,CancellationToken token)
    {
        var link=(MainWindow.IShellLinkW)new MainWindow.ShellLink();
        IntPtr buffer=Marshal.AllocHGlobal(2*32768);
        try
        {
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path,0);token.ThrowIfCancellationRequested();
            string Text(Action<IntPtr> read){Marshal.WriteInt16(buffer,0);read(buffer);return Marshal.PtrToStringUni(buffer)??"";}
            string target=Text(b=>link.GetPath(b,32768,IntPtr.Zero,0));
            string arguments=Text(b=>link.GetArguments(b,32768)),start=Text(b=>link.GetWorkingDirectory(b,32768)),comment=Text(b=>link.GetDescription(b,32768));
            int iconIndex=0;string icon=Text(b=>link.GetIconLocation(b,32768,out iconIndex));
            link.GetShowCmd(out int show);link.GetHotkey(out short hotkey);
            bool administrator=link is IShellLinkDataList data&&data.GetFlags(out uint flags)==0&&(flags&SldfRunAsUser)!=0;
            var rows=new List<(string,string)>();
            ShortcutKind kind;
            if(target.Length==0){target=ItemName(link);kind=ShortcutKind.WindowsItem;rows.Add(("Target",target.Length>0?target+" (Windows item, no file path)":"Windows item without a file path"));}
            else{kind=Classify(target);rows.Add(("Target",target));}
            Add(rows,"Arguments",arguments);Add(rows,"Start in",start);
            rows.Add(("Run",show switch{3=>"Maximized",7 or 2 or 6=>"Minimized",_=>"Normal window"}));
            Add(rows,"Shortcut key",Hotkey(hotkey));
            if(administrator)rows.Add(("Run as administrator","Yes"));
            Add(rows,"Icon",icon.Length>0?$"{icon}, {iconIndex}":"");
            Add(rows,"Comment",comment);
            rows.Add(("Target state",State(kind)));
            return new(rows,target,kind);
        }
        catch(Exception error) when(error is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {throw new InvalidDataException("This shortcut could not be read: "+error.Message,error);}
        finally{Marshal.FreeHGlobal(buffer);Marshal.ReleaseComObject(link);}
    }

    // [InternetShortcut] of an Internet shortcut: an INI file, read as UTF-8 unless a byte order mark says otherwise.
    static ShortcutDetails ReadInternet(string path,CancellationToken token)
    {
        string text;
        using(var reader=new StreamReader(new MemoryStream(PreviewInput.Read(path,1024*1024,token)),Encoding.Default,true))text=reader.ReadToEnd();
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool section=false;
        foreach(string raw in text.Split('\n'))
        {
            string line=raw.Trim();
            if(line.StartsWith('[')){section=line.Equals("[InternetShortcut]",StringComparison.OrdinalIgnoreCase);continue;}
            int equals=line.IndexOf('=');if(section&&equals>0)values.TryAdd(line[..equals].Trim(),line[(equals+1)..].Trim());
        }
        string address=values.GetValueOrDefault("URL")??throw new InvalidDataException("This Internet shortcut has no address.");
        string target=address;var kind=ShortcutKind.Address;
        if(Uri.TryCreate(address,UriKind.Absolute,out var uri)&&uri.IsFile){target=uri.LocalPath;kind=Classify(target);}
        var rows=new List<(string,string)>{("Address",address)};
        string icon=values.GetValueOrDefault("IconFile")??"";Add(rows,"Icon",icon.Length>0?$"{icon}, {values.GetValueOrDefault("IconIndex")??"0"}":"");
        if(int.TryParse(values.GetValueOrDefault("HotKey"),out int hotkey))Add(rows,"Shortcut key",Hotkey((short)hotkey));
        rows.Add(("Target state",State(kind)));
        return new(rows,target,kind);
    }

    static ShortcutKind Classify(string target)=>
        PaneView.IsRemote(target)?ShortcutKind.Network:Directory.Exists(target)?ShortcutKind.Folder:File.Exists(target)?ShortcutKind.File:ShortcutKind.Missing;
    static string State(ShortcutKind kind)=>kind switch
    {
        ShortcutKind.File=>"File found",ShortcutKind.Folder=>"Folder found",ShortcutKind.Missing=>"Not found at this location",
        ShortcutKind.Network=>"Network location (not checked while previewing)",ShortcutKind.Address=>"Web address",_=>"Windows item"
    };
    static void Add(List<(string,string)> rows,string name,string value){if(!string.IsNullOrWhiteSpace(value))rows.Add((name,value));}

    // HOTKEYF_SHIFT 1, HOTKEYF_CONTROL 2, HOTKEYF_ALT 4 in the high byte; a virtual key in the low byte.
    internal static string Hotkey(short value)
    {
        int key=value&0xFF,modifiers=(value>>8)&0xFF;if(key==0)return "";
        var parts=new List<string>();if((modifiers&2)!=0)parts.Add("Ctrl");if((modifiers&4)!=0)parts.Add("Alt");if((modifiers&1)!=0)parts.Add("Shift");
        var name=KeyInterop.KeyFromVirtualKey(key);
        parts.Add(name is >=Key.D0 and <=Key.D9?((char)('0'+(name-Key.D0))).ToString():name.ToString());
        return string.Join("+",parts);
    }

    static string ItemName(MainWindow.IShellLinkW link)
    {
        link.GetIDList(out IntPtr item);if(item==IntPtr.Zero)return "";
        try{return SHGetNameFromIDList(item,0,out IntPtr name)==0?Take(name):"";}
        finally{Marshal.FreeCoTaskMem(item);}
        static string Take(IntPtr name){try{return Marshal.PtrToStringUni(name)??"";}finally{Marshal.FreeCoTaskMem(name);}}
    }
    [DllImport("shell32.dll")]static extern int SHGetNameFromIDList(IntPtr item,int form,out IntPtr name);

    [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("45e2b4ae-b1c3-11d0-b92f-00a0c90312e1")]
    interface IShellLinkDataList
    {
        [PreserveSig]int AddDataBlock(IntPtr block);
        [PreserveSig]int CopyDataBlock(uint signature,out IntPtr block);
        [PreserveSig]int RemoveDataBlock(uint signature);
        [PreserveSig]int GetFlags(out uint flags);
        [PreserveSig]int SetFlags(uint flags);
    }
}

internal sealed class ShortcutViewer(MainWindow owner):IViewer
{
    public bool CanHandle(string path)=>true;
    public void Init(){}
    public void Prepare(string path,ContextObject context)=>context.Title=Path.GetFileName(path)+" · Shortcut";
    public async Task View(string path,ContextObject context)
    {
        var details=await Task.Run(()=>ShortcutInspection.Read(path,context.Token),context.Token);
        context.Token.ThrowIfCancellationRequested();
        var panel=new DockPanel();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12,8,12,0)};
        DockPanel.SetDock(actions,Dock.Top);panel.Children.Add(actions);
        var open=PreviewControls.Icon(owner,details.Kind==ShortcutKind.Folder?"Open target folder":"Open","");
        var reveal=PreviewControls.Icon(owner,"Show target in its folder","");
        open.IsEnabled=details.Kind!=ShortcutKind.Missing;
        reveal.IsEnabled=details.Kind is ShortcutKind.File or ShortcutKind.Folder or ShortcutKind.Network;
        open.Click+=(_,_)=>
        {
            if(details.Kind==ShortcutKind.Folder)owner.OpenWidgetLocation(details.Target);
            else try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}
                catch(System.ComponentModel.Win32Exception){} // cancelled or refused by Windows, as when opened from the list
        };
        reveal.Click+=(_,_)=>owner.RevealWidgetResult(details.Target);
        actions.Children.Add(open);actions.Children.Add(reveal);
        panel.Children.Add(ExecutableDetails.View(details.Rows));
        context.ViewerContent=panel;
    }
    public void Cleanup(){}
}
