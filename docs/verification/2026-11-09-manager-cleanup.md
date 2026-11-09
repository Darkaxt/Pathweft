# UniGetUI manager cleanup review (2026-11-09)

Final corrective result (2026-11-10): v1.1.47 at c275de26 adds reviewed
native cache actions for Chocolatey, Pip, Npm and .NET Tool. The installed
configured Scoop Python is found even when the app's process PATH is stale.
Pip, Npm and NuGet previews now report reviewed file counts and byte estimates;
the seven managers without a qualified generic action no longer appear as
Cleanup rows. The owned Maintenance UI trial passed 220 checks, and the
delivered-package probe passed 1,193 checks. The normal release was published,
all six assets were independently downloaded and hash checked, and the app's
own updater installed 1.1.47. All 11,309 installed members matched the
downloaded release manifest; registration, original profile path, restarted
processes and installed --selftest were verified. No host manager cache was
deleted to test this correction. The records below preserve earlier checkpoints.

Corrective checkpoint: normal v1.1.46 was independently verified, published and
installed, but its actual Cleanup view displayed seven non-actionable manager
rows, a NuGet `HTTP cache present` flag without count/size, and false Pip
unavailability from the app's stale process PATH. The user explicitly rejected
these placeholders. Specification v1.09 now requires only qualified installed
actions in the table. The corrective source retains four native actions, adds
file/byte estimates for the configured Pip, Npm and NuGet caches, resolves
Scoop Python through its configured current app path, and moves reasons for
unsupported managers to this review. v1.1.47 source, UI, package and installed
acceptance are pending; no host cache was removed for this correction.

