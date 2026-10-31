[CmdletBinding()]
param([string]$Executable='D:\Temp\pathweft-s8-delivery\artifacts\bin\FileExplorer\debug\Pathweft.exe',[switch]$CloseOnly)
$ErrorActionPreference='Stop'
$run=Join-Path 'D:\Temp\pathweft-s8-delivery' ('appearance-'+[guid]::NewGuid().ToString('N'))
$profile=Join-Path $run 'profile'
$temp=Join-Path $run 'temp'
foreach($path in @($run,$profile,$temp)){[void][IO.Directory]::CreateDirectory($path)}
$start=[Diagnostics.ProcessStartInfo]::new($Executable)
$start.ArgumentList.Add('--verify-s8-appearance='+$run)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
$start.Environment['PATHWEFT_PROFILE']=$profile
$start.Environment['PATHWEFT_TEST_HIDDEN']='1'
if($CloseOnly){$start.Environment['PATHWEFT_VERIFY_CLOSE_ONLY']='1'}
$start.Environment['TEMP']=$temp;$start.Environment['TMP']=$temp
$version=([xml](Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'FileExplorer.csproj'))).Project.PropertyGroup.Version | Where-Object {$_}
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('theme.native=1','theme.mode=dark','theme.windowsaccent=1','animations=0','tray=0','wine=0','taskbarsearch=0',"lastseen=$version","tabs.Left=$run","tabs.Right=$run"))
$process=[Diagnostics.Process]::Start($start)
$process.WaitForExit()
$receipt=Get-Content -LiteralPath (Join-Path $run 'results.json') -Raw | ConvertFrom-Json
if($receipt.pid -ne $process.Id -or $receipt.result -ne $process.ExitCode){throw 'Original appearance verification process differs from its receipt.'}
$receipt | ConvertTo-Json -Depth 8
Write-Output ('Fixture: '+$run)
exit $process.ExitCode
