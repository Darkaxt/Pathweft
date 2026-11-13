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
