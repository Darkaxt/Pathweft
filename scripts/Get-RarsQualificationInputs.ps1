[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Exact registered native scratch required.' }
$inputs = Join-Path $scratch 'qualification-inputs'
[void][IO.Directory]::CreateDirectory($inputs)
$package = Join-Path $inputs 'unrarw64.exe'
$uri = 'https://www.rarlab.com/rar/unrarw64.exe'
Invoke-WebRequest -Uri $uri -OutFile $package
$tool = Join-Path $env:USERPROFILE 'scoop/apps/7zip/current/7z.exe'
& $tool x $package ('-o' + (Join-Path $inputs 'unrar')) -y
if ($LASTEXITCODE -ne 0) { throw 'Official UnRAR package could not be inspected; installer was not executed.' }
$executable = Join-Path $inputs 'unrar/UnRAR.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Expected reference decoder not found.' }
$signature = Get-AuthenticodeSignature -LiteralPath $executable
[ordered]@{ origin=$uri; packageSha256=(Get-FileHash $package -Algorithm SHA256).Hash; executableSha256=(Get-FileHash $executable -Algorithm SHA256).Hash; signatureStatus=$signature.Status.ToString(); signer=$signature.SignerCertificate.Subject; fileVersion=(Get-Item $executable).VersionInfo.FileVersion; purpose='Independent RAR interoperability qualification only; not a Pathweft runtime dependency'; installerExecuted=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputs 'unrar-provenance.json') -Encoding utf8
$source = 'D:\BuildCache\Pathweft\cargo\git\checkouts\rars-1a73b494a57a3ea7\e2415a2\crates\rars\tests\fixtures'
$fixtures = Join-Path $scratch 'unsupported-fixtures'
[void][IO.Directory]::CreateDirectory($fixtures)
foreach ($relative in @('rar50/with_recovery.rar','rar50/multivol.part1.rar','rar50/multivol.part2.rar','rar50/multivol.part3.rar','rar50/wild/symlink.rar')) {
    Copy-Item -LiteralPath (Join-Path $source $relative) -Destination (Join-Path $fixtures ([IO.Path]::GetFileName($relative)))
}
Write-Output 'Reference decoder extracted without installation; unsupported source fixtures copied from the pinned retained source.'
