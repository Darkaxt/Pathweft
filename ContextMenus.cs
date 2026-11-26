using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Clipboard = System.Windows.Forms.Clipboard;

namespace FileExplorer;

// Right-click: Pathweft' own menu, or the real Windows one (Settings → Files → Context menu).
public partial class MainWindow
{
#if DEBUG
    ContextMenu archiveMenuForVerification;
    bool captureS4Menu;
    internal static IEnumerable<MenuItem> MenuCommands(ItemsControl menu)
    {foreach(var item in menu.Items.OfType<MenuItem>()){yield return item;foreach(var child in MenuCommands(item))yield return child;}}
#endif
    internal static bool InColumnHeader(DependencyObject from, DependencyObject stop)
    {
        for (var d = from; d != null && d != stop; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is GridViewColumnHeader) return true;
        return false;
    }

    void HookContextMenu(PaneView p)
    {
        p.List.ContextMenuOpening += (_, e) =>
        {
            SetActive(p);
            // COLS-01: a column header opens its own menu (the columns), never the folder's
            if (InColumnHeader(e.OriginalSource as DependencyObject, p.List)) return;
            // right-click on empty space (in Details also right of the columns) = the folder itself, not
            // whatever is still selected
            var row = p.RowAt(e.OriginalSource as DependencyObject);
            bool keyboard = e.CursorLeft < 0;
            if (row == null && !keyboard) p.List.SelectedItems.Clear();
            // MENU-07: Ctrl+right-click opens the other menu (the standard Windows one, by default). With Ctrl held WPF
            // leaves the selection as it was, so the row under the pointer is selected here, as a plain right-click does.
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (ctrl && !keyboard && row is Entry entry && !p.List.SelectedItems.Contains(entry)) { p.List.SelectedItems.Clear(); p.List.SelectedItems.Add(entry); }
            e.Handled = true;
            ShowMenu(p, ctrl, keyboard);
        };
        // WPF opens a menu from the keyboard only for Shift+F10 and the Menu key without Ctrl
        p.List.PreviewKeyUp += (_, e) =>
        {
            if (!OtherMenuKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) return;
            e.Handled = true;
            SetActive(p);
            ShowMenu(p, ctrl: true, keyboard: true);
        };
        p.RightDragSelected += () => { SetActive(p); ShowMenu(p); };
    }

    /// Ctrl+Shift+F10 or Ctrl+Menu key: the keyboard's Ctrl+right-click.
    internal static bool OtherMenuKey(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt) &&
        (key == Key.F10 && modifiers.HasFlag(ModifierKeys.Shift) || key == Key.Apps);

    /// Where a menu opened from the keyboard goes: below the focused (else the selected) item, in screen pixels.
    static (int X, int Y)? MenuPoint(PaneView p, bool keyboard)
    {
        if (!keyboard) return null;
        var element = Keyboard.FocusedElement as ListBoxItem is { } focused && ItemsControl.ItemsControlFromItemContainer(focused) == p.List ? focused
            : p.List.SelectedItem != null ? p.List.ItemContainerGenerator.ContainerFromItem(p.List.SelectedItem) as FrameworkElement : null;
        element ??= p.List;
        if (PresentationSource.FromVisual(element) == null) return null;
        var point = element.PointToScreen(element == p.List ? new Point(24, 24) : new Point(Math.Min(48, element.ActualWidth / 2), element.ActualHeight));
        return ((int)point.X, (int)point.Y);
    }

    /// The menu for the pane's selection, or for its folder when nothing is selected. `ctrl`: the other menu than the
    /// right-click setting picks (MENU-07); `keyboard`: opened from the keyboard, so at the focused item.
    void ShowMenu(PaneView p, bool ctrl = false, bool keyboard = false)
    {
        if(closingPreviews)return;
        var at = MenuPoint(p, keyboard);
        // This PC: Windows' menu for the drive (Properties, Eject, Format…), or for This PC itself (Map network drive…)
        if (p.Dir == PaneView.ThisPC && !p.InSearch)
        {
            var drive = p.List.SelectedItem as Entry;
            // Explorer adds these two to This PC's menu from its own window, so the shell's menu has neither: Windows' dialogs
            void Net(bool map)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if ((map ? WNetConnectionDialog(hwnd, 1) : WNetDisconnectDialog(hwnd, 1)) == 0) { RefreshDrives(); p.Refresh(); } // RESOURCETYPE_DISK
            }
            (string, Action)[] net = { ("&Map network drive…", () => Net(true)), ("&Disconnect network drive…", () => Net(false)) };
            ShellMenu.Show(this, PaneView.ThisPC, drive != null ? new[] { drive.Path } : Array.Empty<string>(), rename: false, top: drive == null ? net : null, at: at);
            p.Refresh();
            return;
        }
        if (p.ArchiveRoot is var (archive, _))
        {
            var am = ArchiveMenu(p, archive);
            PrepareContextMenu(am,p.List);
#if DEBUG
            archiveMenuForVerification = am;
            if (captureS4Menu) return;
#endif
            am.IsOpen = true;
            return;
        }
        var sel = p.Selected;
        // Settings → Files → Context menu picks the right-click menu; Ctrl+right-click opens the other one (archives
        // and This PC above have only one)
        if ((Settings.NativeMenu ^ ctrl) && ShellMenu.CanShowItems(sel.Select(entry=>entry.Path).ToArray()))
        {
#if DEBUG
            NativeMenuForVerification?.Invoke(sel.Select(x => x.Path).ToArray(), at);
#endif
            // the folder's menu gets Pathweft' View submenu; Paste on a folder, or a shortcut to one, is Windows' own here
            if (ShellMenu.Show(this, p.Dir, sel.Select(x => x.Path).ToList(), sel.Count == 0 ? p.ViewChoices() : null, at: at) == "rename") Rename();
            p.Refresh();
            return;
        }
        var menu = p.IsRemoteFolder ? (sel.Count > 0 ? RemoteItemMenu(p, sel) : RemoteFolderMenu(p)) : sel.Count > 0 ? ItemMenu(p, sel) : FolderMenu(p);
        PrepareContextMenu(menu,p.List);
#if DEBUG
        archiveMenuForVerification = menu;
        if (captureS4Menu) return;
#endif
        menu.IsOpen = true;
    }
