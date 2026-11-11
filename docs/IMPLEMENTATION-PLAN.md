# Implementation and reconciliation

Authority: [SPECIFICATION.md](SPECIFICATION.md), version 1.13, 2026-11-10,
including the user-authorized continued comparison requirement TRANSFER-07.

Current 2026-11-10/S45: S44 and S45 are COMPLETE. SHELL-04 and DELIVERY-04
passed protected Program Files installation and ACL checks, x64/x86 machine COM
ownership, opted-in route and profile migration, normal and packaged callers,
the actual Codex and Helium actions, protected update/restart, uninstall and
restoration, rollback by reinstalling the published package, and independent
normal v1.1.51 release/installed readback. The 1.1.49 installation was retired
through its own uninstaller during migration; the published v1.1.50 installer
remains available as the prior-version recovery package. See
[2026-11-10-s45-delivery.md](verification/2026-11-10-s45-delivery.md). No blocker
or tracked deferral remains.

QA review 2026-11-11 (Claude session, source-only): a deep review of the implemented source fixed
unhandled-exception paths in the administrator session, Maintenance clean, Ferry jobs, archive release,
update cancel, Settings, drive health, administrator and archive listing, the preview host and the
performance sampler; the single-instance listener now survives a dropped client; MultiRename rejects
trailing-dot names; Scoop cleanup now preserves a version whose .exe is running (write-access probe)
instead of partially deleting it. A follow-up bounds package-manager cache counts, rejects CONIN$/CONOUT$
archive entry names and guards This PC breadcrumb drive enumeration. Gated Debug and Release builds, Release --selftest and a
focused running-.exe Scoop test (version preserved while running, removed after exit) passed.
Accepted by the user: Ferry Overwrite replaces the existing target directly; a cancelled or failed
overwrite does not restore the original (no hidden temporary copy, which would double disk use and work).
The remaining QA findings were fixed for normal release 1.1.52 at the user's request: Setup's desktop
result channel connect and the child's wait are bounded (2 minutes) while the result stays untimed;
the single-instance hand-off refuses a listener owned by another account (owner must be this user or,
for this user's elevated window, Administrators); storage-move results are written atomically (the
location pointer already was); the Maintenance journal mutex carries a this-user/SYSTEM ACL so normal
and elevated windows share it; the Release elevated worker accepts only this profile's Maintenance
journal and review folder; the editor saves a symbolic link's final target. Normal v1.1.52/308ad07
was published, its six assets verified against the local package, and the user updated from within the app.

Current 2026-11-11/spec1.14: S25 is ACTIVE, widened at the user's request into a full UI QA review
(performance and design), an improvement round and an elegant animation enrichment round (UIQA-01..03,
MOTION-01..04). Plan: (1) rendered and source review with measured baselines; (2) findings list,
each fixed or rejected with a reason; (3) design and performance fixes through the shared owners;
(4) motion through Motion.cs; (5) rendered/measured acceptance, then one normal release.
Build stages: `s25-baseline` (Debug review harness S25Verification.cs: renders of every main surface per
theme/layout and in-process timings of ordinary browsing paths, run before any fix), `s25-refinement`
(design, performance and motion source changes, re-run of the same harness for before/after) and
`s25-release` (normal 1.1.53 package, checks and publication).

Historical S45 investigation: after the protected 1.1.50 migration and a fresh Helium
restart, the exact completed PDF exists and the enabled Show in folder control
dispatches its native Show request for download 858, but Pathweft remains hidden
with no new tab. The same Windows API from an independent process opens and
selects that PDF when given canonical shell IDs.
The user restored the desktop and the managed administrator gate completed the
Process Monitor trace. At the click, Helium successfully queries the PDF, but
the trace contains no Pathweft launch. Chromium's exact desktop parser and
absolute item sequence returns E_ABORT against installed 1.1.50. Pathweft's
FolderView joined that absolute item to its folder again. The native correction
accepts an absolute item only when it resolves inside the registered folder;
relative and Chromium forms now pass the private x64/x86 shell test. The installed
Chromium-form probe now returns S_OK, and the user confirmed the real Helium
Show in folder action opened Downloads with the PDF selected. The protected
update returned zero and restarted the desktop app. Final delivery is recorded
above.

The first 1.1.51 candidate installed its files but hung while restoring the
desktop Win+E helper: the medium-integrity child completed the helper work and
then crashed with UnauthorizedAccessException writing to an elevated named
pipe. The exact waiting pipe received a failure result through the managed
administrator broker, allowing Setup to exit without terminating either process.
Setup nevertheless returned zero after a post-install exception. The correction
puts the result pipe in the desktop child and makes Setup return a nonzero custom
exit code if integration or restart fails. This candidate does not pass S45.
The installed native probe also exposed a second browser identity mismatch:
Chromium's desktop parser gives the Downloads known folder a distinct PIDL from
the canonical shell parser. A private test against the actual Downloads folder
shows that registering both identities lets the Chromium call select the PDF.
  The next protected installer correctly returned exit 9 rather than falsely
  acknowledging success, but its medium-integrity result child found the pipe
  disconnected. The elevated broker and medium child have the same user SID;
  elevation changes the token owner to Administrators. .NET's
  `PipeOptions.CurrentUserOnly` client compares token and pipe owners, so it
  rejects this legitimate connection. The correction checks the pipe's owner
  against the actual signed-in user SID, then reads the child result. Its managed
  build and protected installer passed; Setup logged integration restoration and
  desktop restart, and returned exit 0. The installed browser action passed.

S45 migration checkpoint: normal 1.1.50 was published with six downloaded and
hash-verified assets at 46096fa. Its protected installer and machine class
passed Program Files ACL and Codex package-context readback. The first migration
preflight exposed two PowerShell script defects (untranslated built-in App
Package ACL identities and ambiguous null-conditional parsing) before any old
installation change. The corrected script then retired the old copy, restored
the opted-in routes and agent, and verified preserved profile file hashes.
The correction shipped in normal 1.1.51. Codex's actual Playground action passed
in protected 1.1.50; the final published 1.1.51 installation passed both live
Codex and Helium actions. Final release evidence is recorded above.

Historical 2026-11-10/S44 checkpoint (superseded by the verified 1.1.49
installation above): S41-S43 remain COMPLETE. S44 covers MAINT-20..24 and
the normal corrective release. It is BLOCKED on its installed-update criterion;
no stage is ACTIVE. It resumes when the user chooses Quit from the installed
Pathweft tray menu. Remove manual Refresh and
generic Analyze, refresh cached categories on activation without delaying the
tab, re-review Cleanup after actions, and keep technical details out of its
table summaries. Show Recommendation only in Windows tweaks, where its catalog
covers the rows; Startup, Services and Windows apps keep exact matched advice
in selected assessments. Subordinate inactive Performance, disable the Explorer-only
administrator shield in Maintenance and route protected reviews/actions through
Maintenance's independent elevated session. Acceptance requires the owned external-change/cleanup/UI
workflow, delivered-package verification, independent published-asset readback
and installed 1.1.49 verification. Normal 1.1.48 was published and its six
assets independently downloaded with matching hashes; installed ProductVersion
is 1.1.48, but its Npm cache Clean ends at zero because the manager's own logs
invalidate the whole-root comparison. MAINT-24 requires exact `_cacache` review,
truthful manager-command progress and a verified corrective delivery. The
owned Npm fixture, full Maintenance UI workflow and actual delivered package
passed; normal 1.1.49 is published with all six assets. Installed 1.1.48 is
still running with Close to tray enabled: a normal close hid its window, so the
installer was not launched. This external blocker affects only installed
1.1.49 update/readback. The user was asked to choose Quit from Pathweft's tray
menu; after exit, run the prepared exact 1.1.49 setup, read installed version
and app result, then close S44. No tracked deferral is open.
The bundled guidance has 40 reviewed tweak rules, but no general Startup rule;
the sole installed Startup advice is Pathweft's exact-command match. The pinned
Win11Debloat and SysOptimizer imports do not qualify arbitrary Run entries,
scheduled tasks, or the current installed app list for a blanket recommendation.
Previous source checkpoint: automatic category activation and post-clean review,
concise Cleanup summaries and tweak-only Recommendation headers compiled. The
2026-11-10 Explorer-only shield correction is now integrated but not yet compiled;
independent Maintenance elevation, the delivered package, publication and installed
behavior remain unverified.

Current2026-11-10: S41-S43 are COMPLETE. S43's four qualified native actions
and exclusion of unsupported managers passed the owned Maintenance UI trial
(220 checks), the delivered package probe (1,193 checks), release-source audit
and downloaded-asset SHA-256 readback. Normal v1.1.47 at c275de26 is published
at https://github.com/Darkaxt/Pathweft/releases/tag/v1.1.47. Its built-in
updater replaced installed 1.1.46; all 11,309 installed members match the
independently downloaded release manifest, the uninstall entry reports 1.1.47,
the original profile path was retained, both app processes restarted and the
installed --selftest exited 0. No ACTIVE stage, blocker or tracked deferral.

Previous2026-11-09: S41 and S42 are COMPLETE after normal v1.1.46 publication
and installed readback. S43 is the sole ACTIVE stage. The installed 1.1.46
Cleanup UI exposed seven unusable placeholder rows, a NuGet HTTP cache flag
without count or size, and a false missing-Python result for the installed
Scoop Python. The user's correction requires real supported actions without
placeholder rows. Source now targets four installed native cache actions, counts
and sizes NuGet HTTP files, sizes the Pip and Npm caches, and resolves Scoop
Python when the app's PATH is stale. Source integration, managed build, actual
UI/native read-only checks, independent package verification, normal v1.1.47
publication and installed readback remain. No tracked deferral is open.
Source ready for validation: the S43 table, manager discovery, reviewed counts
and byte estimates, changed-cache rejection, cleanup result reporting, owned
fixture and delivered-package assertions are wired in source. The seven
excluded managers remain documented without cleanup rows. No known source
implementation gap remains. The first compilation will use one Coordinator-owned
integrated Debug, Release Publish and independent-probe tree against the
committed v1.1.47 source; actual UI, native read-only inventory, package and
installed correctness are unverified until those checks complete.
Integrated ticket `15b17f87457117bfebabbaa3f093ce5b` completed managed
Debug and Release Publish compilation on attempt 4, workflow
`b21c49d120a34b2a6e9aa2122d685fcb`, with native exit 0 and verified tree
exit. The first actual hidden Maintenance trial passed the new Scoop ReadOnly,
manager-cache and cached Startup checks, then failed on Startup header reversal.
The trial raised a click before laying out the newly replaced GridView header;
the old assertion accepted the default Name sort as proof of its first click.
The DEBUG fixture now realizes each category before header clicks and requires
the first click to change direction. Corrected managed workflow
`622528e0838a88bce0c7ab1d12c6c677` completed Debug and Release Publish
with native exit 0 and verified tree exit. Its full 216-criterion hidden
Maintenance trial passed with no failure. Final package scripts now consume a
managed prebuilt independent assembly probe, avoiding an unmanaged .NET build
inside package verification. Source/release/probe rebuild, package and normal
1.1.46 publication/installed verification remain. The user's 20-second limit
applies to Scoop deletion only.

Previous2026-11-09: S43 was BLOCKED at compiled acceptance for MAINT-19.
Coordinator 0.26.1 is active and the integrated S41-S43 build receipt
`15b17f87457117bfebabbaa3f093ce5b` was admitted. Debug workflow
`7ee8b69f79faeb380373c9c81cb8a428` restored the project, then was
stopped at 18.5 seconds after the assistant misapplied the user's 20-second
Scoop-deletion limit to compilation; it exited
with a verified empty process tree and no compiled result. Release workflow
`c7e2c2be244b4c04bd8581cba4e3b20b` failed before launch because the
.NET gate accepts `dotnet build`, not `dotnet publish`. Its failure-linked
correction can declare a Release build, but that build and packaging remain
unrun. The cancelled Debug target cannot be corrected as a failed attempt;
resolve its admission through the Coordinator's supported owner route without
replaying or disguising the consumed ticket. The user's clarification removes
the build duration limit; allow the properly admitted build to complete.
The 1.1.46 release is absent and the installed app is still 1.1.45. The
original S41 receipt `194fa54cbc569b5cb29534bbe68aeec1` and its rejection
history remain intact. No stage is ACTIVE and no tracked deferral exists.

Previous2026-11-09: S43 was BLOCKED on the 0.26.0 gate admission defect.
UniGetUI enables eleven managers besides Scoop. Its installed 2026.3.3 API exposes no cache-clean action for
them despite newer upstream documentation. Native manager contracts are now
integrated in source for Chocolatey expired user HTTP entries, Npm package
cache, Pip HTTP/wheel cache and .NET Tool's NuGet HTTP cache; seven other
managers show explicit unavailable reasons. WinGet's installer download
location is the user's `D:/Downloads`, so it is not a cleanup target. The
native commands run in an owned process tree, with reviewed cache recheck and
post-action readback. The DEBUG owned contract fixture is prepared but unrun;
actual UI/resource behavior and final delivery remain unverified. The source
diff passes whitespace checks and is ready for compiled validation. The same
original managed gate ticket rejects before compiler dispatch, so S43 is parked
with S41/S42. See [manager review](verification/2026-11-09-manager-cleanup.md).
S42 is parked BLOCKED on the same managed-build admission as S41: its cached Startup
navigation and native regression checks are committed but uncompiled. No other
ACTIVE stage or tracked deferral.

Previous2026-11-09: S42 was ACTIVE for MAINT-18. The Startup transition was
rereading its Windows sources despite Overview having already loaded them for
the summary cards. Source now presents those cached rows on tab selection and
shows the selected empty table/loading state if the cache is absent. Refresh
and post-action loading still reread Windows. Actual UI timing, freshness and
combined release acceptance remain. S41 is parked BLOCKED on its original
managed-build ticket below; S42 has no dependency on a compiled S41 result for
source implementation. No second ACTIVE stage or tracked deferral.

Current2026-11-09: S41 is BLOCKED on the managed .NET compilation boundary.
The 1.1.46 source correction and owned ReadOnly-file fixture are present, but
Coordinator build ticket `194fa54cbc569b5cb29534bbe68aeec1` rejects exact
Debug attempt 1 before a workflow starts with `BUILD_INPUTS_CHANGED`; the
source pins match their files, and the same native rejection occurs with the
issued command/environment through the supported wrapper. `build-status`
reports no workflow or compiler result. The supported correction path requires
a failed workflow identity, which the predispatch rejection did not create.
Inspection of the installed 0.26.0 native client and its source at `2cbd031`
identified the cause: execution constructs an empty environment while the
issued check contains the declared build environment. An independently owned
0.26.1/`26ff1e1` candidate binds the issued environment and has passed package
verification, but its install is awaiting broker consent in its original owner
session. Separately, S42/S43 modified original pinned source documents, so the
old ticket's source digest is now stale. Do not create a replacement S41 scope
or bypass its correction history; resolve the original receipt through a
supported owner path after the Coordinator repair is installed.
This external gate condition blocks compiled fixture acceptance, final commit,
1.1.46 packaging/publication and installed readback. Resolve the original
ticket through the Coordinator's supported owner path; do not run a competing
direct build. The 15 user-reported host obsolete folders are now absent,
current bindings and six persist roots remain, and installed 1.1.45 passed
member/profile/routing readback. No other ACTIVE stage or tracked deferral.

Previous correction2026-11-09: S41 remained ACTIVE. Normal 1.1.45/113ae07 was
published with six hash-matched assets, independently downloaded and source
verified, then installed with 11,309 matching members, unchanged profile and
22 live shell values. Its exact host retry removed 14 of 15 reviewed obsolete
versions in 14.5 seconds. The sole MSYS2 residual stopped at a regular ReadOnly
`cacerts` file after partial deletion; current bindings and six persistent
targets remain intact. Correct direct deletion's ReadOnly recovery, verify an
owned fixture, deliver and installed-verify 1.1.46, then remove the exact
remaining residual. No other ACTIVE stage or tracked deferral.

