# S6 approach check: a Pathweft-owned Start menu next to StartAllBack

Question: should S6 keep patching StartAllBack's own search (approach A), or should
Pathweft own the Start menu while StartAllBack keeps the taskbar, orb, tray and
styling (approach B)? Checked in a disposable Windows Sandbox guest on 2026-11-26.
Nothing on the host was changed.

## Guest

- Coordinator Windows Sandbox gate, 4096 MiB, networking off. Ticket `53fd5f69`.
  An earlier guest (`bee33bea`) was stopped: the installer was started from the System
  context and stayed open; nothing from it is used.
- StartAllBack 3.9.28 copied from the host install folder, without licensing or
  registry state. Configuration tool signature valid. Main DLL SHA-256
  `8c26c72247204d98c2ae3a092bfbe964430d387ea56663f3760ef118f214e1b7`, the same as the host.
  Installed with `/install /silent /allusers` from the guest user session (exit 0).
- The owner's StartIsBack presentation values were applied to the guest user (Windows 7
  menu layout, translucency, right-column items, `AutoUpdates=0`). Explorer was restarted
  and loaded StartAllBack: orb `Button "Start"`, `SIBTranslucentLayer`.

## Probe

`StartProbe.cs` is a separate unelevated user process, compiled inside the guest only.
It uses the same technique as Pathweft's Win+E agent (`Agent.cs`):

- a `WH_KEYBOARD_LL` hook. A lone Win tap is masked with the unassigned VK 0xE8, so the
  shell sees a key combination and opens nothing. Ctrl+Esc is swallowed.
- a `WH_MOUSE_LL` hook that swallows a left press and release on the orb's rectangle.
- for each one, a topmost window is shown and takes the foreground. It hides when it
  loses the foreground.

It logs the foreground window and whether StartAllBack's menu (`DV2ControlHost
"Start menu"`) is visible. Input came from WinApp CLI `send-input`, so it was injected
(`LLKHF_INJECTED`) rather than physical.

## Results

| Case | StartAllBack menu | Probe window | Foreground |
|---|---|---|---|
| Baseline, probe watching only: lone Win | opened (`DV2ControlHost` foreground) | none | n/a |
| Lone Win, taskbar in front | stayed closed | shown | yes, direct |
| Lone Win, Run dialog in front | stayed closed | shown | yes, direct |
| Win+R | stayed closed | hidden on deactivation | Run opened normally |
| Ctrl+Esc, desktop in front (first build) | stayed closed | shown | **no**: `SetForegroundWindow` refused |
| Ctrl+Esc, desktop in front (0xE8 tap retried from the UI thread) | stayed closed | shown | yes, after the tap |
| Orb press, probe already in front | stayed closed | shown | yes, direct |
| Orb press, Run dialog in front | stayed closed | shown | yes, direct |
| Lone Win after the probe exited | opened again | none | n/a |

The logs are in `.local/research/startallback/s6-startmenu-probe-20261126/`.

## Findings

1. **Approach B works without touching Explorer.** One ordinary user process can take
   all three ways of opening Start (lone Win, Ctrl+Esc, the orb) away from StartAllBack.
   StartAllBack keeps the taskbar, orb and styling. Win combinations keep working. When
   the process exits, StartAllBack's menu comes back by itself. No DLL injection, no
   Explorer hook and no StartAllBack patch are needed.
2. The foreground handoff needs Pathweft's existing unassigned-key tap. A swallowed
   Ctrl+Esc doesn't give the process the foreground until it taps 0xE8 itself and
   retries.
3. StartAllBack's configuration has **"Use enhanced classic start menu"** (Start Menu
   tab). Clearing it hands Start back to Windows while StartAllBack keeps the taskbar.
   This is a fallback if the hooks ever lose to the shell; it was not needed here.
   The UI has no Win-key option.
4. Approach A is unchanged since `2026-10-30-s6.md`: StartAllBack's results view rejected
   insertion (`IShellFolderView::AddObject` E_NOINTERFACE), and every StartAllBack update
   moves the private offsets. B depends on no private StartAllBack code.

## Not covered

- Physical keyboard and mouse. All input here was injected. On the host, physical input
  is the case that matters, and it is checked when the feature runs on the owner's
  machine.
- Multiple monitors, DPI, and an Explorer restart while the hooks are active.
- The menu itself (layout, search over the Pathweft index, jump lists). The probe only
  proves that Pathweft can own the opening of Start.
