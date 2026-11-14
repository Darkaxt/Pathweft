# Run PowerShell scripts from the context menu (MENU-04)

Authority: specification 1.22, MENU-04. Owner request 2026-11-14: .ps1 files could not be launched
from Pathweft; this PC associates .ps1 with an editor and has no Run with PowerShell verb registered
(HKCR\Microsoft.PowerShellScript.1\Shell is absent), so the Windows menu offers nothing either.

Implementation: ContextMenus.cs (Run with PowerShell at the root, Run as administrator under Tools for
one existing .ps1), FileOps.cs (ScriptStart: PowerShell 7 from its install folder or PATH, otherwise
Windows PowerShell with a process RemoteSigned policy; -NoProfile -File, the script's folder, runas for
administrator; RunScript ignores a declined UAC prompt).

Dev stage run-powershell-dev-1.1.65 (ticket 74937cef15e648a59c5f27d81be1a023): gated windows-debug build
exit 0; specialist scenario menus-ui on that build, 143 checks, no failures. Both MENU-04 checks passed:
a .ps1 offers Run with PowerShell at the root and Run as administrator under Tools while other files do
not, and Run with PowerShell starts C:\Program Files\PowerShell\7\pwsh.exe -NoProfile -File "<script>"
in the script's folder for a path with spaces and non-ASCII characters (administrator uses runas with the
same command). The launch is captured, not executed.

Release stage s50-release-1.1.65 (ticket 8dd571827e24f4b51bd2ec5cfd6620ca, source 6f6cc3f): gated
windows-debug, windows-release-publish and packaged-checks exit 0; packaged and corresponding sources
verified. The first package-check run stopped when the user's Everything instance, which had not
answered IPC queries for hours, was restarted mid-query ("Everything exited"). The rerun on the same
delivery passed 2486 criteria with no failures. Scenarios on the gated Debug build: maintenance 312
passed, performance-analysis 130 passed, shortcuts 18 passed, menus-ui 143 checks without failures.
OPEN1111 first stopped at 125 checks: the storage-worker check read the exit code of a process opened by
id without holding its handle (a test race, fixed in 1.1.66); the rerun passed 144 with no failures.
Published v1.1.65 (main = tag = 6f6cc3f); downloaded assets match SHA256SUMS.txt and dist/1.1.65 byte
for byte.