Current correction2026-11-09: S41 is ACTIVE again for the installed MAINT-17
ReadOnly-junction regression. Live Cleanup completed 3 of 18 versions and
skipped 15 at persistent-data junction names with access denied. All 15 names
are ReadOnly junctions into Scoop persist. An owned NTFS reproduction and the
app's unfixed red trial confirmed nonrecursive .NET deletion fails on the link.
The corrected owned Maintenance trial passes ReadOnly-junction removal with the
persistent target unchanged, genuine busy/current failures, cancellation and UI
integration. Prepare, independently verify, publish and installed-verify normal
1.1.45, then retry the user's remaining reviewed host obsolete versions. No
other ACTIVE stage or tracked deferral.

Current closure2026-11-09: S41 is COMPLETE. The user superseded the earlier
executable-only preflight with direct deletion; the 23.217-second preflight
for 18 host candidates is gone. Owned-fixture UI verifies immediate deletion,
locked-member/incomplete reporting, cancellation, current-binding and
persistent-link protection. Normal1.1.44/a88da1d is published with six
hash-verified assets; independent package/source/native/application checks and
installed readback pass. The installed 11,309 members match the release;
settings/editor hashes and live routing are verified. Exact spent scratch and
obsolete local release copies are transactionally cleaned. No host Scoop
package was deleted as a test. No S41 blocker, tracked deferral or ACTIVE stage.

Previous delivery2026-11-09: S41 was BLOCKED only on installed verification.
Normal v1.1.43 at 7c26ab4 is published with six assets; final local and
independently downloaded packages pass source/package/application checks,
including real Everything search, compact popup, Maintenance and Office.
Owned-fixture MAINT-17 UI and host read-only timing acceptance passed below.
Obsolete local dist1.1.41/1.1.42 matched their published assets before exact
transactional removal; spent D:/E: scratch has been cleaned. Installed 1.1.41
is still running from D:/Tools/Pathweft. External blocker: its update/readback
criterion remains unsatisfied until the user closes it or authorizes closing
that live session. Resume S41 for the actual update, member/identity/profile
readback and final reconciliation. No other ACTIVE stage or tracked deferral;
do not claim installed parity from package verification.

Previous correction2026-11-09: S41 remains ACTIVE through final delivery.
MAINT-17 source and owned-fixture UI acceptance pass: Everything index misses
remain candidates without recursive fallback; consumer checks are batched;
Clean presents modeless progress/cancellation and permits navigation. The
started-folder cancellation test finishes that version and stops before the
next. Actual host read-only timing finds18 candidates in72ms, indexed sizes in
about1s, and batched consumer detection in23.5s. No host Scoop mutation.
Final/independent1.1.42 package acceptance, publication, installed verification
and cleanup remain. Published1.1.41's independent popup search failed because
Everything was not responding; a later original-consumer query passes but does
not explain the earlier failure. Preserve it and require unchanged full checks.
No host Scoop mutation, other ACTIVE stage or tracked deferral.

Delivery checkpoint2026-11-09: normal1.1.42/dd8d730 is published with six
hash-verified assets and passed local package checks. Its independently
downloaded package reproduces two availability defects: an intermittent
Office snapshot sharing violation on close and repeatable search rejection
from the advisory Everything hung-window check. The official asynchronous SDK
contract and read-only CLI query establish the latter as a Pathweft preflight
error. Remove that check and verify final/independent1.1.43 before S41 closure.
Office owner and transient-lock cause remain under investigation; do not mask
its package assertion. No host Scoop mutation or live-app closure.

Previous correction2026-11-09: S41 remains ACTIVE for final1.1.41 delivery.
Actual UI196476/result0 verifies Everything-based version estimates without
Restart Manager or deletion manifests, whole obsolete-folder removal with
current/active/external-data preservation, cards-first Maintenance, category/file
navigation during analysis/drainage, visible neutral eye and stable disk state.
Read-only host Scoop inventory returned34 versions/4,814,387,987 bytes in1,764ms,
fully indexed, without consumer registration. Native guest4340 donor-action and
exact Restore evidence remains applicable. Independent1.1.40/session78571 finished
result0; its spent retrieval/extraction and hash-verified obsolete local releases
were transactionally cleaned. Require final and independently downloaded1.1.41
package checks and publication. No external blocker, deferral or other ACTIVE stage.

Previous checkpoint2026-11-09: S39 COMPLETE: source98992 and guest4040/build26100;
normal1.1.38/5df239e final235436/independent233852 pass. Installed230000 verifies
11,305 members, ready restarted main232036/helper213028,22 routes and unchanged
profile/shortcut identity. Spent scratch and obsolete local dist1.1.37 are cleaned.
S40 COMPLETE through actual UI204416 for MAINT-12..13: direct actions, Startup
double-click/exact Restore, selection/Copy, ignored header/blank gestures and busy
gates with themed layouts. No required refactor. S41 ACTIVE: native guest4340
checks all24 adopted donor actions and exact Restore; actual UI205736 verifies
40-action catalog, compact complete details, exact guidance, Scoop cleanup and
active/current/persistent/changed-member preservation. MAINT-16 additionally
corrects read-only inspection/navigation gating, native Scoop cancellation and
the inherited Hidden command. Current UI213476 passes the full event-held scan,
native registration cancellation, category-result binding, cancellation ownership
and repeat-entry workflows. Normal1.1.39
is published; its final package passed but the independent popup assertion
failed. Unchanged delivered-widget recheck226712 passes with actual indexed
README results and its original row style; the first failure remains unexplained,
not claimed repaired. Corrected1.1.40 must pass full final and independent
delivery checks. No external blocker or tracked deferral.
S25 remains NOT STARTED. Native scratch D:/Temp/pathweft-s40 uses D for native/
small-file I/O; clean each spent output after its last consumer exits.

Current2026-11-08: S38 is COMPLETE at the user's explicit source-only boundary
after the already completed source/native/UI checks205020 and226536, and consumer
compilation. Exact cleanup5817d834423e7809b825b2c1e98a7395 removed26 retained-release
checksum-matching obsolete setup downloads,8,906,892,051 bytes. No further build,
package, version bump or release is authorized for this fix. Profile location
is unchanged. Spent build/test scratch is cleaned. The user-requested workplace
cleanup removes48,002,348,829 bytes of hash-matching published release copies and
5,586,164,979 bytes of superseded evidence; current delivery and reusable inputs
remain. STORAGE-LIFECYCLE.md replaces blanket local retention. Final source
commit follows; no other stage is activated. See verification/2026-11-08-s38-storage.md.

Initial S38 investigation (historical):
Evidence:26 updater setup files in the exact relocated Runtime/Temp; Download
only discards failed transfers. Expanded tree reads await the entire prior
scan, and changed cache measurements share the terminal Incomplete state with
unreadable subtrees. Remaining acceptance: lifecycle correction, responsive
direct membership, accurate states, real regression verification, exact legacy
cleanup and final verified release. The subsequent user instruction replaces
that release boundary with source-only completion. Initial internal blockers:
no tracked deferral. Preserve current profile and unrelated runtime consumers.

Current2026-11-08: S36 COMPLETE for ACCESS-04..05 through actual hidden UI161520
and helper198244: typed pane denial/action/ribbon, protected root and expanded
branch analysis mode changes, no privileged-row restoration and natural lifecycle.
Themes/scales/narrow and existing resource budgets pass. See verification/2026-11-08-s36-access.md.
S37 is COMPLETE. Normal1.1.37/6097ce4 is
published with six verified assets. Final application233952 and independently
downloaded199512 pass source/native/self-test/application acceptance. A retained
first-pass Office recovery sharing violation has no identified holder; focused
crash/cancel/close226780 and the full unchanged delivered application pass. No
renderer patch or root-cause repair is claimed. User-updated installed readback
222108 verifies11,305 matching members, restarted main234396/helper230560,
readiness,22 routes and preserved profile/shortcut identity. Exact spent
verification/download/installed-check scratch is cleaned through the helper.
No ACTIVE stage, blocker, tracked deferral or required refactor remains.

Prepublication source acceptance: DRIVE-01..02 source/native/UI
acceptance passes final UI187912 and approved UI176888/helper230364. Captured
elevated WMI DBNull conversion failure is corrected at the missing-value boundary;
actual shared helper health/read/analysis/editor/Cleanup/I/O reuse and natural
retirement pass. CPU/memory budgets, historical moment/range, physical identity,
virtual/network refusal, two-real-drive navigation and dark/light/scaled/narrow
layouts pass. Full specialist166052 remains applicable to unchanged paths;
the final delivered consumer compiles. Historical candidate186008 predates the
narrow correction and is not final delivery. Subsequent clean-source1.1.37
packaging, native/application/source checks, normal publication, independent
retrieval and installed readback are recorded above. No source blocker, tracked
deferral or concurrent implementation stage. Failed and successful evidence is
retained; spent candidate and native-test scratch is transactionally cleaned.
S35 is COMPLETE and remains included without an icon-only release. S37 owns the
combined release; no intermediate S36 publication.

Previous2026-11-08: S35 COMPLETE for CORE-25 source-only accents through actual
UI206648. Palette changes, glyph details/neutral outlines, themes/scales/DPI,
actual Hidden/pane/preview and file commands pass. No remaining source criterion,
blocker, tracked deferral or required refactor. No icon-only publication/version
bump. No stage ACTIVE. Establish separate S36 for the subsequently requested
protected-folder denial UX and analyzer administrator-mode qualification.

Delivery2026-11-08: S34 and S30 COMPLETE. Normal1.1.36/12807d2 is published;
final197288 and independent227432 pass source/native/self-test/application checks.
Installed readback236392 verifies11,305 matching members, restarted ready
main236072/helper123040,22 routes and preserved profile/shortcut identity.
Diagnostic scratch is transactionally cleaned. No blocker, tracked deferral or
required consolidation remains. No stage ACTIVE. Older records below are historical.

Current2026-11-08: S34 COMPLETE for MAINT-06..07 source/integration acceptance
through actual UI232500: automatic incremental/cancellable Cleanup queue,
numeric/bidirectional sorting across five tables, retained selection/reviews,
exact cleanup/restore and dark/light100/150-percent/narrow layouts. No blocker,
tracked deferral or required consolidation. S30 is now the single ACTIVE stage
for the renewed reusable administrator-toggle request. ACCESS-01..03 implementation,
real privilege/lifecycle/resource qualification now pass in UI218544 with helper235336:
protected browser/analysis/editor saves, concurrent-read admission, Cleanup/I/O reuse,
denial, exact owner/non-owner and natural retirement. UI229196 verifies compact toolbar
themes/scales and existing file commands; Maintenance234920 passes integrated queue/sort.
Final integrated release and independent/installed readback remain acceptance blockers.
The unpublished first1.1.36 package's independent header consumer was corrected
and now asserts actual sorted values. A transient Office temporary-file lock was
retained; twelve repeated fallback close cycles pass without a renderer patch.
The replacement committed package still requires full final acceptance.
No intermediate S34 publication. S30 owns final delivery. See verification/2026-11-08-s30-access.md.

Previous 2026-11-08: S31 COMPLETE through installed1.1.34 readback233140:
11,305 matching members, restarted main232344/helper233336, ready event,22 routes
and preserved profile/shortcut identity. Normal publication and independent
checks remain verified. S33 is COMPLETE for MAINT-01..05: shared
table busy-state palette, Startup source/type/action coverage, all installed
services and Performance command ownership. Source app134872 and native guest3620
pass affected table/native/workflow criteria; see verification/2026-11-08-s33-maintenance.md.
Normal1.1.35/2f62564 is published; final211320 and independent230548 pass native,
self-test and actual application acceptance. Installed readback203112 verifies
11,305 matching members, restarted main231460/helper207140, readiness,22 routes
and preserved profile/shortcut identity. Diagnostic scratch is cleaned. No stage
is ACTIVE.
No tracked deferral. S32 COMPLETE and
S30 NOT STARTED. No live user startup/service settings will be changed by tests.

Previous 2026-11-08: S32 COMPLETE for the user-authorized source-only ADOPT-01..06
outcome: five bottom Maintenance summary tabs, scoped native actions/exact
restore, app charts, boot/changes/insights and selected-query reports. Actual
Maintenance172948, adoption177288, history162888, Performance128464,
resource166776 and disposable native guest4300 pass. Reconciliation and limits:
verification/2026-11-08-s32-maintenance.md. No source blocker, tracked deferral
or required refactor remains. Natural close passes with a pending chart UI
presentation, draining background reads without awaiting the UI. No stage ACTIVE.
S31 delivery remains BLOCKED by the explicit
pre-release pause; do not publish/install. S30 NOT STARTED.
Network and hardware monitoring remain excluded. The user's profile is preserved.

Previous 2026-11-08: S31 BLOCKED by the user's explicit pre-release pause.
MONITOR-21 source/integration/resource acceptance passes: the recording switch
and setting are removed, startup/tray collection and saving are automatic,
hover/moment/range/app facts work, retention/capacity/WAL/migration/reopen/MCP
and existing layouts/lifecycle pass. Full app182592, final focused113480,
self-test152916 and resource181064 supply inspected evidence; the independent
consumer sources compile. See verification/2026-11-08-s31-automatic-history.md.
Remaining S31 criteria are publication, independently retrieved package checks
and installed delivery verification. Their external execution boundary is the
user's pause, not a technical failure. Resume S31 delivery when the user resumes;
do not start S30 or publish/install now. No stage is ACTIVE, no source blocker or
tracked deferral remains. The closure below is historical.

Historical 2026-11-08: S31 COMPLETE for MONITOR-17..20.
Source/integration/background acceptance now passes: app176784 measures cards,
expanded inspection and tray cost; final focused trialb8168940 passes the themed
filter/checkmarks, real WMI metadata, identity/pins, range recovery and natural
exit; resource19744 passes aggregate/recording/full-hour bounds and card switches.
Local donor comparison is recorded in SYSOPTIMIZER-PERFORMANCE-COMPARISON.md.
Normal 1.1.33 from 97e6cd2 is published with six hash-verified assets. Final
application 12344 and independently downloaded application 113332 pass source,
native, self-test and full application acceptance. Installed readback183244
verifies 11,304 files, restarted main182512/helper181108, readiness, 22 routes
and preserved shortcut/profile identity. S30 NOT STARTED; no tracked deferral
or required refactor. No stage is ACTIVE.
The persistent-only query defect and process inspection requirements are repaired
and verified. No acceptance blocker remains. S29 closure below
is historical.

Current2026-11-08: S29 COMPLETE for MONITOR-14..16 through normal1.1.32/2f3b1cd.
Source163416 passes graph moment/range/events -> linked facts -> reset/navigation/
back, routed keyboard/wheel, identity/gap/error, themed/scaled rendering and natural
lifecycle checks. Resource8824 observes expanded CPU0.454% normalized/138MiB
private, tray0.025%, without retained growth. Final165444 and independent175768
pass complete source/native/self-test/application checks; six published assets
match. Installed171532 verifies11,304 members, restarted main158024/helper169160,
readiness,22 routes and preserved shortcut/profile. The earlier shell-DLL load
attempt preceded actual setup completion; the final readback followed owner exit.
Evidence/releases retained; spent scratch transactionally cleaned. All assigned
criteria pass, no blockers or tracked deferrals. Closure review retains one
history/sampler owner; no required refactor. No stage ACTIVE. S30, S25 and
maintenance remain NOT STARTED.

Previous closure2026-11-08: S28 COMPLETE through normal1.1.31/e8dc64f. Native170636 passes real CPU/GPU/file
attribution, focus, navigation, tray and exact worker/session retirement;
protected request0ecad834edd14848bf3b40068299c752/ticket25 launched and exited0.
The earlier ticket24 expired without launching and is historical. Source116416
and analysis130208 pass the integrated browser/history/MCP workflows, actual
scrolled return, themes/scales, identity/retention/limit/gap and storage-error
shutdown checks. Resource172048 passes: recording0.1093% normalized CPU,
448KiB private growth, with existing off-mode/ring limits preserved. Installed
readback179008 verifies11,304 matching members, restarted main169944/helper169508,
readiness,22 routes and preserved shortcut/profile identity. Normal1.1.31/
e8dc64f is published with six checksum-verified assets. Final92112 and independent
download163252 pass complete source/native/application checks. All MONITOR-09..13
criteria pass; no blocker, required refactor or tracked deferral remains. No stage
is ACTIVE; S25 and maintenance
remain NOT STARTED. See
verification/2026-11-07-s28-system-analysis.md.
Exact D:/Temp/pathweft-s28 native/build scratch uses D: for sustained small-file
I/O and was transactionally cleaned after its final consumer. Installed-check
scratch was cleaned through transactioned7cd4770b60779ee597a2f9df543d49 with no errors.

