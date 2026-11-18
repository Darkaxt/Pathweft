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

## Ledger

| Stage | State | Blockers | Tracked deferrals | Evidence |
| --- | --- | --- | --- | --- |
| IS1 | COMPLETE (source) | – | – | Readiness record below |
| IS2 | COMPLETE | – | – | IS2 record below |
| IS3 | COMPLETE | – | – | IS3 record below |
| IS4 | ACTIVE | Memory over IDX-10 on the first install; fix c691a3d built (ticket `1660ee7c`), reinstall pending admin approval | – | IS4 record below |

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
| query-after-load | `W:\pwtestlpha.txt` size 100 |
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
