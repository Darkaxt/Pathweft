# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.1, 2026-10-28.

Current authorization: S2 implementation/verification, requested by the user on
2026-10-28 after S1 completion. S1 and S2 are COMPLETE; S3-S5 remain NOT STARTED. This
plan neither authorizes itself nor adds approval checkpoints.

## Stage map

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | COMPLETE | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Real index/search/coverage, isolation, rendered UI, browsing, physical OLE drag and OS clipboard copy/paste/restoration pass; see S1 evidence |
| S2 - Connected taskbar workflow | COMPLETE | WIDGET-01..03, affected CORE-02/03, LIFE-01/DELIVERY-03 | S1 COMPLETE | Real widget query/handoff/reveal, shared actions/queue, actual native hotkey/typing before and after Explorer restart, hidden real mixed-DPI transitions and inspected File Labs layouts; see S2 evidence |
| S3 - Internal selectable previews | NOT STARTED | PREVIEW-01..04, affected CORE-03, LIFE-01/DELIVERY-03 | S2 COMPLETE | Ported source/module inventory; preview with QuickLook absent; persisted toggles; selection correctness and resource release across all surfaces |
| S4 - REA-driven transfer improvements | NOT STARTED | TRANSFER-01..06, WIDGET-02 revalidation, affected CORE-02/03, LIFE-01/DELIVERY-03 | S3 COMPLETE; identify TeraCopy artifact before analysis | Required REA Evidence -> adoption table -> integrated transfer behavior; data/failure/recovery checks and comparable measured workloads |
| S5 - Final integration and delivery | NOT STARTED | SHELL-01, DELIVERY-01..03; reconcile every product requirement | S1..S4 COMPLETE; prerequisite blockers/deferrals resolved | Reversible optional shell integration; install/update/uninstall; final criteria review, source verification/commit, normal installer/portable release independently retrieved and verified |

ENV-01..04 belong to the current setup deliverable, not an artificial product
implementation stage. See [setup evidence](verification/2026-10-28-setup.md).

## Complete requirement ownership

| Requirements | Owner | Later boundary |
| --- | --- | --- |
| ENV-01..04 | Setup | Committed/remote document and head readback at setup delivery |
| CORE-01 | S1 | Installer/update isolation S5 |
| CORE-02 | S1, affected actions S2/S4 | Integrated package S5 |
| CORE-03 | Each changed interface in S1..S4 | Full changed-interface review S5 |
| SEARCH-01..04 | S1 | Widget S2, packaged engine S5 |
| WIDGET-01..03 | S2 | Preview S3, transfer S4, package S5 |
| PREVIEW-01..04 | S3 | Packaged dependencies S5 |
| TRANSFER-01..06 | S4 | Final package S5 |
| LIFE-01 | Each implementing stage | Final lifecycle reconciliation S5 |
| SHELL-01 | S5 | Actual disable/uninstall readback |
| DELIVERY-01..02 | S5 | Release retrieval/verification |
| DELIVERY-03 | Each import stage; final S5 | Public source/asset review |

Later packaging verification checks a different boundary; it does not defer
required current-stage wiring or real behavior.

## Required S4 sequence

1. Identify actual installed/official TeraCopy artifact and configuration; record
   version, origin, signature/hash and preserve its installation.
2. Read installed REA/storage/gate guidance; recover matching task-owned evidence
   where available. Submit one gated workflow and retain the original session.
3. Map requested behavior shallowly, then recover necessary copy, scheduling,
   verification, conflicts and interruption dependencies bottom-up. Work in
   bounded coherent behaviors rather than whole-program decompilation.
4. Corroborate static findings with controlled disposable-file observations when
   execution is authorized. Separate observations, inference and unknowns.
5. Produce the finite TRANSFER-02 adoption table. Design-critical unknowns block
   dependent changes; upstream release notes cannot replace this work.
6. Reconstruct/integrate required behaviors through the shared queue/callers;
   verify data preservation before comparisons. Reconcile all TRANSFER criteria.

This is future S4 work, not permission to begin it during setup.

## Current reconciliation ledger

| Stage | Satisfied product criteria | Remaining | Dependency / blocker state | Tracked deferrals |
| --- | --- | --- | --- | --- |
| S1 | All assigned S1 criteria: CORE-01 identity/side-by-side/handoff; CORE-02 navigation/tabs/keyboard/large listing/copy, routed drop, physical source -> OLE -> destination transfer, actual OS clipboard copy/paste, independent process readback and prior-text restoration; CORE-03 rendered controls/settings; SEARCH-01..04 real index/paging/sort/cancellation/coverage/virtual origins; applicable LIFE-01/DELIVERY-03. See [evidence](verification/2026-10-28-s1.md). | None within S1 | None | None |
| S2 | All assigned WIDGET-01..03 and affected CORE-02/03, LIFE-01/DELIVERY-03: complete real query/handoff/reveal/file-action workflow; selected hotkey mode and native input before/after real Explorer restart; original Explorer location restored and Everything processes preserved; actual 96/144-DPI transitions; inspected 100%/150% text layouts; final hidden workflow recorded zero foreground events; Debug/Release builds and source self-test passed. See [evidence](verification/2026-10-29-s2.md). | None within S2 | None | None |
| S3 | None | All assigned criteria | S2 prerequisite complete; S3 remains outside the current next-stage execution | None |
| S4 | None | All assigned criteria including REA | S3 prerequisite not complete; target identity not selected | None |
| S5 | None | All assigned criteria including final release | S1..S4 prerequisites not complete | None |

