using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileExplorer;

// F1: everything Pathweft does, by topic, searchable — keys, and the features that have no key. The text is
// kept here and nowhere else, so a new shortcut in MainWindow.OnPreviewKeyDown or a new feature needs a line here too.
public partial class HelpWindow : Window
{
    readonly List<(TextBlock Heading,Button Topic)> sectionTopics=new();
    static readonly (string Section, (string Key, string What)[] Rows)[] Help =
    {
        ("The window", new[]
        {
            ("Two panes", "Two folders side by side, each with its own tabs. The active pane has the lighter surface and the accent edge; Tab switches. Ctrl+P shows one pane or two"),
            ("Top row", "The tabs. Drag its empty part (or Pathweft' name at the top of the sidebar) to move the window, double-click it to maximize, right-click it for the window menu. Resting on the maximize button shows Windows' snap layouts"),
            ("Sidebar", "Quick access (the folders you pin), Favorites (Home, Desktop, Documents, Downloads, Pictures, Music, Videos, Recycle Bin), Drives with how full each one is, and a Folders tree. Click a section's header to fold it, drag a header to reorder; drag the sidebar's edge to widen it"),
            ("Sidebar controls", "Check for updates, What's new, Help and Settings sit beside Pathweft's name, or just below it in a narrow sidebar. The Drop bin stays at the bottom. Hover a button for its name"),
            ("Summary cards", "Under the panes: the drive (free space, how full), this folder (its size, folders and files, and a bar of what it holds by kind: images, video, code…), and the selection — or, with nothing selected, the latest change in the folder"),
            ("Performance", "The waveform button swaps the bottom cards to CPU, RAM, GPU, drive and folder I/O. A card opens System analysis in the main pane. Collection and bounded history saving are automatic, including in the tray. App history shows CPU/RAM/GPU charts; Boot diagnostics, System changes and Insights use local observations. Export CSV, JSON or HTML for the selected query"),
            ("Record app activity", "Settings → Startup → Record app activity keeps, from sign-in to sign-out, which programs ran with their CPU, memory and disk use, their command line (passwords, tokens and keys masked) and the folder they started in, for 7 days by default. Agents read it through Pathweft's MCP run history and workloads. It runs as an elevated sign-in task in the Program Files installation; runs recorded before 1.1.88 show the program path only"),
            ("Agents (MCP)", "AI agents read Pathweft through its read-only MCP, reached through LLM Coordinator's Pathweft gate: performance history, app activity, Maintenance rows, file details, folder sizes and index search. They can also see the index's own status (each drive or folder ready, scanning, loading or offline) and which folders are content-indexed, so an empty search is not mistaken for a missing file. Nothing is changed through it; content-indexed folders are chosen in Settings"),
            ("Maintenance", "The brush swaps the bottom row to five summary tabs: Cleanup, Startup, Services, Windows tweaks and Windows apps. Click a card to review that section in the main pane. Counts cover the listed items; Windows apps means optional preinstalled apps. Cleanup estimates appear after Analyze. Back to files or the brush restores the browser"),
            ("Maintenance actions", "Review the selected item, then confirm its action. Cleanup removes only reviewed unchanged files; new or busy files are skipped. Startup/service/tweak changes save original settings before applying; Restore preserves newer independent changes. Windows may request administrator permission. Removing a Windows app affects this user; reinstall it through Microsoft Store"),
            ("Bottom bar", "The everyday commands with their keys, and the status line at its right"),
            ("History button", "Left of minimize: the recently closed tabs and the last 25 folders you visited. Click one to go back to it. Operation history… at its end lists what you did to files"),
            ("Operation history", "History button → Operation history…: up to 500 copies, moves, renames, deletes and other operations from the last 90 days, each with how it really ended (done, partly done, failed or cancelled), where it went from and to, and Open folder. Undo there takes back any of them, from any folder, while the Undo history still holds it. Settings → Privacy clears the records; files are never touched"),
        }),
        ("Workspaces and collections", new[]
        {
            ("Workspaces", "Sets of tabs for a project. In the sidebar's Workspaces section, the save button keeps the open tabs of both panes, which pane is active and linked roots under a name you choose. Click a workspace to replace the unpinned tabs with it (pinned tabs stay); middle-click it to add its tabs to the open ones. Right-click it to update it with the open tabs, rename or remove it. A saved folder that no longer exists opens at the nearest folder that does, and the pane says so"),
            ("Link panes", "Right-click a folder's background → Tools → Link panes: both panes' current folders become roots, and going into a folder below one takes the other pane to the same folder below the other root. Handy for comparing a project with its backup or two versions of a tree. A folder missing on the other side is reported, nothing is created. Leaving a root pauses the link until you come back; Re-link at current folders moves both roots; Unlink panes ends it"),
            ("Collections", "Named lists of files and folders from anywhere, for work that cuts across folders; the files stay where they are. Right-click items → Add to collection → a collection or New collection…, or press Save as collection in the Drop bin. The sidebar's Collections section opens one as a listing; right-click it to show it in the other pane, rename it, forget missing items or remove it. Remove from collection, in a collection's item menu, only takes the item off the list"),
            ("Path aliases", "Short names for folders you go to often. Right-click a folder's background → Tools → Add alias for this folder…, then type @name (or @name\\subfolder) in the address bar; suggestions appear as you type @. Path aliases… in the same menu lists, renames and removes them. A real file or folder of that name wins, and an alias whose folder is gone says so"),
        }),
        ("Panes and tabs", new[]
        {
            ("Tab", "Switch to the other pane"),
            ("Ctrl+P", "One pane or two (also in Settings and the bottom bar)"),
            ("Ctrl+U", "Swap the two panes"),
            ("Ctrl+T", "New tab, at This PC"),
            ("Ctrl+W", "Close the tab (pinned tabs stay)"),
            ("Ctrl+Shift+T", "Reopen the tab you closed last; the history button has the rest"),
            ("Ctrl+Tab / Ctrl+Shift+Tab", "Next / previous tab"),
            ("Middle-click a folder", "Open it in a new tab behind this one (a shortcut to a folder too): in the list, the sidebar, the folder tree, the path, and on Back, Forward and Up. On the empty part of the path: the folder above"),
            ("Middle-click a tab", "Close it"),
            ("Right-click a tab", "Pin or unpin it, or close the other tabs. Pinned tabs sit at the left, have no close button, are remembered and stay open"),
            ("Drag a tab", "Reorder, or drop it on the other pane to move it there"),
            ("Many tabs", "Below 120 px each they wrap into more rows, which stay where they are when you switch tabs"),
            ("A folder opened from outside", "From Explorer, another app, a shortcut or Win+E: if a tab already shows it, Pathweft switches to that tab instead of opening a second one"),
            ("Double-click the splitter", "Make the two panes equal again"),
            ("Settings → Close the second pane with its last tab", "Closing the right pane's last tab (its ×, Ctrl+W or a middle click) hides the pane; Ctrl+P brings it back with that tab"),
            ("Tabs are remembered", "Every pane's tabs come back the next time Pathweft starts"),
        }),
        ("Moving around", new[]
        {
            ("Enter / F3", "Open"),
            ("Backspace / Alt+Up", "Up one folder; the folder you left stays selected"),
            ("Alt+Left / Alt+Right", "Back / Forward (also the mouse side buttons)"),
            ("Ctrl+L", "Type a path; matching folders are suggested, Tab completes, Down picks one. %USERPROFILE% and other variables work"),
            ("Chevron after a path segment", "The folders inside it, to jump sideways"),
            ("Ctrl+F / Ctrl+L", "Use the address/search field. Enter resolves an existing path first; otherwise it searches the Pathweft index in this folder. Search from This PC for all indexed files."),
            ("Esc", "Leave search results"),
            ("Ctrl+D", "Pin the folder (or the selected folders) to Quick access; drag a pinned folder to reorder, right-click it to move or remove it"),
            ("Quick access lists", "Several lists with names you choose: the ··· on Quick access' header, or a right-click on the header, switches between them and adds, renames, reorders or deletes them. Ctrl+D pins to the list shown"),
            ("Pin by dragging", "Drag a folder, a shortcut to one, or a tab onto Quick access: it is pinned where the line shows. Nothing is copied or moved"),
            ("Portable copy", "The portable zip runs from any folder or stick: its settings stay in Data beside it and nothing is installed. Its updates open the release page; unpack the new zip over the folder"),
            ("Middle-click empty space", "With Settings → Middle-click autoscroll on: a round marker where you clicked, and the list scrolls toward the pointer, faster the further it is. A left or middle click stops it (or letting go, if you held the button). Settings can keep it to up and down"),
            ("\\\\server", "Type a server's name to see its shared folders; going up from a share comes back to that list"),
        }),
        ("Files", new[]
        {
            ("Ctrl+C / Ctrl+X / Ctrl+V", "Copy, cut, paste, also with Explorer. Right-click a folder, or a shortcut to one, → Paste into folder puts the files in it"),
            ("F2", "Rename in place; with several selected, rename them all: a pattern with [N] name, [C] counter and [E] extension, find and replace (PowerRename's options: regular expressions, match case, every occurrence, ${} enumeration, name or extension only, a case change, files or folders, and date tokens from the created, modified or accessed time), and a preview of every new name; it remembers the options. Right-click a folder's background → Tools → Rename all… renames everything in it"),
            ("Click a selected name", "Rename it, as in Explorer: a slow second click, not a double-click"),
            ("Drag files", "Same drive: move. Other drive: copy. Ctrl copies, Shift moves — to the other pane, Explorer or another app. The folder they would go into lights up; a shortcut to a folder takes them into that folder. The wheel scrolls the list while you drag"),
            ("Drop on the drive strip", "The thin bar between the path and the files: whatever is dropped there goes into the folder shown, whatever is selected"),
            ("Ctrl+drag in the same folder", "Duplicate, as in Explorer: “name - Copy.ext”, then “name - Copy (2).ext”; folders too. Pasting copied files into their own folder does the same"),
            ("F7 / Ctrl+Shift+N", "New folder"),
            ("Ctrl+N", "New file: the types Explorer's New menu offers, plus text, Markdown, JSON, CSV, PowerShell and batch, and your own templates"),
            ("F8 / Del", "Delete to the Recycle Bin; on a drive with its Recycle Bin turned off, it asks first, since that delete is for good"),
            ("Shift+Del, Enter", "Delete permanently; the next file is selected, to carry on from there"),
            ("Ctrl+Z", "Undo the latest copy, move, rename, delete, new file or folder, zip, extract or shortcut in this tab's folder: something made, renamed, moved or deleted there. The folder's right-click menu names it as Undo …. Operations in other folders wait for their own tab, and History → Operation history… can undo any of them"),
            ("Ctrl+Shift+C", "Copy the selected paths, quoted like Explorer's Copy as path; Copy name is in the right-click menu"),
            ("Alt+Enter", "Properties; with several items selected, Windows' combined dialog with their total size"),
            ("Enter on a script", "Runs .bat and .cmd files in their own folder, as Explorer does; F4 edits them. .ps1 opens in the editor"),
            ("Recycle Bin", "In Favorites: what was deleted, from where and when; Restore puts items back, and it can delete them for good or empty the bin"),
        }),
        ("Copy, move and the Drop bin", new[]
        {
            ("Transfers", "Every copy and move is a card in the Transfers window, which opens with it and comes forward when files already exist (the Transfers button at the bottom brings it back): one scan first (so the time left is real), progress, speed over time as a chart, pause, cancel. Several to the same drive run one after another; to different drives, together"),
            ("Files already there", "One question for the whole transfer, inside its card: Keep both (the existing file stays as name.old.ext and the incoming one takes its name), Replace if newer, Replace, or Skip"),
            ("Moving on one drive", "Instant at any size: nothing is copied, the files are renamed into place. A folder moved onto a folder of the same name is merged, file by file"),
            ("Settings → Transfers → Verify", "Reads both copies back after copying and compares them (xxHash3)"),
            ("Ctrl+F5 / F6", "Copy / move the selection to the other pane (with one pane, the first press brings it back)"),
            ("Right-click → Copy to / Move to", "The folders your copies and moves went to lately, or Choose a folder…"),
            ("Right-click → Group into new folder…", "Makes a folder here, named as you like, and moves the selected items into it. Ctrl+Z puts them back"),
            ("Drop bin", "Collect files from several folders, then send them all at once: drag them onto the bin button at the bottom of the sidebar (or onto the open bin), or press Ctrl+B on a selection. Go to the destination and press Copy here or Move here. The bin only remembers the files; nothing moves until you press one of those"),
            ("Ctrl+B", "Add the selection to the Drop bin"),
        }),
        ("Selecting", new[]
        {
            ("Drag on empty space", "Select with a rectangle; Ctrl adds to the selection. In the Recycle Bin, from anywhere, with either button"),
            ("Click on empty space", "Clears the selection; Up and Down carry on from the file you were on"),
            ("Right-drag", "Select with a rectangle from anywhere in the list; letting go opens the menu for what it selected. A plain right-click works as before"),
            ("Num +", "Select by pattern, like *.jpg;*.png"),
            ("Num −", "Deselect by pattern"),
            ("Num *", "Invert the selection"),
        }),
        ("Search and preview", new[]
        {
            ("Address or search", "One field in the path row. Enter tries a quoted, relative or environment-variable path first; folders open and files are revealed. If the path does not exist, the text is searched in the index. Esc returns to browsing."),
            ("Index search", "Searches this folder and its descendants. Search from This PC for the full index. Uses the Pathweft index service, which setup installs (Settings → Windows integration → Pathweft index shows its state). Everything-style syntax works: wildcards, quotes, | for OR, ! for NOT, < > groups, path:, parent:, ext:, size:, dm: and dc:; other functions are refused with a message. Results load 200 at a time; \"index updating\" means a drive or folder is still being indexed."),
            ("Aa / Ab / ..\\ / .*", "Match case, match whole word, match path and regular expression. These switches affect this pane's search and follow a taskbar search when opened in a pane. Whole-word matching is unavailable while Regex is on."),
            ("Search coverage", "Only files known to the index can appear. With the Pathweft index, NTFS drives are complete and other drives, network shares and folders appear once added as folder roots. Only items you can open are listed. When the index service is not running, search says so and never falls back to a filesystem scan."),
            ("Index contents", "To search inside files: open the folder and press Index contents on the bottom bar. Its text files, logs, JSON and documents (Word, PDF and the others Pathweft opens) are indexed in the background, at low priority, folder and subfolders; cloud files that aren't on the disk aren't downloaded. Indexed files carry a content badge. Press it again to drop that folder's content index (the files stay). Settings → Windows integration → Pathweft index lists the indexed folders with their counts"),
            ("content:", "content:word, content:\"exact phrase\" or content:prefix* finds files containing it, combined with any other filter (ext:pdf content:invoice). Each result says where: near line N, page N or paragraph N. Right-click a text result → Open at line opens the editor there"),
            ("Space", "Built-in preview: pictures, video/audio (Enter pauses, ←/→ seek), PDF pages, text/code, Markdown, ZIP contents, fonts and folders. Up/Down move through the list; Space or Esc closes. The taskbar widget uses the same modules"),
            ("Alt+P", "Preview pane: the selected file beside the panes, following the active pane; drag its edge to size it"),
            ("Mark images", "For picking photos while looking at them: the Mark button in the preview's header (M in Quick View) puts the picture into the Marked images collection or takes it out; the arrows beside it (Shift+Up/Down in Quick View) jump between the marked pictures of the listing. Open Marked images from the sidebar's Collections to copy or move the picks together"),
            ("Settings → Files → Preview modules", "Enable or disable each built-in viewer. Changes affect open previews immediately. Markdown takes priority over text; disabled/unsupported files show an honest state or a readable alternative"),
        }),
        ("Tools", new[]
        {
            ("F4", "Edit in the built-in code editor: tabs, syntax coloring, a status bar for encoding, line endings, indentation and language, Markdown preview (Ctrl+Shift+V). It reopens what you had open"),
            ("Right-click → Compare in editor", "Two selected files side by side in the editor"),
            ("Shift+F2", "Compare the two panes: on each side, what the other lacks or has older (or of another size) is selected"),
            ("Right-click → Find duplicates…", "Files that are exactly alike in this folder, and in its subfolders if you tick that, plus shortcuts that open the same thing. Select extra copies keeps the oldest of each; send the rest to the Recycle Bin or move them. It works in the background, with progress and Stop"),
            ("Compress to ZIP / 7z / RAR", "From the right-click menu, at once with the default settings; Compress to ZIP / 7z and email attaches the new archive to a message in your mail program"),
            ("Compress…", "Right-click → Archive → Compress… chooses everything first: the archive's name and place, ZIP, 7z, RAR or TAR, the compression level and method, dictionary and word size, solid blocks, CPU threads, splitting into volumes, a password (AES-256, with the file names hidden too for 7z and RAR; a ZIP password must use English letters, digits and symbols, as in 7-Zip), a RAR recovery record, and deleting the files afterwards (to the Recycle Bin, once the archive is verified). It remembers your choices, never the password"),
            ("Inside an archive", "Enter on a ZIP, 7z, RAR, TAR or TAR.GZ opens it like a folder, and so does Enter on an archive inside it; one whose file names are encrypted asks for its password first. Enter on a file inside opens a copy of it, and Open with, Edit, Run and Quick View work on that copy. Copy, Ctrl+F5, Copy to, Move to and dragging out work as on disk, with the same Transfers window and conflict choices. In a writable archive Del, F2 (one item or several), F7 / Ctrl+Shift+N, Ctrl+N and Group into new folder change it, and dropping or pasting files adds them; every change is staged and verified before the archive is replaced. Alt+Enter shows what the archive records about the selection and about itself"),
            ("Disk images", "Right-click an ISO, IMG, VHD or VHDX → Mount: Windows mounts it and it opens as a new drive. Eject is in the drive's right-click menu"),
            ("Extract and test", "Right-click an archive → Archive: Open archive, Extract files… (choose the folder), Extract here, Extract to “name\\” and Test archive, which checks every file against what the archive stored. Extracting is a Transfers job with the usual conflict choices"),
            ("Checksum", "Right-click → Archive → Checksum: CRC-32, CRC-64, MD5, SHA-1, SHA-256, SHA-384, SHA-512, SHA3-256, XXH64 and XXH3-64, or all of them, of files, folders and files inside archives. A hash already on the clipboard is compared"),
            ("Right-click → Resize images…", "Pictures to a preset or custom size (fill, fit or stretch, in pixels, percent, centimetres or inches), shrinking only if you like, with the JPEG, PNG and TIFF settings, removing metadata and keeping the date modified if you choose; beside the originals, in another folder or replacing them (the originals go to the Recycle Bin). A Transfers job; Ctrl+Z undoes it"),
            ("Right-click → What's using this?", "The programs holding the selected files or folders open, each with its program file and the paths it holds; Refresh looks again once you have closed them. When a copy, move or delete fails because a file is in use, the Transfers card's What's using it? shows the same. Programs running as administrator are only visible when Pathweft is too"),
            ("Right-click → Paste as file", "In an empty spot of a folder: the clipboard's text, HTML or picture saved as a new .txt, .html or .png file, ready to rename"),
            ("Right-click → New → New from template", "Copies a file or folder from your templates folder (Settings → Files → Templates), replacing $YYYY, $MM, $DD and the other date and name variables in its name if you turn that on; Open templates folder is at the end of the list"),
            ("Preview: text in pictures", "The preview's text button picks out the text in a picture (select an area, or the whole picture) with Windows' own OCR, in the languages Windows has installed, and copies it"),
            ("Preview: .reg files", "Registry files show their keys and values as a tree with each value's type and data, without changing the registry"),
            ("Create shortcut", "“name - Shortcut” beside the original"),
            ("Run as administrator", "For programs, scripts and installers"),
            ("Ctrl+`", "A terminal in this folder (Windows Terminal, or PowerShell)"),
            ("Ctrl+right-click", "The full Windows menu for the same items or folder (Ctrl+Shift+F10 or Ctrl+Menu key from the keyboard). Settings → Files → Context menu chooses which one a plain right-click opens; Ctrl opens the other"),
            ("Other apps' commands", "What other programs add to the Windows menu (VS Code, Git, VLC, Defender, PowerToys…) appears in Pathweft's menu, in its groups"),
            ("Analyze space", "Beside Terminal: show this folder as a largest-first tree with logical sizes and usage bars. Expand with the arrows or Left/Right. Folders come from the Pathweft index service at once; while the service is still loading the drive (a few seconds after Windows starts or an update) the analysis waits for it. Folders the index does not cover are measured on disk. Click again to restore the exact previous view."),
            ("Show in File Explorer", "Opens Explorer on the item, for the rare thing only Explorer does"),
            ("Edit with Notepad++", "When Notepad++ is installed"),
        }),
        ("View", new[]
        {
            ("Ctrl+Shift+1…5", "Details, List, icons M, L, XL"),
            ("Ctrl+wheel", "Zoom through the views"),
            ("Right-click empty space → View", "The same five views, from the menu"),
            ("Click a column header", "Sort; the selected file stays in sight. Each folder remembers its own order"),
            ("Drag a column's edge", "Widen it; once you size Name yourself it stays, and a wide list scrolls sideways. Double-click Name's edge to let it fill the pane again"),
            ("Colors by age", "The bar at the left of each row and the age badge go from red (minutes ago) through orange and yellow to blue-grey (a year): what changed lately stands out"),
            ("Sync badges", "Google Drive, Nextcloud and other apps' status icons, on the file icons as in Explorer, updated live"),
            ("Ctrl+H", "Show hidden files"),
            ("F5 / Ctrl+R", "Refresh and measure folder sizes again"),
            ("Expandable folders", "Right-click empty space → View → Expandable folders: in Details, folders open in place under their row (the arrow, or Right/Left), to look into several at once without leaving the folder"),
            ("Flat view", "Right-click empty space → View → Flat view: every file below this folder in one list, with a Location column, to sort or select across subfolders. Copying or moving files from subfolders asks whether to keep the folder structure or put them all side by side. Choose it again to go back"),
            ("Copy as table / Export listing…", "Right-click selected items → Copy as table puts those rows on the clipboard with the columns shown, ready to paste into a spreadsheet. Right-click empty space → Copy listing has Copy as table for every row and Export listing…, which saves the listing as CSV or TSV"),
            ("Folder sizes", "Taken from the Pathweft index service where it covers the drive, otherwise measured in the background and remembered. A change inside a folder updates its size within about a second, also on a busy drive like C:\\"),
        }),
        ("Windows integration", new[]
        {
            ("Default folder application", "Settings → Windows integration enables supported filesystem-folder and drive open/explore links. Switching off restores previous handlers still owned by Pathweft. Explicit Explorer launches, virtual Shell locations and Open/Save dialogs remain Windows surfaces; StartAllBack links are not yet supported."),
            ("Folder routes", "Under Windows integration, enable or disable each route, add or remove rows, then Apply. Directory means filesystem folders, Drive means drive roots, and Folder handles explicit Shell folder-class requests. Open JSON edits folder-routes.json; Reload reads those edits before Apply. When folder integration is off, Apply only saves preferences."),
            ("Win+E", "Enabled separately under Windows integration. A windowless helper opens Pathweft at sign-in and stops when the option is disabled; this leaves the default-folder option unchanged."),
            ("Show in folder", "Pathweft Reveal, /select and Windows Show in folder open the containing folder and select the requested file when Pathweft's folder integration is enabled."),
            ("Right-click a drive or a folder in the sidebar", "Windows' own menu for it: Properties, Eject, Format, BitLocker and what other apps add"),
            ("Ctrl+G in an Open or Save dialog", "Takes the dialog to the folder Pathweft is showing (while Pathweft is the default file manager)"),
            ("Settings → Close to the notification area", "Closing hides Pathweft next to the clock instead of quitting, so it opens again at once. Click the icon to bring it back; right-click it → Quit to really close"),
        }),
        ("Settings and updates", new[]
        {
            ("Ctrl+,", "Settings: appearance (backdrop, opacity, background picture, accent color, row height, text size per area, summary cards, animations), files, the right-click menu, transfers, Windows integration, about"),
            ("Updates", "Pathweft checks every hour; a newer version shows as a dot on the update button at the top of the sidebar, and clicking it shows what's new and asks before updating; the update downloads, is checked against GitHub's published SHA-256, installs and restarts in about ten seconds"),
            ("Light updates", "When only some files changed, the update window offers a light update with its size: just those files, much smaller than the full installer. Pathweft first checks that the installed files are exactly the previous release's (with progress, and Cancel); if any differ, it downloads the full installer instead and says why. If something differs when installing, nothing is changed and Pathweft reopens to install the full update. There's nothing to choose: it happens by itself"),
            ("What's new", "Every release's notes, by month, from the button beside Pathweft's name in the sidebar. It opens by itself after an update"),
            ("Settings → Reset to defaults", "Every setting back to how Pathweft comes, after asking; each card has its own Reset. Windows integration, tabs and Quick access stay. A double-click on a slider's handle resets just that slider"),
            ("Tooltips", "Wait 1.5 s before they show, each one; Settings → Files → Tooltip delay changes that, from the next start"),
            ("F1", "This help"),
        }),
    };

