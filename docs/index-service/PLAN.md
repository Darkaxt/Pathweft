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

## Ledger

| Stage | State | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- |
| IS1 | COMPLETE (source) | – | – | Readiness record below |
| IS2 | COMPLETE | – | – | IS2 record below |
| IS3 | COMPLETE | – | – | IS3 record below |
| IS4 | COMPLETE | – (IDX-10 footprint accepted by the owner 2026-11-18) | – | IS4 record below; `MEASUREMENTS.md` |
| IS5 | COMPLETE | – | – | `INTEGRATION.md`; message sent to the Pathweft thread 2026-11-18 |
| IS6 | ACTIVE | – | – | IS6 record below |

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
