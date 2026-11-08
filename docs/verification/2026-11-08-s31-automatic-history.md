# Automatic performance history correction

Scope: MONITOR-21, specification 0.86. The user explicitly requested correction
and verification, followed by a pause before a new release. The earlier S31
closure through 1.1.33 did not establish automatic process-history investigation.

The cause was the `PerformanceRecording` switch: the aggregate ring continued,
but background process snapshots and SQLite writes depended on that switch.
Consequently a graph could be visible without the contemporaneous process facts.

The switch, setting field, serialization/load branch and menu item are removed.
Legacy `performance.record=0` no longer disables collection. One existing sampler
automatically saves aggregates every five seconds and selected CPU/RAM/GPU app
snapshots every ten seconds, including while hidden. Retention remains 1/7/30 days,
default seven. No second collector, service, MCP activation, command-line retention
or background file-I/O log is introduced.

The automatic recent ring holds at most 360 process snapshots/one hour; queries
combine it with older saved observations without duplicate overlap. PID/start and
GPU identities, true gaps and inaccessible-process coverage remain explicit.
Hover, clicked moments, selected-range top apps and app-history queries use those
facts. Persistent history survives normal close/reopen and the independent stdio
MCP reads the same saved facts without starting another collector.

SQLite has a 256 MiB page ceiling and oldest-first capacity pruning below that
ceiling. Time expiry and capacity pruning use small chronological transactions.
Checkpoint/backpressure stops additional journal writes at the 8 MiB threshold
(plus the bounded transaction that crosses it) if an active reader prevents
checkpointing; saving resumes after release. Existing larger history is reclaimed
incrementally into the ceiling rather than copied, replaced or emptied. The
recent ring remains available while persistence is waiting.

The aggregate ring retains its existing 8 MiB budget. The process ring has a
separate 8 MiB qualification budget with 45 selected processes and two adapters.
Existing steady CPU/private-memory limits remain in force.

Verified source acceptance:

- Full source application 182592 passed actual counters, automatic persistence,
  hover/moment/range/app queries, overlap, GPU/PID identities, gaps, cancellation,
  bounded storage, held-reader backpressure/recovery, main-pane/browser state,
  dark/light 100/150% narrow/wide layouts and natural shutdown. Its hidden-tray
  interval measured 0.0909% normalized CPU and +241,664 private bytes; statistics
  and cards measured 0.1698% and 0.1617%, expanded CPU 0.5308%.
- Final focused application 113480 passed those investigation workflows plus
  actual larger-history migration into a smaller test ceiling, database integrity
  and reopen. No history activation was performed. The old no-database query path
  also remains usable through the bounded recent ring.
- Source self-test 152916 passed the changed settings/reset contract and existing
  application assertions. The later test-only viewport correction does not alter
  that verified product/settings path.
- An intervening focused run failed because its mouse-action test requested a
  container for an off-screen virtualized process row. It now scrolls that row
  into view and awaits WPF layout before exercising the same real handler. The
  final focused run passes; the failed receipt is retained, not reclassified.
- Final isolated resource process 181064 passed automatic collection/persistence,
  detail switches, the 3,600-point aggregate and 360-snapshot process limits, and
  pending I/O-client disposal. Initial steady sampling measured 0.1953% normalized
  CPU and 32,047,104 incremental private bytes (under 32 MiB); the subsequent
  interval measured 0.0929% CPU and +540,672 private bytes. Aggregate retained
  memory was 2,599,640 bytes; the full 45-process/two-adapter hour added 4,373,296
  bytes. No detailed file-I/O permissions were granted: the unelevated worker
  reports the actual denied ETW access and exits, as expected.
- The updated independent delivered-consumer project compiles against the current
  assembly with no warnings/errors. This is source/interface qualification, not
  a claim that a new release package has been built or delivered.

Private evidence is under `.local/evidence/2026-11-08-s31-auto`; it includes
original receipts, rendered captures, build/self-test logs and hashed retention
records. All tests use isolated task profiles; the installed 1.1.33 processes,
settings and MCP-client configuration are unchanged. All source criteria pass.
Exact spent build, self-test and application/resource trial roots were removed
through inspected transactional-cleanup tickets after retaining required evidence.
S31 is BLOCKED only by the explicit pre-release pause; no stage is ACTIVE and
no source blocker or tracked deferral remains. Normal source commit/remote sync
does not publish a release or claim installed parity.
Publication, independent package retrieval and installed verification are paused
by the user's explicit instruction and have not been performed for this patch.
