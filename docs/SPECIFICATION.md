# Pathweft specification

Version 0.71 - 2026-11-06. Owner: Darkaxt.

Bug correction2026-11-06: S23 - Missing-folder tab recovery is COMPLETE under the
user's requested fix. CORE-23 requires selectable tabs when an old local folder
has disappeared: resolve confirmed missing descendants to the nearest existing
directory, however many parent levels are needed, and persist the repaired tab.
Preserve tab order/pins/active identity during switching and startup restoration,
normal filesystem/archive navigation, and normal unavailable/access-denied states.
Do not reinterpret a disconnected drive/network provider as a deleted folder or
block the UI on network recovery. Verify actual tab clicks, keyboard/external
activation, restored state and ancestor listing, then deliver one normal verified
release under DELIVERY-01..03. Verified through published/independently retrieved
and installed1.1.25 acceptance; see the S23 verification record. No later feature
stage is activated.

Plan addition2026-11-06: S22 - Focused file workflow enhancements is NOT STARTED,
immediately after Linux-inspired S21 and before S6. OPUS-01..10 register named
project workspaces, paired navigation, expandable/flat browsing, persistent
collections, operation history, listing export and lower-priority aliases,
photo marking and batch embedded-metadata editing. Implement focused behavior
inside Pathweft; use bounded REA only for a concrete unresolved behavior or
interoperability question. No Directory Opus installation/runtime dependency.
This request records future scope only; no implementation, uninstall, settings
change or stage activation. Remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S21 - Browsing productivity and smart destinations is
NOT STARTED after S20 and before S22. BROWSE-01..12 register the discussed
Linux-inspired capabilities and browser-style address suggestions, including
recent/pinned folders, open tabs, saved searches and filesystem completions.
The user's latest instruction supersedes the earlier research-only treatment
of those suggestions by adding them to the future plan; it does not authorize
implementation, connection, metadata/source mutation or live configuration.
No implementation stage is ACTIVE. Remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S20 - Integrated PowerToys file tools is NOT STARTED
after S19 and before S21. TOOLS-01..09 register the seven discussed capabilities:
PowerRename, File Locksmith, Image Resizer, Text Extractor/OCR, Registry Preview,
local deterministic Advanced Paste and New+. New+ is included at lower priority.
Borrow focused source implementations and integrate them into Pathweft's own
UI/selection/operation lifecycle; do not require a separate PowerToys installation.
This authorizes documentation only, not implementation, installation, registry
changes or process control. No stage is ACTIVE. The subsequent S21 request
explicitly registers the Linux-inspired findings as future scope.

Plan addition2026-11-06: S19 - Selected-folder content indexing is NOT STARTED,
immediately after S12 and before S20. CONTENT-01..06 record folder opt-in,
qualified Craft/plain-text extraction, searchable strings with source-location
arrays, periodic incremental refresh and current-index icon badges. Retaining
whole extracted documents or offline excerpts is superseded by the user's lean
strings/location design. SQLite FTS5 is the leading candidate, not a qualified
implementation. This is a plan-only request; no reads/downloads, indexing,
schedules, settings changes or implementation are activated.

Plan addition2026-11-06: S18 - Contextual performance monitoring is NOT STARTED,
ordered after S16 and before S12. Remaining execution order is
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7. This request authorizes specification/plan changes
only; it does not activate monitoring, tracing, privileged helpers or installation.
MONITOR-01..05 below capture the agreed bottom-card and Resource Monitor-inspired
detail behavior. No implementation stage is ACTIVE.

Execution reconciliation2026-11-06: S17 COMPLETE. TRANSFER-16, PREVIEW-38 and
CORE-22 are delivered in normal v1.1.24 at abacf4171f753616dd50ac2233b58df364036692.
Actual source/package, independent retrieval/full consumer57492 and installed
readback35716 verify all assigned criteria, including preserved Windows identity.
No unresolved S17 blocker or tracked deferral remains; no stage is ACTIVE.
Later unrequested stages remain unchanged. See the S17 verification record.
The authorization descriptions below document the scope at implementation start.

Approved branding direction2026-11-05, CORE-22, assigned to ACTIVE S17:
replace the application icon with the user-approved yellow folder and compass.
Use a clear darker compass silhouette at small title/taskbar sizes; retain
polished depth/detail at larger sizes without fine lettering becoming noise.
Provide transparent multiresolution Windows icon assets, verify 16/24/32/48/64/
128/256 sizes on light/dark surfaces and actual embedded WPF/native/package use.
Preserve Darkaxt.Pathweft application/shortcut/Jump List identity and all settings.
This joins TRANSFER-16/PREVIEW-38 in one final next update, not a separate release.

Additional S17 next-update request2026-11-05, PREVIEW-38: PiP and Windowed
previews follow the active file selection instead of keeping the original item
pinned. This explicitly supersedes PREVIEW-37's pin-while-browsing behavior.
The user subsequently requested an explicit pin icon alongside PiP/Windowed/close:
default unpinned follows selection; pinned retains the displayed item while
browsing/no selection, across host changes. Unpin immediately follows the current
selection in the same host. Keep pin state visibly/accessibly distinct and clear
it on explicit preview close; it is a session control, not a new persisted setting.
The user's subsequent approval activates CORE-22's folder/compass replacement.
Preserve the selected host mode/bounds on file changes; replace content/title/
icon and release the previous renderer/source lease correctly. Obsolete async
completions must not overwrite the latest selection. Keep mode-only transitions
state-preserving and floating-window closure restoring the sidebar. Clear stale
content for no selection under the existing preview enablement rules. Scale the
PiP and Windowed glyphs to the close icon's small visual proportion while keeping
accessible tooltips and usable hit targets. Qualify actual selection changes in
both hosts, rapid replacement, renderer/lease teardown and dark/light/scaled UI.
Implement this alongside TRANSFER-16 in S17's one combined next release; no
second stage is ACTIVE and S15 remains delivered.

New authorized next-update scope2026-11-05, TRANSFER-16, assigned to S17:
prioritize existing destination files newer than their incoming replacements at
the top of the conflict table, using readable orange and a textual/accessibility
cue. By default show files whose size or modification timestamp differs, and
render only the differing properties in their Incoming/Existing cells. Hide
matching property values; offer Show matching files with accurate shown/total
counts and explicit action scope. Matching size/date is not a claim of identical
contents: this review uses existing metadata, without additional content reads,
hashing or cloud hydration. Keep the complete conflict set and existing four
resolution choices; filtering must never silently skip files. Display enough
size/time precision to distinguish unequal values, including subsecond changes.
Use the existing compact table/virtualization and application design; qualify
mixed/all-matching conflicts, newer-destination ordering/cues, show-all, actual
resolution outcomes, keyboard and dark/light100/150% rendering. S17 owns the
normal next release, independent retrieval and installed verification. S15 is
already delivered; S17 alone is ACTIVE, with later stages unchanged.

Execution reconciliation2026-11-05: S11/S14/S15 COMPLETE through normal1.1.23
at fc01ce1. Actual final57312, independently downloaded51296 and installed
readback58076 satisfy the assigned behavior, native boundaries and delivery
criteria, including the transfer addition below. No stage is ACTIVE; no required
blocker or tracked deferral remains in this authorized round. Dated execution
checkpoints below retain the original requirements/history, not current status.

This is the authoritative specification. The derived plan must preserve every
requirement and acceptance criterion. Material scope changes require explicit
user direction recorded here; ordinary implementation choices need no new gate.

2026-11-05 resumed S15 correction: the user reports that1.1.22 still leaves an
empty Pending tree. SPACE-01..03 require startup to proceed after cancelled
ordinary sizing, including when its optional index dispatcher is occupied.
Present initial loading activity while startup is genuinely waiting; respect
reduced motion and retain quiet completed values during background refresh.
CORE-11 is amended in S15: healthy disk-capacity bars stay blue independently
of the theme accent; retain amber from80% and red from90% used on every disk
capacity surface. This does not change ordinary accent-based progress controls.
The user's pause is superseded by their subsequent "continue". S15 remains
ACTIVE; the reported Pending bug, final correction delivery and installed
readback remain acceptance blockers. No later stage is activated.

2026-11-05 current S15 regression: the installed1.1.21 analysis button can leave
an empty tree after overlapping delayed entry/exit/re-entry. SPACE-01..03 and
LIFE-01 require the entire admission/startup operation to remain owned until
completion/cancellation, immediate mode presentation and preservation of the
current tree when old operations finish. Qualify repeated real button events,
navigation during held admission, exact restore and corrected final delivery.
This clarifies existing lifecycle acceptance; no later stage is activated.

User-authorized S15 addition2026-11-05, TRANSFER-15: redesign the Transfers
popup using Huashu within the existing WPF auxiliary design. Keep its compact
640-DIP default width and content-sized height; use the existing palette, Segoe
type,12/13-DIP body and18-DIP aggregate heading,12/16-DIP spacing and32-DIP
actions. Conflict content is an actual virtualized comparison table (File,
Incoming, Existing; paired size/date values), with a bounded240-DIP scrollable
viewport, readable full-path tooltips and keyboard navigation. Populate/layout
new content without an opacity-zero entrance; automatic growth must be driven
by actual content, not a speculative blank area. Preserve user resizing,
multiple jobs and100/150% dark/light layouts. Display aggregate speed once for
one job; show per-job legend rates only when several jobs run. Do not confuse
combined copy/verification throughput with copied-payload throughput.
Explicitly cancelling a transfer dismisses it only after actual worker exit,
unless errors require review; preserve other active/adverse jobs and existing
completion/Undo/arrival semantics. This explicitly supersedes TRANSFER-14's
requirement to retain an otherwise clean cancelled card. Cancelling a paste
must preserve its copied/cut clipboard formats and allow another Paste, without
overwriting newer clipboard contents. Reproduce real copy/conflict/cancel/paste
and compare native formats/sequence on an isolated clipboard. Show actual
file-specific failures first; harmless metadata notes belong in Details, not
an error summary that hides the failure. This addition stays in ACTIVE S15 and
the combined final release; S14 is not reopened and no second stage is ACTIVE.

Current authorization2026-11-05: "continue working then" authorizes implementing
the recorded native-category correction, S14 window/update feedback and the
selected current-pane Diskweft integration. Execute S11 -> S14 -> S15 with one
ACTIVE implementation stage. S11/S14 close their complete source/integration
acceptance before advancement; S15 owns one combined normal release and
independent source/package/installed verification for this round. No per-stage
release. Earlier plan-only wording below is historical for these now-authorized
items. S12/S6/S7 remain outside this request. The user confirms an actual native
folder pin survives restarting1.1.18; preserve existing pins during correction.
The user additionally requests automatic dismissal of successful transfers;
TRANSFER-14 belongs to the currently ACTIVE S14. Keep unsuccessful outcomes
available for review and preserve operation completion, Undo and arrival selection.

The user's subsequent "Follow your best recommendation choice" authorizes
Everything-first folder sizing in S15: reuse available indexed folder totals,
retain metadata-only traversal for uncovered scopes, and replace the unbounded
eager text-cache load with a bounded hot cache and disk-backed fallback records.
Do not change the external Everything index/configuration or require an engine
upgrade. Preserve indexed-versus-scanned coverage and last measured UI values.
On2026-11-05 the user explicitly authorized enabling folder-size indexing in
their existing Everything instance. This is a scoped machine-setting exception:
preserve its other settings, service, index scope and version, restart normally
in the background and verify actual indexed sizing. Product fallback remains
required when another user's index is disabled or a path is uncovered.

The user's1.1.19 feedback extends S15's refresh acceptance: the analysis command
must track actual active-folder readiness after asynchronous navigation. Known
size refreshes must remain quiet (no flashing measuring labels or partial-total
suffixes); unknown initial totals still show honest progress. Background listing
or sizing refresh must preserve an in-progress address edit, text, caret/selection
and focus. Explicit navigation, submit and cancel retain their normal behavior.

While1.1.20 publication was already in flight, the user added these S15
acceptance requirements for the following integrated correction: entering
analysis selects/focuses the first available top row once, without stealing
focus on later updates; expansion arrows render unclipped at existing compact
proportions; ordinary background writes refresh affected analysis branches
without repeatedly cancelling/restarting the whole root or resetting completed
unrelated rows to Pending/Measuring. Preserve truthful initial progress, explicit
full refresh, actual invalidation and exact grow/shrink/rename/delete totals.
Audit all bottom-bar toggles: hidden items have an appropriate crossed/open eye,
pane count depicts the current layout, and analysis/preview/hidden/two-pane
states have clear persistent active styling, action tooltips and accessible
state. One final correction release owns these additions and installed readback;
do not request an intermediate1.1.20 installation merely to advance this work.

The user's2026-11-05 Craft maintenance handoff requests consideration of newer
Office cores and additional installed Craft products. The assessment is recorded
in [CRAFT-CORE-REVIEW.md](CRAFT-CORE-REVIEW.md), with proposed S16 NOT STARTED.
It preserves source-built minimal DLLs/owned workers, not external application or
MCP dependencies. This assessment does not expand the current S15 release or
authorize activating a second implementation stage, replacing PDF rendering
without comparison, or changing installed Craft/Coordinator selections.

On2026-11-04 the user authorized implementation of S13, S9 and S11 together.
Execute them in that order with at most one ACTIVE stage. Current execution
checkpoint: S13 and S9's assigned source/integration acceptance is COMPLETE;
S11 is ACTIVE. Advance only after each predecessor's complete
implementation/integration acceptance passes. Deliver one normal release for the complete combined outcome at the end
of S11, with independent package/source retrieval and actual installed-update
verification for all three stages. This shared final delivery assignment
supersedes the earlier per-stage publication sequence without removing any
verification criterion. S13/S9 close against their complete source/integration
criteria; their release verification is explicitly owned by S11, and overall
completion requires it. S12 and S6/S7 remain outside this implementation request.
Preserve the running product, existing user state and completed S8 evidence.

2026-11-05 delivery readback: normal1.1.18 at8191b55972700af87f963f6266c6373f61d64dc2
is published and independently retrieved/qualified. The actual installed update
matches all10672 manifest members; restarted main/helper, readiness,22 folder
route values and the shortcut AppID pass. The user's screenshot confirms native
StartAllBack folder-menu presentation and reports it working. This does not
independently demonstrate every pin/unpin/remove/restart action in JUMP-02.
Their subsequent correction requires Explorer's native Pinned/Frequent category
structure rather than the custom Quick access/Recent folders and Home/Downloads
Tasks in1.1.18. S11 remains ACTIVE for that correction and its qualification;
do not label the requested revision delivered based on the existing release.

The user reiterates S14's outstanding popup unification and manual update-check
confirmation, ahead of unrelated StartAllBack work. They request an evaluation
of borrowing Diskweft's engine, clarifying that it must use Pathweft's own style
and the currently open path, and then selecting a reversible mode in the current
pane: a compact button beside Terminal, expandable folder tree, Space used
column and descending size order; clicking again restores the standard view.
No separate Diskweft UI/window is wanted. See [the assessment](DISKWEFT-INTEGRATION.md).
This evaluation does not activate another implementation stage or authorize a
standalone-app/MCP dependency. S14 remains NOT STARTED; S6/S7 stay postponed.

The user adds planned S14 (NOT STARTED) for consistent auxiliary-window design
and immediate manual update-check feedback, after the currently authorized
combined S13/S9/S11 delivery. Priority becomes
S13 -> S9 -> S11 -> S14 -> S12 -> S6 -> S7. This is a plan addition, not authority
to activate S14 or expand the current combined release. S9's menu work also
includes search-widget result actions: open containing folder in Pathweft while
preserving result selection, and native Properties for the actual selected
file/folder/shortcut, with equivalent keyboard access and truthful eligibility.
S9 clarification from native diagnosis129196: Windows hosts its Properties
sheets through a same-process StubWindow32, rather than directly owning them
with the invoking search widget. Direct GW_OWNER equality was an over-specified
diagnostic condition, not part of the requested QoL behavior. Preserve the actual
native sheet and selected object, including shortcuts. Like opening a result,
successful Properties dispatch dismisses search; the native sheet must survive
that popup hide and close normally. A failed native dispatch keeps search visible
and reports the row error. Pass the main application window to the existing Shell
API; do not reparent or redesign native property sheets. Complete actual native
target/visibility/lifetime and Delete verification before S9 closure and S11.

