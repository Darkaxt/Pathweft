# Pathweft

**Browse. Find. Preview. Move.**

A Windows file-manager project forked from [File Labs](https://github.com/limburatorul/file-labs), bringing selected search, taskbar, preview and transfer capabilities into one maintained product.

## Current state

**Environment and specification established; product integration has not started.**

The application source still uses the upstream File Labs name, settings, installer identity and update feed. Do not install or activate it as Pathweft yet. Inherited upstream releases are not Pathweft releases.

Starting snapshot: `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6` (upstream development version 1.0.62; latest published release observed at setup: v1.0.61). Local Release compilation is verified; application runtime and planned integrations are not.

## Direction

- Reuse selected EverythingToolbar search clients, controls and taskbar integration.
- Share one deliberately selected Everything index across panes and widget. The engine remains an external binary/API dependency.
- Port selected QuickLook viewer and add-on source into internal modules, controlled by persisted enable/disable settings. Standalone QuickLook is not required.
- Share file actions and one transfer queue across all surfaces.
- Use **REA analysis of TeraCopy** and measured workflows to guide transfer development. Upstream changes are baseline context, not the transfer design authority.
- Preserve responsive browsing and an uncluttered interface.

These are planned capabilities, not integrations delivered by this setup.

## Documents

- [Authoritative specification](docs/SPECIFICATION.md)
- [Stage plan and reconciliation](docs/IMPLEMENTATION-PLAN.md)
- [Development environment](docs/DEVELOPMENT.md)
- [Setup verification](docs/verification/2026-10-28-setup.md)

## Development build

Windows, PowerShell 7, Git and the .NET SDK selected by `global.json` are required.

```powershell
pwsh -NoProfile -File scripts/Build-Development.ps1 -ScratchRoot D:/Temp/pathweft-development-build
```

Register exact generated scratch before creation where practical; clean it through the host's transactional-cleanup workflow after its last consumer exits. See the development notes for storage and runtime boundaries.

## Upstream and licensing

The inherited File Labs source remains MIT licensed with its original [notice](LICENSE). EverythingToolbar and QuickLook source has not yet been imported. Preserve all imported notices/dependency terms and provide applicable GPL source/distribution materials when QuickLook code is incorporated; that future combination must not be described as MIT-only.

**No Pathweft application release exists yet.**
