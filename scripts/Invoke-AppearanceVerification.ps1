[CmdletBinding()]
param([string]$Executable='D:\Temp\pathweft-s8-delivery\artifacts\bin\FileExplorer\debug\Pathweft.exe',[switch]$CloseOnly,[switch]$SidebarOnly,[switch]$SettingsActionOnly,[switch]$WidgetOnly,[switch]$WidgetDismissOnly,[switch]$ArchiveDragOnly,[switch]$PreviewOnly,[switch]$DrivesOnly,[switch]$StatsOnly,[switch]$CloudOnly,[switch]$CrcOnly,[switch]$Round118,[switch]$OrbitOnly,[string]$Model,[string]$Archive)
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
if($SidebarOnly){$start.Environment['PATHWEFT_VERIFY_SIDEBAR_ONLY']='1'}
if($SettingsActionOnly){$start.Environment['PATHWEFT_VERIFY_SETTINGS_ACTION_ONLY']='1'}
if($WidgetOnly){$start.Environment['PATHWEFT_VERIFY_WIDGET_ONLY']='1'}
if($WidgetDismissOnly){$start.Environment['PATHWEFT_VERIFY_WIDGET_DISMISS_ONLY']='1'}
if($ArchiveDragOnly){$start.Environment['PATHWEFT_VERIFY_ARCHIVE_DRAG_ONLY']='1'}
if($PreviewOnly){$start.Environment['PATHWEFT_VERIFY_PREVIEW_ONLY']='1'}
if($DrivesOnly){$start.Environment['PATHWEFT_VERIFY_DRIVES_ONLY']='1'}
if($StatsOnly){$start.Environment['PATHWEFT_VERIFY_STATS_ONLY']='1'}
if($CloudOnly){$start.Environment['PATHWEFT_VERIFY_CLOUD_ONLY']='1'}
if($CrcOnly){$start.Environment['PATHWEFT_VERIFY_CRC_ONLY']='1'}
if($Round118){$start.Environment['PATHWEFT_VERIFY_ROUND118']='1'}
if($OrbitOnly){$start.Environment['PATHWEFT_VERIFY_ORBIT']='1'}
if($env:PATHWEFT_VERIFY_S25 -eq '1'){$start.Environment['PATHWEFT_VERIFY_S25']='1'}
if($env:PATHWEFT_VERIFY_RENDER_PERF -eq '1'){$start.Environment['PATHWEFT_VERIFY_RENDER_PERF']='1'}
if($env:PATHWEFT_VERIFY_OPEN1111 -eq '1'){$start.Environment['PATHWEFT_VERIFY_OPEN1111']='1'}
if($env:PATHWEFT_VERIFY_FOLDER112 -eq '1'){$start.Environment['PATHWEFT_VERIFY_FOLDER112']='1'}
if($env:PATHWEFT_VERIFY_MEDIA113 -eq '1'){$start.Environment['PATHWEFT_VERIFY_MEDIA113']='1';$start.Environment['PATHWEFT_MEDIA113_INPUTS']=$env:PATHWEFT_MEDIA113_INPUTS}
if($env:PATHWEFT_VERIFY_UPDATE114 -eq '1'){$start.Environment['PATHWEFT_VERIFY_UPDATE114']='1';$start.Environment['PATHWEFT_MEDIA113_INPUTS']=$env:PATHWEFT_MEDIA113_INPUTS}
if($env:PATHWEFT_VERIFY_PREVIEW115 -eq '1'){$start.Environment['PATHWEFT_VERIFY_PREVIEW115']='1'}
if($env:PATHWEFT_VERIFY_DEPS117 -eq '1'){$start.Environment['PATHWEFT_VERIFY_DEPS117']='1';$start.Environment['PATHWEFT_MEDIA113_INPUTS']=$env:PATHWEFT_MEDIA113_INPUTS}
if($env:PATHWEFT_HTML116_PATHS){$start.Environment['PATHWEFT_HTML116_PATHS']=$env:PATHWEFT_HTML116_PATHS}
if($env:PATHWEFT_MEDIA113_CALLER){$start.Environment['PATHWEFT_MEDIA113_CALLER']=$env:PATHWEFT_MEDIA113_CALLER}
if($Model){$start.Environment['PATHWEFT_VERIFY_MODEL']=$Model}
if($Archive){$start.Environment['PATHWEFT_VERIFY_ARCHIVE']=$Archive}
$start.Environment['TEMP']=$temp;$start.Environment['TMP']=$temp
$version=([xml](Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'FileExplorer.csproj'))).Project.PropertyGroup.Version | Where-Object {$_}
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('theme.native=1','theme.mode=dark','theme.windowsaccent=1','animations=0','tray=0','wine=0','taskbarsearch=0',"lastseen=$version","tabs.Left=$run","tabs.Right=$run"))
$process=[Diagnostics.Process]::Start($start)
$process.WaitForExit()
if(!(Test-Path -LiteralPath (Join-Path $run 'results.json'))){throw "Original hidden process $($process.Id) exited $($process.ExitCode) before writing its receipt: $run"}
$receipt=Get-Content -LiteralPath (Join-Path $run 'results.json') -Raw | ConvertFrom-Json
if($receipt.pid -ne $process.Id -or $receipt.result -ne $process.ExitCode){throw 'Original appearance verification process differs from its receipt.'}
$receipt | ConvertTo-Json -Depth 8
Write-Output ('Fixture: '+$run)
exit $process.ExitCode
