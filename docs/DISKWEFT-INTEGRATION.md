# Diskweft integration assessment

2026-11-05. The selected current-pane integration is now implemented in active
S15. Actual metadata workflow22428 passes recursive totals, progressive display,
expansion, exact restore, denied/reparse/Unicode/long paths, repeated writes,
two-pane ownership, real copy and cancellation. S11/S14 source criteria pass;
the combined normal release and installed verification remain outstanding.

Everything-first sizing and bounded SQLite persistence are now part of the
authorized scope. The user separately authorized enabling folder-size indexing
in their existing Everything1.4 instance; its service/version/scope are preserved.
Indexed coverage and metadata fallback remain distinct. The earlier assessment
below records the selected source and boundaries; it is not a release claim.

## Selected interaction

Use the current pane, not Diskweft's complete UI, a new tab or another window.
The user selected this interaction after the initial assessment:

- A compact **Analyze space** toggle beside Terminal, using the same16-DIP icon
  and command-button dimensions as Pathweft's current Tools group. A disk/blocks
  motif like the supplied Optimize Drives/defrag icon fits this position. Its
  tooltip and accessible name must say Analyze space, never Defragment.
- Capture the active pane's current filesystem path as the analysis root.
  Switch that pane into an expandable folder tree with a **Space used** column,
  a bar/percentage relative to that root, and largest-first sibling ordering.
  Retain normal file icons, Pathweft palettes, row density and keyboard behavior.
- Publish progressive totals. Mark pending, incomplete, denied and cancelled
  rows honestly; an unfinished folder is not a completed zero-byte folder.
  Preserve row identity, selection and viewport as growing totals change order.
- The same toggle exits analysis and restores the prior view mode, column
  configuration/widths, sort/direction, path, selection and scroll position.
  Cancel and observe the actual scan worker completion; reject stale batches.
  Each pane owns its mode; changing the active pane must not retarget a running scan.
- Reuse Pathweft's existing Open/reveal/Properties and file-operation owners.
  Analysis is not a second file manager. Reconcile affected analysis rows after
  an operation through actual invalidation, not independent duplicate mutations.

The source currently computes **logical file lengths**, not allocated physical
disk space: `Scanner.EnumWithCancellation` uses WIN32_FIND_DATA file size and
the MFT parser uses the first data extent's real size. Label the metric honestly.
Sparse/compressed files, hard links and cloud placeholders mean a sum of these
lengths is not proof of occupied clusters or cloud account quota. Allocation-aware
measurement would be a separately specified extension, not an implied benefit.

## Checked source and upstream identity

- Existing checkout: `D:/Projects/Diskweft`; clean at
  `82ac41c9a74f3cd0ee6ae44c6bdeb0c7328eb980`. Official `origin/master`
  readback matches that commit. No replacement checkout, build or app launch.
