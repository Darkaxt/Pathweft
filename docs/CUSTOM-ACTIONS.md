# Custom actions

Your own commands in Pathweft's menu. Right-click files or folders and they appear under
**Actions** when they apply to that selection. They run only when you click them.

Edit them in **Settings > Actions** (Add action, Edit, Move up/down, Remove), or by hand in
`actions.json` in Pathweft's settings folder (Settings > About > Open settings folder). Pathweft
reads the file again when it changes. If the file can't be read, Settings > Actions says why and
no actions are shown.

## Fields

| Field | Meaning |
| --- | --- |
| `name` | The menu text. Required. |
| `glyph` | One Segoe Fluent Icons character shown as the icon, or empty. |
| `submenu` | Groups actions under Actions > *submenu*. Empty: directly under Actions. |
| `executable` | The program to start. Required. Environment variables such as `%SystemRoot%` are expanded. A document or script is opened by its associated program. |
| `arguments` | The command line, with the placeholders below. |
| `workingFolder` | The folder the program starts in. Empty: the current folder. Placeholders work here too. |
| `patterns` | File name patterns separated by `;`, for example `*.jpg;*.png`. `*` matches every file. Folders ignore it. |
| `appliesTo` | `files`, `folders` or `both`. |
| `selection` | `one` (exactly one item), `several` (two or more) or `any`. |

## Placeholders

| Placeholder | Becomes |
| --- | --- |
| `%f` | The first selected item's full path. |
| `%F` | Every selected item's full path, one argument each. |
| `%n` | The first selected item's name. |
| `%N` | Every selected item's name, one argument each. |
| `%d` | The current folder. |
| `%%` | A literal `%`. |

Each path or name becomes one argument, quoted by the Windows command-line rules
(`CommandLineToArgvW` and the C runtime read it back exactly), so spaces, quotes, Unicode and
characters such as `&` or `^` arrive unchanged. Don't put quotes around a placeholder yourself.

## Running

Pathweft starts the program and does not wait for it or keep it, the same as Open with. If the
program can't be started, the status bar shows why. An action never runs on its own: nothing
happens until you click it.

## Example

```json
[
  {
    "name": "Compare with WinMerge",
    "glyph": "\uE8F1",
    "submenu": "",
    "executable": "C:\\Program Files\\WinMerge\\WinMergeU.exe",
    "arguments": "%F",
    "workingFolder": "",
    "patterns": "*",
    "appliesTo": "both",
    "selection": "several"
  },
  {
    "name": "Open in VS Code",
    "glyph": "",
    "submenu": "Developer",
    "executable": "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe",
    "arguments": "%f",
    "workingFolder": "%d",
    "patterns": "*",
    "appliesTo": "both",
    "selection": "one"
  }
]
```
