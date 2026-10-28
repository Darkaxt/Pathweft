[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [switch]$VirtualOnly, [switch]$InteractionsOnly, [switch]$ClipboardOnly)
$ErrorActionPreference = 'Stop'
if ((@($VirtualOnly,$InteractionsOnly,$ClipboardOnly) | Where-Object { $_ }).Count -gt 1) { throw 'Select only one focused scenario.' }
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s1-[^\\]+$') { throw 'Use the registered S1 scratch directory.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$env:PATHWEFT_PROFILE = Join-Path $scratch 'profile'
$env:PATHWEFT_S1_VIRTUAL_ONLY = if ($VirtualOnly) { '1' } else { '0' }
$env:PATHWEFT_S1_INTERACTIONS_ONLY = if ($InteractionsOnly) { '1' } else { '0' }
$env:PATHWEFT_S1_CLIPBOARD_ONLY = if ($ClipboardOnly) { '1' } else { '0' }
$env:PATHWEFT_S1_COVERAGE_FILE = Join-Path (Split-Path -Parent $PSScriptRoot) '.local/s1-coverage-case.json'
[void][IO.Directory]::CreateDirectory($env:PATHWEFT_PROFILE)
$fixture = Join-Path $scratch 'fixture'
$settings = @(
    'backdrop=3', 'animations=0', 'smoothscroll=0', 'textscale=1',
    'singlepane=0', 'preview=0', 'tray=0', 'wine=0', 'quicklook=0',
    'lastseen=1.0.62', "tabs.Left=$fixture\scope spaced é", "tabs.Right=$fixture\destination"
)
[IO.File]::WriteAllLines((Join-Path $env:PATHWEFT_PROFILE 'settings.txt'), $settings)
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Build the S1 debug application first.' }
# A waited foreground trial, with no detached helper, global environment change,
# shell registration, default-handler assignment or engine restart.
$process = Start-Process -FilePath $executable -ArgumentList @("--verify-s1=`"$fixture`"") -PassThru -Wait -WindowStyle Normal
exit $process.ExitCode
