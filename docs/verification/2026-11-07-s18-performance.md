# S18 performance monitoring — release qualification

Final closure2026-11-07: S18 COMPLETE. Normal1.1.28/cc9f0ea is published with
six assets. Final and independently downloaded source/native/application checks
pass; installed readback152724 verifies11,301 matching members, restarted
main173864/helper171780, readiness,22 routes and preserved Darkaxt.Pathweft
shortcut/profile identity. No blocker or tracked deferral remains. Historical
pre-release checkpoints below retain their original evidence and limitations.

Historical checkpoint: Specification0.78 assigns MONITOR-01..05 and CORE-25..27 to S18, the only ACTIVE
stage. Source/native workflows pass. Final package/publication/retrieval and
applicable installed acceptance remain required; candidate app170864 passes the
complete native/self-test/application checks, actual packaged counters/toggle
and all twelve annual resources. Public/installed release is
still1.1.27. S24 is COMPLETE, S25 motion NOT STARTED; no tracked deferral.

## Implemented product boundary

The compact Performance command swaps the lower statistics area for CPU, GPU,
physical-drive and focused-folder cards. Card actions open direct themed tables;
the command restores statistics. Existing StatCard/palette resources support
wide/narrow, independent-pane, light/dark and100/150% layouts. No S25 card
transitions or independently installed collector service are included.

One shared one-second sampler retains at most3,600 observations and one hour,
including tray mode. CPU uses GetSystemTimes deltas. GPU uses hardware DXGI
LUIDs and PDH busiest-engine/dedicated-memory data. Drive counters represent
physical-device activity. Initial/missing/sleep-gap observations stay unavailable.
Directory handles resolve final path, volume mount and a single physical extent.
Network, Google virtual mounts and unresolved/spanned devices do not borrow the
boot/cache disk's throughput. Physical mapping is resolved once per path change.

Explicit detailed-I/O enablement starts a privileged Pathweft-owned worker bound
to exact parent/start/image and pipe-peer identities. A named stop event covers
pre-connection cancellation; owner/pipe loss and disposal retire its unique ETW
session and process. No service or persistent ETL log is installed. Logical File
I/O includes cached requests and differs from physical traffic. Folder tracing
requires the visible active owner's focused widget; other detail attribution
is demand-driven. Interval records/strings/process identities are bounded.
Dropped/truncated observations are explicit, never exact totals or fabricated zero.

The repeated address capacity strip is removed; sidebar/lower capacity remains.
Real foreground folder/address/search work owns a current-surface/black-track
address background. Unknown totals use an indeterminate band. Quiet background
sizing does not flash it. Completion/error/cancel/supersession retire matching
state/animations without replacing drafts/caret/selection/focus. Reduced motion
is honored. Read-only archives use a faint centered diagonal ribbon reaching the
outer pane edges through unused footer space, without intercepting input;
editable archives remain unlabelled. Twelve distinct ImageGen changelog banners
select by release month across years in a centered960:170 viewport.

## Inspected acceptance evidence

Retained private evidence: `.local/evidence/2026-11-07-s18`.

| Criteria | Actual evidence |
| --- | --- |
| MONITOR-01/02: command/cards/details/browsing/layouts | Full source workflows including `source-ribbon-loading` pass commands/cards/CPU-GPU tables, return action, current path state, independent panes and responsive themes/scales. `native-final` captures focused-folder card/table presentation. |
| MONITOR-03: attribution and honest states | Actual Windows CPU, NVIDIA RTX4090 Laptop GPU and physical-device readings; other-process CPU/D3D11 GPU, writes and cached reads. Network/registered Google physical mapping is rejected. Non-admin ETW denial remains explicit. Other GPU hardware/spanned volumes are not claimed as exercised. |
| MONITOR-04: history/tray/bounds | Source workflow proves shared collector, tray/reopen, hour/3,600-sample retention and unavailable gaps. Synthetic retention input is confined to the fixture. Focus loss/hiding/scope change stops folder tracing. |
| MONITOR-05: native lifecycle | Request8639b6fca3344a1bbb211db3c56587b8/ticket20 exited0. App172048 passes actual private-desktop focus/refocus, compact/detail transitions, no unfocused restart, scope replacement and owned trace/worker exit. Desktop receipt confirms unchanged input desktop and empty job. Medium client171624/high worker171800 also prove actual IPC, read/write attribution and natural worker exit0. |
| CORE-25: artwork | All twelve generated originals inspected and adopted with SHA-256 records in `banners/monthly-manifest.json`. Actual source changelog decodes resources, verifies2026/2027 month selection and centered crop. Final packaged rendering remains a delivery check. |
| CORE-26/27: loading/ribbon | Final `interface-faint` app173512 proves pending real read, draft/caret/selection preservation, completion, quiet refresh and actual indexed-query retirement. Actual dark/light100/150% captures and geometry checks prove the faint ribbon reaches the outer pane border, rather than stopping above the footer. |

