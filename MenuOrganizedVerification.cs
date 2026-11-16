#if DEBUG
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileExplorer;

// S49 (MENU-05..09): one organized Pathweft menu, checked on the real menus this PC builds.
public partial class MainWindow
{
    internal async Task VerifyOrganizedMenu49(string root,string source,string file,string folder,string move,string zip,
        Func<string[],Task> select,Func<ContextMenu> menu,Action<bool,string> check)
    {
        Settings.NativeMenu=false;
        var inventory=new Dictionary<string,object>();var timings=new Dictionary<string,double>();
        static string Label(MenuItem item)=>ShellMenu.Comparable(item.Header as string??"");
        // Pathweft's own level: the root and its groups (a provider's own submenu, Tag "shell:…", may repeat names)
        static IEnumerable<MenuItem> Commands(ItemsControl parent)
        {
            foreach(var item in parent.Items.OfType<MenuItem>())
            {
                if(item.Items.Count>0&&item.Tag==null){foreach(var child in Commands(item))yield return child;}
                else yield return item;
            }
        }
        async Task<ContextMenu> Built(string context,params string[] names)
        {
            await select(names);menu();var built=menu(); // the second build is the warm one a user sees after the first
            timings[context]=ShellMenu.LastLoadMilliseconds;
            var commands=MenuCommands(built).ToArray();
            inventory[context]=commands.Select(item=>new{label=item.Header?.ToString(),tag=item.Tag?.ToString(),parent=(item.Parent as MenuItem)?.Header?.ToString()}).ToArray();
            check(!commands.Any(item=>item.Header is string h&&new[]{"Shelf","Reel","Backup Labs","Lume","Windows menu","Stash"}.Any(word=>h.Contains(word,StringComparison.OrdinalIgnoreCase))),"MENU-05/07: no sister-app or Windows menu… item: "+context);
            // a taken-in command never repeats one of Pathweft's or another taken-in one (Sort by / Group by may share names)
            var own=Commands(built).Select(Label).Where(key=>key.Length>0).ToArray();
            var repeated=Commands(built).Where(item=>item.Tag as string is "shell:apps" or "shell:other").Select(Label).Where(key=>own.Count(k=>k==key)>1).Distinct().ToArray();
            check(repeated.Length==0,"MENU-06: no taken-in command repeats one already in Pathweft's groups: "+context+(repeated.Length>0?" ("+string.Join(", ",repeated)+")":""));
            return built;
        }
        MenuItem Taken(ContextMenu built,Func<string,bool> match)=>MenuCommands(built).FirstOrDefault(item=>item.Tag as string is "shell:apps" or "shell:other"&&match(Label(item)));

        // Explorer's own menu for the same items, as the shell reports it (Shell.Application), for parity
        static string[] ExplorerVerbs(string folder,string name)
        {
            dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            try
            {
                dynamic item=name==null?shell.NameSpace(folder).Self:shell.NameSpace(folder).ParseName(name);
                var labels=new List<string>();foreach(dynamic verb in item.Verbs())labels.Add(((string)verb.Name).Replace("&",""));
                return labels.Select(ShellMenu.Comparable).Where(label=>label.Length>0).ToArray();
            }
            finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        // Explorer's entries Pathweft has under its own name; everything else must be in Pathweft's menu by its label
        var equivalent=new Dictionary<string,string>{["copy as path"]="copy path",["open"]="open",["edit"]="edit",
            ["open in new window"]="open in new window",["open in new tab"]="open in new tab",["pin to quick access"]="pin to quick access",["new"]="new folder",["explore"]="open",["open in pathweft"]="open",
            ["scan with diskweft"]="analyze space in this folder",["analyze disk space usage with ridnacs"]="analyze space in this folder"};
        void Parity(ContextMenu built,string context,string folderOfItems,string name)
        {
            var labels=MenuCommands(built).Select(Label).ToHashSet();
            var explorer=ExplorerVerbs(folderOfItems,name);
            var missing=explorer.Where(verb=>!labels.Contains(verb)&&!(equivalent.TryGetValue(verb,out var own)&&labels.Contains(own))&&
                verb is not ("open in terminal" or "open powershell window here" or "open command window here")).ToArray();
            inventory[context+"-explorer"]=explorer;
            check(missing.Length==0,"MENU-06: everything in Explorer's menu for the "+context+" is in Pathweft's ("+explorer.Length+" entries)"+(missing.Length>0?"; missing: "+string.Join(", ",missing):""));
        }

        var single=await Built("file",Path.GetFileName(file));
        Parity(single,"file",source,Path.GetFileName(file));
        var code=Taken(single,key=>key.Contains("code"));
        check(code!=null&&code.Tag as string=="shell:apps"&&code.Parent==single,"MENU-06: VS Code's Open with Code is taken in beside Open for a file");
        var handlerEntry=Taken(single,key=>key.Contains("file locksmith")||key.Contains("previous versions"));
        check(handlerEntry?.Parent is MenuItem{Header:"More actions"},"MENU-06: a handler-based entry (PowerToys File Locksmith or Previous Versions) is taken in under More actions");
        check(MenuCommands(single).Count(item=>Label(item)=="edit with notepad++")<=1,"MENU-06: Notepad++'s own Edit with Notepad++ does not repeat Pathweft's");
        var folderMenu=await Built("folder",Path.GetFileName(folder));
        Parity(folderMenu,"folder",source,Path.GetFileName(folder));
        var gitBash=Taken(folderMenu,key=>key.Contains("git bash"));
        var vlc=Taken(folderMenu,key=>key.Contains("vlc"));
        check(gitBash!=null&&vlc!=null,"MENU-06: Git Bash here and VLC are taken in for a folder");
        await Built("mixed",Path.GetFileName(file),Path.GetFileName(folder));
        await Built("multiple",Path.GetFileName(file),Path.GetFileName(move));
        var background=await Built("background");
        var backgroundBash=Taken(background,key=>key.Contains("git bash"));
        check(backgroundBash?.Parent is MenuItem{Header:"Tools"},"MENU-06: Git Bash here is taken in under Tools on a folder's background");

        // A taken-in command runs Explorer's own verb, whose registered program exists (observed instead of started)
        var invoked=new List<string>();ShellMenu.InvokeForVerification=invoked.Add;
        try
        {
            void Run(MenuItem item,string[] classes,string program,string what)
            {
                invoked.Clear();
                if(item==null){check(false,"MENU-06: "+what+" is present to run");return;}
                item.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
                string verb=invoked.Count==1?invoked[0]:null,command=null;
                foreach(var cls in classes)
                {
                    if(verb==null||command!=null)break;
                    using var key=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(cls+@"\shell\"+verb+@"\command");
                    command=key?.GetValue("") as string;
                }
                string exe=command==null?null:Environment.ExpandEnvironmentVariables(command.StartsWith('"')?command[1..command.IndexOf('"',1)]:command.Split(' ')[0]);
                check(exe!=null&&File.Exists(exe)&&Path.GetFileName(exe).Equals(program,StringComparison.OrdinalIgnoreCase),"MENU-06: "+what+" runs Explorer's verb "+verb+": "+command);
            }
            Run(code,new[]{"*"},"Code.exe","Open with Code on a file");
            Run(gitBash,new[]{"Directory","Folder"},"git-bash.exe","Git Bash here on a folder");
            Run(vlc,new[]{"Directory","Folder"},"vlc.exe","VLC on a folder");
            Run(backgroundBash,new[]{@"Directory\Background"},"git-bash.exe","Git Bash here on the background");
        }finally{ShellMenu.InvokeForVerification=null;}
        check(timings.Values.All(ms=>ms<500),"MENU-08: building the menu with every registered handler stays fast: "+string.Join(", ",timings.Select(t=>$"{t.Key} {t.Value:0} ms")));

        // MENU-07: Ctrl+right-click and its keyboard equivalents open the other menu
        check(OtherMenuKey(Key.F10,ModifierKeys.Control|ModifierKeys.Shift)&&OtherMenuKey(Key.Apps,ModifierKeys.Control)&&!OtherMenuKey(Key.F10,ModifierKeys.Shift)&&
            !OtherMenuKey(Key.Apps,ModifierKeys.None)&&!OtherMenuKey(Key.F10,ModifierKeys.Control|ModifierKeys.Shift|ModifierKeys.Alt)&&!OtherMenuKey(Key.F10,ModifierKeys.Control),
            "MENU-07: Ctrl+Shift+F10 and Ctrl+Menu key are the keyboard's Ctrl+right-click");
        var natives=new List<(string[] Paths,(int X,int Y)? At)>();var verbs=new List<IReadOnlyList<string>>();
        NativeMenuForVerification=(paths,at)=>natives.Add((paths,at));ShellMenu.InspectForVerification=(_,found,_)=>verbs.Add(found);
        try
        {
            await select(new[]{Path.GetFileName(file)});
            S49ShowMenu(Left,ctrl:true,keyboard:false);
            check(natives.Count==1&&natives[0].Paths.SequenceEqual(new[]{file})&&natives[0].At==null&&verbs.Count==1&&verbs[0].Contains("properties"),"MENU-07: Ctrl+right-click opens the standard Windows menu for the same file, at the pointer");
            S49ShowMenu(Left,ctrl:true,keyboard:true);
            check(natives.Count==2&&natives[1].At!=null,"MENU-07: from the keyboard it opens at the focused item");
            await select(Array.Empty<string>());
            S49ShowMenu(Left,ctrl:true,keyboard:false);
            check(natives.Count==3&&natives[2].Paths.Length==0&&verbs.Count==3,"MENU-07: Ctrl+right-click on empty space opens the folder's Windows menu");
            Settings.NativeMenu=true;await select(new[]{Path.GetFileName(file)});
            archiveMenuForVerification=null;captureS4Menu=true;
            try{S49ShowMenu(Left,ctrl:true,keyboard:false);}finally{captureS4Menu=false;}
            check(natives.Count==3&&archiveMenuForVerification!=null&&MenuCommands(archiveMenuForVerification).Any(item=>item.Header as string=="Rename"),"MENU-07: with the Windows menu as the default, Ctrl+right-click opens Pathweft's");
            Settings.NativeMenu=false;
            await S1Verification.Navigate(Left,zip);await select(new[]{"first.txt"});
            archiveMenuForVerification=null;captureS4Menu=true;
            try{S49ShowMenu(Left,ctrl:true,keyboard:false);}finally{captureS4Menu=false;}
            check(natives.Count==3&&archiveMenuForVerification!=null,"MENU-07: inside an archive Ctrl+right-click shows Pathweft's archive menu");
            await S1Verification.Navigate(Left,source);
        }
        finally{Settings.NativeMenu=false;NativeMenuForVerification=null;ShellMenu.InspectForVerification=null;captureS4Menu=false;}

        // MENU-05: the program carries no ecosystem link, and the one ecosystem file Pathweft wrote is removed
        var program=File.ReadAllBytes(typeof(MainWindow).Assembly.Location);
        string site=string.Join("","protagonist","labs"),tracking=string.Join("","utm","_"); // joined at run time, so this check is not its own match
        check(!Contains(program,Encoding.Unicode.GetBytes(site))&&!Contains(program,Encoding.UTF8.GetBytes(site))&&!Contains(program,Encoding.Unicode.GetBytes(tracking)),"MENU-05: no link to the sister apps' site or UTM address remains in the program");
        string lume=Path.Combine(Settings.Dir,"lume.txt");File.WriteAllText(lume,@"C:\pinned");LoadQuickAccess();
        check(!File.Exists(lume),"MENU-05: Pathweft's own lume.txt is removed");
        File.WriteAllText(Path.Combine(root,"organized-menu.json"),JsonSerializer.Serialize(new{inventory,timings},new JsonSerializerOptions{WriteIndented=true}));
    }
    static bool Contains(byte[] data,byte[] pattern)=>data.AsSpan().IndexOf(pattern)>=0;
}
#endif
