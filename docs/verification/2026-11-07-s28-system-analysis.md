# Integrated system analysis qualification

Scope: MONITOR-09..13. User selected performance/history/read-only MCP before
maintenance. The running installation remains 1.1.30; no MCP client is configured
or activated. DNS, app installation, security enforcement and maintenance actions
are excluded.

Implemented: compact cards open the main analysis region, with themed selectors,
sortable tables and preserved browser objects/state. Existing sampler and tracing
ownership remain; recording is optional and defaults off. A single SQLite writer
stores aggregate observations every five seconds and selected heavy processes
every ten seconds, with explicit 1/7/30-day retention. Shared queries and the
explicit stdio endpoint return recorded facts, gaps and observation limits.

Source application116416 passes the complete existing counter/card/tray workflows
and actual widget -> analysis -> recording -> query -> independent MCP process ->
browser return. Analysis130208 additionally passes genuinely scrolled tab/selection
preservation, dark/light100/150% narrow/wide layouts, PID reuse, bounded top-app
counts, retention, gap rendering after recorded-to-live transitions, and explicit
unreadable-history handling through normal application shutdown. Evidence is
retained under .local/evidence/2026-11-07-s18/s28-source-pass and s28-final-analysis.

The top-app query uses identity-ordered indexes for streaming groups, a bounded
LIMIT sort and a separate full matching count. The actual SQLite query plan is
retained under .local/evidence/2026-11-07-s28/query-plan.json. No new production
package dependency, service, startup entry, donor app or hardware driver is added.

Selected SysOptimizer MIT sources are pinned at
6ddd104fe4fc3f990bc2a9b376e5795943541d49. Original selected blobs, hashes, license
and adaptation boundaries are recorded under vendor/SysOptimizer. The corresponding
source verifier now checks those original Git blobs in the delivered source archive.

Refactor assessment: existing collector/panel/worker ownership and shared query
contracts suffice. No additional provider framework or required restructuring was
identified. The finite analysis grid preserves table virtualization; query errors
cannot skip native-worker retirement. No required tracked deferral.

Resource172048 passes with recording0.1093% normalized CPU and448KiB retained
private growth; default aggregate0.031%, steady private growth27.9MiB and the
synthetic full-hour ring2.73MiB also satisfy existing limits. These are short
controlled observations, not a long-running memory or storage forecast.

Native170636 passes actual CPU/GPU/file attribution, automatic folder scope,
analysis focus, tray suspension/resumption and exact worker/session retirement.
Protected broker request0ecad834edd14848bf3b40068299c752/ticket25 launched and
exited0. Required evidence is retained under
.local/evidence/2026-11-07-s18/s28-native-pass. The earlier ticket24 expired
without launching and is historical, not a test failure or remaining blocker.

Remaining acceptance: committed-source package/source/application
verification, normal1.1.31 publication, independent retrieval and applicable installed
readback. No current external execution blocker or tracked deferral remains.

The first unpublished package from fa518f2 failed its existing settings reset
coverage assertion for performance.record/performance.days. Those explicit choices
belong to System analysis, so unrelated Appearance/Files/Transfers resets preserve
them. The production coverage map now records that ownership, with a regression
assertion that both values survive those resets. Source self-test95992 passes.
Failed package evidence is retained privately; it is not final delivery evidence.

The superseded early candidate is not release evidence: later query/shutdown/graph
corrections require a fresh package. Cleanup transactiondea868b6ed3990f6717f8629c32ed6f5
applied successfully, removing7,323,831,805 bytes with no errors and confirming
the exact candidate root absent. Required build/source staging remains under the exact registered
D:/Temp/pathweft-s28 and D:/Temp/pathweft-s28-delivery paths for native/small-file I/O.
