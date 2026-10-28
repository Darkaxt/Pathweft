[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s1-[^\\]+$') { throw 'Use registered S1 native/build scratch.' }
$baselineExe = Join-Path $scratch 'baseline-build/artifacts/bin/FileExplorer/release/FileLabs.exe'
$baselineProfile = Join-Path $scratch 'baseline-profile'
[void][IO.Directory]::CreateDirectory($baselineProfile)
$env:FILELABS_PROFILE = $baselineProfile
$env:TEMP = $env:TMP = Join-Path $scratch 'temp'
$fixture = Join-Path $scratch 'fixture'
[IO.File]::WriteAllLines((Join-Path $baselineProfile 'settings.txt'), @('tray=0','lastseen=1.0.62','singlepane=0','quicklook=0','preview=0',"tabs.Left=$fixture\scope spaced é", "tabs.Right=$fixture\destination"))
$sentinel = Join-Path $baselineProfile 'isolation-sentinel.txt'
[IO.File]::WriteAllText($sentinel, 'Original File Labs profile sentinel; Pathweft must not migrate or modify it.')
$before = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$baseline = Start-Process -FilePath $baselineExe -PassThru -WindowStyle Normal
$exitCode = 1
try {
    [void]$baseline.WaitForInputIdle()
    $env:PATHWEFT_S1_BASELINE_PID = $baseline.Id.ToString()
    & (Join-Path $PSScriptRoot 'Invoke-S1Verification.ps1') -ScratchRoot $scratch
    $exitCode = $LASTEXITCODE
    $baseline.Refresh()
    $after = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    [ordered]@{ baselinePid=$baseline.Id; baselineExe=$baselineExe; baselineStillRunning=!$baseline.HasExited; profileSentinelUnchanged=$before -eq $after; forkVerificationExit=$exitCode } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'evidence/side-by-side.json') -Encoding utf8
}
finally {
    $baseline.Refresh()
    if (!$baseline.HasExited) {
        if (!$baseline.CloseMainWindow()) { throw "Original validation process $($baseline.Id) has no closable main window; preserve it for diagnosis." }
        $baseline.WaitForExit()
    }
    $baseline.Dispose()
}
exit $exitCode
