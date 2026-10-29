# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.5, 2026-10-29.

Current authorization: S4U implementation/verification, the approved read-only
icon treatment and Ctrl+H/dot-name visibility. Earlier S3 work was requested on
2026-10-29 after S2 completion. The user also added the F3D prototype and a separate
archive-browsing/editing stage. On 2026-10-29 the user added native StartAllBack
search integration and the long-term all-Everything/Windows Search retirement
goal, then authorized proceeding to the next stage after plan review.
S1-S4 and S4R are COMPLETE; S5-S8 remain NOT STARTED.
The user authorized trying rars on 2026-10-29 and delegated its integration
choice. The user approved read-only icon cues and Ctrl+H/dot-name visibility;
S4U is ACTIVE, the sole active stage. A wider Rust port is not in scope.
Native source/SDK integration is preferred. This
plan neither authorizes itself nor adds approval checkpoints.

## Stage map

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | COMPLETE | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Real index/search/coverage, isolation, rendered UI, browsing, physical OLE drag and OS clipboard copy/paste/restoration pass; see S1 evidence |
| S2 - Connected taskbar workflow | COMPLETE | WIDGET-01..03, affected CORE-02/03, LIFE-01/DELIVERY-03 | S1 COMPLETE | Real widget query/handoff/reveal, shared actions/queue, actual native hotkey/typing before and after Explorer restart, hidden real mixed-DPI transitions and inspected File Labs layouts; see S2 evidence |
| S3 - Internal selectable previews | COMPLETE | PREVIEW-01..05, affected CORE-03, LIFE-01/DELIVERY-03 | S2 COMPLETE | Selected source/native inventory; complete real preview/toggle/file-release workflow with QuickLook absent; separate-process persistence; F3D model/camera/native teardown; hidden rendered acceptance and source self-test; see S3 evidence |
| S4 - Native archive browsing and editing | COMPLETE | ARCHIVE-01..05, affected CORE-02/03, PREVIEW-04, LIFE-01/DELIVERY-03 | S3 COMPLETE | Pinned native library/source/terms, real pane/clipboard/edit/open/preview workflows, source/destination volume ownership, preservation and actual consumer cleanup verified; see S4 evidence |
| S4R - Embedded writable RAR | COMPLETE | RAR-01..05, affected ARCHIVE-02..05/CORE/LIFE/DELIVERY-03 | S4 COMPLETE | Pinned loaded Rust DLL, real hidden pane/actions/queue, qualified solid/encrypted preservation and refusals, independent 7-Zip/UnRAR extraction, original/cut-source protection, clipboard rollback, cleanup and source commit/remote readback; see S4R evidence |
| S4U - Read-only cues and hidden visibility | ACTIVE | FILEUI-01..03, affected CORE/SEARCH/ARCHIVE/LIFE | S4R COMPLETE; user-approved icon direction | Real hidden browser/widget/tree/archives and native index workflow, persisted global toggle, grayscale/lock rendering, preservation and focused regressions |
| S5 - REA-driven transfer improvements | NOT STARTED | TRANSFER-01..06, archive/caller and WIDGET-02 revalidation, affected CORE-02/03, LIFE-01/DELIVERY-03 | S4U COMPLETE; identify TeraCopy artifact before analysis | Required REA Evidence -> adoption table -> integrated transfer behavior; data/failure/recovery checks and comparable measured workloads |
| S6 - Native StartAllBack search | NOT STARTED | SAB-01..05, affected SEARCH/CORE/LIFE/DELIVERY criteria | S5 COMPLETE; exact host identified before hook selection | REA evidence -> version-bound native adapter -> actual Everything results inside the existing Start menu; preserved app/settings actions, lifecycle/compatibility/disable/recovery |
| S7 - Everything everywhere and WSearch retirement | NOT STARTED | EVERYWHERE-01..05, affected SEARCH/CORE/LIFE/SHELL criteria | S6 COMPLETE; finite consumer/semantics inventory before migration | Actual per-surface Everything replacement, resolved content/property/mail dependencies, isolated WSearch-off workflows, reversible service Disabled state and reboot/recovery verification |
| S8 - Final integration and delivery | NOT STARTED | SHELL-01, DELIVERY-01..03; reconcile every product requirement | S1..S7 COMPLETE; prerequisite blockers/deferrals resolved | Reversible optional shell integration; all delivered search/migration/recovery boundaries; install/update/uninstall; final criteria review, source verification/commit, normal installer/portable release independently retrieved and verified |

