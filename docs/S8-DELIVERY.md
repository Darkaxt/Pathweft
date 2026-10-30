# S8 core delivery contract

Authority: specification v0.10 and the user's explicit S8-first/delivery instructions.
S8 is COMPLETE as of 2026-10-31; S6 is BLOCKED by that instruction and S7 is NOT STARTED.
This release covers the file explorer. StartAllBack and broader Windows Search
replacement remain unfinished later work and will be identified as such.

## Design contract

Use Huashu as a design reference, applying the standing instruction to resolve
ordinary design decisions without introducing another approval gate. The supplied
Explorer screenshot is the concrete visual reference. Preserve the existing
layout, single/split views, recognizable file icons and keyboard behavior.

Considered directions: neutral Windows dark/light; subtle translucent Fluent;
and compact classic Windows. Select neutral Windows as the alternate Native
theme: opaque neutral surfaces, Segoe UI, restrained borders, no active-pane glow
or decorative gradients, neutral file-age metadata chips, and Windows accent
for focus, selection and progress. File-type icons keep their existing colors.
Keep the existing appearance selectable. Respect Windows contrast preferences.
Appearance owns theme and system/custom accent controls. Windows integration
owns default folders and the independently explained Win+E option; both are off
until explicitly enabled. Product dialogs and the search widget share the theme.

Verify actual WPF output at 100%/150% scale, single/split panes and minimum/normal
settings widths; controls, focus, selected text, menus and preview remain usable.
Hidden/offscreen rendering is preferred. Physical shell claims require separate
real input and launch-boundary evidence, not just rendered screenshots.

## Upstream review before compilation

File Labs reviewed from imported a8d5a5c through 9bfbbce (2026-10-30 fetch).

| Change | Decision |
| --- | --- |
| dabc9cb / 9bfbbce filter navigation, match counts and clear action | Port into current pane/search ownership, retaining Everything paging and coverage. |
| a44e780 inclusive size comparisons | Port `>=`/`<=` into the existing filesystem query parser. |
| 5fc2d5d batch-rename rollback | Port two-phase rollback so final names cannot obstruct restoring originals; report actual unresolved paths. |
| New persistent undo/journals, recycle-undo and card controls | Do not import the new feature and its transfer-state architecture during release integration. Retain current undo contracts; qualify the adopted rename correction separately. |
| Upstream transfer scheduler/copy engine changes | Keep the REA-qualified S5 engine and its adopted semantics. No blind Ferry replacement or donor timing claims. |
| New search continuation and custom size/date dialog | Retain explicit bounded filesystem search and existing Everything paging/coverage. These are additional upstream features, not fixes required to integrate the selected changes. |
| Large-selection debounce and watcher timer coalescing | Do not introduce fixed-delay synchronization or stale disk-existence caches. Retain current event/state ownership. |
| Missing sister-app menu preference | No new cross-product preference needed for core release. Retain existing explicit external integration behavior. |
| Notices/version/What's new | Preserve donor provenance; Pathweft receives its own version/notes, never an upstream product/update identity. |

Dependency review before the first build: retain F3D4.0.0 and 7-Zip26.05, the
current published native releases, and Markdig1.5.0. Use the installed .NET10 SDK
10.0.403 and target .NET10 LTS for delivery: .NET8 support ends2026-11-10, while
.NET10 is supported through2028-11-14. Adopt System.IO.Hashing10.0.14 servicing
and current stable WebView2 SDK1.0.4314.35; actual restore/runtime compatibility
and existing hash/editor/preview behavior must pass before adoption is accepted.
Keep SharpSevenZip's required local patch and the qualified embedded Rust ABI.

