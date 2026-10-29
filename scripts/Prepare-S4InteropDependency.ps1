[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [string]$CacheRoot = 'D:\BuildCache\Pathweft')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s4-[^\\]+$') { throw 'Use registered S4 native scratch.' }
$project = Split-Path -Parent $PSScriptRoot
$pin = '9b2379be603a9d4ebff06c56944f87f0c623f8db'
$source = Join-Path $scratch "inputs/interop-source/SharpSevenZip-$pin"
$file = Join-Path $source 'SharpSevenZip/SharpSevenZip/SharpSevenZipCompressor.cs'
$patch = Join-Path $project 'vendor/SharpSevenZip/empty-directory.patch'
$originalHash = '325D4616E3D3C6FAA0124FF3F954851C7E1C75E0C5D96F10B2D1A248ED52B577'
Push-Location $source
try {
    & git apply --reverse --check --ignore-space-change $patch
    if ($LASTEXITCODE -ne 0) {
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $originalHash -or (Get-FileHash -LiteralPath (Join-Path $source 'SharpSevenZip/SharpSevenZip/ArchiveUpdateCallback.cs') -Algorithm SHA256).Hash -ne '10640F6672F6E03294792ACD642376B51D325742E475FB7188C6987E280089AE') { throw 'Source is neither the pinned original nor the exact reviewed patch; preserve it.' }
        & git apply --check --ignore-space-change $patch
        if ($LASTEXITCODE -ne 0) { throw 'Reviewed source patch does not apply.' }
        & git apply --ignore-space-change $patch
        if ($LASTEXITCODE -ne 0) { throw 'Source patch failed.' }
    }
}
finally { Pop-Location }
& (Join-Path $PSScriptRoot 'Build-Development.ps1') -ScratchRoot $scratch -CacheRoot $CacheRoot -Configuration Release -ProjectFile (Join-Path $source 'SharpSevenZip/SharpSevenZip/SharpSevenZip.csproj') -BuildProperty @('TargetFrameworks=net8.0','GeneratePackageOnBuild=false','Version=2.0.130-pathweft.1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$output = Join-Path $scratch 'artifacts/bin/SharpSevenZip/release_net8.0/SharpSevenZip.dll'
if (!(Test-Path -LiteralPath $output)) { throw 'Expected source-built library output is missing.' }
$target = Join-Path $CacheRoot 'sharpsevenzip/2.0.130-pathweft.1'
if (Test-Path -LiteralPath $target) {
    $previous = Get-Content -LiteralPath (Join-Path $target 'manifest.json') -Raw | ConvertFrom-Json
    if ($previous.sourceCommit -ne $pin -or (Get-FileHash -LiteralPath (Join-Path $target 'SharpSevenZip.dll') -Algorithm SHA256).Hash -ne $previous.dllSha256) { throw 'Existing dependency identity changed; preserve it.' }
}
[void][IO.Directory]::CreateDirectory($target)
Copy-Item -LiteralPath $output -Destination $target
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $target 'LICENSE.txt')
[ordered]@{ version='2.0.130-pathweft.1'; sourceCommit=$pin; originalCompressorSha256=$originalHash; originalCallbackSha256='10640F6672F6E03294792ACD642376B51D325742E475FB7188C6987E280089AE'; patchSha256=(Get-FileHash $patch -Algorithm SHA256).Hash; modifiedCompressorSha256=(Get-FileHash $file -Algorithm SHA256).Hash; modifiedCallbackSha256=(Get-FileHash (Join-Path $source 'SharpSevenZip/SharpSevenZip/ArchiveUpdateCallback.cs') -Algorithm SHA256).Hash; dllSha256=(Get-FileHash (Join-Path $target 'SharpSevenZip.dll') -Algorithm SHA256).Hash; consumer='Pathweft S4 native archive interop and final packaging' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding utf8
Write-Output "Built pinned interop with the reviewed empty-directory fix: $target"