Current2026-11-07: S27 COMPLETE through normal1.1.30/659691f. MONITOR-06..08
pass source104260, native160216 and sampler115172: actual counters/RAM,
statistics-height five-card dark/light100/150% narrow/wide layouts, automatic
folder-scope/navigation/visibility/lifecycle and numerical resource budgets.
Final154828 and independently retrieved168072 pass source/native/application
and actual short/hour/gapped rendered graph acceptance. Installed159192 verifies
11,301 matching members, restarted main174956/helper174452, readiness,22 routes
and preserved shortcut/profile identity. No blocker or tracked deferral remains.
Refactor assessment: existing collector/panel/worker ownership remains cohesive;
no required abstraction or restructuring. Retain evidence and transactionally
clean spent scratch. No stage is ACTIVE; S25 remains NOT STARTED.
SysOptimizer performance/MCP research is separate; security, DNS and app management
are excluded. Later adoption must fit existing Pathweft cards, detail layout,
themes/type/spacing and narrow/high-DPI behavior as requested by the user.

Previous closure2026-11-07: S26 COMPLETE through normal1.1.29/d66f0c6. Final163400 and
independent retrieved174916 pass corresponding-source/native/application checks.
Installed164052 verifies11,301 members, restarted main111432/helper157636,
readiness,22 routes and preserved identity/profile. Upstream8ceb281 ancestry and
CodeQL42392128695 pass; alert1's false-positive disposition is read back and
scanning remains enabled. All assigned criteria pass; no blocker/deferral. No
stage is ACTIVE. Evidence retained; generated scratch transactionally cleaned.

Historical checkpoint2026-11-07: S18 COMPLETE through normal1.1.28/cc9f0ea. Final and
independently downloaded packages pass source/native/application workflows;
installed readback152724 verifies11,301 members, restarted main173864/helper171780,
helper readiness,22 routes and preserved Darkaxt.Pathweft/profile identity.
All MONITOR-01..05/CORE-25..27 criteria pass; no blocker or tracked deferral.
S26 is now the only ACTIVE stage for the explicitly requested thirteen-commit
upstream sync through8ceb281 and code-scanning alert1. Static assessment shows
the alert's optional Monaco importScripts configuration is app-owned, unset and
unreachable from opened file/Markdown contents; GitHub false-positive dismissal
was written and read back, with the rule retained. Remaining: integrated merge,
affected real transfer/undo/filter/search/icon acceptance, final source/package
verification, commit, normal publication and independent/applicable installed
delivery. Preserve fork identity and omit upstream promotional changes.
S25 and later stages remain NOT STARTED.

S26 source checkpoint: application113800/restart163084 pass integrated native
move/Undo (including later additions, original-location collisions and partial
failure), exact Windows recycling/restoration, durable startup history, rename
rollback, unified query contracts and virtualized Drop bin. Native containing-
volume lookup and existing compact action styling are retained. See the exact
adoption/security record in docs/verification/2026-11-07-s26-upstream.md.
S26 remains ACTIVE for clean merge/package/native/runtime acceptance, normal
1.1.29 publication, independent retrieval and applicable installed verification.

Historical qualification2026-11-07: S18 was ACTIVE. The current native and
cross-integrity request8639b6fca3344a1bbb211db3c56587b8/ticket20 passed actual
attribution/focus/scope/cleanup and active/idle budgets. Worker171800 exited0
naturally after medium client171624 disposed it. The shutdown self-join and
scope acknowledgement race are corrected and requalified. CORE-26 now follows
the user's outer-pane edge and faint-opacity ribbon refinement; CORE-27 keeps
the current address color over a black loading track without replacing input.
Final UI app173512 verifies the faint ribbon's outer-pane edge alignment and
pending-read input/loading workflow; the user accepts its rendered appearance.
Candidate package app170864 passes complete native/self-test/application and
delivered counter/toggle/annual-resource workflows.
Remaining acceptance: final committed package/source/app
qualification, commit, normal release, independent retrieval and applicable
installed verification. No tracked deferral; S25 stays NOT STARTED.
Earlier qualification checkpoints below are historical, not current blockers.

Earlier qualification2026-11-07: S18 was ACTIVE, resumed when the user returned.
Native request900eaac1ed8541d087aa3a85e1646757/ticket13 completed successfully:
real CPU/GPU/file attribution, focus/scope transitions and owned cleanup pass.
Remaining detailed qualification covers medium-client/elevated-worker IPC and
measured trace cost. The user also requested removing the duplicate address-bar
capacity strip; preserve archive context, sidebar/lower capacity and real loading.
Independent source/artwork/resource checks are already complete.
MONITOR-01..05/CORE-25 are the current scope; S24 remains COMPLETE and S25 stays
NOT STARTED. CORE-26/27 add the user-directed strip removal, centered read-only
corner ribbon and address-background loading with draft/caret preservation.
The owned worker shutdown self-join and the in-flight scope acknowledgement race
are current-stage corrections; actual lifecycle requalification remains required.
Complete one real workflow: shared Windows aggregate collection ->
toolbar toggle/cards -> current path/device/GPU details -> focused folder trace
and truthful privilege handling -> tray/reopen history and scope cleanup.
Then finish all sources/states/resources/UI qualification, twelve actual
ImageGen banners and packaged annual selection; reconcile and deliver one
normal independently downloaded/applicably installed verified release.
Huashu uses existing StatCard/command/auxiliary references; the design contract
and numerical collection budgets are in the specification. Internal acceptance
blockers: native attributed-I/O/focus/lifecycle qualification, its measured cost
and final package/delivery qualification.
Source checks verify the toolbar/cards/tables, actual aggregate sources, tray
history, hour bound, pane ownership and light/dark100/150% layouts. Twelve
ImageGen banners render centered and select by release month across years.
Debug/Release compilation and Release self-test pass; the independent package
consumer compiles but awaits a delivered package. The isolated
sampler meets the initial CPU/memory budgets, and non-admin I/O reports actual
access denial. Native focus/ETW qualification is externally pending: Coordinator
request6d675d773db2494aaf8ba495572ef098 was not launched because fullscreen quiet
mode rejected it. Resume that exact test after user readiness/approval; no
automatic retry or bypass. No tracked deferral. Native/small-file task scratch is
D:/Temp/pathweft-s18 (D: required for native/build sustained I/O), registered for
transactional cleanup after each consumer exits; final assets/evidence stay in
the repository. The current app/native build is the preserved input for that
pending test. S18 is the only ACTIVE stage.
Do not change installed settings or start S25. See
verification/2026-11-07-s18-performance.md for evidence and remaining criteria.

Planning refinement2026-11-07: S18 is NOT STARTED. MONITOR-01/04/05 now require
the Task Manager Performance icon with active state, a shared bounded rolling
hour of CPU/GPU/drive-unit I/O while Pathweft runs in the tray, demand-driven
attribution and folder I/O only while its widget is focused. This supersedes
the earlier visible-only aggregate sampling rule. Qualify idle cost, retention,
session/sleep gaps, focus release and collector ownership; no collector runs now.
S25 Summary-card motion is NOT STARTED after S18 and before S12: upward card
entry/pane squeeze, Statistics/Performance rotation, reversible non-blocking
transitions and reduced-motion behavior. S25 owns motion acceptance and its later
delivery; S18 has no motion prerequisite. No stage is ACTIVE. Remaining order:
S18 -> S25 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Next-release companion2026-11-07: CORE-25 is NOT STARTED, assigned to S18's
next planned release. Generate twelve distinct monthly changelog banners with
ImageGen in a consistent Pathweft style and the existing960:170 presentation;
bundle and qualify all January–December selections by release month, including
year rollover. Inspect each asset and actual packaged rendering. No image
generation, implementation or stage activation occurs in this plan-only update;
S24 remains COMPLETE and no stage is ACTIVE.

Current2026-11-07: S24 is COMPLETE for the requested transfer progress refinement
(TRANSFER-17), before S18. Root cause: the concurrent-copy template renders a
secondary CurrentFraction track that is always zero; Fluent glyph and text runs
share an unsuitable baseline. Final source acceptance45728/113412 passes actual
visible-ink alignment, progress/counter/header/action layout, real transfer
lifecycle/error/conflict behavior and rendered themes/scaling/minimum width.
Normal1.1.27/6b294cf is published with all six assets. Final99016 and independently
downloaded85284 pass actual source/native/application qualification. Installed
readback99016 verifies11,287 matching members, restarted main90828/helper89284,
readiness,22 owned routes and preserved shortcut/profile identity. All assigned
acceptance criteria pass; no remaining blocker or tracked deferral. No stage is
ACTIVE and S18 is not activated.
Native/build/test scratch: exact D:/Temp/pathweft-transfer-progress and
D:/Temp/pathweft-transfer-progress-delivery for sustained native/small-file I/O;
register and clean disposable output after its last consumer. S18 stays NOT STARTED.

Previous closure2026-11-07: S16 is COMPLETE through installed1.1.26 acceptance.
All CRAFT-01..07/CORE-24 criteria pass with
no required blocker or tracked deferral. Final110748, independent42084 and
installed108640 establish delivery; see the S16 verification record.
The following implementation sequence records the completed authorized work.
CRAFT-01..07 and
CORE-24 are authoritative. Execute complete workflows sequentially: Office core
refresh/extraction; Photo; Vector; Fig; PDF extraction/comparison; icon sizing;
combined integration/source reconciliation; one normal final release with
independent and applicable installed verification. These are work units within
S16, not concurrent ACTIVE stages or separate publication checkpoints.

Acceptance satisfied at source: refreshed Office cores retain actual declared
formats/navigation, sidebar/Space/widget/archive ownership, source/MOTW and
single-attempt LibreOffice crash recovery. Located Office extraction passes
bounded Unicode continuations, native coordinates, empty/protected/malformed
input, actual sent-response cancellation and subsequent rendering/worker exit.
Old/new representative pagination/geometry matches; six rasters are identical,
one Excel page differs at31 pixels with unchanged inspected content/layout.
Native/source hashes, locks and selected Office graphs are recorded.
Photo source workflow96640 also passes actual native/PSD/PSB layer composition,
located Unicode/type-layer extraction, image-only/malformed states, sent-response
cancellation, all three preview hosts, archive/disable behavior and source/MOTW/
worker exit. Independent merged-image references retain inspected content/layout;
recorded raster differences are not concealed by a new tolerance gate.
Vector source workflow11704 passes actual native/SVG/SVGZ/PDF-AI artboards,
located imported Unicode text/continuations, cancellation, all three hosts,
archive/disable and source/MOTW/native exit. Inspected layout and exact sampled
solid-fill reference colors pass; legacy AI/EPS limits are explicit. Spent trials
and fixture executables were cleaned after evidence retention.
Fig source workflow97564 also passes native/Figma frame rendering, located text,
all hosts/archive/thumbnail priority, cancellation/source/MOTW and actual exit;
the exact stable-Rust donor compatibility edit is recorded reproducibly.
PDF source workflow98700 passes actual optional rendering and located extraction,
Unicode/order, empty/image-only/protected/malformed inputs, all hosts/archive,
cancellation and native exit. Windows remains the default. CORE-24 source icon
footprint/contrast and actual packaged native/WPF identity checks pass.
Selected source/dependency/license/advisory and Windows import review now passes;
all seven native builds/format/extraction/resource workflows are recorded in
docs/verification/2026-11-06-s16-craft.md. Combined candidate consumer106552
passes all seven core/extraction workflows and the existing application/native
checks. Actual executable icons at32/40/48/64px pass transparency/contrast and
visual inspection. Normal1.1.26/0856498 is published; actual final110748 and
independently downloaded42084 pass complete source/native/application checks.
All six remote assets, digests and exact source/tag identity pass readback.
Installed108640 verifies11,287 members, restarted main106840/helper84768, helper
readiness,22 owned routes and preserved Windows/shortcut/profile identity.
Acceptance remaining: none. No blockers or tracked deferrals. S19 indexing remains NOT STARTED and
consumes the extraction contract later. Native/source/build scratch uses exact
D:/Temp/pathweft-s16 paths for sustained small-file I/O; cleanup follows actual
last consumers. No installed Craft/Coordinator change; Pathweft was adopted
through the user's normal updater, with read-only installed verification.

Historical next-release refinement2026-11-06: CORE-24 was NOT STARTED, assigned alongside
S16 for the next planned release. Enlarge the taskbar icon's visible artwork
approximately10-15% relative to1.1.25, preserving the folder/compass design,
transparency, unclipped bounds and Windows identity. Acceptance requires actual
packaged taskbar/small-size display-scale checks. No implementation or stage
activation is authorized by this planning update; existing stage order remains.

Current2026-11-06: S23 Missing-folder tab recovery is COMPLETE for CORE-23 and
affected CORE/LIFE/DELIVERY behavior. Source92776 and actual Release consumer93428
verify nearest-existing ancestor recovery, selected header/list, keyboard/external
activation, restored pin/order/settings identity, archive/UNC preservation and
actual access-denied behavior. All original integrated consumer assertions pass.
The corrected consumer restores its isolated initial folder; no assertion was
weakened. Normal v1.1.25/11e5d292 is published with all six required assets;
independent downloaded-source/native/app verification passes in consumer87644.
Installed readback94548 verifies10674 matching members, restarted main78384/
helper59432, helper readiness,22 owned routes and preserved shortcut/profile
identity. The user confirms the updated behavior. All acceptance criteria pass;
none remain, no blocker or tracked deferral remains, and no stage is ACTIVE.
S16/S18/S12/S19/S20/S21/S22/S6/S7 remain unactivated.

Plan addition2026-11-06: S22 - Focused file workflow enhancements is NOT STARTED
immediately after Linux-inspired S21 and before S6. OPUS-01..10 register named
project workspaces, paired navigation, expandable/flat browsing, persistent
collections, operation history and listing export, followed by lower-priority
aliases, image marking and batch embedded-metadata/timestamp editing. Prefer
focused implementation in existing Pathweft owners; bounded REA only for a
concrete unresolved behavior/interoperability question, with qualified artifacts
and retained evidence before adoption. No installed Opus dependency or generic
framework. No criteria verified, stage activated, blocker asserted unresolvable
or tracked deferral introduced. Documentation only; remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S21 - Browsing productivity and smart destinations is
NOT STARTED after S20 and before S22. BROWSE-01..12 register the accepted Linux
suggestions and unified address dropdown: recent/pinned/open-tab/saved-query/path
matches; saved searches; configurable actions; per-folder profiles; recursive
comparison/reviewed sync; tags/ratings/comments; embedded synchronized terminal;
direct SSH/SFTP/WebDAV; temporary current-list filtering; and native link tools.
Reuse existing working features/owners, preserve address drafts/focus and user/
remote data, and qualify actual storage/runtime/protocol/resource boundaries.
No criteria are verified, no unresolvable blocker or tracked deferral is claimed,
and no stage is ACTIVE. This request updates documents only. Remaining order:
S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.

Plan addition2026-11-06: S20 - Integrated PowerToys file tools is NOT STARTED
after S19 and before S21. TOOLS-01..09 cover source-borrowed PowerRename, File
Locksmith, Image Resizer, OCR, Registry Preview, deterministic Advanced Paste
and lower-priority New+. Keep them in Pathweft's own commands/previews/operation
lifecycle, without installed PowerToys, runner, hooks or endpoint dependencies.
No acceptance criteria are satisfied; extraction, compatibility, dependency terms
and resource costs remain to qualify. No blocker is claimed unresolvable and no
tracked deferral is introduced. This request registers the stage only; no stage
is ACTIVE. The subsequent S21 request registers the Linux-inspired findings.

Plan addition2026-11-06: S19 - Selected-folder content indexing is NOT STARTED,
immediately after S12. CONTENT-01..06 cover a folder toolbar toggle, current-index
magnifier badges, qualified Craft/plain-text extraction, lean strings/location
storage without retained whole-document text, periodic metadata reconciliation
and cloud-cache independence. SQLite FTS5 remains a candidate requiring actual
qualification. No implementation/extraction criterion is satisfied; extractor APIs,
query granularity, metadata reliability and resource budgets remain unresolved.
This is documentation only; no indexer, schedule, download or settings change.
No tracked deferral or ACTIVE stage is introduced.

