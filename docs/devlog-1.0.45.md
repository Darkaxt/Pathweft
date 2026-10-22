# File Labs 1.0.45: easier to see where a selection starts, and readable renames

Three small changes from feedback after 1.0.44.

## What changed

- **Where Name ends, in Details.** A press right of the Name column, on any row, starts a selection rectangle
  (since 1.0.38); a press on the name picks the file up. Nothing showed where one turned into the other. A thin line
  (white at 9%) now runs down the list exactly there, following the column's width, the sideways scroll and the
  list's height. Details only: List and Icons have no such rule.
- **The selected text while renaming is readable with any accent.** The selection was drawn over the text with the
  accent at 85% opacity; with a red accent, the selected part of the name turned into a red block. It is now 45%.
  Drawing the selection behind the text instead (WPF's `UseAdornerForTextboxSelectionRendering=false`) was tried
  first and dropped: in the rename box the selected text disappeared entirely.
- **Start with Windows is easier to turn on.** The option was greyed out until "Close to the notification area" was
  on, with nothing saying why. It can be ticked at once now and turns that option on, since a window hidden at sign-in
  needs the icon by the clock to come back. Turning the icon off still turns the start off. It stays off unless the
  user turns it on.
- What's new: the 1.0.44 notes, rewritten after its release, are now in the app.

## How

- `PaneView`: `NameEdge`, a 1 px rectangle in the selection overlay at `NameColumn.ActualWidth - ScrollX`, the same
  boundary `Drag_Down` tests; moved on the column's `ActualWidth` change, `ScrollChanged` and the overlay's size.
- The three rename boxes (Details, Icons, List): `SelectionOpacity` 0.85 → 0.45.
- `SettingsWindow`: `Startup_Click` turns on `CloseToTray` (and its icon) when the start is ticked.

## Tests

- `--selftest` passes (the running version's What's new entry included).
- On an isolated copy with a red accent (#FF1E00): F2 on `chrome_Dti88joVyU.png` showed the selected name light on
  dark red, the extension beside it unselected; at 85% it was a plain red block.
- The line seen in Details at the Name column's edge, through every row, at 25% and then at 9%.
- Settings on the isolated copy, with Space on the checkbox as a user would press it: ticking Start with Windows
  turned on Close to the notification area and saved it; unticking the icon turned the start off. The test copy
  never writes the sign-in entry; the one on this PC (set by the user) was left as it was.

## Not tested

- The line with the column dragged narrower than its header text, and in a pane scrolled sideways by a lot.
- Other accent colours than red and the default blue for the rename selection.
