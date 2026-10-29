[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s[1234]-[^\\]+$' -and $scratch -ne 'D:\Temp\pathweft-rars-native') { throw 'Use the registered stage scratch root.' }
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$env:PATHWEFT_PROFILE = Join-Path $scratch 'selftest-profile'
[void][IO.Directory]::CreateDirectory($env:PATHWEFT_PROFILE)
$executable = Join-Path $scratch 'artifacts/bin/FileExplorer/release/Pathweft.exe'
$process = Start-Process -FilePath $executable -ArgumentList '--selftest' -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput (Join-Path $scratch 'selftest-output.txt') -RedirectStandardError (Join-Path $scratch 'selftest-error.txt')
Get-Content -LiteralPath (Join-Path $scratch 'selftest-output.txt')
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $scratch 'temp/pathweft-selftest.txt') -ErrorAction SilentlyContinue
}
exit $process.ExitCode
