# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 0.19, 2026-10-31,
including the user-authorized continued comparison requirement TRANSFER-07.

S8 is COMPLETE through1.1.4: CORE-09/10, PREVIEW-06/07 and About are verified in
source and the final Release package, normal publication, independent retrieval,
installation and exact cleanup. No stage is ACTIVE. The release source is
8552fbdc4a0c38acf7cbb76d98d1ef3d227b6063; documentation closure leaves its tag
unchanged. User settings and folder/startup registrations are preserved.
Current authorization also includes core file-explorer integration and delivery, explicitly
reprioritized by the user ahead of S6/S7 on 2026-10-30. Keep shell integration
opt-in in its Windows integration settings section; selectively adopt relevant
upstream changes before compilation; add an alternate Native theme/accent controls
under Appearance using Huashu and the supplied Explorer screenshot as references.
S6 is parked by this user-directed execution boundary, not technically complete;
S7 stays unstarted. Their requirements remain later program work, outside this
core release. Final current-source delivery passes, including actual downloaded
consumer PID4672, installed-member/app-host checks and removed task roots.
Prior S8 is COMPLETE through v1.1.3, normally published at source9ed2de0,
independently retrieved and installed with all member hashes and preserved user
settings/routing. Native OLE custom-format/CF_HDROP consumer checks also pass.
The user verified Explorer subfolder navigation stays in Explorer. Exact old
large-archive physical dragging was not replayed. Prior task scratch is cleaned.
No required S8 blocker or tracked deferral remains.
Historical authorization follows.
S6 native StartAllBack search was authorized after S5 closure.
Earlier S5 REA-driven transfer implementation was requested after
S4U closure. Earlier S3 work was requested on
2026-10-29 after S2 completion. The user also added the F3D prototype and a separate
archive-browsing/editing stage. On 2026-10-29 the user added native StartAllBack
search integration and the long-term all-Everything/Windows Search retirement
goal, then revised S7 to retain WSearch for Outlook while reducing filesystem
indexing overlap. This authorized specification change does not alter live settings.
On 2026-10-30 the user clarified bounded Everything-backed results per existing
StartAllBack category plus See more results into the full integrated search view,
and requested Pathweft folder routing with Directory Opus as the Explorer
replacement reference. Compact results must not depend on WSearch filesystem
indexing. Programs/Settings independence remains to be qualified, not assumed.
S1-S5, S4R and S4U are COMPLETE; S6 is BLOCKED by the user's reprioritization;
S7 is NOT STARTED; S8 is COMPLETE through the bounded v1.1.4 correction.
The user authorized trying rars on 2026-10-29 and delegated its integration
choice. The user approved read-only icon cues and Ctrl+H/dot-name visibility;
Historical S6 investigation following the user's reboot: the guest Windows kernel/build and
relevant Shell DLL bytes match the host. Following the user's update clarification,
current native qualification targets signed StartAllBack 3.9.28 rather than holding
the guest on the host's 3.9.25. Earlier 3.9.25 evidence remains version-specific;
updated private interfaces/offsets require qualification. There was no Windows
downgrade. Native query/provider contracts remain under investigation.
Programs/Settings baseline results and launches on 3.9.25 were observed with
WSearch stopped, including real guest Win-key/typing input; running-service and
indexing-exclusion comparisons remain unverified. See
[current S6 evidence](verification/2026-10-30-s6.md). A wider Rust port is not in scope.
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
| S4U - Read-only cues and hidden visibility | COMPLETE | FILEUI-01..03, affected CORE/SEARCH/ARCHIVE/LIFE | S4R COMPLETE; user-approved icon direction | Real hidden browser/widget/tree/archives and native index workflow, persisted global toggle, grayscale/lock rendering, preservation, focused regressions, exact cleanup and source commit/remote readback; see S4U evidence |
| S5 - REA-driven transfer improvements | COMPLETE | TRANSFER-01..07, archive/caller and WIDGET-02 revalidation, affected CORE-02/03, LIFE-01/DELIVERY-03 | Initial S5 criteria passed at e478986; user extended comparison/implementation | Finite findings inventory resolved; source/target handle reuse, copy-time hashing, metadata/EOF reductions, storage decisions, matched profiles, actual callers, adverse/preservation/cleanup and Release checks pass; see [evidence](verification/2026-10-29-s5.md) |
| S6 - Native StartAllBack search | BLOCKED | SAB-01..06, affected SEARCH/CORE/LIFE/DELIVERY criteria | Parked by explicit user-directed S8-first execution; resume only after reprioritization is lifted | Investigation only; no working patch or complete SAB criterion. All native groups/handoff/provider/lifecycle/routing criteria remain open. |
| S7 - Everything filesystem search and Outlook indexing scope | NOT STARTED | EVERYWHERE-01..05, affected SEARCH/CORE/LIFE/SHELL criteria | S6 COMPLETE; finite consumer/semantics/scope inventory before migration | Native Everything file results with selected filesystem roots excluded from Windows indexing; WSearch/Outlook retained, explicit Windows-managed exceptions, local/mail/reboot/recovery verification |
| S8 - Core integration and delivery | COMPLETE | SHELL-01, CORE-03..10, PREVIEW-06/07, affected SEARCH/WIDGET/ARCHIVE, DELIVERY-01..03 | Prior S8 through v1.1.3 and user-authorized additions | v1.1.4 source/rendered/native/package checks, normal six-asset release, independent retrieval, installed verification and exact cleanup pass. S6/S7 remain parked. |

