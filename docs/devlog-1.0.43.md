# File Labs 1.0.43: tabs at the top, a Drop bin, history and Group into folder

A new user's email asked for six things on 1.0.39: tabs where the title bar was, a bin to collect files from several
folders, switching to an open tab instead of a duplicate, a history of visited folders, two right-click commands
(Group into new folder, Copy / Move to recent destinations), and closing to the notification area. All six are in
1.0.43, with What's new in the format Shelf 3.66 introduced and a Help that covers everything.

## What changed

- **No Windows title bar.** The tabs are the top row; minimize, maximize and close sit at the right end of the top
  right pane's tab row, which keeps that room free. The empty part of the tab row moves the window (Aero Snap and
  dragging off a maximized window work, as Windows does the moving), a double-click maximizes, a right-click opens the
  window menu, and resting on maximize shows Windows 11's snap layouts. File Labs' name and version moved to the top of
  the sidebar; Check for updates, What's new, Help and Settings are one row of icons at its bottom. The summary cards
  sit under the panes.
- **Drop bin.** Drag files onto the bin at the bottom of the sidebar (or onto the open bin), or press Ctrl+B / use
  "Add to Drop bin" on a selection. The bin lists them and says where they would go; Copy here or Move here sends them
  all to the active pane's folder as one transfer. It only keeps the paths: nothing moves until a button is pressed.
- **History.** The button left of minimize lists the tabs closed this session and the last 25 folders visited;
  Ctrl+Shift+T reopens the last closed tab.
- **No duplicate tabs.** A folder opened from outside (Explorer, another app, Win+E, the command line) switches to the
  tab that already shows it, in either pane; a file is selected there.
- **Copy to / Move to** in the right-click menu: the last 8 folders that copies, moves, pastes or drops went to, then
  Choose a folder…. **Group into new folder…** makes a folder beside the selection and moves the selection into it;
  Ctrl+Z puts it back.
- **Settings → Windows integration → Close to the notification area** (off by default). Closing hides the window
  next to the clock; its icon brings it back, its menu has Quit.
- **What's new** lists every release (1.0.0 to 1.0.43) by month, each under a banner of its own: a colour from the
  version number and one of five pictures, drawn the same way every time. The notes are built into the app, so it works
  offline; it opens by itself after an update, and from the sidebar.
- **Help (F1)**: twelve topics, from the window and tabs to transfers, tools, Windows integration and the sister apps,
  searchable, with a list of topics on the left.

## How

- `Chrome.cs` (new): `WindowChrome` with no caption and the glass frame kept for the acrylic; `CaptionDown` hands the
  drag to Windows (`WM_NCLBUTTONDOWN` / `HTCAPTION`) and times the double-click itself (the move loop of the first click
  swallows WPF's click count); a `WM_NCHITTEST` hook answers `HTMAXBUTTON` over the maximize button for snap layouts.
  Windows drew its own caption buttons faintly behind ours in the extended frame: `SetWindowThemeAttribute`
  (no caption, icon or system menu) removes them in a normal window, and only a window without `WS_SYSMENU` is spared
  when maximized, so the style goes. The window menu still opens from a right-click on the tab row; Alt+Space no
  longer opens it. Maximized, the root gets a margin the size of the resize border, which the window reaches past the
  screen.
- `PaneView`: `CaptionSpace` in the tab band (so the space under the buttons still moves the window), `FindTab` /
  `ShowTab`, `Visited` and `TabClosed` events.
- `DropBin.cs` (new), `ContextMenus.cs` (`SendTo`, `GroupIntoFolder`), `Settings` (`tray`, `recent`, `destinations`).
- Tray: WinForms' `NotifyIcon` (`UseWindowsForms`; the DLL was already in the self-contained publish, so the
  installer is no bigger). A real exit (Quit, signing out, the updater) closes even with the option on.
- `WhatsNewWindow.cs` (new) reads `whatsnew.txt`, an embedded resource generated from the GitHub notes of every
  release and the commit titles. `UpdateDialog` lost its "notes only" mode.
- A test copy (`FILELABS_PROFILE`) now gets a single-instance name per profile folder.

## Tests

- `--selftest` passes, with new checks: every What's new entry parses, newest first, with notes, and the running
  version has one (a release that forgets its entry fails); the recent lists keep the newest first, once, capped;
  long folder paths are shortened in menus.
- Driven on isolated test copies (own profile, own single instance, the window on the second monitor), never the
  copy in use:
  - Tab reuse: three hand-offs (`a`, `b`, `a\a-file2.txt`): two new tabs, then a switch back to `a` with the file
    selected, no third tab.
  - Drop bin: one file each from three folders with Ctrl+B, then Move here into a fourth: all three moved in one
    transfer ("Moved 3 files").
  - Group into new folder… on a file: folder "New folder" created, the file inside it.
  - History: after Ctrl+W, the menu listed the closed tab and the recent folders; Ctrl+Shift+T reopened it.
  - Close to the notification area: Close left the process running with the window hidden; a second launch showed it
    again (about 1 s, nearly all of it the second process starting).
  - Real mouse on the test window only (the cursor put back each time): a drag on the empty tab row moved the window;
    a double-click maximized it, another restored it. The hit test over maximize answered `HTMAXBUTTON` (9).
  - Maximized on a 1440 × 2560 screen: the window fills it exactly, nothing cut off; no ghost caption buttons in the
    normal or maximized state.
- Screenshots are from a demo profile and a throwaway folder tree.

## Not tested

- Minimizing and restoring by clicking the taskbar button (the window menu style is gone; the system's minimize
  and restore work, the taskbar click itself was not driven).
- Snap layouts appearing under a real pointer resting on maximize: only the hit test answer was checked.
- Copy to / Move to with a real destination list from use, and Choose a folder…: only the menu was opened.
- Dragging files from Explorer onto the Drop bin: only Ctrl+B was used to fill it.
- The window on a monitor with a different scaling than the one it was tested on.
