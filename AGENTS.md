# Pathweft development

Read `docs/SPECIFICATION.md` first. It is authoritative; the plan and this file
do not change its requirements or the user's standing instructions.

Current S8 ACTIVE: SHELL-02/03/spec0.30. The user extends the unpublished update
with JSON-backed dynamic folder verbs and a themed Windows-integration editor.
Do not publish the b3f583c package. Complete route configuration/editor, applied
snapshot recovery, isolated/native/hidden UI checks, then one final1.1.12 release.
Add explicit Folder/explore to the existing
opt-in handler, preserve Explorer/virtual behavior and intact prior opt-in
migration/restoration. Source registration and hidden native32/64-bit COM/browser
checks pass. Host ShellExecute fixtures escaped SW_HIDE and were stopped; do not
run more test-folder launches. Finish shipping-mapping/package checks, one normal
1.1.12 release/retrieval/installed readback and user-controlled Helium click, then
cleanup. No address-field
change, reveal-file hook, Sandbox, desktop takeover or postponed-stage work.
Historical S8 COMPLETE: CORE-16/17/spec0.28, normalv1.1.11 at76694a9.
General Open/Message/editor/archive, real native32/64 desktop activation,
non-system storage, cancelled-drag disposal and Change location pass source and
delivered-package checks. Independent retrieval passes; all792 installed hashes,
reopened mainPID63812/helperPID69224 ownership/readiness and desktop audit policy
pass. The shipping worker moved the real profile to D:, preserving settings and
cache/editor content; only the76-byte Roaming pointer remains. External diagnostic
and desktop Roaming views differed: final relocation/runtime verification used
the actual desktop context and their current pointers are reconciled. Preserve
the active app/profile. Exact abandoned-output and scratch cleanup pass; REA is
detached/released. No S8 blocker/deferral or ACTIVE stage. No Sandbox/desktop
takeover or S6/S7/S9/S11 implementation. Earlier checkpoints below are historical.

- S1-S4 are COMPLETE;
  Current S8 correction is COMPLETE under specification0.26/PREVIEW-13/14.
  Normal1.1.10 ataf82ca2 separates native-equivalent camera math from per-input
  draws. Camera/burst/preview/pressure and actual package/retrieved workflows pass;
  all792 installed hashes and reopened mainPID52824 pass; user reports fixed.
  Scratch cleanup passes. No current helper-owner/startup/stored wine opt-in is
  visible: preserve that state; do not claim fresh Win+E readiness or rewrite it.
  No ACTIVE stage or S8 blocker/deferral; no S9/S11/S6/S7 implementation.
  Current S8 extension is COMPLETE under specification0.24: PREVIEW-12 smooth
  native 3D/quality, CORE-14 vertical Settings, CORE-15 Help section tracking and
  JUMP-03 bounded S11 research. Delivered together as normal1.1.8 at64118e9;
  package/retrieved native and app workflows,792 installed hashes, reopened
  mainPID4748/helperPID37904 ownership/readiness and exact scratch cleanup pass.
  No ACTIVE stage, S8 blocker or tracked deferral. S11 implementation is NOT
  STARTED; no S6/S7/S9 work. Prior1.1.7 closure remains historical below.
  S4R is COMPLETE, authorized by the user's 2026-10-29 request to try bitplane/rars
  with the integration choice delegated. A plan does not
  authorize itself or intermediate product releases. S4U is now COMPLETE, authorized
  by approval of grayscale/lock icons and Ctrl+H with dot-name visibility; see
  FILEUI-01..03. Initial S5 acceptance closed at e478986. The user's continued
  comparison/implementation goal reopened S5 under TRANSFER-07; its finite
  findings inventory and affected acceptance are now verified COMPLETE.
  see `docs/verification/2026-10-29-s5.md` for the direct-write contract,
  measured performance limits and actual caller/preservation/recovery evidence.
  The user explicitly reprioritized S8 core delivery ahead of Windows search work.
  S8 is COMPLETE through v1.1.5: CORE-09..11 drive details/dynamic columns,
  PREVIEW-06..11, About, normal release/retrieval/installation and exact cleanup
  are verified. Prior S8 work is COMPLETE through v1.1.3,
  including the compact Ctrl+Alt+Space popup and clipped Settings action fix.
  The prior frame/search/editor/icon/sidebar work shipped in v1.1.1.
  S8 is COMPLETE through1.1.7. CORE-12/13 source, final package, normal six-asset
  publication, independent native/app retrieval, actual user-updater installation
  and exact cleanup pass at eeb5d40/tagv1.1.7. All792 installed members match;
  reopened mainPID12928/helperPID40692 and helper ownership/ready state pass.
  No S8 blocker, ACTIVE stage or tracked deferral. Do not resume S9/S6/S7 automatically.
  The user installed1.1.6 with
  the updater and confirms Explorer navigation / Win+E. Preserve their running
  app; installed1.1.6 and1.1.7 identity/hashes and agent readiness are verified. Keep the
  current external-tool registry-view discrepancy explicit; do not rewrite live
  registrations from that diagnostic view. The prepared read-only installed
  verifier is in .local/evidence/2026-10-31-cloud117/Verify-Installed.ps1.
  User external-folder-link testing waits for a real use case; keep prior hidden
  native evidence separate. S9 remains NOT STARTED.
  Previous checkpoint: S8 was BLOCKED on the user-controlled app exit required for the final
  installed 1.1.6 verification. The SHELL-01 correction is normally published at
  a7648b9 and independently retrieved/qualified; the running host copy remains
  1.1.5. Scratch cleanup passes. Resume the prepared installation when the user
  exits normally; do not force-close their work. S9 is not started. The prior drive and preview refinements are
  delivered and verified. No S6/S7 implementation is authorized.
  S6 is BLOCKED by this user-directed execution boundary,
  with investigation retained and no working patch; S7 remains NOT STARTED.
  S9 context-menu reorganization is planned, NOT STARTED (MENU-01..02).
  S11 native pinned/recent folder Jump Lists is planned, NOT STARTED (JUMP-01..02).
  Identify TeraCopy and derive adoption from REA
  evidence before transfer implementation.