ENV-01..04 belong to the current setup deliverable, not an artificial product
implementation stage. See [setup evidence](verification/2026-10-28-setup.md).

## Complete requirement ownership

### Current S8 compact-popup design and verification contract

The user's reference supersedes the historical S2 destination/action-bar design.
Use the main window's theme background, existing palette/accent and Segoe UI;
no independent toolbar theme. Default 720x520 DIP, minimum 520x320, with 16-DIP
outer inset and a 38-DIP rounded query bar. Keep four accessible match switches;
hide the native caption and all file-browser/action/footer rows. Plain result
rows show icon/name, muted parent location, size and modified date, with theme
selection/focus states. Columns fit the viewport; long values ellipsize and retain
the existing full-path tooltip. Text scaling grows the popup, monitor work-area
constraints and DPI handling remain owned by the existing window.

The complete source workflow is recorded privately in the original-process
`ui112-compact-widget-verification.txt` receipt and rendered snapshots: actual
global index queries and cancellation, option semantics, missing-index error,
scroll-driven paging, file-open boundary without launching another user app,
folder/ZIP opening in the main manager, Ctrl+Enter command, Escape, 100%/150%
minimum/normal sizes and Native/Original theme review. Existing native hotkey
registration is unchanged; these hidden actions do not claim a fresh physical
hotkey/Explorer-restart test. Restore-action red/green receipts measure the actual
final character within the actual button at 440/660 DIP and both text scales.
WIDGET-01/02 and CORE-03 acceptance require these workflows and final 1.1.2
delivery verification; no required deferrals or new infrastructure are added.

Post-release stage addition authorized 2026-10-31:

| Stage | Status | Requirements | Acceptance / execution boundary |
| --- | --- | --- | --- |
| S9 - Context menu reorganization | NOT STARTED | MENU-01..02, affected CORE-03/04 and archive/file actions | Inventory available actions and eligibility, group/prioritize existing commands, preserve native-menu choice and shortcuts, verify real commands/keyboard/layout/themes and final delivery. Begin only after the active S8 correction closes; S6/S7 remain parked under the current priority. No menu implementation is included in v1.1.1. |
| S11 - Native pinned/recent folder Jump Lists | NOT STARTED | JUMP-01..02, affected CORE-01/02 and LIFE/DELIVERY | Native expandable Start-menu/taskbar folder destinations with pin/unpin/recent/removal/privacy and direct Pathweft cold/warm launches; useful tasks, stable application identity and update/uninstall preservation. Full folder-destination behavior must pass; Opus's Tasks-only example is not proof. Plan only during S8; no S6/Sandbox work. Stage number follows the user's explicit request. |

