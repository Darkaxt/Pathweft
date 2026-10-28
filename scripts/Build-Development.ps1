[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ScratchRoot,
    [string]$CacheRoot = 'D:\BuildCache\Pathweft',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ProjectFile
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$ProjectFile) { $ProjectFile = Join-Path $projectRoot 'FileExplorer.csproj' }
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$cache = [IO.Path]::GetFullPath($CacheRoot)
if ($scratch -notmatch '^[DE]:\\Temp\\[^\\]+(?:\\.*)?$') {
    throw 'Use an exact task-owned directory under D:\Temp or E:\Temp.'
}
if ($cache -notmatch '^D:\\' -or $cache.StartsWith('D:\Temp\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Dependency caches belong in explicit persistent storage on D:.'
}

# No deletion or publishing here. Review generated scratch with transactional-cleanup.
$scopedEnvironment = @{
    TEMP = Join-Path $scratch 'temp'
    TMP = Join-Path $scratch 'temp'
    DOTNET_CLI_HOME = Join-Path $cache 'dotnet-home'
    NUGET_PACKAGES = Join-Path $cache 'nuget-packages'
    NUGET_HTTP_CACHE_PATH = Join-Path $cache 'nuget-http'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_NOLOGO = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    # This WPF project uses no optional SDK workloads. A fresh CLI home must not
    # repair unrelated machine-installed Android/MAUI workloads on first use.
    DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK = '1'
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
}
$previous = @{}
$exitCode = 1
try {
    foreach ($name in $scopedEnvironment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $scopedEnvironment[$name], 'Process')
    }
    foreach ($path in @($scratch, $scopedEnvironment.TEMP, $cache,
                       $scopedEnvironment.DOTNET_CLI_HOME, $scopedEnvironment.NUGET_PACKAGES,
                       $scopedEnvironment.NUGET_HTTP_CACHE_PATH)) {
        [void][IO.Directory]::CreateDirectory($path)
    }
    Push-Location $projectRoot
    try {
        & dotnet --version
        if ($LASTEXITCODE -ne 0) { throw 'The SDK selected by global.json is unavailable.' }
        $buildArguments = @(
            'build', ([IO.Path]::GetFullPath($ProjectFile)),
            '--configuration', $Configuration,
            '--artifacts-path', (Join-Path $scratch 'artifacts'),
            '--disable-build-servers', '--nologo', '--verbosity', 'minimal',
            '-maxcpucount:2', '-nodeReuse:false', '-p:UseSharedCompilation=false'
        )
        & dotnet @buildArguments
        $exitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
exit $exitCode
