[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProductDirectory,[Parameter(Mandatory)][string]$RunRoot,[switch]$EngineStateOnly)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$run=[IO.Path]::GetFullPath($RunRoot)
$product=[IO.Path]::GetFullPath($ProductDirectory)
if($run -notmatch '^D:\\Temp\\pathweft-s8-delivery\\verification-[a-f0-9]{32}$' -or !$product.StartsWith($run+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use an exact owned package verification copy.'}
$manifest=Get-Content -LiteralPath (Join-Path $product 'release-manifest.json') -Raw | ConvertFrom-Json
foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $product $file.path)).Hash -ne $file.sha256){throw "The actual delivered member changed: $($file.path)"}}
$fixture=Join-Path $run ('integration-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$env:PATHWEFT_PROFILE=Join-Path $fixture 'profile'
$env:TEMP=$env:TMP=Join-Path $run 'temp'
& (Join-Path $PSScriptRoot 'Build-Development.ps1') -ScratchRoot (Join-Path $run 'probe-build') -Configuration Release -ProjectFile (Join-Path $project 'tests/PackagedChecks/PackagedChecks.csproj') -BuildProperty @("PathweftAssembly=$product\Pathweft.dll")
if($LASTEXITCODE -ne 0){throw 'Independent delivered-assembly consumer did not compile.'}
$probeOutput=Join-Path $run 'probe-build/artifacts/bin/PackagedChecks/release'
foreach($name in @('PackagedChecks.exe','PackagedChecks.dll','PackagedChecks.deps.json','PackagedChecks.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path $probeOutput $name) -Destination $product}
if($EngineStateOnly){
    & dotnet (Join-Path $product 'PackagedChecks.dll') --engine-state $run
    exit $LASTEXITCODE
}
$probe=Start-Process -FilePath (Get-Command dotnet.exe).Source -ArgumentList @(('"'+(Join-Path $product 'PackagedChecks.dll')+'"'),('"'+$fixture+'"'),('"'+(Join-Path $project 'README.md')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $fixture 'stdout.txt') -RedirectStandardError (Join-Path $fixture 'stderr.txt')
$probe.WaitForExit();$probe.Refresh()
if($probe.ExitCode -ne 0){Get-Content (Join-Path $fixture 'stderr.txt');throw "Delivered assembly integration check failed: $fixture"}
$receipt=Get-Content -LiteralPath (Join-Path $fixture 'results.json') -Raw | ConvertFrom-Json
if($receipt.pid -ne $probe.Id -or $receipt.result -ne 0 -or $receipt.product -ne (Join-Path $product 'Pathweft.dll')){throw 'Original package probe identity/result mismatch.'}
Copy-Item -LiteralPath (Join-Path $fixture 'results.json') -Destination (Join-Path $run 'results.json')
foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $product $file.path)).Hash -ne $file.sha256){throw 'The probe changed a delivered program member.'}}
Get-Content -LiteralPath (Join-Path $run 'results.json')
