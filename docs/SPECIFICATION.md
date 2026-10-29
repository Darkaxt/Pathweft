# Pathweft specification

Version 0.5 - 2026-10-29. Owner: Darkaxt.

This is the authoritative specification. The derived plan must preserve every
requirement and acceptance criterion. Material scope changes require explicit
user direction recorded here; ordinary implementation choices need no new gate.

## 1. Intent and current scope

Create a responsive Windows folder browser with connected Everything search,
taskbar search, internal previews and improved transfers in one maintained
product. Keep normal views uncluttered and expose advanced controls in settings.

The setup request established the environment, public fork and specification.
On 2026-10-28 the user authorized **implementation of S1 using Specification-Gated
Implementation**. This includes independent identity, real pane search and its
required verification. After S1 completion, the user authorized **S2, the connected
taskbar workflow**, by requesting the next stage. On 2026-10-29 the user requested
the next stage again, authorizing **S3, internal selectable previews**. The same
day the user added an F3D GLB prototype to S3 and requested a separate **S4 archive
browsing/editing stage**, including rename/copy/cut/paste. REA-driven transfers
remain S5. The user subsequently requested plan review and a final integration task:
Everything inside the existing StartAllBack search, explicitly anticipating a
hook/hack because native support appears absent. This is **S6 StartAllBack search
integration**. The user then explicitly expanded the long-term outcome to
**Everything in every search location and the Windows Search service disabled**.
That migration/retirement is **S7**, after the focused StartAllBack integration;
final delivery moves to **S8**. This request authorizes specification/plan review,
not immediate service shutdown, hook activation or modification of installed
applications. The user then authorized the next implementation stage, S4, after
this plan update. S1-S4 are complete. On 2026-10-29, after reviewing unarr and
bitplane/rars, the user authorized trying rars and delegated the integration
choice. S4R extends native archives with a pinned embedded Rust RAR writer;
it precedes S5 and does not authorize a wider Rust port. No
intermediate product release is authorized. User default-handler and Windows
Search changes are not part of the S1-S3 trials.

Source integration is selective: port useful modules and add-on source rather
than requiring the complete donor applications. The 2026-10-28 clarification
makes REA analysis of TeraCopy a required basis for transfer development.
The 2026-10-29 clarification prefers native integration: use source/native SDK
libraries inside the application wherever practical, with Pathweft owning the
interface, settings and resource lifecycle. Do not make standalone viewer or
archive utility windows the default integration. Record an unavoidable external
process boundary explicitly rather than presenting it as an embedded module.

## 2. Source baseline and provenance

