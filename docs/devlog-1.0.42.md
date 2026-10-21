# File Labs 1.0.42: the tester's list on 1.0.39

Everything open in the tester's notes on 1.0.39 (resent unchanged on 1.0.41). The "selected text" item needed nothing:
it was about the rename box, and the tester asked to keep what 1.0.39 did.

## What changed

**Bugs**

- **Arrows after a click on empty space.** The click clears the selection but the keyboard stays on the row it was
  on (unselected, as in Explorer); Up and Down carry on from there. Before, the list itself took the focus and Down
  left the list for a button.
- **Shift+Delete → Enter (and Delete) leave the next file selected**, the one that moved up into the deleted row's
  place (the new last row when the last one went), not nothing and then the folder's first row.
- **Preview pane dragged to the left edge.** The preview's column is capped at what the panes leave (250 px each and
  10 between, 250 with one pane). Before, its splitter had no limit: one drag made the column 8,267 px wide in a
  1,500 px window, and the picture, centred in it, ran off to the right.
- **Recycle Bin: drag to select**, left or right button, from a row or from empty space: every row the rectangle
  touches (drawn as in the panes); Ctrl adds. Plain click and Ctrl+click unchanged.
- **Open with → Notepad when Notepad is minimized or behind.** For 3 s after a Windows-menu command, while File Labs
  is still the window in front, a window of another app whose title has just changed to name the file is restored
  and brought forward (see "Not reproduced" below).

**Suggestions**

- **View in the Windows menu**: the folder's own menu (right-click on empty space, Windows menu setting) starts with a
  View submenu: XL / L / M Icons, List, Details, the current one checked.
- **The wheel scrolls the list while dragging files**, ours or from Explorer: rows in Details, columns in List,
  tiles in Icons.
