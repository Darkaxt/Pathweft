# File Labs 1.0.40: Stash and Backup Labs backups, previewed and opened in their apps

## What changed

- **The preview pane (and Quick View) reads backups made by two sister apps** without their passwords and
  without restoring anything:
  - **Stash** — `Stash-<date>.stash` (2.1), and the older `Stash-<date>.zip` / `Thunderbird-Backup-<date>.zip`.
    A plain one shows when it was made, its size, the apps and profiles in it, the number of files, and every
    Thunderbird mail folder with its message count.
  - **Backup Labs** — `<stamp>.bkl` (1.3.0), and the older plain `<stamp>.zip`. A plain one shows the number of
    files, their total size and where they came from (per drive or share, the deepest folder they share).
  - An encrypted one says "Encrypted — open it in Stash / Backup Labs with its password".
  - A file that claims to be one and isn't (random bytes, a cut-off zip, a `.stash` without a manifest) says
    "Could not read this backup".
- **"Open in Stash" / "Open in Backup Labs"** button in the preview: starts `Stash.exe --browse "<path>"` or
  `BackupLabs.exe --open "<path>"`. Without the app, or with a version older than the one that knows the switch
  (Stash 2.1.0, Backup Labs 1.3.0), it opens the app's page
  (`utm_source=filelabs&utm_medium=app&utm_campaign=open-backup`).
