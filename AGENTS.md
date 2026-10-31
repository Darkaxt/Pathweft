# Pathweft development

Read `docs/SPECIFICATION.md` first. It is authoritative; the plan and this file
do not change its requirements or the user's standing instructions.

- S1-S4 are COMPLETE;
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
  No stage is ACTIVE. The user-directed 1.1.5 drive and preview refinements are
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