| Requirements | Owner | Later boundary |
| --- | --- | --- |
| ENV-01..04 | Setup | Committed/remote document and head readback at setup delivery |
| CORE-01 | S1 | Installer/update isolation S8 |
| CORE-02 | S1, affected actions S2/S4/S5/S6/S7 | Integrated package S8 |
| CORE-03 | Each changed interface in S1..S7 | Full changed-interface review S8 |
| CORE-04..05 | S8 | Native theme/accent, upstream adoption and delivered regression verification |
| SEARCH-01..04 | S1 | Widget S2, native adapters S6/S7, packaged engine S8 |
| WIDGET-01..03 | S2 | Preview S3, archive S4, transfer S5, package S8 |
| PREVIEW-01..05 | S3; archive paths S4 | Packaged dependencies S8 |
| PREVIEW-06/07 | S8 | Archive tree, full embedded F3D interaction/reader support and final delivery |
| CORE-09 | S8 | This PC capacity/network/connection details and final delivery |
| CORE-10 | S8 | Dynamic populated detail columns and final delivery |
| ARCHIVE-01..05 | S4 | Transfer revalidation S5; final package S8 |
| RAR-01..05 | S4R | Transfer revalidation S5; final package/source/license/install S8 |
| FILEUI-01..03 | S4U | Delivered icon/visibility/settings package revalidation S8 |
| TRANSFER-01..07 | S5 | Final package S8 |
| SAB-01..06 | S6 | Reduced filesystem-index scope behavior S7; packaged adapter S8 |
| EVERYWHERE-01..05 | S7 | Delivered migration/compatibility/recovery S8 |
| LIFE-01 | Each implementing stage | Final lifecycle reconciliation S8 |
| SHELL-01 | S7 for search migration, S8 for file-manager shell integration | Actual disable/uninstall readback |
| DELIVERY-01..02 | S8 | Release retrieval/verification |
| DELIVERY-03 | Each import stage; final S8 | Public source/asset review |

Later packaging verification checks a different boundary; it does not defer
required current-stage wiring or real behavior.

## S5 execution and reconciliation

Initial S5 criteria passed at e478986. The user's continued comparison/implementation
goal reopened S5, adding TRANSFER-07; the extended stage is now COMPLETE. TRANSFER-01..06
are reconciled to the user's explicit TeraCopy-behavior clarification:
direct writes, no long-lived source locks, and cancellation cleanup of the current
incomplete/unverified output. Former staging/locking assertions are historical
diagnostics, not acceptance for this revised behavior. Extra protections are not
required deferrals. The integrated revised path and affected verification pass;
no later stage is active. The earlier closure evidence below remains historical;
subsequent changes were revalidated before this renewed closure.

| Pipeline finding | Current evidence / action | State |
| --- | --- | --- |
| Repeated volume discovery | Implemented direct stream enumeration with documented unsupported-filesystem result, without a cache. Matched profile: 201 capability queries to 0. Native NTFS/ADS/cancellation/error contracts and real FAT32 refusal/rounding/disk-full pass; VHD detached. | Implemented and verified; no remaining candidate |
| Repeated timestamp opens | Batch three times through SetFileTime. Plain verified copies reuse the target reader with FILE_WRITE_ATTRIBUTES. For 100 files and one directory, explicit timestamp-only opens are now one instead of the initial 303. File/folder creation/access/write checks and current FAT32 precision reporting pass. | Implemented and verified; native/unverified paths retain their existing metadata handling |
| Source hashing reread | Single-stream data, including compressed/sparse contents, hashes from the pooled 1 MiB copy buffer and rereads only the target. The ordinary target digest now reads its planned length, detects premature EOF and uses the existing final metadata stamp to reject growth. Native CopyFileEx/named-stream digests retain EOF behavior. Current source/link/target-length, copying, cancellation and actual callers pass. | Implemented and verified; 100 target reads instead of 200 for 100 small files, and 12 instead of 14 for the storage pair |
| Native metadata / repeated path validation | Removed duplicate read-only scan ancestry checks and already-matching attribute setters. Source stamp validation reuses its normally shared no-follow reader before target creation; verified target timestamps reuse the target reader. Current profile: 0 attribute writes, 907 native opens/closes and 1,103 information queries, versus 1,107 opens/closes before both handle changes. Final path-based readback and required identity/streams/reparse checks remain. | Implemented and verified without replacing removed opens with queries; no remaining candidate |
| Scheduling / buffers / workload sensitivity | Repeated small/large/mixed and serial controls pass independent contents. Current 512 MiB D: and mixed D: to E: workflows pass after the final reader changes. Qualified donor/current payload requests are already 1 MiB. Keep four workers/buffer size; explicitly retain the buffered/unbuffered verification difference and reject unsupported tuning/aligned-I/O expansion. Storage prototypes have documented non-adoption decisions. | Comparisons and adoption/rejection decisions closed; no universal timing or physical-media claim |
| Conflict / progress / pause / failure / recovery | Current native contracts and actual hidden pane/widget/archive controls pass, including partial/failed outcomes, direct overwrites, cleanup, source sharing and no foreground events. Abrupt child exit preserves the source and fresh resubmission verifies the whole copy. Real FAT32 disk-full removes the partial output, retains the source and detaches its VHD. Release self-test/audit pass without test hooks or donor assets. | Affected workflows verified for this iteration; revalidate relevant paths after further pipeline changes |

