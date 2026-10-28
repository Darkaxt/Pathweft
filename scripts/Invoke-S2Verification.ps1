[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [switch]$VisibleInteractions, [switch]$PresentationOnly, [switch]$ExplorerLifecycle, [switch]$DpiOnly, [switch]$NativeLifecycleOnly)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s2-[^\\]+$') { throw 'Use registered S2 native scratch.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$env:PATHWEFT_PROFILE = Join-Path $scratch 'profile'
$env:PATHWEFT_TEST_HIDDEN = if ($VisibleInteractions) { '0' } else { '1' }
$env:PATHWEFT_S2_PRESENTATION_ONLY = if ($PresentationOnly) { '1' } else { '0' }
$env:PATHWEFT_S2_EXPLORER_LIFECYCLE = if ($ExplorerLifecycle) { '1' } else { '0' }
$env:PATHWEFT_S2_DPI_ONLY = if ($DpiOnly) { '1' } else { '0' }
$env:PATHWEFT_S2_NATIVE_ONLY = if ($NativeLifecycleOnly) { '1' } else { '0' }
[void][IO.Directory]::CreateDirectory($env:PATHWEFT_PROFILE)
$fixture = Join-Path $scratch 'fixture'
$settings = @('backdrop=3','animations=0','smoothscroll=0','textscale=1','singlepane=0','preview=0','tray=0','wine=0','quicklook=0','lastseen=1.0.62','taskbarsearch=0',
    "tabs.Left=$fixture\scope spaced é", "tabs.Right=$fixture\destination")
[IO.File]::WriteAllLines((Join-Path $env:PATHWEFT_PROFILE 'settings.txt'), $settings)
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Build S2 Debug first.' }
$style = if ($VisibleInteractions) { 'Normal' } else { 'Hidden' }
$trial = Start-Process -FilePath $executable -ArgumentList @("--verify-s2=`"$fixture`"") -PassThru -Wait -WindowStyle $style
Write-Output "S2 evidence: $fixture\evidence\$($trial.Id)"
$reportPath = Join-Path $fixture "evidence/$($trial.Id)/s2-results.json"
if (!(Test-Path -LiteralPath $reportPath)) { throw 'The trial exited before writing its acceptance result; process exit alone is not a passing scenario.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.pid -ne $trial.Id) { throw 'The result belongs to a different process.' }
if ($trial.ExitCode -ne $report.result) { throw 'The scenario result differs from the process exit code.' }
exit $report.result
