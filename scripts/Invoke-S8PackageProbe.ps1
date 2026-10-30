[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProductDirectory,[Parameter(Mandatory)][string]$RunRoot)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$run=[IO.Path]::GetFullPath($RunRoot)
$product=[IO.Path]::GetFullPath($ProductDirectory)
if($run -notmatch '^D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}$' -or !$product.StartsWith($run+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use an exact owned package verification copy.'}
$manifest=Get-Content -LiteralPath (Join-Path $product 'release-manifest.json') -Raw | ConvertFrom-Json
foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $product $file.path)).Hash -ne $file.sha256){throw "The actual delivered member changed: $($file.path)"}}
$env:PATHWEFT_PROFILE=Join-Path $run 'profile'
$env:TEMP=$env:TMP=Join-Path $run 'temp'
& (Join-Path $PSScriptRoot 'Build-Development.ps1') -ScratchRoot (Join-Path $run 'probe-build') -Configuration Release -ProjectFile (Join-Path $project 'tests/PackagedChecks/PackagedChecks.csproj') -BuildProperty @("PathweftAssembly=$product\Pathweft.dll")
if($LASTEXITCODE -ne 0){throw 'Independent delivered-assembly consumer did not compile.'}
$probeOutput=Join-Path $run 'probe-build/artifacts/bin/PackagedChecks/release'
foreach($name in @('PackagedChecks.exe','PackagedChecks.dll','PackagedChecks.deps.json','PackagedChecks.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path $probeOutput $name) -Destination $product}
$probe=Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList @(('"'+(Join-Path $product 'PackagedChecks.dll')+'"'),('"'+$run+'"'),('"'+(Join-Path $project 'README.md')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'probe-output.txt') -RedirectStandardError (Join-Path $run 'probe-error.txt')
$probe.WaitForExit();$probe.Refresh()
if($probe.ExitCode -ne 0){Get-Content (Join-Path $run 'probe-error.txt');throw "Delivered assembly integration check failed: $run"}
foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $product $file.path)).Hash -ne $file.sha256){throw 'The probe changed a delivered program member.'}}
Get-Content -LiteralPath (Join-Path $run 'results.json')