| Source | Pinned baseline | Role |
| --- | --- | --- |
| [File Labs](https://github.com/limburatorul/file-labs/tree/a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6) | `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6` | Fork source, development version 1.0.62; latest published release observed v1.0.61 |
| [EverythingToolbar](https://github.com/srwi/EverythingToolbar/tree/a4379f181ea8e9b4d3ff8b0e5178d8d39491830c) | `a4379f181ea8e9b4d3ff8b0e5178d8d39491830c` | Selected search clients, controls and taskbar integration |
| [QuickLook](https://github.com/QL-Win/QuickLook/tree/2ec219aa18416c6e3b89d6aad3d82d10610f917f) | `2ec219aa18416c6e3b89d6aad3d82d10610f917f` | Viewer host/contract, selected viewers and add-ons; .NET Framework 4.6.2 donor |
| Everything engine | 1.4.1.1032, SHA-256 `F191F756996A14A11E5445FA7103D302EFD510CF2FBF920E6C0C8ED51D512E36` | Existing default instance; official SDK API 2. See `vendor/EverythingSDK/NOTICES.txt`; engine source is not imported. |
| F3D | 4.0.0, b72c7b5c9244e4a0c12975ce6546645c1a8b2680 | Source-derived C API/offscreen rendering for the S3 GLB prototype |
| 7-Zip | Pin source/SDK/runtime in S4 | Native archive browsing and supported edit operations |
| rars | `e2415a2eb12e6b62deaa0cca919864eabe4671b1`, crate 0.12.3 | S4R embedded RAR encoder/rewrite API; Apache-2.0; independently qualify before enabling edits |
| TeraCopy | 4.0.5.0 x64, signed main SHA-256 `1f0cca0be4e6a22e856c3b0de6655a1af8059a06cc66133398307de335098c6f` | Required REA reference, not a product runtime dependency. Evidence and bounded behavior adoption: [S5 transfer contract](S5-TRANSFER-ADOPTION.md). |
| StartAllBack | Installed DLL file metadata observed 3.9.25 on 2026-10-29; pin exact artifact/hash and Windows dependencies in S6 | Proprietary host for a version-bound native search adapter; no donor binary redistribution or provider-hook feasibility claimed |

File Labs currently targets .NET 8/WPF. It uses recursive filesystem search
capped at 10,000 results, implements its own previews plus an external QuickLook
bridge, and retains upstream product/update identities. This newer baseline
already includes optional concurrent small-file copying. The older v1.0.53
serial-copy assessment must not be represented as current behavior. Neither
source inspection nor upstream timing claims prove comparative performance.

Pin every imported source/dependency. Reassess relevant drift before adopting
another revision. Preserve upstream history; do not import unrelated product code.

## 3. Setup acceptance

| ID | Requirement | Acceptance evidence |
| --- | --- | --- |
| ENV-01 | Named public fork preserving upstream lineage | GitHub reports `Darkaxt/Pathweft`, public, fork=true, parent `limburatorul/file-labs` |
| ENV-02 | Maintained checkout and correct remote ownership | Origin is Pathweft, upstream is File Labs, default GitHub target is the fork, clean committed worktree and matching remote head |
| ENV-03 | Reviewable development environment | Installed compatible SDK selected; Release compile succeeds; generated scratch reviewed/cleaned; retained dependencies have an ongoing project consumer |
| ENV-04 | Published project documentation | Specification, complete stage map, development notes and truthful setup evidence committed/pushed and read back independently |

## 4. Product requirements

Each row specifies required behavior and its acceptance boundary. Cross-cutting
criteria apply within the implementing stage and again at the packaged boundary.

### Identity, browsing and interface

| ID | Requirement and acceptance |
| --- | --- |
| CORE-01 | Give Pathweft independent executable/product, settings, IPC/single-instance, agent/startup, shell, installer and updater identities. Verify side-by-side isolation from File Labs: no forwarding into, migrating, overwriting or uninstalling it. Establish isolation before ordinary-user-state runtime trials. |
| CORE-02 | Preserve usable folder navigation, tabs, one/two panes, selection, keyboard actions, clipboard and drag/drop. Verify representative real folders and cross-pane operations, including a large listing. Inherited source is not proof of runtime correctness. |
| CORE-03 | One settings interface owns integrations and module controls; normal views remain uncluttered. Before accepting UI changes, establish concrete layout/display-scale expectations and inspect current rendered screenshots at representative sizes. |

### File visibility and read-only cues (S4U)

On 2026-10-29 the user approved grayscale icons with a small lock and requested
Ctrl+H plus Linux-style dot names. This authorizes S4U before S5.

| ID | Requirement and acceptance |
| --- | --- |
| FILEUI-01 | Preserve icon shapes, filename brightness, selection and cloud/cut cues. Desaturate only icons/thumbnails and add a contrasting lower-right lock for confirmed read-only files or archive contents, in Details/List/icon views and physical unsupported containers/virtual entries. Give specific tooltip and accessible state. Directory ReadOnly customization bits do not prove access denial; do not infer ACL/admin rights or write user files to probe permission. Verify writable/read-only files, capability refusals, member states and refresh. |
| FILEUI-02 | Ctrl+H and the Hidden button share one persisted setting across both panes and widget. Handle file-list/filter/path contexts while preserving text, location/query and usable selection. Preserve inline rename and separate editor shortcuts; ignore auto-repeat/additional modifiers. Verify routed input/actions, both directions and separate-process persistence in an isolated profile. |
| FILEUI-03 | Hidden means Windows Hidden attribute or a basename starting '.', excluding navigation '.'/'..'. Apply the global setting to browsing/caches, breadcrumbs/path suggestions, recursive filesystem/Everything search and archives. Hide descendants of hidden directories from recursive search; direct navigation into a known hidden folder remains usable. Never alter filesystem attributes to implement visibility. Verify dot and Windows-hidden files/directories/descendants, ordinary dotted names, cache revisits, live attributes, paging/counts and real index behavior. |

Design contract: retain File Labs tokens and row metrics. Small 16-DIP icons
use an 8-DIP lock; tiles a 24-DIP lock. Keep icon opacity apart from existing
cut/inactive styling. Inspect actual hidden WPF Details/List/icon output at
100%/150% text scale. Approved generated concept supplies direction; production
uses existing native icons. Final packaged delivery remains S8; no stage release.
The expanded sidebar and new duplicate-finder scans use this same visibility
rule, retaining the tools' other exclusion rules. Indexed results are filtered
one requested page at a time; native match counts include filtered candidates,
and the footer distinguishes visible rows from candidates checked. Inaccessible
Windows attributes remain explicitly unknown; no writable/permission claim is
made from them. Dot names remain detectable even on an unavailable indexed path.

### Everything search

| ID | Requirement and acceptance |
| --- | --- |
| SEARCH-01 | Use the actual Everything index inside panes, reusing selected EverythingToolbar client source. Offer full indexed scope and explicit current-subtree scope, distinct from ordinary list filtering. Compare real engine results for Unicode, spaces, wildcards, regex and scope boundaries. |
| SEARCH-02 | Page/lazily retrieve results and indexed metadata/sorts; do not eagerly decode every icon or enumerate all hits. Superseded queries cannot replace newer results. Verify rapid query changes, paging/sorting, empty results and changing/deleted paths. |
| SEARCH-03 | Share one deliberately selected engine instance/index between panes and widget, reusing an existing compatible instance where appropriate. Pin the binary/API version and own readiness/reconnect/instance lifecycle explicitly; never silently create a duplicate index. Keep the GUI unprivileged and necessary service privileges at the indexing boundary. |
| SEARCH-04 | Display true coverage and engine availability. Offer an explicit filesystem-search mode for unindexed locations with visible scope/progress. Do not silently fall back to a full-drive crawl or label partial results complete. Verify missing engine and unindexed/offline/network/non-NTFS cases. |

Integrating Everything does not itself authorize disabling Windows Search,
changing Outlook indexing or modifying the existing engine configuration.

### Taskbar workflow and shared actions

| ID | Requirement and acceptance |
| --- | --- |
| WIDGET-01 | Integrate selected EverythingToolbar controls/taskbar code with the product's settings/update owner. Hand query, scope and sort into a full pane; reveal opens the real parent and selects the correct item. Verify widget -> real query -> result -> pane/reveal. |
| WIDGET-02 | Share open/reveal/copy/move behavior and one transfer queue across panes/widget. No duplicate jobs or standalone utility dispatch. Keep independent query, focus and selection state per surface. Verify actual file actions/transfers from both entry points. |
| WIDGET-03 | Make taskbar integration explicit and optional. Verify the selected search-box mode on the supported Windows build across Explorer restart, focus, keyboard and monitor/DPI changes. Where the box cannot be hosted, provide an explicitly reported supported pinned-icon/hotkey mode. Do not replace the desktop shell. |

### Internal preview modules

| ID | Requirement and acceptance |
| --- | --- |
| PREVIEW-01 | Port the necessary QuickLook host/contract and selected viewer/add-on source into built-in modules for the supported modern .NET runtime. Do not depend on standalone QuickLook, its tray/global hook/updater/installer. Verify previews with standalone QuickLook absent. |
| PREVIEW-02 | Initial source-integration scope: text/code, Markdown, images, PDF and archive listing. Preserve media/audio and font previews as internal selectable modules, reusing current code where suitable. Record the exact shipped module/source/dependency/format inventory before S3 acceptance. Importing every plugin is not required. |
| PREVIEW-03 | Persist per-module enable/disable settings. Disabled modules are not selected or initialized; disabling the current module releases its preview resources. Resolve overlaps by deterministic priority or explicit preference. Show an eligible alternative or honest unsupported/disabled state. Verify toggles, active-disable and restart persistence. |
| PREVIEW-04 | Space, side-pane and widget previews share selection/lifecycle rules. Rapid navigation cannot show obsolete content. Close/change releases documents, playback and native resources. Previewing must not execute a selected executable or run document macros/scripts. Verify representative formats, malformed/large/unsupported inputs and file release by rename/delete after closing. |
| PREVIEW-05 | User-authorized S3 prototype addition (2026-10-29): add an opt-in F3D-backed GLB preview module and verify the user-specified Castlevania GLB inside Pathweft. Inspect/adapt F3D source and its supported library API. Keep module settings and resource lifetime under the internal preview host; verify a real model render, camera controls, disable/close cleanup and unchanged original asset. The private asset path/hash belongs in ignored trial input, not public source. This adds a bounded 3D prototype, not arbitrary 3D format parity or editing. |

Compiled-in modules with on-demand initialization are sufficient. Assembly
unloading, a dynamic plugin marketplace and a general extension framework are not
requirements. The explicitly requested F3D GLB prototype is included in S3;
Office and other specialist viewers remain separately scoped additions.

### Native archive browsing and editing

| ID | Requirement and acceptance |
| --- | --- |
| ARCHIVE-01 | Integrate a pinned 7-Zip-backed library/SDK into the file manager. Record exact read/write format capabilities, selected source/native dependencies and terms. Verify real ZIP/7z and representative additional supported formats without requiring a standalone archive utility window. |
| ARCHIVE-02 | Browse archive paths/folders through normal panes, tabs, breadcrumbs, selection and sorting, as virtual folders. Open an archive directly and navigate its tree without extracting the whole archive first. Preserve ordinary ZIP browsing and existing file-manager behavior. Verify representative nested paths and honest unsupported/corrupt/encrypted/multipart states. |
| ARCHIVE-03 | Provide rename/copy/cut/paste for writable formats, including filesystem-to-archive, archive-to-filesystem and archive-to-archive workflows. Keep one application action/transfer owner and explicit conflict decisions. Read-only formats expose their actual capabilities; do not imply write support where the backend lacks it. Verify real operations and destination contents through the integrated UI. |
| ARCHIVE-04 | Stage and verify archive edits before publishing a replacement. Failed/canceled writes preserve the original archive; a cut removes its source only after the destination operation is verified. Detect concurrent source changes before replacement. Verify conflicts, interrupted writes, invalid/traversal entry paths and data preservation; never silently extract an entry outside its selected destination. |
| ARCHIVE-05 | Open selected archive entries and integrate eligible internal previews through controlled, entry-bound extraction where needed. Own temporary content until its actual consumer is finished; do not delete files merely because a launcher exited or a delay elapsed. Passwords remain ephemeral/private. Verify opening/previewing actual entries, resource release and reviewed cleanup without executing content during preview. |

### Embedded writable RAR extension (S4R)

User authorization: "Give it a try, you can ... choose how to better integrate
it" (2026-10-29). Use the selected Rust library internally without deciding a
whole-application Rust migration. Full compatibility with every historical RAR
variant is not established by the upstream feature list; unsupported preservation
must be detected and reported rather than silently converting or losing data.

| ID | Requirement and acceptance |
| --- | --- |
| RAR-01 | Pin rars source and dependency lock, record licenses/provenance, and build a native library loaded in process by Pathweft. No WinRAR/rars executable dispatch for application operations. Missing/incompatible native support leaves RAR reading available and editing honestly unavailable. Verify actual loaded DLL identity and reproducible build inputs. |
| RAR-02 | Integrate RAR creation and eligible existing-archive rename/delete/copy/cut/paste through existing panes, shared actions and Ferry queue. Include filesystem-to-RAR, RAR-to-filesystem and archive-to-archive transfers, nested/Unicode/empty directories, conflicts and delete-all. Existing ZIP/7z/TAR behavior must remain verified. New archives use an explicitly recorded modern RAR format. |
| RAR-03 | Preflight source preservation through the pinned library before offering/publishing edits. Qualify supported solid and encrypted single-file archives; preserve their supported names, content, timestamps/attributes, comments and encryption settings. Refuse unsupported metadata, links, ambiguous names, codecs and multipart rewrites with a concrete reason. Do not claim every RAR variant writable; maintain an explicit capability table backed by fixtures. Passwords remain session-only and absent from logs. |
| RAR-04 | Reuse staged verification and atomic replacement: independently decode the staged output with 7-Zip and compare all expected names/content before publication; exercise UnRAR interoperability independently on representative writer outputs. Source/input changes, native failure, cancellation and failed verification preserve originals and retain cut sources. Own native buffers, callbacks and scratch through actual completion; verify no orphan process or locked input/output. |
| RAR-05 | Verify the complete integration through hidden real WPF pane/action/queue workflows, including preview and encrypted password handling, plus focused data-preservation and interoperability contracts. Retain actionable evidence, clean expendable scratch, reconcile RAR-01..05 and commit/push/read back. Final package/source/license/install and downloadable delivery remain S8; no intermediate product release. |

### REA-driven transfer improvements

After S4U closure, the user requested the next stage on 2026-10-29, authorizing
S5 investigation, evidence-backed adoption and integrated transfer implementation.
No later stage is activated and final product packaging/publication remains S8.

The user clarified S5 on 2026-10-29: reconstruct the observed TeraCopy workflow
inside Pathweft, without the additional staged overwrites or source locks held
across copying/verification. Copies write directly to their selected destination
using normal native sharing. Cancellation removes the current incomplete or
unverified copied output and retains the source; completed outputs remain, and
the previous contents of an overwritten destination are not restored. Extra
protections may be considered only if a later demonstrated need warrants them;
they are not required tracked deferrals. Source preservation, correct conflict
decisions, verification before copied-move source removal, truthful outcomes and
the metadata/filesystem criteria below still apply.

The user then requested continued comparison and implementation: **"keep
comparing our pipeline against TeraCopy, and implementing the findings, until
there are no more lessons to be learned."** This reopens S5 after its initial
verified closure. Maintain the complete comparison/findings inventory, implement
useful supported findings, and continue investigation when evidence exposes
another actionable difference. Do not treat one faster sample or completion of
the first two optimizations as exhaustion of the objective. Preserve the existing
behavioral contract and record unsupported inferences and rejected experiments.

| ID | Requirement and acceptance |
| --- | --- |
| TRANSFER-01 | Use the installed `reverse-engineer-anything` (REA) skill on an exactly identified TeraCopy artifact before selecting transfer changes. Record version, origin, signature/hash and relevant configuration. Trace scheduling, copy/verification, conflicts, pause/cancel, failure/recovery and progress with retained Evidence IDs. Separate static findings, runtime observations, inferences and unknowns. Neither marketing nor upstream changelogs satisfies this criterion. |
| TRANSFER-02 | Derive a bounded adoption table: TeraCopy behavior, evidence, current gap, implementation decision and acceptance check. Reconstruct useful behavior in Pathweft-owned code without a TeraCopy runtime dependency. Resolve design-critical unknowns before dependent implementation; decompiled pseudocode is not original buildable source. Full TeraCopy feature parity is not implied. |
| TRANSFER-03 | Integrate one queue with coherent conflict decisions, truthful progress/outcomes, pause/resume and cancellation. Failed/skipped/unverified/incomplete members must not be presented as wholly successful. Verify copy, same-volume move, cross-volume move, merge and overwrite through real callers. |
| TRANSFER-04 | Preserve source data on failed/cancelled operations and failed verification; remove moved sources only after required copy/verification succeeds. Use direct destination writes with normal source sharing. Cancellation cleans up the current incomplete/unverified copied output; completed outputs remain and overwritten old destination contents are not restored. Define and verify links/reparse points, timestamps, attributes, alternate streams and filesystem limitations. Report unsupported preservation. Use disposable fixtures for denied access, destination-full/disappearing and changing-source cases. |
| TRANSFER-05 | Compare many-small-file and large-file workloads across the identified TeraCopy version, inherited baseline and changed implementation under equivalent conditions. Record files/bytes, storage/filesystems, caching/verification, wall time, CPU and memory. Choose concurrency/buffering/scheduling from evidence; no invented speedup promise or unmeasured aggregation mechanism. Adopted performance changes need measured benefit without correctness regression. |
| TRANSFER-06 | State and verify the recovery guarantee precisely. Live pause/resume, device interruption and process-restart recovery differ. Select the required behavior from REA findings in the adoption table; never call an in-memory queue durably resumable. Keep user data outside fixtures untouched. |
| TRANSFER-07 | Continue comparing the complete transfer pipeline with the qualified TeraCopy reference and implement evidence-backed lessons. Maintain findings for enumeration/metadata/streams, copy/hash/verification, scheduling/buffers, conflicts/progress/control and failure/recovery. Cover representative small, large and mixed workloads and applicable volume paths. Separate I/O profiles from timing; compare relevant before/after and donor samples under matched conditions, repeat measurements to assess variability, and reverify affected integrated behavior. Every discovered candidate must be implemented and verified, experimentally rejected with evidence, or remain explicitly unresolved; do not declare exhaustion while actionable findings or uninvestigated pipeline differences remain. No new donor runtime dependency, optional protective workflow or later-stage implementation is implied. |

TeraCopy evidence drives transfer design. Read inherited source only for actual
integration points/gaps and preservation of valid behavior; repeated upstream
review is not a substitute for REA.

Before REA use, read the installed skill, gate/reconstruction references and
`E:/Tools/REA/STORAGE.md`. Use the installed Coordinator workflow and version-pinned
launcher, retaining original sessions through completion. Preserve existing
legacy bindings and unrelated projects. For new storage, retain findings and
validation evidence on D:, use E: for disposable generation except native/build
I/O on D:, and G: for long-term archives. Existing E: bindings are migration
constraints, not permission to reconfigure the runtime. Authenticate exported
Evidence, close owned analysis and verify worker exit before cleaning scratch.

### StartAllBack in-menu Everything integration

| ID | Requirement and acceptance |
| --- | --- |
| SAB-01 | Use the installed REA skill and Coordinator gate on an exactly identified StartAllBack artifact before selecting a hook. Record version, origin, signature/hash, Windows build and relevant loaded-module identities. Trace typed-query dispatch, file-result production/materialization, cancellation, selection/open/reveal and teardown, with retained Evidence IDs. Recheck documented extension points; distinguish static evidence, actual runtime observations, inference and unresolved ABI/ownership questions. Resolve design-critical unknowns before dependent implementation. |
| SAB-02 | Provide an optional native integration that supplies Everything-backed file/folder results inside StartAllBack's existing Start-menu search and result UI. Preserve its app/shortcut, settings and command behavior. Verify Win -> type -> actual indexed result inside that menu -> keyboard/mouse selection -> correct open/reveal. Compare controlled queries/results with the same existing Everything index. A separate Pathweft/EverythingToolbar popup, taskbar deskband or search-protocol handoff does not satisfy this in-menu requirement. |
| SAB-03 | Reuse the deliberately selected Everything instance and shared Pathweft search behavior; do not start a duplicate index. Keep the native shell adapter limited to the required query/result boundary and existing application ownership; do not host the full managed file manager inside Explorer. Own rapid-query cancellation, result identity, paging/bounds and thread/COM lifetimes through actual state/events. Verify Unicode/spaces, rapid typing, empty results, deleted/offline paths and real index unavailability; never label stale/partial results complete. The exact ABI/transport is selected from SAB-01 evidence, not assumed by this plan. |
| SAB-04 | Version/hash-bind any runtime hook to the exact validated host/module/Windows combination and verify expected target identities before attachment. Keep integration opt-in under Pathweft settings, with visible compatibility/readiness state. Unknown updates, missing owners and failed attachment leave normal StartAllBack search usable and report the integration unavailable; no guessed offsets or automatic patch retries. Preserve original installed binaries, licensing state, settings and unrelated hooks. Supply verified disable/uninstall/recovery and actual attach/detach/Explorer-restart behavior. |
| SAB-05 | Validate the complete integration in an isolated matching Windows/StartAllBack environment before a coordinated live-shell trial. Exercise enable/disable, actual native input, Explorer restart, relevant DPI/monitor/focus behavior, unavailable backend, host/app exit and an unsupported-version case. Verify restoration of normal StartAllBack search and absence of persistent orphan hooks. Use hidden diagnostics where possible; physical Start-menu acceptance requires actual visible input. S8 must package only Pathweft-owned adapter/source with exact supported-version notes and verify the delivered integration. |

The user calls this "inject Everything"; the required outcome is native in-menu
file/folder results. A version-bound runtime interception is an allowed design
candidate for S6, not an established hook or a promise of universal compatibility.
No documented replacement-provider API has been established during planning.
If evidence shows that in-menu integration cannot be implemented safely in the
available supported environment, retain the unmet criteria and record the exact
blocker. Do not silently substitute another UI or redefine the task as a hotkey.
S6 leaves Windows Search services and other consumers unchanged while proving the
in-menu adapter. S7 owns the broader replacement and service retirement explicitly
requested afterward. Do not modify licensing/activation code.

### Everything everywhere and Windows Search retirement

| ID | Requirement and acceptance |
| --- | --- |
| EVERYWHERE-01 | Establish a finite, evidence-backed inventory of search surfaces/consumers on the target installation before service retirement: Pathweft, StartAllBack, Explorer, search-capable common Open/Save dialogs, applicable native Start/taskbar surfaces, and applications using Windows Search such as classic Outlook/Office. For each, record actual host/version, entry point, backend, required query/result semantics, current coverage and acceptance workflow. Include discovered dependencies; no silent exclusions. An unanswered applicability/semantics question remains an explicit gap, not assumed coverage. |
| EVERYWHERE-02 | Route the inventoried system/file-search surfaces to the selected Everything engine, retaining their native entry points/results/actions wherever feasible. Use bounded source/native adapters and REA for unavailable host contracts. Verify actual typed query -> Everything result -> correct selection/open/reveal in each applicable surface, with WSearch unavailable in an isolated environment. A shortcut to a separate popup does not satisfy replacement of an existing search location. Record exact supported versions and capability limits. |
| EVERYWHERE-03 | Reconcile filename/path search separately from content, metadata/property, app/settings/command and mail-store search. Preserve required useful search behavior when Windows Search is removed; do not label a filename result set equivalent to message/content/property queries. Establish whether the selected Everything version and supported APIs can supply each required semantic contract. Missing capability is a real feasibility/advancement blocker. Do not silently introduce another indexing engine, retain a hidden Windows Search dependency, or narrow the user's all-Everything target to claim success; any necessary scope/backend change needs explicit user direction. |
| EVERYWHERE-04 | Only after all inventoried replacement workflows pass, carry out a reversible, explicitly recorded Windows Search (WSearch) service shutdown/disable as the specified migration outcome. Preserve its actual prior startup/delayed-start/running state and recovery procedure; do not delete its index, change unrelated services/policies or alter mailbox/user data as a side effect. Use the required administrator broker for actual service control. Verify replacements with the service stopped and Disabled, and verify state/behavior after reboot/sign-in. Current planning does not authorize this live change. |
| EVERYWHERE-05 | Verify the integrated system with WSearch disabled: all required consumer workflows, index freshness, scope/Unicode, unavailable Everything, host updates, exit/restart, rollback and uninstall. Unknown versions/unavailable adapters must report the real loss of coverage; no silent dependency on a stopped Windows Search service or unrequested service restart. Verify actual restoration of the prior service/search configuration through the documented recovery path. Final acceptance requires closure of every inventoried consumer gap; S8 verifies the delivered packages and migration/recovery materials. |

This long-term direction supersedes the earlier blanket exclusion of Windows
Search/Outlook changes only within the explicitly specified S7 replacement
outcome. It is not an instruction to stop the current service now. Outlook and
other non-file consumers are assessed dependencies, not presumed compatible or
silently sacrificed. Universal replacement is an unproven engineering goal;
StartAllBack hook feasibility and broader consumer compatibility remain separate
questions. The plan cannot turn those unknowns into completed integration.

### Lifecycle, shell integration and final delivery

| ID | Requirement and acceptance |
| --- | --- |
| LIFE-01 | Own initialization, readiness, cancellation and cleanup through actual state/events. Do not copy fixed sleeps/retries or arbitrary timeout-driven synchronization from donors. Isolate unavoidable external timeouts at their boundary. Verify owned helper exit before scratch cleanup. |
| SHELL-01 | Folder/default-handler and Win+E integration is optional, reversible and verified. Restore the actual prior owner/settings on disable/uninstall. Document actual virtual-location/dialog support. Folder-browser scope does not imply desktop-shell replacement. |
| DELIVERY-01 | One installation, settings owner and update channel. Pin/package Everything and native preview dependencies; preserve external instances/data and prevent upstream updates replacing Pathweft. Verify install/update/uninstall in an isolated supported Windows environment. |
| DELIVERY-02 | After all stages pass, publish one normal versioned Pathweft release here: usable Windows x64 installer and portable package, source/tag, checksums, accurate notes and required source/notices. State signing identity/status honestly. Independently retrieve both assets and verify hashes/version/commit and packaged search/preview/transfer behavior. No setup/intermediate commit is an application release. |
| DELIVERY-03 | Preserve original notices, exact donor/dependency revisions and terms; provide applicable GPL source/distribution materials when QuickLook code is imported. Keep private traces, credentials, personal sample paths and machine records out of public source/assets. |

## 5. Acceptance, stages and completion

The [plan](IMPLEMENTATION-PLAN.md) maps every requirement to an owner. Complete
features/real workflows are acceptance units. Compilation, mocks, test counts or
worker reports do not prove runtime integration or delivery. Use focused checks
appropriate to the actual risks; repeat only invalidated checks.

At most one implementation stage is ACTIVE. Missing current-stage work is an
advancement blocker, even if straightforward. Record its exact requirement,
reason, internal/external cause, resolution condition and dependent work. Park
as BLOCKED only when it cannot presently be resolved. A tracked deferral must
name a later stage and verification, preserve current acceptance and close before
overall completion. Do not silently narrow requirements to close a stage.

No performance/memory-saving claim without measured attributable evidence. No
installed/runtime claim from the setup build. Overall product completion requires
every criterion, integration, blocker, deferral and final release verification
resolved. Initial setup delivered the environment/repository/documents; the
current implementation authorization and reconciliation are recorded in the plan.

## 6. Decisions and limits

- Name: **Pathweft**, connecting file workflows in one product.
- Initial development SDK: .NET 8 for baseline comparability. Select a supported
  runtime and verify donors before release; baseline preservation does not justify
  shipping an unsupported runtime.
- No Everything indexer rewrite, standalone donor-product requirement,
  desktop-shell replacement or unrelated expansion. S6 targets StartAllBack;
  S7 explicitly owns the user's system-wide search replacement/service-retirement
  goal, subject to actual consumer/semantic coverage and verified recovery.
- Existing application identity stays unchanged during setup; S1 must isolate it
  before runtime trials. No claim that the inherited installer is ready.
- No implementation-stage releases. The final release belongs to S8 after all
  prerequisite criteria pass under actual implementation authorization.
