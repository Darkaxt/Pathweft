[CmdletBinding()]
param([string]$ApplicationRoot='C:\Pathweft')
$ErrorActionPreference='Stop'
if($ApplicationRoot -ne 'C:\Pathweft'){throw 'Disposable guest application path required.'}
$output='C:\Coordinator\Work\apps'
[void][IO.Directory]::CreateDirectory($output)
$env:PATHWEFT_MAINTENANCE_GUEST='1'
$env:PATHWEFT_PROFILE=Join-Path $output 'profile'
$env:TEMP=$env:TMP=Join-Path $output 'temp'
[void][IO.Directory]::CreateDirectory($env:TEMP)
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $ApplicationRoot 'Pathweft.exe'))
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.Arguments='--verify-maintenance-apps "'+$output+'"'
$child=[Diagnostics.Process]::Start($start)
$child.WaitForExit()
if(!(Test-Path -LiteralPath (Join-Path $output 'apps-results.json'))){throw 'Guest verification produced no receipt.'}
$receipt=Get-Content -LiteralPath (Join-Path $output 'apps-results.json') -Raw|ConvertFrom-Json
if($receipt.pid -ne $child.Id -or $receipt.result -ne $child.ExitCode){throw 'Guest verification identity/result mismatch.'}
$receipt|ConvertTo-Json -Depth 8
exit $child.ExitCode
