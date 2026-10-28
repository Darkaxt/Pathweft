# Pathweft

**Browse. Find. Preview. Move.**

A Windows file-manager project forked from [File Labs](https://github.com/limburatorul/file-labs), bringing selected search, taskbar, preview and transfer capabilities into one maintained product.

## Current state

**S1 development: isolated identity and paged Everything pane search implemented; acceptance remains open.**

Pathweft has independent executable, profile, IPC, startup, shell, installer and update identities. Pane search reuses the existing default Everything instance, with explicit folder/all-index scopes and a separate live-filesystem mode. It never starts another indexer or changes Windows Search. Installer/update execution remains an S5 acceptance boundary; do not treat this development branch as an installable release.

Starting snapshot: `a8d5a5ca81ab4b70d26e293e69b9cc402baa77b6` (upstream development version 1.0.62; latest published release observed at setup: v1.0.61). See the [S1 evidence and remaining criteria](docs/verification/2026-10-28-s1.md). Inherited upstream releases are not Pathweft releases.

## Direction

- Reuse selected EverythingToolbar search clients, controls and taskbar integration.
- Share one deliberately selected Everything index across panes and widget. The engine remains an external binary/API dependency.
- Port selected QuickLook viewer and add-on source into internal modules, controlled by persisted enable/disable settings. Standalone QuickLook is not required.
- Share file actions and one transfer queue across all surfaces.
- Use **REA analysis of TeraCopy** and measured workflows to guide transfer development. Upstream changes are baseline context, not the transfer design authority.
- Preserve responsive browsing and an uncluttered interface.

Taskbar, internal preview modules and REA-driven transfers belong to S2-S4 and remain unstarted.

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

The inherited File Labs source retains its original [MIT notice](LICENSE). Selected EverythingToolbar client code and the official native Everything SDK retain their [MIT notices and pins](vendor/EverythingSDK/NOTICES.txt). QuickLook source has not yet been imported. Preserve applicable GPL source/distribution materials when that source is incorporated in S3; that future combination must not be described as MIT-only.

**No Pathweft application release exists yet.**
