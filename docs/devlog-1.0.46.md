# File Labs 1.0.46: show in folder lands on the file, smooth scrolling, scrolling while dragging

Three requests from a power user. Local build only, not released.

## What changed

- **"Open path" selects the file.** A file passed by another program already opened its folder with the file
  selected, but only as a bare path. Explorer's own form, `/select,"C:\x\file.txt"` (what Everything's "Open path"
  and many other programs send to a file manager), matched neither a file nor a folder and was dropped. Launches now
  read it, with `/n,` and similar switches in front, and with the path split into pieces by a space after the comma
  or a path with spaces and no quotes. When the folder was already the one on screen, the row was selected but the
  keyboard stayed where it was; it now moves to the file too. At startup, a folder already open in the right pane
  no longer loses the active pane back to the left one.
- **Smooth scrolling** (Settings → Appearance, off by default). The wheel moves a target by what it would have
  jumped (3 lines = 48 px in Details, 96 px in the icon views, a screen if Windows is set to a screen a notch), and
  the list eases there frame by frame, about 85% of the way in 0.1 s at any refresh rate. Notches during a glide add
  up. A scrollbar drag, a key or a new folder that scrolls the list ends the glide. List view scrolls sideways and
  keeps the plain wheel.
- **Scrolling while dragging.** A drag over the list within 48 px of the rows' top or bottom (the column header and
  the scrollbar count as the edge) scrolls up to 20 px every 16 ms, faster the closer to the edge, for files dragged
  from File Labs or from another app. It stops when the pointer leaves the zone or the list, on drop, Esc, or the
  end of the drag.

## How

- `App.xaml.cs`: `Program.PathArg` turns the arguments into the path; used by the fast hand-off in `Main`, by
  `OnStartup`'s hand-off and by `MainWindow`'s first-launch path.
- `PaneView.Navigate`: the "same rows" branch focuses the selected row (only if the list has the keyboard and no
  rename is open).
- `PaneView`: `Glide`/`GlideStep` on `CompositionTarget.Rendering`, driven by `ScrollViewer.ScrollToVerticalOffset`,
  so Details' pixel-scrolling `VirtualizingStackPanel` and `TileVirtualizer` keep virtualizing.
- `PaneView.ScrollAtEdge`: speed from the last DragOver, applied by a 16 ms `DispatcherTimer` (Windows' drag loop
  keeps running the dispatcher's timers, so it scrolls while the pointer rests).
- `Drop_Over` now arms the wheel hook and the edge scroll on DragOver only: `Drop_Drop` calls it after ending them,
  and before this the drop put the low-level wheel hook back for good after a drop from another app.

## Tests

- `--selftest` passes, with four new checks for `PathArg` (quoted, in pieces, two switches with a stray quote, plain).
- Isolated copy (`FILELABS_PROFILE` = a scratch folder, its own single-instance pipe; the user's running 1.0.45 was
  left alone), a folder of 300 files:
  - started with `/select,"…\file-250.txt"`: file-250 selected and on screen (UI Automation, read-only).
  - a second launch, `/select,` and the path as two arguments, handed off to the copy: file-040 selected and on
    screen in the same tab; the second process exited at once.

## Not tested

- Keyboard focus on the selected row: the test copy never got the foreground (Windows' foreground lock), so UI
  Automation could not report it.
- Smooth scrolling and the edge scroll were not tried live: both need real mouse input on the desktop the user was
  working on. They build, and were checked by reading the code paths only.
- Everything's "Open path" itself, and the agent's Explorer-window route with it.