    public HelpWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        AuxiliaryWindows.Prepare(this,Root);
#if DEBUG
        HiddenTrialPresentation.Prepare(this);
#endif
        SourceInitialized += (_, _) => MainWindow.ApplyAcrylic(this);
        Root.Background = MainWindow.Tint();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // Laying out every topic took 150-250 ms before the first frame; the window comes up empty and they follow on
        // the next one, while it fades in (Motion).
        ContentRendered += (_, _) => {
            Body.Visibility = Visibility.Visible;
#if DEBUG
            if(!HiddenTrialPresentation.SuppressFocus(this))
#endif
                Find.Focus();
        };
        Scroller.ScrollChanged+=(_,_)=>HighlightReadingSection();
        Sections.SizeChanged+=(_,_)=>HighlightReadingSection();
        Scroller.SizeChanged+=(_,_)=>FitHelpRows();
        Show("");
    }

    void Find_Changed(object s, TextChangedEventArgs e)
    {
        FindHint.Visibility = Find.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        Show(Find.Text.Trim());
    }

    void Show(string query)
    {
        Sections.Children.Clear();
        Topics.Children.Clear();
        sectionTopics.Clear();
        foreach (var (section, rows) in Help)
        {
            var hits = rows.Where(r => query == "" || r.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                    || r.What.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                    || section.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) continue;
            var heading = new TextBlock { Text = section, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, Sections.Children.Count == 0 ? 0 : 18, 0, 6) };
            Sections.Children.Add(heading);
            var topic = new Button { Content = new TextBlock{Text=section,TextWrapping=TextWrapping.Wrap}, Style = (Style)FindResource("AuxiliaryNavigation") };
            System.Windows.Automation.AutomationProperties.SetName(topic,section);
            topic.Click += (_, _) => Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + heading.TranslatePoint(new Point(0, 0), Scroller).Y);
            Topics.Children.Add(topic);
            sectionTopics.Add((heading,topic));
            foreach (var (key, what) in hits)
            {
                var row = new Grid { Margin = new(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var k = new TextBlock { Text = key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Foreground = (Brush)FindResource("Accent"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 12, 0) };
                var w = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Hex("#C9D3E0") };
                w.SetResourceReference(TextBlock.ForegroundProperty,"Palette.B4BFCE");
                Grid.SetColumn(w, 1);
                row.Children.Add(k); row.Children.Add(w);
                Sections.Children.Add(row);
            }
        }
        if (Sections.Children.Count == 0)
            Sections.Children.Add(new TextBlock { Text = "Nothing matches.", Foreground = MainWindow.Hex("#8A97AA") });
        Scroller.ScrollToTop();
        FitHelpRows();
        Sections.Dispatcher.InvokeAsync(HighlightReadingSection,System.Windows.Threading.DispatcherPriority.Loaded);
    }
    void HighlightReadingSection()
    {
        if(sectionTopics.Count==0)return;
        // The last heading at/above the reading edge owns the current section;
        // near the end, select the final section even when it cannot scroll to top.
        var current=sectionTopics[0].Topic;
        foreach(var (heading,topic) in sectionTopics)
            if(heading.TranslatePoint(new Point(),Scroller).Y<=2)current=topic;
        if(Scroller.ScrollableHeight>0&&Scroller.VerticalOffset>=Scroller.ScrollableHeight-1)current=sectionTopics[^1].Topic;
        foreach(var (_,topic) in sectionTopics)
        {topic.Tag=ReferenceEquals(topic,current)?"on":null;topic.FontWeight=ReferenceEquals(topic,current)?FontWeights.Bold:FontWeights.Normal;System.Windows.Automation.AutomationProperties.SetItemStatus(topic,ReferenceEquals(topic,current)?"Current section":"");}
    }
    void FitHelpRows()
    {
        double width=Math.Min(210,Math.Max(90,Scroller.ActualWidth*.34));
        foreach(var row in Sections.Children.OfType<Grid>())row.ColumnDefinitions[0].Width=new GridLength(width);
    }
}
