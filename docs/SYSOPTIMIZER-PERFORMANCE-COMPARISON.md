# Performance donor comparison

Compared locally on 8 November 2026 before the next release. Reference:
[SysOptimizer](https://github.com/limburatorul/sysoptimizer/tree/6ddd104fe4fc3f990bc2a9b376e5795943541d49),
MIT, clean shallow clone at `D:/Temp/pathweft-s31/sysoptimizer-reference`.
The revision matches the existing donor pin; this is a feature audit, not an
upstream version upgrade. The temporary clone remains an active comparison
input until adoption and verification finish.

| Reference capability / source | Pathweft treatment |
| --- | --- |
| CPU, RAM, GPU/VRAM and per-device disk counters: `Services/ResourceMonitor.cs`, `MainWindow.Monitor.cs` | Existing process-wide Windows counter collector supplies the five compact cards and the expanded main-pane analysis. Reuse its observations rather than importing a second collector. Folder attribution remains a separate visible scope, with explicit Windows consent and lost-event reporting. |
| Live process filtering, stable rows, half-percent CPU/GPU sort: `MainWindow.Monitor.cs:UpdateProcessRows` | Adapt the filter and stable row/container lifetime into the CPU inspection table, with quantized CPU ordering and deterministic process/PID ties. Preserve the requested PID/start identity and pin behavior rather than donor name-only grouping. Metadata filtering uses already requested columns. |
| Absolute-time graph, peak buckets, rounded line/area, marker grouping, heat, selection, pan, zoom and overview: `MainWindow.History.cs` | Already adapted into PerformanceHistoryChart/PerformanceTimeline. Preserve existing theme, keyboard actions and narrow/high-DPI layout. Fix live-range continuity in the underlying query, rather than importing donor CSV storage. |
| Cursor facts, selected-range app averages/peaks, events and spikes: `MainWindow.History.cs`, `Services/HistoryStore.cs` | Existing Moment, Top apps, App history, Events and Spikes use the shared SQLite recording. Automatically overlay live aggregate history. Do not invent past process snapshots from current processes or turn recording on. |
| Local app search, average/peak sorting, per-app CPU/RAM/GPU timelines, session estimates: `MainWindow.AppHistory.cs` | Name queries and average/peak sorting already exist in the common facts table. Separate three-chart per-app pages and inferred sessions are not imported into this CPU inspection correction; donor sessions depend on its different app-index coverage. This remains a disclosed difference, not claimed parity. |
| CSV/app ZIP exports and weekly HTML reports: `MainWindow.History.cs`, `MainWindow.AppHistory.cs`, `Services/ReportService.cs` | Not imported in this correction. These add user-facing export/report workflows and privacy decisions beyond current inspection; no automatic reports or file writes are introduced by the audit. |
| Current usage, summaries, heavy processes, spikes, app history and events: `Sysoptimizer.Mcp/Tools.cs` | Already adapted as explicit read-only stdio tools over Pathweft history. No network listener, external app, client registration or MCP activation. |
| CPU temperature/frequency, power/fans, battery/FPS, network, remote-PC history, long retention | Additional sensors/counters/drivers, remote access or retention are not imported. Show unavailable measurements honestly and preserve the selected one-hour live ring and opt-in recorded retention. |
| Tweaks, cleanup, services, startup, debloat, signatures, app installation and DNS | Outside this performance-first release. No maintenance effects, execution blocking or new administrator operations are borrowed. |

## Background audit contract

- One process-wide one-second aggregate sampler; 3,600 samples / one hour maximum.
- Tray mode keeps aggregate history, but does not render the cards or enumerate
  process details. Recording stays opt-in; off closes the writer and creates no
  new database. On writes aggregates every five seconds, heavy app snapshots
  every ten seconds and applies the selected retention.
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
