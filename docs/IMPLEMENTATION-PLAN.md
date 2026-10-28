# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.1, 2026-10-28.

Current authorization: S1 implementation/verification, requested by the user on
2026-10-28. S1 is parked BLOCKED on the locked input desktop; later stages remain NOT STARTED. This
plan neither authorizes itself nor adds approval checkpoints.

## Stage map

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | BLOCKED | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Search/identity/rendered/virtual-origin checks pass; final OS clipboard and physical OLE drag require an unlocked input desktop |
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
| S1 | CORE-01 identity/side-by-side/handoff; CORE-03 rendered controls/settings/keyboard; SEARCH-01..04 real index, paging/sort/cancellation/coverage/virtual origins; applicable LIFE-01/DELIVERY-03, including the queued-watcher regression. See [evidence](verification/2026-10-28-s1.md). CORE-02 navigation/tabs/keyboard/large listing/copy/routed drop pass. | CORE-02 OS clipboard round trip and physical source -> OLE -> destination drag | External advancement blocker: Windows LockApp owns the input desktop; OLE clipboard returns CLIPBRD_E_CANT_OPEN. Resolve by unlocking normally and rerunning the actual interaction checks. S1 acceptance and S2 progression depend on this. | None |
| S2 | None | All assigned criteria | S1 prerequisite not complete | None |
| S3 | None | All assigned criteria | S2 prerequisite not complete | None |
| S4 | None | All assigned criteria including REA | S3 prerequisite not complete; target identity not selected | None |
| S5 | None | All assigned criteria including final release | S1..S4 prerequisites not complete | None |

S1 is parked BLOCKED because its remaining acceptance work requires an unlocked
input desktop. No stage is ACTIVE. Later stages remain NOT STARTED; their S1
prerequisite is unresolved and implementation was not requested. There are no
tracked deferrals; the OS interaction criterion remains in S1.

Source review found one SDK dispatcher/queue owner and independent pane query
generations, with no new timeout-driven search path or external helper product.
No required consolidation was identified in the implemented scope. This is not
stage closure: CORE-02 and final S1 reconciliation still require real interaction
evidence. The development commit/branch is an intermediate deliverable, not a
completed stage or product release.

During implementation, update this ledger at meaningful integrated-feature/stage
checkpoints. Record concrete evidence and exact blockers/deferrals. At closure,
assess actual debt within the changed scope without inventing infrastructure or
consolidation stages. No intermediate product publication; final delivery is S5.
