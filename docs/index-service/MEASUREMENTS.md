# Pathweft index service: measurements against Everything (SPEC IDX-12)

Measured 2026-11-18 on the owner's PC (32 logical processors), service
`PathweftIndex` running as LocalSystem, against the owner's running Everything
(1.4 GUI process, PID 71256, with its service). Everything was used read-only
through `es.exe` with timeouts: its settings were not changed, it was never
restarted or stopped, and no root-wide query was sent (`pwindex compare`
refuses empty and `*` expressions).

Builds: first install from 228dbd9; memory fixes c691a3d and 60ac4b6/db23e27;
current install d5d9fc7 (per-hit visibility flag, administrator-view folder
sizes). Raw logs: `C:\ProgramData\PathweftIndex\service.log`.

## Summary

| Measure | Pathweft index (d5d9fc7) | Everything |
| --- | --- | --- |
| Items indexed (NTFS C:, D:, E:, G:) | 11.9 M | similar set, plus its own folder indexes |
| Memory | 677 MB private for NTFS only (stable through churn); 718 MB with H:, J:, T: folder roots | 1,131 MB working set, 1,192 MB private at the start; 1,255 / 1,279 MB later while hung |
| Initial index, NTFS (all four in parallel) | about 10.5 s (MFT read) | not measured: would need an Everything restart, which the brief forbids |
| Typical query (round trip) | 85–225 ms p50 | 0.4–1.3 s via `es.exe` including process start, before the hang; then no answer within 10 s |
| Query during heavy churn | 127/127 answered, p50 213 ms, p95 246 ms, max 272 ms | 5 of 9 queries timed out (> 5 s), then no answer within 10 s for the next hour (11:47 to 12:42 local, still hung) |