Installed UniGetUI 2026.3.3 reports twelve enabled, ready managers through its
read-only `uniget.exe manager list` API: WinGet, Scoop, Chocolatey, Npm, Pip,
Cargo, vcpkg, .NET Tool, PowerShell 5.x, PowerShell 7.x, Installer packages and
Bun. Its installed `manager maintenance` response offers `reload` for every
manager and `repair-winget` for WinGet; none offers `clear-cache`. The [newer
UniGetUI CLI documentation](https://github.com/Devolutions/UniGetUI/blob/main/docs/CLI.md)
advertises cache actions for Chocolatey, Pip, Npm, Bun and .NET Tool, but that
source claim is not installed 2026.3.3 behavior.

Read-only local commands identify Chocolatey v2.7.5 with 125 expired user HTTP
cache entries, Pip's 2,712 HTTP files and 12 wheels under its configured cache,
Npm's configured local cache, and NuGet's separate HTTP/global-package roots.
WinGet 1.29.380 reports `D:/Downloads` as its installer-download directory;
those user downloads are preserved. Bun's cache command requires a project in
the current installed CLI. Cargo's stable `clean` has no manual global-cache
prune route. vcpkg's shared downloads/binary cache and PowerShell module
versions require project/dependency and live-consumer context; Installer's
managed cache supports repair/uninstall.

Pathweft source adds four category-level reviewed native actions. Chocolatey
uses `cache list` and `cache remove --expired` only from a non-elevated token:
its installed help says an elevated command also touches the system cache.
[Chocolatey documents its cache
command](https://docs.chocolatey.org/en-us/choco/commands/). Pip uses its
[cache dir/info/purge](https://pip.pypa.io/en/stable/cli/pip_cache/) commands.
Npm uses [cache ls/clean](https://docs.npmjs.com/cli/cache/) and .NET Tool uses
[NuGet HTTP-cache list/clear](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-locals).
The other seven manager rows report the exact unsupported boundary. Bun's
[linked global store warning](https://bun.sh/docs/pm/cli/install), Cargo's
[automatic global-cache policy](https://doc.rust-lang.org/nightly/cargo/reference/config.html),
and [WinGet's configured download behavior](https://learn.microsoft.com/en-us/windows/package-manager/winget/settings)
are relevant to those boundaries.

The Pip, Npm and NuGet actions require their configured cache root to be a
private user folder outside Documents and Downloads, with no linked ancestor.
Other roots are reported unavailable because their shared consumers cannot be
inferred from the location alone. Chocolatey's action is restricted to its
non-elevated user cache.

An attempted read-only Npm diagnostic, `npm cache verify --dry-run`, was accepted
by npm but **did perform garbage collection**. It reported 216 unneeded objects
(509,431,485 bytes) removed and a verified remaining content cache. The user was
notified immediately. No other live manager cache deletion command was run.
The DEBUG owned contract fixture covers reviewed Pip cache counts, native-action
recheck/readback, changed-root rejection, Downloads exclusion and unsupported
WinGet behavior. Its source is present but has not run. No Pathweft build,
installed UI or release acceptance has run for MAINT-19. The managed .NET gate
still rejects the previously issued S41 ticket before dispatch.
Current delivered-package checkpoint: managed workflow
`8f1ac7325cdebdc2176f318ca6455ab4` compiled source-stamped Debug, Release
Publish and the independent assembly probe with native exit 0 and verified
tree exit. 1.1.46 was assembled from commit `3df1988`. Its extracted-package
check passed x64 and x86 native shell routing, then stopped at the independent
Maintenance probe's obsolete six-row Cleanup assumption. This version has six
original reviewed categories plus eleven manager rows. The assertion now
requires both sets rather than excluding manager entries. Fresh build/package
and complete independent acceptance remain; no release was published.

Previous acceptance checkpoint: managed workflow
`622528e0838a88bce0c7ab1d12c6c677` completed fresh Debug and Release
Publish with native exit 0 and verified process-tree exit. The complete owned
hidden Maintenance trial exited 0 with all 216 checks passing, including the
ordinary ReadOnly Scoop file, all manager previews/rejections, cached Startup
navigation, header sorting, fresh Startup Refresh/action and native cleanup
boundaries. No host Scoop package was deleted by this trial. Final package,
independent download and installed delivery remain.

Previous trial checkpoint: integrated workflow
`b21c49d120a34b2a6e9aa2122d685fcb` completed managed Debug and Release
Publish compilation with native exit 0 and verified tree exit. The first hidden
Maintenance trial passed owned Scoop ReadOnly deletion, manager cache contracts,
all eleven manager rows and cached Startup navigation without a second source
enumeration. It stopped at Startup Name-header reversal: the test switched to a
new GridView and clicked its header before layout, when the event route was
not yet established; the old first-click assertion accepted default Name ascending as
success. The fixture now lays out each category and requires the first click
to change direction. Full corrected trial and release acceptance remain.

Previous build checkpoint: installed Coordinator 0.26.1 fixes the environment
binding and admitted integrated ticket `15b17f87457117bfebabbaa3f093ce5b`.
The assistant erroneously stopped its Debug attempt at 18.5 seconds by applying
the user's 20-second Scoop-deletion limit to compilation. The user clarified
that builds may run to completion. The Release attempt failed before launch
because the .NET gate accepts `dotnet build`, not `dotnet publish`. Preserve
the cancelled Debug workflow and failed Release workflow under the original
ticket; no compiled acceptance or release is established. The failed Release
target was corrected to a managed integrated `dotnet build`, but its first
native run failed with 14 missing WebView2 references in the WPF-generated
temporary project. The main Debug assets contain the pinned WebView2 package.
The integrated MSBuild task had invoked Restore and Build in one evaluation;
NuGet's generated imports require a fresh build evaluation. The corrected
validation project invokes sequential Debug and Release-Publish `dotnet build`
children inside the same Coordinator-owned tree, so the failed native workflow
can receive a failure-linked correction on the original ticket. The
cancelled Debug attempt is not a failed build or replay permission.

Previous gate diagnosis: the installed 0.26.0 client passed an empty execution environment against that
ticket's nonempty issued environment. A separately owned 0.26.1 candidate fixes
environment forwarding but is awaiting protected broker installation. The
original ticket also predates S42/S43 source edits and has no failed workflow
identity for its supported correction route. No alternate build was launched.