- **Enter / double-click** on such a backup opens it in its app when that is installed and new enough; otherwise
  exactly what happened before (the shell's default).
- **Context menu**: "Open in Stash" / "Open in Backup Labs" appears only when the app is *not* usable (it then
  leads to the page). With the app installed, the menu's own Open already opens the backup there, so no second
  Open is added (the studio's no-duplicate-Open rule).

## Why

Stash 2.1 and Backup Labs 1.3.0 gave their backups their own extensions and a command-line switch to open one.
File Labs is where people meet these files; until now a `.stash` or `.bkl` was a hex dump in the preview and
"no app associated" on Enter.

## How

- `Backups.cs` (new): what a file is, decided by its **first 8 bytes**, never the name alone — `STASHENC` (Stash,
  encrypted), `BKLENC1\0` (Backup Labs, encrypted), `PK\3\4` / `PK\5\6` (zip, then looked inside). Only `.stash`,
  `.bkl` and `.zip` files named the way the apps named backups are opened at all, so an ordinary zip costs nothing
  and behaves exactly as before.
  - Stash plain: `manifest.json` with a `Format` number and an `Apps` object (capped at 32 MB). Profiles from
    `<App>/profiles.ini` (`Name=`, first 64 KB). Mail folders by Stash's own rule (`MailStore.Folders`): under
    `Thunderbird/<profile>/Mail|ImapMail/<account>/` or `External/<n>/`, a file with a `.msf` beside it or no
    extension. Messages = lines beginning `From ` that are the first line or follow an empty one (RFC 4155, the
    rule Stash splits folders by), counted while streaming the zip entry 1 MB at a time.
  - Backup Labs has no manifest. An old `<stamp>.zip` counts as Backup Labs only when its name is a Backup Labs
    stamp (`yyyy-MM-dd_HHmmss.zip`) **and** every entry starts with a drive letter or `UNC/server/share`;
    any other zip with that name stays a zip. A `.bkl` that is a zip is Backup Labs' (Backup Labs itself reads
    any zip named `.bkl`).
  - Counting is capped at **64 MB of mail per folder and 3 s for the whole backup**; a capped folder says
    "≥ n messages", folders past the 3 s say "not counted". Everything runs off the UI thread and stops when
    the selection moves on.
- `ContextMenus.cs`: `BackupStart` (exe from the ecosystem card, else the uninstall entry; version from the exe's
  file version), `OpenBackup`, the menu entry. Paths go through `ArgumentList`.
- `PaneView.OpenSelected` is async: the check runs on a worker thread before deciding.

## Tests

All files synthetic, in a scratch folder.

**Made with the apps' own engines**
- Stash: `tests\New-MailFixture.ps1` from the Stash repo (its `Invoke-Backup`, Stash 2.1 working tree), a plain
  `.stash` with 300 messages in `Big`, a plain one with **20 000** messages (`Big` = 978 MB uncompressed, file
  792 MB), and with `-Password` an encrypted copy through `Protect-Backup`. The old `Stash-<date>.zip` = the plain
  backup under its pre-2.1 name (same bytes).
- Backup Labs: a scratch console project compiling Backup Labs' `Engine.cs` / `Crypto.cs` and calling
  `Engine.Run` (two folders and a single file as sources), plain and with a password → `.bkl`. The old
  `<stamp>.zip` = the plain archive under its pre-1.3.0 name (same bytes, as 1.2 wrote it).
- Also: an ordinary zip, random bytes as `.stash` and `.bkl`, the plain `.stash` cut in half (as `.stash` and as
  `Stash-<date>.zip`).

**`FileLabs.exe --selftest`** (new checks, exit 0): plain Stash zip (app, profile, files, two mail folders with
3 messages each including a `From ` inside a paragraph and a `>From ` line), plain `.stash`, encrypted Stash,
encrypted and plain Backup Labs (files, size, `C:\Users\me` and `\\nas\share\x`), old Backup Labs zip, ordinary
zips (even named like a backup) stay zips, junk / cut-off / no-manifest = could not read, counting stops at its cap.

**On the engine-made files** (timings on this PC, NVMe):

| File | Result | Time |
|---|---|---|
| plain `.stash`, 300 msgs in Big | 8 folders: Big 300, INBOX 2, Sent 1, Receipts 2, Inbox 9, Trash 0, Archives/2025 1, pop Inbox 3 (all match the fixture) | 377 ms |
| plain `.stash`, 20 000 msgs in Big | Big "≥ 1,309 messages" (64 MB cap), the rest exact | 693 ms |
| uncapped count of that Big folder | 20 000, 978 MB in 7.3 s = 133 MB/s | — why the cap |
| encrypted `.stash` / `.bkl` | encrypted, date from the name | < 1 ms |
| plain `.bkl` | 66 files, 540.8 KB, from the sources' shared folder | 3 ms |
| quick check (Enter / menu) | any of them | 0.1–0.9 ms |

**UI, on an isolated copy** (`FILELABS_PROFILE` → own settings, own single-instance mutex, own ecosystem cards;
the user's File Labs, Stash and Backup Labs kept running untouched). Driven through UI Automation; every key
was sent only after checking the foreground window belonged to the test copy.
- Screenshots (window only): `img/1.0.40/stash-plain.png`, `stash-encrypted.png`, `bkl-plain.png`,
  `bkl-encrypted.png`, `junk-bkl.png`, `stash-big.png`.
- While the 792 MB backup was being counted the window kept answering: 20 UI Automation round-trips, max 4 ms.
- Old Stash zip and old Backup Labs zip preview like the new ones; `photos.zip` still shows its listing; the cut
  `Stash-<date>.zip` shows the same zip error as before.
- **Sister apps not installed** (profile without their cards): the menu has "Open in Stash" on plain/encrypted
  `.stash` and the old Stash zip, "Open in Backup Labs" on `.bkl` and the old Backup Labs zip, nothing on
  `photos.zip` or `junk.stash`.
- **Sister apps too old**: cards pointing at the installed Stash 1.9.0 and Backup Labs 1.2.0 (menu only read,
  nothing clicked): both still count as not usable, so the menu shows "Open in Stash" / "Open in Backup Labs"
  (the page), and neither app was started.
- **Sister apps installed**: the test profile's cards pointed at a stub exe (file version 9.9.0) that writes down
  how it was started. The menu then has no extra Open. Enter on 6 backups and the preview button (invoked through
  UI Automation) on 2 started the stubs with exactly:
  `--browse | <full path>` (plain, encrypted `.stash`, old Stash zip) and `--open | <full path>` (plain,
  encrypted `.bkl`, old Backup Labs zip).

**Build**: `.\build.ps1` → `dist\FileLabs-1.0.40-setup.exe` (54.5 MB), no new warnings; `--selftest` on the published app: exit 0.

## Not tested

- The real Stash 2.1 / Backup Labs 1.3.0 receiving the command (stubs only; the user's copies were running and
  are single-instance, and neither 2.1 nor 1.3.0 is installed here).
- Clicking the page fallback (it would open the browser); the URL is in the code and the menu entry was seen.
- Enter on a junk `.stash` / `.bkl` (no app is associated here; Windows would show its Open with dialog) — the
  decision not to hand it to the app is in the self-test.
- Backups on a network share that is asleep: the preview and Enter read off the UI thread, but the context menu
  reads the first 8 bytes (and an old-named `.zip`'s entry list) on the UI thread when it opens.
- A Stash backup with Firefox, several Thunderbird profiles, or mail outside the profile (`External/`): the code
  follows Stash's rules, but the fixture has one Thunderbird profile only.
- A cut-off `.stash` still gets "Open in Stash" in the menu (only its first bytes are read there); Stash then
  reports it cannot read it.
