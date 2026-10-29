[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot,
      [string]$UnsupportedRar = (Join-Path $PSScriptRoot '../.local/evidence/2026-10-29-s4r/unsupported-fixtures/with_recovery.rar'))
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-s4-ui') { throw 'Exact registered S4U scratch required.' }
$key = [guid]::NewGuid().ToString('N')
$run = Join-Path $scratch "trial-$key"
$profile = Join-Path $scratch "profile-$key"
foreach ($path in @($run,$profile,(Join-Path $run 'fixtures/gallery'),(Join-Path $run 'other'),(Join-Path $scratch 'temp'))) { [void][IO.Directory]::CreateDirectory($path) }
Copy-Item -LiteralPath $UnsupportedRar -Destination (Join-Path $run 'fixtures/gallery/unsupported.rar')
$env:PATHWEFT_PROFILE = $profile
$env:PATHWEFT_TEST_HIDDEN = '1'
$env:PATHWEFT_S2_NATIVE_ONLY = '0'
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'), @('backdrop=1','animations=0','smoothscroll=0','textscale=1','hidden=0','singlepane=0','preview=0','tray=0','wine=0','nativemenu=0','lastseen=1.0.62','taskbarsearch=0',"tabs.Left=$run\fixtures","tabs.Right=$run\other"))
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
$process = Start-Process -FilePath $executable -ArgumentList @("--verify-fileui=`"$run`"") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.txt') -RedirectStandardError (Join-Path $run 'stderr.txt')
[ordered]@{ pid=$process.Id; run=$run; profile=$profile } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'active-trial.json') -Encoding utf8
$process.WaitForExit(); $process.Refresh()
$report = Get-Content -LiteralPath (Join-Path $run 'evidence/results.json') -Raw | ConvertFrom-Json
if ($process.ExitCode -ne $report.result -or $process.Id -ne $report.pid) { throw 'Original process/report mismatch.' }
Write-Output "S4U report: $run/evidence/results.json"
$report.checks | Where-Object { $_.passed -eq $false -or $_.failure } | ConvertTo-Json -Depth 4
exit $report.result
