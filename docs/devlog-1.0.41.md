# File Labs 1.0.41: what Reel knows about a video, above its preview

## What changed

- **A line above a video in the preview pane (and Quick View)** when Reel is installed and the video is in its
  library: Reel's poster, then `Heat (1995) · watched 12 Oct · Reel` for a film, or
  `The X-Files — S02E04 Memento Mori · not watched · Reel` for an episode (the episode title is left out when Reel
  only had the series title for it). "watched" without a date when Reel has none; the year is added to the date
  when it isn't this year.
- **"Reel" is a link that starts Reel.** Reel's list gives no id, so there is nothing to hand to its `--play <id>`;
  the link only opens the app. No Open or Play button was added: the preview's own play button stays the only one
  (the studio's no-duplicate-Open rule).
- **Nothing extra** when Reel is not installed (no card, or a card whose exe is gone or not on a local drive), when
  its list is missing or unusable, or when the video is not in it.
- **Works with**: Reel's line now also says it puts a film's poster and watched state above it in the preview.
- **Cards from sister apps**: an `exe` that is not a plain local path (`\\server\share`, `\\?\`, `\\.\`, a mapped
  network drive, a relative path) is now refused before anything touches the disk. This applies to every sister,
  not only Reel.

## Why

Plan item 9 (vault `Plan-integrari-ecosistem-2026-10-20.md`). Reel 1.13.0 writes
`%APPDATA%\ProtagonistLabs\data\reel.files.json`; File Labs is where people meet the video files themselves, and
until now a film looked like any other mp4. The card change: `File.Exists` on `\\host\x.exe` makes Windows try to
sign in to that host with the user's NTLM hash, and a card is another program's file.

## How

- `Sisters.cs`
  - `Local(path)`: `X:\...`, at most 1024 characters, no invalid path characters, and the drive is not a network or
    missing one (`GetDriveType`, which never goes on the network). `ExeFromCard` uses it before `File.Exists`.
  - `Reel(video)`: card + exe first (`Exe("reel")`), then the list, looked up by full path, case-insensitive.
  - `ReelFiles(file, posters)`: at most 16 MB (bigger = treated as absent, not read), BOM stripped, `JsonDocument`
    with `MaxDepth` 8, `schema` must be the number 1 and `files` an array; only the first 20,000 entries (Reel's own
    cap) are read. Cached by the file's modified time and size, one reference swapped whole (lookups run on worker
    threads); an unusable file is cached as unusable, so it is not parsed again at every video.
  - Every entry checked: `path` a fully qualified string, `title` a string, `watched` a boolean — otherwise the entry
    is skipped. Strings at most 300 characters (paths 1024), no control characters. `year` 1870–2100,
    `season`/`episode` 0–999, `lastWatched` an ISO date — otherwise that field is dropped. `poster` must be local
    (`Local`), inside Reel's own poster folder after normalising (`%AppData%\reel\posters\`, where Reel imports every
    poster; `..` cannot climb out) and `.jpg/.jpeg/.png/.webp`. Duplicate paths: the first wins. `updated` is not
    used: Reel rewrites the file when the library changes, so an old date only means nothing changed.
- `QuickView.cs`
  - `MediaView` (video only) starts `ReelHeader`: the lookup and the poster run on a worker thread; the header is
    added only if the selection hasn't moved on (`quickVersion`).
  - `Poster(path)`: under 10 MB and not empty, decoded with `DecodePixelWidth = 200`, `CacheOption = OnLoad` (Reel's
    file isn't kept locked), frozen; anything unreadable = no poster, the line still shows.
  - The link is a `Hyperlink` (keyboard-focusable, exposed to UI Automation), muted colour, underline on hover; the
    poster has the automation name "Poster".
- With `FILELABS_PROFILE` set, the ecosystem folder and Reel's poster folder are both under the profile, so a test
  copy never reads the real ones.

## Tests

**`FileLabs.exe --selftest`** (exit 0 — built app and published `dist\app`), new checks, all on synthetic files in
a temp folder (the real `%APPDATA%\ProtagonistLabs` and its leftover `reel.files.json` were never read):
- a list with 10 well-formed and broken entries plus `7`, `null`, `[]`: exactly the 5 usable entries; film fields,
  poster and date read; lookup in another case; episode line; the "episode title = series title" line
- poster `..\..\evil.png` out of the poster folder, `\\nas\share\p.png`, `\\?\C:\p.png`, a `.exe` poster: dropped
- `watched: "yes"`, `title: 42`, a relative path, a title with a line break: entry skipped; `year: "2019"`,
  `year: 1500`, `season: 5000`, `lastWatched: "yesterday"`: field dropped
- `Local`: UNC, `\\?\`, `\\.\pipe`, relative, `C:x.exe` refused; a card whose exe is `\\127.0.0.1\c$\...` = no app
- cached while unchanged (same instance), read again after a change; a BOM; old `updated` (2001) still read
- missing file, `schema: 2`, `schema: "1"`, `files: {}`, a top-level array, a file cut in half, 64-deep nesting,
  **8 MB of random bytes**, a file over 16 MB: all treated as absent, no exception
- **20,001 entries**: 20,000 read, the 20,001st not found
- poster: a real PNG decodes at 200 px wide; a 10 MB file, 5 KB of junk named `.jpg`, a missing file: no poster

**Measured** (this PC, 5900X, NVMe; synthetic list of 20,001 entries with long paths, 7,539 KB — bigger than Reel's
~330 bytes/entry because the synthetic paths are long), three runs of the built app and one of the published one:

| | |
|---|---|
| first read + parse + validation of the 20,001-entry list | 314–333 ms, on a worker thread, once per change of the file |
| lookup afterwards (file time check + dictionary) | 44–48 µs |

**UI, isolated copy** (`FILELABS_PROFILE` = a scratch folder: own settings, single-instance pipe, ecosystem folder
and Reel poster folder). The user's File Labs 1.0.40 kept running untouched (same pid before and after); no Reel was
running. The fixture: 5 tiny mp4s made with ffmpeg (3 s, 640×360), 3 posters drawn with Pillow (400×600 jpg), a
`reel.files.json` naming 4 of the videos (one with a `\\127.0.0.1\c$\x.jpg` poster), and a Reel card whose exe is a
stub that logs how it was started. The window sat 1 px inside the left edge of the desktop; driven only through UI
Automation patterns (Select, Invoke), no keys or clicks injected. For each screenshot it was moved on screen at the
bottom of the z-order without activation (an off-screen WPF window prints blank), printed, and moved back.

- Heat → `Heat (1995) · watched 12 Oct · Reel` with poster; Arrival → `Arrival (2016) · not watched · Reel`;
  the X-Files episode → `The X-Files — S02E04 Memento Mori · not watched · Reel`; Bad poster → the line without a
  poster; Home video (not in Reel) → no line.
- Invoking "Reel" started the stub with **no arguments** (log: `ARGV=` empty).
- No line for any of them with: the card removed; the card's exe renamed away; the card's exe set to
  `\\127.0.0.1\c$\Windows\notepad.exe`; `reel.files.json` replaced by 8 MB of random bytes. The line came back when
  each was restored. The window answered throughout.
- Screenshots (window only, cropped to the preview pane): `img/1.0.41/film.png`, `episode.png`, `no-poster.png`,
  `not-in-reel.png`.

**Build**: `.\build.ps1` → `dist\FileLabs-1.0.41-setup.exe` (54.5 MB), no new warnings.

## Not tested

- The real Reel 1.13.0 writing the list and receiving the click (stub card and synthetic list only; Reel 1.13.0 is
  not released, and the user's real ecosystem folder was not to be read).
- Quick View (Space) itself: it builds the same `MediaView`, but opening it needs a key press, which the test did not
  inject; only the preview pane was driven.
- A `.webp` poster (needs Windows' WebP codec; jpg and png were used).
- The time to the line appearing in the window: UI Automation's tree search (~3 s per look on this window) is far
  slower than the lookup, so it measures nothing; the lookup itself is the 44–48 µs above.
- Keyboard: the link is a `Hyperlink` and reachable by Tab in WPF, but focus order was not walked.
