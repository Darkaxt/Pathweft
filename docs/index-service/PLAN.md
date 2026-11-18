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
| IS1 | SOURCE READY (unvalidated) | – | – | Readiness record below |
| IS2 | ACTIVE | – | – | – |

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