#if DEBUG
    internal Action<string[], (int X, int Y)?> NativeMenuForVerification;
    internal void S49ShowMenu(PaneView p, bool ctrl, bool keyboard) { SetActive(p); ShowMenu(p, ctrl, keyboard); }
#endif

    internal static MenuItem Item(string header, string glyph, string gesture, Action run, bool enabled = true)
    {
        var mi = new MenuItem
        {
            Header = header, InputGestureText = gesture, IsEnabled = enabled,
            Icon = new TextBlock { Text = glyph is { Length: > 0 } ? glyph : ArchiveGlyph(header) ?? "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13 },
        };
        string icon=header switch{
            "Open"=>"Open","Copy"=>"Copy","Cut"=>"Cut","Paste" or "Paste into folder"=>"Paste",
            "Rename" or "Rename all…"=>"Rename","Delete" or "Delete permanently"=>"Delete",
            "Quick View" or "Preview"=>"Preview","New folder"=>"NewFolder","Open terminal here"=>"Terminal",_=>null};
        if(icon!=null)mi.Icon=ExplorerIcons.Image(icon,14);
        System.Windows.Automation.AutomationProperties.SetName(mi,header);
        mi.Click += (_, _) => run();
        return mi;
    }

    ContextMenu ItemMenu(PaneView p, List<Entry> sel)
    {
        var first = sel[0];
        bool one = sel.Count == 1, dir = one && first.IsDir;
        var ext = Path.GetExtension(first.Name);
        var m = new ContextMenu();
        // Open with, 7-Zip, WinRAR, Google Drive, Tresorit and what other programs register: their own commands, as in Explorer (ShellMenu.LoadExtensions)
        var shell = ShellMenu.LoadExtensions(this, sel.Select(x => x.Path).ToList());
        m.Closed += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, shell.Dispose); // after a Click
        m.Items.Add(Item("Open", "", "Enter", () => p.OpenSelected()));
        if (dir)
        {
            m.Items.Add(Item("Open in new tab", "", "Middle click", () => { p.NewTab(); p.Navigate(first.Path); }));
            m.Items.Add(Item("Open in other pane", "", "", () => Other.Navigate(first.Path),!Settings.SinglePane));
            m.Items.Add(Item("Find duplicates in folder…", "", "", () => FindDuplicates(first.Path)));
        }
        else if (sel.Count(x => x.FolderTarget != null) > 1) // several folders: compared with each other
            m.Items.Add(Item("Find duplicates in these folders…", "", "", () => FindDuplicates(sel.Where(x => x.FolderTarget != null).Select(x => x.FolderTarget).ToArray())));
        // Windows' Open with list (its apps in Windows' order, Choose another app, the Store), for several files too
        if (!AddShell(m, shell, "openwith") && one && !dir)
            m.Items.Add(Item("Open with…", "", "", () => OpenWith(first.Path)));
        if (one && !dir)
        {
            if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && File.Exists(first.Path))
            {
                m.Items.Add(Item("Run with PowerShell", "", "", () => RunScript(first.Path, false)));
                m.Items.Add(Item("Run as administrator", "", "", () => RunScript(first.Path, true)));
            }
            else if (Runnable.Contains(ext)) m.Items.Add(Item("Run as administrator", "", "", () => RunAsAdmin(first.Path)));
        }
        if (p.InSearch && one)
            m.Items.Add(Item("Open file location", "", "", () => { p.Navigate(Path.GetDirectoryName(first.Path), first.Name); p.FocusList(); }));
        // CONTENT-05: a content hit in a text file opens in the editor at each matching line.
        if (p.InSearch && one && !dir && first.Matches is { Count: > 0 } matches && EditorWindow.IsText(first.Path))
            foreach (int line in Search.ContentExtractors.ExactLines(first.Path, matches.Select(Search.ContentExtractors.Line).OfType<int>(), first.MatchTerms).Take(5))
                m.Items.Add(Item($"Open at line {line}", "", "", () => EditorWindow.OpenAt(first.Path, line)));
        if (sel.Any(x => !x.IsDir)) m.Items.Add(Item("Edit", "", "F4", EditSelected));
        if (sel.Count == 2 && sel.All(x => !x.IsDir)) m.Items.Add(Item("Compare in editor", "", "", () => EditorWindow.Compare(sel[0].Path, sel[1].Path)));
        if (NotepadPlusPlus is { } npp && sel.Any(x => !x.IsDir))
            m.Items.Add(Item("Edit with Notepad++", "", "", () =>
                Process.Start(npp, string.Join(" ", sel.Where(x => !x.IsDir).Select(x => $"\"{x.Path}\"")))));
        m.Items.Add(Item("Quick View", "", "Space", QuickView,one));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Cut", "", "Ctrl+X", () => ClipboardPut(cut: true)));
        m.Items.Add(Item("Copy", "", "Ctrl+C", () => ClipboardPut(cut: false)));
        // on a folder, or a shortcut to one, Paste goes into it (Explorer's rule); on a file, into the folder shown
        var into = one ? first.FolderTarget : null;
        m.Items.Add(into != null ? Item("Paste into folder", "", "", () => ClipboardPaste(into), HasTransferClipboard())
                                 : Item("Paste", "", "Ctrl+V", () => ClipboardPaste(), HasTransferClipboard()));
        m.Items.Add(Item("Copy to other pane", "", "Ctrl+F5", () => Transfer(false),!Settings.SinglePane));
        m.Items.Add(Item("Move to other pane", "", "F6", () => Transfer(true),!Settings.SinglePane));
        m.Items.Add(SendTo("Copy to", "\uE8C8", p, sel, move: false));
        m.Items.Add(SendTo("Move to", "\uE8DE", p, sel, move: true));
        m.Items.Add(Item("Group into new folder…", "\uE8F4", "", () => GroupIntoFolder(p, sel)));
        m.Items.Add(Item("Add to Drop bin", "\uE7B8", "Ctrl+B", () => AddToBin(sel.Select(x => x.Path))));
        m.Items.Add(AddToCollectionMenu(sel)); // OPUS-04
        m.Items.Add(new Separator());
        m.Items.Add(Item("Copy path", "", "Ctrl+Shift+C", () => PutText(Entry.QuotedPaths(sel), "Path copied")));
        m.Items.Add(Item("Copy name", "", "", () => PutText(string.Join(Environment.NewLine, sel.Select(x => x.Name)), "Name copied")));
        foreach (var detail in CopyDetailItems(p, sel)) m.Items.Add(detail);
        var table = Item("Copy as table", "\uE8C8", "", () => CopyAsTable(p)); table.Tag = "copy-detail"; m.Items.Add(table); // OPUS-06
        m.Items.Add(one ? Item("Rename", "", "F2", Rename) : Item("Rename all…", "", "F2", RenameMany));
        if (p.ShownCollection != null) m.Items.Add(Item("Remove from collection", "\uE738", "", () => RemoveFromCollection(p, sel))); // OPUS-04: the files stay
        m.Items.Add(Item("Delete", "", "Del", () => Delete()));
        m.Items.Add(Item("Delete permanently", "", "Shift+Del", () => Delete(permanent: true)));
        m.Items.Add(new Separator());
        AddArchiveOpeners(m, p, sel);
        m.Items.Add(Item("Compress to ZIP", "", "", Compress));
        m.Items.Add(Item("Compress to 7z", "", "", () => CompressArchive(".7z")));
        string rarUnavailable = RarNative.AvailabilityReason();
        var rarCreate = Item("Compress to RAR", "", "", () => CompressArchive(".rar"), rarUnavailable.Length == 0);
        if (rarUnavailable.Length > 0) { rarCreate.ToolTip = rarUnavailable; ToolTipService.SetShowOnDisabled(rarCreate, true); }
        m.Items.Add(rarCreate);
        m.Items.Add(Item("Compress…", "", "", CompressWithOptions));
        if (one && DiskImageExt.Contains(ext)) m.Items.Add(Item("Mount", "", "", () => MountImage(first.Path)));
        AddShell(m, shell, "7-zip");
        AddShell(m, shell, "winrar");
        AddArchiveSenders(m, sel);
        if (CanResizeSelection(sel)) m.Items.Add(Item("Resize images…", "\uE799", "", ResizeImages));
        m.Items.Add(Item("What's using this?", "\uE7EF", "", () => WhatsUsing(sel.Select(x => x.Path).ToList())));
        m.Items.Add(Item("Create shortcut", "", "", CreateShortcuts));
        bool local=sel.All(x=>!PaneView.IsVirtual(x.Path)&&!RemoteAddress.IsRemote(x.Path));
        if (one && local && !p.InSearch && first.Path is var linkTarget) m.Items.Add(Item("Create link…", "\uE71B", "", () => NewLink(linkTarget)));
        m.Items.Add(Item("Tags and notes…", "\uE8EC", "", () => NotesDialog.Edit(this, sel.Select(x => x.Path).ToList()), local));
        if (local && sel.Any(x => !x.IsDir && EmbeddedMetadata.FamilyOf(x.Path) != null)) m.Items.Add(Item("Edit metadata…", "\uE70F", "", () => EditMetadata(sel.Select(x => x.Path).ToList()))); // OPUS-09
        if (local && CustomActions.Menu(sel, p.InSearch ? Path.GetDirectoryName(first.Path) : p.Dir, error => MessageBox.Show(this, error, "Actions")) is { } actions) m.Items.Add(actions);
        m.Items.Add(new Separator());
        if (sel.Any(x => x.IsDir)) m.Items.Add(Item("Pin to Quick access", "", "Ctrl+D", PinSelected));
        m.Items.Add(Item("Show in File Explorer", "", "", () => OpenInExplorer($"/select,\"{first.Path}\"")));
        AddShell(m, shell, "drive");
        AddShell(m, shell, "tresorit");
        var paths=sel.Select(entry=>entry.Path).ToArray();
        var properties=Item("Properties", "", "Alt+Enter", () => Properties(paths),SupportsProperties(paths));
        if(!properties.IsEnabled){properties.ToolTip="Select items on the same drive or share for combined Properties.";ToolTipService.SetShowOnDisabled(properties,true);}m.Items.Add(properties);
        AddOthers(m, shell);
        GroupItemMenu(m);
        return m;
    }

    // Reparent the existing command nodes, preserving their exact callbacks,
    // predicates, native-extension children and disposal lifetime.
    static void GroupItemMenu(ContextMenu menu)
    {
        var remaining=menu.Items.OfType<MenuItem>().ToList();menu.Items.Clear();
        var properties=remaining.Single(item=>item.Header as string=="Properties");remaining.Remove(properties);
        void Take(ItemsControl target,Func<MenuItem,bool> predicate){foreach(var item in remaining.Where(predicate).ToArray()){remaining.Remove(item);target.Items.Add(item);}}
        void Group(string name,string glyph,Func<MenuItem,bool> predicate){var group=Item(name,glyph,"",()=>{});Take(group,predicate);if(group.Items.Count>0)menu.Items.Add(group);}
        static bool Named(MenuItem item,params string[] labels)=>labels.Contains(item.Header as string);
        Take(menu,item=>item.Tag==null&&(Named(item,"Open","Open in new tab","Open in other pane","Open file location")||(item.Header as string)?.StartsWith("Open at line ")==true||(item.Header as string)?.StartsWith("Open in ")==true&&item.Header as string!="Open in File Explorer"));
        OpenWithGroup(menu,remaining);
        Take(menu,item=>Named(item,"Run with PowerShell","Edit","Quick View"));
        menu.Items.Add(new Separator());
        Take(menu,item=>Named(item,"Cut","Copy","Paste","Paste into folder","Rename","Rename all…","Remove from collection","Delete","Delete permanently"));
        menu.Items.Add(new Separator());
        Group("Transfer","\uE8DE",item=>Named(item,"Copy to other pane","Move to other pane","Copy to","Move to","Group into new folder…","Add to Drop bin","Add to collection")||item.Tag as string=="shell:transfer");
        Group("Copy details","\uE8C8",item=>Named(item,"Copy path","Copy name")||item.Tag as string=="copy-detail");
        Group("Archive","\uF012",item=>item.Tag==null&&IsArchiveCommand(item.Header as string)||item.Tag as string is "shell:7-zip" or "shell:winrar");
        Group("Tools","\uE713",item=>Named(item,"Run as administrator","Compare in editor","Edit with Notepad++","Resize images…","What's using this?","Create shortcut","Mount","Tags and notes…","Edit metadata…","Create link…")||(item.Header as string)?.StartsWith("Find duplicates")==true);
        Take(menu,item=>item.Tag as string=="custom-actions");
        Group("More actions","\uE712",_=>true);
        menu.Items.Add(new Separator());menu.Items.Add(properties);
    }

    /// MENU-06: what other programs and Windows registered for the selection, in Pathweft's groups: the ones that open
    /// it in a program (Open with Code, Git Bash here, Play with VLC) beside Open, or in Tools on a folder's background,
    /// the rest in More actions. One that repeats a command already in the menu, or another taken-in one, is left out.
    static void AddOthers(ContextMenu menu, ShellMenu.Extensions shell)
    {
        var taken = new HashSet<string>();
        void Collect(ItemsControl parent) { foreach (var item in parent.Items.OfType<MenuItem>()) { if (item.Header is string h) taken.Add(ShellMenu.Comparable(h)); Collect(item); } }
        Collect(menu);
        foreach (var node in shell.Others)
        {
            var key = ShellMenu.Comparable(node.Text);
            if (key.Length == 0 || IsNanaZip(node) || PathweftHasPowerToy(key) || !taken.Add(key)) continue;
            if (ShellItem(node) is not MenuItem item) continue;
            item.Tag = OpensInApp(key) ? "shell:apps" : SendsElsewhere(key) ? "shell:transfer" : "shell:other";
            menu.Items.Add(item);
        }
    }

    /// TOOLS-01..03: PowerToys' own entries for the tools Pathweft has built in (Rename all…, What's using this?, Resize
    /// images…) are left out, as NanaZip's are, so each tool is offered once, under Pathweft's name.
    internal static bool PathweftHasPowerToy(string comparable) =>
        comparable.Contains("powerrename") || comparable.Contains("file locksmith") || comparable.Contains("image resizer");

    /// MENU-06 (owner 2026-11-23): a program's own "Send with …" (Tailscale's Taildrop) goes to Transfer, beside Copy to / Move to.
    internal static bool SendsElsewhere(string comparable) => comparable.StartsWith("send with ");

    internal static bool OpensInApp(string comparable) =>
        comparable.StartsWith("open") || comparable.StartsWith("play") || comparable.StartsWith("edit with") || comparable.EndsWith(" here");

    /// Adds what a shell extension offered for the selection (ShellMenu.Extensions); false when it offered nothing.
    static bool AddShell(ItemsControl menu, ShellMenu.Extensions shell, string name)
    {
        if (!shell.Found.TryGetValue(name, out var nodes)) return false;
        foreach (var n in nodes){if(name!="openwith"&&n.Text!=null&&IsNanaZip(n))continue;var item=ShellItem(n);if(item is MenuItem command)command.Tag="shell:"+name;menu.Items.Add(item);}
        return true;
    }

    static object ShellItem(ShellMenu.Node n)
    {
        if (n.Text == null) return new Separator();
        var mi = new MenuItem { Header = n.Text, InputGestureText = n.Gesture ?? "" };
        if (n.Icon != null) mi.Icon = new Image { Source = n.Icon, Width = 16, Height = 16 };
        if (n.Children != null) foreach (var c in n.Children) mi.Items.Add(ShellItem(c));
        else mi.Click += (_, _) => n.Run();
        return mi;
    }

    ContextMenu FolderMenu(PaneView p)
    {
        var m = new ContextMenu();
        var shell = ShellMenu.LoadExtensions(this, Array.Empty<string>(), p.InSearch ? null : p.Dir);
        m.Closed += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, shell.Dispose); // after a Click
        if (LatestUndo(p.Dir) is { } latest) m.Items.Add(Item($"Undo {latest.What}", "", "Ctrl+Z", () => Undo(latest)));
        m.Items.Add(Item("Paste", "", "Ctrl+V", () => ClipboardPaste(), HasTransferClipboard()));
        bool writable = !p.InSearch && !PaneView.IsVirtual(p.Dir);
        var pasteAs = Item("Paste as file", "\uE77F", "", () => { }, writable && CanPasteAsFile());
        if (pasteAs.IsEnabled) foreach (var item in PasteAsFileMenuItems()) pasteAs.Items.Add(item);
        m.Items.Add(pasteAs);
        m.Items.Add(new Separator());
        m.Items.Add(Item("New folder", "", "Ctrl+Shift+N", MkDir));
        m.Items.Add(Item("New file…", "", "Ctrl+N", NewFile));
        var fromTemplate = Item("New from template", "\uE8F4", "", () => { }, writable);
        if (writable) foreach (var item in TemplateMenuItems()) fromTemplate.Items.Add(item);
        m.Items.Add(fromTemplate);
        m.Items.Add(Item("Link…", "\uE71B", "", () => NewLink(), writable && !RemoteAddress.IsRemote(p.Dir)));
        m.Items.Add(new Separator());
        // View, as in Explorer's own menu for a folder (the Windows menu gets the same from ShellMenu.Show)
        var view = new MenuItem { Header = $"View  ·  {p.ViewModeName}", Icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13 } };
        foreach (var item in p.ViewMenuItems()) view.Items.Add(item);
        m.Items.Add(view);
        var sort = new MenuItem
        {
            Header = $"Sort by  ·  {p.SortKey}",
            Icon = new TextBlock { Text = p.SortDescending ? "" : "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13 },
        };
        foreach (var item in p.SortMenuItems()) sort.Items.Add(item);
        m.Items.Add(sort);
        if (p.InSearch) m.Items.Add(Item("Save this search…", "\uE721", "", () => SaveCurrentSearch(p)));
        m.Items.Add(Item("Select by pattern…", "", "Num +", () => SelectByPattern(true)));
        m.Items.Add(Item("Invert selection", "", "Num *", p.InvertSelection));
        m.Items.Add(Item("Compare with other pane", "", "Shift+F2", ComparePanes));
        m.Items.Add(Item("Synchronize with other pane…", "", "", SynchronizePanes, !Settings.SinglePane && !p.InSearch));
        m.Items.Add(Item("Find duplicates…", "", "", () => FindDuplicates(p.Dir)));
        // the other pane's folder too, when both are on screen and show different places
        var other = p == Left ? Right : Left;
        if (!Settings.SinglePane && other.Dir is { Length: > 0 } od && od != PaneView.ThisPC && !od.Equals(p.Dir, StringComparison.OrdinalIgnoreCase))
            m.Items.Add(Item("Find duplicates here and in the other pane…", "", "", () => FindDuplicates(p.Dir, od)));
        // TOOLS-01..03 on the folder itself, as on a selection: rename or resize what it holds, see what's using it
        bool local = writable && !p.IsRemoteFolder && p.ArchiveRoot == null;
        m.Items.Add(Item("Rename all…", "", "", () => RenameAllIn(p), local && p.Items.Any()));
        var pictures = local ? p.Items.Where(ResizableImage).ToList() : new List<Entry>();
        if (pictures.Count > 0) m.Items.Add(Item("Resize images…", "", "", () => ResizeImages(pictures)));
        if (local) m.Items.Add(Item("What's using this?", "", "", () => WhatsUsing(new[] { p.Dir })));
        // OPUS-02: linked relative navigation between the panes
        if (PanesLinked)
        {
            m.Items.Add(Item("Unlink panes", "\uE71B", "", () => UnlinkPanes()));
            m.Items.Add(Item("Re-link at current folders", "\uE71B", "", LinkPanes, !Settings.SinglePane));
        }
        else m.Items.Add(Item("Link panes", "\uE71B", "", LinkPanes, !Settings.SinglePane && !p.InSearch && other.Dir != PaneView.ThisPC && !PaneView.IsVirtual(other.Dir) && writable));
        // OPUS-06: the listing as shown, as a table
        m.Items.Add(Item("Copy as table", "\uE8C8", "", () => CopyAsTable(p), p.Items.Any()));
        m.Items.Add(Item("Export listing…", "\uEDE1", "", () => ExportListing(p), p.Items.Any()));
        // OPUS-07: aliases
        if (writable && !RemoteAddress.IsRemote(p.Dir)) m.Items.Add(Item("Add alias for this folder…", "\uE8EC", "", () => AddAlias(p.Dir)));
        m.Items.Add(Item("Path aliases…", "\uE8EC", "", ManageAliases));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Refresh", "", "F5 / Ctrl+R", () => { SizeCache.Clear(); p.Refresh(force: true); }));
        m.Items.Add(Item(Settings.ShowHidden ? "Hide hidden files" : "Show hidden files", "", "Ctrl+H", ToggleHidden));
        m.Items.Add(Item("Pin to Quick access", "", "Ctrl+D", PinSelected));
        m.Items.Add(Item("Open terminal here", "", "Ctrl+`", OpenTerminal));
        m.Items.Add(Item("Open in File Explorer", "", "", () => OpenInExplorer($"\"{p.Dir}\"")));
        AddShell(m, shell, "drive");
        AddShell(m, shell, "tresorit");
        m.Items.Add(new Separator());
        m.Items.Add(Item("Properties", "", "", () => Properties(p.Dir)));
        AddOthers(m, shell);
        GroupFolderMenu(m);
        return m;
    }

    static void GroupFolderMenu(ContextMenu menu)
    {
        var remaining=menu.Items.OfType<MenuItem>().ToList();menu.Items.Clear();
        void Take(ItemsControl target,Func<MenuItem,bool> predicate){foreach(var item in remaining.Where(predicate).ToArray()){remaining.Remove(item);target.Items.Add(item);}}
        void Group(string name,string glyph,Func<MenuItem,bool> predicate){var group=Item(name,glyph,"",()=>{});Take(group,predicate);if(group.Items.Count>0)menu.Items.Add(group);}
        Take(menu,item=>item.Header as string is "Paste" or "Paste as file"||(item.Header as string)?.StartsWith("Undo ")==true);
        Group("New","\uE710",item=>item.Header as string is "New folder" or "New file…" or "New from template" or "Link…");
        menu.Items.Add(new Separator());
        Take(menu,item=>(item.Header as string)?.StartsWith("View  ·")==true||(item.Header as string)?.StartsWith("Sort by  ·")==true||item.Header as string=="Save this search…");
        Group("Select","\uE762",item=>item.Header as string is "Select by pattern…" or "Invert selection");
        Take(menu,item=>item.Header as string is "Refresh" or "Show hidden files" or "Hide hidden files");
        menu.Items.Add(new Separator());
        OpenWithGroup(menu,remaining);
        Group("Tools","\uE713",item=>item.Header as string is "Compare with other pane" or "Synchronize with other pane…" or "Link panes" or "Unlink panes" or "Re-link at current folders" or "Rename all…" or "Resize images…" or "What's using this?" or "Open terminal here" or "Add alias for this folder…" or "Path aliases…"||(item.Header as string)?.StartsWith("Find duplicates")==true);
        Group("Copy listing","\uE8C8",item=>item.Header as string is "Copy as table" or "Export listing…");
        Group("More actions","\uE712",item=>item.Header as string!="Properties");
        menu.Items.Add(new Separator());Take(menu,_=>true);
    }

    /// MENU-10: one "Open with" submenu: Windows' Open with list (or Pathweft's Open with… dialog), then every taken-in
    /// program that opens, plays or edits the selection (VS Code, Git Bash here, VLC). A lone entry stays unwrapped.
    static void OpenWithGroup(ContextMenu menu,List<MenuItem> remaining)
    {
        var group=Item("Open with","\uE7AC","",()=>{});
        foreach(var item in remaining.Where(item=>item.Tag as string=="shell:openwith"||item.Header as string=="Open with…").ToArray())
        {
            remaining.Remove(item);
            if(item.Items.Count==0){group.Items.Add(item);continue;}
            foreach(var child in item.Items.Cast<object>().ToArray()){item.Items.Remove(child);group.Items.Add(child);}
        }
        var apps=remaining.Where(item=>item.Tag as string=="shell:apps").ToArray();
        if(apps.Length>0&&group.Items.Count>0)group.Items.Add(new Separator());
        foreach(var app in apps){remaining.Remove(app);group.Items.Add(app);}
        if(group.Items.Count==1&&group.Items[0] is MenuItem only){group.Items.Clear();menu.Items.Add(only);}
        else if(group.Items.OfType<MenuItem>().Any())menu.Items.Add(group);
    }

    static void OpenWith(string path)
    {
        var start=new ProcessStartInfo("rundll32.exe");
        start.ArgumentList.Add("shell32.dll,OpenAs_RunDLL");start.ArgumentList.Add(path);Process.Start(start);
    }

    internal static void PrepareContextMenu(ContextMenu menu,FrameworkElement target)
    {
        menu.PlacementTarget=target;
        double scale=Math.Max(1,Settings.TextScale);
        menu.LayoutTransform=scale>1?new ScaleTransform(scale,scale):null;
        menu.Opened+=(_,_)=>
        {
            var source=PresentationSource.FromVisual(target) as System.Windows.Interop.HwndSource;
            var screen=System.Windows.Forms.Screen.FromHandle(source?.Handle??IntPtr.Zero);
            var dpi=VisualTreeHelper.GetDpi(target);
            menu.Resources["MenuPopupMaxHeight"]=Math.Max(100,screen.WorkingArea.Height/dpi.DpiScaleY/scale-24);
            menu.Resources["MenuPopupMaxWidth"]=Math.Max(220,screen.WorkingArea.Width/dpi.DpiScaleX/scale-24);
#if DEBUG
            HiddenTrialPresentation.PreparePopup(menu);
#endif
        };
    }

    /// Copy to / Move to: the folders copies and moves went to lately, then any folder. The folder shown is left
    /// out (copying there duplicates, moving there does nothing).
    MenuItem SendTo(string header, string glyph, PaneView p, List<Entry> sel, bool move)
    {
        var sub = Item(header, glyph, "", () => { });
        foreach (var dest in Settings.RecentDestinations.Where(d => !d.Equals(p.Dir, StringComparison.OrdinalIgnoreCase)
                                                                   && (PaneView.IsRemote(d) || Directory.Exists(d))))
        {
            var target = dest;
            var mi = Item(Path.GetFileName(dest.TrimEnd('\\')) is { Length: > 0 } n ? n : dest, "", "", () => SubmitShaped(p, sel, target, move));
            mi.InputGestureText = ParentForMenu(dest); // where it is, muted at the right
            mi.ToolTip = dest;
            mi.Icon = ShellIcon(dest, "\uE8B7", 16, new Thickness(0));
            sub.Items.Add(mi);
        }
        if (sub.Items.Count > 0) sub.Items.Add(new Separator());
        sub.Items.Add(Item("Choose a folder…", "\uE838", "", () =>
        {
            var pick = new Microsoft.Win32.OpenFolderDialog { Title = $"{header} which folder?", InitialDirectory = Other.Dir };
            if (pick.ShowDialog(this) == true) SubmitShaped(p, sel, pick.FolderName, move);
        }));
        return sub;
    }

    /// Makes a folder beside the selection and moves the selection into it (one Ferry move, undoable with Ctrl+Z);
    /// the new folder is selected afterwards. An existing name is refused rather than merged into.
    void GroupIntoFolder(PaneView p, List<Entry> sel)
    {
        var name = PromptDialog.Ask(this, "Group into new folder", $"Move the {(sel.Count == 1 ? "selected item" : $"{sel.Count} selected items")} into a new folder named", "New folder", "Group");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(this, $"A folder name can't contain {string.Join(" ", Path.GetInvalidFileNameChars().Where(c => c >= ' '))}", "Group into new folder"); return; }
        var folder = Path.Combine(p.Dir, name);
        if (Directory.Exists(folder) || File.Exists(folder)) { MessageBox.Show(this, $"“{name}” already exists here.", "Group into new folder"); return; }
        if (!Run(() => Directory.CreateDirectory(folder))) return;
        Submit(sel.Select(x => x.Path).ToList(), folder, move: true);
        p.Navigate(p.Dir, name, record: false);
    }

    // Notepad++ path from its installer's registry entries (App Paths, then its own key), or null if not installed.
    static readonly Lazy<string> nppPath = new(() =>
    {
        string Reg(string key, string value = "") => Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key)?.GetValue(value) as string;
        var candidates = new[]
        {
            Reg(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe"),
            Reg(@"SOFTWARE\Notepad++") is { } dir ? Path.Combine(dir, "notepad++.exe") : null,
            Reg(@"SOFTWARE\WOW6432Node\Notepad++") is { } dir32 ? Path.Combine(dir32, "notepad++.exe") : null,
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    });
    static string NotepadPlusPlus => nppPath.Value;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHObjectProperties(IntPtr hwnd, int type, string name, string page);
    [DllImport("mpr.dll")] static extern int WNetConnectionDialog(IntPtr hwnd, int type);
    [DllImport("mpr.dll")] static extern int WNetDisconnectDialog(IntPtr hwnd, int type);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHCreateDataObject(IntPtr folder, uint count, IntPtr[] pidls, IntPtr inner, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object dataObject);
    [DllImport("shell32.dll")] static extern int SHMultiFileProperties([MarshalAs(UnmanagedType.Interface)] object dataObject, int flags);

    void Properties(string path) => Properties(new[] { path });
    static string LinkDescription(string path)
    {
        try { return Links.Describe(path, File.GetAttributes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    [DllImport("shell32.dll")] static extern IntPtr ILFindChild(IntPtr parent, IntPtr child);

    // Several items get Explorer's combined dialog (total size, counts). The shell wants them as IDs
    // relative to one folder (relative to the desktop it answers "properties not available"), so
    // that folder is the deepest one they share — search results from several subfolders work too.
    internal bool Properties(IReadOnlyList<string> paths,Window owner=null)
    {
        if(!SupportsProperties(paths))return false;
        owner??=this;
#if DEBUG
        if(PropertiesForVerification is {} dispatch){dispatch(owner,paths);return true;}
#endif
        if (paths.Count == 1 && LinkDescription(paths[0]) is { Length: > 0 } link)
        {
            string target = Links.Target(paths[0]);
            switch (LinkPropertiesDialog.Show(owner, paths[0], link, target))
            {
                case 0: active.Navigate(Path.GetDirectoryName(target.TrimEnd('\\')) ?? target, select: target); active.FocusList(); return true;
                case 1: break;
                default: return true;
            }
        }
        if (paths.Count == 1) return SHObjectProperties(new System.Windows.Interop.WindowInteropHelper(owner).Handle, 2 /* SHOP_FILEPATH */, paths[0], null);
        var common = paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\')) ?? p).Aggregate(SharedFolder);
        if (common == null) { Status.Text = "Properties of items on different drives can't be shown together"; return false; }
        var pidls = new List<IntPtr>();
        try
        {
            if (SHParseDisplayName(common, IntPtr.Zero, out var folder, 0, out _) != 0) return false;
            pidls.Add(folder);
            var children = new List<IntPtr>();
            foreach (var p in paths)
                if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) == 0) { pidls.Add(pidl); children.Add(ILFindChild(folder, pidl)); }
            var iid = new Guid("0000010e-0000-0000-C000-000000000046"); // IDataObject
            if (children.Count > 0 && SHCreateDataObject(folder, (uint)children.Count, children.ToArray(), IntPtr.Zero, ref iid, out var data) == 0)
            {
                try{return SHMultiFileProperties(data, 0)>=0;}
                finally{Marshal.ReleaseComObject(data);}
            }
            return false;
        }
        finally { foreach (var p in pidls) Marshal.FreeCoTaskMem(p); }
    }
    internal static bool SupportsProperties(IReadOnlyList<string> paths)=>paths.Count==1||paths.Count>1&&
        paths.Select(Path.GetPathRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1;
#if DEBUG
    internal Action<Window,IReadOnlyList<string>> PropertiesForVerification;
#endif

    /// The deepest folder containing both, or null when they're on different drives.
    static string SharedFolder(string a, string b)
    {
        if (a == null || b == null) return null;
        for (var d = a; d != null; d = Path.GetDirectoryName(d))
            if (b.Equals(d, StringComparison.OrdinalIgnoreCase) || b.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }
}
