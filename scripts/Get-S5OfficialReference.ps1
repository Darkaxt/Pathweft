[CmdletBinding()]
param([string]$ScratchRoot = 'D:\Temp\pathweft-s5-native')
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -ne 'D:\Temp\pathweft-s5-native') { throw 'Exact registered S5 scratch required.' }
$package = Join-Path $scratch 'official-teracopy.zip'
$output = Join-Path $scratch 'official-reference'
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $output)) { throw 'Preserve existing reference operation/output; do not duplicate the download.' }
$uri = 'https://codesector.com/files/teracopy.zip'
Invoke-WebRequest -Uri $uri -OutFile $package
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($entry in $entries) {
        if ([IO.Path]::IsPathRooted($entry) -or $entry.Replace('\','/').Split('/') -contains '..') { throw 'Unsafe package path.' }
        if ([IO.Path]::GetFileName($entry) -ieq 'BTCR.dll') { throw 'Vendor package unexpectedly contains the separately denied module; do not extract or run it.' }
    }
}
finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($package, $output)
$executables = @(Get-ChildItem -LiteralPath $output -Filter TeraCopy.exe -Recurse -File)
$records = foreach ($file in $executables) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Code Sector') { throw 'Official reference signature is not qualified.' }
    [ordered]@{ path=$file.FullName; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash; bytes=$file.Length; signature=$signature.Status.ToString(); signer=$signature.SignerCertificate.Subject; fileVersion=$file.VersionInfo.FileVersion; productVersion=$file.VersionInfo.ProductVersion }
}
$persistent = Join-Path $PSScriptRoot '../.local/research/teracopy-4.0.5'
[void][IO.Directory]::CreateDirectory($persistent)
[ordered]@{
    origin=$uri; packageSha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    entries=$entries; executables=$records; deniedModulePresent=$false
    installersExecuted=$false; shellRegistration=$false; targetExecuted=$false
    installedState='No intentional change to installed application, activation, configuration or protection.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $persistent 'official-reference.json') -Encoding utf8
Write-Output 'Official package inspected/extracted as data; no install.cmd, installer, target or denied module executed.'
$records | ConvertTo-Json -Depth 4
