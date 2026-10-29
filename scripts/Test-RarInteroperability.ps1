[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot, [Parameter(Mandatory)][string]$EditedRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
$edited = [IO.Path]::GetFullPath($EditedRoot)
if ($scratch -ne 'D:\Temp\pathweft-rars-native' -or !$edited.StartsWith($scratch + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Registered native fixtures required.' }
$executable = Join-Path $scratch 'qualification-inputs/unrar/UnRAR.exe'
$provenance = Get-Content -LiteralPath (Join-Path $scratch 'qualification-inputs/unrar-provenance.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $provenance.executableSha256 -or (Get-AuthenticodeSignature -LiteralPath $executable).Status -ne 'Valid') { throw 'Reference decoder identity/signature differs.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$reports = @()
foreach ($path in Get-ChildItem -LiteralPath $edited -File -Filter '*-rar*.rar') {
    $output = Join-Path $scratch ('unrar-output/' + [IO.Path]::GetFileNameWithoutExtension($path.Name))
    [void][IO.Directory]::CreateDirectory($output)
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('x','-idq','-o+','-y',$(if ($path.Name.StartsWith('encrypted')) { '-pfixture-password' } else { '-p-' }),$path.FullName,($output + '\'))) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit(); $code = $process.ExitCode
    $outText = $stdout.GetAwaiter().GetResult(); $errText = $stderr.GetAwaiter().GetResult(); $process.Dispose()
    if ($code -ne 0) { throw "Reference decoder rejected $($path.Name): $code $errText $outText" }
    $actual = Join-Path $output 'renamed 子/large.bin'
    $original = Join-Path $scratch 'rar-fixtures/large-original.bin'
    if ((Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash) { throw 'Independent extraction content differs.' }
    if ([IO.File]::ReadAllText((Join-Path $output 'second.txt')) -ne 'retained secondary content') { throw 'Unrelated member changed.' }
    if ([IO.File]::ReadAllText((Join-Path $output 'added.txt')) -ne 'new payload') { throw 'Added member differs.' }
    if (([IO.File]::GetAttributes($actual) -band [IO.FileAttributes]::ReadOnly) -eq 0) { throw 'Retained RAR member read-only attribute was lost.' }
    $reports += [ordered]@{ archive=$path.Name; archiveSha256=(Get-FileHash -LiteralPath $path.FullName -Algorithm SHA256).Hash; decoderExit=$code; contentSha256=(Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash; attributes=[IO.File]::GetAttributes($actual).ToString(); modifiedUtc=[IO.File]::GetLastWriteTimeUtc($actual).ToString('o') }
}
if ($reports.Count -ne 5) { throw 'All five legacy/modern/solid/encrypted variants must be covered.' }
[ordered]@{ decoder=$provenance; fixtures=$reports } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $scratch 'unrar-interoperability.json') -Encoding utf8
Write-Output 'Official signed UnRAR independently extracted all five edited RAR4/RAR5/RAR7 solid/encrypted archives with matching contents.'