User additions2026-11-04 to the current S13 interface work: remove the version
from the sidebar header and move Update, What's new, Help and Settings beside
the app name. Keep Drop bin at the bottom. Use compact icon buttons and retain
tooltips, keyboard access, the draggable brand area and existing actions. At a
narrow sidebar, put those four controls immediately below the brand instead of
clipping them; preserve usability at100%/150% text/sidebar scale. About keeps
Pathweft's version and Darkaxt identity, its own project link and existing help,
settings-folder and update actions. Remove upstream-author branding, feedback
and promotional links from About; preserve all distributed source/license notices
and actual optional integrations. Child settings toggles retain their indentation
and use the standard6-DIP vertical separation, including taskbar embedding,
autoscroll direction, compact summary cards and startup options.

User additions2026-11-04: S13 Office recovery feedback must use a concise orange
bottom-right status, such as `Error loading: {filename}`, with full diagnostics
on hover instead of a wrapped technical error paragraph beneath the document.
Retain the actual renderer icon to show successful LibreOffice recovery. S9 also
owns MENU-03: simplify the bottom command area with familiar Explorer-style icons
and tooltips containing command names and shortcuts, preserving accessible names,
keyboard actions, eligibility and the existing Native/Original theme behavior.
Recover the native toolbar and View-menu artwork from the supplied references;
prefer exact scalable system assets over screenshot crops or approximate symbols.
On2026-11-04 the user then explicitly directed implementing the recovered icons
now, preserving Pathweft's proportions rather than Explorer's larger spacing.
Assign that bounded MENU-03 presentation integration to active S13; MENU-01/02
remain in S9 after S13 acceptance, and final release stays in S11. This addition
does not mark the broader context-menu stage active or complete. Keep the bottom
bar at its compact scale:16-DIP command artwork,30x24-DIP buttons,14-DIP pane
view/sort artwork in28x22-DIP buttons, with no new toolbar row. Keep all existing
actions and shortcuts; show names/shortcuts/current view or sort on hover. Preserve
keyboard focus visibility, disabled/toggled states and narrow/100%/150% layouts.
The user's subsequent grouping correction requires coherent compact groups:
New folder/New file; Open/Rename/Delete; Copy/Move/Transfers; Terminal;
Hidden/Panes/Preview. Separate groups with restrained1-DIP dividers and4-DIP
side margins; preserve command semantics and match keyboard order to visual order.
At narrow widths reduce button width only, down to22 DIP, to keep the16-DIP art
and every command on one row at150% text scale; do not enlarge the bar or hide actions.
The terminal command uses the user-selected terminal-window `>_` glyph (Segoe
Fluent Icons U+E756), replacing the folder/play artwork of Explorer's PowerShell
asset. Keep its existing launch action and Ctrl+` tooltip.
Use installed native SVGs without redistributing Windows assets; preserve glyph
compatibility when that Windows resource set is absent.

On2026-11-04 the user added and clarified PREVIEW-37 in active S13: provide two
distinct preview modes through accessible icon controls next to the sidebar's
close button. Windowed moves the preview into its own resizable floating window;
closing that window MUST restore the same preview to the side column. PiP moves
the preview into a compact corner overlay inside Pathweft, freeing the whole
sidebar column; it is not another external floating window. Provide a return-to-
sidebar control in PiP. Preserve the viewed item, page/sheet/slide, media position/
play state and 3D view/animation state; changing hosts must not restart or duplicate
the renderer. Detached modes stay on their item while the browser navigates
elsewhere. Retain the original close-preview action separately from window-close
restoration. Reuse Pathweft's theme, selection/lifetime and floating-window
infrastructure. Implement after Office integration, within the same ACTIVE stage
and combined release; verify actual relevant native surfaces and app-exit cleanup.

The user also clarified that workload gates coordinate agent threads and resource
use. Borrowed source libraries and the shipped Pathweft worker do not require a
Craft application gate identity, Coordinator or MCP. Keep the prescribed managed
routes for actual registered development-tool workloads without turning them into
product dependencies or inventing a library-admission blocker.

Office architecture decision2026-11-04: resume implementation using the pinned
Craft source cores as they currently behave, with minimal native-library bridges.
Preserve their rendering semantics and current limitations; this does not require
reimplementing Microsoft Office or adding missing formats to Craft. Keep our bridge
separate from upstream so reviewed fixes can be incorporated later. No Craft GUI,
CLI application, MCP service or separately installed Office product is required by
the delivered viewer. A small Pathweft-owned preview worker hosts the bundled
libraries; the user explicitly selected this arrangement for native-crash recovery.

LibreOffice is the required bundled fallback for declared Office formats that Craft
does not import, including legacy PPT/ODP and Visio, and for failed Craft imports,
renders or abnormal worker exits. A fallback must actually render inside Pathweft
with the same page/sheet/slide navigation. Use one fallback attempt per failed
request; observe original worker exit and ownership before switching engines.
Cancellation, disabled modules and source/policy limits must not start fallback
work. Preserve source bytes/MOTW and never execute macros or update external links.
If both engines fail, report an explicit preview error with truthful engine context.
Public interest in future Craft Visio/Project/OneNote applications is a monitoring
signal, not evidence of an existing importer. Resolve their actual LibreOffice
coverage before claiming or extending supported formats; do not substitute a
metadata-only view for promised document rendering.

On2026-11-04 the user explicitly chose full Office page/sheet/slide navigation
with an embedded renderer, accepting an additional runtime of several hundred
MB rather than limiting support to installed Windows Office handlers. The user
subsequently directed qualification of Storytold's WordCraft, GridCraft and
DeckCraft renderers and installed their portable candidates. Assess actual
release-pinned rendering, format coverage and renderer-only footprint before
selection; alpha labels or an assumed coverage percentage are not evidence.
LibreOffice remains a candidate where required formats need it. Do not deploy
MCP/gate features: the user owns enabling those identities. Render
the result inside Pathweft with real navigation. Do not open an associated Office
UI or install/change a system Office application. Use an owned isolated profile,
explicit read-only loading, never-execute macro and never-update link policies,
deterministic process/cancellation/cleanup, original byte/Zone.Identifier
preservation and complete native/source/license provenance. Missing/protected
inputs remain explicit. The earlier native-handler-only matrix is superseded
for Office, not a waiver of PREVIEW-22/36. Stage order and one final release stay
unchanged; S13 remains ACTIVE.

On2026-11-04 the user added live folder-size correction to this round after
installing nested Craft release folders left their ancestor rows at zero until
F5. CORE-18 belongs to current S13 and its shared S11 delivery gate: observe
nested creates/writes/deletes/renames, invalidate affected cached ancestors,
and remeasure/update visible rows, selected totals and folder summary together
without manual refresh. Preserve selection/scroll, unchanged sibling cache,
navigation/cancellation ownership, and prevent an obsolete sizing result from
overwriting newer state. Handle watcher overflow explicitly. Unknown or ongoing
measurements must not claim an exact empty-folder result. Verify real external
nested mutations, growth/shrink, both panes/return navigation, and source/state
preservation; do not add timer polling or periodic full-drive rescans.

On 2026-11-02 the user requested a plan stage implementing all fourteen missing
QuickLook viewer categories identified in the preview comparison, and requested
Pathweft improvements before StartAllBack. S13 owns PREVIEW-21..36 below and is
NOT STARTED. Preserve stage IDs and completed evidence; the remaining execution
order is S13 -> S9 -> S11 -> S12 -> S6 -> S7. S6 and its dependent S7 form the
final Windows-search integration block: S7 cannot reduce indexing scope before
S6's replacement workflows pass. No existing SAB/EVERYWHERE acceptance is waived.
This instruction updates specification/plan only; it does not activate a stage,
authorize implementation, restart Sandbox or change the running system. S8 stays
COMPLETE through installed1.1.17. Future implementation delivery belongs to the
stage owning that outcome, not a retrospective reopening of completed S8.

S8 is COMPLETE through installed1.1.17 for PREVIEW-20. The authorized Marked/
FriBidi implementation, source/native workflows, normal publication and
independently downloaded package/source verification pass at5019884. The user
updated/reopened; all879 installed members, fresh main/helper identities,
helper readiness and22 desktop route values pass. Task scratch is removed
without residuals. No stage is ACTIVE and no blocker or tracked deferral remains.
Other review candidates and S12 remain unstarted.

PREVIEW-20: replace the editor's Marked18.0.14 bundle with a source/hash-pinned
patched stable release containing the Unicode-link cubic-backtracking fix.
Replace FriBidi1.0.16 in the embedded media runtime with1.0.18 using a source-bound
native build; retain existing mpv/FFmpeg/libplacebo revisions, WASAPI patch,
Dolby Vision-to-SDR contract and native-window presentation. Preserve the running
product, donor worktree and retained build inputs. Use the existing gated native
toolchain and isolated task output. Provide exact notices, source/build recipes
and packaged runtime identity. Verify ordinary and malformed/Unicode Markdown in
the actual editor, RTL/isolate-heavy subtitles in actual native playback,
SDR/DV P5/P8 color/seek/teardown, and all affected integration boundaries. Publish
one normal1.1.17 release after both integrated workflows pass; independently
retrieve it and verify the user-controlled installed update. All acceptance and
delivery work remains an internal advancement blocker until verified; no tracked
deferral or new integration stage is introduced.

Previous S8 was COMPLETE through installed1.1.16 for PREVIEW-19. Normal publication at
35f01f6, independently downloaded native/app workflows, matching sources and
all878 installed members pass. Main/helper restart/readiness and22 actual
desktop route values pass; the user confirms the reported HTML now renders.
Scratch cleanup is applied without residuals. No stage is ACTIVE and no current
S8 blocker or tracked deferral remains. S12 remains NOT STARTED and plan-only.

PREVIEW-19: correct static HTML preview on legitimate mapped-drive/UNC aliases.
Opened file identity must match the canonical drive/share
root plus the checked relative path; do not authorize a substituted document
directory or relax reparse, traversal, script or network restrictions. Verify
the reported provider-backed file read-only, extended mapped-path access, normal local
and archived assets, true escape rejection and cleanup. Complete normal1.1.16
publication and independent package verification; preserve the user's update
control and verify the actual installed update.
Tresorit's normalized UNC handle name is synthetic: direct reopening of that
name returns ERROR_INVALID_NAME. Qualify its canonical identity through the
working mapped and extended paths; do not claim direct provider UNC access.

Previous S8 was COMPLETE for the authorized GLB/glTF smart timeline and isolated HTML
viewer. Both features, normal1.1.15 publication and independent delivered-package
verification pass at85de43e. The user updated/reopened; all878 installed members,
main/helper restart/readiness and22 actual desktop route values pass. Spent
scratch cleanup is applied without residuals. No stage is ACTIVE and no current
S8 blocker or tracked deferral remains.

PREVIEW-17: default GLB/glTF timelines omit only a trailing held pose using actual
animation samples and scale-aware movement tolerance. Ignore exporter drift without
discarding intentional small/slow motion or internal pauses. Preserve native clip
identity, starts, playback/seek/drag/keyboard semantics, switching and cleanup.
Offer the original full range through the existing themed timeline's context menu;
do not add another toolbar row. Static, uninspectable/unsupported or genuinely
changing clips retain the native full range. Never rewrite the source model.
Verify the reported SNES model (visible motion to6.875s; native end55.416668s),
late/slow motion, small animated parts, pauses, multiple clips, malformed metadata,
and actual native rendered/playing/seeking/teardown results.

PREVIEW-18: add an HTML module above generic text for .html/.htm, available in the
existing Settings > Files preview-module section. Render static HTML/CSS and
allow-listed local images/fonts, including relative assets inside a compressed
folder. Isolate the WebView2 environment from the editor; do not expose messages,
host objects, credentials or unrestricted disk access. JavaScript, network access,
frames, permissions, form submission, downloads, popups and automatic navigation
are disabled/blocked. Serve only checked document-scope resources; reject escapes,
links/reparse paths and external/custom schemes. Sanitize HTML/CSS before browser
parsing to prevent speculative connections. No blanket directory mapping or
whole-archive extraction. Preserve document presentation inside Pathweft's themed
preview frame; script-dependent controls remain inactive. HTML document/resource/
total-resource budgets are16/64/128MiB, with explicit failure at the boundary. Disabled HTML returns
to source preview; missing runtime/load failures are explicit. Observe completion,
selection/module cancellation, archive source/resource lifetimes and browser teardown with
no arbitrary delay. Verify actual WebView2 rendering, local/archive assets and
negative boundary fixtures, source preservation, dark/light layouts and all preview
surfaces, then the actual delivered package and installed updater result.

Design direction: preserve existing Pathweft controls/spacing, using the installed
Huashu production-UI guidance. One S8 stage owns both fully integrated features,
focused source/native/UI acceptance, final reconciliation/commit, one normal release,
independent retrieval and installed verification. No postponed-stage work.

Historical1.1.14 acceptance follows. The previous S8 extension is COMPLETE through1.1.13: all871 installed members,
reopened main/helper readiness and22 desktop route values pass after the user's
update. The following corrections are published and independently qualified in
normal1.1.14 at1d20122f52768c2760dd1f552d37e19f07ca67b0. S8 is COMPLETE:
after the user's update/reopen, fresh2026-11-02 verification passes all871
installed member hashes, main/helper ownership and readiness, and22 actual
desktop route values. No remaining S8 blocker, ACTIVE stage or tracked deferral.

UPDATE-01: the user requests a themed popup reporting deployment progress during
updates. Retain the existing update action and verified per-user installer.
Show actual downloading/checking progress before exit, then an installer-owned
compact progress popup that remains while Pathweft closes and files are replaced.
Report actual deployment, integration/helper restoration and reopening phases;
percentages refer to measured work, never elapsed-time simulation. Preserve
light/dark, accent, readable contrast and DPI scaling using Pathweft's existing
palette, icon and typography. Visual contract: approximately520x240 DIP,20-DIP
insets, concise heading/status, accent progress track and no debug/path dump.
The deployment popup follows the existing updater UI: the same compact heading,
subtitle, low progress track and bottom status row, including its current palette.
Handle failed preparation/installation visibly without reporting completion.
Keep download cancellation, checksum/size checks, active-work blockers, exact
requesting-process wait and existing opt-in/recovery behavior. Do not introduce
another updater service, arbitrary delays, forced exits or a new administrator
requirement. Existing1.1.13 clients must receive deployment progress when they
install this release, despite using the old very-silent launch switch. Verify
real installer progress/phase/error callbacks and hidden dark/light100/150%
layouts without altering the installed identity or taking desktop focus.

PREVIEW-16: clicking the video/audio or3D animation timeline seeks directly to
the clicked position while paused AND while playing. Playback position updates
must not overwrite user seek intent before it is submitted. Dragging and keyboard
seeking remain usable, and a seek while playing continues from the new position
without restarting or pausing playback. Verify actual WPF timeline actions and
native media/animation state with playback events and renderer completion.
Release both corrections together, independently retrieve/check the delivered
package and verify the installed updater result. No postponed-stage work.

Historical1.1.13 requirements follow. The user restarted Helium from Start and confirmed
folder opening works; native tracing verifies class lookup and handler activation
now succeed. The user explicitly requests preserving file highlighting too.
Complete that native selection handoff without taking over Explorer navigation,
preserve opt-out/virtual-folder behavior and verify the real Helium request before
the final release of this extension. The prior process-context lookup failure is
resolved in the restarted caller; its lower-level cause is not inferred.
The user's current sidebar report also requires consistent drive display names:
Folders uses the same fallback label as Drives for an unlabeled volume. Do not
rename the actual filesystem volume. Media-engine alternatives from the user's
VLC/NuvioDesktopDV forks were initially a feasibility review; the user's subsequent
"go ahead, libmpv" explicitly authorizes PREVIEW-15 in this same S8 extension.

PREVIEW-15 (S8, already authorized): replace Video and audio's WPF/installed-codec
backend with a bundled, version/source-pinned libmpv runtime using the user's
VLC/NuvioDesktopDV Dolby Vision work. Keep the existing module enable/disable
setting, Pathweft theme and preview ownership; no standalone installed player,
Kotlin/Qt UI import, extra engine chooser or codec downloads. Render into a
native child window through gpu-next/libplacebo, without PNG/CPU frame roundtrips
or loss of Dolby Vision metadata. Reuse the proven DV pipeline's exact source,
runtime identity, relevant configuration, notices and corresponding sources;
do not assume that merely using libVLC carries the fork's MPV/DV implementation.
Maintain video/audio play/pause, seek, duration/position, Space-window autoplay,
side-pane pause until Play, keyboard controls, errors and deterministic teardown
when changing selection, disabling/closing preview or shutting down. Retain themed
icon controls and accessible labels. Verify real SDR/audio and representative
DV Profile5/8 rendering against the available known-good fork fixtures/reference,
negative color control, actual native playback/events/EOF/teardown and hidden
pane/popup integration. Qualify the known-good SDR/sRGB conversion; native Dolby
Vision display signaling and full Profile7 FEL are not claimed. Include native
runtime/provenance/source in the normal final release and independently verify
the actual downloaded package and installed integration. No intermediate release
or unrelated stage is authorized by this extension.

SHELL-02 clarification (user: "I clicked Show in Folder", followed by permission
to test while away): the actual failing Helium action is Show in folder. Its
observed boundary is SHOpenFolderAndSelectItems, not the separate Open downloads
folder command assumed below. The real Helium action remains a required S8
integration criterion; shipping a Folder/explore mapping or editable JSON alone
does not satisfy it. Preserve the requested file selection where the native
request supplies it. This clarification supersedes the earlier exclusion of
that action from this correction; it does not declare that API implemented.
Retain the separate Open downloads folder criterion and prior Explorer/virtual
folder protections. The user permits controlled tests while away; preserve
unrelated running applications, browser state and other workflow ownership.
The released1.1.12 dynamic editor remains verified separately. Do not ship a
speculative follow-up or claim the real browser boundary passes.
Publication occurred before the real Helium acceptance criterion passed; that
delivery gap remains explicit. The release is not evidence of SHELL-02 completion.

SHELL-03 (S8 ACTIVE, user-authorized extension before1.1.12 publication): store
folder-class verb routes in folder-routes.json inside the selected settings
directory. Settings > Windows integration includes a themed row editor with
per-route enable checkboxes, class/verb fields, Add/remove and Apply controls;
support editing/reloading the JSON without recompiling. Classes are the existing
filesystem-folder classes Directory, Drive and Folder; verbs are editable Windows
verb identifiers. Seed the existing routes, including Folder/explore. Master
folder integration remains opt-in. Applying disabled/removed routes restores
their prior values; recovery stores the applied route snapshot so later JSON
edits cannot prevent disable/uninstall. Preserve later-manager changes. Validate
duplicates/invalid identifiers and malformed JSON before native changes; keep
existing configuration and binding when applying fails. Verify actual editor
actions, persistence/reload, active/off behavior, old registration migration and
restoration after removing a route, using isolated registry and hidden100/150%
dark/light layouts. Reuse the current settings design; no additional approval
gate or separate release. The unpublished b3f583c package is superseded; publish
one final1.1.12 only after SHELL-02/03 and their integrated gates pass.

SHELL-02 (S8 ACTIVE, already authorized): cover the explicit Folder-class explore
request used by Chromium/Helium's Open downloads folder action through the
existing opt-in native handler. Preserve navigation within Explorer, native
virtual folders, prior handler values and later third-party changes. Upgrade an
intact existing opt-in registration without enabling an opted-out installation.
Verify the exact Folder/explore registration in the shipping assembly,
registration upgrade/disable restoration and hidden32/64-bit native COM/browser
navigation. Live ShellExecute caller checks escaped their process-local fixture
and opened Explorer despite SW_HIDE; the user is closing those popups. Those
results are failures, not acceptance. Stop that host test and use a user-controlled
Helium Open downloads folder click for the final actual browser boundary.
Deliver one normal1.1.12 release, independently retrieve/verify it and verify the
installed registration. No address-bar change: the user confirmed the apparent
blank-folder problem was an unsubmitted address. Direct reveal-file API coverage
and other postponed stages remain outside this correction.

CORE-17 (S8, user-authorized storage correction): initially keep small settings on C:;
place installed bulk archive working copies, folder-size caches, editor WebView2
data, child-process runtime scratch and preview caches on D: under a per-user Pathweft data directory. A small
data-root.txt setting may select another non-system data drive; never silently
fall back to C:. Preserve isolated test/explicit portable profiles. Preserve
existing folder-size cache data and legacy working-copy path bindings until
their consumers finish. The user identifies the historical VLC extraction as
abandoned failed-drag output: release that exact copy with reviewed cleanup.
Known failed/cancelled drags automatically release materialized copies;
accepted external handoffs retain their actual consumer lifetime. Verify real
archive extraction/drag outcomes, cache writes/legacy reads, editor profile paths,
legacy-copy release and installed storage migration/cleanup. Migration must wait
for old writers to exit, verify destination content and preserve settings.
The user extends CORE-17 with Settings > Storage > Change location: select a
data folder, move existing settings/runtime data after graceful app/helper exit,
verify copied content before switching the startup pointer, remove only the
verified old members, and reopen with the same settings/integrations. Only a
small location pointer remains in Roaming. Display current location and a concise
success/failure message. Reject nonempty/overlapping/system-drive destinations;
block while transfers, editor tabs or retained external working copies are in
use. Preserve source data and the current binding on pre-commit failure. Verify
the real relocation worker, UI action and themed layouts before final release.

CORE-16 includes the user's requested dynamic Message grid column for file-open
failures, with full-detail tooltip and clearing on successful retry. Do not bury
these errors in the bottom status path. Verify actual row/column rendering.
Root-cause evidence identifies installer-inherited RedirectionGuard enforcement:
the running main rejects the registered Scoop player junction with error448;
Explorer uses audit mode. Inno6.7.4 documents this inheritance for lowest-privilege
installers. Correct desktop application/helper launch ownership while retaining
the installer's protection; do not disable Windows or installer mitigations,
rewrite user associations, or hardcode resolved player executable paths.

## 1. Intent and current scope

Current correction: S8 COMPLETE for CORE-16/17, delivered as normal1.1.11 at
76694a9. Source, local/retrieved package workflows, actual installed identity,
desktop-context restart, complete profile relocation and exact cleanup pass.
No current S8 blocker or tracked deferral. The diagnostic history follows.

The correction began with CORE-16 after the user's live reproduction
under the prepared, temporary read-only launch trace. MP4 dispatch reaches
ShellExecuteExW and fails with HRESULT0x800401F5; TS/PDF/XLSX return success,
with unexpected association-choice dialogs reported for TS/XLSX. The temporary
trace is detached. Synthetic double-click,
custom context Open and actual Windows association receivers pass without a
production behavior change; they do not establish the cause of the live failure.
The user reports ordinary MP4
double-click and custom context Open doing nothing, while Explorer and native
Open with work. Trace the shared action to its root before changing behavior.
Correct and verify the general open contract across ordinary associations,
double-click/Enter/context Open, editor enabled/disabled, directories and archive
working copies. Preserve explicit edit/preview semantics. No MP4-only routing,
host media playback, desktop takeover or unrelated shell registration changes.
Use synthetic receivers for actual association-boundary checks. Source acceptance
and one final normal release/retrieval/installed verification are required;
previous 1.1.10 evidence below remains historical.
The live trace proved the inherited mitigation root cause and is detached;
the desktop launch correction and general action/message checks pass in source.
Storage correction, integrated verification and final1.1.11 delivery are verified,
including the real desktop-profile worker and ready integration helper. The
installed app retains desktop audit mode; the installer retains its protection.

Current correction: S8 COMPLETE through1.1.10 for PREVIEW-13/14. The installed1.1.9
orbit behavior passed scripted correctness but regressed rendering performance. PREVIEW-14
requires processing every ordered camera sample without drawing for each sample,
retaining one explicit frame capture per render batch and completed-frame
presentation. Port the selected pinned F3D controller math if its input API
couples camera updates to immediate rendering; this implementation choice
preserves the user's native orbit behavior rather than requiring that API.
Compare camera state with the pinned native controller for circular/reverse/pole
and pan paths; measure whole batch input work, capture and transfer for realistic
bursts on the real model. Require zero implicit input renders and no sample/path
loss, quality reduction, arbitrary timer, throttle or input-delay workaround.
For native numeric comparison, position/focal and ordinary up components use
1e-7 absolute tolerance. At the native1e-10-degree pole clamp only, up components
allow1e-4 (<.006 degrees): the projected basis is ill-conditioned there, while
position/focal stay strict. Pole limits/upright behavior remain separately tested.
Deliver and independently verify one final1.1.10 release and installed update.

Reconciliation: camera/native parity, ordered burst/zero implicit draws, full
gesture/preview/frame-pressure acceptance pass. Normalv1.1.10 sourceaf82ca29eb46261ea62e2cf193f664333923d844
and all six assets match; local/retrieved real native/app workflows pass. All792
installed files and reopened mainPID52824 match; the user reports performance
fixed. Current external view has no helper owner/startup/stored wine opt-in, so
no fresh Win+E readiness is claimed and no setting is changed. Exact task cleanup
passes. No ACTIVE stage or S8 blocker/deferral. S9/S11 remain NOT STARTED and
S6/S7 postponed. See the1.1.10 verification record.

PREVIEW-13 was authorized by the user's circular
mouse-gesture regression report. Use the pinned F3D default input controller's
fixed model-up orbit/pole behavior instead of camera-local trackball math.
Circular drags must expose sides/underside without accumulating unwanted roll.
Preserve current right/middle/Shift pan mapping, wheel zoom, camera buttons,
frame presentation, DPI/resize, animation and native worker/cancellation ownership.
Verify an ordered circular/reverse circular gesture, vertical pole limits,
camera-up/focal/distance, pan/zoom/reset, actual large-model previews and teardown
through hidden app actions; compare with pinned native controller source/API.
The1.1.9 normal release, independent retrieval and installed updater hashes/main/
helper readiness passed; the subsequent performance regression reopened S8 and
is resolved in1.1.10. No unrelated S9/S11/S6/S7 work.

Historical extension: S8 is COMPLETE through1.1.8 for the previous refinement round.
PREVIEW-12: correct continuous mouse rotation/frame presentation, remove PNG
encode/decode from interactive rendering, size native frames for the viewport
and DPI, preserve camera/animation/cancellation/resource ownership, and provide
F3D-quality anti-aliasing/lighting/grid presentation consistent with the theme.
Inspect the pinned native API/source; verify sustained mouse gestures produce
intermediate presented frames (not only a final jump), camera motion and native
pixel orientation/alpha, actual large/animated models, resize/DPI and teardown.
Measure relevant render/presentation stages; do not invent a frame-rate guarantee.
CORE-14: replace the Settings paper roll with themed vertical category tabs on
the left, widening the default window to preserve the existing content area.
Display only the selected category; retain every setting, handler/reset/persistence
and keyboard/accessibility behavior. Verify navigation/settings and unclipped
dark/light100/150% layouts, including scrolling within a tall category.
CORE-15: Help's left category becomes bold automatically for the section being
read, including scrolling, category clicks, search filtering and resize. Use
actual layout/scroll events; no polling or feedback loops that move the reader.
JUMP-03: investigate the native pinned/recent folder expansion shown in the
user's StartAllBack screenshot and its taskbar Jump List equivalent. Document
how Pathweft can expose real folder destinations, pin/recent/removal/privacy,
application identity and cold/warm launch under S11/JUMP-01..02. This request
authorizes bounded investigation and a concrete implementation plan, not a
StartAllBack patch, desktop takeover or premature S11 implementation. Deliver
PREVIEW-12/CORE-14/15 together in one verified normal release, retaining current
opt-ins/user settings and the installed updater workflow. No S6/S7/S9 work.

Current1.1.8 reconciliation: PREVIEW-12, CORE-14/15 and JUMP-03 research pass.
Normal release/tagv1.1.8 source64118e927b71d528fdc6626ae3315e4faeafcd12
has six verified assets. Actual local and independently retrieved native/app
checks pass, as do792 installed hashes and reopened main/helper readiness after
the user's updater. Exact task scratch cleanup passes. No ACTIVE stage or S8
blocker/tracked deferral remains. S11 runtime implementation remains NOT STARTED;
S6/S7 are postponed and S9 is NOT STARTED. See the1.1.8 verification record.

Historical1.1.7 reconciliation: CORE-12/13 source,
hidden real-account/navigation/layout acceptance, final package, normal six-asset
publication, independent retrieved native/app verification and the user's actual
installed update pass. Release source is
eeb5d40a0b7ef2a3799445ad352ec21a5bad2c3b, tag v1.1.7. All 792 installed members
match the released manifest; reopened main PID12928/helper PID40692 use the
installed executable, helper ownership/start identity matches and its ready
event is signalled. Recovery remains version2. Exact scratch cleanup passes.
No required S8 blocker or tracked deferral remains; no ACTIVE implementation
stage. S6/S7 remain postponed and S9/S11 are NOT STARTED. No desktop control,
forced close, reinstall or live handler rewrite was performed.
The user updated to1.1.6 through the built-in updater;
the new main application and Win+E helper are running. The user confirms Explorer
navigation stays in Explorer and Win+E works. User testing of external folder
links is expressly left until a real use case; retain the hidden native evidence
and distinguish that from a physical user check. Installed identity reconciliation
is now verified; keep the external-tool registry-view discrepancy separate from
that evidence and do not replace live handlers from that diagnostic view.

CORE-12 (2026-10-31): automatically collapse the bottom statistics strip whenever
it contains no meaningful figures, particularly This PC's fake folder total and
empty latest-change card. Collapse each unavailable card and its columns/gaps,
and reclaim the whole strip's height/margin when none remain. Preserve full,
compact and disabled preferences; navigating to useful folder/archive/search
content or changing active panes restores applicable cards automatically. A real
empty folder's available drive-capacity card can remain, while empty placeholder
folder/latest-change cards stay hidden. This PC's footer should count locations
without inventing folder/file/byte totals. Use the existing Native/Original design,
without new controls or animation. Verify actual hidden single/split navigation,
selection, empty content, global search, native/virtual archive data and settings
changes, plus dark/light100/150% layout bounds and reclaimed height. Deliver one
normal verified update after integrated acceptance; preserve live user work.

CORE-13 (2026-10-31): for Google Drive's streaming mounts, show the actual
per-account cloud capacity, used/free space and percentage consistently in This
PC, the sidebar and applicable capacity cards. The filesystem's cache-volume
capacity is not the account's cloud quota. Associate accounts through provider
mount metadata, never fixed drive letters or screenshot numbers. Prefer a
read-only provider-exposed local source; do not copy the desktop client's auth
tokens or change its streaming/cache settings. Keep local-cache figures distinctly
labelled where relevant. An unavailable quota must be explicit, and must not hide
an otherwise ready cloud mount or present local disk figures as cloud capacity.
Verify both real mounted accounts, numeric interpretation, mount changes and
unknown/unlimited/unavailable states, plus existing local/network drive behavior.
Finish this authorized refinement in the same active S8 integration/delivery.

Previous reconciliation: S8 was BLOCKED on installed verification of the SHELL-01
Explorer-navigation correction. Normal 1.1.6 is published at source
a7648b9d51c63d5aadac9e96b48ecbd8a428581c. Independently retrieved hashes, real
32/64-bit native Shell/COM/browser workflows and packaged app checks pass.
Scratch cleanup is verified. The existing 1.1.5 main application is still running;
the installer requires its normal user-controlled exit. Installation and its
actual installed routing/identity/profile-preservation checks remain required.
Resume them after that exit; do not force-close user work. Overall S8 is not
complete. S9 remains NOT STARTED.
The correction uses a native DelegateExecute handler: a Shell browser site routes
through that browser's BrowseObject, while an external filesystem request launches
Pathweft. Keep 32/64-bit COM registration reversible, migrate only wholly owned
old opt-ins, and retain disabled/third-party owners. Version native handler files
so an update never needs to terminate an Explorer caller holding the previous DLL.
Refresh folder integration's Settings state when its window activates; its old
constructor-time label is not evidence of current handler ownership.
Earlier S8 is COMPLETE through the user-requested1.1.5 refinements
below, normally published at source3fa4b465775ce411a89ba2628b1907420facac50.
Independent retrieval, actual packaged workflows, installed hashes/app-host
self-test, preserved settings/routing and exact scratch cleanup pass. There is
no blocker or required deferral in that prior closure. The earlier authorized S8 scope through CORE-09/10 and
PREVIEW-06/07 is COMPLETE in normally published and independently retrieved/
installed1.1.4 (source8552fbdc4a0c38acf7cbb76d98d1ef3d227b6063), with exact
scratch cleanup verified. This does
not complete the separately postponed S6/S7 or unstarted S9/S11 requirements.

Additional user-directed S8 refinements (2026-10-31), CORE-11 and PREVIEW-08:
show rounded free-space percentages beside the measured free-space number in
This PC, computed from each volume's real available/total values. Share usage
warning colors between This PC and sidebar: blue below 80% used,
amber from 80% and red from 90%. These are explicit Pathweft thresholds, not a
claim that Explorer implements an amber tier. The user's subsequent clarification
requires unavailable/no-media drives hidden in This PC as in the sidebar;
refresh must restore them when media is available. This supersedes CORE-09's
earlier placeholder-row presentation, without hiding ready cloud/network volumes.
Identify supported virtual cloud volumes using Windows mount/provider metadata,
never drive letters or account names; show cloud/provider identity instead of
misleading physical FAT32/exFAT labels, retaining raw format in tooltips.
Replace F3D and PDF preview text actions with conventional icon buttons and
tooltips/accessibility names. Theme the animation selector, including its popup,
focus and selection states. Composite model rendering into the preview pane so
there is no contrasting black rectangular cutoff. Keep camera, animation and
PDF navigation behavior. Verify real cloud mounts, warning boundaries and actual
large-model/animated/PDF controls in hidden dark/light layouts at 100/150% text
scale; then deliver one normal 1.1.5 release, independently retrieve and verify
its package, and install with existing settings/routing preserved.
PREVIEW-09, added by the user's executable-properties screenshots: show a themed,
selectable details view for .exe metadata including available description/company,
file/product versions, copyright, original filename, language, architecture,
file size, dates and location. Read resources/PE headers only; never execute the
selected application. Missing version fields stay omitted, malformed headers
must not crash the preview. Verify actual signed/unsigned and no-version files,
source preservation, selection changes and module disable behavior.
PREVIEW-10, subsequent user clarification: extend the themed expandable folder/
file tree to every ordinary native folder and virtual folder inside supported
compressed containers. Read native branches asynchronously only when expanded;
preserve names, type icons, file sizes, natural folder-first sorting and hidden
visibility. Show clear empty/inaccessible-folder states, without extracting or
executing files. Cancel obsolete branch loads on selection change/closure.
PREVIEW-11: when the active pane has no selected item (including the parent row),
collapse the side preview and its splitter/column completely. Restore the saved
preview width on a new selection when preview remains enabled; do not change the
user's enabled preference. Caption buttons must remain correctly reserved in
single/split layouts. Verify actual selection clearing/restoration, disabled
preview, pending folder/model cleanup and themed rendering without desktop input.
These additions supersede the still-unpublished initial1.1.5 package atbb946b7;
no intermediate release is authorized.

Additional S8 user requirement (2026-10-31), CORE-09: This PC must show drive
capacity/free space and usage bars using the same measured volume data as the
sidebar, with useful network share/provider details and capacity bars when
available. Show external-drive connection speed from Windows' actual USB link
information, including the reported E:/G: 10-Gbps expectation; distinguish the
negotiated connection from device capability and measured transfer throughput.
Unknown/unavailable media or unsupported speed reporting must remain honest.
Read device/volume information asynchronously without administrator prompts,
write benchmarks, USB resets or desktop takeover. Verify real local/external/
network drives, navigation/refresh, missing-media states and themed layout.
Integrate this before the pending final release; earlier unpublished packages
are superseded qualification output, not separate deliveries.
CORE-10 adds dynamic detail columns: retain Name; omit columns with no meaningful
data in the current folder/search/drive view, restore them when asynchronous
metadata or incoming results supply values, and preserve widths/applicable sort/
navigation. A drive-only sort resets to Name when entering file search.
Use drive-specific capacity/free/filesystem/connection columns in This PC and
normal file metadata elsewhere. Verify empty/mixed folders, async version/size,
search streaming, drive-to-folder transitions and resize without empty columns.

Post-v1.1.3 user additions (2026-10-31), owned by S8: replace the About
Diagnostics expander/telemetry with an Open settings folder button (CORE-03).
PREVIEW-06 replaces flat archive text with an expandable, themed folder/file tree
matching the sidebar Folders selector, concise names, keyboard expansion,
scrolling and sizes; enumerate metadata without extracting entries, preserve
empty/implicit directories, and release obsolete preview work.
PREVIEW-07 promotes the F3D module beyond the GLB prototype: remove its file-size
and embedded-resource restrictions, select the formats supported by the bundled
readers, and provide mouse orbit, pan, wheel zoom, fit/reset and keyboard access,
plus native animation clip selection, playback and timeline when present.
Keep module opt-in, native rendering inside Pathweft, asynchronous rendering and
actual worker teardown. Verify a real user model above 64 MiB, camera/frame
changes, supported external-resource models, disabled/malformed cases and
unchanged source assets. Engine reader/rendering limitations remain explicit;
3D editing and formats unavailable in the bundled engine are not promised.
Complete integrated hidden/rendered checks, normal final release, independent
package retrieval and installation verification. No Sandbox or desktop takeover.
The preceding v1.1.3 correction below is delivered and verified.

Post-v1.1.2 correction: clicking outside the search popup must dismiss it even
if WPF still reports keyboard focus in the query field during deactivation.
Moving focus into its owned preview keeps that interaction open; moving outside
the popup/preview dismisses both. Escape remains available, and reopening must
not be closed by a previous deactivation callback. S8 owns this WIDGET-01/02
correction, focused hidden regression checks and final v1.1.3 delivery.
The same correction includes the reported archive-to-folder drag freeze and
delayed drop: internal archive drags must start without extracting on the UI
thread, submit the existing archive transfer queue on drop, preserve source
contents and interoperation, and never drop into a later pointer location after
preparation. Restore the redirected Windows Downloads known folder in Favorites.
Existing Explorer windows must retain their own subfolder navigation while
opt-in external folder launches route to Pathweft; SHELL-01 owns this boundary.

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
The user subsequently revised that target: **retain WSearch for Outlook, reduce
its filesystem indexing scope and use Everything for filesystem search**. This
explicitly supersedes service retirement; unavoidable Windows-managed indexing
exceptions must be identified. That scoped migration is **S7**, after S6;
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

On 2026-10-30 the user clarified S6's presentation: retain StartAllBack's grouped
layout, show a reduced Everything-backed subset within each file category, and
use See more results for the complete EverythingToolbar-derived view. A handoff
that leaves Windows Search producing the compact file results is not the target.
Preserve Programs and Settings functionality if technically achievable and qualify
their actual backend dependencies. The user also requested StartAllBack folder
launches to prefer Pathweft, and investigation of Directory Opus's Explorer
replacement as the reference for replacing the inherited FileLabs window watcher.
StartAllBack-specific routing belongs to S6; broader default-handler replacement
and its final delivered recovery belong to the existing S8 SHELL-01 scope. The
conditional discussion of WSearch being disabled does not authorize disabling the
host service or remove the retained Outlook requirement.

The user's subsequent update clarification makes current-version qualification
the S6 test direction. Do not downgrade Windows or StartAllBack merely to retain
an earlier test baseline. Keep earlier-build findings as comparison evidence and
qualify changed private interfaces on current builds. Exact artifact identities
remain required for each capture and supported combination; they do not impose a
product requirement to freeze updates or promise unverified update compatibility.

The user subsequently explicitly directed execution to **jump to S8** and deliver
the file explorer before continuing the broader Windows search work. S8 now
depends on completed S1-S5/S4R/S4U, not S6/S7. S6 is parked by this user-directed
priority boundary and S7 remains unstarted; their requirements remain required
later work, outside this core file-explorer release. They are not satisfied by
S8 or removed from the program. Overall program completion remains distinct from
this S8 core release. The Sandbox was shut down at the user's request.

S8 must keep shell integration opt-in in its proper Windows integration settings
section, review and selectively adopt useful upstream changes before compilation,
and provide an alternate restrained Windows-style theme with accent support.
Use the user's File Labs and Explorer screenshots plus Huashu design guidance as
references; retain the existing appearance as a selectable option. This explicit
core-first delivery instruction supersedes earlier all-stages-before-release
ordering for this release only. It does not authorize resuming S6/S7 or changing
live Windows Search/Outlook configuration.

The user requires subsequent verification without taking over active host
windows. Prefer the existing app-owned hidden diagnostics and direct native
Shell callers. Do not activate their windows or synthesize desktop input.
Keep any remaining actual menu-caller criterion explicit until user-controlled
or genuinely isolated evidence qualifies it; hidden rendering alone does not
prove StartAllBack's menu action. This execution constraint does not waive
SHELL-01 or authorize restarting the parked Sandbox/S6 work.

The user then explicitly confirmed that the StartAllBack-specific folder-link
check belongs to postponed S6. Normal filesystem folder/shortcut routing has
already been qualified independently; do not make further Start-menu actions
an S8 core-release gate. SAB-06 owns the actual StartAllBack entry points,
including their Shell/known-folder/library distinctions and direct routing.
This preserves that requirement in S6 rather than waiving its eventual acceptance.

On 2026-10-31 the user explicitly stopped all Sandbox work and directed S8
delivery and installation on the host. Use the completed actual per-user host
install/update/uninstall and preservation trials, then verify the final published
and installed package. A separate Sandbox/guest acceptance run is no longer an
S8 gate. Keep integration opt-in and preserve existing settings during installation.

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
| StartAllBack | Host 3.9.25; current isolated qualification 3.9.28 on 2026-10-30; identify exact artifact/hash and Windows dependencies for each capture | Proprietary host for a version-bound native search adapter; current-build qualification replaces routine downgrades; no donor binary redistribution or provider-hook feasibility claimed |

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
| CORE-03 | One settings interface owns integrations and module controls; normal views remain uncluttered. Settings starts with actionable preferences, not a search implementation/qualification report. Keep search instructions, version/paging/coverage details and verification terminology in Help or developer documentation; preserve concise actionable availability/unsupported-operation messages. Before accepting UI changes, establish concrete layout/display-scale expectations and inspect current rendered screenshots at representative sizes. |
| CORE-04 | S8 provides an alternate Native appearance with neutral surfaces, restrained selection/focus and Windows accent support. Appearance settings select the existing or Native theme and system/custom accent; persist the choice and apply it consistently to panes, settings, taskbar search and product dialogs. Follow Windows light/dark and high-contrast preferences where supported; retain readable text and keyboard focus. Inspect actual rendered single/split layouts and settings at 100%/150% scale and representative window widths, including live theme/accent changes and restart persistence. Keep the existing theme available. |
| CORE-05 | Before S8 compilation, review upstream source/dependency drift against the imported revisions, record concrete adoption/non-adoption decisions and integrate applicable fixes. Preserve fork identity, implemented Everything/native archive/preview/transfer behavior, source provenance and notices. Adopted behavior receives focused regression checks and the final integrated release verification; do not blindly replace fork implementations or upgrade dependencies without a relevant benefit. |
| CORE-06 | Automatic file opening (double-click/Enter) with the internal editor enabled must use the Windows association when the editor cannot read the file, without an editor error or empty window. Check actual readability before creating the editor, reuse decoded content once accepted, and preserve an already-open document's unsaved content. Apply the same decision to archive working copies while preserving their ownership. Disabling the editor uses Windows associations; explicit F4/Edit continues to request editing. Verify unsupported extensions and rejected HTML content/size through the actual double-click route, supported text/HTML, disabled-editor behavior, archive routing and absence of host focus. |
| CORE-07 | Replace the app-wide pink tile/dark-folder icon with a transparent Windows-yellow/gold folder, retaining the two-pane list motif with three left lines, two right lines and the central divider. Use contrasting brown marks, remove the bottom-right line, and fill the icon canvas enough to match Explorer's apparent folder size (about 92% width). Include native Windows icon sizes from 16 through 256 pixels. Apply through the shared WPF resource, executable/window/tray/shortcut identity and installer/uninstaller; verify alpha/color, small-size legibility and final packaged resources. |
| CORE-08 | Replace the sidebar's hard top/bottom scrolling cutoffs with a soft opacity fade over the final18 logical pixels of the content viewport, only on edges with content beyond them. Fade the viewport content, keeping the scrollbar, app title and footer controls sharp and interactive. Respect resize, scroll, section/extent and 100%/150% text-scale changes; show no fade when all content fits or in Windows high contrast. Use transparency over the existing background rather than a hardcoded color. Inspect actual top/middle/bottom WPF renders and verify scrollbar/input ownership without host desktop input. |
| CORE-09 | S8 This PC drive capacity/free/usage and network details plus truthful external connection speeds, as specified in current scope. Verify actual inventory, live refresh/navigation, unknown media/speed, native USB evidence and dark/light responsive layout. |
| CORE-10 | S8 dynamic detail columns described in current scope; verify data-driven visibility, asynchronous metadata/results, width/sort preservation and contextual navigation. |
| CORE-11 | S8 measured free-space percentages, shared capacity warning colors and provider-based cloud drive identity, as specified in current scope. Verify actual mounts and unavailable capacity. |
| CORE-22 | S17 approved yellow-folder/dark-compass application icon. Transparent multiresolution assets must remain legible at small sizes, retain polished larger detail, appear in embedded WPF/native/package surfaces, and preserve the existing Windows application/shortcut/Jump List identity. |

On 2026-10-31, the user also requested EverythingToolbar's four inline search
toggles: Match case, Match whole word, Match path and Regex. They belong at the
right end of the single address/search field, with accessible names, tooltips,
visible selected states and actual Everything behavior. Match whole word is
disabled while Regex is enabled, matching the pinned donor. Options stay local
to each pane/search session and cross widget-to-pane handoff with its query;
they must not leak across the shared native client or bypass folder scope.

After verified v1.1.1 installation, the user reported the Settings action
"Restore previous folder handlers" clipped by the shared tab style's width cap.
CORE-03 requires its complete action label to fit or wrap at the minimum440-DIP
Settings width and at 100%/150% text scales. Preserve the width cap for actual
tabs and the existing restore action; do not invoke live recovery during visual
verification. This bounded correction remains S8 and receives a normal v1.1.2
patch through the existing release/install channel; v1.1.1 stays immutable.

On 2026-10-31, after v1.1.0 delivery, the user reported the Original theme's
active header/glow, excessive Settings prose, and the Native window's missing
green outer activation border. This reopens S8 for the bounded CORE-03/04
correction. Native window frames must use the actual Windows accent for the
active outer border when Windows enables colored title bars/borders, revert on
deactivation, refresh on system changes, and retain neutral caption surfaces.
Keep the existing Original theme selectable. Verify native DWM call results and frames
without taking host focus and inspect Settings at 100%/150%; no Sandbox/S6 work.
The user's later color clarification uses the sidebar's near-black background
for the Native dark outer window area behind the panes and summary cards; retain
the gray file-pane surfaces, Light mode and Windows high-contrast behavior.
The user subsequently reported a brown intermediate square on close and requested
black or immediate disappearance. After the header correction, qualify the
closing path and remove any Native client-only fade that exposes a colored DWM
surface. Preserve ordinary Windows closing behavior, settings and active work.

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
| SEARCH-01 | Use the actual Everything index inside panes, reusing selected EverythingToolbar client source. The user's 2026-10-31 clarification replaces the mode picker and separate search row with one browser-style address/search field in the existing path row. On Enter, first resolve the text as an existing path (including quoted, environment-variable and relative paths); navigate to a folder or reveal a file. If it does not resolve, search Everything within the current folder. This PC supplies global scope. Remove the second row, scope selector, funnel and search icons. Ctrl+L and Ctrl+F focus this same field. Preserve navigation/history, archive browsing, Unicode/spaces, wildcards/regex, paging and cancellation; compare against the real index. |
| SEARCH-02 | Page/lazily retrieve results and indexed metadata/sorts; do not eagerly decode every icon or enumerate all hits. Superseded queries cannot replace newer results. Verify rapid query changes, paging/sorting, empty results and changing/deleted paths. |
| SEARCH-03 | Share one deliberately selected engine instance/index between panes and widget, reusing an existing compatible instance where appropriate. Pin the binary/API version and own readiness/reconnect/instance lifecycle explicitly; never silently create a duplicate index. Keep the GUI unprivileged and necessary service privileges at the indexing boundary. |
| SEARCH-04 | Display concise true coverage and actionable engine availability. Under the user's 2026-10-31 Everything-only clarification, remove the filesystem-scan mode from the product UI; a failed path lookup falls back to Everything, never to a filesystem crawl. Unindexed/offline/network/non-NTFS coverage depends on the actual existing Everything index and must not be presented as complete. Path resolution must remain responsive and a superseded resolution/query cannot navigate or replace newer state. |

Integrating Everything does not itself authorize disabling Windows Search,
changing Outlook indexing or modifying the existing engine configuration.

### Taskbar workflow and shared actions

| ID | Requirement and acceptance |
| --- | --- |
| WIDGET-01 | Integrate selected EverythingToolbar controls/taskbar code with the product's settings/update owner. The user's post-v1.1.1 design correction requires a compact global search field with the four existing match toggles and a clean name/location/size/date results list in Pathweft's theme. Search as text changes, cancel superseded work, preserve paging and hidden-file rules, and show no drive listing for an empty query. Remove the duplicate title/settings row, browser navigation, destination field, view/sort footer and action bars. Enter/double-click opens results; folder/archive results open in the main manager. Escape/deactivation dismisses, Down focuses results, and Ctrl+Enter hands the query/options into a main pane. Verify actual query/result/open/handoff, errors/cancellation/paging and current rendered minimum/normal layouts at100%/150%. |
| WIDGET-02 | Keep query, focus and selection independent per surface and reuse the existing Everything/result-open/preview owners. The user's compact-widget correction removes widget-specific copy/move/destination controls and their dedicated transfer workflow; transfers remain in the main manager and its existing shared queue. No standalone utility dispatch or second indexing/transfer owner. Preserve ordinary result opening and optional Space preview without adding visible feature bars. Original S2 transfer-widget evidence is historical, not acceptance of the new compact UI. |
| WIDGET-03 | Make taskbar integration explicit and optional. Verify the selected search-box mode on the supported Windows build across Explorer restart, focus, keyboard and monitor/DPI changes. Where the box cannot be hosted, provide an explicitly reported supported pinned-icon/hotkey mode. Do not replace the desktop shell. |

### Internal preview modules

| ID | Requirement and acceptance |
| --- | --- |
| PREVIEW-01 | Port the necessary QuickLook host/contract and selected viewer/add-on source into built-in modules for the supported modern .NET runtime. Do not depend on standalone QuickLook, its tray/global hook/updater/installer. Verify previews with standalone QuickLook absent. |
| PREVIEW-02 | Initial source-integration scope: text/code, Markdown, images, PDF and archive listing. Preserve media/audio and font previews as internal selectable modules, reusing current code where suitable. Record the exact shipped module/source/dependency/format inventory before S3 acceptance. Importing every plugin was not required for S3; S13 separately adds the fourteen specialist categories under PREVIEW-21..36. |
| PREVIEW-03 | Persist per-module enable/disable settings. Disabled modules are not selected or initialized; disabling the current module releases its preview resources. Resolve overlaps by deterministic priority or explicit preference. Show an eligible alternative or honest unsupported/disabled state. Verify toggles, active-disable and restart persistence. |
| PREVIEW-04 | Space, side-pane and widget previews share selection/lifecycle rules. Rapid navigation cannot show obsolete content. Close/change releases documents, playback and native resources. Previewing must not execute a selected executable or run document macros/scripts. Verify representative formats, malformed/large/unsupported inputs and file release by rename/delete after closing. |
| PREVIEW-05 | User-authorized S3 prototype addition (2026-10-29): add an opt-in F3D-backed GLB preview module and verify the user-specified Castlevania GLB inside Pathweft. Inspect/adapt F3D source and its supported library API. Keep module settings and resource lifetime under the internal preview host; verify a real model render, camera controls, disable/close cleanup and unchanged original asset. The private asset path/hash belongs in ignored trial input, not public source. This adds a bounded 3D prototype, not arbitrary 3D format parity or editing. |
| PREVIEW-06 | S8 archive preview tree described in current scope; verify the reported real ZIP and nested/empty/implicit folder fixtures, theme/layout, keyboard expansion, metadata-only browsing and lifecycle. |
| PREVIEW-07 | S8 full embedded F3D viewer described in current scope, superseding PREVIEW-05's prototype restrictions; verify actual bundled-reader formats and large-model interaction/rendering, opt-in and resource release across preview surfaces. |
| PREVIEW-08 | S8 themed icon actions in PDF/F3D, animation selector and transparent model composition; preserve interaction and lifecycle, with dark/light responsive evidence. |
| PREVIEW-09 | S8 executable details preview described in current scope; verify actual resource metadata, source preservation, no execution, missing resources and module settings. |
| PREVIEW-10 | S8 common tree presentation for native and virtual compressed folders, lazy asynchronous native branch reads, hidden visibility and resource lifecycle, as specified above. |
| PREVIEW-11 | S8 selection-driven preview collapse/restoration without altering the enabled preference, with correct saved width/caption layout and resource cleanup. |

Compiled-in modules with on-demand initialization are sufficient. Assembly
unloading, a dynamic plugin marketplace and a general extension framework are not
requirements. The explicitly requested F3D GLB prototype is included in S3;
All fourteen missing specialist categories, including Office, are scoped to S13.

### Remaining specialist preview modules (S13)

The comparison baseline is the [QuickLook viewer directory](https://github.com/QL-Win/QuickLook/tree/master/QuickLook.Plugin)
reviewed on2026-11-02:24 viewer categories plus PluginInstaller. Pathweft has ten
corresponding categories, with narrower format/features in several. S13 implements
the fourteen missing categories below; it does not claim exhaustive format parity,
silently expand the existing ten modules, import the separate add-on catalogue or
add QuickLook's plugin installer/marketplace. Existing gaps remain documented.

| ID | Required category | Required behavior and acceptance |
| --- | --- | --- |
| PREVIEW-21 | Shared integration and exact inventory | Pin the selected source/dependencies and record an explicit supported-format/feature matrix for all fourteen categories before implementation. Reuse the existing PreviewSession/IViewer, module settings, priority, side pane, Space and widget paths; initialize only enabled selected modules. Use source/native integration without requiring standalone QuickLook or launching associated applications. Preserve PREVIEW-03/04 and LIFE-01. A generic text view, file icon, external application or metadata-only substitute does not satisfy a specialist rendering requirement. Missing required capabilities are acceptance blockers, not permission to drop a category. |
| PREVIEW-22 | OfficeViewer | Embedded readable document, spreadsheet, presentation and applicable diagram/OpenDocument previews through a qualified renderer or native preview-handler boundary. Verify actual page/sheet/slide content and navigation, required handler/runtime availability and explicit unavailable/error states. Never execute macros, remove Zone.Identifier/protected-view state automatically, or change the document. State actual format and installed-handler dependencies; opening Office externally is not acceptance. |
| PREVIEW-23 | CsvViewer | Render delimited files as navigable tables with headers/columns, quoted delimiters, embedded newlines, encoding handling and explicit large-file limits. Verify CSV/TSV and the adopted delimiter set; raw source text is not table rendering. |
| PREVIEW-24 | DbViewer | Read-only database type/schema/table inspection and bounded row browsing for the adopted SQLite and LiteDB formats. Verify correct rows/types, locked/encrypted/malformed and large-database states; do not migrate schemas, execute document-supplied queries, modify source/sidecars or acquire avoidable write locks. |
| PREVIEW-25 | MailViewer | Render EML/MSG headers, body and attachment inventory without opening Outlook or executing active content. Preserve HTML isolation; remote images, scripts and automatic attachment opening remain inactive. Verify plain/HTML/multipart and malformed messages with source preservation. |
| PREVIEW-26 | CertViewer | Inspect certificate, certificate-store, signature and provisioning metadata for the adopted donor formats, including public identity, validity, issuer and applicable chain/signature details. Verify protected/malformed inputs and explicit password/unavailable states without persisting secrets. Do not install certificates, export private keys, modify trust stores or imply unperformed online validation. |
| PREVIEW-27 | AppViewer | Structured application-package inspection: identity, version, publisher, package contents and available icon/permissions for the adopted Android, Windows and other donor package families. Verify real representative packages for each declared family; archive listing alone is insufficient. Never install, execute, mount through an unsolicited system action or unpack entire packages merely to preview. |
| PREVIEW-28 | ThumbnailViewer | Render real embedded previews/thumbnails for adopted design formats such as Krita, Paint.NET, Sketch and the other source-qualified donor formats. Record per-format support and missing-thumbnail behavior; a Shell file icon or generic metadata view is not a design thumbnail. Verify malformed packages and bounded extraction with no editor launch or source changes. |
| PREVIEW-29 | MediaInfoViewer | A selectable structured technical report with container, codec, duration, video/audio/subtitle tracks and available color/HDR metadata. Verify displayed values against actual files independently of playback; preserve libmpv playback and Dolby Vision behavior. Playback controls alone do not satisfy this report. |
| PREVIEW-30 | BinaryViewer | Read-only hex/ASCII inspection with correct byte offsets, bounded large-file loading and navigation. Verify empty, malformed/unknown and large files; preserve source bytes and ensure binary inspection does not incorrectly win over a more specific viewer. |
| PREVIEW-31 | ELFViewer | Structured ELF and adopted Mach-O/UImage metadata: format, architecture, headers/sections and available dependencies. Verify real representative inputs for each declared family, truncation and invalid headers; do not load or execute inspected code. |
| PREVIEW-32 | DumpViewer | Structured crash/minidump inspection showing available process, exception, module and thread information with explicit unavailable fields. Verify supported dump variants and truncated/large inputs. Do not imply a stack trace or symbol resolution that was not performed, or initiate automatic symbol/network downloads. |
| PREVIEW-33 | PrefetchViewer | Parse supported Windows Prefetch records into executable, run-count/time and referenced-path information where present. Verify source-qualified compressed/version variants, invalid/truncated inputs and truthful unavailable fields; never execute the referenced application or alter system Prefetch data. |
| PREVIEW-34 | ChmViewer | Read-only embedded CHM topic/content navigation with bounded resource access. Block scripts, external navigation, downloads and active content; verify internal topic links, images, malformed archives and source preservation. Opening the system Help application is not acceptance. |
| PREVIEW-35 | CLSIDViewer | Inspect supported Shell namespace inputs of the form ::{GUID} and show resolved identity/registration metadata. This is a virtual-input viewer, not an invented file extension. Wire actual selection/input into the preview host and verify known, unknown and malformed identifiers without COM activation, registry writes or executing a registered command. |
| PREVIEW-36 | Integrated qualification and delivery | Demonstrate every category through actual complete selection -> rendering/inspection -> navigation -> close workflows, including eligible native/archive and side/Space/widget surfaces; document genuine input/surface exclusions. Verify per-module toggles and restart persistence, deterministic overlaps, active-disable, rapid selection, malformed/large/encrypted/locked/missing-runtime states, cancellation and post-close rename/delete with source hashes unchanged. Inspect themed accessible controls at narrow/normal sizes and100%/150% scaling. Revalidate affected existing previews and HTML/media/3D lifecycle boundaries. S13 closes only after all fourteen specialist outcomes pass, source/license/runtime inventory is complete, one normal release is independently retrieved/qualified and its actual installed update is verified. No per-module intermediate product releases. |
| PREVIEW-37 | Windowed and in-app PiP preview | Add themed accessible mode icons beside sidebar close. Show the actual renderer's app icon on the left of the filename instead of labels such as WordCraft. The icon must switch to LibreOffice when fallback takes over, including after primary failure; expose the engine/recovery reason in its tooltip. Keep sidebar, PiP and Windowed identification consistent. Windowed uses a resizable external floating window and restores the same preview to the side column upon window closure. PiP uses a compact corner overlay inside Pathweft, releases the sidebar column and offers return to sidebar. Retain renderer/navigation/playback/camera state during mode changes. The former pin-while-browsing behavior is explicitly superseded by PREVIEW-38. Keep explicit close-preview distinct from window-close restoration. Verify actual Office/media/3D/HTML/ordinary transfers, restoration, compact/narrow sizing, active-disable, cancellation and app-exit cleanup without duplicate renderers or lost source/archive leases. |
| PREVIEW-38 | Selection-following and explicitly pinned preview hosts | S17: default PiP/Windowed follows selection with updated content/title/icon, preserved host mode/bounds, correct previous renderer/lease teardown and stale-async protection. Explicit pin beside the three header buttons retains the displayed item across browsing/empty selection/host changes; unpin follows current selection immediately. Clear pin on explicit close and expose visible/accessibility state. Unpinned empty selection clears stale content. Preserve mode-switch state/restoration semantics; small glyphs with usable accessible targets. Verify actual replacement/pin/unpin, rapid changes, lease/player cleanup, themes and scaling. |

At S13 activation, qualify dependencies and exact formats against the pinned
upstream implementation and available runtime rather than accepting marketing
claims. Reuse the current ten module backends; no second preview host, generic
plugin framework, duplicate playback engine or arbitrary timeout/retry machinery.
Keep optional renderer/handler dependencies explicit. A missing implementation
keeps S13 ACTIVE; an externally unavailable required dependency may justify
parking with the exact unsatisfied criterion, not declaring parity or completion.

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

Implementation decision under the user's existing instruction to match TeraCopy
without optional protective workflows: qualified NTFS fixtures show that the
donor copies compressed and sparse contents into ordinary destination files.
Compression policy and sparse allocation are filesystem limitations to report
under TRANSFER-04, not reasons to reject an otherwise correct copy. Keep source
data, verification, cancellation and basic attributes/timestamps unchanged;
report compression-state differences and lack of a sparse-allocation guarantee.
Encrypted files still require encryption preservation or explicit failure;
encrypted directory policy remains unsupported. This decision replaces the
derived adoption document's earlier blanket storage-attribute failure rule.

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
| SAB-01 | Use the installed REA skill and Coordinator gate on an exactly identified StartAllBack artifact before selecting a hook. Record version, origin, signature/hash, Windows build and relevant loaded-module identities. Trace typed-query dispatch, grouped file-result production/materialization, cancellation, selection/open/reveal, See more results dispatch and teardown, with retained Evidence IDs. Independently trace Programs and Settings providers and qualify dependencies on filesystem indexing versus the running WSearch service. Recheck documented extension points; distinguish static evidence, actual runtime observations, inference and unresolved ABI/ownership questions. Resolve design-critical unknowns before dependent implementation. |
| SAB-02 | Provide an optional native integration that supplies Everything-backed file/folder results inside StartAllBack's existing grouped Start-menu result UI. Retain category headings/dividers and show a bounded reduced subset in each applicable group; qualify the actual library/scope membership behind categories such as Documents, Games and Modding rather than assuming extension-only grouping. Preserve app/shortcut, Settings and command functionality; a missing independent provider remains a blocker, not silently omitted coverage. Verify Win -> type -> actual indexed results in compact native groups -> keyboard/mouse selection -> correct open/reveal. See more results opens the full Pathweft-owned EverythingToolbar-derived view with the query and applicable scope preserved. A separate view/handoff without Everything-backed compact in-menu results does not satisfy this requirement. Do not require the standalone EverythingToolbar binary. |
| SAB-03 | Reuse the deliberately selected Everything instance and shared Pathweft search behavior; do not start a duplicate index. Keep the native shell adapter limited to the required query/result boundary and existing application ownership; do not host the full managed file manager inside Explorer. Own rapid-query cancellation, result identity, paging/bounds and thread/COM lifetimes through actual state/events. Verify Unicode/spaces, rapid typing, empty results, deleted/offline paths and real index unavailability; never label stale/partial results complete. The exact ABI/transport is selected from SAB-01 evidence, not assumed by this plan. |
| SAB-04 | Version/hash-bind any runtime hook to the exact validated host/module/Windows combination and verify expected target identities before attachment. Keep integration opt-in under Pathweft settings, with visible compatibility/readiness state. Unknown updates, missing owners and failed attachment leave normal StartAllBack search usable and report the integration unavailable; no guessed offsets or automatic patch retries. Preserve original installed binaries, licensing state, settings and unrelated hooks. Supply verified disable/uninstall/recovery and actual attach/detach/Explorer-restart behavior. |
| SAB-05 | Validate the complete integration in an isolated matching Windows/StartAllBack environment before a coordinated live-shell trial. Exercise enable/disable, actual native input, Explorer restart, relevant DPI/monitor/focus behavior, unavailable backend, host/app exit and an unsupported-version case. Verify restoration of normal StartAllBack search and absence of persistent orphan hooks. Use hidden diagnostics where possible; physical Start-menu acceptance requires actual visible input. S6 owns its final release: package only Pathweft-owned adapter/source with exact supported-version notes and verify the delivered integration. The completed S8 core release does not satisfy this future gate. |
| SAB-06 | Route ordinary filesystem-folder links and folder-result open/reveal actions originating in StartAllBack directly to Pathweft, preserving the requested folder and selected file where applicable. Include built-in Documents and other known-folder shortcuts that bypass ordinary folder verbs: establish their actual Shell/known-folder/library call path under the Everything wiring procedure before selecting an adapter. The user's2026-11-05 clarification keeps this investigation and correction here, outside current S11/S14/S15. Preserve app/Settings/command launches and explicitly inventory virtual Shell locations that Pathweft cannot render. Test cold/warm Pathweft, Unicode/quoted paths, native selection, no duplicate or transient Explorer window for covered routes, and disable/recovery. Reuse the application launch/selection boundary; do not activate the inherited timed Explorer-window hiding/polling/closing watcher. Broader system-wide default-folder registration belongs to SHELL-01 in S8. |

The user calls this "inject Everything"; the required outcome is native in-menu
grouped file/folder subsets plus a full-results view, not preserving Windows
Search filesystem results and redirecting only See more results. A version-bound runtime interception is an allowed design
candidate for S6, not an established hook or a promise of universal compatibility.
No documented replacement-provider API has been established during planning.
If evidence shows that in-menu integration cannot be implemented safely in the
available supported environment, retain the unmet criteria and record the exact
blocker. Do not silently substitute another UI or redefine the task as a hotkey.
S6 leaves Windows Search services and other consumers unchanged while proving the
in-menu adapter. S7 owns the broader filesystem replacement and the user's revised
Outlook-preserving indexing-scope reduction. Do not modify licensing/activation code.

### Everything filesystem search and Outlook-preserving Windows indexing

| ID | Requirement and acceptance |
| --- | --- |
| EVERYWHERE-01 | Establish a finite, evidence-backed inventory of search surfaces/consumers before changing indexing scope: Pathweft, StartAllBack, Explorer, search-capable common Open/Save dialogs, applicable native Start/taskbar surfaces, and classic Outlook/Office. Record actual host/version, entry point, backend, query/result semantics and acceptance workflow. Inventory current file/protocol crawl-scope rules, Classic/Enhanced mode, Outlook inclusion and relevant account/cache/store search behavior. Identify unavoidable Windows-managed indexing locations and their purpose; do not silently classify ordinary filesystem indexing as an Outlook exception. Unanswered applicability/semantics questions remain gaps. |
| EVERYWHERE-02 | Route inventoried filesystem-search surfaces to the selected Everything engine while retaining native entry points/results/actions wherever feasible. Use bounded source/native adapters and REA for unavailable contracts. Verify typed query -> actual Everything result -> correct selection/open/reveal with the selected filesystem locations excluded from Windows indexing. A separate popup does not replace an existing search location. Record exact supported versions and capability limits; prove the file result backend rather than inferring it from WSearch still running. |
| EVERYWHERE-03 | Reconcile filename/path search separately from file contents/properties, app/settings/commands and mail-store search. Everything owns filename/path search; the user-authorized opt-in content index belongs to S19/CONTENT-01..06 and does not replace that filename engine. Outlook retains its native local Windows Search and applicable server-assisted behavior. Do not promise Everything mailbox search or change Outlook account/search providers. Qualify actual Everything APIs and the distinct S19 content coverage/query semantics at each inventoried surface. Missing required behavior remains a feasibility blocker; no silent substitute engine or new non-mail WSearch dependency. Report Windows-managed exceptions explicitly. |
| EVERYWHERE-04 | After replacement workflows pass, apply a reversible, recorded reduction of WSearch filesystem indexing scope while keeping WSearch available and Microsoft Outlook included. Exclude development/document and other selected filesystem roots, minimize duplicated file indexing and preserve required Outlook protocol/store indexing plus identified Windows-managed exceptions. Preserve actual prior crawl-scope/mode/service configuration and a verified recovery procedure. Use supported configuration APIs/UI and the administrator broker where required. Do not disable the service, delete its catalog, rebuild it by default, change unrelated policies or alter mailbox/user data. This revised plan does not itself authorize an immediate live change during S6. |
| EVERYWHERE-05 | Verify native Everything filesystem workflows and Outlook mail search under the reduced indexing scope: scope readback, actual backend ownership, file index freshness, Unicode, unavailable Everything, host/app restart, reboot/sign-in, updates, rollback and uninstall. Qualify local/offline Outlook search for applicable accounts rather than relying only on server results. Report actual residual filesystem indexing and resource effects without promising zero background work. Restore and verify the prior scope/configuration through the recovery path. Close every inventoried consumer gap; S7 owns final delivered-package and migration/recovery verification after S6, rather than reopening completed S8. |

The user's revised direction supersedes the former S7 requirement to stop/disable
WSearch. Microsoft documents both configurable indexed locations and Outlook's
included-location/service requirements. Keep the distinction between local and
server-assisted mail search; do not attribute every Outlook failure to indexing.
This is a specification change, not live configuration authorization during S6.
Native file-surface feasibility and account-specific Outlook compatibility remain
separate questions. The plan cannot turn either into completed integration.

Primary sources: [Windows indexing scope](https://support.microsoft.com/en-us/windows/experience/performance-optimization/search-indexing-in-windows),
[Outlook indexing/service checks](https://support.microsoft.com/en-gb/outlook/troubleshooting-outlook-search-issues?nochrome=true),
[local versus service search](https://support.microsoft.com/en-us/support/known-issues/outlook-search-might-not-display-recent-emails-on-windows-11).

### Window consistency and update feedback (S14, planned)

| ID | Requirement and acceptance |
| --- | --- |
| WINDOW-01 | Inventory Pathweft-owned auxiliary windows, including Settings, Help, What's new, updater/check/deployment, transfers, search/preview and action/error dialogs. Establish one concrete design contract using Native/Original palettes: consistent typography/hierarchy, margins, content/title alignment, navigation/selected states, cards, buttons/icons, scrollbars and resize/minimum-size behavior. Preserve each window's purpose and existing functionality; do not redesign native OS dialogs or third-party interfaces. Reuse existing resources where appropriate without speculative framework work. |
| WINDOW-02 | Apply that contract to all inventoried windows and verify actual screenshots/behavior in Native light/dark and Original at narrow/normal sizes and100%/150% text/DPI scales. Preserve Help's reading-section highlight, settings tabs, keyboard/focus/accessibility, window ownership/reuse/close and preview state. Resolve every in-scope inconsistency or record its exact blocker; palette changes alone are insufficient. |
| UPDATE-02 | Clicking manual Check for updates in the sidebar or Settings immediately shows nearby visible/accessibility feedback, followed by a clear result: Checking, up to date with installed version, update available with version/action, or check failed with explanation/explicit retry. Use a discoverable themed update surface/in-control status; the main bottom-right status line alone is insufficient. Observe the real task, prevent duplicate concurrent checks and avoid artificial completion delays. Automatic checks stay quiet when unchanged; preserve download/hash/cancel/deployment progress, transfer/editor protection and user-controlled restart. Verify no-update/available/network-failure and progress/result behavior through actual application workflows. |
| TRANSFER-14 | On actual successful transfer completion, automatically dismiss that job through the existing Transfers UI owner and hide the window when no cards remain. Retain active/queued/paused/conflicting and failed/partial/error outcomes; explicit clean cancellation follows TRANSFER-15. Preserve completion notifications, Undo, arrival selection, source/destination refresh and reusable close-to-hide behavior. Use completion events, with no simulated click, delay or polling deadline. Verify real copy/move, multiple jobs and unsuccessful outcomes; successful completion must not hide outstanding work or erase its errors. |
| TRANSFER-15 | The S15 transfer popup amendment in current scope governs compact content-ready sizing, a real virtualized conflict table, clean explicit-cancel dismissal after worker completion, unchanged native clipboard data and repeat Paste, nonduplicated speed, and visible file-specific errors with secondary informational details. Qualify real job actions, isolated native clipboard, multiple jobs, resizing, keyboard, dark/light and100/150% rendering. |
| TRANSFER-16 | S17: differences-first metadata review, newer destination files first in orange with noncolor cue, only differing size/date values rendered at distinguishing precision, explicit matching-file reveal/counts/action scope and unchanged full-set transfer decisions. No extra content reads or identity claim from equal metadata. Verify real conflicts/resolution, compact themes/scaling and final independent/installed release. |
| WINDOW-03 | Reconcile WINDOW-01/02 and UPDATE-02 against the complete inventory, revalidate affected updater/window lifecycle, then deliver one normal S14 release through the established channel. Independently retrieve the actual package, verify matching source/runtime/notices and packaged workflows, and verify the installed update. No per-window intermediate release or unsolicited system/theme/settings changes. |

### Current-pane disk analysis (S15)

The source assessment in [DISKWEFT-INTEGRATION.md](DISKWEFT-INTEGRATION.md) is now
authorized for implementation within the focused mode below. Import selected
scanner source, not Diskweft's application UI, installation, settings or MCP.

| ID | Required outcome and acceptance |
| --- | --- |
| SPACE-01 | A compact Analyze space toggle beside Terminal uses Pathweft's existing proportions, theme, tooltip, accessible name and keyboard/focus conventions; use a disk/blocks motif reflecting the supplied defrag reference. Analyze only the active pane's current filesystem path. Replace the old external Scan with SpaceScan menu/Help routes with this same action. State unsupported virtual/search/This PC eligibility honestly; do not silently extract archives or scan all drives. |
| SPACE-02 | Morph that pane into a virtualized expandable directory/file tree with Space used totals and root-relative usage bars/percentages, descending size ordering among siblings and stable selection/viewport while totals grow. Publish useful progressive recursive totals before completion. Distinguish pending, measured empty, incomplete/denied, cancelled and failed results. The current donor measures logical file lengths: do not present them as allocated disk clusters or cloud account quota. |
| SPACE-03 | The same toggle restores the prior standard view mode, exact columns/order/widths, sort/direction, path, selection and scroll. Own each pane's root/session, reject old generations, and await actual cancellation/worker completion on exit/navigation/close. Preserve ordinary navigation, selection, preview and real file actions; do not create a second mutation owner or let updates from an old scan overwrite the restored view. |
| SPACE-04 | Borrow and pin selected Diskweft traversal/accounting/progressive source with MIT provenance/notices and upstream-review inventory. Scope mutable state/cancellation/statistics to the scan; do not import shared static donor application state, a new endpoint app, background service or Coordinator/MCP requirement. Do not follow reparse points or read cloud file contents merely for sizing. Avoid duplicate measurement of the same scope through existing SizeCache; promote only complete valid totals under its invalidation contract. Ordinary subtree traversal is required; no automatic raw-volume elevation or unrelated donor search/duplicate/history/reporting features. |
| SPACE-05 | Verify the real nested-folder toggle workflow, exact terminal totals, progressive/largest-first/selection/expansion behavior, cancelled/denied/reparse/Unicode/long paths, actual nested mutation invalidation, two-pane ownership and shutdown; compare actual scan/display overhead and retained memory against the selected donor on the same fixture without promising unmeasured performance. Verify Native light/dark/Original, narrow/normal,100/150% and keyboard/accessibility. Reconcile S11/S14/S15 and deliver one normal release with corresponding sources/notices, independently retrieved actual packaged workflows and installed verification. Preserve the running app and user data until its normal updater/restart. |
| CORE-19 | For ordinary folder-size refreshes, preserve the last measured displayed value while invalidation/recalculation runs. Atomically replace it when a new valid measurement arrives; use the unknown placeholder only before any measurement exists. Cover repeated background writes, row refresh/reconciliation and summary/selection totals, without accepting invalid/incomplete measurements as completed cache values. The user's continuously changing folder reproduction belongs to S15's sizing integration and final release, not a separate intermediate release. |
| CORE-20 | Prefer Everything's available indexed folder sizes for visible ordinary rows and expanded analysis branches through the existing serialized SDK owner. Detect disabled folder-size indexing and unknown/uncovered paths; use metadata traversal when unavailable without altering Everything settings, requiring an engine upgrade or loading its whole index into Pathweft. Identify indexed coverage in the UI; an indexed total may omit excluded files and is not a verified complete physical-disk scan. Sizes remain logical lengths, not allocated bytes or cloud account quota. Archive sizing remains with its archive owner. |
| CORE-21 | Replace eager in-memory folder-size persistence with a bounded hot cache and indexed on-disk fallback measurements under the existing bulk cache root. Migrate existing text records transactionally without loading them all into RAM, preserve correctness across interrupted migration, and use targeted ancestor/subtree invalidation under the existing measurement publication contract. Avoid full-map snapshots/rewrites and per-record invalidation allocations. Verify actual legacy migration, persistence, concurrency, invalidation, bounded retained memory and unchanged storage relocation; compare the installed-code baseline and corrected data path on the same fixture. |

### Context menu reorganization (S9)

The user's 2026-10-31 request adds S9 to the plan, without starting it during the
S8 appearance correction. It covers the Pathweft context menu; it does not
authorize redesigning Windows' native third-party menu or starting S6/S7.

| ID | Requirement and acceptance |
| --- | --- |
| MENU-01 | Inventory current commands and their eligibility for files, folders, mixed/multiple selections, empty space and archive entries. Reorganize related commands into clear groups/submenus, prioritize common file operations and remove duplicate presentation while preserving supported actions, keyboard access and existing shortcuts. Define the final grouping when S9 becomes ACTIVE using the existing Native/Original design contract. |
| MENU-02 | Preserve the selectable native Windows context-menu path, Shell extension behavior and capability-aware unsupported/read-only states. Verify real open/copy/cut/paste/rename/delete and archive operations through the reorganized menu, keyboard navigation, narrow/small-display placement and both themes at 100%/150%. Publish and verify the completed S9 change through the existing release channel. |
| MENU-03 | Replace bottom command text with familiar Explorer-style icons, keeping names and shortcuts in hover tooltips and accessible names. Recover exact toolbar and View-menu SVG artwork where available; use verified Fluent glyphs for structural controls. Preserve supported actions, state/eligibility, shortcuts and Native/Original styling. Verify recognizable active/disabled states, keyboard/focus access, narrow layouts and 100%/150% rendering in both themes. Native icon recovery is research until S9 becomes ACTIVE; source provenance and distribution/runtime-use decisions must be explicit. Shared final delivery belongs to S11. |

### Native Jump Lists (S11)

The user's 2026-10-31 screenshots add S11 as planned work: Pathweft's expandable
application entry should expose native pinned and recent/frequent folder
destinations, comparable to Explorer, plus useful tasks as demonstrated by
Directory Opus. This is native Shell Jump List integration, separate from S6's
StartAllBack search-provider work; no host Start menu interaction is authorized
as part of recording this plan.

| ID | Requirement and acceptance |
| --- | --- |
| JUMP-01 | Qualify supported native Shell/WPF Jump List APIs and stable Pathweft application identity. Match Explorer's native Pinned and Frequent category structure in the expandable app entry and taskbar Jump List. Windows owns pinning and frequent ordering; do not imitate these with custom categories or substitute Quick access/Recent folders or Home/Downloads tasks. Preserve Windows-controlled pin/unpin/removal and recent-items privacy preferences; do not overwrite or copy Explorer/Opus lists. Respect removed/rejected items and retain pinned state across updates. Qualify actual native Frequent readback for Pathweft's argument-bearing folder links; a property assignment or renamed custom category alone does not pass. A Tasks-only menu does not satisfy folder-destination acceptance. |
| JUMP-02 | Folder destinations launch Pathweft directly with the correct Unicode/quoted path in cold and running-instance states, independently of the optional default-folder setting. Qualify virtual/offline destinations explicitly and preserve existing open/reveal semantics. Add applicable tasks through existing application actions, not a second manager. Verify actual native Jump List contents and user pin/unpin/open/remove behavior, restart/update/uninstall ownership and final release delivery. StartAllBack is a caller-compatibility check for this native feature, not permission to resume S6 or restart Sandbox. |

Native reference: [Microsoft WPF JumpList](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.jumplist).

### Contextual performance monitoring (S18)

Planned after S16 and before S12; NOT STARTED. Reuse the bottom summary area and
Pathweft's current design. Resource Monitor is a behavioral reference for
process/file attribution, not a requirement to reproduce its entire interface.

| ID | Requirement and acceptance |
| --- | --- |
| MONITOR-01 | Provide a directly accessible Statistics/Performance toggle for the current pane's bottom cards, preserving the ordinary file view and existing statistics. Performance presents four compact cards in order: CPU utilization/history; GPU utilization/name/memory with adapter selection where multiple GPUs exist; current-drive read/write throughput/activity; current-folder read/write throughput including descendants. Retain readable Pathweft proportions, theme, scaling and responsive layout. |
| MONITOR-02 | Clicking a card directly reveals its relevant details in Pathweft-styled tables without an intermediary single-action overflow menu. Drive/folder details attribute activity to Process, PID, File, Read/s, Write/s and Total/s; folder scope follows the current path and descendants. CPU/GPU details explain the displayed utilization through the qualified data source. Preserve current browsing and support returning to the compact cards. |
| MONITOR-03 | Establish actual Windows counter/event sources and required privileges before selecting implementation. Distinguish file-level logical I/O, including cached requests, from physical device activity; do not infer throughput from folder-size changes. Define current volume/device mapping and qualify mapped, virtual/cloud and unavailable sources explicitly. Missing access, unsupported measurements or incomplete/lost observations must be visibly distinguished from zero activity. Verify path attribution, process identity/reuse and navigation scope; assess overhead with actual workloads. |
| MONITOR-04 | Collect detailed monitoring only while its Performance view is visible. Use bounded history/buffers and event/state-based ownership; release collectors and any owned helpers when hidden, navigation replaces scope or the app exits. Reuse existing pane ownership and preserve transfers, preview sessions, settings and other consumers. Document any necessary privilege boundary; this planning request does not authorize starting a trace, changing system configuration or deploying a helper. |
| MONITOR-05 | Qualify the complete toggle -> live cards -> attributed details -> scope change -> return/cleanup workflow against real known CPU/GPU and file read/write activity. Cover cached I/O semantics, other-process activity, multiple GPU/device selection where available, unsupported/access-denied states, independent panes, cancellation/exit and dark/light/scaled layouts. Measure CPU/memory/collection overhead and establish concrete budgets before implementation acceptance; do not claim resource neutrality from appearance. S18 owns normal release, independent retrieval and applicable installed verification after implementation is separately authorized. |

Before implementation activation, resolve collector feasibility, privilege handling,
measurement definitions and numerical resource budgets within this existing scope.
No extra monitoring categories or system-management actions are implied.

### Tresorit capability investigation (S12)

The user's 2026-11-02 request adds S12 as planned work, NOT STARTED.
Recording this stage does not activate a client integration, change Tresorit
configuration or authorize deleting cloud content. The reported main-folder
deletion restriction is a provider capability boundary, not an NTFS ACL fix.

| ID | Requirement and acceptance |
| --- | --- |
| TRES-01 | Inventory concrete gaps in the mounted-drive integration: main-folder versus child operations, deletion/recovery semantics, ownership/roles, actionable errors, trustworthy metadata/quota and cloud state. Distinguish verified client behavior, provider restrictions and possible improvements; do not promise unsupported capabilities. |
| TRES-02 | Assess supported public APIs/SDKs and documented client commands first. Verify actual content-operation coverage versus administration/audit-only surfaces, account/tier availability, authentication/scopes, limits and terms. Produce a capability matrix with evidence; availability of a suitable API is an open question. |
| TRES-03 | Where documented surfaces are insufficient, use the installed REA workflow and its storage/Coordinator guidance to inspect the exact signed installed client/version. Examine command/URI/Shell/IPC contracts before internal hooks. Qualify any candidate with bounded runtime evidence; record version coupling, maintenance and unsupported operations. Static analysis alone does not establish feasibility. No automatic Sandbox setup or credential extraction. |
| TRES-04 | Deliver an evidence-backed integration decision and a narrow end-to-end implementation proposal with acceptance, lifecycle, recovery and release criteria. Preserve provider ownership, consent and permanent-delete semantics; never bypass permissions or silently substitute permanent deletion. Use disposable fixtures for later mutation tests and preserve existing cloud data. Any implementation remains separate from this plan-only request. |

### Selected-folder content indexing (S19)

Planned immediately after S12 and before S20; NOT STARTED. Everything remains the
filename/path engine. Content indexing is opt-in for selected folders and is a
separate capability. This planning request does not authorize extracting user
documents, hydrating cloud files, starting indexers or installing components.

| ID | Requirement and acceptance |
| --- | --- |
| CONTENT-01 | Add a directly accessible toolbar control to enable/disable content indexing for the current folder and descendants, with discoverable scope/progress and explicit supported-format coverage. A small magnifier badge at the file icon corner means extraction/index publication succeeded and the file is current under the qualified change-detection contract. Pending, changed, unsupported, failed and unavailable observations must not falsely show successful current indexing; explain state through accessible text/tooltips. Disabling removes that scope's indexed data without modifying source files; preserve files still included by another selected scope. |
| CONTENT-02 | Use existing bundled source-built Craft cores through owned extraction boundaries where actual text extraction is qualified, and direct streaming readers for plain text, Markdown, JSON/JSONL and other individually supported text types. Derive advertised support from verified extractors, not file extensions or successful preview rendering alone. Preserve native page/sheet/slide/paragraph locations where physical line numbers do not apply; retain line numbers and suitable offsets for text. Distinguish searchable strings/tokenization from any proposed Strings executable; no external Strings dependency or particular extractor is assumed qualified. Preserve originals and existing macro/script/network isolation. |
| CONTENT-03 | Retain a compact searchable strings/terms index with file identity/path, source metadata and arrays of matching source locations; do not retain full extracted document text or require offline snippets. Offline searches may return indexed file/location matches; obtain surrounding text from the original only when requested/available. Qualify SQLite FTS5 contentless storage as the leading candidate, including actual runtime support, terms/literal/phrase/Unicode behavior and location mapping; use measured corpus evidence before selecting another engine. Keep the rebuildable index in Pathweft's configured data/cache location under the existing storage policy, with bounded memory and no corpus-wide in-memory text copy. |
| CONTENT-04 | Once implementation is authorized, initial indexing reads the selected supported files and may fetch their content through the existing cloud provider. Indexed originals need not remain locally available: do not pin them offline, delete cloud/source files or force cache eviction. Periodically reconcile selected folders and incrementally re-extract only new/changed files; combine timestamp and size with stable provider revision/identity where available, qualifying providers with unreliable metadata. Handle renames/deletions, disconnected roots, in-flight writes, missed notifications and restart catch-up without classifying unavailable files as deleted. Metadata equality is a change-detection heuristic, not proof of byte identity. Keep prior failed-refresh results explicitly stale and publish only complete valid replacements. |
| CONTENT-05 | Expose content search through Pathweft's existing browsing/search workflow with clear selected-root coverage, freshness and filename-versus-content semantics. Return original file identity and matching line/page/sheet/cell/slide/paragraph locations; navigate via the appropriate original-file viewer/open boundary where supported. A cached index can answer while its source is offline, but never claim the original is accessible or the result is freshly verified. Do not silently hydrate files merely to draw result rows. |
| CONTENT-06 | Qualify a real folder-toggle -> extraction -> persisted strings/location index -> query -> original-location workflow, including current badges, changed/deleted/renamed files, mixed supported/unsupported formats, large/actively appended JSONL, extraction failure, offline/cloud-cache independence and restart recovery. Measure index size, query/update latency and peak/idle CPU/memory on representative corpora; establish concrete resource budgets and bounded background ownership before acceptance. Verify settings scope, overlapping roots, disable/purge, cancellation/app-exit cleanup, themes/scaling and source preservation. S19 owns normal release, independent retrieval and applicable installed verification after implementation is separately authorized. |

No implementation criterion is satisfied yet. Extractor APIs, search granularity,
provider metadata reliability and resource budgets remain to qualify in this scope.
There are no tracked deferrals. No system-wide index or search-service change is
authorized by registration of this stage.

### Integrated PowerToys file tools (S20)

Planned after S19 and before S21; NOT STARTED. Scope is the seven capabilities
accepted in the brainstorming discussion, with New+ last in implementation
priority. This registration does not activate implementation or any live action.
Use the [PowerToys source](https://github.com/microsoft/PowerToys/tree/main/src/modules)
as a donor, not a separately installed product or a promise of drop-in libraries.

| ID | Requirement and acceptance |
| --- | --- |
| TOOLS-01 | Upgrade the existing batch-rename workflow using selected PowerRename source for regex/literal replacement, case conversion, configurable numbering and date substitutions. Keep current pattern workflows compatible, preview old/new names and invalid/colliding results before applying, and integrate actual rename execution, failure handling and Undo with Pathweft's existing owner. Qualify cancellation, name cycles/case-only changes and representative Unicode/path cases without silently substituting the donor's independent undo/settings ownership. |
| TOOLS-02 | Integrate File Locksmith's process/file-use discovery into selected-file/folder context actions and relevant failed transfer/delete details. Show accessible process identity and affected paths, explicit access/privilege limitations, refresh/cancellation and actionable diagnostics. Qualification must identify a real controlled file holder and its release. Process termination, forced handle closure and automatic elevation are not included in the accepted discovery scope. |
| TOOLS-03 | Integrate Image Resizer as a selected-image batch action with size presets/custom dimensions, proportional fit/fill/stretch choices, quality and explicit output destination/naming. Preserve originals by default; replacement must be an explicit choice using existing conflict/operation handling. Qualify actual dimensions/output decoding, orientation, metadata options, errors/cancellation and source preservation on advertised formats. |
| TOOLS-04 | Integrate Text Extractor/OCR into image previews, including selection of a preview region and explicit text-copy action. Qualify supported input conversion, region coordinates/scaling and actual language availability; clearly report missing Windows OCR capabilities/language packs instead of installing them silently or claiming bundled language coverage. OCR output is recognized text, not guaranteed exact transcription. Keep broader automatic OCR indexing outside S19's already defined extractor coverage unless separately requested. |
| TOOLS-05 | Integrate Registry Preview parsing/presentation for selected .reg files as a structured key/value tree in the existing preview hosts, with source-text access and useful invalid-content errors. Preserve file contents and preview isolation; registry import/write and changing the default .reg association are outside this preview scope. Qualify actual supported syntax, large/malformed input, selection replacement and PiP/Windowed lifecycle. |
| TOOLS-06 | Integrate the deterministic Advanced Paste subset as explicit Paste as file actions for clipboard text, HTML and images, producing .txt/.html/.png files in the current writable folder. Qualify actual native clipboard formats, destination naming/conflicts, repeat invocation, clipboard preservation and Undo; preserve existing file Copy/Cut/Paste behavior. AI providers, model installation, media transcoding, global clipboard hooks/history and automatic clipboard transformation are outside this scope. |
| TOOLS-07 | Add lower-priority New+ file/folder templates to existing New File/New Folder workflows, including a configurable template directory, folder structures and supported filename variables. Qualify template discovery, actual creation/rename selection, conflicts, failure/cancellation and Undo without launching template content or changing Explorer's New menu. Reuse Pathweft storage policy and do not require PowerToys settings or template locations. Lower priority is execution order, not a deferral or omission from S20 acceptance. |
| TOOLS-08 | Pin exact donor source/dependency revisions and retain required notices/distribution materials. Inspect each module's actual build/runtime coupling before extraction: existing PowerRename static-library separation does not establish a standalone supported ABI. Use focused bundled native DLLs or managed assemblies as appropriate and existing owned workers where needed; keep one Pathweft settings, selection, command and operation owner. No standalone PowerToys installation, runner, shell-extension registration, telemetry infrastructure, global hooks or endpoint/MCP dependency. Do not introduce a generalized plugin framework. Preserve current theme/proportions, contextual eligibility, accessible names and source/clipboard data. |
| TOOLS-09 | Verify all seven complete integrated workflows against actual borrowed implementations, including applicable file/clipboard/Windows boundaries, errors, cancellation, Undo, preview replacement/host transitions and app-exit cleanup. Record exact supported scope and measure added package size, invocation latency and idle/peak resources before making footprint/performance claims. No capability is accepted solely from an upstream feature list or isolated library test. S20 owns normal final release, corresponding sources/notices, independent retrieval and applicable installed verification once implementation is separately authorized. No per-tool intermediate release. |

Initial source review: [PowerRename](https://github.com/microsoft/PowerToys/tree/main/src/modules/powerrename),
[File Locksmith](https://github.com/microsoft/PowerToys/tree/main/src/modules/FileLocksmith),
[Image Resizer](https://github.com/microsoft/PowerToys/tree/main/src/modules/imageresizer),
[OCR](https://github.com/microsoft/PowerToys/tree/main/src/modules/PowerOCR),
[Registry Preview](https://github.com/microsoft/PowerToys/tree/main/src/modules/registrypreview),
[Advanced Paste](https://github.com/microsoft/PowerToys/tree/main/src/modules/AdvancedPaste),
[New+](https://github.com/microsoft/PowerToys/tree/main/src/modules/NewPlus),
and [license](https://github.com/microsoft/PowerToys/blob/main/LICENSE).
These main-branch links are research pointers, not adopted pins. No implementation
criterion has passed; module extraction, runtime compatibility and resource costs
remain unqualified. No tracked deferral or ACTIVE stage is introduced.

### Browsing productivity and smart destinations (S21)

Planned after S20 and before S22; NOT STARTED. The user registered the Linux
comparison's suggested features and the agreed browser-style address dropdown
as one future stage. This extends existing browsing rather than replacing its
working commands or importing a whole donor application. Scope registration
does not activate implementation or authorize changes to source/user/remote data.

| ID | Requirement and acceptance |
| --- | --- |
| BROWSE-01 | Extend the existing address field and suggestion popup with recent folders, pinned folders, already-open tabs, saved searches and filesystem path completions. Match folder names and full paths without requiring an absolute prefix, highlight matches and identify each result type with its icon/action and location. Rank strong matches first and then recency, deduplicate equivalent destinations without losing a distinct switch-to-tab action, and show recent destinations when the focused field is empty. Reuse existing history/pins/tab state; cached suggestions must not trigger recursive disk scans or cloud hydration on every keystroke. Preserve explicit filesystem completion as a supplement when a path is being entered; qualify matching/ranking against representative Unicode/UNC/archive inputs. |
| BROWSE-02 | Preserve keyboard/mouse behavior for the unified dropdown: Up/Down selects, Tab completes an applicable path, Enter activates the selected destination/tab/query and Escape dismisses suggestions without discarding the draft. A history-row remove control removes only that history entry, never source files, pins or open tabs; integrate clear-history/privacy behavior consistently. Background listing/sizing/search completions and popup interaction must not overwrite typed text, caret, selection or focus; obsolete work cannot replace current suggestions. Qualify actual editing, selection, navigation/tab activation, history removal, restart persistence, keyboard accessibility and light/dark/scaled layouts. |
| BROWSE-03 | Add named saved searches/smart destinations with persisted query, scope and relevant match options, reachable from the existing sidebar/address suggestions. Opening reruns the query against its qualified engine instead of storing a frozen result list. Support editing/renaming/removing the saved query without deleting files. Preserve Everything filename/path semantics and separately identify S19 content searches, selected-root coverage and unavailable/offline sources; content-query support depends on S19's verified capability. Qualify actual save -> restart -> reopen -> changed results and option/scope round trips. |
| BROWSE-04 | Add a user-configurable context-action editor backed by a documented configuration file, reusing Pathweft's configuration approach where appropriate. Actions specify name, icon, optional submenu, executable/arguments and applicability by file type and selection count, with explicit selected-path/current-folder placeholders. Qualify correct argument boundaries for spaces/Unicode/metacharacters, multiple selections, working directory, command errors and owned process lifecycle. Actions run only on explicit invocation; configuration is not automatic execution or a second folder-route registry owner. Integrate actions into the existing command/menu eligibility and presentation without a general-purpose plugin framework. |
| BROWSE-05 | Extend current folder-specific sorting/date grouping into complete per-folder view profiles: view mode, displayed columns/order/widths, grouping, folder-first/hidden visibility and preview preferences. Provide explicit current-folder, descendant and default application semantics, preserving unconfigured folders and user-selected global defaults. Resolve inheritance/reset behavior before implementation; do not let a saved profile silently change unrelated folders or unexpectedly persist temporary disk-analysis mode. Qualify navigation/restart, two-pane/tab independence, defaults/descendants, dynamic empty columns and exact restoration of existing modes. |
| BROWSE-06 | Extend the immediate-entry metadata comparison into recursive folder comparison with optional content comparison and a reviewed synchronization plan. Show proposed copy/replacement/deletion directions and conflicts before mutation, let users exclude/change operations, and distinguish metadata matches from verified content equality. Reuse existing transfer/conflict/Undo ownership and preserve newer destinations and source data under explicit choices; synchronization is manual and reviewed, not an unrequested background mirroring service. Qualify nested differences, equal-size changed content, conflicts, cancellation/partial failure, existing link traversal policy and actual applied outcomes on controlled fixtures. |
| BROWSE-07 | Add file/folder tags, ratings and comments with readable editing/display and filtering/search integration where qualified. Select and document metadata storage/identity behavior before implementation, including moves/renames, disconnected/cloud roots, unsupported filesystems and cross-machine portability. Do not claim universal provider synchronization or silently write alternate streams/sidecars into user content. Qualify actual persistence, mutation/restart, search and unavailable/stale cases under the selected explicit storage contract. |
| BROWSE-08 | Add an optional embedded terminal panel synchronized with the active local folder, including terminal directory changes navigating the browser when supported. Preserve the existing external-terminal shortcut and keep terminal sessions/processes owned across pane/tab switches, hide/close and application exit. Qualify actual terminal hosting, shell directory-change signals, quoting and feedback-loop prevention; display unsupported remote/archive contexts honestly. Do not substitute command injection or inferred prompt text for a qualified directory synchronization contract. |
| BROWSE-09 | Add direct saved SSH/SFTP and WebDAV connections within normal browsing, complementing existing UNC/mapped drives. Qualify protocol/library coverage, authentication/credential storage, SSH host identity/TLS validation, remote path/metadata semantics, listing/preview/transfers and cancellation/disconnection/recovery before advertising support. Preserve existing transfer/source-conflict handling and provider permission boundaries; do not pretend remote items have local paths or local Undo guarantees. Use controlled endpoints for mutation acceptance, never user servers as disposable fixtures. No FTP/NFS expansion is implied by the comparison examples. |
| BROWSE-10 | Add an explicitly invoked temporary live filter of entries in the current listing, distinct from recursive Everything search. Keep the unified address/search design; no permanently added second search bar. Make scope/state clear, retain hidden-file policy, and restore the unfiltered listing/selection appropriately when dismissed. Qualify typing/keyboard/cancellation, navigation, active background refresh and interaction with search/archive/analysis modes without silently changing recursive search semantics. |
| BROWSE-11 | Add contextual symbolic-link, hard-link and directory-junction creation with target inspection and honest type/eligibility, separate from existing .lnk shortcuts. Qualify actual Windows/filesystem/privilege/volume support, relative versus absolute targets, conflicts and broken links. Preserve existing non-following traversal/deletion policies; removing a link must not remove its target. Reuse current operation/Undo ownership where applicable and clearly report unsupported combinations instead of substituting copied contents or .lnk files. |
| BROWSE-12 | Verify every complete registered workflow through its real UI/state/filesystem/terminal/protocol boundaries, including coexistence with S19/S20, errors/cancellation, restart/privacy, source preservation and app-exit cleanup. Inspect any donor's actual code, dependency/license/runtime fit and retain exact adopted pins/notices; Linux feature parity does not imply importing KDE/GNOME runtime infrastructure. Establish measured resource budgets for suggestions, metadata, remote and terminal work; no unbounded corpus/connection polling or performance claims from feature lists. S21 owns one normal final release, corresponding source/notices, independent retrieval and applicable installed verification after implementation is separately authorized. No per-feature intermediate release. |

Reference behaviors: [Dolphin filtering/saved searches](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/quick-tips.html),
[Dolphin metadata/terminal panels](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/panels.html),
[Dolphin folder profiles](https://docs.kde.org/stable_kf6/en/dolphin/dolphin/view-properties.html),
[Thunar custom actions/link examples](https://docs.xfce.org/xfce/thunar/custom-actions),
[GNOME remote browsing](https://help.gnome.org/gnome-help/nautilus-connect.html),
and [Krusader recursive synchronization](https://docs.kde.org/trunk_kf6/en/krusader/krusader/synchronizer.html).
The user's browser screenshot supplies the address-suggestion behavior reference,
not permission to import its browser profile/history or recreate its full UI.

No acceptance criterion is implemented or verified for S21. Metadata storage,
terminal hosting/synchronization, protocol libraries and donor suitability remain
ordinary design/qualification work, not asserted external blockers. No tracked
deferral or ACTIVE stage is introduced; S19 and S20 retain their own scope.

### Focused file workflow enhancements (S22)

Planned immediately after Linux-inspired S21 and before S6; NOT STARTED. The
user registered the Directory Opus assessment's useful additions, including its
lower-priority candidates. Favor direct implementation using existing tabs,
panes, operations, preview and settings owners. This is not whole-product parity,
a new scripting/plugin framework or a dependency on an installed Opus client.
This plan-only request does not activate implementation or uninstall Opus.

| ID | Requirement and acceptance |
| --- | --- |
| OPUS-01 | Add named project workspaces containing left/right tab sets and active folders/pane. Provide explicit add-to-current versus replace-current restoration, preserving current session/pinned-tab behavior and relevant S21 view-profile rules. Reuse existing settings/session owners rather than creating a separate window-management framework. Verify save, rename, remove, restart and restore with unavailable destinations and unsaved editor state handled through existing preservation contracts; removing a workspace never deletes its files. |
| OPUS-02 | Add explicit paired-folder roots and opt-in linked relative navigation between two panes or a designated tab pair. Navigating below one root resolves the counterpart below the other; missing counterparts are visibly explained without automatic folder creation, copy or synchronization. Define rebase/unlink and tab-close/navigation behavior, preserving independent use when unlinked. Qualify actual source/deployment-style navigation and missing/renamed destinations; S21 continues to own recursive comparison and reviewed file synchronization. |
| OPUS-03 | Extend ordinary browsing with inline expandable folders and reversible flat descendant views, with a Location column where needed. Reuse qualified disk-analysis tree/browsing components while keeping analysis mode distinct. Preserve actual source paths, existing hidden/link traversal policy, selection/focus, view restoration and bounded/cancellable enumeration. Nested selections must not duplicate transfer work; flat/tree copy choices must clearly distinguish preserving relative hierarchy from flattening and use existing conflict/operation ownership. Qualify navigation, refresh, nested actions and actual resulting files, not tree rendering alone. |
| OPUS-04 | Add named persistent file collections referencing items across directories without moving originals. Extend existing Drop bin concepts while preserving its temporary transfer workflow; collections are distinct from S21's live saved searches. Support adding/removing references, rename/remove of a collection, containing-folder navigation and existing explicit file actions. Clearly distinguish Remove from collection from Delete original. Define moved/missing/offline member behavior and verify restart persistence, multi-root actions and original-file preservation. |
| OPUS-05 | Add bounded persistent operation history showing actual result, time, source/destination and containing-folder actions, plus a usable list of currently eligible Undo actions. Reuse transfer/file-operation completion and Undo owners, retaining truthful partial/error/cancelled outcomes after cards close. A persisted log must not serialize executable delegates, replay operations automatically or imply safe undo after restart; preserve existing eligibility and qualify stale/conflicting destinations before any undo action. Verify completed/failed/cancelled jobs, restart history, retention/privacy and actual eligible Undo outcomes. |
| OPUS-06 | Add copying selected table rows/columns and exporting the current visible listing to CSV/TSV, with explicit column/header and current-view scope. Preserve displayed order/filtering and resolve actual nested/virtual paths consistently. Qualify escaping of delimiters, quotes, line breaks and Unicode and round-trip the exported values; preserve clipboard ownership and do not read file contents, hydrate sources or start recursive scans merely to export displayed metadata. A general printing subsystem is outside this requirement. |
| OPUS-07 | Add lower-priority user-defined path aliases as stable named destinations integrated into S21 address suggestions. Define unambiguous syntax, validation and precedence with real paths, history, pins and saved searches; support rename/remove and restart persistence. Missing/offline targets must not redirect to another destination silently. Qualify expansion, completion, Unicode/UNC targets and preservation of address drafts/focus. Aliases change navigation references only, not source paths. |
| OPUS-08 | Add lower-priority image picking/marking in the preview with a collection-backed set and navigation through marked images. Keep marks separate from current file selection, S21 ratings and the preview pin/follow state. Qualify marking/unmarking, persistence, missing members, next/previous navigation and sidebar/PiP/Windowed host transitions without unexpected file moves/deletions or retained renderer resources. |
| OPUS-09 | Add lower-priority batch embedded-metadata editing and timestamp adjustment for explicitly qualified writable formats/properties. Integrate mixed-value presentation and reviewed apply/cancel with existing Properties/operation ownership and S21 metadata contracts. Establish a concrete supported format/property matrix before implementation; preserve unedited metadata, file content and existing filesystem/provider capabilities. Do not assume NTFS alternate streams are portable or write unsupported cloud metadata silently. Qualify actual write/reopen, unrelated-field preservation, unsupported/read-only sources, partial failure and cancellation; document available Undo/recovery honestly. |
| OPUS-10 | Verify all nine complete integrated workflows with actual UI/state/filesystem/clipboard/preview boundaries, errors/cancellation, restart/privacy and source preservation. Measure added package/idle/peak costs and bound collection/history/enumeration work. Public documentation establishes behavior references, not compatibility or binary reuse. Any unresolved question requiring REA must have a bounded objective, identified qualified artifact, retained evidence and matching runtime verification before adopting behavior/hooks. No Opus DLL reuse or installed-app dependency is qualified by this assessment. S22 owns one normal final release, corresponding source/notices, independent retrieval and applicable installed verification when implementation is separately authorized; no per-feature releases. |

Priority follows the assessment: workspaces, paired navigation, expandable/flat
browsing, collections, operation history and listing export first; aliases,
image marking and batch metadata afterward. Lower priority does not remove any
registered requirement from S22 acceptance. Existing S20/S21 features remain
with their current owners and are not duplicate implementations in S22.

Behavior references: [tab groups](https://docs.dopus.com/doku.php?id=preferences:preferences_categories:folder_tabs:groups),
[navigation lock](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:dual_display:navigation_lock),
[expandable folders](https://docs.dopus.com/doku.php?id=basic_concepts:expandable_folders),
[flat view](https://docs.dopus.com/doku.php?id=basic_concepts:flat_view),
[collections](https://docs.dopus.com/doku.php?id=basic_concepts:virtual_file_system:file_collections),
[operation history](https://docs.dopus.com/doku.php?id=file_operations:tracking_and_undoing_file_operations),
[listing export](https://docs.dopus.com/doku.php?id=additional_functionality:print_folder),
[aliases](https://docs.dopus.com/doku.php?id=basic_concepts:the_lister:navigation:aliases),
[image marking](https://docs.dopus.com/doku.php?id=additional_functionality:viewing_images:image_marking)
and [metadata editing](https://docs.dopus.com/doku.php?id=file_operations:editing_metadata).
These are reference documentation, not adopted source pins or runtime acceptance.

No S22 criterion is implemented or verified. No stage is ACTIVE, no blocker is
asserted unresolvable and no tracked deferral is introduced. Artifact/API/format
qualification remains future stage work, not a reason to activate it now.

### Lifecycle, shell integration and final delivery

| ID | Requirement and acceptance |
| --- | --- |
| CORE-23 | Missing local folder tabs recover to the nearest confirmed existing ancestor on activation and restoration, retaining order/pins/selection identity and persisting the repaired path. Correct the tab/header/list state together; no old folder rows under a new tab identity. Preserve normal paths, existing archive tabs and access-denied/offline destinations without mistaking failed availability probes for deletion. Verify actual click, keyboard/external activation, multiple vanished levels, restored tabs and normal navigation, plus normal final release and independently retrieved package behavior. |
| LIFE-01 | Own initialization, readiness, cancellation and cleanup through actual state/events. Do not copy fixed sleeps/retries or arbitrary timeout-driven synchronization from donors. Isolate unavoidable external timeouts at their boundary. Verify owned helper exit before scratch cleanup. |
| SHELL-01 | Folder/default-handler and Win+E integration is opt-in, reversible and verified. Investigate Directory Opus's native Explorer replacement behavior and replace the inherited timed hide/poll/close Explorer-window takeover for covered entry points with launch-boundary routing. Qualify normal folder/default and explore verbs, shortcuts, Win+E, and SHOpenFolderAndSelectItems-style folder-plus-selection requests, including applicable caller architectures and cold/warm application states. The user's explicit scope correction assigns StartAllBack-specific links and Shell/known-folder/library routing to postponed S6/SAB-06; they do not gate S8 core delivery. Keep unsupported virtual locations, explicit explorer.exe launches and common Open/Save dialog behavior explicit; do not claim universal interception. Preserve actual prior registrations/owners, respect another default manager, and verify disable/uninstall/restoration, no loops or duplicate/transient Explorer windows for covered routes. Native code stays limited to routing; do not host the managed file manager inside caller processes or substitute global process-creation interception. Folder-browser scope does not imply desktop-shell or common-dialog replacement. |
| DELIVERY-01 | One installation, settings owner and update channel. Pin/package Everything and native preview dependencies; preserve external instances/data and prevent upstream updates replacing Pathweft. Under the user's 2026-10-31 instruction stopping Sandbox work, use the completed isolated-directory per-user host install/update/uninstall and preservation trials, then verify the final published and actual host-installed package. Do not reopen a guest environment as a delivery gate. |
| DELIVERY-02 | After S8 core-release criteria pass under the user's core-first scope, publish one normal versioned Pathweft release here: usable Windows x64 installer and portable package, source/tag, checksums, accurate notes and required source/notices. State signing identity/status honestly. Independently retrieve both assets and verify hashes/version/commit and packaged search/preview/transfer behavior. Identify S6/S7 as undelivered later integration work. Future implementation outcomes, including S13, own equivalent normal release, independent retrieval/qualification and applicable installed verification within their stage after the whole authorized outcome passes; do not publish partial modules or reopen completed S8 as their delivery owner. Plan/investigation-only work does not trigger software publication. No setup/intermediate commit is an application release. |
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
  S7 explicitly owns filesystem search replacement and Outlook-preserving indexing
  scope reduction, subject to actual consumer/semantic coverage and verified recovery.
- Existing application identity stays unchanged during setup; S1 must isolate it
  before runtime trials. No claim that the inherited installer is ready.
- No releases for intermediate edits or component work. The user-authorized core
  release belongs to S8 after its revised prerequisites/criteria pass; S6/S7
  remain later integration work and do not gate this core release.