Plan addition2026-11-06: S18 - Contextual performance monitoring is NOT STARTED,
between S16 and S12. Remaining execution order: S16 -> S18 -> S12 -> S19 -> S20 -> S21 -> S22 -> S6 -> S7.
MONITOR-01..05 cover the Statistics/Performance toggle, four contextual cards,
Resource Monitor-inspired process/file details, qualified measurements and bounded
visible-only collection. This is documentation-only authorization; no stage is
activated and no collector/helper/system configuration is changed. No criteria
are satisfied yet; source/privilege feasibility and resource budgets must be
resolved before implementation acceptance. No tracked deferral is introduced.

Current delivery2026-11-06: S17 COMPLETE through the user-owned installed1.1.24
update and readback35716. Normal v1.1.24 is published
at abacf4171f753616dd50ac2233b58df364036692 with all six required assets.
Independent retrieval verifies asset hashes, corresponding sources, native
x86/x64 routing and actual app-host selftest. Full actual consumer57492 passes
the original assertions against the downloaded application; only its initial
folders were isolated and additional search diagnostics collected. All package
members remain hash-matched. The approved local Everything lvm=0/GUI recovery
restored SDK readiness and queries; its service and saved data are preserved.
Installed readback verifies10674 members, restarted main45604/helper68276,
readiness,22 owned routes and unchanged Darkaxt.Pathweft shortcut/profile identity.
All assigned acceptance criteria pass, with no unresolved blocker or tracked
deferral. No stage is ACTIVE; no later stage is activated. The existing design
assessment below remains applicable; no new consolidation is required.
See [S17 verification](verification/2026-11-06-s17-refinements.md).

The following authorization and source checkpoints are historical.

Current next-update request: S17 ACTIVE for TRANSFER-16, differences-first
conflict review with newer-destination warnings, precise conditional values,
explicit matching-file reveal and preserved full-set resolution. S15/1.1.23 is
delivered. Implement this complete feature, qualify real metadata/conflict/choice
flows and UI, then commit and deliver one normal independently/installed verified
release. Current advancement blockers are implementation/verification/delivery;
none are external or deferred. No other stage is ACTIVE; S16 stays NOT STARTED.

The user's additional PREVIEW-38 belongs to this same S17 release: smaller host
glyphs and selection-following PiP/Windowed content, replacing the former pinned
behavior. Qualify each complete feature sequentially, then integrate and deliver
the combined outcome. Previous host-mode state/restore/lifecycle criteria remain.
Explicit pin is also authorized: default follows; pinned retains the current
preview; unpin catches up immediately in the same host. The subsequently approved
CORE-22 folder/compass icon joins this same release, with transparent multiresolution
assets and preserved Windows identity. Source preview-selection61048 and broader
modes57588 pass actual following/pin/unpin and existing renderer/host lifecycle.
At that checkpoint transfer review, icon and final combined delivery remained;
the current source reconciliation below supersedes those implementation gaps.
Source60620 now verifies TRANSFER-16 actual four-choice outcomes, filtered metadata
matches with different contents, precise size/time differences, matching-file
binding and all-matching review. Rendered dark/light100/150 and prior virtualized
120-file/cancellation behavior pass. CORE-22 master/ICO size-sheet checks pass;
embedded final-package/Windows identity checks remain. Reconciliation/refactoring
assessment: one preview session still owns content; explicit pin replaces the old
implicit guard, and one filtered projection leaves the transfer plan authoritative.
No additional abstraction or consolidation stage is required. Final combined1.1.24
package/publication/independent/installed gates remain internal advancement blockers.
Reconciliation correction: the icon is CORE-22. CORE-12 already belongs to the
delivered useful-statistics behavior; this numbering correction preserves both
requirements and their original scope. No production behavior changes here.

The prior authorized round below is complete.

Current authorized round2026-11-05: continue implementing the native-category
correction, popup/update feedback and current-pane disk analysis. Sequence:
S11 COMPLETE -> S14 COMPLETE -> S15 COMPLETE. No ACTIVE stage.

Final delivery2026-11-05: normal1.1.23/fc01ce1 is published with six verified
assets and matching independently downloaded sources/package. Full final57312
and independent51296 pass actual native/app behavior. Installed58076 verifies
10674 members, restarted main/helper, helper readiness,22 owned routes and
unchanged shortcut/pin identity. S15's startup/loading, capacity-health colors
and TRANSFER-15 addition are satisfied. No required blocker or tracked deferral
remains in this round; later stages stay NOT STARTED/parked as assigned.

The dated checkpoints below are historical and superseded by this closure.

Resumed after the user's cleanup/pause instruction and subsequent "continue":
1.1.22/c3463e7 is normally published. Final local consumer30252 passes; the
independently retrieved assets/source hashes match, but consumer21780 fails
the actual index/widget workflow with Everything not responding. Installed
manifest22 was observed; full installed verification remains. The user also
reports an empty Pending tree in22, so its bounded stale-entry correction does
not close SPACE-01..03. Current internal blockers: cancellation/admission while
the optional SDK dispatcher is occupied, truthful initial loading feedback,
and the CORE-11 amendment making all healthy disk-capacity bars blue rather
than accent-colored. Verify cancellation, traversal fallback, repeated mode
entry/navigation and quiet background refresh before corrected final delivery.
Preserve the shared engine and all settings; no forced restart or index change.

The user's subsequent transfer-popup/Huashu request adds TRANSFER-15 to ACTIVE
S15 and the same final release: content-ready compact layout and conflict table,
completion-owned clean cancellation dismissal, preserved native clipboard/repeat
Paste, one aggregate speed for a single job and actual file-specific failures
separated from informational notes. This is additional acceptance work; no
partial publication or second ACTIVE stage. Native clipboard verification must
use an isolated station or managed guest, never the user's clipboard.

1.1.21 is published/independently verified and now observed installed. S15
remains ACTIVE for the reported entry regression: delayed admission was outside
the view's Work task, allowing an exited entry to resume over a newer tree.
The actual delivered-assembly reproduction retains valid current nodes/totals
while replacing its visible rows with an empty older collection. This internal
SPACE-01/03/LIFE acceptance blocker requires owned startup/admission, immediate
mode presentation, repeated-toggle/navigation/cancellation verification and final
corrected delivery; it is not deferred to S16. Separate live Everything health
reports process24032 unresponsive; do not restart or alter the shared engine.

Correction source acceptance now passes actual consumer18256 (admission,
repeated buttons, exact restore, re-entry and pending-entry navigation),15116
(watched refresh/selection/status/layout),43080 (broad tree/accounting/restore/
copy/cancellation and real index) and25848 (ordinary sizing/address/mutation/
overflow). The original native query completed on the same unchanged engine.
No additional architecture/consolidation requirement is justified by this
bounded lifecycle correction. Final1.1.22 package/source, normal publication,
independent retrieval and installed readback remain S15 acceptance blockers.

Latest steering during1.1.20 publication keeps S15 ACTIVE for entry row focus,
unclipped compact expanders, targeted/coalesced analysis refresh with stable
completed states, and consistent bottom-toolbar toggle state/icons. These are
internal acceptance blockers; verify real watched growth/shrink/rename/delete,
unrelated-branch preservation and actual input/rendered states.1.1.20 source,
final package154064 and normal publication at e8f9038 pass; independently retrieved
consumer164632 passes too. The next integrated correction owns final installed readback.
Do not activate S16 or ask for an intermediate installed update.

1.1.21 source/integration acceptance passes:171880 actual held-pass invalidation,
branch replacement/stable status/selection and compact toggle states; native
163812 first-row keyboard focus/private-desktop lifecycle;156464 broader tree/
restore/real-index/copy/cancellation;170340 ordinary address/size/overflow. No
additional architecture/consolidation requirement was found. S15 stays ACTIVE
for final committed package, normal release, independent delivery and installed
update/identity readback. This is one integrated correction, not a new stage.

Normal1.1.19 at2339fd0 is published and independently retrieved; installed
readback verifies10674 members, main154988/helper149844 readiness,22 routing
values and shortcut identity. S15 remains ACTIVE: user-reported stale disabled
analysis command, noisy known-size refresh status and interrupted address edits
are corrected and source50836 verifies actual watcher/edit behavior. The complete
1.1.20 candidate consumer passes actual toolbar invocation, quiet known totals,
row replacement, address editing/Enter, real-index search and existing previews.
Final committed-source packaging, normal publication, independent retrieval and
installed readback remain acceptance blockers. No S16 implementation is activated.
S11/S14 source/integration criteria close before advancement; S15 owns the one
combined normal release, independent source/package and installed verification
for this round. This supersedes their earlier plan-only/per-stage-delivery
wording without weakening any acceptance criteria. Source-only closure of a
predecessor does not claim final delivery. S12/S6/S7 remain outside this request.
The user confirms real folder pinning and restart persistence in installed1.1.18.
Native removal/reconstruction is verified by126032; do not request redundant
manual upstream checks. Installed update identity/pin preservation remains.

Latest S14 addition: TRANSFER-14 automatically dismisses successful transfers
and hides an empty Transfers window; unsuccessful/active work stays available.
Source acceptance includes real completion, multi-job and failure/cancel/partial
regressions; delivery remains the combined S15 release.

Current source checkpoint: S11/S14 COMPLETE for assigned source/integration
acceptance; S15 ACTIVE. Native91140 qualifies shared auxiliary/HTTP/update,
102436 transfer completion/adverse outcomes and103700 preview host/lifecycle.
See [S14 reconciliation](verification/2026-11-05-s14-windows.md). The unchanged
publisher reproduction133840 fails the native category contract. Corrected
native126032 passes actual Frequent readback/removal/privacy/reconstruction,
direct cold/warm launches and installed-list preservation. See
[the S11 reconciliation](verification/2026-11-05-s11-categories.md).
S15 explicitly owns actual packaged source/runtime/release/installed acceptance
and installed update/identity/pin-preservation evidence. These
do not gate independent S14 source work; they are required before overall
completion. No intermediate publication and no second ACTIVE stage.

S15 sizing addition already authorized2026-11-05: CORE-20/21 reuse Everything's
available indexed totals first, scoped metadata traversal otherwise, a bounded
RAM cache and transactional SQLite fallback persistence. The user subsequently
authorized enabling folder-size indexing in the existing engine; normal background
restart58916 and actual SDK/ordinary/analysis/mutation121860 pass. No engine
upgrade or other setting/scope/service changes. CORE-19 regression103988 passes;
metadata workflow22428 and isolated million-record migration/memory comparisons
pass. Current136168 passes the full analysis workflow, including repeated mutation,
real IPC coverage, bounded hot-cache persistence and virtual-pane ownership.
Final actual Release package/release/installed checks remain before S15 closure.
The user's2026-11-05 Craft maintenance handoff is assessed in
[Craft core review](CRAFT-CORE-REVIEW.md). Proposed S16 is NOT STARTED:
refresh existing source-built Office cores and qualify additional Photo/Vector/
Fig/PDF rendering libraries. This consideration request does not activate a
second implementation stage or change the current combined release scope.
Clean-machine Sandbox qualification passes absent/disabled/enabled Everything
capability paths. The full delivered workflow found a RAR loading blocker:
VCRUNTIME140 was not app-local to that module. The guest lacks the system runtime;
preloading the exact bundled runtime makes the unchanged RAR DLL load. S15 stays
ACTIVE while the deterministic app-local packaging/loader correction is verified
and the final combined artifacts are rebuilt. No host restart is required.

Current execution checkpoint (2026-11-04): user authorization covers S13, S9 and
S11 as one implementation/release outcome. S13 is COMPLETE against all assigned
source/integration acceptance; S9 is COMPLETE for its assigned source/integration
acceptance and S11 is ACTIVE. S12/S14 remain
NOT STARTED and S6 remains parked. S13 owns PREVIEW-21..37, CORE-18 and the
bounded MENU-03 presentation additions. S9 owns MENU-01/02 and the search-widget
containing-folder/Properties actions; S11 follows verified S9 acceptance.
S11 owns one shared final normal release and
independent/installed delivery verification for all three stages. That required
delivery work is tracked explicitly to S11, with no intermediate publication.
No implementation stage is complete until its assigned criteria pass.

Delivery checkpoint2026-11-05: normal1.1.18 is published at8191b55972700af87f963f6266c6373f61d64dc2.
All six published asset digests, independently retrieved source/dependencies and
native/app packaged workflows pass. Actual installed readback verifies10672
members, restarted main123876/helper130928, readiness,22 route values and
Darkaxt.Pathweft shortcut identity. Final local package127596/independent29992 receipts and
the installed receipt remain private; the prior candidate-only checkpoint below
is historical. S13/S9's shared delivery assignments are now satisfied.
The user shows StartAllBack's actual Pathweft destinations and confirms working.
Full native pin/unpin/remove/restart acceptance is still not independently proven.
The requested Explorer-style category correction is an additional internal
JUMP-01 acceptance blocker:1.1.18 still publishes custom groups/tasks. Keep S11
ACTIVE until native Pinned/Frequent publication, privacy/removal/direct launch
and final delivery of the correction are qualified. The resolution condition is
actual corrected native behavior, not renaming custom headings or relying on
the existing1.1.18 package. No other implementation stage is ACTIVE.

Current bounded investigation: [Diskweft source-integration assessment](DISKWEFT-INTEGRATION.md).
The user selected a reversible analysis mode in the current pane: Terminal-adjacent
toggle, Pathweft styling, current-path root, expandable tree, Space used column,
largest-first sibling order and exact standard-view restore. No standalone UI,
new window or new tab. Source feasibility is assessed; runtime/performance and
integration acceptance are unverified. No new implementation stage is activated.
S14 remains outstanding and ahead of unrelated StartAllBack work; the assessment
does not reprioritize or silently implement its proposed scanner integration.

Plan addition2026-11-04: S14 is NOT STARTED after the combined S13/S9/S11
delivery. WINDOW-01..03/UPDATE-02 cover a complete auxiliary-window design
inventory/contract, consistent actual layouts and immediate manual update-check
feedback/results. Preserve quiet automatic checks and existing deployment
protection/progress. It owns its later normal release and independent/installed
verification. Priority: S13 -> S9 -> S11 -> S14 -> S12 -> S6 -> S7. No S14
implementation or second ACTIVE stage is authorized by this plan-only request.
The user's search-widget QoL request belongs to S9: result-context Open
containing folder with selection and native Properties for the selected object
(including the .lnk itself), keyboard access and capability-aware states.

Latest user additions: S13 must replace the verbose Office recovery footer with
a concise orange bottom-right `Error loading: {filename}` status and full hover
details. The user's subsequent direct implementation request assigns MENU-03
compact icons to S13; S9 owns MENU-01/02 and is COMPLETE as recorded below.
Keep16-DIP command art in30x24 buttons and14-DIP pane art in28x22 buttons, no new
toolbar row, with accessible names/shortcuts and current state on hover.
Group creation, selected-item actions, transfers, Terminal and view controls with
four narrow dividers; verify order and fit after the user's screenshot correction.
Read installed SVGs with glyph compatibility; do not bundle Windows assets. Exact
local SVG variants and source hashes are retained
under .local/evidence/2026-11-04-specialists/explorer-icons; see EXPLORER-ICONS.md.

User extension2026-11-04: PREVIEW-37 Windowed and PiP modes belong to S13. After
Office integration, add adjacent mode icons. Windowed uses a resizable external
window and restores the same preview to the sidebar when closed. PiP uses a compact
corner overlay inside Pathweft, frees the column and offers return to sidebar.
Preserve actual viewer state and ownership across host changes. Verify Office/
media/3D/HTML/ordinary previews, navigation away, restoration, compact/narrow sizing,
source/archive leases, active-disable and app-exit cleanup. This is
assigned S13 acceptance, now verified below rather than moved to a later stage
or separate release. Existing S9/S11 prerequisites remain unchanged.

Office decision2026-11-04: use the pinned Craft source cores as they behave now,
with thin native-library bridges and no bundled Craft GUI/CLI/MCP application.
The user selected a small Pathweft-owned worker for genuine crash recovery and
authorized LibreOffice fallback for unsupported Office formats and failed/crashed
Craft rendering. Keep document state while navigating and render requested content
directly. Preserve original/MOTW, macro/link policies and process ownership.
New fallback starts only after actual primary failure/exit, once per request;
cancellation or policy rejection cannot start it. Source integration must preserve
upstream behavior, not invent missing Craft format implementations. No S9/S11 work
begins before complete S13 implementation/integration acceptance.

