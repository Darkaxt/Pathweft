# File Labs 1.0.44: starts up to three times faster, can wait by the clock

A user saw the window take 3–5 s to appear on a work laptop on battery. This release finds where the time went
and cuts it, adds a way to have File Labs ready before it is needed, and puts the last start's timing in About so a
slow start can be read rather than guessed.

## Where the time went

Measured on a copy with a trace, on this PC and under a slow-laptop simulation (2 cores, efficiency mode and a CPU
cap; harsher than a real laptop, so its numbers are relative):

- **The remembered folder sizes.** `folder-sizes.txt` was read before the window appeared, and every listing waited
  for it. On this PC it had grown to 277 MB and 638,584 entries: 563,000 of them were paths going round a pnpm
  junction that points back up its own repository, all written within two minutes on the day the projects were
  copied to another drive (the junction was copied as real folders, which File Labs then measured). Parsing it cost
  0.8 s of garbage-collection pauses on the window's thread here, 2.2 s throttled.
- Drawing the first rows of both lists: 1.4–1.6 s throttled.
- Compiling code on first use: 0.65–0.95 s throttled.
- One new thread per sidebar icon: 259 ms of the window's thread throttled.
- The drive rows and the folder tree's drives: 200–270 ms throttled before the first frame.

## What changed

- The size cache is read after the first frame (or when something first needs a size); listings no longer wait for
  it. Saving waits for the read to finish, and never saves a cache that was not read: closing within seconds of
  opening could write a partial file before.
- Paths that go round a loop (the same three folders twice in a row, checked past 200 characters) are dropped when
  the cache is read: 277 MB became 6.6 MB (42,986 entries) at the first close.
- Listings are shown just after the window's first frame; the drive rows and the tree's drives right after it.
- One thread for every sidebar and tab icon.
- Colours are parsed once and frozen (rows asked for theirs one by one).
- A second launch hands its folder to the running window from a `Main` that runs before WPF and App.xaml load.
- Settings → Windows integration → **Start with Windows, hidden next to the clock** (needs Close to the notification
  area). Started with `--tray` at sign-in, the window is built and drawn off screen without taking focus, then
  hidden; What's new and the update offer wait until it is first opened. The value is removed on uninstall.
- Settings → About: "Last start: window … ms, files … ms, on battery / plugged in"; the last 30 starts are in
  `startup.log` in the settings folder.
- What's new: twelve banners of File Labs itself (panes, tabs, drives, a transfer, the tree, the path bar,
  thumbnails, the Drop bin, search, age colours, sheets, folders), with the palette varied by release.

## Measurements

Self-contained builds as released, profile copied from this PC's, the test windows off screen and never focused.

With this PC's 277 MB cache, 5 runs each, median (outside timing: window visible / window answering):

| | 1.0.43 | 1.0.44 (first round) |
|---|---|---|
| throttled: window | 5.2 s | 1.9 s |
| throttled: ready | 10.2 s | 2.7 s |
| normal: window | 1.28 s | 0.80 s |
| normal: ready | 2.28 s | 1.74 s |

The later changes (drives after the first frame, frozen colours), same binary switched on and off, 8 runs each, the
app's own clock, a 4 MB cache like a laptop's:

| | before | after |
|---|---|---|
| throttled: window | 3.27 s | 2.64 s |
| throttled: files in both panes | 4.39 s | 3.96 s |
| normal: window | 1.46 s | 1.28 s |
| normal: files | 1.68 s | 1.72 s (noise) |

Tried and left out: making the second pane wait for the first (window earlier, files in both panes 450 ms later),
ReadyToRun (no measurable gain, +32.6 MB; composite −0.5 s for +59 MB), InvariantGlobalization (WPF fails to
start), a lazy rename box and a smaller virtualization cache (no gain).

A second launch handing over: 200 → 76 ms. Started hidden at sign-in, then opened: the window, files included, in
79 ms.

## Tests

- `--selftest` passes; new check: loop paths are dropped, long real ones and short ones are kept.
- The cache file after a close with the new build: 6.6 MB from 277 MB.
- `--tray`: no visible window after start; a second `--tray` exits; a normal launch shows the window in 79 ms at its
  saved place; What's new appeared only after that.
- Settings shows the new option (enabled only with Close to the notification area) and the last start line.

## Not tested

- The real laptop: a first start after boot with a cold disk and Windows Defender checking each library could not be
  simulated. About and startup.log now show what it is there.
- The sign-in start through the Run value itself (the test copies never write it); `--tray` was started by hand.
- The uninstaller removing the Run value.