**IDX-10: accepted by the owner over the target.** The target is service private memory at most half of
Everything's working set: 565 MB against 1,131 MB. The service uses 677 MB for
NTFS, 43% below Everything's working set and 2.3× lower than its own first
build, but 112 MB over the target. Getting below it would need less precise
timestamps or dropping attribute bits (cloud-placeholder flags among them).
That trade was not made. The owner then accepted the footprint (2026-11-18:
"as long as it is an improvement over Everything, I accept whatever resources
it may be using"). With every root loaded after the IS6 install: 713 MB private.

## Memory

| Build | Private | Working set | Notes |
| --- | --- | --- | --- |
| 228dbd9 (first install) | 1,132 MB | 840 MB | record slots indexed by MFT record number: about 1.6 slots per live entry |
| c691a3d | 1,014 MB | 670 MB | dense records + key map + shared names; vectors doubled on the first USN change after the trimmed load |
| 60ac4b6 / db23e27 | 677 MB | 671 MB | vectors grow by about 1/64 instead of doubling; unchanged after 60 s of churn |
| d5d9fc7, with H:, J:, T: | 718 MB | 712 MB | folder roots add 24.2 MB (H:) and 10.5 MB (T:) |

Breakdown logged by the service on load (60ac4b6 and later):

| Volume | Items | Records | Names | Key map | Store total |
| --- | --- | --- | --- | --- | --- |
| C: | 5.63 M | 214.8 MB | 76.0 MB | 22.0 MB | 317 MB |
| D: | 4.00 M | 152.5 MB | 46.9 MB | 20.1 MB | 223 MB |
| E: | 1.32 M | 50.5 MB | 20.1 MB | 5.2 MB | 77 MB |
| G: | 0.92 M | 35.3 MB | 14.6 MB | 3.5 MB | 53 MB |

Records are 40 bytes (size, modified, created as 64-bit; parent, name offset,
attributes, security ID as 32-bit).

## Initial index time (NTFS, MFT read, four volumes in parallel)

| Build / conditions | C: | D: | E: | G: |
| --- | --- | --- | --- | --- |
| 228dbd9 | 10.4 s | 8.6 s | 3.9 s | 2.2 s |
| c691a3d | 11.1 s | 9.0 s | 3.2 s | 2.3 s |
| 60ac4b6 / db23e27 | 10.5 s | 9.0 s | 4.0 s | 2.1 s |
| d5d9fc7, while the VHDX self-test ran and Everything used a full core | 41.9 s | 28.0 s | 4.9 s | 4.9 s |

The last row is not a clean measurement; it is recorded because it happened.

## Query latency (d5d9fc7, `pwindex bench`, 20 runs, all stores loaded)

Everything was using a full core (hung) during these runs; the machine has 32
logical processors.

| Query | Matches | p50 | p95 |
| --- | --- | --- | --- |
| `path:"D:\Projects\" *.cs` | 1,293 | 85 ms | 98 ms |
| `notepad.exe` | 20 | 110 ms | 126 ms |
| `pathweft` | 258 | 112 ms | 119 ms |
| `desktop.ini` | 37,881 | 121 ms | 157 ms |
| `ext:pdf` | 4,185 | 158 ms | 187 ms |
| `*.sln` | 868 | 224 ms | 259 ms |

Before d5d9fc7 the caller's access was checked on every candidate; `pathweft`
took 688 ms p50. It is now checked only for the returned page.

`folder_sizes` for `C:\Windows`, `C:\Users` and `D:\Projects`: 298 ms including
the `pwindex` process start (sizes are kept per folder, no traversal).

## Query latency during heavy churn

`pwindex churn`: a writer creates, renames, rewrites and deletes files in
`D:\Temp\pathweft-index\churn` while `*.sln` is queried repeatedly (it cleans up
after itself).

| Run | File operations | Pathweft index | Everything (`es.exe`, 5 s timeout) |
| --- | --- | --- | --- |
| 30 s, with `--es` | 62,340 | 9/9 answered, p50 212 ms, max 261 ms | 4 answered (p50 725 ms, max 2.0 s), 5 timed out |
| 30 s, ours only | 40,479 | 127/127 answered, p50 213 ms, p95 246 ms, max 272 ms | – |

After these runs Everything stayed at about one full core (45–100% of a core in
later samples, no I/O) and answered no query within 10 s from then until the
last check (11:47 to 12:42 local, about an hour). This matches the owner's report that Everything
hangs. The service was idle at 0% between requests.

The elevated self-test (IDX-14) measured the same path on a disposable volume:
5,000 files created with 3,315 queries during the churn, p50 2 ms, p95 25 ms;
changes visible in queries within 235 ms.

## Result parity (`pwindex compare`, 228dbd9, before Everything hung)

At that time the service still filtered by the caller's access; d5d9fc7 returns
every match with a `visible` flag instead.

| Query | Ours | Everything | Common | Only ours | Only Everything |
| --- | --- | --- | --- | --- | --- |
| `notepad.exe` | 17 | 27 | 17 | 0 | 10 |
| `*.sln` | 864 | 868 | 862 | 2 | 6 |
| `pathweft` | 239 | 272 | 239 | 0 | 33 |
| `ext:lnk` | 2,273 | 2,667 | 2,239 | 34 | 428 |
| `*.pdf` | 2,924 | 4,179 | 2,469 | 455 | 1,710 |
| `desktop.ini` | 416 | 37,883 | 409 | 7 | 37,474 |
| `ext:rs` | 114,306 total | 114,364 | – | – | – (ours capped at 100,000 per page) |

Every difference inspected falls into one of these groups:

- **Folder roots not scanned yet.** H: (37,392 of the `desktop.ini` difference)
  and T: were still in their first scan.
- **Folders the normal account cannot list.** Prefetch, other users' profiles,
  container layers, `$Recycle.Bin\S-1-5-18`, `C:\Windows\ServiceProfiles`.
  Everything runs elevated. These are now returned with `visible: false`.
- **Hard links.** For example `C:\Windows\en-US\notepad.exe.mui` (3 links) and
  WinSxS files: the index lists one primary name per file, as the brief allows
  ("no hard-link nuances").
- **Comparison artifacts.** `es.exe` prints in the ANSI code page, so accented
  paths ("Álbum de cámara", "Imágenes") did not line up in the diff and showed
  as "only ours" plus "only Everything".
- **Query syntax.** `folder: node_modules`: Everything returned 0 through
  `es.exe` argument splitting; not a result difference.

With d5d9fc7 and the folder roots loaded, `desktop.ini` returns 37,881 (37,810
visible to the owner's account, 71 flagged `visible: false`) against
Everything's 37,883 from before the hang, and `ext:pdf` returns 4,185 against
4,179. A full re-run of the table was not possible because Everything no longer
answered.

## Folder roots (opt-in scans)

Enumerated with the owner's token at background priority; names and metadata
only, no file contents, no hydration.

| Root | Entries | First scan (10:40 UTC) | After restart (11:25 UTC) |
| --- | --- | --- | --- |
| J: (Google Drive) | 8 | 7 ms | 17 ms |
| T: (Tresorit) | 174,670 | 97 s | 813 s |
| H: (Google Drive) | 386,709 | 136 s | 868 s |
| `\\100.74.127.101\flint-ssd` | 30,178 | 21 min | > 23 min (still running at the time of writing) |

The second scans were 6–8× slower with the same trees. The service was
enumerating steadily (about 3,000 directory operations per second, no errors);
the cause is not established. Those scans overlapped the elevated self-test's
disk activity and Everything's busy core, but Everything did no I/O. Folder
roots are rescanned from scratch after every service restart and every
`rescan_minutes` (default 360). Until a root's first scan finishes, queries
report it in `updating_stores` and return nothing from it.

## Limits of these measurements

- Everything's initial index time was not measured (it would need a restart).
- Everything's latency comes from `es.exe` and includes process start; ours is
  the pipe round trip from `pwindex`.
- Everything hung after the churn test, so parity was not re-run on the final
  build, and Everything's idle latency comes from before the hang only.
- One machine, one session, no repeated cold starts.
