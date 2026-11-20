# Pathweft index service: plan and reconciliation ledger

Authorization: `already_authorized`. The owner asked for the service on 2026-11-18,
through the Pathweft thread's brief. The scope and location revision of the same
day (owner's setup only, inside the Pathweft repository on branch `index-service`)
is recorded in SPEC.md.

| Stage | Objective | Requirements | Verification evidence |
| --- | --- | --- | --- |
| IS1 source | Complete source: core (store, query, MFT parser, protocol), service (NTFS/USN, scans, pipe, security, status, install), client library + C ABI, CLI, self-test, unit tests | IDX-01..11, IDX-14 (source) | Source inspection; readiness record below |
| IS2 build | First compilation and unit tests through the Rust gate | IDX-05, IDX-06, IDX-08 (unit), all (compiles) | Gate workflow result, test output |
| IS3 disposable | Elevated self-test on a disposable VHDX via the admin broker | IDX-02, IDX-04, IDX-06, IDX-07, IDX-14 | Self-test JSON report; VHDX removed |
| IS4 install + measure | Install through the broker; live status, parity, churn latency, memory | IDX-01, IDX-02, IDX-03, IDX-08, IDX-09, IDX-10, IDX-12 | MEASUREMENTS.md |
| IS5 integration | Integration note; message to the Pathweft thread | IDX-13 | INTEGRATION.md, message sent |
| IS6 folder changes | Follow folder roots with change notifications while running (owner request 2026-11-18) | IDX-03 (live changes) | Unit tests; console-mode test on a local folder root; installed service log on the real roots |
| IS7 adaptive checks | Per-folder adaptive re-listing, 15 min to 6 h, replacing the fixed full rescan (owner request 2026-11-18) | IDX-03 (scheduled checks) | Unit tests; console-mode test with a short schedule; installed service log (schedule summary, changes found by a scheduled check on the share) |
| IS8 query additions | `dm:`/`dc:` date filters and `parent:` (Pathweft thread request 2026-11-19, to replace Everything completely) | IDX-05 (dates, parent) | Unit tests; installed service queries |
| IS10 content index | S19 part b, owner decisions 2026-11-19: service-owned terms store beside the file index on C:, content scopes, change tracking from the name index, worker ops, `content:` with read-access filtering, reference worker in `pwindex` | IDX-15..19 | Unit tests and release build through the gate; console-instance end-to-end with the reference worker; installed service after Pathweft's release |
| IS9 install layout | S19 part (a), owner decision 2026-11-19: run in place from `Program Files\Pathweft\services\file_index`, data in `ProgramData\Pathweft\services\file_index`, migrate the old layout, add `stop` | IDX-01 | Unit tests and release build through the gate; the installed upgrade by Pathweft 1.1.83 setup (`sc qc`, `status`, folder roots kept, no `PathweftIndex` folders left) |

## Ledger

| Stage | State | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- |
| IS1 | COMPLETE (source) | – | – | Readiness record below |
| IS2 | COMPLETE | – | – | IS2 record below |
| IS3 | COMPLETE | – | – | IS3 record below |
| IS4 | COMPLETE | – (IDX-10 footprint accepted by the owner 2026-11-18) | – | IS4 record below; `MEASUREMENTS.md` |
| IS5 | COMPLETE | – | – | `INTEGRATION.md`; message sent to the Pathweft thread 2026-11-18 |
| IS6 | COMPLETE | – | – | IS6 record below |
| IS7 | COMPLETE | – | – | IS7 record below |
| IS8 | COMPLETE | – | – | IS8 record below |
| FIX1 | COMPLETE | – | – | FIX1 record below |
| IS9 | COMPLETE | – | – | IS9 record below |
| IS10 | ACTIVE | – | – | IS10 record below |

## IS1 readiness record (2026-11-18)

Source ready for validation; nothing has been compiled or run yet, so
correctness is unknown.

- Core library `native/pathweft-index/src`: `store.rs` (40-byte records, name
  arena, incremental folder sizes, orphan drop, compaction), `query.rs`
  (IDX-05 language, chunked parallel query with top-k, visibility via the
  `Access` oracle, folder sizes with restricted-directory cache), `mft.rs`
  (fixups, FILE records, runs, attribute lists, USN V2 buffers),
  `protocol.rs` (framing, JSON types), `ffi.rs`, `client.rs`, `capi.rs`
  (`pwi_request`/`pwi_free`). Unit tests in store/query/mft/protocol.
- Service `src/bin/pathweft-indexd/`: `ntfs.rs` (IDX-02 MFT read, USN follow,
  re-read on reset, offline retry, discovery), `scan.rs` (IDX-03 background,
  impersonated, swap on finish), `pipe.rs` (IDX-07 pipe, IDX-04 snapshot
  queries, status IDX-09), `security.rs` (IDX-08 AccessCheck rule and caches,
  pipe DACL), `service.rs` (SCM), `install.rs` (IDX-01), `selftest.rs`
  (IDX-14), `config.rs`, `log.rs`, `state.rs`.
- CLI `src/bin/pwindex.rs` (IDX-09 commands, IDX-12 comparison helpers).
- `scripts/selftest-vhd.ps1` (IDX-14 disposable VHDX wrapper), `PROTOCOL.md`.
- Platform guard: service and CLI are Windows-only (`cfg(windows)`); the core
  modules are platform independent.
- Validation scope: `cargo test` and `cargo build --release` through the Rust
  gate (IS2); then IS3 self-test through the administrator broker.

## IS2 record (2026-11-18)

Rust gate ticket `9dbb5ac8a13bcb74868b9889f715191b`:

- Attempt 1 (`cargo test`, workflow `22a9e301`): failed to compile the
  daemon, E0659 `warn` macro ambiguous with the built-in attribute. Renamed
  to `warning` (6e73143).
- Attempt 3 (workflow `6fa341b8`): all targets compiled; 2 of 17 tests failed.
  Regex terms containing escape backslashes were matched against the full
  path. Regex terms now use the path only with `path:` or an escaped `\`
  (e925788).
- Attempt 5 (workflow `edce8bf0`): 1 of 17 failed; the test expected
  `^n.*e\.exe$` to match `notepad.exe`, a wrong expectation (228dbd9).
- Attempt 7 (workflow `6af2ad6f`): `cargo test` exit 0, 17/17 library tests
  pass; daemon and CLI test harnesses compile (no tests of their own).
- Attempt 8 (workflow `3a45a4ac`): `cargo build --release` exit 0, no
  warnings; tree exit verified. Outputs: `pathweft-indexd.exe` 2,522,112 B,
  `pwindex.exe` 558,080 B, `pathweft_index.dll` 171,520 B.

Unit tests cover the store, query language, MFT parsing and protocol only;
the service's volume, journal, pipe and security paths are first exercised
by the IS3 self-test.

## IS3 record (2026-11-18)

Admin ticket 1 timed out without launching; ticket 5 failed in the wrapper
(Windows PowerShell turned the daemon's first stderr line into a terminating
error; fixed in 2dc545a). Ticket 6 ran `scripts/selftest-vhd.ps1` elevated on a
1 GB VHDX mounted as W: (binary from 228dbd9). All 8 checks passed:

| Check | Result |
| --- | --- |
| load | ready in 2 ms, 14 items, journal read |
| query-after-load | `W:\pwtest\a\alpha.txt` size 100 |
| churn-within-2s | create, rename, move, resize, delete visible after 217 ms |
| folder-sizes | recursive sizes correct; missing folder unknown |
| visibility | entry of an unlistable folder hidden, the folder itself listed |
| bulk-churn | 5000 files; 2063 queries during churn, p50 0 ms, p95 1 ms, max 3 ms |
| forced-reread | re-read in 5 ms; queries answered during and after |
| status-round-trip | status over the pipe, private 4.7 MB |

The VHDX was detached and deleted (`vhdx removed: True`). Limitation: the
visibility check ran with an elevated token; AccessCheck ignores privileges,
so the per-caller rule was still exercised.

## IS4 record (2026-11-18)

Admin ticket 7 installed the service (LocalSystem, automatic start). First
load of C:, D:, E:, G: (11.9 M items) took about 10.4 s in parallel. Service
private memory was 1,132 MB against Everything's working set of 1,127 MB, so
IDX-10 (private at most half of Everything's working set) failed. Cause: record
slots were indexed by MFT record number and sized to the MFT's record count,
about 1.6 slots per live entry. Fix c691a3d keeps records dense behind a key
map, shares repeated names while building and logs a breakdown on install.
Rust gate ticket `1660ee7c9c1130f205f149952f562bbb` (renews the closed
`9dbb5ac8`): attempt 1 `cargo test` 18/18 pass (workflow `a8899256`), attempt 2
release build exit 0, no warnings (workflow `b1d514d0`). The first reinstall
request returned `approval_timeout` (not launched).

Continued (same day):

- c691a3d installed (admin ticket after one `approval_timeout`): 1,014 MB
  private. The service log showed the stores at about 660 MB; the rest was
  vectors doubling on the first USN change after `shrink()` trimmed them.
- 60ac4b6 + db23e27 (ticket `521ccdac`, attempt 1 failed with E0502; corrected
  attempt 3 `cargo test` 19/19, attempt 4 release build): vectors grow by about
  1/64. Installed: 677 MB private, unchanged after 60 s of churn. IDX-10 still
  missed by 112 MB (half of Everything's 1,131 MB working set is 565 MB).
- Owner (2026-11-18): opt in every drive on this laptop, T: included ("I want
  support for everything in my laptop. I don't find much risk in knowing the
  file paths of my files"). H:, J:, T: and `\\100.74.127.101\flint-ssd` were
  added as folder roots (directory listing only).
- Owner revision of IDX-08 (2026-11-18): "Just return a visibility flag so we
  can decide in the client to filter them out or not. But features like the
  disk analyzer should always run with admin view". A machine-wide switch
  committed just before (e7aad12) was reverted in d5d9fc7, which returns every
  hit with `visible`, adds `visible_only` and makes folder sizes the
  administrator view. Ticket `c7328f39` (renews `521ccdac`): `cargo test` 19/19,
  release build exit 0 with one unused-import warning, removed in a3bac6c
  without a rebuild. Installed from d5d9fc7.
- Self-test of d5d9fc7 (admin, disposable VHDX, removed): 8/8 pass; the hidden
  entry is returned with `visible=false`, dropped by `visible_only`, and counted
  in the folder size (43,334 vs 38,334 bytes).
- Measurements: `MEASUREMENTS.md`. During the churn test Everything timed out on
  5 of 9 queries, then stayed busy and unresponsive (no answer within 10 s) for
  the rest of the session, so parity was not re-run on the final build.
- Observed, not changed: the installed service picked up the self-test's W:
  volume and keeps it `offline` until restart, logging a memory line every 15 s
  retry; folder-root rescans after the restart took 6–8× longer than the first
  scans, cause not established.

## IS6 record (2026-11-18)

Owner: folder-root rescans after a restart may stay delayed, "as long as we can
monitor updates while running". Folder modified times were checked first and are
not a usable change signal: on H: `Roms\Nintendo - Switch 2` (2026-11-16 01:02)
is newer than `Roms` (00:03), so changes do not propagate upwards, and every T:
folder reports 1970/1980.

Source: `watch.rs` (watcher thread per root, `ReadDirectoryChangesW` with a 64 KB
buffer, overflow -> rescan, failure -> rescans only; `reconcile` re-lists the
folders containing changed paths and applies additions, removals with their
subtrees, updates and new subtrees), `scan.rs` (shared `list_dir`/`walk`; the
watcher starts before the scan, its changes are applied after it and then as they
arrive). A passive 2-minute probe started watchers on H:, J:, T: and the share
without error but saw no events, so delivery on those drives is not yet observed.

Validation: ticket `134347a3` (renews `c7328f39`). Attempt 1 `cargo test`: 24/24
(5 new `watch::` tests: adds/removals/updates/renames match a rescan, subtree
removal, no-op listing, case-only rename, notification parsing), no warnings.
Attempt 2 release build exit 0, no warnings. Console-mode end-to-end test on a
local folder root (`pathweft-indexd console`, private pipe, no NTFS volumes):
9/9 pass. Created, modified, renamed and deleted files; a folder renamed with
content; a folder moved in with nested content; a folder deleted with its
subtree. All appeared in queries without a rescan; batches were applied about
1.3 s after the changes, and the folder size matched the remaining files.
Installed through the admin broker (exit 0); the installed service logged
"watching for changes" for H:, J:, T: and the share at start-up.
After the install (service restart 22:04 UTC) the first scans finished while
watching: H: 386,708 entries in 163 s, T: 174,700 in 122 s, J: 7, the share
30,204 in 1,098 s (one folder could not be listed). With every root loaded the
service used 713 MB private; the self-test's W: entry was gone.
From the end of the scans to 22:36 UTC no change batch, overflow or watcher
error was logged for any folder root, so delivery of notifications from Google
Drive, Tresorit and the SMB share is still not observed. Resolution condition:
a "changes in" line for that root in `service.log` after a file is saved or
renamed there. Until then those roots are covered at least by the periodic
rescans.
Owner test, 2026-11-18 23:11 UTC: renamed `H:\My Drive\Roms\Nintendo - Switch 2\Pokopia.nup`
to `[0400A3A01EE4C000] Pokopia.nup`. The service logged "H:: 2 changes in 1 folders
(1 added, 1 removed, 0 updated) in 167 ms" and the query returned the new path
only, so Google Drive (H:) delivery is observed. T: and the share remain
unobserved; they keep the periodic-rescan fallback.
Canary test (owner's suggestion, 2026-11-18 23:14 UTC): a small file was created,
renamed and deleted in `T:\`, `T:\Documents`, the share root and `Staging` on the
share. Every step appeared in queries within 1-2 s, and each was logged as a
change batch; no canary file remained. T: and the share therefore deliver
notifications for changes made through them.

Gap found on the share: `.music-lifecycle-recovery\.music-lifecycle-state-checkpoint`
was rewritten at 22:45:04 UTC (178,195 bytes), after the scan, but no batch was
logged and the index still holds 178,192 bytes. That file is written by a process
on the server itself, not over SMB; Samba apparently does not forward those
changes to a subtree watch. Such changes are picked up only by the periodic
rescan (every 360 minutes). Not changed; a shorter `rescan_minutes` for the share
is the owner's choice.

## IS7 record (2026-11-18)

Owner: instead of fixed rescans, "make it learn how often a branch is updated and
refresh that path ... by a minimum of 15 minutes and a maximun of 6 hours".
Source: `sched.rs` (per-folder schedule: interval = half the time since the last
observed change, clamped to 15 min .. `rescan_minutes`; seeded from believable
folder modified times; first checks spread over the second half of the
interval), `watch.rs` (`relist` re-lists folders by key and reports, per folder,
whether it changed, plus new, missing, unlistable and touched subfolders),
`scan.rs` (worker loop: notifications first, then due folders in batches of
256; a full rescan only at start-up, on `rescan` and after an overflow; hourly
summary in the log). Changes found by notifications also mark their folder as
recently changed.

Build and install (Rust gate ticket 8de04be8, renewing 134347a3): attempt 1
(workflow 2758f16f) failed two watch tests because `relist` could not build the
root folder's path (`full_path` does not resolve the root record); correction 1
uses the store name for the root. Attempt 3: `cargo test` 30/30. Attempt 4: the
release build passed but installing failed with a sharing violation (os error
32): the service reports SERVICE_STOPPED before its process has exited while it
frees about 700 MB; correction 2 makes `install` wait for that process
(QueryServiceStatusEx + OpenProcess SYNCHRONIZE) (5c6ed7c). Attempts 5 and 6:
release build and `cargo test` 30/30 (19 lib + 11 daemon), no warnings. Installed
through the admin broker (exit 0: "stopping the running service… installed
binaries… service running"); service started 2026-11-18T23:38:04Z. Schedules
after the first scans: J: 7 folders (0 at least hourly), T: 17,766 (0), H: 86,821
(2).

Real-share evidence (IS8 build, same scheduling code): after the restart the
share scanned 30,381 entries in 1,131 s and scheduled 5,131 folders (35 checked
at least hourly). At 2026-11-19T00:25:54Z scheduled checks found changes that no
notification had reported: "91 changes in 1 folders (11 added, 78 removed, 2
updated) ... (found by a scheduled check)" and "21 changes in 2 folders ...
(found by a scheduled check)". This closes the IS6 gap for changes made on the
share's server itself (now caught within 15 minutes for active folders).

## IS8 record (2026-11-19)

The Pathweft thread asked for `dm:`/`dc:` (today, yesterday, thisweek, lastweek,
thismonth, YYYY-MM-DD, ranges, `>`/`<`) and `parent:<folder>` (direct children
only) so the next Pathweft release can replace Everything completely; `dupe:`
and similar may stay refused. Source: `query.rs` (`Node::Date` with UTC FILETIME
bounds of local midnights, `Node::Parent` resolved like path terms to
`Node::Children`, date parser with an injected "today"; also `lastmonth`,
`thisyear`, `lastyear`, `YYYY`, `YYYY-MM`, `>=`, `<=`, `=` and the long forms
`datemodified:`/`datecreated:`), `ffi.rs` (GetLocalTime,
TzSpecificLocalTimeToSystemTime, SystemTimeToFileTime). Weeks start on Monday;
unknown times never match. Docs: SPEC IDX-05, PROTOCOL, INTEGRATION (also the
install and private-instance facts the Pathweft thread asked about).

Build (Rust gate ticket 0677b660, renewing 8de04be8): attempt 1 (workflow
6e87949d) failed 1 of 22 lib tests: `<dc:2024 | dm:2026-11-19>` read the
group-closing `>` into the date. Correction 1: comparison arguments (`dm:`,
`dc:`, `size:`) take only leading `<`/`>`/`=` characters, then a group-safe
term; new grouped `size:` test. Attempt 3: `cargo test` 33/33 (22 lib + 11
daemon), no warnings; attempt 4: release build, no warnings. Installed through
the admin broker (exit 0). Live checks on the installed service: `dm:today file:`
143 hits in 22 ms, `dc:thisweek ext:rs` 509, `dm:2026-11-18..yesterday *.md`
641, `parent:"D:\Projects\Pathweft-index\docs\index-service"` 5 direct
children, `folder: parent:D:\` 33, `dupe:` refused with the supported list.
Commit ef3f204; merged by the Pathweft thread (2c82aee) for 1.1.80.

## FIX1 record (2026-11-19): folder sizes of new NTFS folders

Reported by the Pathweft thread (blocking 1.1.80; IDX-06): folders created at
01:11 under `D:\Temp\pathweft-s13-s9-s11\...` reported 17,259 / 17,704 / 346,771
bytes for contents of 579 / 0 / 579 bytes, stable for 20 minutes; file sizes were
right. Cause (`store.rs` `set_index`): deleting a record marked it dead but kept
its `size`; when NTFS reused the record number for a new folder, the folder kept
that size as if its children had contributed it, and added it to every
ancestor. D:\Temp's create/delete churn reuses record numbers constantly. Fix: a
deleted file record's size is reset to 0, so a dead record holds only what live
children still contribute (a dead folder's children subtract their sizes when
they are deleted). Regression test `reused_records_do_not_inherit_old_sizes`.
A restart (install) reloads the MFT, which clears sizes already corrupted.

Verification:
- Gate ticket 005fbec6: tests 34/34 (workflow 4f5009e4), release build with 0
  warnings (c22ef6b3).
- Install: Pathweft merged it as ed56333. Its setup installed the bundled
  service (binaries dated 2026-11-19 14:29 local).
- Live check at about 21:45Z, service up 6.6 h:
  - In `D:\Temp\pwfix1-verify`, three rounds each created and deleted 200
    files, then created `rN\empty` and `rN\sub\a.bin` (579 bytes).
  - `pwindex sizes` gave root 1737, r1 579, each `empty` 0, and r3\sub 579.
    These equal `du -sb` (1737).
  - The folder was removed afterwards.
- Limit: these rounds should make NTFS reuse record numbers, but reuse was not
  confirmed. The release binary could not be hash-compared because the build
  output was already gone.

## IS9 record (2026-11-19): install layout (S19 part a)

The owner decided, relayed and confirmed in this thread:

> S19 should move the installer and its dependencies inside
> "C:\Program Files\Pathweft\index\" or better yet
> "C:\Program Files\Pathweft\services\file_index\" in case I add more features in
> the future.

Until now, Pathweft's setup shipped the service in `{app}\index` and `install`
copied it to `%ProgramFiles%\PathweftIndex`. That left two identical copies
(SHA-256 1a9036f2… for both on this machine).

Source changes (`install.rs`, `config.rs`, `main.rs`, `ffi.rs`):
- `install` registers the running executable in place. It must be under
  `%ProgramFiles%`, because a LocalSystem service must not run from a
  user-writable folder.
- `install` re-points an existing registration with `ChangeServiceConfigW`.
- It moves `config.json` and `service.log` from `%ProgramData%\PathweftIndex`
  to `%ProgramData%\Pathweft\services\file_index`, then removes the old
  folder. If a config already exists at the new location, that config wins.
- It removes `%ProgramFiles%\PathweftIndex` after the service is running.
- New `stop` subcommand.
- `uninstall` no longer deletes its own folder, because setup owns those files.
  `--purge` removes the old and new data folders.
- Service name and pipe are unchanged.

Pathweft side (its thread): `Pathweft.iss` (files to
`{app}\services\file_index`, PrepareToInstall `stop`, install, UninstallRun),
`IndexServiceSetup`, verification, PackagedChecks and build.ps1.

Consumer to update when 1.1.83 installs: the everything-search skill's fixed
path `C:\Program Files\PathweftIndex\pwindex.exe`.

Known limit: a non-administrator could create the new ProgramData folder
before the first install. The leaf folder gets the protected descriptor only
when `install` creates it. This matches the old layout's behaviour and is
accepted on the owner's single-user setup.

Gate (ticket cd5fe818, renewing 005fbec6): cargo test 36/36 (workflow
dbe801a3), release build 0 warnings (46722777). Release SHA-256:
pathweft-indexd.exe 963fda5e…c109, pwindex.exe 7a962c84…a5ae,
pathweft_index.dll 3bc7b07c…2112. Still to verify: the installed upgrade from
1.1.82 through Pathweft 1.1.83 setup.

Installed (2026-11-20): the owner upgraded Pathweft 1.1.82 to 1.1.83 (tag
v1.1.83 = 6b62885, which merges 2e46d75). The Pathweft thread reported the
result, and this thread checked it independently:
- `sc qc PathweftIndex`: AUTO_START, binary
  `"C:\Program Files\Pathweft\services\file_index\pathweft-indexd.exe" service`.
- `pwindex status` from the new folder answers (protocol 1). The stores are
  loading and becoming ready.
- `C:\Program Files\PathweftIndex` and `C:\ProgramData\PathweftIndex` are gone.
- `C:\ProgramData\Pathweft\services\file_index` holds config.json and
  service.log.
- The everything-search skill copies (the Claude plugin cache, the marketplace
  and Codex) now point at the new pwindex path. A skill query answered from
  the pathweft-index backend.

## IS10 record (content index, S19 part b)

Specified in SPEC IDX-15..19 and PROTOCOL version 2. The design was agreed with
the Pathweft thread on 2026-11-19/20:
- its schema;
- states 0-5;
- staged publish;
- global scopes;
- SYSTEM/Administrators-only store ACL;
- read-access filtering;
- no terms or snippets in replies.

Change from what was offered: no NTFS file-reference column. The name index does
not keep the reverse key map, so moves use the extension/size/modified
heuristic on all stores.

Source ready for validation (2026-11-20), not yet compiled or tested:
- `src/protocol.rs`: protocol 2, the content ops and types,
  `QueryRequest.locations`, `Hit.locations`.
- `src/query.rs`: the `Content` trait, `content:` terms, the read-access filter
  for content queries, `subtree_files`.
- `src/bin/pathweft-indexd/content.rs`: the SQLite/FTS5 store (rusqlite,
  bundled), the scope rules, reconcile, the worker ops, the protected folder and
  the reconcile thread.
- `state.rs`, `service.rs`, `main.rs`, `pipe.rs`: the store opens in
  `ProgramData\Pathweft\services\file_index\content` for the service and only
  with `--content-dir` for a console instance; the pipe serves the ops.
- `src/bin/pwindex.rs`: `content ...` commands, `query --locations N` and the
  reference plain-text worker `content work`.
- Checks to run through the Rust gate: `cargo test`, `cargo build --release`,
  then an end-to-end console-instance test (scope, work, query, change, move,
  delete, remove) on a scratch folder.