- Keep one ACTIVE stage during authorized implementation. Reconcile complete
  features and actual integration evidence in the existing implementation plan.
- Reuse selected source components. Do not substitute standalone QuickLook or
  EverythingToolbar installations for internal source integration.
- Transfer design requires the installed `reverse-engineer-anything` (REA) skill
  on an identified TeraCopy artifact. Upstream transfer changes are context,
  never a substitute for the required investigation.
- Isolate settings, IPC, agent, shell, installer and updater identities from
  File Labs before application trials. This is S1 work, not completed setup.
- Use `scripts/Build-Development.ps1` for compilation and the adapted `build.ps1`
  for clean-source Pathweft release packaging; see `docs/RELEASE-USAGE.md`.
- Keep private traces, credentials and machine notes out of commits. `.local/`
  is ignored for machine-specific evidence.
- Preserve donor provenance/notices. Do not add dynamic plugin infrastructure
  without a current requirement. A build is not runtime/delivery verification.
- No intermediate product releases; final delivery belongs to S8.
- S8's authorized core release excludes unfinished S6/S7 integration; do not
  restart Sandbox or resume those stages automatically. The user explicitly
  stopped Sandbox work and directed final S8 release/host installation; use the
  completed real host lifecycle evidence and verify final delivered/installed
  packages. Review useful upstream
  changes before compiling. Implement optional Native appearance/accent settings
  under Appearance and opt-in shell routing under Windows integration.
- Prefer source/native SDK integration inside Pathweft. S8 promotes the S3 F3D
  prototype to the interactive viewer and archive tree under PREVIEW-06/07;
  native 7-Zip-backed browsing and rename/copy/cut/paste belong to S4.
- S6 targets Everything results inside the existing StartAllBack menu, allowing a
  version-bound native hook after REA evidence. Keep native category dividers with
  reduced Everything-backed subsets; See more results opens the full integrated
  view. Qualify Programs/Settings independently of filesystem indexing and route
  StartAllBack folder actions directly to Pathweft under SAB-06. S8 SHELL-01 owns
  Directory Opus-informed default-handler routing replacing the inherited timed
  Explorer-window takeover. S7 owns the user-requested long-term
  Everything filesystem migration and Outlook-preserving WSearch scope reduction.
  The user superseded service retirement; preserve explicit consumer/content/mail
  compatibility gaps and leave live indexing configuration unchanged during S6.
- Use Huashu design as a reference for new interfaces, aligned with the File Labs
  theme already in Pathweft. Run test windows hidden where possible; keep native
  physical focus/hotkey/shell claims separate from hidden rendered/action checks.
- S6 UI diagnostics may use the pinned project-local `winapp-sandbox` and
  `winapp-ui-automation` skills under `.agents/skills/`. Read their provenance and
  project routing in `.agents/skills/WINAPP-SOURCE.md` before use. WinApp is a
  development tool, not a product dependency or substitute for REA/native
  acceptance. Preserve explicit guest targeting and existing execution gates.
