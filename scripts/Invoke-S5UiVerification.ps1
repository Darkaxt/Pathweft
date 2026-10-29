[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Join-Path 'D:\Temp\pathweft-s5-native' ('ui-'+[guid]::NewGuid().ToString('N'))
$profile=Join-Path $root 'profile';$temporary=Join-Path $root 'temp'
foreach($path in @($root,$profile,$temporary,(Join-Path $root 'source'),(Join-Path $root 'target'))) {[void][IO.Directory]::CreateDirectory($path)}
& 'D:\Temp\pathweft-s5-native\contracts-build\artifacts\bin\TransferContracts\debug\TransferContracts.exe' --storage-fixture (Join-Path $root 'storage-source')
if($LASTEXITCODE -ne 0){throw 'Owned storage fixture creation failed.'}
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('backdrop=1','animations=0','smoothscroll=0','textscale=1','hidden=1','singlepane=0','preview=0','tray=0','wine=0','nativemenu=0','lastseen=1.0.62','taskbarsearch=0',"tabs.Left=$root\source","tabs.Right=$root\target"))
$start=[Diagnostics.ProcessStartInfo]::new('D:\Temp\pathweft-s5-native\changed-build\artifacts\bin\FileExplorer\debug\Pathweft.exe')
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
$start.ArgumentList.Add("--verify-s5=$root")
$start.Environment['PATHWEFT_PROFILE']=$profile;$start.Environment['PATHWEFT_TEST_HIDDEN']='1';$start.Environment['PATHWEFT_S2_NATIVE_ONLY']='0';$start.Environment['TEMP']=$temporary;$start.Environment['TMP']=$temporary
$process=[Diagnostics.Process]::Start($start)
[ordered]@{pid=$process.Id;root=$root;profile=$profile} | ConvertTo-Json | Set-Content -LiteralPath 'D:\Temp\pathweft-s5-native\active-ui.json'
$process.WaitForExit()
$report=Get-Content -LiteralPath (Join-Path $root 'evidence/results.json') -Raw | ConvertFrom-Json
if($report.pid -ne $process.Id -or $report.result -ne $process.ExitCode){throw 'Actual trial process/result mismatch.'}
Write-Output "S5 UI results: $root\evidence\results.json"
$report.checks | Where-Object {$_.passed -eq $false -or $_.failure} | ConvertTo-Json -Depth 4
exit $process.ExitCode