User extension2026-11-04: CORE-18 live ancestor-size updates belong to S13.
Root cause: SizeCache trusted only a folder's own timestamp; the view watched
direct children only, Patch updated directory sizes only when Modified differed,
and queued sizing results lacked a generation check. Source PID84388 now verifies
the complete CORE-18 workflow: external nested213MB installation, growth/shrink,
rename/removal, both panes and return navigation, unchanged sibling cache,
invalidated-scan rejection, overflow recovery and actual watcher teardown with
zero foreground activation. The extended workflow also verifies that a refresh
preserves selection atomically, without an intermediate empty-selection event
disposing a live preview. Unknown totals show measuring/partial state.
Source acceptance passes; shared final delivered-package verification remains
assigned to S11. The complete S13 source closure is recorded below.

S13 closure2026-11-04: all fourteen specialist categories now satisfy assigned
source/integration acceptance. Source PID101788 verifies actual64-page Word,
three-sheet workbook, distinct-slide navigation, native side/Space/widget/archive
surfaces, active-disable/locked-input release, original/MOTW and Native/Original
100%/150% layouts. PID117348 supplies the remaining DOC/DOCM/ODT/XLS/XLSB/ODS/XLSM
and protected-document workflows. PID108864 verifies real PPT/ODP/VSD/VSDX,
one fallback after exact primary-worker termination,64-page recovery, explicit
combined failure and policy/cancellation exclusion with actual tree/workspace exit.
No Craft GUI/CLI/MCP/installed product participates. Manifest status is qualified
for this source integration; final delivery is explicitly still unmet in S11.

PREVIEW-37 PID114112 verifies sidebar/in-app PiP/floating restoration, actual
Office navigation including fallback icons/orange bottom-right warnings, table/
HTML scroll, paused/playing native MPV HWND, F3D camera/animation and archive leases.
Native light/dark/Original150% Office mode moves preserve the same worker/page;
narrow sizing, active-disable and app-exit drainage pass. PID39948 verifies the
compact grouped/native icons, terminal glyph, real Copy/Move, View/Sort, hover/
keyboard/state, minimum sidebar/header and corrected About/child-row spacing,
with actual96/144DPI transitions and zero foreground activation. An initial
What’s New diagnostic exposed its missing Debug hidden guard; that guard is
corrected and the complete affected workflow passes, without Release changes.

Source/runtime closure:9584 Office runtime/manifest/notice members match;450
notice hashes and actual142/93/100 Windows build graphs are retained. The actual
176-query core/NuGet advisory review has only zip3.0.0 matches; selected read-only
named-part import excludes the affected extraction/decryption/streaming paths.
LibreOffice's233 distributed PE imports and149-entry official external build
source universe are inventoried without claiming all platform/build inputs linked.
622 corresponding-source members stage/hash-check successfully: complete donor/
font packs,156 dependency archives,450 notices and complete official core/dictionary
sources/recipes. Review the26.2 fallback maintenance transition before2026-11-30;
current official advisories are fixed in the selected26.2.7. Source/package docs
describe the actual DLL/worker architecture rather than the obsolete CLI baseline.

Closure assessment: one existing preview/session owner serves all specialist and
mode paths; native models/workers retain one explicit lifetime and one fallback
boundary. No material consolidation is required before S9. No required S13
implementation blocker or tracked implementation deferral remains. The only
required later work is the explicitly assigned S11 final combined delivery.

Spent candidate scratch and compiler/PDF-test output are removed through reviewed
transactions6618f469/45582aca/4188d3e9 (all applied without residuals); source,
native output baselines, lifecycle receipts and active dependency caches remain.
The older cleanup30534b06 still has locked Office logs under the exact
specialists-73d67fb1b8ed4fd1827e8a30c5b42d9c/temp path. WINWORD102304 remains
alive; preserve that consumer and retry its original ticket after actual release.
This residual is cleanup work, not evidence of passed Office integration.

### Current acceptance ledger

| Stage | Status | Satisfied | Remaining / blockers | Tracked delivery work |
| --- | --- | --- | --- | --- |
| S13 | COMPLETE | PREVIEW-21..37, CORE-18 and bounded MENU-03 source/integration acceptance pass. Thirteen specialist content/policy/lifecycle paths retain PID101932/102932/97712 and prior integrated receipts; Office closes with PID101788/117348/108864. CORE-18 PID84388, existing previews PID79048, shared PDF PID87760, modes PID114112 and compact/header/settings UI PID39948 pass. Source/license/runtime inventory and corresponding-source staging pass as recorded above. Shared1.1.18 independent/installed delivery passes | None within assigned source/integration acceptance | Shared S11 delivery assignment satisfied in1.1.18 |
| S9 | COMPLETE | Inventory/grouped menus, routed keyboard/theme100/150%, real archive edits/extraction and widget reveal pass PID127940/122788. Native122788 verifies Windows clipboard Copy/Cut/Paste and actual Shell Open. Native128032 verifies exact file/folder/shortcut Properties visibility/shown state, search dismissal and actual HWND destruction, regular native recycle completion and exact hash-matched restore. Owned job empty, user input desktop unchanged, no host activation. Actual independently retrieved/installed1.1.18 delivery passes | None within assigned source/integration acceptance. Test observations distinguish noninteractive station from private WinSta0 desktop; no native reparenting or product recycling workaround | Shared S11 delivery assignment satisfied in1.1.18 |
| S11 | COMPLETE | Corrected native126032 source/integration: actual Frequent readback/removal/privacy/reconstruction, cold/warm links and installed-list preservation; user reports pin/restart persistence | None in assigned source/integration scope | S15 final packaged/independent/installed delivery, including native menu update preservation |
| S14 | COMPLETE | Native91140 verifies shared layouts/themes/text/96-144DPI, navigation, actual updater HTTP/retry/cancellation/download/handoff.102436 verifies real successful transfer dismissal, Undo and adverse/multi-job retention.103700 verifies preview host/session/lease/lifecycle preservation. Scope reconciliation and closure assessment pass | None within assigned source/integration acceptance | S15 combined final release/independent/installed delivery |
| S15 | COMPLETE | SPACE/CORE source workflows44956/19244/51812/48584, transfer53320 and private native clipboard26532 pass. Full final57312/independent51296 verify actual1.1.23 package, sources and native/app boundaries. Installed58076 verifies10674 members, restarted main/helper/readiness,22 routes and shortcut/pin identity | None in assigned scope | None; combined normal1.1.23 delivery verified, no later stage activated |
| S17 | COMPLETE | TRANSFER-16/PREVIEW-38/CORE-22 source, combined package, normal1.1.24 release, independently retrieved full consumer57492 and installed readback35716 pass | None | None |

The earlier planning snapshot and historical per-stage delivery wording below
are superseded only for this combined request's sequencing by spec0.40. No
acceptance behavior or delivery evidence is waived. The stage map statuses will
be reconciled at meaningful feature/stage checkpoints.

S9 design/eligibility inventory is in CONTEXT-MENUS.md. Preserve existing action
owners; group secondary native commands under Transfer, Copy details, Archive,
Tools and More actions, with primary file commands at the root. Background uses
New/View/Sort/Select/Tools groups. Writable/read-only archive capabilities remain
explicit, and This PC/native extension ownership stays unchanged. The widget gets
its own selection-bound actions and keyboard paths, never the main pane's state.
S9 checkpoint: normal hidden source/UI PID127940 passes the complete menu/layout,
routed Right/Left keyboard, archive preview/rename/delete/read-only extraction and
widget reveal/dispatch workflow; retained receipts/PNG/JSON are private. Native
PID130144 on a genuinely private window station also passes real Copy/Cut/Paste
through the Windows clipboard and Ferry plus Shell Open of a windowless owned
receiver. It fails at the first native Properties widget-ownership assertion;
the observer may need to distinguish a child dialog from the top-level sheet,
but that remains a hypothesis. Do not modify production behavior speculatively
or claim native Properties/lifetime acceptance from the successful dispatch seam.

S9 closure: accepted native diagnosis129196 identifies the Shell's StubWindow32
owner; direct ownership of the invoking widget was an over-specified diagnostic.
The DEBUG observer callback is rooted statically through hook disposal. Native
122788 passes the complete clipboard/Open/grouped/archive/theme paths. Its final
recycle restoration fails because Microsoft.VisualBasic ignores recycling when
Environment.UserInteractive is false: this is a private-station test limitation,
not evidence of normal product recycling. Native128032 on an exact private,
never-switched WinSta0 desktop verifies actual visible Properties for all three
object types, exact selection and keyboard/reveal, regular native recycle
completion, exact restored hash, actual dialog destruction and full owned-tree
exit. The launcher verifies the user's input desktop is unchanged; the observer
distinguishes private-desktop activation from host-input-desktop activation.
No user clipboard/input/installed settings change. Raw target/owner/style and
lifecycle receipts remain private; do not claim an on-screen manual inspection.
Stage-closure assessment: existing action owners and menu nodes remain the single
source of behavior; no material consolidation is needed before S11. S9 has no
remaining source/integration blocker or tracked implementation deferral. S11 owns
its final delivery and is now the single ACTIVE stage.
The source distribution staging remains for final S11 packaging; all622 staged
sources and9584 Office runtime/notice members reverify against current pins.
No final commit, version bump or publication has occurred; installed1.1.17 and
its running main/helper remain unchanged.

Current plan revision (2026-11-02): add S13 for all fourteen remaining specialist
preview categories, PREVIEW-21..36. Remaining execution order:
**S13 -> S9 -> S11 -> S12 -> S6 -> S7**. Stage IDs are stable historical
identifiers, not execution priority. Pathweft improvements precede the final
StartAllBack/Windows-search block; S7 still depends on S6 before indexing changes.
This is a documentation-only update. S13/S9/S11/S12/S7 are NOT STARTED; S6 stays
BLOCKED/parked. No stage is ACTIVE, no implementation starts and the installed
1.1.17/release state remains unchanged. Completed S8 evidence below is preserved.

Current S8 status: COMPLETE through installed1.1.17 for PREVIEW-20/spec0.38.
Both approved fixes are implemented and source-qualified (PID194432); native173
verifies exact new FriBidi linkage and unchanged borrowed libraries, with empty
group/released mounts. Native RTL subtitles, editor50k Unicode/sandbox/formatting,
SDR/audio and paused/playing DV seeks/teardown pass; paired SDR/DV pixels match.
Normal1.1.17 at5019884 is published with six assets and matching source/notices.
Local package167100 and independently downloaded package202232 pass complete
native/app workflows, hashes and source/signing identity. All task scratch is
removed. The user updated/reopened; fresh desktop readback verifies all879
installed members, restarted main161564/helper167740, helper readiness and22
actual route values. Diagnostic scratch cleanup f81e354c1f3c1658cae444d1cf8ea9f6
is applied without residuals. No blocker, tracked deferral or ACTIVE stage remains.
Preserve the dirty donor worktree, retained caches, release assets and private
receipts. Other candidates/S12 remain unstarted. No further software release.
See verification/2026-11-02-deps117.md and .local/evidence/2026-11-02-deps117.

Previous S8 status: COMPLETE through installed1.1.16 for PREVIEW-19/spec0.37.
Normal release source35f01f6, six assets and matching sources pass. Independent
delivered native/app workflows PID149196 pass, as do all878 installed members,
restarted main134568/helper139436 readiness and22 actual desktop route values.
The user confirms the reported file now renders. All task scratch is removed;
no stage is ACTIVE and no current S8 blocker/tracked deferral remains.
See [evidence](verification/2026-11-02-html116.md). S12 is NOT STARTED and plan-only.

The mapped-drive HTML
regression is confirmed by metadata-only opened handles: a drive-letter path
resolves to its legitimate UNC share and fails literal path equality. Resolve
only the drive/share root, preserve checked remaining path components and all
static-resource restrictions. The complete source workflow passes in hidden
native PID151332: provider mapped/extended paths, local/archive assets, source
preservation, escape/junction rejection and browser teardown.

Previous S8 status: COMPLETE through installed1.1.15 for PREVIEW-17/18.
Both features are implemented and released together;
normal publication and independently downloaded native/app workflows pass at
85de43ec3b9385b11e803409dc898ed948e79ede. No stage is ACTIVE.
Both complete features pass the real native source workflow (PID143240), including
SNES playback/seek/clip/full-range and actual local/archive HTML, source fallback,
zero endpoint connections, boundaries and teardown. See
[acceptance](verification/2026-11-02-preview115.md). Package qualification,
normal release/retrieval and the user-controlled installed update are verified,
including actual local/archive fonts and matching source archives. All878
installed members, restarted main/helper ownership/readiness and22 real desktop
route values pass. Scratch cleanup0a36020d41ddd9f62cc84f5ba802568e is applied
without residuals. No current advancement blocker or tracked deferral remains.
Use D:/Temp/pathweft-s8-delivery for native/browser/compiler I/O and clean spent
output through transactional-cleanup. Retain required private evidence in
.local/evidence/2026-11-02-preview115. No Sandbox or desktop takeover.

Previous S8 status: COMPLETE through installed1.1.14 for UPDATE-01/PREVIEW-16.
Implementation, normal release, independent delivery and installed verification
pass. No stage is ACTIVE; no remaining S8 blocker or tracked deferral.
Previous1.1.13 acceptance is COMPLETE:871 installed member hashes, reopened main
PID125152/helperPID137620 readiness and22 actual desktop route values pass.
Current scope: installer-owned themed deployment progress that survives parent
exit, and video/3D timeline seeking while playing. Native installer progress
callbacks own deployment state; existing WPF update UI owns download/checking.
One shared preview timeline distinguishes user ValueChanged from engine feedback;
3D seeks must re-anchor its running animation clock. No new updater service.
Both features are integrated and source-qualified: real paused/playing libmpv
and F3D seeks, retained dragging/clock/cleanup, actual HTTP download/checksum/
cancellation, and hidden WPF/native light/dark100/150 layouts pass. The native
installer reports real file callbacks, integration/reopen phases and preparation
failure with no fake success. Its popup matches the existing updater's typography,
spacing and low progress track; no foreground input is used. Native paint/DPI
diagnostics resolved ParentBackground and capture-awareness defects, rather than
accepting clipped/blank screenshots. Private receipts are retained in the audit.
The complete1.1.14 candidate also passes its delivered native32/64 folder/reveal,
real-index, archive, storage, preview and playback workflows, including actual
paused/playing libmpv seeks and shipped updater palette arguments. Production
output contains no debug verification hooks. Source/native receipts and relevant
rendered states are retained privately; no live window was controlled.
Scope-closure assessment: no required consolidation finding. The small shared
timeline directly serves the two current engines; the existing installer owns
deployment state and uses the real requesting-process/event lifetime. No new
service, timer-based synchronization or competing state owner was introduced.
Normal1.1.14 is published at1d20122f52768c2760dd1f552d37e19f07ca67b0;
main/branch/tag readback, six asset digests, independent retrieval and the actual
downloaded package's native/app workflows pass. Its documented unsigned state
is verified. Source and downloaded updater/timeline checks pass. Reuse the
unchanged scope-closure assessment above; no required consolidation finding.
The user updated/reopened1.1.14. Fresh2026-11-02 Invoke-InstalledDesktop114.ps1
verification passes all871 installed member hashes against the independently
qualified release, main132612/helper130388 ownership and helper readiness,
plus22 actual desktop route values and configured/applied route equality.
Folder/open remains disabled; existing opted-in routes retain ownership. No
desktop input, folder launch or handler mutation was used. Retained private
installed114-via-updater.json and installed114-desktop.json close the last
acceptance blocker. The unchanged scope-closure assessment remains applicable;
no required consolidation, further release or postponed-stage implementation.
All exact task scratch D:/Temp/pathweft-s8-delivery is removed through reviewed
transactional-cleanup tickets with zero residuals; final transaction
3c1f187466b43f8bb132e78beaa50935 is applied. Preserve dist/1.1.14 and required
private evidence under .local/evidence/2026-11-01-update114.
Earlier1.1.13 delivery checkpoint follows as history.

