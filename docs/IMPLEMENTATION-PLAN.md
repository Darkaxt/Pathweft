# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.1, 2026-10-28.

Current authorization: repository/environment/specification setup only. Product
implementation has not started. This plan neither authorizes itself nor adds
approval checkpoints. No implementation stage is ACTIVE.

## Stage map

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | NOT STARTED | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Side-by-side identity; real index query -> paged pane -> reveal/open; unavailable/unindexed modes; browsing/UI regression evidence |
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
| S1 | None | All assigned criteria | No execution blocker asserted before work starts | None |
| S2 | None | All assigned criteria | S1 prerequisite not complete | None |
| S3 | None | All assigned criteria | S2 prerequisite not complete | None |
| S4 | None | All assigned criteria including REA | S3 prerequisite not complete; target identity not selected | None |
| S5 | None | All assigned criteria including final release | S1..S4 prerequisites not complete | None |

All statuses are NOT STARTED, not parked BLOCKED stages. Future unimplemented
requirements are planned work, not deferrals of setup acceptance.

During implementation, update this ledger at meaningful integrated-feature/stage
checkpoints. Record concrete evidence and exact blockers/deferrals. At closure,
assess actual debt within the changed scope without inventing infrastructure or
consolidation stages. No intermediate product publication; final delivery is S5.