## Measured resources

Sampler45316 measured0.032556% total-machine CPU over30.34s,26,619,904 incremental
private bytes and2,417,288 retained heap bytes for3,600 independent payloads.
These satisfy0.5% CPU,32MiB incremental private and8MiB history budgets.

Final actual medium/high boundary:

| State | Duration | Client CPU | Worker CPU | Worker private bytes |
| --- | ---: | ---: | ---: | ---: |
| Known folder activity | 30.008s | 0.06183% | 0.18875% | 50,696,192 |
| Idle interval | 29.990s | 0.03908% | 0.12211% | 47,214,592 |

The representative-workload1% total-machine worker CPU/96MiB private budgets
pass. They are observations, not universal caps. This busy host dropped
76,281/2,858 observations; the product reports `Incomplete observations`.
Known attribution/lifecycle pass; readings are not exact machine-wide totals.

Earlier integrated WPF measurements over30 sample windows per state:

| State | Process CPU | End private bytes |
| --- | ---: | ---: |
| Statistics | 0.2851% | 134,000,640 |
| Cards | 0.1131% | 172,433,408 |
| CPU detail | 0.3513% | 146,100,224 |
| Hidden/tray | 0.0523% | 138,211,328 |

These are warmed isolated source WPF processes including browsing/offscreen
presentation/capture allocations, not installed idle baselines. The whole app
footprint is not the sampler's incremental cost.

## Corrected failures

An earlier native request was rejected before launch by fullscreen quiet mode;
explicit user readiness and the normal broker resolved that gap. No host desktop
or clipboard input was substituted.

Cross-integrity testing exposed a shutdown self-join: the stop callback cancelled
the async worker inline, whose finally block synchronously joined that callback.
It now awaits the event-owned callback join asynchronously, without a timeout.
Exact hung-fixture recovery was separately broker-approved; retained evidence
`bridge-shutdown-reproduction` is not lifecycle acceptance. Final171800 exits0
naturally. A later refocus check exposed an in-flight scope acknowledgement race:
identical requests now share the pending acknowledgement. Failed evidence remains
in `native-scope-ack-race`; final172048 passes. Earlier owned-launch-handle fixture
errors were fixed in the DEBUG hook, not misdiagnosed as a product pipe-ACL issue.

## Sources, delivery and cleanup

The actual graph has25 packages. TraceEvent4.0.0 and newly selected dependencies
have pinned packages, original source archives and MIT records in
`vendor/Performance`; the official NuGet advisory query reported no listed
vulnerable selected packages. Final corresponding-source checks remain required.

Final1.1.28 must pass full package/native/application checks, publish six normal
assets, independently retrieve/hash/verify them and complete applicable installed
readback. Exact D:/Temp/pathweft-s18 and D:/Temp/pathweft-s18-delivery use D: for
native/small-file I/O. Captures/results are retained by hash; spent fixtures are
transactionally cleaned after actual process exit. Active build/package inputs
remain only until their last consumer.

## Primary references

- [System CPU times](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
- [GPU counters and busiest-engine semantics](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)
- [Volume mount mapping](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumepathnamew)
- [Physical volume extents](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_volume_get_volume_disk_extents)
- [Kernel File I/O](https://learn.microsoft.com/en-us/windows/win32/etw/fileio)
- [TraceEvent4.0.0](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/4.0.0)
- [Pinned source](https://github.com/microsoft/perfview/tree/10e5d271cebcdf5d6cf122c6ab9c863860a7a2f1)
- [Pinned MIT license](https://github.com/microsoft/perfview/blob/10e5d271cebcdf5d6cf122c6ab9c863860a7a2f1/LICENSE.TXT)
