[CmdletBinding()]
param([string]$ScratchRoot='D:\Temp\pathweft-s8-delivery')
$ErrorActionPreference='Stop'
if([IO.Path]::GetFullPath($ScratchRoot) -ne 'D:\Temp\pathweft-s8-delivery'){throw 'Exact S8 scratch required.'}
$run=Join-Path $ScratchRoot ('trial-'+[guid]::NewGuid().ToString('N'))
$profile=Join-Path $run 'profile'
foreach($path in @($profile,(Join-Path $run 'initial'),(Join-Path $ScratchRoot 'temp'))){[void][IO.Directory]::CreateDirectory($path)}
$env:PATHWEFT_PROFILE=$profile
$env:PATHWEFT_TEST_HIDDEN='1'
$env:PATHWEFT_S2_NATIVE_ONLY='0'
$env:TEMP=$env:TMP=Join-Path $ScratchRoot 'temp'
$project=Split-Path -Parent $PSScriptRoot
[xml]$metadata=Get-Content -LiteralPath (Join-Path $project 'FileExplorer.csproj')
$version=($metadata.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('backdrop=1','animations=0','smoothscroll=0','textscale=1','hidden=0','singlepane=0','preview=0','tray=0','wine=0','nativemenu=0','taskbarsearch=0',"lastseen=$version","tabs.Left=$run\initial","tabs.Right=$run\initial"))
$exe=Join-Path $ScratchRoot 'artifacts/bin/FileExplorer/debug/Pathweft.exe'
$process=Start-Process -FilePath $exe -ArgumentList @("--verify-s8=`"$run`"") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.txt') -RedirectStandardError (Join-Path $run 'stderr.txt')
[ordered]@{pid=$process.Id;run=$run;profile=$profile} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ScratchRoot 'active-trial.json') -Encoding UTF8
$process.WaitForExit();$process.Refresh()
if(!(Test-Path -LiteralPath (Join-Path $run 'evidence/results.json'))){
 Get-Content -LiteralPath (Join-Path $run 'stderr.txt')
 throw "S8 process $($process.Id) exited $($process.ExitCode) before writing its acceptance receipt: $run"
}
$report=Get-Content -LiteralPath (Join-Path $run 'evidence/results.json') -Raw | ConvertFrom-Json
if($process.ExitCode -ne $report.result -or $process.Id -ne $report.pid){throw 'Original process/report mismatch.'}
[ordered]@{report="$run/evidence/results.json";result=$report.result;checks=$report.checks} | ConvertTo-Json -Depth 7
if($report.result -eq 0){
    Copy-Item -LiteralPath (Join-Path $run 'evidence/results.json') -Destination (Join-Path $run 'evidence/initial-results.json')
    $env:PATHWEFT_S8_RESTART='1'
    $restart=Start-Process -FilePath $exe -ArgumentList @("--verify-s8=`"$run`"") -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'restart-stdout.txt') -RedirectStandardError (Join-Path $run 'restart-stderr.txt')
    $restart.WaitForExit();$restart.Refresh()
    $receipt=Get-Content -LiteralPath (Join-Path $run 'evidence/results.json') -Raw | ConvertFrom-Json
    if($restart.ExitCode -ne 0 -or $receipt.pid -ne $restart.Id -or $receipt.result -ne 0){throw 'Separate-process Native appearance restoration failed.'}
    $receipt.checks | ConvertTo-Json -Depth 5
}
exit $report.result