- [Darkaxt/Diskweft](https://github.com/Darkaxt/Diskweft), normal
  [0.1.2](https://github.com/Darkaxt/Diskweft/releases/tag/v0.1.2).
  The repository records product source
  `fcdec3bac0cc2524df4f77ba116a46c791b7d177` for that release and later delivery
  documentation at the checked head. This assessment did not repeat its
  independent package/installed qualification.
- [MIT license](https://github.com/Darkaxt/Diskweft/blob/82ac41c9a74f3cd0ee6ae44c6bdeb0c7328eb980/LICENSE).
  Preserve Protagonist Labs' copyright and the full notice with imported portions.
  This is compatible with selected source reuse; do not remove attribution.

## What to borrow and adapt

| Source | Useful part | Required adaptation |
| --- | --- | --- |
| `SpaceScan.cs` | Win32 traversal/long-path handling, directory accounting, extension/largest-file summaries, NTFS record parsing | Select scanner types rather than importing the whole application. `Stats`, `Scanner` cancellation/progress and exclusion rules are static mutable state; scope them to the owned scan or serialize the one analysis session explicitly. Do not share them with Pathweft's existing sizing workers. |
| `LiveScan.cs` | `ScanSession`, copied-value batches, progressive ancestor totals, attempt identity, cancellation/fallback/terminal reconciliation | Retain bounded coalesced publication and dispatcher-owned rows. Remove the `MainUI.NothingFound` dependency by keeping the relevant scanner validity check with the engine, not by importing Diskweft's main window. |
| `MftProgress.cs`, `MftStreaming.cs` | Progressive parent resolution and bounded raw-reader/parser pipeline | Preserve out-of-order-parent/exclusion accounting, one serialized raw reader, fixed buffers and actual reader join before closing handles. Keep raw-volume privileges outside the normal browser process. |
| `ClassicUI.cs`, `App.cs`, `Appearance.cs` | Reference for stable growing-result presentation only | Do not import the standalone window, theme/settings/updater/registry/ecosystem ownership. Pathweft owns its existing UI and lifecycle. |

Both products use C#/WPF. Diskweft builds against the installed .NET Framework;
Pathweft targets .NET10. Selectively compiling adapted scanner source into
Pathweft is a smaller starting point than wrapping the standalone EXE. A managed
DLL is optional if the selected code needs a real assembly boundary; no C ABI,
native shim, plugin framework, endpoint installation or Coordinator/MCP runtime
dependency is needed merely to call C# scanning code.

Pathweft's existing `FileList` is a virtualized WPF ListView/GridView. An expanded
tree can be represented as visible hierarchical rows inside that pane, preserving
the current column/selection machinery rather than placing a full Diskweft window
inside it. Verify virtualization with a real large result; this is not yet measured.

## Boundaries and performance

- Ordinary folder analysis uses traversal of the chosen subtree. Do not scan
  the whole volume merely because a folder resides on NTFS.
- Diskweft's MFT fast path is for whole NTFS drive roots and opens the raw volume
  for reading. If later exposed, request the necessary consent through the
  product's scoped elevation design; keep Pathweft itself unelevated and retain
  ordinary traversal when raw-volume access is unavailable. No automatic UAC.
- Cloud/network paths use provider-visible metadata. Do not recurse through
  junctions, hydrate file contents to calculate sizes, or claim trusted provider
  account quota. Virtual archives/This PC need an explicit eligibility contract;
  do not silently extract archives or choose all drives as a replacement scope.
- Avoid running a duplicate recursive measurement alongside Pathweft's current
  `SizeCache` for the same root. Cache promotion must accept only valid completed
  totals under its existing invalidation contract, never partial results.
- Do not import Diskweft's search/duplicates/history/reporting/deletion/updater
  surfaces just because they are present. They are outside this focused mode.
- The donor's retained S4 evidence reports real NTFS/classic performance and
  bounded publication, not zero memory overhead. Its recorded million-file MFT
  peak working set is roughly246-250MB. This is donor evidence, not a Pathweft
  benchmark or a promised integration footprint. Measure actual memory, useful
  first results, dispatcher responsiveness and teardown after integration.

The current Pathweft menus still contain inherited **Scan with SpaceScan**
routes (`ContextMenus.cs` plus Help), including the upstream website fallback.
A later implementation must replace those entry points with this same owned
analysis action; adding a toolbar icon while retaining the old external launcher
would leave the integration incomplete. Nothing in this evaluation changes the
installed Diskweft, SpaceScan recovery material, Pathweft settings or those routes.

## Proposed qualification

First prove the complete toggle workflow on a real nested folder: correct
recursive totals and bars, navigation/expansion during scanning, largest-first
ordering without lost selection, cancellation and exact standard-view restore.
Then qualify denied/empty/reparse/Unicode/long paths, mutation invalidation,
two-pane ownership and normal shutdown. Qualify raw NTFS separately only if
included; native elevation and worker lifetime are real acceptance boundaries.
Finish with theme/narrow/DPI/keyboard/accessibility and actual packaged workflows.
Pin imported source/notices and add it to upstream-review inventory when adopted.

This is a feasible, focused source-integration proposal. It has not been built,
benchmarked or released inside Pathweft. No new implementation stage is activated
by recording it, and it does not displace S14 or resume S6/S7.