The user clarified that deterministic support-operation reduction takes priority
over noisy timing. Use matched scoped operation counts and correctness as primary
evidence; keep repeated timings as secondary observations, not a stable speedup
gate or a reason to run endless samples.

Earlier goal iterations implemented and verified copy-time hashing and redundant
attribute-write removal, followed by source/target handle and EOF reductions.
Exact receipts and authenticated REA observations are retained. On 2026-10-29
the user left and requested delaying additional administrator simulations. The
already accepted FAT32 test finished and detached; administrator simulations were
delayed until the user returned. The user subsequently authorized the affected
FAT32 checks, which completed and detached their disposable disks.

The next iteration qualified native storage behavior. The donor normalizes
compression/sparse allocation instead of preserving the flags; the former
Pathweft blanket failure was not donor behavior. The specification/adoption
decision now reports these filesystem limitations while preserving required
contents/basic metadata and encryption constraints. Ordinary buffered copying
also removes the extra source hash pass for these inputs. CopyFile2 sparse and
callback-compression prototypes were experimentally rejected as additional
storage behavior outside the selected workflow, not deferred requirements.
Current real hidden notes use the existing neutral theme, wrap and expose full
detail; actual errors retain their red treatment. The source-handle/EOF iteration removes those redundant
operations while preserving the full transfer contract. Current hidden callers,
Release self-test/audit and the real FAT32 full-destination check pass; the
disposable disk is detached. The user returned and authorized this administrator
check after the earlier delay. Applicable volume/buffer reconciliation and the
final target metadata-handle reduction are now verified. The finite TRANSFER-07
inventory has no unresolved implementation candidate, blocker or tracked deferral.
Remaining identity/stream/path/readback checks serve existing acceptance criteria;
no equivalent cheaper replacement was established by the comparison. Closure
assessment found no material consolidation requirement. All original native/admin
operations completed, VHDs detached, evidence was retained and exact scratch was
transactionally removed. No intermediate product release; S8 owns delivery.

The [adoption contract](S5-TRANSFER-ADOPTION.md) and
[verification record](verification/2026-10-29-s5.md) reconcile TRANSFER-01..07.
The exact signed official 4.0.5 reference is qualified; scoped REA evidence is
retained/authenticated and every owned analysis/gate/trial has exited. Packed
static code did not reveal its algorithm. No donor binary becomes a dependency.
The unrelated installed antivirus-blocked module was not retried, and installed
settings/history/activation and protection remain unchanged.

The revised direct path passes native preservation/adverse contracts, actual
hidden pane/widget/archive actions, Resume/Cancel controls and 100%/150% card
review with zero foreground acquisitions. Real FAT32 error 112, ADS refusal and
timestamp rounding are verified, followed by actual VHD detachment. Actual crash
testing preserves sources and verifies fresh full resubmission; no automatic
restart/offset recovery is claimed. Previous staging/locking results are historical.

Equivalent inherited/TeraCopy/current small/large samples are retained with
wall/CPU/peak working set, cache/verification/storage and native-exit boundaries.
TeraCopy remains faster for 3,000 small files (4,025 ms versus 5,366 ms); the direct
engine is faster in this 512 MiB sample (1,262 ms versus 2,789 ms). These are single
warm-cache measurements with the same presentation instrumentation, not a general
speed promise. Separate I/O profiles show excess per-file opens, repeated volume
queries and timestamp updates; future optional optimization is not a required
deferral. No donor internal algorithm is inferred as recovered source.

Final Release build/self-test and the actual Release assembly/assets audit pass;
diagnostic hooks and donor dependencies are absent. Required receipts/captures
are hash-verified in persistent private evidence. Final source reconciliation and
exact transactional scratch cleanup pass. All 43,732 reviewed members were removed
(4,446,987,153 non-directory bytes), with no residuals in the two exact task roots.
Read-only test attributes on two retained cancellation inputs were restored before
resuming the same ticket; no alternate deletion mechanism was used. There is no
remaining S5 advancement blocker or tracked deferral. Verified source synchronization
accompanies stage delivery; final product packaging/release remains S8.
S6 was not started at that S5 closure; it is now ACTIVE under the subsequent
user authorization. S7-S8 remain NOT STARTED. No intermediate product release.

