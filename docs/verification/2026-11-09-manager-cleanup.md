# UniGetUI manager cleanup review (2026-11-09)

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
