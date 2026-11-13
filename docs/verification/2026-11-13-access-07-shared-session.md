# One administrator session for the whole application (ACCESS-07)

Authority: specification 1.19, ACCESS-04 and ACCESS-07 (supersedes MAINT-23's
separate Maintenance session and MAINT-34's unelevated launch of desktop
uninstallers).

Cause of the reported prompt: Maintenance opened its own elevated worker session
(MAINT-23) instead of reusing the one approved at launch, and ended it whenever
Maintenance was left, so elevated tweaks, services, all-users startup items,
protected Cleanup review and restore points asked UAC again on each visit.

Implementation: AdministratorSession keeps a single session; Maintenance uses it
and requests one only when none exists (launch request declined or disabled in
Settings). Leaving Maintenance no longer ends it. Desktop, MSI and Chocolatey
uninstallers run through the worker's new uninstall-app action, which accepts
only app:reg: and app:choco: ids; Store and Scoop removal stay in-process for the
current user. The explicit "Run as administrator" file command is unchanged.

Also in this change: the details well keeps its title and close button fixed and
scrolls only the text; protected rows explain why once.

Host (gated debug build s47b-session-debug-1): Maintenance scenario 271 of 271
passed. The details-well change alone had passed 271 of 271 earlier
(s47b-details-debug-2).

Guest (disposable Windows Sandbox, no network, same build,
--verify-maintenance-apps): 15 checks, none failed, result 0. New checks:
- The worker's uninstall-app runs a desktop program's own uninstaller and
  confirms Removed.
- The worker refuses to uninstall a Store package.

Not verifiable automatically: the absence of a second UAC prompt on a real
desktop. The scenarios never request elevation, so the guest worker runs
unelevated; the prompt-free path is confirmed only by use of the release.
