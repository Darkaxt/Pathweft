# Pathweft development

Read `docs/SPECIFICATION.md` first. It is authoritative; the plan and this file
do not change its requirements or the user's standing instructions.

- Current authorization includes S4 implementation and verification (2026-10-29
  user request for the next stage after plan review). S1-S4 are COMPLETE;
  S4R is COMPLETE, authorized by the user's 2026-10-29 request to try bitplane/rars
  with the integration choice delegated. S5-S8 remain NOT STARTED. A plan does not
  authorize itself or intermediate product releases.
- Keep one ACTIVE stage during authorized implementation. Reconcile complete
  features and actual integration evidence in the existing implementation plan.
- Reuse selected source components. Do not substitute standalone QuickLook or
  EverythingToolbar installations for internal source integration.
- Transfer design requires the installed `reverse-engineer-anything` (REA) skill
  on an identified TeraCopy artifact. Upstream transfer changes are context,
  never a substitute for the required investigation.
- Isolate settings, IPC, agent, shell, installer and updater identities from
  File Labs before application trials. This is S1 work, not completed setup.
- Use `scripts/Build-Development.ps1` for compilation. The inherited packaging
  script must be adapted in S8 before it can deliver Pathweft.
- Keep private traces, credentials and machine notes out of commits. `.local/`
  is ignored for machine-specific evidence.
- Preserve donor provenance/notices. Do not add dynamic plugin infrastructure
  without a current requirement. A build is not runtime/delivery verification.
- No intermediate product releases; final delivery belongs to S8.
- Prefer source/native SDK integration inside Pathweft. The F3D GLB prototype
  belongs to S3; native 7-Zip-backed browsing and rename/copy/cut/paste belong to S4.
- S6 targets Everything results inside the existing StartAllBack menu, allowing a
  version-bound native hook after REA evidence. S7 owns the user-requested long-term
  all-Everything search migration and WSearch service retirement. Preserve explicit
  consumer/content/mail compatibility gaps; no service shutdown during planning.
- Use Huashu design as a reference for new interfaces, aligned with the File Labs
  theme already in Pathweft. Run test windows hidden where possible; keep native
  physical focus/hotkey/shell claims separate from hidden rendered/action checks.