Previous S8 status: BLOCKED only on installed1.1.13 verification.
Normal1.1.13 is published at26143f4b1dee768e587da6e13704005bbc71fede;
all six published assets match their source/tag and SHA-256 digests. Independent
retrieval passes native32/64 browser routing/selection, actual app-host self-test,
bundled media GPU rendering/teardown and integrated app checks with unchanged
packaged member hashes. No visible windows or live routing changes were used.
Source criteria for SHELL-02, PREVIEW-15 and drive-label consistency pass below.
Remaining criterion: actual installed/updater integration and all installed-member
hashes, process/helper readiness and desktop routing readback. The existing1.1.12
main/helper remain active and preserved. This is external/user-controlled update
readiness; the user has been asked once to update/reopen. Resolution/next action:
run the prepared read-only installed113 desktop-context verifier after reopen.
Until then S8 is not COMPLETE. No tracked deferrals or other ACTIVE stage.
Reviewed task scratch cleanup passes with no residuals; normal deliverables
remain in dist/1.1.13 and required private verification evidence is retained.
The earlier ACTIVE checkpoint follows as history.

Current reconciliation (1.1.13 candidate): native RegisterPending/Register folder
views preserve multi-selection and inactive-tab selection; opting out revokes
them. Actual Helium PID109860's controlled native request returned S_OK and the
hidden product selected the exact supplied file (PID58968), with no foreground
takeover. This is an actual caller-process boundary replay, not a browser UI click.
Folders/Drives C:/D: fallback labels pass. Bundled native media passes real SDR/
audio playback, seek/EOF, side pause, Space autoplay, module-disable and shutdown
cleanup. P5/P8.1/P8.4 preserve Dolby Vision metadata through D3D11VA; GPU frames
pass comparisons with source-bound fork references and the P5 negative control.
The Nuvio BT.2390 curve with explicit sRGB output and the fork's contrast/peak
settings is the qualified preset. No PNG roundtrip is used in product playback.
Closure assessment: one media client/event owner per preview, native-window
teardown awaits actual renderer exit; no duplicate installed-codec path remains.
The complete1.1.13 qualification candidate passes delivered native32/64 folder
selection/browser routing, actual portable app-host self-test and independent
assembly checks, including real bundled media GPU output and teardown. No visible
WPF window or live defaults were changed. Packaged member hashes remain intact.
Required remaining acceptance: committed final package, normal six-asset1.1.13
release, independent retrieval and installed/updater verification.
No tracked deferral or second ACTIVE stage. Earlier investigation below is history.

Helium's normal desktop restart resolves class visibility: the real user action
loads the shipped native handler, CoCreateInstance returns S_OK and ShellExecute
successfully launches Pathweft for Downloads. The user confirms folder opening.
File highlighting is explicitly requested and remains unsatisfied: the handler
passes only the folder and SHOpenFolderAndSelectItems returns E_ABORT afterward.
Complete native selection handoff and preserve Explorer/virtual/opt-out behavior,
then verify source, shipping package and real caller before one final release.
Folders now uses DriveRow.Label, like Drives, instead of raw VolumeLabel; the
reported C: has an empty label and D: is actually labeled Local Disk. Verify the
actual sidebar labels with the integrated change; no volume rename is intended.
The user subsequently authorizes bundled libmpv/DV integration as PREVIEW-15.
Implement complete native media playback/controls/lifecycle with the pinned
fork-derived runtime, then real media/color and packaged/installed verification.
Finish the current native selection contract before opening that implementation
front; keep one final release for the whole requested extension. No other stage
is ACTIVE. Prior1.1.12 source/package/installation evidence remains applicable
to unchanged behavior. The previous BLOCKED checkpoint follows as history.

Previous S8 status: BLOCKED for SHELL-02; SHELL-03 is verified and delivered.
Normal1.1.12 at363a266 is installed through the user's updater; all792 members,
reopened main/helper ownership/readiness and actual desktop22 registry values
pass. The user's real action was Show in folder. The native trace records
SHOpenFolderAndSelectItems and a Folder/open activation; the trial returned
REGDB_E_CLASSNOTREG. Only the added Folder/open test row is now disabled and
Windows' original handler is restored. Other desired/applied routes are retained.

Acceptance blocker: the real Helium request must open Pathweft and preserve its
supplied file selection, alongside the separate Open downloads folder behavior.
The caller and a fresh hidden child cannot see the registered Pathweft CLSID;
fresh desktop-context reads can. The cause is unresolved. This is an external
process/registry visibility impediment to the live acceptance check, not proof
that a product source fix is complete. Package-hive read returned access denied;
do not circumvent that boundary or copy registrations speculatively.
Resolution condition/next action: normal state-preserving Helium exit and desktop
relaunch, then repeat the bounded class-visibility check before the real request.
The supported wrapper exposes no explicit healthy-browser restart action; the
existing browser is user-owned and retained. User completion of that normal
close/reopen is needed. Required native reveal behavior and the final browser
acceptance remain unresolved; no dependent S8 completion/release claim is valid.
No other ACTIVE stage and no tracked deferral. All original delivery scratch and
the final REA staging are cleaned; retained authenticated diagnostic evidence is
private. REA/AppControl sessions are closed. No additional product release was
made for this investigation. Historical implementation checkpoint follows.

Previous S8 status: ACTIVE for SHELL-02/03. The user extends the pending update
with JSON-backed dynamic folder verbs and a Settings editor. Existing fixed
registration/source checks pass; b3f583c's unpublished1.1.12 package is superseded.
Dynamic configuration/editor, applied snapshot recovery, exact failed-apply
rollback, malformed JSON handling and version1..4 migration pass the complete
hidden feature check (PID113288). Actual row actions and dark/light100/150%
layouts pass; four screenshots are reviewed. The native router is unchanged.
Closure assessment: configuration owns desired routes; recovery owns applied
routes, using the existing native registration lifecycle. No speculative plugin
framework or additional state owner, and no required consolidation finding.
Normal1.1.12 is published at363a266. Local and independently downloaded six-asset
packages pass actual shipped editor/JSON and hidden native32/64-bit/app checks.
Remaining blockers: installed readback, user-controlled Helium action and final
scratch cleanup. The user has been asked once to update and click Helium's real
button; the active app remains untouched. One ACTIVE stage; no
tracked deferral or postponed-stage implementation.

SHELL-02 was already authorized by the user's go ahead.
Implement the missing explicit Folder/explore route using the existing opt-in
native handler; verify migration/restoration, the exact shipping Folder/explore
mapping, hidden32/64-bit COM/browser routing and a user-controlled final Helium
click. Host ShellExecute fixture checks opened unwanted Explorer windows; they
were stopped and are failed diagnostics, not acceptance. Registration migration/
restoration and hidden native32/64-bit COM/browser source checks pass. Remaining
internal blockers: packaged shipping-mapping verification and one final1.1.12 release/retrieval/
installed readback plus scratch cleanup. No tracked deferral; other stages stay
unchanged. Historical1.1.11 closure follows.

Previous S8 status: COMPLETE, CORE-16 general file opening and CORE-17 storage. Already authorized by
the reported silent Open regression and direction to verify all file types.
Root cause: Inno6.7.4 lowest-privilege installer passes enforcing RedirectionGuard
to launched apps, unlike the desktop. Native junction and association failures
are proven. Actual existing-desktop launch retains installer protection and
passes native32/64 checks and isolated installer/helper completion. General
double-click/context/Enter-F3 dispatch, row Message rendering/retry, real fixture
associations and editor/archive boundaries pass (PID61648).
Storage/drag source acceptance passes, including Change location, verified moves,
failure preservation/cleanup, an actual parent/worker exit handoff and themed
100/150% Storage layouts (PID66244). The historical abandoned copy is removed;
cache/editor data are migrated offline with hashes and private permissions retained.
Final normalv1.1.11 at76694a9 is published with all six assets; source/tag and
GitHub digests match. Local and independently downloaded native/app package
workflows pass (PID69544/57644). All792 installed members, reopened mainPID63812,
helperPID69224 ownership/readiness and desktop audit mode pass. The shipping
worker moved the actual desktop profile with exit0, preserved settings/runtime
content and left only a76-byte Roaming pointer. External diagnostic and desktop
Roaming views differed; actual desktop-context acceptance and pointer readback
resolve that verification issue without production workarounds. Exact abandoned
output, obsolete migration output and final scratch cleanup pass. Closure
assessment found no required consolidation. No S8 blocker, tracked deferral or
ACTIVE stage. Preserve the running app; no Sandbox/desktop takeover.

S8 COMPLETE through1.1.10 for PREVIEW-13/14. Historical1.1.9 was published/retrieved and the installed
792 hashes/new mainPID58436/helperPID24392 readiness pass, but rendering has
regressed. Native Rotate renders each input sample, defeating batching. The
selected native-equivalent math is now separate from draws, with camera/burst
parity and verified1.1.10 delivery. No ACTIVE stage, S8 blocker or tracked deferral.

Current source acceptance passes: native camera parity and ordered burst work
PID28160, complete mouse/resize/teardown PID62544, preview integration PID64724,
and pending-frame/layout lifecycle PID29524. The24-sample input path drops from
24 implicit draws/689.9ms to0 draws/2.77ms while retaining the same endpoint.
Normalv1.1.10 ataf82ca2 and six assets pass. Packaged consumer PID36932 and
independently retrieved consumer PID54992 pass real32/64-bit native/app workflows.
All792 installed files and reopened mainPID52824 match; user reports fixed.
No current helper-owner/startup/stored wine opt-in is visible, so no fresh Win+E
readiness claim or live setting rewrite. Exact cleanup applies7696 final members
after spent-output checkpoints; both scratch roots are absent. See
[current evidence](verification/2026-11-01-render1110.md).

Prior PREVIEW-13 correction used F3D's native
default input controller. Source confirms camera-local azimuth/elevation differs
from native fixed-up orbit/pole limits. Verify complete circular/reverse/pole and
pan/zoom/reset/DPI/lifecycle behavior, then package/publish/retrieve/install1.1.9.
The unsatisfied gesture contract and final delivery block closure; both are
internally actionable. No external blocker or tracked deferral. S9/S11 remain
NOT STARTED, S6/S7 postponed. Previous1.1.8 acceptance remains historical below.

Source gesture acceptance now passes (PID46772); preview regression PID55864
and completed-frame pressure/lifecycle PID18152 also pass. The old circular-roll
failure is reproduced before the change (PID46944). Native source/API inspection
and complete model image were reviewed; package consumer compiles with an added
delivered native-circle check. Only final1.1.9 package/publication/retrieval and
installed updater verification remain. See [evidence](verification/2026-11-01-orbit119.md).

S8 is COMPLETE through1.1.8 for PREVIEW-12 (continuous native 3D interaction/quality), CORE-14
(vertical Settings categories) and CORE-15 (Help reading-position highlight),
plus bounded JUMP-03 research/planning for S11. Complete user-facing workflows,
normal publication, independent retrieval and installed updater verification
pass. Source/API/render pipeline and existing Settings/Help control owners are
reused. No remaining S8 criterion, blocker or tracked deferral. S11
implementation and S6/S7/S9 remain unstarted/postponed.

Current source acceptance passes: complete Settings/Help/continuous-render
workflow PID10212; broader preview regression PID61704. Screenshots were visually
reviewed and source inputs preserved. The independent packaged consumer compiles
against the new assembly. JUMP-03's documented native destination plan is complete,
without claiming S11 runtime implementation. The scoped closure assessment found
no required consolidation. See [current evidence](verification/2026-10-31-render118.md).
Normal1.1.8/tagv1.1.8 source64118e9 is published with six verified assets.
Local packaged consumer PID61916 and independently retrieved consumer PID16188
pass, including actual32/64-bit Shell/COM and hidden app workflows. All792
installed files match; reopened mainPID4748/helperPID37904 and helper identity/
readiness pass after the user's updater. Final exact cleanup applies7692 members
after spent-output checkpoints; both scratch roots are absent and the older
partial ticket is closed. No ACTIVE stage or required consolidation remains.

Prior S8 is COMPLETE through1.1.7. CORE-12/13, integrated source and actual packaged
workflows, normal publication, independent retrieval, the actual user-updater
installation and exact cleanup pass; see the current verification document.
Published source: eeb5d40a0b7ef2a3799445ad352ec21a5bad2c3b. The installed
manifest's792 members match, reopened mainPID12928/helperPID40692 are current,
and helper owner/start identity and ready event pass. No forced close, reinstall
or desktop control. No required S8 blocker, tracked deferral or ACTIVE stage.
S6/S7 remain postponed; S9/S11 are NOT STARTED.

Current refinement contract: make bottom statistics reflect useful data, collapse
unavailable cards/columns and the entire empty strip, reclaim layout space and
restore applicable information without changing the stored full/compact/off
preference. This PC counts locations, not zero-byte folders. Verify the complete
hidden navigation/selection/search/archive/settings and themed layout contract,
then commit/publish/retrieve the integrated update. In the same S8 reconciliation,
verify the user's1.1.6 updater result without reinstalling over their live app.
The user confirms Explorer navigation and Win+E are correct; physical external
folder-link testing is explicitly left until a real use case. Its existing hidden
native proof remains distinct. The source/package requirements below are now
satisfied, including the installed1.1.7 update/verification boundary.
CORE-12 passes current integrated hidden navigation/selection/archive/search,
full/compact/off and dark/light100/150% layout acceptance (PID22756); CORE-13
passes the real two-account quota, all capacity surfaces, account/mount binding,
unknown/quota-unavailable handling and local/network preservation checks
(PID50104). Targeted dark/light screenshots were visually reviewed. The source
reads only the DriveFS account quota properties through Windows' read-only
SQLite interface, without auth access or cache/client setting changes. See
[current evidence](verification/2026-10-31-cloud117.md). Final packaged consumer
PID54992 and independently downloaded consumer PID45068 pass, including actual
32/64-bit Shell/COM/browser and external-launch verification, real quota values,
statistics collapse, index/archives/transfers/previews. Six published asset
sizes/digests and immutable tag/source match. Final cleanup applied9706 members
without residuals after two spent-output checkpoints. Actual installed identity,
files and reopened helper/main verification also pass. No implementation gap or
tracked deferral in CORE-12/13. Closure review found no design debt preventing
current acceptance; retain the existing bounded drive/statistics flow without
unrelated refactoring. This unchanged assessment applies to final reconciliation.

Previous checkpoint: S8 was BLOCKED on installed verification of SHELL-01.
Normal 1.1.6/source a7648b9d51c63d5aadac9e96b48ecbd8a428581c is published with
all six required assets and independently retrieved/verified. Downloaded native
handlers pass actual 32/64-bit COM/context-menu/browser and external launch checks;
the independently delivered app consumer PID 41776 and app-host checks pass.
The existing 1.1.5 main process still prevents host installation. This is an
external user-controlled exit requirement: finish transfers/editors and exit
normally, then run the prepared installed update and its exact registration,
program identity, profile/startup preservation and native-byte checks. No forced
close, desktop control, new publication approval or additional product release
is needed. Task scratch is cleaned through the reviewed helper: transaction
add460bb2ea452db26d38495899cdf8e applied 20,072 members / 3,677,774,353 helper bytes
with no residual; earlier spent diagnostics cleanup applied 201 members.
There is no ACTIVE stage or tracked deferral. S9 is NOT STARTED.

Investigation and implementation evidence:
The old UI label is cached at window construction; passive CLR inspection confirmed
that cached enabled text and the normal live profile, while fresh registry reads
show no owned values. Do not infer current registration from that label. Existing
code replaced the canonical open command with a context-free executable launch.
The correction preserves the native browser site through DelegateExecute and
refreshes Settings ownership on activation. Real hidden Shell IContextMenu calls
and actual ExplorerBrowser navigation pass for 32/64-bit handlers; external
receiver launch/path and original-process exit plus DLL unload pass. Private
registry checks pass for both legacy opt-ins, disabled integration and owner
restoration. Full current-source qualification, actual COM registry activation,
package/publication/retrieval and cleanup now pass. Installed verification remains
the external blocker above, with a prepared controller retained privately.
Prior S8 is COMPLETE for CORE-11 / PREVIEW-08..11: free percentages, consistent capacity
warnings, provider-based cloud labels, icon preview actions, themed animation
selection, seamless model composition, executable metadata, shared native/virtual
folder trees and selection-driven preview collapse. Source qualification,
normal1.1.5 publication at3fa4b465775ce411a89ba2628b1907420facac50,
independent downloaded consumer PID15184, installation and exact cleanup pass.
That prior closure had no remaining acceptance, blocker or tracked deferral.
S6/S7 remain postponed. The immutable release tag remains at its verified source.
S8 was COMPLETE through1.1.4: CORE-09/10, PREVIEW-06/07 and About are verified in
source and the final Release package, normal publication, independent retrieval,
installation and exact cleanup. The prior release source is
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
No required blocker or tracked deferral remains in the current S8 scope.
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
S7 is NOT STARTED; S8 became ACTIVE after the delivered v1.1.4 correction.
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

