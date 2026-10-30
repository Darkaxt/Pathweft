# Microsoft WinApp skills provenance

Adopted 2026-10-30 for Pathweft's existing S6 isolated runtime diagnostics.

- Repository: <https://github.com/microsoft/winappCli>
- Release: `v0.7.1`
- Immutable source: `e3cd0b94f3e7b776ac51ba0564f4908094e979b5`
- Imported paths: `plugins/winapp/skills/winapp-sandbox`,
  `plugins/winapp/skills/winapp-ui-automation`.
- Additional reference: `docs/sandbox-execution.md` at the same commit.
- License: MIT; the upstream notice is retained in `WINAPP-LICENSE`.
- Installation: official Codex skill-installer, explicit commit and project destination.
- Local adaptation: repair the Sandbox skill's four documentation links to its
  bundled `references/sandbox-execution.md`; point that reference's seven sibling
  document links at the same immutable upstream commit. The other imported skill
  files retain their upstream content; no behavioral guidance was rewritten.

These are repository-scoped agent instructions, not Pathweft runtime dependencies
or a global plugin installation. CLI installation/provenance and private guest
results are recorded under ignored `.local/research/startallback/`.
The public evaluation is [WinApp CLI](../../docs/WINAPP-CLI.md).

## Project routing

Standing user instructions, the authoritative specification and existing managed
execution gates take priority over the imported examples. They do not authorize
host elevation, networking changes, host UI input, installing other skills, or
new project stages. Keep the established build and package scripts.

For host-invoked WinApp guest commands, always use `--on sandbox`. Its TCP agent
requires guest networking; do not silently recreate or reconfigure an offline
Sandbox. `target snapshot` is inspection only and cannot adopt an unmanaged guest.
The verified offline alternative is an explicit `wsb exec --id <owned-id>` of the
signed CLI inside that guest, using a read-only tool mapping and bounded mapped
output. Only in that guest process does `--on local` mean the guest desktop.
Never rerun that command directly on the host as a fallback.

Inspect the target before acting, rediscover selectors/PIDs after guest recreation,
use a workflow ID for a UI sequence, and yield explicitly. Prefer read-only UIA
diagnostics over screenshots or physical input when they answer the question.
Native input, screenshot delivery, private Shell ABI behavior and complete S6
acceptance each require their own evidence. A successful UIA query proves none of
those other boundaries. Use process-scoped `WINAPP_TARGET_STATE_ROOT`, scratch
paths and `WINAPP_CLI_TELEMETRY_OPTOUT=1`; preserve user/global settings.
