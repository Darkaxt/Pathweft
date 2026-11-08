# Performance donor comparison

Compared locally on 8 November 2026 before the next release. Reference:
[SysOptimizer](https://github.com/limburatorul/sysoptimizer/tree/6ddd104fe4fc3f990bc2a9b376e5795943541d49),
MIT, initially inspected through a clean shallow local clone.
The revision matches the existing donor pin; this is a feature audit, not an
upstream version upgrade. The temporary comparison clone was cleaned; selected
original sources and blob IDs are
retained in `vendor/SysOptimizer/source-manifest.json`. The S32 additions below
are source-qualified in verification/2026-11-08-s32-maintenance.md and remain
unreleased under the user's pre-release pause.

| Reference capability / source | Pathweft treatment |
| --- | --- |
| CPU, RAM, GPU/VRAM and per-device disk counters: `Services/ResourceMonitor.cs`, `MainWindow.Monitor.cs` | Existing process-wide Windows counter collector supplies the five compact cards and the expanded main-pane analysis. Reuse its observations rather than importing a second collector. Folder attribution remains a separate visible scope, with explicit Windows consent and lost-event reporting. |
| Live process filtering, stable rows, half-percent CPU/GPU sort: `MainWindow.Monitor.cs:UpdateProcessRows` | Adapt the filter and stable row/container lifetime into the CPU inspection table, with quantized CPU ordering and deterministic process/PID ties. Preserve the requested PID/start identity and pin behavior rather than donor name-only grouping. Metadata filtering uses already requested columns. |
| Absolute-time graph, peak buckets, rounded line/area, marker grouping, heat, selection, pan, zoom and overview: `MainWindow.History.cs` | Already adapted into PerformanceHistoryChart/PerformanceTimeline. Preserve existing theme, keyboard actions and narrow/high-DPI layout. Fix live-range continuity in the underlying query, rather than importing donor CSV storage. |
| Cursor facts, selected-range app averages/peaks, events and spikes: `MainWindow.History.cs`, `Services/HistoryStore.cs` | Existing Moment, Top apps, App history, Events and Spikes use the shared automatic SQLite history. Overlay recent aggregate/process observations and retain gaps; never infer past process snapshots from current processes. There is no recording switch. |
| Local app search, average/peak sorting, per-app CPU/RAM/GPU timelines, session estimates: `MainWindow.AppHistory.cs` | S32 adds the three app charts over the same SQLite/live snapshots, exact PID/start identity and explicit gaps. Process-list Inspect app history opens that section. Name-only inferred sessions are intentionally omitted because selected heavy-app observations do not prove launch/exit times. |
| CSV/app ZIP exports and weekly HTML reports: `MainWindow.History.cs`, `MainWindow.AppHistory.cs`, `Services/ReportService.cs` | S32 adds explicit selected-query CSV/JSON/standalone HTML exports. JSON includes the three chart series. CSV formula text and HTML app/event names are escaped. No automatic reports, ZIP/session export or network resources. |
| Current usage, summaries, heavy processes, spikes, app history and events: `Sysoptimizer.Mcp/Tools.cs` | Already adapted as explicit read-only stdio tools over Pathweft history. No network listener, external app, client registration or MCP activation. |
| CPU temperature/frequency, power/fans, battery/FPS | Deferred to the separately requested THRM/Taskbar Thermals consolidation assessment. No new hardware reader/driver. |
| All network/DNS, remote-PC history, signatures, app installation | Explicitly excluded by the user; not future acceptance work. UnigetUI and dedicated network tooling retain those roles. |
| Boot, Windows/driver/app changes, insights: `BootService`, `ChangeService`, `InsightService` | S32 borrows event-log readers, installed/version/removed comparisons and pressure/large-holder/sustained-CPU/boot comparisons. On-demand queries use existing retained history. App comparison baselines advance only after the existing writer saves observed change events; first discovery is a baseline, not a mass installation event. Diagnostic access failures are explicit. |
| Cleanup, startup, services, registry tweaks, built-in apps: corresponding donor services | S32 brush opens Maintenance in the current main pane with five bottom summary-card tabs, replacing the folder statistics and avoiding another category sidebar. Counts are scoped to the listed items; Cleanup bytes require analysis. Curated cleanup uses a streamed exact-file review, not donor recursive deletion. Registry/service prior state is journaled before mutation, restored exactly and conflict-checked. Current-user built-in package removal is explicit with Store reinstall guidance. No donor UI, standalone app dependency, generic command runner or blanket optimization. |

## Background audit contract

- One process-wide one-second aggregate sampler; 3,600 samples / one hour maximum.
- Tray mode keeps aggregate history without rendering cards. Aggregate samples
  save every five seconds and selected heavy-app snapshots every ten seconds,
  automatically. There is no recording/saving switch. One hour of live history,
  360 recent process snapshots, 1/7/30-day retention and the 256-MiB database plus
  bounded WAL policy apply in the tray as well as the foreground.
- CPU metadata is requested only for the visible optional columns, asynchronously
  and only for pending PID/start identities. WMI queries those PIDs rather than
  retrieving command lines for unrelated processes. No metadata persistence.
- Native file I/O is restricted to the visible loaded scope, never a background
  folder log. Worker/pipe/session lifetime stays bound to the owning process.
- Cancelled/replaced history queries cannot overwrite the latest result. Closing
  drains background work, not a continuation waiting for that same UI thread.
- Each sampler tick captures one detail selection. Re-reading the UI selection
  between CPU/GPU reads and publication could otherwise publish a null array
  after a card switch; the captured selection removes that race.
- Verify actual aggregate/recording CPU and memory, full-hour payload bounds,
  expanded/card/tray costs, hidden metadata scheduling, actual identities,
  cancellation and natural exit. Acceptance is recorded in the S31 ledger;
  code inspection alone does not establish measured cost or delivery.

Remote-PC monitoring is explicitly excluded by the user; it is not a deferred
feature or acceptance gap. No remote agent, listener or credentials are added.

The form remains Pathweft's graph-led main pane, compact themed toolbar and
virtualized table. Existing donor notices and original selected files remain in
`vendor/SysOptimizer`; no installed SysOptimizer dependency is added.
