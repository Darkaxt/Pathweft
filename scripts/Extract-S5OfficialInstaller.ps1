[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native'
$installer = Join-Path $root 'official-teracopy-4.0.5.exe'
$output = Join-Path $root 'official-4.0.5-extracted'
if (Test-Path -LiteralPath $output) { throw 'Existing extraction must be inspected, not replayed.' }
if ((Get-FileHash -LiteralPath $installer).Hash -ne '22F050B51C80A344CBDAF368C7F6A9266EFCA4D95B5109D606466906A421FF7E') { throw 'Pinned official installer changed.' }
if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'Installer signature invalid.' }
[void][IO.Directory]::CreateDirectory($output)
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $installer
$info.Arguments = "/extract `"$output`" /exenoui"
$info.WorkingDirectory = $root
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.EnvironmentVariables['TEMP'] = $root
$info.EnvironmentVariables['TMP'] = $root
# Advanced Installer documents /extract as payload extraction, not installation.
# No MSI installation, administrative extraction, shell registration or UAC request.
$process = [Diagnostics.Process]::Start($info)
$identity = [ordered]@{pid=$process.Id; start=$process.StartTime.ToUniversalTime().ToString('o'); arguments=$info.Arguments}
$process.WaitForExit()
$identity.exitCode = $process.ExitCode
$identity.files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Select-Object FullName, Length)
$identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'official-extraction-receipt.json') -Encoding utf8
$identity | ConvertTo-Json -Depth 4
if ($process.ExitCode -ne 0) { throw 'Official extraction failed; preserve receipt and do not substitute installation.' }