ENV-01..04 belong to the current setup deliverable, not an artificial product
implementation stage. See [setup evidence](verification/2026-10-28-setup.md).

## Complete requirement ownership

| Requirements | Owner | Later boundary |
| --- | --- | --- |
| ENV-01..04 | Setup | Committed/remote document and head readback at setup delivery |
| CORE-01 | S1 | Installer/update isolation S8 |
| CORE-02 | S1, affected actions S2/S4/S5/S6/S7 | Integrated package S8 |
| CORE-03 | Each changed interface in S1..S7 | Full changed-interface review S8 |
| SEARCH-01..04 | S1 | Widget S2, native adapters S6/S7, packaged engine S8 |
| WIDGET-01..03 | S2 | Preview S3, archive S4, transfer S5, package S8 |
| PREVIEW-01..05 | S3; archive paths S4 | Packaged dependencies S8 |
| ARCHIVE-01..05 | S4 | Transfer revalidation S5; final package S8 |
| RAR-01..05 | S4R | Transfer revalidation S5; final package/source/license/install S8 |
| FILEUI-01..03 | S4U | Delivered icon/visibility/settings package revalidation S8 |
| TRANSFER-01..06 | S5 | Final package S8 |
| SAB-01..05 | S6 | WSearch-off behavior S7; packaged adapter S8 |
| EVERYWHERE-01..05 | S7 | Delivered migration/compatibility/recovery S8 |
| LIFE-01 | Each implementing stage | Final lifecycle reconciliation S8 |
| SHELL-01 | S7 for search migration, S8 for file-manager shell integration | Actual disable/uninstall readback |
| DELIVERY-01..02 | S8 | Release retrieval/verification |
| DELIVERY-03 | Each import stage; final S8 | Public source/asset review |

Later packaging verification checks a different boundary; it does not defer
required current-stage wiring or real behavior.

## S4U execution and reconciliation

S4U is ACTIVE after S4R and before S5, owning FILEUI-01..03 and affected CORE,
SEARCH, ARCHIVE and LIFE criteria. Finish one integrated outcome: read-only
presentation plus consistent persisted hidden visibility across existing callers.
Actual hidden WPF actions/layouts, native index visibility/counts, archive states,
grayscale/cache/live attributes, widget/tree/settings and preservation checks pass;
see [S4U evidence](verification/2026-10-29-s4u.md). Current acceptance has no
implementation gap. Exact scratch cleanup is verified; source commit/remote
readback is the remaining internal closure blocker. No external blocker or
tracked deferral. Closure assessment found no material consolidation requirement.
S5 cannot begin until S4U completes. Final package/release verification remains S8.
Scratch: `D:\Temp\pathweft-s4-ui` for native WPF/build small-file I/O;
`E:\Temp\pathweft-s4-ui-review` for preflight data. Retain only defined S8 evidence
in the project; helper-clean scratch after its actual last consumer exits.

## S4R execution and reconciliation

- Integrate the pinned rars crate through one narrow native C ABI bridge; keep
  virtual paths, reading/preview, verified publication and queue ownership in
  existing Pathweft code. Rust is the codec/rewrite boundary only.
- Use upstream preservation preflight; refuse unsupported layouts without data
  loss. Start with a real RAR creation/edit/independent extraction workflow and
  finish existing-archive solid/encryption and data-failure criteria before closure.
- RAR-01..04 and integrated hidden WPF behavior pass: pinned loaded DLL, eligible
  solid/encrypted edits, preservation refusals, real transfers, independent
  7-Zip/UnRAR decoding and failure/cancellation/source preservation. See
  [S4R evidence](verification/2026-10-29-s4r.md).
- RAR-05 passes: verification, exact cleanup and source delivery are complete.
  Source commit `3517b8aea1ceadd6955b07f327e96b37f5eaeccf` was pushed to
  `origin/codex/s4r-native-rar`; independent `ls-remote` readback matches.
  S4R has no remaining advancement blocker or tracked deferral. S5 has not started.
