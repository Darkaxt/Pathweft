# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.1, 2026-10-28.

Current authorization: S1 implementation/verification, requested by the user on
2026-10-28. S1 is COMPLETE; later stages remain NOT STARTED. This
plan neither authorizes itself nor adds approval checkpoints.

## Stage map

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | COMPLETE | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Real index/search/coverage, isolation, rendered UI, browsing, physical OLE drag and OS clipboard copy/paste/restoration pass; see S1 evidence |
| S2 - Connected taskbar workflow | NOT STARTED | WIDGET-01..03, affected CORE-02/03, LIFE-01/DELIVERY-03 | S1 COMPLETE | Widget -> query -> full pane handoff -> shared actions/queue; independent view state; taskbar restart/display/focus evidence |
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
| S2 | None | All assigned criteria | S1 prerequisite satisfied; S2 implementation not requested | None |
| S3 | None | All assigned criteria | S2 prerequisite not complete | None |
| S4 | None | All assigned criteria including REA | S3 prerequisite not complete; target identity not selected | None |
| S5 | None | All assigned criteria including final release | S1..S4 prerequisites not complete | None |

Only S1 is COMPLETE. The user confirmed normal unlock; real physical drag and
clipboard checks subsequently passed. A separate STA process read the Windows
file list/copy effect, paste delivered matching contents through the existing
queue, and an independent post-exit read confirmed prior-text restoration.
No stage is ACTIVE. S2-S5 remain NOT STARTED; their implementation was not
requested. S1 has no remaining blocker or tracked deferral. Installer/update/
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

During implementation, update this ledger at meaningful integrated-feature/stage
checkpoints. Record concrete evidence and exact blockers/deferrals. At closure,
assess actual debt within the changed scope without inventing infrastructure or
consolidation stages. No intermediate product publication; final delivery is S5.