S1 and S2 are COMPLETE. The user confirmed normal unlock; real physical drag and
clipboard checks subsequently passed. A separate STA process read the Windows
file list/copy effect, paste delivered matching contents through the existing
queue, and an independent post-exit read confirmed prior-text restoration.
S2 passed following the user's next-stage request. S3-S5 remain NOT STARTED.
S1 and S2 have no remaining blocker or tracked deferral. Installer/update/
uninstall and packaged integration retain their explicitly assigned S5 boundary.

Source review found one SDK dispatcher/queue owner and independent pane query
generations, with no new timeout-driven search path or external helper product.
Clipboard operations consistently use the already-referenced WinForms API after
an isolated .NET 8 WPF flush/read failure was reproduced; this adds no fallback,
retry mechanism, package or competing clipboard owner. The finite Debug harness
waits for the actual source OLE loop to return. No required consolidation was
identified at S1 closure. All assigned S1 criteria are reconciled with actual
evidence; unchanged real-search/rendered/side-by-side evidence remains applicable.
The development branch is a completed S1 source deliverable, not a product release.

At S2 closure, one SDK owner, one settings owner and one Ferry queue still serve
all callers. Separate widget state and an immutable handoff request avoid shared
query/selection ownership; no required consolidation was found. The selected
supported mode on Windows build 26200 is explicitly reported hotkey mode because
safe taskbar space was not measurable. An attached search box is not claimed as
runtime-verified on this shell. Native shortcut/focus/query and real Explorer
recovery passed; later text-layout and Debug-only hidden-presentation corrections
did not change that native host/input path at the verified default text scale.
The final complete hidden run verified the current source, layouts, actions and
mixed-DPI notifications together with no foreground events.

S2 scratch `D:\Temp\pathweft-s2-native` used the native/small-file I/O storage
exception. All owned processes exited and required S5 comparison evidence was
retained under ignored project evidence storage before cleanup. Transaction
`30901f2e0f6fbb6ab9035fb1ee0bcc91` inspected all 4,660 frozen members and removed
4,659 ordinary candidates (297,611,120 file bytes). Its sole excluded self-test
junction was separately reviewed as an exact leaf in transaction
`6a985393cca3be50b0fc2eb452c3253e` and removed without traversing the target.
Both transactions are applied; the scratch root is independently absent.
These are helper-reported deleted file bytes, not measured free-space growth.

During implementation, update this ledger at meaningful integrated-feature/stage
checkpoints. Record concrete evidence and exact blockers/deferrals. At closure,
assess actual debt within the changed scope without inventing infrastructure or
consolidation stages. No intermediate product publication; final delivery is S5.

## S2 execution and design contract

1. Reuse a separate PaneView in the widget with the shared SDK owner. Route
   open/reveal/copy/move through the existing application actions and Ferry queue;
   hand query, explicit scope and sort into a main pane without shared selections.
2. Adapt the pinned donor's toolbar/search focus handling, native taskbar-child
   attachment and UIA layout measurement. Own hotkey, Explorer TaskbarCreated,
   display/DPI and WinEvent subscriptions without timer-driven retries. Default
   integration is off. Report hotkey mode when attachment or safe space is absent.
3. Add controls to the existing settings window; preserve the existing blue accent,
   dark WPF surfaces and Segoe UI typography. Taskbar box: 120-240 DIP wide, no
   overlap with measured shell buttons/tray. Popup: 680x520 DIP, minimum 560x380,
   constrained to the active monitor work area. Inspect 100%/150% text scale,
   increasing popup bounds with the text setting (1020x780 DIP at 150%) so the
   result list retains usable space rather than clipping below fixed controls.
   keyboard focus, scrolling, coverage and action reachability in rendered evidence.
   User clarification: use Huashu design as a reference and align EverythingToolbar
   components with the File Labs theme. Keep results/name and query foremost,
   transfer destination/actions together, and reuse the existing typography,
   brushes and controls. No separate donor-product theme. Hidden trials are the
   default to avoid disrupting current activity; native focus/shell claims need
   their own real boundary verification. Per-monitor-v2 awareness is declared in
   the app manifest so monitor transitions can produce actual DPI notifications.
4. Verify complete real widget query -> handoff/reveal -> file actions/queue,
   independent pane/widget state, opt-in persistence/disable, native shell lifecycle
   and display changes. Reconcile every S2 criterion, preserve private evidence,
   clean scratch, commit/push/read back. No S3 work or intermediate release.
