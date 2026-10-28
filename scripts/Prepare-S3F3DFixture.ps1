[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [Parameter(Mandatory)][string]$InputFile)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$input = Get-Content -LiteralPath $InputFile -Raw | ConvertFrom-Json
$asset = Get-Item -LiteralPath $input.path
$before = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash
if ($before -ne $input.sha256 -or $asset.Length -ne $input.bytes) { throw 'The user-selected GLB changed; preserve it and update the private evidence input deliberately.' }
$target = Join-Path $scratch 'fixture/files/pwfts3_f3d.glb'
Copy-Item -LiteralPath $asset.FullName -Destination $target
if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $before) { throw 'The controlled render fixture differs from the user model.' }
$stream = [IO.File]::OpenRead($asset.FullName)
try {
    $reader = [IO.BinaryReader]::new($stream)
    [void]$reader.ReadBytes(12)
    $length = $reader.ReadUInt32(); $kind = $reader.ReadUInt32()
    if ($kind -ne 0x4E4F534A -or $length -gt 4194304) { throw 'Unexpected GLB JSON chunk.' }
    $model = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)) | ConvertFrom-Json
}
finally { $stream.Dispose() }
[ordered]@{ originalPath=$asset.FullName; sha256=$before; bytes=$asset.Length; meshes=@($model.meshes).Count; materials=@($model.materials).Count; animations=@($model.animations).Count; preserved=((Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash -eq $before) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'fixture/user-glb-readback.json') -Encoding utf8
Write-Output 'Prepared a hash-identical, controlled GLB render fixture; the original asset is unchanged.'
