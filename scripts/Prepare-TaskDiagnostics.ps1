param([ValidateSet('dotnet-stack','dotnet-dump')][string]$Tool='dotnet-stack')
$ErrorActionPreference='Stop'
$env:TEMP='D:\Temp\pathweft-s13-s9-s11\build\temp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME='D:\BuildCache\Pathweft\dotnet-home'
$env:NUGET_PACKAGES='D:\BuildCache\Pathweft\nuget-packages'
$env:NUGET_HTTP_CACHE_PATH='D:\BuildCache\Pathweft\nuget-http'
$env:DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK='1'
dotnet tool install $Tool --version 10.0.745401 --tool-path D:\Temp\pathweft-s13-s9-s11\diagnostics
exit $LASTEXITCODE
