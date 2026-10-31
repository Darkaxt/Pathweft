# Pathweft specification

Version 0.23 - 2026-10-31. Owner: Darkaxt.

This is the authoritative specification. The derived plan must preserve every
requirement and acceptance criterion. Material scope changes require explicit
user direction recorded here; ordinary implementation choices need no new gate.

## 1. Intent and current scope

Current reconciliation: S8 is BLOCKED only on the user-controlled installed
update from1.1.6 to1.1.7. CORE-12/13 source, hidden real-account/navigation/layout
acceptance, final package, normal six-asset publication and independent retrieved
native/app verification pass. Release source is
eeb5d40a0b7ef2a3799445ad352ec21a5bad2c3b, tagv1.1.7. Exact scratch cleanup passes.
The published product is verified; the requested installed outcome is incomplete
until the user updates normally and the installed identity/files/runtime check
passes. This external normal-update boundary is not a missing publication
approval. Resume the retained read-only installed verifier after the updater
reopens1.1.7; do not close or reinstall over the running user application.
The user updated to1.1.6 through the built-in updater;
the new main application and Win+E helper are running. The user confirms Explorer
navigation stays in Explorer and Win+E works. User testing of external folder
links is expressly left until a real use case; retain the hidden native evidence
and distinguish that from a physical user check. Finish installed identity/routing
reconciliation alongside the current refinement; do not reinstall over the live
application or replace the user's registry from an inconsistent diagnostic view.

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
warning colors between This PC and sidebar: theme accent below 80% used,
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
| PREVIEW-02 | Initial source-integration scope: text/code, Markdown, images, PDF and archive listing. Preserve media/audio and font previews as internal selectable modules, reusing current code where suitable. Record the exact shipped module/source/dependency/format inventory before S3 acceptance. Importing every plugin is not required. |
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
| SAB-05 | Validate the complete integration in an isolated matching Windows/StartAllBack environment before a coordinated live-shell trial. Exercise enable/disable, actual native input, Explorer restart, relevant DPI/monitor/focus behavior, unavailable backend, host/app exit and an unsupported-version case. Verify restoration of normal StartAllBack search and absence of persistent orphan hooks. Use hidden diagnostics where possible; physical Start-menu acceptance requires actual visible input. S8 must package only Pathweft-owned adapter/source with exact supported-version notes and verify the delivered integration. |
| SAB-06 | Route ordinary filesystem-folder links and folder-result open/reveal actions originating in StartAllBack directly to Pathweft, preserving the requested folder and selected file where applicable. Preserve app/Settings/command launches and explicitly inventory virtual Shell locations that Pathweft cannot render. Test cold/warm Pathweft, Unicode/quoted paths, native selection, no duplicate or transient Explorer window for covered routes, and disable/recovery. Reuse the application launch/selection boundary; do not activate the inherited timed Explorer-window hiding/polling/closing watcher. Broader system-wide default-folder registration belongs to SHELL-01 in S8. |

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
| EVERYWHERE-03 | Reconcile filename/path search separately from file contents/properties, app/settings/commands and mail-store search. Everything owns filesystem search; Outlook retains its native local Windows Search and applicable server-assisted behavior. Do not promise Everything mailbox search or change Outlook account/search providers. Qualify required file-content/property capabilities against the actual Everything version/APIs. Missing required behavior remains a feasibility blocker; no silent substitute engine or new non-mail WSearch dependency. Report Windows-managed exceptions explicitly. |
| EVERYWHERE-04 | After replacement workflows pass, apply a reversible, recorded reduction of WSearch filesystem indexing scope while keeping WSearch available and Microsoft Outlook included. Exclude development/document and other selected filesystem roots, minimize duplicated file indexing and preserve required Outlook protocol/store indexing plus identified Windows-managed exceptions. Preserve actual prior crawl-scope/mode/service configuration and a verified recovery procedure. Use supported configuration APIs/UI and the administrator broker where required. Do not disable the service, delete its catalog, rebuild it by default, change unrelated policies or alter mailbox/user data. This revised plan does not itself authorize an immediate live change during S6. |
| EVERYWHERE-05 | Verify native Everything filesystem workflows and Outlook mail search under the reduced indexing scope: scope readback, actual backend ownership, file index freshness, Unicode, unavailable Everything, host/app restart, reboot/sign-in, updates, rollback and uninstall. Qualify local/offline Outlook search for applicable accounts rather than relying only on server results. Report actual residual filesystem indexing and resource effects without promising zero background work. Restore and verify the prior scope/configuration through the recovery path. Close every inventoried consumer gap; S8 verifies delivered packages and migration/recovery materials. |

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

### Context menu reorganization (S9)