- **Accent line on the active tab**, 2 px along its rounded top, in the accent colour (as Frostpane's). No setting:
  it is always there, like the active pane's edge.
- **Right-click a folder, or a shortcut to one → "Paste into folder"** (File Labs' menu) puts the clipboard's files
  in it. On a file, Paste still goes into the folder shown. In the Windows menu, Windows' own Paste already does this
  for both (checked), so nothing was added there.
- **Settings → Files → "Sort shortcuts to folders with the folders"** (off by default): a shortcut that opens a folder
  is listed among the folders.

**Not done**

- Find duplicate files: listed under "is that even possible"; not small, not built.
- Several Quick access menus with renamable titles: noted for later, as agreed.

## How

- `PaneView.xaml.cs`
  - `FocusKeepingRow()`: a press on empty space (and the strip under the list) keeps the focused row, else focuses
    the selected one, else the list.
  - `ShowItems(keep)`: remembers the view index of the first selected row; if none of the selected names is there after
    the re-read, selects the row now at that index.
  - `EntrySort`: `Folder(e) = IsDir || (setting && FolderTarget != null)`. The shortcut is read once per row (already
    cached on `Entry` since 1.0.39) and only when the setting is on.
  - Wheel while dragging: Windows' drag loop takes the mouse and swallows the wheel. A low-level mouse hook
    (`WH_MOUSE_LL`) is set on the list's first DragOver and removed when the drag leaves the list, drops, or (ours)
    ends however it ends; it only acts on a wheel turn over the list, which it hands to the list's ScrollViewer as a
    normal MouseWheel event, and passes every other message on.
  - `ViewChoices()` for the Windows menu.
- `ShellMenu.cs`: `view` items inserted at the top of the background menu as a submenu, ids from 0x8000 (past the
  shell's 1–0x7FFF); `BringForward` snapshots the titles of other processes' visible windows before `InvokeCommand`,
  then polls every 100 ms for 3 s and stops as soon as the foreground is not File Labs.
- `QuickView.cs` / `MainWindow`: `FitPreview()` sets `PreviewColumn.MaxWidth` on the area's SizeChanged, on Alt+P and
  on one/two panes.
- `RecycleBinWindow`: rectangle overlay; a left press keeps the list's own capture (taking it from the list left the
  window without any input), a right press is captured by the window (the list drops capture on the first move when
  the left button is up).
- `App.xaml` (`BrowserTab`): the tab's top border is always 2 px, transparent unless active, and comes out of the
  top padding (7 → 5), so no tab changes height.
- `ContextMenus.cs`, `MainWindow.ClipboardPaste(folder)`, `Settings`, `SettingsWindow`, Help, README.

## Tests

**`FileLabs.exe --selftest`**: exit 0, built app and published `dist\app`. New: shortcuts sort with the files with the
setting off (`..,zz,a.txt,dst.lnk,record.lnk`) and with the folders with it on (`..,dst.lnk,zz,a.txt,record.lnk`), on a
real shortcut to a folder; the title check behind "bring forward" (`note.txt - Notepad` names `note.txt`,
case-insensitive; `Untitled - Notepad` does not).

**UI, isolated copy**: `FILELABS_PROFILE` = a scratch folder (own settings, single-instance pipe, ecosystem folder),
fixtures in a scratch folder (80 text files, two folders, three shortcuts, an 800×500 PNG). The user's File Labs
1.0.41 kept running (same pid before and after). The same scripts were run against 1.0.41 built from HEAD (before)
and 1.0.42 (after). Every injected key or click was preceded by a check that the test copy's window was in front and
under the pointer; where it wasn't, the step was refused.

| | 1.0.41 | 1.0.42 |
|---|---|---|
| click file 005, click empty space, Down | nothing selected, focus on a button outside the list | file 006 selected |
| …click empty space again, Up | nothing | file 005 |
| Shift+Delete on file 010, Enter, Down | nothing selected, then Alpha (first row) | file 011 selected, then file 012 |
| Shift+Delete on files 015–017, Enter | — | file 018 selected |
| preview splitter dragged to x = 20 | preview column 8,267 px, picture at x 856–1,955 in a 1,500 px window | column stops at 666 px, picture at x 829–1,461 |
| Recycle Bin: left drag rows 1→3 | row 3 only (the list moved its selection) | rows 1, 2, 3 |
| Recycle Bin: right drag rows 3→1 | nothing changed | rows 1, 2, 3 |
| Recycle Bin: click row 4, Ctrl+click row 2 | — | row 4; rows 4 and 2 |
| 3 wheel notches down while dragging file 003 | 0 % scrolled | 10.4 % (86 rows), drag then cancelled with Esc, nothing moved |
| own menu on "A Target link.lnk" / "Alpha" / a file → Paste | — | "Paste into folder" → file in Target / Alpha; "Paste" → in the folder shown |
| Windows menu on the shortcut | (Windows' own Paste) | one Paste (Windows'), file landed in Target |
| Windows menu on empty space → View → L Icons | no View | pane switched to L Icons |
| Settings: shortcuts with folders, on | — | A Target link.lnk, Alpha, Target, Zeta link.lnk, then files |

Recycle Bin: drive C:'s bin is set to delete at once on this PC, so the four test items were written into it in
Windows' format (`$I`/`$R` pairs named `FL42X…`, pointing back at the scratch folder) and removed afterwards (8
files); nothing else in the bin was read for the test, restored or deleted (it was empty).

**Notepad, not reproduced.** Windows 11 Notepad 11.2607, already open on a scratch file, then Windows menu → Open
with → Notepad on another scratch file. On 1.0.41 Notepad came forward by itself every time: minimized 667 ms,
behind 214 ms, minimized for 20 s first 761 ms. On 1.0.42, minimized: 448 ms. So the fallback was checked with a
stand-in that does what the tester's Notepad did (a minimized window that changes its title to the file's name 0.5 s
after a Windows-menu command, here Copy as path, and never activates itself): **1.0.41 left it minimized and behind;
1.0.42 restored it and brought it forward 150 ms after its title changed.**

**Build**: `.\build.ps1` → `dist\FileLabs-1.0.42-setup.exe` (54.5 MB), no new warnings (the three CS0108 are old).

Screenshots (window only, cropped): `img/1.0.42/preview-1.0.41.png`, `preview-1.0.42.png`, `recycle-bin-rectangle.png`,
`tabs-1.0.41.png`, `tabs-1.0.42.png`, `windows-menu-view.png`, `shortcuts-with-folders-off.png`, `-on.png`.

## Not tested / limits

- The tester's Notepad case itself (it did not happen here); the fallback is tested only with the stand-in. Its
  limit: for 3 s after a Windows-menu command, any other app whose title changes to name that file is brought forward
  even if the command was not the reason (e.g. Copy as path while an editor reloads the same file).
- The wheel while dragging from Explorer into File Labs, and in List and Icons views (only our own drag, in Details).
  The hook is global while a drag is over a list: a busy UI thread at that moment would slow the mouse system-wide.
- Shortcuts sorted with folders in a folder of thousands of shortcuts: each is read once (COM + `Directory.Exists`)
  on the UI thread when the setting is on; not measured.
- Delete to the Recycle Bin (`Del`) for the next-row selection: same code path as Shift+Delete, but C:'s bin deletes at
  once here, so only Shift+Delete was driven.
- Accent line with other accent colours and light text sizes: looked at with the default accent only.
- The Windows menu's View on a drive root and on network folders.