New persistent analysis, reports and snapshots: `.local/research/teracopy-4.0.5/`
on D:, private because donor binaries/configuration are not public deliverables.
Native staging/build/benchmark scratch: `D:\Temp\pathweft-s5-native` (native and
sustained small-file I/O exception); preflight data: `E:\Temp\pathweft-s5-review`.
Use exact helper cleanup after last consumers, preserve shared REA runtime and
existing Skyrim/legacy E: bindings. New persistent destinations follow D: policy.

## S4U execution and reconciliation

S4U is COMPLETE after S4R and before S5, owning FILEUI-01..03 and affected CORE,
SEARCH, ARCHIVE and LIFE criteria. Finish one integrated outcome: read-only
presentation plus consistent persisted hidden visibility across existing callers.
Actual hidden WPF actions/layouts, native index visibility/counts, archive states,
grayscale/cache/live attributes, widget/tree/settings and preservation checks pass;
see [S4U evidence](verification/2026-10-29-s4u.md). Current acceptance has no
implementation gap. Exact scratch cleanup and source delivery are verified:
`a8a5d9aadb25d20c63da8c172d6cf3d85ee631f3` matches independent remote readback on
`origin/codex/s4u-file-visibility`. No blocker or tracked deferral remains in S4U.
Closure assessment found no material consolidation requirement. S5 had not
started at that closure. Final package/release verification remains S8.
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
  S4R has no remaining advancement blocker or tracked deferral. S5 had not started
  at that closure.
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

User outcome: use Everything for filesystem search in existing search locations,
retain WSearch for Outlook and reduce duplicate filesystem indexing. This replaces
the earlier service-disable target. StartAllBack is the first new native host.
A redirected hotkey/popup is not acceptance for its existing menu. No live-shell
hook or indexing-scope change has occurred.

Clarified interaction contract: keep the current headings and dividers, including
actual user/library scopes such as Documents, Games and Modding; show a bounded
Everything-backed subset per applicable group. Do not simply retain native
Windows Search file results. Preserve Programs and Settings as separately
qualified result sources. See more results opens the full Pathweft-owned
EverythingToolbar-derived view with the query/scope carried through. That expanded
view is additional to the compact native groups; it does not replace them.
StartAllBack folder links and folder-result actions should route directly to
Pathweft under SAB-06. S8 SHELL-01 owns broader system/default-handler registration.

Programs has plausible index-independent sources: Windows' per-user/common
Programs known folders and AppsFolder enumerate launchable entries. This does not
prove StartAllBack's installed Programs provider already uses them independently.
Settings pages have documented ms-settings URIs, but URI activation is not a
localized keyword-search catalogue. Qualify existing native providers first;
required app/Settings coverage remains blocked if their dependencies cannot be
preserved or replaced. Isolated diagnostics must distinguish excluded filesystem
indexing from a stopped service. A service-stopped test is a diagnostic condition,
not authorization to disable the host's Outlook-dependent WSearch service.
Sources: [Windows known folders](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid),
[Settings activation](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).

The installed signed StartAllBack 3.9.25 x64 artifact is now hash-bound in private
S6 evidence on Windows 25H2 build 26200.9711. Gated REA traces distinct search-box
and results windows, ExplorerBrowser creation, query dispatch and view teardown.
File-provider enumeration and cancellation ownership remain unresolved; no usable
replacement hook is established. Select the smallest native adapter supported by the evidence;
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
  mail-store search parity. The revised S7 retains native Outlook indexing;
  filesystem capabilities still require actual selected-engine/resource qualification.
