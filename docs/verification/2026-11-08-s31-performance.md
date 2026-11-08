# Performance continuity, process inspection and background audit

Scope: MONITOR-17..20; S31 is ACTIVE. Remote features are explicitly excluded.
The next release must include the local donor comparison and actual resource
audit, not just the process columns. No installed user setting or MCP client is
changed by qualification.

Root cause: the cards used the live ring while range queries used only recorded
SQLite observations. With recording off, the same visible interval became empty.
Shared queries now overlay available live aggregates on older records, prefer
the live overlap, preserve gaps/device identity and reset Now to the rolling hour.
Missing past process snapshots remain explicit; live aggregates do not invent
attribution or enable recording.

CPU inspection uses stable PID/start rows, on-demand asynchronous WMI metadata,
a themed Columns menu, pin/unpin, local filtering and deterministic half-percent
sort steps. Path/command values are not recorded. A hidden panel schedules no
new metadata reads; current background reads drain without awaiting UI work
during synchronous window closure. Each sampler tick captures one detail mode
to avoid a CPU/GPU switch publishing an uncollected null array.

The [local source comparison](../SYSOPTIMIZER-PERFORMANCE-COMPARISON.md) records
borrowed behavior and deliberate differences. The existing MIT donor revision
6ddd104fe4fc3f990bc2a9b376e5795943541d49 is unchanged. The newly selected monitor
source matches blob610f35133a64fd69a367f2f2661eadbb09a6827c in both clone and vendor.
No second collector, donor installation, sensor driver, remote listener or
maintenance workflow is added.

Source application176784 completed the integrated counter/card/tray/history/MCP
workflow and exited naturally. Its actual 30-sample intervals measured normalized
CPU0.0779% for statistics,0.0380% for cards,0.7359% for expanded CPU inspection
and0.0378% hidden in the tray. Private-memory changes were -8.33MB,+1.31MB,
-6.69MB and0 respectively. These are short observations, not proof of a
long-running leak's absence or a whole-machine idle guarantee.

Source application169080 then passed the requested themed filter and separate
popup layouts. The final focused trial (specialists-b8168940643d4643b8d924071d4cf631)
passes the updated sampler, mouse/keyboard pins, real identity-bound metadata,
PID reuse, donor-style filtering, hidden scheduling, live/recorded overlap,
range/Now recovery, device/cancellation/gap/error handling and natural shutdown.
Dark/light100/150%,820/1400-DIP layouts and the separate checked menu surface
are rendered and inspected. Required evidence is hash-retained under
.local/evidence/2026-11-08-s31/source-evidence.

Independent resource19744 passes default aggregate0.0244% normalized CPU,
recording0.0954%, recording private growth1.35MiB, initial native-counter warm
private growth28.66MiB and full-hour ring2.71MiB. Card-switch publication and
pending worker connection/retirement also pass. The receipt's `seconds` field
covers subsequent phases as well; CPU fractions were calculated at each actual
30-tick observation boundary. Do not interpret it as an80-second CPU window.
Recording-off and read-only MCP privacy contracts remain verified. The medium
test correctly receives Access Denied for ETW; it does not request elevation.
Existing real elevated I/O qualification remains applicable because tracing/IPC
and its ownership code are unchanged.

The first unpublished 1.1.33 package passed source and native routing checks but
failed its application self-test because the embedded changelog omitted 1.1.33.
The missing entry is added without weakening the release-order/current-version
assertion. The failed package receipts are retained; it was never published.

Remaining acceptance: final committed package/source/native/app checks, normal release,
independent retrieval and applicable installed readback. S31 remains ACTIVE;
there is no tracked deferral. S30 remains NOT STARTED.

Refactor assessment: preserve existing collector/query/panel/worker ownership.
The stable row collection directly serves live inspection; no generic provider
framework or storage replacement is needed. No unrelated restructuring is required.