Sources: [runtime support](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[Hashing](https://www.nuget.org/packages/System.IO.Hashing/),
[WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2/),
[Markdig](https://www.nuget.org/packages/Markdig/),
[F3D](https://github.com/f3d-app/f3d/releases/tag/v4.0.0),
[7-Zip](https://www.7-zip.org/download.html).

GitHub API comparisons confirm EverythingToolbar and rars remain identical to
their imported commits. QuickLook is six commits ahead at8170d955; changes concern
VLC video rotation parsing, SVG web-view zoom, installer and application icons.
Pathweft uses its own MediaElement/bitmap renderers and packaging rather than
those donor components; no matching code path needs porting from that comparison.
The exact comparison report is retained privately; no donor whole-product upgrade.

## Acceptance ledger

| Requirement | Verified | Remaining advancement blockers |
| --- | --- | --- |
| CORE-04 Native theme | Actual offscreen dark/light, live editor and widget changes, Original switch, custom accent, settings at100/150%, single/split and separate-process persistence passed with zero foreground activation. Open-editor updater guard passes. Release member hashes bind these product changes to the delivered package; Native/System and Windows accent selected in the installed profile. | None. |
| CORE-05 upstream adoption | Pinned review and focused real filter, size-boundary and overlapping rename/rollback regressions passed. A real shutdown exposed a concurrent size-cache Count/CopyTo race; stable snapshots fixed the cause and the repeated native routing trial exited cleanly. Final packaged app-host self-test and real integrated workflows pass. | None. |
| SHELL-01 opt-in routing | Real32/64bit default/open/explore, Unicode/spaces, folder shortcut, CLI file selection, drive root, cold/warm singleton, no transient Explorer windows, actual Win+E input and exact prior-value/kind restoration passed. Windowless helper readiness/stop, install/update/uninstall and matched/mismatched updater identity checks passed. Final installation leaves routing unchanged. SHOpenFolderAndSelectItems returns E_ABORT and loses selection: explicitly unsupported in Settings/usage/release notes. | None within S8. StartAllBack-specific caller qualification/routing belongs to S6/SAB-06: actual Documents/library and Downloads links opened Explorer without a Pathweft request; prior handlers were restored. These links are not claimed as supported. |
| DELIVERY-01..03 | Clean source c20f9d7 packaged and normally published as v1.1.0. All six published asset sizes/digests checked and independently downloaded. Retrieved package app-host self-test, real-index search, transfer, ZIP/7z/RAR editing/extraction, Markdown/F3D preview and native exit passed without showing WPF windows. Actual FFmpeg9.0.1 shared LGPL/OpenH264 native configuration matches shipped source/notices. Actual installed members/version/source/self-test and Start-menu shortcut verified at D:\Tools\Pathweft; existing preferences/handlers preserved before explicit theme selection. Host install/update/uninstall and recovery passed. | None. User explicitly stopped the separate Sandbox gate. |

No release until all S8 criteria pass. S6/S7 stay parked. The user's 2026-10-31
instruction stops Sandbox work and directs final release/host installation.
DELIVERY-01 now uses the verified per-user host lifecycle trials plus final
published/installed-package verification. The attempted headless guest never ran
the installer and was stopped; it supplies no acceptance evidence. Do not reopen
it or label the entire program complete at S8 closure.

The user requires verification without taking over active windows. Use the
existing app-owned hidden actions/rendering and direct native Shell callers for
automated checks. Do not activate host windows or send keyboard/mouse input.
The user explicitly assigned the remaining StartAllBack-specific checks back to
postponed S6. Preserve the actual negative observations and Shell-target questions
under SAB-06; do not reinterpret them as passed Pathweft routing or an S8 gate.
Another virtual desktop or background UI automation on the same input desktop
does not establish isolation. This does not reopen S6's search-provider work.

Closure assessment: shell registration/recovery has one owner, Win+E has an
independent owned helper, and appearance has one shared palette. The inherited
Explorer-window takeover is removed. Current acceptance found no material
consolidation requirement; no extra refactoring stage is introduced. All assigned
S8 criteria are satisfied with no S8 blocker or tracked deferral.

## Final release and installed verification

[Pathweft v1.1.0](https://github.com/Darkaxt/Pathweft/releases/tag/v1.1.0) is a normal
published release, not a draft or pre-release. Its immutable source/tag commit is
`c20f9d7f989a9d4b0c2985fe9945e6bffd641f3a`; remote main and delivery branch were
read back at that source commit before publication. Later documentation closure
does not move the release tag.

The release contains installer, portable, application source, dependency source,
release manifest and SHA256SUMS. All GitHub asset sizes and SHA-256 digests match
the qualified originals. Independently retrieved copies passed checksum/member
verification and actual packaged workflows. The final native integration receipt
identifies original PID59344 and its unique fixture; the installed app-host
self-test completed with exit0. Existing Everything intermittently became busy
during extraction/build I/O; checks resumed after actual SDK database readiness
and responsive-window observation, without restarting or reconfiguring it. This
establishes real integration with the ready index, not a cold-start or timing claim.

The installer was run silently with no restart or integration tasks, using the
independently retrieved asset. `D:\Tools\Pathweft\Pathweft.exe`, version1.1.0, has
the expected release manifest and every installed member hash. Its normal per-user
uninstall registration and Start-menu shortcut point to that location. Installed
verification used an isolated profile; existing settings and folder handlers were
unchanged. Then only Native/System/Windows-accent appearance preferences were
explicitly selected to follow the user's system-theme request. Routing remains
opt-in under Windows integration. The executable/installer remain unsigned, as
disclosed in the release notes and manifest.

The earlier unexpected light window came from queued WPF normal startup in the
verification harness. The final source cancels that exact startup operation before
pumping the dispatcher and verifies all probe windows remain invisible. This is
bound to the fresh final integration receipts; the old visible probe is not used
as acceptance evidence.

Retained private receipts include publication metadata, independent retrieval,
final native integration, actual installation/self-test and cleanup manifests.
Requested local release assets remain in `dist/1.1.0-c20f9d7f`. S6 is BLOCKED and
S7 is NOT STARTED; this release does not claim their search or StartAllBack behavior.
