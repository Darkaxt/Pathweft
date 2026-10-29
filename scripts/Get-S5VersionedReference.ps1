[CmdletBinding()]
param([string]$ReceiptDirectory=(Join-Path $PSScriptRoot '../.local/research/teracopy-4.0.5'))
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native'
$package = Join-Path $root 'official-teracopy-4.0.5.exe'
$listing = Join-Path $root 'official-teracopy-4.0.5-list.txt'
$uri = 'https://codesector.com/files/teracopy4.0.5.exe'
$sevenZip = (Get-Command 7z.exe -ErrorAction Stop).Source
if (Test-Path -LiteralPath $package) { throw 'Existing download must be inspected rather than overwritten.' }
if (-not (Test-Path -LiteralPath $sevenZip)) { throw 'The existing archive reader is unavailable.' }
Invoke-WebRequest -Uri $uri -OutFile $package
$signature = Get-AuthenticodeSignature -LiteralPath $package
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Code Sector') { throw 'Official installer signature is not qualified; do not execute or extract.' }
$archiveListing = & $sevenZip l -slt -- $package
if ($LASTEXITCODE -ne 0) { throw 'Archive inspection failed; installer was not executed.' }
$archiveListing | Set-Content -LiteralPath $listing -Encoding utf8
$receipt = [ordered]@{
    origin = $uri; path = $package
    sha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    bytes = (Get-Item -LiteralPath $package).Length
    signature = $signature.Status.ToString(); signer = $signature.SignerCertificate.Subject
    archiveListing = $listing; installerExecuted = $false; extracted = $false
}
[void][IO.Directory]::CreateDirectory($ReceiptDirectory)
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ReceiptDirectory 'official-versioned-reference.json') -Encoding utf8
$receipt | ConvertTo-Json
