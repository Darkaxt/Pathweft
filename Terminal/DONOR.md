# BROWSE-08 terminal donors (inspected 2026-11-19)

| Package | Pin | Publisher / source | License | Assets used |
| --- | --- | --- | --- | --- |
| `CI.Microsoft.Terminal.Wpf` | 1.25.260303002 (nupkg SHA-256 `3e639a019607432552f8bb3385b51d065ff8cb19608d4c7e17528c9481a10545`) | Repackaged by the "CI2Nuget Repackage Team" from microsoft/terminal commit `9ae724aa5b080aafbeea2bbf88db630b182cc802` (`src/cascadia/WpfTerminalControl`); nuget.org owner CI2NugetRepackageTeam. Not a Microsoft-published package. | MIT (Microsoft Corporation; the nuspec states no license, the source repository's LICENSE applies) | `lib/net8.0-windows7.0/Microsoft.Terminal.Wpf.dll` (33 KB), `runtimes/win-x64/native/Microsoft.Terminal.Control.dll` (1.68 MB). The package also carries 85 MB `.pdb` files per architecture and x86/arm64 binaries; Pathweft ships x64 only and must exclude the `.pdb` files (FileExplorer.csproj target DropTerminalControlSymbols). |
| `Microsoft.Windows.Console.ConPTY` | 1.24.260710001 (nupkg SHA-256 `175640566a3b59c4b132070ee96c2c77e5ab7edd2e92732a5eb3610bbf63d90e`) | Microsoft (nuget.org), project https://github.com/microsoft/terminal | MIT (license expression in the nuspec) | `runtimes/win-x64/native/conpty.dll` and `build/native/runtimes/x64/OpenConsole.exe` (copied as `x64\OpenConsole.exe` when `ConptyRequiresx64Host=true`). |

Not adopted: `EasyWindowsTerminalControl` 1.0.38 (MitchCapper, MIT) wraps the same two packages with its own
ConPTY/process code. Pathweft owns process lifetime (kill-on-close job), shell integration and the OSC
parser itself, so only the upstream control and ConPTY are taken.

## API used (from microsoft/terminal `src/cascadia/WpfTerminalControl`)

- `Microsoft.Terminal.Wpf.TerminalControl : UserControl` - `Connection { set; }` (calls `Start()` when ready),
  `AutoResize`, `Rows`, `Columns`, `SetTheme(TerminalTheme theme, string fontFamily, short fontSize, Color externalBackground = default)`,
  `GetSelectedText()`, `ResizeAsync`, `TriggerResize`.
- `ITerminalConnection` - `event EventHandler<TerminalOutputEventArgs> TerminalOutput; void Start(); void WriteInput(string data); void Resize(uint rows, uint columns); void Close();`
- `TerminalOutputEventArgs(string data)` / `Data`.
- `TerminalTheme` struct - `DefaultBackground`, `DefaultForeground`, `DefaultSelectionBackground` (COLORREF `uint`), `CursorStyle`, `ColorTable` (`uint[16]`).

## ConPTY host selection

`conpty.dll` looks for `OpenConsole.exe` beside itself, then in `<arch>\OpenConsole.exe` beside itself, and
otherwise falls back to the inbox `conhost.exe`. In Pathweft's self-contained win-x64 publish both land in the
application folder (`conpty.dll`, `x64\OpenConsole.exe`). In a RID-less development build `conpty.dll` sits
under `runtimes\win-x64\native\`, so it uses inbox conhost. If `conpty.dll` cannot load at all,
`ConPtyConnection` uses kernel32's `CreatePseudoConsole`.

Unverified (needs the integrated build): that the inbox conhost on Windows 11 26200 passes OSC 9;9 / 133
through; packaged OpenConsole does. The panel reports `Synchronized=false` until it sees either sequence.