- Closure assessment found no material design debt requiring consolidation;
  native ownership and the existing queue/publication owners remain explicit.
- Native build/output scratch: `D:\Temp\pathweft-rars-native` (native compilation
  and sustained small-file I/O); diagnostics/review and small cross-volume fixture:
  `E:\Temp\pathweft-rars-review`. Both roots were helper-cleaned and absence verified.
- Persistent source/tool dependency cache: `D:\BuildCache\Pathweft\cargo` and
  `D:\BuildCache\Pathweft\rars\0.12.3-pathweft.1`, retained for current development
  and reproducible S8 packaging. Native source/bridge/lock/notices remain in Git.
- Final native packaging/delivery is owned by S8; this stage adds no release.

## S4 execution and design contract

1. Pin a native 7-Zip DLL/SDK and its managed interop source/dependency. Record
   actual read/write capabilities and terms; no standalone donor GUI or registry
   installation. Prove normal pane -> native ZIP/7z listing -> selected entry
   preview/open before broadening the remaining archive/edit cases.
2. Keep virtual paths in existing panes/tabs/breadcrumbs and shared actions.
   Route archive edits/transfers through the existing Ferry job owner, conflict
   controls and progress surface. Rename/copy/cut/paste must serve filesystem and
   archive sources/destinations; read-only formats expose honest capabilities.
3. Stage edits beside the destination, verify resulting contents, detect source
   changes and publish the replacement only after success. Cancellation/failure
   preserves originals; a cut deletes only verified unchanged source members.
   Reject ambiguous/traversal names and unsafe links before writing outside an
   archive. Exercise the real failures using disposable fixtures.
4. Entry-bound temporary extraction belongs to actual consumers: internal
   previews release on close/change; an external open is retained until its real
   consumer lifetime is established, not a launcher exit or delay. Passwords are
   ephemeral. Use the existing File Labs UI styling and hidden finite diagnostics;
   inspect 100%/150% layouts without introducing another design system.
5. Verify the complete integrated feature against ARCHIVE-01..05, retain required
   evidence for final delivery, clean task scratch with the installed helper,
   assess required debt once, commit/push/read back. No S5 work or product release.

## S6/S7 integration direction and open feasibility

User outcome: use Everything in the existing search locations, ultimately with
Windows Search disabled. StartAllBack is the first new native host. A redirected
hotkey/popup is not acceptance for its existing menu. No shell/service mutation
or REA target execution occurred during this plan review.

Read-only installed-file metadata reported StartAllBack 3.9.25. This is not a
hash-bound analysis or proof of an available hook. S6 begins by pinning that
actual artifact and Windows dependencies and using gated REA to trace the query
and result boundary. Select the smallest native adapter supported by the evidence;
preserve host UI, application/settings commands and original files. Verify one
complete in-menu query/result/open workflow before broadening required cases.
Then finish version rejection, cancellation, backend loss and attach/detach/
Explorer lifecycle before S6 closes. No activation/licensing changes.

Current primary-source evidence and its limits:

