[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native\fixtures'
if (Test-Path -LiteralPath $root) { throw 'Preserve existing fixture run.' }
$source = Join-Path $root 'copy-source'
$target = Join-Path $root 'copy-target'
[void][IO.Directory]::CreateDirectory($source)
[void][IO.Directory]::CreateDirectory($target)
[IO.File]::WriteAllText((Join-Path $source 'unicode-ç-文件.txt'), 'Pathweft S5 donor observation; disposable content.', [Text.UTF8Encoding]::new($false))
$bytes = [byte[]]::new(1048576)
[Random]::new(815).NextBytes($bytes)
$stream = [IO.File]::Open((Join-Path $source 'large.bin'), [IO.FileMode]::CreateNew)
try { for ($i=0; $i -lt 32; $i++) { $stream.Write($bytes,0,$bytes.Length) } } finally { $stream.Dispose() }
Set-Content -LiteralPath (Join-Path $source 'unicode-ç-文件.txt') -Stream 'PathweftFixture' -Value 'disposable ADS'
$manifest = @(Get-ChildItem -LiteralPath $source -File | ForEach-Object { [ordered]@{name=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'copy-input.json') -Encoding utf8
$manifest | ConvertTo-Json
