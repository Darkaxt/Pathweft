[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [switch]$RarWriter)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$' -and $scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Use registered archive native scratch.' }
$key = [guid]::NewGuid().ToString('N')
$run = Join-Path $scratch "runs/$key"
$profile = Join-Path $scratch "profile-$key"
[void][IO.Directory]::CreateDirectory($run)
[void][IO.Directory]::CreateDirectory($profile)
foreach ($name in @('source','containers','destination')) { Copy-Item -LiteralPath (Join-Path $scratch "fixture/$name") -Destination (Join-Path $run $name) -Recurse }
$env:PATHWEFT_PROFILE = $profile
$env:PATHWEFT_TEST_HIDDEN = '1'
$env:PATHWEFT_VERIFY_RAR = $(if ($RarWriter) { '1' } else { '0' })
if ($RarWriter -and !$profile.StartsWith('D:\Temp\pathweft-rars-native\', [StringComparison]::OrdinalIgnoreCase)) { throw 'RAR trial must use its admitted hidden profile root.' }
$env:PATHWEFT_S2_NATIVE_ONLY = '0'
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'), @('backdrop=3','animations=0','smoothscroll=0','textscale=1','singlepane=0','preview=0','tray=0','wine=0','nativemenu=0','lastseen=1.0.62','taskbarsearch=0',"tabs.Left=$run\containers","tabs.Right=$run\destination"))
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
$trial = Start-Process -FilePath $executable -ArgumentList @("--verify-s4=`"$run`"") -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'native-output.txt') -RedirectStandardError (Join-Path $run 'native-error.txt')
[ordered]@{ pid=$trial.Id; root=$run; profile=$profile } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'active-trial.json') -Encoding utf8
$trial.WaitForExit(); $trial.Refresh()
$evidence = Join-Path $run "evidence/$($trial.Id)"
Write-Output "S4 evidence: $evidence"
$report = Get-Content -LiteralPath (Join-Path $evidence 's4-results.json') -Raw | ConvertFrom-Json
if ($report.pid -ne $trial.Id -or $trial.ExitCode -ne $report.result) { throw 'Report and original process result differ.' }
if ($report.originalClipboardRestored) {
    do {
        $reader = Start-Process -FilePath $executable -ArgumentList @("--verify-s4-clipboard=`"$(Join-Path $evidence 'clipboard-expected.json')`"") -PassThru -Wait -WindowStyle Hidden
    } while ($reader.ExitCode -eq 3) # explicit read-only Windows clipboard-busy result
    if ($reader.ExitCode -ne 0) { throw 'Independent post-exit clipboard readback failed; no clipboard mutation was attempted.' }
    Write-Output 'Independent STA process verified restored clipboard after the original app exited.'
}
exit $report.result