- [Microsoft's Outlook search guidance](https://support.microsoft.com/en-us/outlook/troubleshooting-outlook-search-issues)
  explicitly checks the Windows Search service for classic Outlook. The actual
  installed client's account/offline/search behavior must be inventoried, rather
  than assuming either complete dependence or an unaffected cloud alternative.

S7 starts from a concrete consumer/semantic matrix, not a system-wide hook that
claims all applications. Cover native file search, dialogs and non-file consumers
under EVERYWHERE-01..03; keep genuine unsupported contracts explicit. No alternate
engine or exclusion is authorized merely because a consumer is difficult. Under
the user's revised S7 direction, keep WSearch and Outlook indexing, remove selected
filesystem locations from its scope, and prove native file queries use Everything.
Inventory local/server-assisted Outlook behavior and unavoidable Windows-managed
exceptions. Preserve scope/mode/service state and recovery before mutation;
verify actual local mail search, reboot, unavailable Everything, updates, uninstall
and rollback. Do not delete/rebuild the mail index by default or disable WSearch.
Physical shell trials are coordinated; hidden diagnostics remain the default.

The user authorized enabling Windows Sandbox with NoRestart on 2026-10-30. The
broker command succeeded and readback reports Enabled with restartNeeded=true.
No restart or Sandbox launch occurred. SAB-05's matching isolated runtime cannot
be considered available until the user reboots and the guest build is verified.
The static trace is retained and the owned native analysis has exited. The
remaining data-provider replacement/cancellation contracts need observation of
actual Shell objects in that guest before hook selection; static pseudocode does
not establish those private runtime contracts. S6 was parked at the reboot
dependency. On 2026-10-30 the user reported restarting the laptop; host boot time,
Windows build26200.9711 and both signed DLL hashes were freshly verified. S6 is
ACTIVE again: establish the matching guest identity/storage, then perform isolated
query/result/teardown probes. This does not justify implementing later stages or
a live-shell trial.

The first owned Sandbox is running. Its guest reports 24H2 build26100.9711,
while the host reports 25H2 build26200.9711. The signed searchfolder DLL matches
exactly; this is preliminary diagnostic evidence, not SAB-05 matching-runtime
acceptance. The publisher's exact StartAllBack3.9.25 reference installer was
obtained and its extracted main DLL matches the installed host hash; it has not
been installed in the guest or executed on the host. Resolve the Windows
enablement/build boundary before formal isolated integration acceptance.

At the user's request, evaluated and adopted pinned WinApp CLI0.7.1 plus its two
project-scoped Sandbox/UIA skills; see [evaluation](WINAPP-CLI.md). Read-only
guest window/tree inspection works through the native WSB bridge without host
input. WinApp's host agent transport requires networking, which remains disabled
in this guest. No network changes, host UI input, hook or product dependency was
introduced. This tooling result completes no SAB criterion and does not replace
the required private provider/cancellation runtime investigation.

This is a specified long-term target with unresolved feasibility, not a claim
that Everything already replaces all Windows Search semantics. A requirement
that cannot be met remains a blocker with an exact resolution condition. Do not
silently change the required file-search semantics or leave an undeclared
filesystem WSearch owner. Outlook's retained WSearch owner is explicit and authorized.

## StartAllBack design review and future candidates

Requested analysis, 2026-10-30. These are optional future candidates, not added
specification requirements, implementation stages or tracked deferrals. Priority
and effort below are engineering judgments; native feasibility remains conditional
on the S6 host/view boundary. The installed menu has not received a pixel-level or
physical keyboard audit, so this review does not assert visual or accessibility
defects in the user's current configuration.

Keep the native menu's compact keyboard-first launch workflow. The greatest
potential is a connected file workflow: identify a result, inspect it, then hand
the chosen action to Pathweft. Existing taskbar position/spacing/labels,
translucency, menu styles and context-menu styling are already product features;
reuse their controls before adding parallel styling. See the
[official feature list and change history](https://www.startallback.com/).

| Priority | Candidate | Useful behavior | Feasibility and limits |
| --- | --- | --- | --- |
| 1 | Preview the selected result | Space opens Pathweft's existing preview for a real file; Escape returns focus and selection to the same result. Reuse enabled viewer modules, including the qualified GLB prototype. | Medium. Reuses the existing preview host, but selection/PIDL resolution, key conflicts, menu dismissal and focus restoration need isolated native proof. Keep decoding outside Explorer. |
| 2 | Make similar filenames distinguishable | Show a readable parent path and file type; expose full paths without crowding the name. Clearly distinguish unavailable locations from an empty query. | Medium for metadata in supported view modes; high if a new owner-drawn row is necessary. Determine what the current Shell view already exposes before changing layout. Native provider/backend readiness is already required by S6, not a new feature. |
| 3 | Offer a few consistent result actions | Copy path, open containing location in Pathweft, open the location's terminal, and preview. Reuse existing Pathweft actions and native Shell verbs where applicable. | Low-to-medium once result identity is available; custom StartAllBack menu insertion remains conditional. Native open/reveal is already S6 scope. The installed search code filters rename/link verbs, so restoring every file-manager operation is not merely enabling an existing generic menu. |
| 4 | Reuse saved search scopes | Select a project/folder/type scope while retaining predictable app/settings launching. Explicit scopes should be visible and removable with the keyboard. | Medium-to-high. Query routing and app/file ranking need design beyond a text prefix. Reuse Pathweft's scope state; do not create another indexing or history owner. Global hidden-file behavior must stay consistent. |
| 5 | Align restrained theme details | Match Pathweft's dark surfaces, blue accent, selection, typography and spacing where StartAllBack's own styles allow it. Offer an opt-in coordinated preset rather than changing all Windows colors. | Low for existing settings/skin affordances; high for unsupported per-control repainting. Static code confirms native theme painting and Start_LargeSearchIcons. Actual contrast, focus indication, text scaling and monitor-DPI behavior need rendered review before any visual redesign. |
| 6 | Clarify file capabilities consistently | Reuse the approved grayscale plus lock treatment for read-only results, with an accessible reason. Keep unavailable files, unsupported archive edits, hidden items and administrator-required operations distinguishable. | Medium-to-high. Capability semantics can be reused, but native icon composition/cache invalidation must be qualified. Grayscale alone must not imply a disabled or unopenable item, and administrator requirements cannot be inferred from an icon or file attribute. |

The native evidence supports targeting Shell identities and existing view seams.
SearchResults instantiates CLSID_ExplorerBrowser, sets its site, navigates to a
search-folder PIDL and retrieves a private search-view interface. Microsoft's
[ExplorerBrowser contract](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)
separates browser, view and data source; its public methods can host Shell items,
but this does not make StartAllBack's private query/view interface a public SDK.
The most economical extension point may be the data source or a result action;
the exact hook must follow the S6 trace and isolated proof.

### Directory Opus Explorer replacement investigation

The user requested this reference for SHELL-01; this is investigation, not S8
implementation or a change to live defaults. Inherited Agent.cs currently moves
new CabinetWClass windows off screen, changes opacity, polls Shell registration
and selected items every 50 ms, then closes Explorer and hands the location to
Pathweft. Selection and focus also use elapsed-time heuristics. This confirms the
underlying lifecycle problem behind the user's concern; it is not a native
launch-boundary default-handler contract.

GPSoftware's developer explicitly identifies a native ShellExecuteHook as part of
Opus's replacement mechanism in a
[2024 support reply](https://resource.dopus.com/t/win11-cant-start-dopus-123synccloud-123cloudstorage/52311/7).
Its developers also document separate coverage of ShellExecute explore requests
and SHOpenFolderAndSelectItems, tested from both 32-bit and 64-bit callers:
[developer diagnostic discussion](https://resource.dopus.com/t/visual-studio-open-folder-in-file-explorer/15274).
Read-only local inspection found Opus version metadata 13.23.0.0 and the cited
CLSID registered to dopuslib.dll with ThreadingModel Both, plus its ShellExecuteHooks
entry. Registration alone does not prove that replacement is currently enabled
or disclose how its selection payload is conveyed. No Opus setting was changed
and no binary was executed. The installed executable's Authenticode check does
not validate, so it is not a qualified signed reference for subsequent runtime
adoption; establish an independently verified exact reference before that investigation.

The replication target is equivalent native folder/reveal routing and explicit
coverage, not a claim that every application invocation is intercepted. A default
folder verb alone does not establish folder-plus-selection handling. Retain
selection, Unicode paths, caller architecture, cold/warm IPC, lifecycle and actual
prior registration restoration as acceptance boundaries. Do not copy a legacy
registration recipe without qualifying it on the target Windows build.
GPSoftware also explains that explicit explorer.exe CreateProcess calls can
bypass its replacement:
[documented limit](https://resource.dopus.com/t/free-download-manager-opens-explorer-instead-of-opus/24963).
Common file dialogs and unsupported Shell virtual locations remain separate
contracts. S6's direct StartAllBack routing can reuse the application boundary;
S8 must replace the inherited timed window takeover and verify broader coverage.

Prioritize previews and modest result/action improvements after the required
native search works. A broad Start-menu/taskbar rewrite, global recoloring, a
second result renderer, or another generalized plugin system has no demonstrated
need here. Taskbar transfer progress is another possible later integration, but
first verify how StartAllBack renders the standard Windows taskbar progress API;
it may need only a Pathweft-side call rather than a StartAllBack hook.

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

This sequence belongs only to S5, after S4U; it does not authorize later stages.

## Current reconciliation ledger

Prior post-v1.1.1 correction: S8 COMPLETE; WIDGET-01/02 and CORE-03 cover the
compact search popup and full restore-action label plus v1.1.2 delivery. Historical S8 closure row below
describes v1.1.0 only. S9 NOT STARTED; MENU-01..02 remain unimplemented, with no
tracked deferral assigned to the current S8 correction.

| Stage | Satisfied product criteria | Remaining | Dependency / blocker state | Tracked deferrals |
| --- | --- | --- | --- | --- |
| S1 | All assigned S1 criteria: CORE-01 identity/side-by-side/handoff; CORE-02 navigation/tabs/keyboard/large listing/copy, routed drop, physical source -> OLE -> destination transfer, actual OS clipboard copy/paste, independent process readback and prior-text restoration; CORE-03 rendered controls/settings; SEARCH-01..04 real index/paging/sort/cancellation/coverage/virtual origins; applicable LIFE-01/DELIVERY-03. See [evidence](verification/2026-10-28-s1.md). | None within S1 | None | None |
| S2 | All assigned WIDGET-01..03 and affected CORE-02/03, LIFE-01/DELIVERY-03: complete real query/handoff/reveal/file-action workflow; selected hotkey mode and native input before/after real Explorer restart; original Explorer location restored and Everything processes preserved; actual 96/144-DPI transitions; inspected 100%/150% text layouts; final hidden workflow recorded zero foreground events; Debug/Release builds and source self-test passed. See [evidence](verification/2026-10-29-s2.md). | None within S2 | None | None |
| S3 | All PREVIEW-01..05 and affected CORE-03/LIFE-01/DELIVERY-03: eight internal modules, actual decoding/paging, all active toggles and separate-process persistence; independent widget/Space/side selection; malformed/large/unsupported/inert inputs; actual file release; F3D real model/camera/native teardown and unchanged original; complete final QuickLook-absent run and verified restoration, zero foreground events; Debug/Release builds and Release source self-test. See [evidence](verification/2026-10-29-s3.md). | None within S3 | None | None |
| S4 | All ARCHIVE-01..05 and affected criteria: final integrated PID32156, actual native ZIP/7z/TAR/CAB/RAR5, normal archive-file handling, real clipboard/conflicts/rename/copy/cut/paste/Compress/Extract, cancellation/concurrency and D/E source-volume reservation, actual viewer/consumer release, read-only/encrypted/unsafe states, inspected 100%/150% layouts, zero foreground events and independent clipboard restoration; Debug/Release, archive contracts and updated source self-test pass. See [S4 evidence](verification/2026-10-29-s4.md). | None within S4 | None | None |
| S5 | TRANSFER-01..07 and affected caller/CORE/LIFE criteria; final native/Release/FAT32 evidence and finite findings reconciliation | None within S5 | COMPLETE; no blocker | None |
| S6 | Matching isolated Windows/Shell modules; current StartAllBack 3.9.28; native query/view trace and Programs/Settings groups with WSearch stopped; program launch and See more results URI dispatch observed on 3.9.28; earlier program/Settings launches on 3.9.25; no complete SAB criterion yet | SAB-01..06, including file-provider/cancellation ownership, remaining provider conditions, compact Everything groups/full-results handoff and direct folder routing | BLOCKED by the user's explicit S8-first priority. Investigation is retained; Sandbox was shut down. Native ABI/ownership and remaining service/indexing comparisons are unresolved when this stage resumes. Host configuration is unchanged. | None |
| S7 | None | EVERYWHERE-01..05 | S6 prerequisite not complete. File-surface, indexing-scope and Outlook compatibility inventory remains unresolved; no live scope changes during S6. | None |
| S8 | PREVIEW-06/07/About pass PID28232; CORE-09/10 pass PID28560, including actual 10-Gbps USB lanes, shared capacity/network/refresh and dynamic columns. Current local Release PID45236 and independently retrieved PID4672 pass; normal release at8552fbd, installed manifest/self-test/identity/settings checks and cleanup caec23233407869470384fecd6beeaf7 pass. | None within authorized S8 correction | COMPLETE; no blocker. S6/S7 remain outside this release. | None |

S1 and S2 are COMPLETE. The user confirmed normal unlock; real physical drag and
clipboard checks subsequently passed. A separate STA process read the Windows
file list/copy effect, paste delivered matching contents through the existing
queue, and an independent post-exit read confirmed prior-text restoration.
S2 passed following the user's next-stage request. S3 passed after the next
2026-10-29 request and its user-added F3D prototype. S4 is COMPLETE following the
user's instruction to continue with the next stage after the search-plan update.
S4R and S4U are also COMPLETE. S5 is COMPLETE under the extended goal;
S6 was ACTIVE after the user-controlled reboot, establishing isolated runtime
contract evidence. The later explicit core-first instruction parked S6 as BLOCKED
and activated S8; S7 remains NOT STARTED. S8 subsequently closed with the verified
v1.1.0 release and actual host installation on 2026-10-31. This completes the
authorized core delivery, not the unfinished S6/S7 program requirements.
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
