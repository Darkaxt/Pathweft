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
S1-S3 trials leave the user's default file manager, startup and Windows Search
unchanged. S6/S7 add explicitly planned shell/search migration boundaries;
planning does not activate them. Installer/update/uninstall belongs to S8.

Inherited `build.ps1` still writes/removes staging directly. Its identity strings
and installer AppId are isolated, but packaging has not been executed/accepted.
Adapt its lifecycle, outputs and cleanup in S8. The new launcher is a bounded
development compile, not an alternate release engine.

`Pathweft.exe --selftest` is the inherited source/filesystem regression entry
point. `scripts/Invoke-SourceSelfTest.ps1` binds its profile/temp to the registered
S1/S2/S3 scratch root, waits for process exit and returns its result. Fixtures are
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

## S2 local scenario

Use `Prepare-S2Fixtures.ps1`, a Debug build and `Invoke-S2Verification.ps1` with
the same registered `D:\Temp\pathweft-s2-...` native/small-file I/O root. Prepare
fixtures before repeating the complete workflow because it includes a real move.
Normal trial windows render offscreen; native monitor measurements stay hidden.
Each process writes a separate evidence directory and the launcher requires its
scenario result to match the original process exit. There is no control server.

`-PresentationOnly` diagnoses hidden placement. `-DpiOnly` exercises actual
mixed-DPI monitors without file mutations. `-NativeLifecycleOnly` intentionally
allows the search popup to receive actual shortcut/typing focus and waits for an
actual Explorer TaskbarCreated message. That boundary requires an unlocked input
desktop and coordinated timing with the user. `Invoke-S2ExplorerRestart.ps1`
inspects by default; `-Restart` validates the waiting trial's process identity and
uses graceful Restart Manager shutdown/restart, preserving a private folder
restoration snapshot. Do not run this disruptive check as a routine build step.

The selected EverythingToolbar sources can be reproduced with
`Get-S2DonorSources.ps1` at the pinned revision. They are inspection input, not a
runtime donor-product dependency. `Test-SearchContracts.ps1` also checks safe
taskbar coordinates and occupied-space constraints. No packaging/release is
performed by these development scripts.

## S3 local scenario

Use one registered `D:\Temp\pathweft-s3-...` root for source/SDK downloads,
fixtures, isolated profiles, native renders and builds. D: is required here for
native libraries, dependency/build trees and small-file I/O. Selected source and
provenance are committed; generated runtime output is disposable after preserving
the required private evidence.

Run `Get-S3DonorSources.ps1`, `Get-S3F3DInputs.ps1` and
`Prepare-F3DDependency.ps1` before `Build-Development.ps1`. The latter prepares a
pinned persistent native dependency in the development cache; it is an ongoing
build consumer, separate from scratch. `Import-S3Provenance.ps1` and
`Import-F3DProvenance.ps1` record the selected donor files and notices. Follow
their explicit input parameters; no installed donor GUI supplies runtime code.

`Prepare-S3Fixtures.py` creates the bounded test corpus. The optional private F3D
input JSON contains `path`, `sha256` and `bytes` for a user-owned GLB; `Prepare-S3F3DFixture.ps1`
records its hash and creates a controlled copy without editing the original.
The MP4 fixture is encoded from the generated Y4M through the installed media
helper (H.264, yuv420p, two threads, no audio). Do not commit private input,
user-owned models or raw machine screenshots.

`Invoke-S3Verification.ps1 -DiagnosticsOnly` runs the complete hidden preview
workflow and a new-process settings readback while leaving QuickLook untouched.
`-F3dOnly` checks the model/camera/native teardown; `-PdfOnly` diagnoses actual
page rendering. These focused diagnostics do not prove standalone absence.
For final PREVIEW-01 acceptance, `Invoke-S3QuickLookAbsence.ps1` temporarily
stops the exact user-authorized QuickLook process, runs the complete scenario,
then restores its original invocation and verifies configuration hashes/readiness.
Never use that stop/restore wrapper as a routine debugging loop.

All trial windows render offscreen and measure foreground events. The harness
uses real controls, actual decoder/render completion and native thread exit;
folder refresh can replace a preview generation, so it drains the actual owned
generation chain. Each process has a distinct report directory, and its report
must match the original process exit. Release excludes these finite Debug entry
points. Run `Invoke-SourceSelfTest.ps1` on the Release build for existing source
behavior, including settings reset and preserved File Labs metadata helpers.

## Evidence

Private machine notes/receipts go in ignored `.local/` or explicit private storage.
Public summaries should be sanitized and source-bound. Never commit personal
sample data, credentials, local traces or copied target binaries.

TeraCopy research uses the installed `reverse-engineer-anything` skill through
Coordinator; preserve version pins/legacy bindings. New persistent findings and
validation assets belong on D:, disposable generation on E: except native D:
scratch. Authenticate/deliver Evidence and verify provider exit before cleanup.
No REA analysis ran during setup; it is required in S5 after the native archive stage.