## Stage map in execution order

| Stage | Status | Requirements / complete outcome | Prerequisites | Acceptance evidence |
| --- | --- | --- | --- | --- |
| S1 - Isolated product and real pane search | COMPLETE | CORE-01..03, SEARCH-01..04, applicable LIFE-01/DELIVERY-03 | Implementation authorized, setup available | Real index/search/coverage, isolation, rendered UI, browsing, physical OLE drag and OS clipboard copy/paste/restoration pass; see S1 evidence |
| S2 - Connected taskbar workflow | COMPLETE | WIDGET-01..03, affected CORE-02/03, LIFE-01/DELIVERY-03 | S1 COMPLETE | Real widget query/handoff/reveal, shared actions/queue, actual native hotkey/typing before and after Explorer restart, hidden real mixed-DPI transitions and inspected File Labs layouts; see S2 evidence |
| S3 - Internal selectable previews | COMPLETE | PREVIEW-01..05, affected CORE-03, LIFE-01/DELIVERY-03 | S2 COMPLETE | Selected source/native inventory; complete real preview/toggle/file-release workflow with QuickLook absent; separate-process persistence; F3D model/camera/native teardown; hidden rendered acceptance and source self-test; see S3 evidence |
| S4 - Native archive browsing and editing | COMPLETE | ARCHIVE-01..05, affected CORE-02/03, PREVIEW-04, LIFE-01/DELIVERY-03 | S3 COMPLETE | Pinned native library/source/terms, real pane/clipboard/edit/open/preview workflows, source/destination volume ownership, preservation and actual consumer cleanup verified; see S4 evidence |
| S4R - Embedded writable RAR | COMPLETE | RAR-01..05, affected ARCHIVE-02..05/CORE/LIFE/DELIVERY-03 | S4 COMPLETE | Pinned loaded Rust DLL, real hidden pane/actions/queue, qualified solid/encrypted preservation and refusals, independent 7-Zip/UnRAR extraction, original/cut-source protection, clipboard rollback, cleanup and source commit/remote readback; see S4R evidence |
| S4U - Read-only cues and hidden visibility | COMPLETE | FILEUI-01..03, affected CORE/SEARCH/ARCHIVE/LIFE | S4R COMPLETE; user-approved icon direction | Real hidden browser/widget/tree/archives and native index workflow, persisted global toggle, grayscale/lock rendering, preservation, focused regressions, exact cleanup and source commit/remote readback; see S4U evidence |
| S5 - REA-driven transfer improvements | COMPLETE | TRANSFER-01..07, archive/caller and WIDGET-02 revalidation, affected CORE-02/03, LIFE-01/DELIVERY-03 | Initial S5 criteria passed at e478986; user extended comparison/implementation | Finite findings inventory resolved; source/target handle reuse, copy-time hashing, metadata/EOF reductions, storage decisions, matched profiles, actual callers, adverse/preservation/cleanup and Release checks pass; see [evidence](verification/2026-10-29-s5.md) |
| S8 - Core integration and delivery | COMPLETE | PREVIEW-20 targeted Marked/FriBidi maintenance; prior core criteria and DELIVERY-01..03 | Source/native, delivered1.1.17 package/source and installed879-member/main/helper/route checks pass; scratch removed | None within S8. No tracked deferral. |
| S13 - Remaining specialist preview modules | COMPLETE | PREVIEW-21..37, CORE-18, bounded MENU-03; affected CORE/LIFE | S8 COMPLETE | All assigned source/integration criteria pass; S11 owns shared final independent/installed release verification. |
| S9 - Context menu and command presentation | COMPLETE | MENU-01/02; affected CORE-03/04 and archive/file actions; MENU-03 compact presentation completed in S13 | S13 source/integration COMPLETE | Complete source/integration acceptance PID127940/122788/128032; S11 owns shared final delivery. |
| S11 - Native pinned/frequent folder Jump Lists | COMPLETE | JUMP-01..02; affected CORE-01/02 and LIFE/DELIVERY | S9 COMPLETE | Corrected native126032 source/integration passes; S15 owns combined final delivery/native update preservation. |
| S14 - Window design consistency and update feedback | COMPLETE | WINDOW-01..03, UPDATE-02, TRANSFER-14; affected CORE-03/LIFE/DELIVERY | S11 source/integration COMPLETE | Actual auxiliary/HTTP/update91140, real completion/error/multi-job transfer102436 and floating-preview lifecycle103700 pass. Combined delivery remains S15. |
| S15 - Current-pane disk analysis | COMPLETE | SPACE-01..05, CORE-11/19..21, TRANSFER-15; affected CORE/LIFE/DELIVERY | S14 source/integration COMPLETE | Source/native acceptance plus full final57312/independent51296 and installed58076 close normal1.1.23 delivery. No required blocker or deferral remains. |
| S17 - Transfer review, preview and icon refinements | COMPLETE | TRANSFER-16, PREVIEW-38, CORE-22; affected LIFE/DELIVERY | S15 delivered | Difference-only review/newer warnings/full-set actions; selection-following/pinned PiP/Windowed; transparent folder/compass icon and preserved Windows identity. Normal1.1.24 independently downloaded and installed-verified. |
| S23 - Missing-folder tab recovery | COMPLETE | CORE-23; affected CORE/LIFE/DELIVERY | User-reported vanished local version folder; correction precedes future feature stages | Source92776, final Release93428 and independent retrieved87644 pass recovery/restoration and preservation regressions. Normal1.1.25/11e5d292 published with verified sources/assets. Installed94548 verifies matching files, restarted app/helper, readiness, routes and shortcut/profile identity; user confirms behavior. No remaining criteria, blockers or tracked deferrals. |
| S16 - Craft core refresh and additional renderers | COMPLETE | CRAFT-01..07; CORE-24; affected PREVIEW/LIFE/DELIVERY | S23 delivered; existing worker and LibreOffice baseline | Refreshed Office plus Photo/Vector/Fig/optional PDF and bounded located extraction qualify in actual source/preview/worker workflows. Native/icon/source/advisory qualification, normal1.1.26/0856498, final110748, independent42084 and installed108640 pass. No acceptance criteria remaining, blockers or tracked deferrals. No next stage activated. |
| S24 - Transfer progress presentation | COMPLETE | TRANSFER-17; affected TRANSFER-14..16/CORE/LIFE/DELIVERY | S16 delivered; user requests compact progress/counter/action corrections | Source45728/113412 and final99016/independent85284 verify actual layouts, rasterized icon/text alignment, labels, transfer lifecycle and delivered behavior. Normal1.1.27/6b294cf published; installed99016 verifies matching members, restarted main/helper, readiness, routes and preserved identity. No remaining criteria, blockers or tracked deferrals. |
| S18 - Contextual performance monitoring | COMPLETE | MONITOR-01..05; CORE-25..27; affected CORE-03/LIFE/DELIVERY | S16/S24 COMPLETE | Normal1.1.28/cc9f0ea independently verifies source/native/app workflows. Installed152724 verifies11,301 members, new main/helper, readiness,22 routes and identity. All assigned criteria pass; no blocker or tracked deferral. |
| S26 - Upstream synchronization and security alert disposition | COMPLETE | User request2026-11-07; affected CORE-05/TRANSFER/LIFE/DELIVERY | S18 COMPLETE; before S25 | Normal1.1.29/d66f0c6 retains upstream8ceb281 ancestry and documented adoption/supersession. Final163400/independent174916 pass source/native/application workflows; installed164052 verifies11,301 members, restarted main/helper, readiness,22 routes and identity. Alert1 justified false-positive disposition is read back; rule retained and CodeQL42392128695 succeeds. No blocker or tracked deferral. |
| S27 - Compact automatic performance widgets | COMPLETE | MONITOR-06..08; affected MONITOR/LIFE/DELIVERY | S26 COMPLETE; before S25 | Source104260/native160216/sampler115172 qualify compact RAM/counter/layout, automatic folder-scope and lifecycle/resource behavior. Final154828/independent168072 pass actual graph/source/native/application workflows; installed159192 verifies normal1.1.30/659691f, matching files, restarted main/helper, readiness, routes and shortcut/profile. No remaining criterion, blocker or tracked deferral. |
| S28 - Integrated system analysis and recorded history | COMPLETE | MONITOR-09..13; affected MONITOR/LIFE/DELIVERY | S27 COMPLETE; user selected performance/history/read-only MCP before maintenance | MONITOR-09..13 verified by source116416/analysis130208/resource172048/native170636 and final92112/independent163252. Normal1.1.31/e8dc64f and six assets published/read back; downloaded hashes/source/native/application checks pass. Installed179008 verifies11,304 matching members, restarted main169944/helper169508, readiness,22 routes and preserved shortcut/profile identity. Spent scratch cleaned; no blocker, required refactor or tracked deferral. |
| S29 - Interactive performance investigation | COMPLETE | MONITOR-14..16; affected MONITOR/LIFE/DELIVERY | S28 COMPLETE; user reference2026-11-08 | Source163416/resource8824 pass graph-led investigation and preserved scope/layout/lifecycle. Normal1.1.32/2f3b1cd published; final165444/independent175768 pass source/native/application checks. Installed171532 verifies11,304 members, restarted ready main/helper,22 routes and preserved identity/profile. Scratch cleaned; all criteria pass. No blocker, deferral or required refactor. |
| S31 - Performance continuity and process inspection | COMPLETE | MONITOR-17..21 and final S32 delivery | S29 and S32 COMPLETE; before S30 | Normal1.1.34/2de366d published; final215364/independent172600 pass all delivered checks. Installed233140 verifies11,305 members, restarted ready main/helper,22 routes and preserved profile/shortcut. No blocker or tracked deferral. |
| S32 - Local SysOptimizer adoption | COMPLETE | ADOPT-01..06 | Source work authorized during the former S31 publication pause | Five bottom Maintenance summary tabs, scoped native actions/exact restore, app charts, boot/changes/insights and reports pass source/native/rendered/lifecycle/resource acceptance. Delivered in normal1.1.34; final and independent actual assemblies pass. Installed parity remains in S31. No source blocker or tracked deferral. |
| S33 - Maintenance coverage and table states | COMPLETE | MAINT-01..05 | S31/S32 COMPLETE; before S30 | Source134872/native3620/Performance60844 pass shared disabled-state theme, Startup source/Type/exact Restore, full SCM inventory and mutually exclusive toolbar modes. Normal1.1.35/2f62564 is published; final211320 and independent230548 pass actual native/self-test/application checks. Installed203112 verifies11,305 matching members, restarted ready main/helper,22 routes and preserved profile/shortcut identity. Scratch cleaned. No blocker, tracked deferral or required refactor. |
| S34 - Automatic Maintenance review and sorting | COMPLETE | MAINT-06..07 | S33 COMPLETE; before S30 | Actual UI232500 verifies sequential automatic review, incremental summaries/cancel, real headers/numeric units, selection/sort retention and existing exact cleanup/restore with themed layouts. No blocker/deferral/refactor. Final delivery is integrated with S30. |
| S30 - Reusable administrator access session | COMPLETE | ACCESS-01..03; affected CORE/LIFE/DELIVERY | After S34, before S25; renewed user request2026-11-08 | Source218544/helper235336 and owner233356/helper225008 pass protected browser/analysis/editor saves, concurrency, reuse, denial, owner/lifecycle and original resource budgets. Normal1.1.36/12807d2 final197288/independent227432 pass. Installed236392 verifies11,305 matching members, restarted ready main236072/helper123040,22 routes and preserved profile/shortcut. No blocker, deferral or required refactor. |
| S35 - Toolbar action accents | COMPLETE | CORE-25 | S30 COMPLETE; source-only user instruction2026-11-08 | Actual UI206648 verifies accent mapping, original terminal lettering, smaller gear/brush collar/two-pane divider, neutral outlines,16-DIP geometry, themes/scales/DPI and real commands. Source-only verified scope; no blocker, deferral or required refactor. Publication held against an icon-only release. |
| S36 - Protected-folder presentation and analyzer access | COMPLETE | ACCESS-04..05 | S35 COMPLETE; before S37 | Actual UI161520/helper198244 pass typed pane denial/action/ribbon, root/expanded analysis privilege changes, no stale privileged listing, layouts and lifecycle/resources. No source blocker/deferral. Final delivery belongs to S37. |
| S37 - Drive-scoped performance detail | COMPLETE | DRIVE-01..02; final combined DELIVERY | S36 COMPLETE; before S25 | Source/native/UI and helper acceptance pass. Normal1.1.37/6097ce4 is published; final233952 and independently downloaded199512 pass source/native/self-test/application checks. Retained uncertain Office sharing failure is followed by exact crash/cancel/close226780 and full unchanged-package acceptance; no speculative renderer patch. Installed222108 verifies matching members, restarted ready main/helper, routes and profile/shortcut identity. Spent scratch cleaned; no blocker or deferral. |
| S38 - Updater scratch and live analysis disclosure | COMPLETE | STORAGE-01/SPACE-08 | S37 COMPLETE; explicit source-only boundary | Actual source205020/226536 and consumer compilation pass. Legacy cleanup removes26 checksum-verified updater downloads,8,906,892,051 bytes. Preserve relocated profile; no further compilation, version bump or release for this fix. No blocker/deferral/refactor. |
| S39 - Maintenance guidance and categorized settings | COMPLETE | MAINT-08..11; final DELIVERY | S38 COMPLETE | Source98992/guest4040; published1.1.38/5df239e final235436/independent233852 and installed230000 pass. Pending recommendations/dropdown alignment verified; spent scratch cleaned. No blocker/deferral or required refactor. |
| S40 - Direct Maintenance actions | COMPLETE | MAINT-12..13 | S39 COMPLETE | Actual UI204416 verifies owned Startup double-click/exact Restore, direct buttons/external-change refusal/Cleanup, selectable exact assessment, ignored header/blank and busy gating, dark/light/scaled/narrow presentation. No required refactor/blocker/deferral. S41 owns combined final delivery. |
| S41 - Complete Maintenance adoption and cleanup | COMPLETE | MAINT-14..17 and final DELIVERY | S40 COMPLETE | Scoop cleanup checks passed and normal v1.1.46 was independently verified, published and installed with exact member readback. No blocker or tracked deferral. |
| S42 - Fast Maintenance tab navigation | COMPLETE | MAINT-18 and combined final DELIVERY | S41 required UI checks passed | Actual Cleanup-to-Startup transition, Refresh and action results passed; normal v1.1.46 delivery and installed readback completed. No blocker or tracked deferral. |
| S43 - UniGetUI manager-aware Cleanup | COMPLETE | MAINT-19 and corrective final DELIVERY | S41/S42 COMPLETE | Four actionable native caches and exclusion of unsupported managers passed actual UI, package and installed checks. Normal v1.1.47 was published, independently downloaded and installed with exact member readback. No blocker or tracked deferral. |
| S44 - Maintenance clarity and automatic freshness | COMPLETE | MAINT-20..24 and final DELIVERY | S43 COMPLETE | Npm correction, owned UI, delivered package and published 1.1.49 passed. Installed D:/Tools/Pathweft executable reports 1.1.49+2b2adf6f and its SHA-256 matches the release manifest; the running front and agent use that installation. No blocker or tracked deferral. |
| S45 - Protected shell installation and packaged callers | COMPLETE | SHELL-04, DELIVERY-04 | S44 COMPLETE | Protected migration, actual Codex/browser action, update/restart, uninstall/restoration and published-package reinstall passed. Normal v1.1.51/5a6aadd was independently retrieved, installed and read back. Prior v1.1.50 installer remains published for recovery. No blocker or tracked deferral. |
| S25 - UI quality review, refinement and motion | ACTIVE | UIQA-01..03, MOTION-01..04; affected MONITOR/CORE-03/LIFE/DELIVERY | S18 COMPLETE; after S28 and before S12 | Upward card entry with smooth file-pane squeeze; rotation between Statistics/Performance; reversal/interruption and focus/scroll preservation; reduced-motion support and actual frame/layout/resource qualification. One final independently/installed verified release after implementation authorization. No criteria verified yet. |
| S12 - Tresorit capability investigation | NOT STARTED | TRES-01..04 | S15 delivered; S16/S18/S25 considered first in the selected execution order | Supported API/client assessment, bounded REA as needed, evidence-backed capability matrix/decision and implementation proposal. Investigation only; no cloud mutation or integration implementation. |
| S19 - Selected-folder content indexing | NOT STARTED | CONTENT-01..06; affected SEARCH/CORE/LIFE/DELIVERY | After S12 in the selected execution order; qualify extractor APIs, query/location semantics, provider metadata and resource budgets | Toolbar folder opt-in/current magnifier badges; qualified Craft/text extraction; persisted searchable strings and location arrays, periodic incremental refresh and cloud-cache independence. Verify actual search/open/freshness/recovery and one final independently/installed verified release when implementation is authorized. No criteria verified yet. |
| S20 - Integrated PowerToys file tools | NOT STARTED | TOOLS-01..09; affected CORE/LIFE/DELIVERY and existing rename/preview/clipboard actions | After S19, before S21; qualify pinned donor boundaries/dependencies before acceptance | Integrated rename, file-holder discovery, image resizing, preview OCR, structured .reg preview, Paste as file and lower-priority templates; real workflow/data/Undo/lifecycle/resource checks and one final independently/installed verified release when implementation is authorized. No criteria verified yet; no tracked deferrals. |
| S21 - Browsing productivity and smart destinations | NOT STARTED | BROWSE-01..12; affected SEARCH/CORE/LIFE/DELIVERY and existing browsing/operation owners | After S20 and before S22; S19 content-query capability; qualify metadata storage, terminal/protocol/donor boundaries | Address suggestions/history/pins/tab switching and saved searches; configurable actions/profiles; recursive compare/reviewed sync; tags/ratings/comments; embedded terminal; direct SSH/SFTP/WebDAV; local filtering and link tools. Verify all real workflows, preservation/privacy/resources and one final independently/installed verified release after implementation authorization. None verified; no tracked deferrals. |
| S22 - Focused file workflow enhancements | NOT STARTED | OPUS-01..10; affected CORE/LIFE/DELIVERY and existing tabs/panes/operations/previews/settings | After S21 and before S6; qualify supported metadata writes and any concrete REA boundary before acceptance | Named workspaces, paired navigation, ordinary expandable/flat browsing, persistent collections, operation history and listing export; lower-priority aliases/image marking/batch metadata. Verify all actual workflows, preservation, restart/privacy/resources and one final independently/installed verified release after implementation authorization. None verified; no tracked deferrals. |
| S6 - Native StartAllBack search | BLOCKED | SAB-01..06; affected SEARCH/CORE/LIFE/DELIVERY criteria | Parked until preceding Pathweft-first work through S22 is closed and execution is resumed; retain unresolved native feasibility boundaries | Investigation only; no complete SAB criterion. Qualify native groups/provider/cancellation/handoff/lifecycle/direct routing and independently verified adapter release in S6; no automatic Sandbox restart. |
| S7 - Everything filesystem search and Outlook indexing scope | NOT STARTED | EVERYWHERE-01..05; affected SEARCH/CORE/LIFE/SHELL criteria | S6 COMPLETE; consumer/semantics/scope inventory before migration | Final dependent Windows-search work: real Everything file results with selected roots excluded from indexing, WSearch/Outlook retained, explicit exceptions, mail/reboot/recovery and verified migration/package delivery in S7. |

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

