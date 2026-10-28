[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^[DE]:\\Temp\\[^\\]+(?:\\.*)?$') { throw 'Use exact task-owned Temp storage.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$env:DOTNET_CLI_HOME = 'D:\BuildCache\Pathweft\dotnet-home'
$env:NUGET_PACKAGES = 'D:\BuildCache\Pathweft\nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = 'D:\BuildCache\Pathweft\nuget-http'
$env:DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[void][IO.Directory]::CreateDirectory($env:TEMP)
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet build tests/SearchContracts/SearchContracts.csproj --artifacts-path (Join-Path $scratch 'contracts') --disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false -maxcpucount:2 --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & dotnet (Join-Path $scratch 'contracts/bin/SearchContracts/debug/SearchContracts.dll')
    exit $LASTEXITCODE
}
finally { Pop-Location }