The user's 2026-10-31 request adds S9 to the plan, without starting it during the
S8 appearance correction. It covers the Pathweft context menu; it does not
authorize redesigning Windows' native third-party menu or starting S6/S7.

| ID | Requirement and acceptance |
| --- | --- |
| MENU-01 | Inventory current commands and their eligibility for files, folders, mixed/multiple selections, empty space and archive entries. Reorganize related commands into clear groups/submenus, prioritize common file operations and remove duplicate presentation while preserving supported actions, keyboard access and existing shortcuts. Define the final grouping when S9 becomes ACTIVE using the existing Native/Original design contract. |
| MENU-02 | Preserve the selectable native Windows context-menu path, Shell extension behavior and capability-aware unsupported/read-only states. Verify real open/copy/cut/paste/rename/delete and archive operations through the reorganized menu, keyboard navigation, narrow/small-display placement and both themes at 100%/150%. Publish and verify the completed S9 change through the existing release channel. |

### Native Jump Lists (S11)

The user's 2026-10-31 screenshots add S11 as planned work: Pathweft's expandable
application entry should expose native pinned and recent/frequent folder
destinations, comparable to Explorer, plus useful tasks as demonstrated by
Directory Opus. This is native Shell Jump List integration, separate from S6's
StartAllBack search-provider work; no host Start menu interaction is authorized
as part of recording this plan.

| ID | Requirement and acceptance |
| --- | --- |
| JUMP-01 | Qualify supported native Shell/WPF Jump List APIs and stable Pathweft application identity. Show pinned destinations and recent/frequent folder destinations in the expandable app entry and taskbar Jump List. Preserve Windows-controlled pin/unpin/removal and recent-items privacy preferences; do not overwrite Explorer/Opus lists. Respect removed/rejected items and retain pinned state across updates. A Tasks-only menu does not satisfy folder-destination acceptance. |
| JUMP-02 | Folder destinations launch Pathweft directly with the correct Unicode/quoted path in cold and running-instance states, independently of the optional default-folder setting. Qualify virtual/offline destinations explicitly and preserve existing open/reveal semantics. Add applicable tasks through existing application actions, not a second manager. Verify actual native Jump List contents and user pin/unpin/open/remove behavior, restart/update/uninstall ownership and final release delivery. StartAllBack is a caller-compatibility check for this native feature, not permission to resume S6 or restart Sandbox. |

Native reference: [Microsoft WPF JumpList](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.jumplist).

### Lifecycle, shell integration and final delivery

| ID | Requirement and acceptance |
| --- | --- |
| LIFE-01 | Own initialization, readiness, cancellation and cleanup through actual state/events. Do not copy fixed sleeps/retries or arbitrary timeout-driven synchronization from donors. Isolate unavoidable external timeouts at their boundary. Verify owned helper exit before scratch cleanup. |
| SHELL-01 | Folder/default-handler and Win+E integration is opt-in, reversible and verified. Investigate Directory Opus's native Explorer replacement behavior and replace the inherited timed hide/poll/close Explorer-window takeover for covered entry points with launch-boundary routing. Qualify normal folder/default and explore verbs, shortcuts, Win+E, and SHOpenFolderAndSelectItems-style folder-plus-selection requests, including applicable caller architectures and cold/warm application states. The user's explicit scope correction assigns StartAllBack-specific links and Shell/known-folder/library routing to postponed S6/SAB-06; they do not gate S8 core delivery. Keep unsupported virtual locations, explicit explorer.exe launches and common Open/Save dialog behavior explicit; do not claim universal interception. Preserve actual prior registrations/owners, respect another default manager, and verify disable/uninstall/restoration, no loops or duplicate/transient Explorer windows for covered routes. Native code stays limited to routing; do not host the managed file manager inside caller processes or substitute global process-creation interception. Folder-browser scope does not imply desktop-shell or common-dialog replacement. |
| DELIVERY-01 | One installation, settings owner and update channel. Pin/package Everything and native preview dependencies; preserve external instances/data and prevent upstream updates replacing Pathweft. Under the user's 2026-10-31 instruction stopping Sandbox work, use the completed isolated-directory per-user host install/update/uninstall and preservation trials, then verify the final published and actual host-installed package. Do not reopen a guest environment as a delivery gate. |
| DELIVERY-02 | After S8 core-release criteria pass under the user's core-first scope, publish one normal versioned Pathweft release here: usable Windows x64 installer and portable package, source/tag, checksums, accurate notes and required source/notices. State signing identity/status honestly. Independently retrieve both assets and verify hashes/version/commit and packaged search/preview/transfer behavior. Identify S6/S7 as undelivered later integration work. No setup/intermediate commit is an application release. |
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