- [StartAllBack's official feature page](https://www.startallback.com/) describes
  its restored Start menu/search; it does not establish a replacement-provider
  SDK or usable native hook. No such API has been verified in this review.
- [EverythingToolbar's FAQ](https://github.com/srwi/EverythingToolbar/blob/develop/FAQ.md)
  describes a separate SDK-driven search UI and taskbar modes. Compatibility with
  StartAllBack is not proof of integration into its own results list.
- [Everything's SDK](https://www.voidtools.com/en-us/support/everything/sdk/)
  supplies its own IPC query interface. Replacing Windows Search consumers is
  additional integration work; this interface is not evidence of ABI compatibility.
- [Everything's content options](https://www.voidtools.com/en-us/support/everything/options/)
  document 1.5 content indexing and the `si:` Windows-index query. Neither proves
  mail-store search parity; a Windows-index dependency cannot satisfy WSearch-off
  acceptance. S7 must qualify the actual selected engine/version and resource cost.
- [Microsoft's Outlook search guidance](https://support.microsoft.com/en-us/outlook/troubleshooting-outlook-search-issues)
  explicitly checks the Windows Search service for classic Outlook. The actual
  installed client's account/offline/search behavior must be inventoried, rather
  than assuming either complete dependence or an unaffected cloud alternative.

S7 starts from a concrete consumer/semantic matrix, not a system-wide hook that
claims all applications. Cover native file search, dialogs and non-file consumers
under EVERYWHERE-01..03; keep genuine unsupported contracts explicit. No alternate
engine or exclusion is authorized merely because a consumer is difficult. Prove
each required workflow with WSearch absent in an isolated environment, then apply
and verify the reversible service-retirement outcome under its actual stage
authorization. Record service startup/running state and recovery before mutation;
verify reboot, unavailable Everything, unsupported updates, uninstall and recovery.
Physical shell trials are coordinated; hidden diagnostics remain the default.

This is a specified long-term target with unresolved feasibility, not a claim
that Everything already replaces all Windows Search semantics. A requirement
that cannot be met remains a blocker with an exact resolution condition. Do not
silently change the goal to filename-only search or leave a hidden WSearch owner.

## Required S5 sequence

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

This is future S5 work; it does not begin during S3 or the archive stage.

## Current reconciliation ledger

| Stage | Satisfied product criteria | Remaining | Dependency / blocker state | Tracked deferrals |
| --- | --- | --- | --- | --- |
| S1 | All assigned S1 criteria: CORE-01 identity/side-by-side/handoff; CORE-02 navigation/tabs/keyboard/large listing/copy, routed drop, physical source -> OLE -> destination transfer, actual OS clipboard copy/paste, independent process readback and prior-text restoration; CORE-03 rendered controls/settings; SEARCH-01..04 real index/paging/sort/cancellation/coverage/virtual origins; applicable LIFE-01/DELIVERY-03. See [evidence](verification/2026-10-28-s1.md). | None within S1 | None | None |
| S2 | All assigned WIDGET-01..03 and affected CORE-02/03, LIFE-01/DELIVERY-03: complete real query/handoff/reveal/file-action workflow; selected hotkey mode and native input before/after real Explorer restart; original Explorer location restored and Everything processes preserved; actual 96/144-DPI transitions; inspected 100%/150% text layouts; final hidden workflow recorded zero foreground events; Debug/Release builds and source self-test passed. See [evidence](verification/2026-10-29-s2.md). | None within S2 | None | None |
| S3 | All PREVIEW-01..05 and affected CORE-03/LIFE-01/DELIVERY-03: eight internal modules, actual decoding/paging, all active toggles and separate-process persistence; independent widget/Space/side selection; malformed/large/unsupported/inert inputs; actual file release; F3D real model/camera/native teardown and unchanged original; complete final QuickLook-absent run and verified restoration, zero foreground events; Debug/Release builds and Release source self-test. See [evidence](verification/2026-10-29-s3.md). | None within S3 | None | None |
| S4 | All ARCHIVE-01..05 and affected criteria: final integrated PID32156, actual native ZIP/7z/TAR/CAB/RAR5, normal archive-file handling, real clipboard/conflicts/rename/copy/cut/paste/Compress/Extract, cancellation/concurrency and D/E source-volume reservation, actual viewer/consumer release, read-only/encrypted/unsafe states, inspected 100%/150% layouts, zero foreground events and independent clipboard restoration; Debug/Release, archive contracts and updated source self-test pass. See [S4 evidence](verification/2026-10-29-s4.md). | None within S4 | None | None |
| S5 | None | All assigned criteria including REA | S4 prerequisite complete; TeraCopy target identity/evidence not selected yet | None |
| S6 | None | SAB-01..05 | S5 prerequisite not complete. Native hook/ABI feasibility is unresolved, not a verified implementation. | None |
| S7 | None | EVERYWHERE-01..05 | S6 prerequisite not complete. Consumer inventory and non-file semantic compatibility unresolved; these must close before service retirement. | None |
| S8 | None | All assigned criteria including final release | S1..S7 prerequisites not complete | None |

S1 and S2 are COMPLETE. The user confirmed normal unlock; real physical drag and
clipboard checks subsequently passed. A separate STA process read the Windows
file list/copy effect, paste delivered matching contents through the existing
queue, and an independent post-exit read confirmed prior-text restoration.
S2 passed following the user's next-stage request. S3 passed after the next
2026-10-29 request and its user-added F3D prototype. S4 is COMPLETE following the
user's instruction to continue with the next stage after the search-plan update.
S5-S8 remain NOT STARTED; no later stage is ACTIVE.
S1-S4 have no remaining blocker or tracked deferral. Installer/update/
uninstall and packaged integration retain their explicitly assigned S8 boundary.

Source review found one SDK dispatcher/queue owner and independent pane query
generations, with no new timeout-driven search path or external helper product.
At S1 closure, clipboard operations used the already-referenced WinForms API after
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
exception. All owned processes exited and required final-package comparison evidence was
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
consolidation stages. No intermediate product publication; final delivery is S8.

The 2026-10-29 archive-stage addition shifted REA transfers from S4 to S5 and final
delivery from S5 to S6. The subsequent StartAllBack and all-Everything migration
requests added current S6/S7 and shifted final delivery to S8. Earlier dated
verification documents retain their original stage labels; the current
requirement/stage ownership above is authoritative.

At S3 closure, one settings owner and per-surface generation/lifecycle host serve
the fixed eight-module inventory; F3D's queue exists for actual OpenGL thread
affinity. No duplicate action owner, dynamic plugin infrastructure or required
consolidation was identified. The final real workflow passed with QuickLook
absent and restored its configuration unchanged. See S3 evidence for the bounded
prototype/codec limits and diagnostic corrections; no acceptance was weakened.

S3 scratch `D:\Temp\pathweft-s3-native` used the native/build I/O exception.
Required private evidence was retained for final packaging comparison before cleanup. Transaction
`a9ce1f29c4b59e39ce0bfa27f3176eed` inspected all 8,286 frozen members and removed
8,285 ordinary candidates (1,055,048,193 helper-reported file bytes). The sole
excluded self-test junction was independently reviewed and removed as an exact
leaf in transaction `806a28e9dfd03055c51e58306c1f7115`, without traversing its
target. Both transactions are applied; independent readback confirms the scratch
root is absent. Persistent pinned build dependencies remain ongoing consumers.

## S3 execution and design contract

1. Port the pinned QuickLook viewer contract, context/lifecycle and selected
   viewer source into fixed, built-in modules. Keep a per-surface generation and
   cancellation owner; initialize only the selected enabled module. Use a stable
   documented priority for Markdown versus text and all other overlaps.
2. Integrate text/code, native Markdown, image, PDF and archive listing; preserve
   media/audio and font previews as selectable modules using existing renderers.
   Record exact formats, dependency/source pins, limits and applicable GPL source
   materials. Replace standalone QuickLook dispatch and unsafe shell-preview
   fallback in the governed preview path; these cannot bypass disabled modules.
3. Reuse the existing side preview and Space window; add widget Preview/Space
   through the same host. Changes of selection/settings/visibility cancel obsolete
   work and release content/resources. Use actual completion for loads/cleanup;
   retain display-only playback position updates where needed, not settling timers.
4. Put module toggles in the existing Settings window using its dark cards, Segoe
   UI and blue accent. Preview window defaults to 1000x720 DIP; side pane retains
   its 200-DIP minimum; controls wrap/scroll at narrow widths and 100%/150% text.
   Native Markdown is read-only; no embedded HTML, scripts, macro handlers or
   automatic link/network navigation. Inspect real WPF renders at these sizes.
   Huashu is reference guidance within the established File Labs design system.
5. Verify complete selected-file -> preview workflows from Space, side pane and
   widget with standalone QuickLook absent. Exercise persisted toggles/restart,
   active-disable, priority, rapid changes, malformed/large/unsupported inputs,
   playback release and post-close rename/delete. Reuse hidden trial presentation,
   collect readiness/rendered evidence, reconcile, clean scratch and commit/push.
   S3 does not publish an intermediate product release or begin S4.
6. User addition on 2026-10-29: PREVIEW-05 includes an opt-in F3D-backed GLB
   prototype, using source/API inspection and a real user-selected model held
   read-only. Pin the F3D source/native dependency; integrate camera controls,
   settings and actual resource cleanup into the same host. Generic source/UI
   diagnostics leave installed QuickLook untouched; its absence is a separate
   final dependency verification boundary.

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
