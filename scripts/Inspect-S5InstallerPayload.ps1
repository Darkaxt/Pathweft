[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native'
$source = Join-Path $root 'official-teracopy-4.0.5.exe'
$destination = Join-Path $root 'versioned-payload'
$sevenZip = (Get-Command 7z.exe -ErrorAction Stop).Source
if (Test-Path -LiteralPath $destination) { throw 'Preserve the existing payload inspection.' }
[void][IO.Directory]::CreateDirectory($destination)
& $sevenZip x '-y' "-o$destination" -- $source '[0]'
if ($LASTEXITCODE -ne 0) { throw 'Data extraction failed.' }
$payload = Join-Path $destination '[0]'
$data = [IO.File]::ReadAllBytes($payload)
$cabinets = [Collections.Generic.List[object]]::new()
for ($offset = 0; $offset -le $data.Length - 36; $offset++) {
    if ($data[$offset] -ne 77 -or $data[$offset+1] -ne 83 -or $data[$offset+2] -ne 67 -or $data[$offset+3] -ne 70) { continue }
    $size = [BitConverter]::ToUInt32($data, $offset + 8)
    if ($size -lt 36 -or $size -gt $data.Length - $offset) { continue }
    $cab = Join-Path $destination "payload-$offset.cab"
    $stream = [IO.File]::Open($cab, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $stream.Write($data, $offset, $size) } finally { $stream.Dispose() }
    $listing = & $sevenZip l -slt -- $cab
    if ($LASTEXITCODE -ne 0) { throw 'Embedded cabinet inspection failed; no target executed.' }
    $listing | Set-Content -LiteralPath "$cab.list.txt" -Encoding utf8
    $cabinets.Add([ordered]@{ path=$cab; offset=$offset; bytes=$size; listing="$cab.list.txt" })
}
[ordered]@{installerExecuted=$false; payloadSha256=(Get-FileHash -LiteralPath $payload).Hash; cabinets=$cabinets.ToArray()} | ConvertTo-Json -Depth 4
