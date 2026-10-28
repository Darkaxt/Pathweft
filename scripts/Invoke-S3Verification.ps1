[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [switch]$PdfOnly, [switch]$F3dOnly, [switch]$DiagnosticsOnly)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$env:PATHWEFT_PROFILE = Join-Path $scratch 'profile'
$env:PATHWEFT_TEST_HIDDEN = '1'
$env:PATHWEFT_S2_NATIVE_ONLY = '0'
$env:PATHWEFT_S3_PDF_ONLY = if ($PdfOnly) { '1' } else { '0' }
$env:PATHWEFT_S3_F3D_ONLY = if ($F3dOnly) { '1' } else { '0' }
$env:PATHWEFT_S3_DIAGNOSTIC = if ($DiagnosticsOnly) { '1' } else { '0' }
[void][IO.Directory]::CreateDirectory($env:TEMP)
[void][IO.Directory]::CreateDirectory($env:PATHWEFT_PROFILE)
$fixture = Join-Path $scratch 'fixture'
$settings = @('backdrop=3','animations=0','smoothscroll=0','textscale=1','singlepane=0','preview=0','tray=0','wine=0','lastseen=1.0.62','taskbarsearch=0','preview.modules.disabled=f3d',
    "tabs.Left=$fixture\files", "tabs.Right=$fixture")
[IO.File]::WriteAllLines((Join-Path $env:PATHWEFT_PROFILE 'settings.txt'), $settings)
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
$phases = if ($PdfOnly -or $F3dOnly) { @('0') } else { @('0','1') }
foreach ($phase in $phases) {
    $env:PATHWEFT_S3_RESTART = $phase
    $logRoot = Join-Path $fixture ('logs/' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($logRoot)
    $trial = Start-Process -FilePath $executable -ArgumentList @("--verify-s3=`"$fixture`"") -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot 'native-output.txt') -RedirectStandardError (Join-Path $logRoot 'native-error.txt')
    $evidence = Join-Path $fixture "evidence/$($trial.Id)"
    Write-Output "S3 evidence: $evidence"
    Write-Output "Native diagnostics: $logRoot"
    $report = Get-Content -LiteralPath (Join-Path $evidence 's3-results.json') -Raw | ConvertFrom-Json
    if ($report.pid -ne $trial.Id -or $trial.ExitCode -ne $report.result) { throw 'Scenario and original process result differ.' }
    if ($report.result -ne 0) { exit $report.result }
}
exit 0
