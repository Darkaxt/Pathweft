# Development environment

## Checkout and tools

Maintain the active checkout on D:. `origin` is `Darkaxt/Pathweft`; `upstream` is
`limburatorul/file-labs`. Select the fork as the GitHub CLI default. Preserve
upstream history/notices and never publish project changes to upstream by mistake.

Tools: Windows, PowerShell 7, Git, GitHub CLI, and SDK 8.0.427 (or compatible patch
selected by `global.json`). No Android/MAUI workload is needed for this WPF project.
Inno Setup belongs to packaging, not this compile. Select/verify the supported
runtime before final release rather than silently upgrading the baseline now.

## Compilation

```powershell
pwsh -NoProfile -File scripts/Build-Development.ps1 -ScratchRoot D:/Temp/pathweft-development-build
```

The launcher builds the existing project without publishing/installing/starting
it. It limits MSBuild to two workers, disables persistent compiler/build servers,
routes artifacts and child TEMP/TMP to explicit scratch, returns the build exit
code and restores process-scoped environment variables. No timeout/retry governs
completion. Optional `-CacheRoot` defaults to `D:\BuildCache\Pathweft`: dependency
packages, HTTP cache and CLI state retained for this project's subsequent builds.
These are not release artifacts. Reassess retention when development ends.

The invocation scopes `DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1` and
`DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1`. A fresh CLI home otherwise can
repair unrelated machine-installed workloads on first use. This WPF build needs
none. These flags do not change global settings or disable package auditing.
See [Microsoft's environment documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables).

Register exact scratch before creation where practical; after the actual build
and consumers exit, use the installed transactional-cleanup
begin/register/review/inspect/ticket/apply workflow. E:\Temp is the default for
ordinary disposable generation; D:\Temp is the explicit build/native/small-file
I/O exception. Clean D: output immediately after verified last use.

## Runtime and packaging boundaries

The executable still uses File Labs identities, settings and upstream updates.
Do not launch it against ordinary user state or install it as Pathweft before S1
isolation. No installed-parity claim follows from a successful compile.

Inherited `build.ps1` writes/removes staging directly and packages upstream
identities. It was inspected, not invoked. Reuse/adapt its packaging path in S5
with proper identity, outputs and cleanup. The new launcher is a bounded
development compile, not an alternate release engine.

Inherited `FileLabs.exe --selftest` is a runtime entry point. It was not executed
during setup. Review its current side effects, storage and identity before use.

## Evidence

Private machine notes/receipts go in ignored `.local/` or explicit private storage.
Public summaries should be sanitized and source-bound. Never commit personal
sample data, credentials, local traces or copied target binaries.

TeraCopy research uses the installed `reverse-engineer-anything` skill through
Coordinator; preserve version pins/legacy bindings. New persistent findings and
validation assets belong on D:, disposable generation on E: except native D:
scratch. Authenticate/deliver Evidence and verify provider exit before cleanup.
No REA analysis ran during setup; it is required in S4.
