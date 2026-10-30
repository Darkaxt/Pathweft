# WinApp CLI evaluation

Evaluated 2026-10-30 for the existing S6 StartAllBack integration. This is a
development-tool decision; it changes no product requirement or stage boundary.

## Decision

Adopt Microsoft's portable **WinApp CLI v0.7.1**, with project-scoped
`winapp-sandbox` and `winapp-ui-automation` skills. The executable is a separate
installed development tool; the skills provide instructions for using it.
No WinApp plugin, global PATH entry, certificate, Windows SDK, package identity,
project migration, additional MCP server or application configuration is needed.

The released executable supports WPF and native Win32 UI Automation, Sandbox
execution, target inspection, file transfer and captures. These are relevant to
Pathweft's WPF surfaces and its native Shell integration; it does not require a
WinUI port. Existing build scripts remain authoritative. See the
[pinned Sandbox documentation](https://github.com/microsoft/winappCli/blob/e3cd0b94f3e7b776ac51ba0564f4908094e979b5/docs/sandbox-execution.md)
and [release](https://github.com/microsoft/winappCli/releases/tag/v0.7.1).

## Qualification and observed behavior

- Source commit: `e3cd0b94f3e7b776ac51ba0564f4908094e979b5`.
- Official x64 ZIP SHA-256:
  `D925D1E32CDC320B6D271F653FD2CD3C05B5D7558DEB20D1B548BEAD77900FCF`;
  independently matches the release asset digest.
- Installed executable SHA-256:
  `BF29F665AB4A030F20CD34A9121276301871EE89FD9C13118C7221563858D692`;
  Authenticode valid, Microsoft Corporation. Installed version reports `0.7.1`.
- Actual released `target` and `ui` help confirm the relevant commands. Do not
  infer released capabilities from newer `main` documentation.
- A host `target snapshot sandbox --json` returned `running:false` and
  `sandbox_target_stale` for the externally started, unadopted guest. Native
  `wsb list --raw` independently confirmed that same guest remained running.
  Snapshot intentionally does not adopt, start, reconnect or repair a guest.
- The current S6 guest has networking disabled. Its native IP query reports
  network-not-found. The released backend calls `GetIpAddressAsync` and connects
  `GuestTcpTransport`; the host WinApp transport is therefore unsuitable for
  this offline guest. It was not bootstrapped, restarted or given networking to
  force this test through.
- Using native `wsb exec` for the exact owned guest, the same signed CLI ran
  from a read-only shared tool directory. Its `ui list-windows --on local
  --json` listed guest processes/windows. An exact taskbar HWND inspection
  returned the guest Start/Search controls, selectors and 144-DPI geometry.
  The process's guest hostname/account were recorded before querying. No
  input-injecting operation was used, and explicit `ui yield` exited normally.
  This verifies guest-side UIA inspection through the native WSB bridge, **not**
  the host WinApp agent transport or StartAllBack integration.

Required private receipts, original JSON and reviewable command scripts remain
in ignored project evidence storage. Machine paths, VM identities and captures
are intentionally absent from this document.

## Fit and limitations

Use it to discover windows, inspect accessible controls, read state and collect
targeted UI evidence. In a networked owned Sandbox its documented agent also
offers deployment/exec/transfer and capture; those paths are not runtime-qualified
by the offline check above. An adopted Sandbox receives bootstrap shares,
Developer Mode and a narrowly targeted guest firewall rule. Cold connection or
reconnection can briefly affect focus; already connected read-only inspection
does not require host input. Do not promise every testing operation is hidden.

UIA sees what a control exposes. It cannot establish private StartAllBack COM
contracts, replace REA, identify Everything as the real search backend, prove
physical keyboard/mouse delivery or qualify attach/detach/Explorer recovery.
Custom Shell rows may need native diagnostics and rendered evidence. Keep those
acceptance boundaries in SAB-01..06 explicit.

The live Sandbox's Windows core reports 24H2 build 26100.9711; the host reports
25H2 build 26200.9711. The analysed `searchfolder.dll` hashes match, but this alone
does not satisfy SAB-05's matching-runtime requirement. Tool adoption does not
resolve that requirement or complete any SAB criterion.

## Other candidates from the supplied report

| Candidate | Assessment and decision |
| --- | --- |
| Microsoft `winapp-sandbox` | Adopt. Explicit guest targeting, lifecycle/transfer diagnostics and no silent host fallback fit S6. |
| Microsoft `winapp-ui-automation` | Adopt. WPF/Win32 UIA support and structured state queries are useful now; guest-side inspection was exercised. |
| Microsoft `winui-ui-testing` in `win-dev-skills` | The exact skill is absent from the current upstream `main` tree examined at `5ce74fa89c49ca353c6ab03bd5889aa519aa9e5e`. The report's install recommendation is not currently reproducible; Pathweft also uses WPF/native Shell rather than WinUI. Do not install a substitute. |
| [CursorTouch/Windows-MCP](https://github.com/CursorTouch/Windows-MCP) | A broader desktop-control MCP server. Its advertised controls overlap this tool and existing project diagnostics. Leave uninstalled; no distinct current S6 requirement justifies adding another server. This is a fit assessment, not a security audit or runtime comparison. |
| [iFurySt/open-computer-use](https://github.com/iFurySt/open-computer-use) | Cross-platform MCP/CLI computer use. It adds another general desktop runtime/configuration with no demonstrated advantage for this native Windows stage. Leave uninstalled; no runtime evaluation was performed. |

The imported skills' license, exact source and local link repair are documented
in [skill provenance](../.agents/skills/WINAPP-SOURCE.md). Future adoption of a
different version requires the same release/source/installed-runtime checks.
