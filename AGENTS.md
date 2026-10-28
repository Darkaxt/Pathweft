# Pathweft development

Read `docs/SPECIFICATION.md` first. It is authoritative; the plan and this file
do not change its requirements or the user's standing instructions.

- Current authorization is environment/public-fork/specification setup. Product
  implementation stages have not started; a plan does not authorize itself.
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
  script must be adapted in S5 before it can deliver Pathweft.
- Keep private traces, credentials and machine notes out of commits. `.local/`
  is ignored for machine-specific evidence.
- Preserve donor provenance/notices. Do not add dynamic plugin infrastructure
  without a current requirement. A build is not runtime/delivery verification.
- No intermediate product releases; final delivery belongs to S5.