Post-release additions are consolidated into the execution-ordered stage map
above: S9/S11 were requested on2026-10-31 and S12/S13 on2026-11-02. Their
requirements remain separate from completed S8. Each future implementation stage
owns its complete release/delivery gate; S12 delivers research documents only.

| Requirements | Owner | Later boundary |
| --- | --- | --- |
| MAINT-08..11 | S39 | Existing-action guidance/category adoption, exact Restore and native guest configuration qualification, actual themed UI and final independently verified normal release |
| MAINT-12..13 | S40 | Direct explicit Maintenance actions, Startup row double-click and selectable selected-row guidance; exact Restore and real owned-fixture UI workflow; S41 owns combined independently verified release |
| CORE-23 | S23 | Complete missing-folder tab activation/restoration recovery, preservation regressions and normal final independently verified release |
| ENV-01..04 | Setup | Committed/remote document and head readback at setup delivery |
| WINDOW-01..03, UPDATE-02, TRANSFER-14 | S14 | Shared auxiliary windows, immediate manual check/results and successful-transfer dismissal; combined S15 independently retrieved/installed release |
| SPACE-01..05, CORE-19 | S15 | Current-pane disk analysis, stable displayed sizes during refresh and one shared S11/S14/S15 final independent/installed release |
| CORE-11, TRANSFER-15 | S15 | Shared capacity-health colors, content-ready conflict table, clean Cancel dismissal after exit, native clipboard preservation/repeat Paste and visible actual file errors |
| TRANSFER-16 | S17 | Metadata-difference review, newer-destination ordering/warnings, matching-file reveal, full-set choices, distinguishing values and verified next release |
| PREVIEW-38 | S17 | Selection-following PiP/Windowed, preserved host state/teardown, small accessible glyphs and combined next release |
| CORE-22 | S17 | Approved yellow folder/darker compass icon, transparent multiresolution assets, embedded WPF/native use and unchanged Windows identity |
| CORE-25 | S35 | Map only blue toolbar details to the app accent; Hidden slash state, compact native/fallback artwork, actual accent/theme/scale/action verification; source-only publication hold |
| CORE-24 | S16 next-release companion | Approximately10-15% larger visible taskbar artwork; preserved design, bounds and identity; actual packaged/display-scale visual verification |
| CORE-25 | S18 next-release companion | Twelve distinct ImageGen monthly changelog banners; consistent Pathweft style/960:170 presentation; selected-release month and year rollover coverage; inspected assets and actual packaged rendering |
| CORE-26..27 | S18 user refinements | Remove duplicate capacity strip; centered edge-to-edge read-only archive ribbon; real foreground loading within address field with quiet background refresh, preserved drafts/caret/focus, reduced-motion and delivered/theme/scale qualification |
| CRAFT-01..07 | S16 | Refreshed Office and additional source-built libraries, reusable located text extraction, preserved worker/preview lifetimes, actual format/fidelity/resource qualification and one final independently/installed verified release |
| MONITOR-01..05 | S18 | Performance icon, contextual cards/details, bounded shared rolling-hour tray CPU/GPU/drive-unit I/O history, focus-only folder tracing, qualified attribution/resources/gaps/lifecycle and verified final delivery after implementation authorization |
| MONITOR-06..08 | S27 | Full-width actual observed histories, separate physical RAM, five statistics-height cards and automatic visible current-folder I/O; supersedes S18 focus-only tracing; real source/render/native/lifecycle and normal independent delivery acceptance |
| MONITOR-09..13 | S28 | Main-pane analysis with preserved browser state; optional bounded recording and shared historical queries; packaged explicit read-only MCP with no client activation; actual UI/protocol/native/resource and final delivery acceptance |
| MONITOR-14..16 | S29 | Interactive event/spike timeline, moment snapshots and selected ranges linked to factual tables; compact widgets preserved; actual interaction/layout/query/lifecycle and final delivery acceptance |
| MONITOR-17..21 | S31 | Automatic bounded collection/saving and recent aggregate/process continuity, CPU columns/pins, local donor comparison/adoption and background audit; integrated regression/layout/resource/delivery acceptance |
| ADOPT-01..06 | S32 | Five scoped bottom Maintenance summary tabs, native actions/exact restore, local performance adoption, donor provenance, integrated workflows/layout/lifecycle checks; source held for user validation |
| ACCESS-01..03 | S30 | Compact UAC shield beside Hidden, standard/admin access mode, authenticated reusable elevated helper and real consent/scope/lifecycle/resource/delivery acceptance |
| UIQA-01..03, MOTION-01..04 | S25 | Rendered design and performance review with fixes; soft view transitions; upward summary-card entry and file-pane squeeze; Statistics/Performance rotation; reversible non-blocking state/focus preservation, reduced-motion settings and actual visual/performance/delivery verification |
| TRES-01..04 | S12 | Evidence-backed Tresorit investigation and implementation proposal; execution remains NOT STARTED |
| CONTENT-01..06 | S19 | Selected-folder content extraction, compact searchable strings/location arrays, periodic freshness/current badges, original-file navigation and verified final delivery after implementation authorization |
| TOOLS-01..09 | S20 | Seven source-borrowed PowerToys capabilities in native Pathweft workflows; New+ lower priority but required, actual Windows/file/clipboard/lifecycle/resource qualification and verified final delivery after implementation authorization |
| BROWSE-01..12 | S21 | Accepted Linux-inspired browsing features and unified recent/pinned/tab/saved-query/path suggestions; actual UI/state/filesystem/terminal/protocol preservation and resource qualification, then verified final delivery after implementation authorization |
| OPUS-01..10 | S22 | Focused Opus-inspired workspaces/navigation/browsing/collections/history/export and lower-priority aliases/image marking/batch metadata; direct implementation or bounded evidence-qualified REA, actual complete workflows and verified final delivery after implementation authorization |
| PREVIEW-21..36 | S13 | All fourteen remaining specialist categories, exact format coverage, integrated qualification and final release/installed verification in S13 |
| PREVIEW-19 | S8 | Canonical drive/share identity for static HTML, retained isolation and final1.1.16 delivery |
| PREVIEW-20 | S8 | Patched Marked and FriBidi, preserved native media contract, source/notice closure and final1.1.17 delivery |
| CORE-01 | S1 | Installer/update isolation S8 |
| CORE-02 | S1, affected actions S2/S4/S5/S6/S7 | Integrated package S8 |
| CORE-03 | Each changed interface, including S9/S11/S13 | S8 verified core interfaces; each future stage verifies its own changed interfaces |
| CORE-04..05 | S8 | Native theme/accent, upstream adoption and delivered regression verification |
| SEARCH-01..04 | S1 | Widget S2, native adapters S6/S7, packaged engine S8 |
| WIDGET-01..03 | S2 | Preview S3, archive S4, transfer S5, package S8 |
| PREVIEW-01..05 | S3; archive paths S4 | Packaged dependencies S8 |
| PREVIEW-06/07 | S8 | Archive tree, full embedded F3D interaction/reader support and final delivery |
| PREVIEW-12..14 | S8 | Continuous raw frames, native-equivalent orbit and no per-input redraws; final1.1.10 delivery |
| CORE-09 | S8 | This PC capacity/network/connection details and final delivery |
| CORE-10 | S8 | Dynamic populated detail columns and final delivery |
| CORE-06..08 | S8 | Editor fallback, transparent folder icon and sidebar edge fade delivered in1.1.1 (95f4e81, c57d135); no dedicated verification record. CORE-07 artwork superseded by CORE-22 (S17) |
| CORE-16/17 | S8 | General Windows Open/row errors, desktop-context startup, non-system bulk storage and failed drag disposal; final1.1.11 delivery |
| CORE-11, PREVIEW-08..11 | S8 | Capacity/provider refinements, icon/themed previews, executable details, shared folder trees, empty-selection collapse and final1.1.5 delivery |
| CORE-12/13 | S8 | Automatic useful statistics and mapped Google cloud-account quota; source/package/publication/retrieval and actual installed1.1.7 verification pass |
| ARCHIVE-01..05 | S4 | Transfer revalidation S5; final package S8 |
| RAR-01..05 | S4R | Transfer revalidation S5; final package/source/license/install S8 |
| FILEUI-01..03 | S4U | Delivered icon/visibility/settings package revalidation S8 |
| TRANSFER-01..07 | S5 | Final package S8 |
| SAB-01..06 | S6 | Delivered adapter/recovery in S6; reduced filesystem-index scope behavior revalidated in S7 |
| EVERYWHERE-01..05 | S7 | Delivered migration/compatibility/recovery in S7, after S6 |
| LIFE-01 | Each implementing stage | S8 core reconciliation complete; future stages own affected lifecycle acceptance |
| SHELL-01 | S7 for search migration, S8 for file-manager shell integration | Actual disable/uninstall readback |
| DELIVERY-01..02 | S8 core release; each future implementing stage | Normal release, independent retrieval/qualification and applicable installed verification for the completed stage outcome |
| DELIVERY-03 | Each import/implementation stage | Public source/asset/license review for its actual delivered dependencies |

Later packaging verification checks a different boundary; it does not defer
required current-stage wiring or real behavior.

## S13 execution and reconciliation contract

Status: **COMPLETE** (see the stage map). The following paragraph is the original pre-start contract. No acceptance criterion has been implemented or verified
for S13. All PREVIEW-21..36 remain; there is no ACTIVE stage or tracked deferral.
The next action on future implementation authorization is source/dependency and
format qualification, followed by the first complete specialist preview workflow.

1. Use the specification's fourteen-category matrix as the finite required scope:
   Office, CSV tables, databases, mail, certificates, application packages, design
   thumbnails, MediaInfo, hex/binary, ELF/Mach-O/UImage, crash dumps, Prefetch, CHM
   and CLSID. Record exact pinned donor files, native/managed dependencies,
   supported extensions/features, runtime requirements, licenses and acceptance
   fixtures. Keep existing ten-module limits explicit; do not relabel raw text,
   archive listings or icons as a specialist viewer. PluginInstaller and separate
   QuickLook add-ons are excluded.
2. Integrate one complete real selection -> table rendering -> navigation ->
   close workflow first (CSV), using Preview/PreviewModules.cs,
   Preview/PreviewSession.cs, Preview/IViewer.cs, BuiltinViewer where appropriate
   and the existing Settings owner. Then finish the other required viewers within
   this same stage, each with complete input/dispatch/render/cleanup wiring before
   its acceptance checks. CLSID requires the real virtual-input boundary; it is
   not a file-extension alias. Do not invent a dynamic plugin host or a second
   application-action/lifecycle owner. No per-module stages or product releases.
3. Qualify Office/native-handler and other unavoidable process boundaries before
   relying on them; unavailable required support remains a concrete blocker.
   Reuse the isolated HTML resource/security boundary for mail/CHM presentation,
   and retain the accepted MPV/F3D paths. Inspect parsers read-only, bound large
   inputs and preserve originals/sidecars. Do not execute inspected code/macros
   or activate inspected CLSIDs, install certificates, persist secrets or initiate
   unsolicited network access. Qualified renderer/preview-handler activation is
   separate from executing inspected content.
4. Extend the existing hidden app-owned preview/package diagnostics with actual
   rendered or structured-value fixtures for every category, from eligible native
   and archive paths on the side pane, Space and widget. Cover priority, settings
   restart/active-disable, rapid selection, malformed/large/locked/encrypted and
   missing-runtime inputs, readiness/cancellation, browser/native teardown and
   post-close file release. Inspect accessible themed layouts at narrow/normal
   sizes and100%/150%. Mocks/parser tests alone do not qualify the integrated
   feature; revalidate affected current HTML/media/model workflows.
5. Reconcile every PREVIEW-21..36 criterion and source/runtime/format matrix. S13
   stays ACTIVE while required implementation is actionable; record exact
   external impediments before justified parking. Do not defer a missing category
   to S9 or call S13 complete with fewer than fourteen. After all integrated
   criteria pass, commit and deliver one normal release through the established
   build/package channel, independently retrieve and verify assets/behavior and
   verify the actual installed update. Clean spent task output transactionally.
   Only then close S13 and proceed in the recorded priority order.

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

## Historical reconciliation ledger

The rows below preserve earlier acceptance evidence. Current statuses and the
remaining execution order are in the opening checkpoint and stage map; the
historical ACTIVE/S8-first wording below does not resume work or supersede them.

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
| S8 | PREVIEW-13/14 camera/burst/zero implicit draws, gestures, previews, frame ownership, actual package/retrieved workflows, normal1.1.10 release, installed hashes/main identity, user confirmation and exact cleanup pass. | None within S8 | COMPLETE; no blocker. Current external Win+E view preserved without fresh readiness claim. S6/S7/S9/S11 runtime work remains outside this release. | None |

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
