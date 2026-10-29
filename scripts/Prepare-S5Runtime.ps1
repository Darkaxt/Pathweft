[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native'
$official = Join-Path $root 'official-4.0.5-extracted\15E018F'
$runtime = Join-Path $root 'runtime-4.0.5'
$expected = '1F0CCA0BE4E6A22E856C3B0DE6655A1AF8059A06CC66133398307DE335098C6F'
if (Test-Path -LiteralPath $runtime) { throw 'Preserve existing runtime/configuration.' }
$officialFiles = @(Get-ChildItem -LiteralPath $official -Recurse -File)
if ($officialFiles.Name -icontains 'BTCR.dll') { throw 'Official package unexpectedly includes separately denied content; stop qualification.' }
$executable = Join-Path $official 'TeraCopy.exe'
if ((Get-FileHash -LiteralPath $executable).Hash -ne $expected) { throw 'Official application differs from static-analysis target.' }
$signature = Get-AuthenticodeSignature -LiteralPath $executable
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Code Sector') { throw 'Unqualified official application.' }
[void][IO.Directory]::CreateDirectory($runtime)
$files = foreach ($name in @('TeraCopy.exe','Blake3.dll','xxHashAVX2.dll','xxHashSSE2.dll','License.txt','Readme.txt','Whatsnew.txt')) {
    $source = Join-Path $official $name
    $destination = Join-Path $runtime $name
    [IO.File]::Copy($source, $destination, $false)
    $digest = (Get-FileHash -LiteralPath $source).Hash
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $digest) { throw 'Staging hash mismatch.' }
    [ordered]@{name=$name; sha256=$digest; bytes=(Get-Item -LiteralPath $destination).Length}
}
[IO.File]::WriteAllText((Join-Path $runtime 'Portable'), '')
$options = @(
    '[Main]','OnTop=0','NoClose=0','EnableSound=0','ShowNotifications=0',
    # Transfer options belong in Transfer, not Main. Read back the generated
    # configuration and actual Verify control before accepting a measurement.
    '[Transfer]','TestAlways=1','TestSkippedFiles=0','UseVSS=0',
    'HashName=xxHash3-64','FilesAttributes=1','FilesTimestamps=1','FilesStreams=1',
    'FoldersAttributes=1','FoldersTimestamps=1','FoldersStreams=1',
    'Threads=2','Buffers=10','BufferSize=1048576','UseWriteCache=1','SyncMethodOnly=1'
)
[IO.File]::WriteAllLines((Join-Path $runtime 'Options.ini'), $options, [Text.UTF8Encoding]::new($false))
$receipt = [ordered]@{
    origin='https://codesector.com/files/teracopy4.0.5.exe'; version='4.0.5.0'
    versionSource='PE resource decoded by 7-Zip 26.05, installed signed EXE; same SHA256 as official payload'
    installerSha256='22F050B51C80A344CBDAF368C7F6A9266EFCA4D95B5109D606466906A421FF7E'
    signature=$signature.Status.ToString(); signer=$signature.SignerCertificate.Subject
    runtime=$runtime; files=$files; options=$options; portable=$true
    officialPackageContainsDeniedModule=$false; targetExecuted=$false
    installedSettingsCopied=$false; shellRegistration=$false; scriptsCopied=$false
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot '../.local/research/teracopy-4.0.5/runtime-qualification.json') -Encoding utf8
Write-Output "Qualified clean official TeraCopy 4.0.5, hash $expected; private portable profile at $runtime; no target execution yet."
