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

The executable now uses Pathweft identities, `PATHWEFT_PROFILE`, its own normal
AppData directory and fork update URLs. Runtime trials use an isolated profile;
they never change the user's default file manager, startup or Windows Search.
Installer/update/uninstall execution is still an S5 boundary.

Inherited `build.ps1` still writes/removes staging directly. Its identity strings
and installer AppId are isolated, but packaging has not been executed/accepted.
Adapt its lifecycle, outputs and cleanup in S5. The new launcher is a bounded
development compile, not an alternate release engine.

`Pathweft.exe --selftest` is the inherited source/filesystem regression entry
point. `scripts/Invoke-SourceSelfTest.ps1` binds its profile/temp to the registered
S1 scratch root, waits for process exit and returns its result. Fixtures are
retained for reviewed cleanup, including its exact generated junction leaf.

## S1 local scenario

Use `Prepare-S1Fixtures.ps1`, a Debug build and `Invoke-S1Verification.ps1`, all
with the same registered `D:\Temp\pathweft-s1-...` root. The finite diagnostic
entry point is compiled out of Release; it is not a remote control service.
It invokes the real WPF actions, queries the real existing index, records
completion predicates and captures rendered trees/PNGs. Physical mouse testing
requires an unlocked input desktop and restores the pointer/button state.
Run the complementary `-VirtualOnly` scenario to verify This PC/archive origins;
use `-InteractionsOnly` for real OLE/clipboard checks or `-ClipboardOnly` for the
complete clipboard copy/paste/restoration workflow, including an independent STA
reader. These focused reports are labelled and do not replace other S1 evidence.

The coverage input `.local/s1-coverage-case.json` must identify a readable,
bounded, unindexed non-NTFS leaf folder with its `path`, existing `sample` filename
and actual file `count` inside `nonNtfs`. It is private, read-only input; do not
publish real paths or filenames. Missing coverage input fails acceptance. The
scenario also uses the local D$ UNC view of its own fixtures; an inaccessible
share is a real environment limitation, not permission to bypass networking.

For side-by-side testing, `Build-S1Baseline.ps1` exports the exact original
baseline into disposable scratch and reuses the build launcher. Then
`Invoke-S1SideBySide.ps1` runs original/fork profiles, verifies coexistence and
preserves the original sentinel. It closes its own baseline process afterward.
Never substitute an installed user's live File Labs profile.

The official SDK API 2 supports the existing default instance in this build.
Search again to reconnect after readiness changes; choose Filesystem · Folder
explicitly for live coverage. There is no hidden engine launch/fallback crawl.
See [Everything's native search syntax](https://www.voidtools.com/support/everything/searching/).

## Evidence

Private machine notes/receipts go in ignored `.local/` or explicit private storage.
Public summaries should be sanitized and source-bound. Never commit personal
sample data, credentials, local traces or copied target binaries.

TeraCopy research uses the installed `reverse-engineer-anything` skill through
Coordinator; preserve version pins/legacy bindings. New persistent findings and
validation assets belong on D:, disposable generation on E: except native D:
scratch. Authenticate/deliver Evidence and verify provider exit before cleanup.
No REA analysis ran during setup; it is required in S4.
