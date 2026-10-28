[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
if ($env:TEMP.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase) -or $env:TMP.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'The continuing user application must not inherit task scratch.' }
function Read-SettingsHashes {
    $configuration = Join-Path $env:APPDATA 'pooi.moe/QuickLook'
    @(Get-ChildItem -LiteralPath $configuration -Filter '*.config' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
        [ordered]@{ file=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$originals = @(Get-Process QuickLook -ErrorAction SilentlyContinue)
if ($originals.Count -gt 1) { throw 'More than one QuickLook process exists; no process was changed.' }
$original = if ($originals.Count -eq 1) { $originals[0] } else { $null }
$before = @(Read-SettingsHashes)
$receipt = [ordered]@{ originalPid=$null; stopped=$false; stopMethod=$null; previewTrialExit=$null; restoredPid=$null; restoredInputIdle=$false; settingsUnchanged=$null }
$trialExit = 1
try {
    if ($original) {
        $exe = $original.Path
        if ($exe -ne (Join-Path $env:LOCALAPPDATA 'Programs/QuickLook/QuickLook.exe')) { throw 'Unexpected QuickLook executable; no process was changed.' }
        $native = Get-CimInstance Win32_Process -Filter "ProcessId=$($original.Id)"
        if ($native.CommandLine -ne ('"' + $exe + '" /autorun')) { throw 'Original startup arguments changed; preserve the current process.' }
        $receipt.originalPid = $original.Id
        $binaryHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        $started = $original.StartTime
        # User explicitly authorized temporary QuickLook stop/restore on 2026-10-29.
        # Installed 4.0.2 exposes no donor control pipe; do not wait for a nonexistent endpoint.
        $current = Get-Process -Id $original.Id
        if ($current.StartTime -ne $started -or $current.Path -ne $exe) { throw 'QuickLook identity changed before stopping.' }
        if ($original.CloseMainWindow()) { $receipt.stopMethod = 'graceful-main-window' }
        else { Stop-Process -InputObject $current; $receipt.stopMethod = 'exact-tray-process' }
        $original.WaitForExit(); $receipt.stopped = $true
    }
    if (@(Get-Process QuickLook -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Standalone QuickLook is still present.' }
    $launcher = Join-Path $PSScriptRoot 'Invoke-S3Verification.ps1'
    $trial = Start-Process -FilePath (Get-Command pwsh).Source -ArgumentList @('-NoProfile','-File',('"' + $launcher + '"'),'-ScratchRoot',('"' + $scratch + '"')) -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput (Join-Path $scratch 'scenario-output.txt') -RedirectStandardError (Join-Path $scratch 'scenario-error.txt')
    $trialExit = $receipt.previewTrialExit = $trial.ExitCode
    Get-Content -LiteralPath (Join-Path $scratch 'scenario-output.txt')
    if ($trialExit -ne 0) { Get-Content -LiteralPath (Join-Path $scratch 'scenario-error.txt') }
}
finally {
    try {
        if ($original -and $receipt.stopped) {
            if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $binaryHash) { throw 'QuickLook binary changed; preserve the recovery receipt and original configuration.' }
            $running = @(Get-Process QuickLook -ErrorAction SilentlyContinue)
            if ($running.Count -gt 1 -or ($running.Count -eq 1 -and $running[0].Path -ne $exe)) { throw 'A different QuickLook owner appeared; do not launch a competing instance.' }
            $restored = if ($running.Count -eq 1) { $running[0] } else { Start-Process -FilePath $exe -ArgumentList '/autorun' -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -PassThru }
            $receipt.restoredPid = $restored.Id
            $receipt.restoredInputIdle = $restored.WaitForInputIdle()
            $restored.Refresh()
            if ($restored.HasExited -or !$receipt.restoredInputIdle -or $restored.Path -ne $exe) { throw 'QuickLook restoration did not reach its native message-loop readiness.' }
        }
    }
    finally {
        $after = @(Read-SettingsHashes)
        $receipt.settingsUnchanged = ((ConvertTo-Json -InputObject $before -Compress) -eq (ConvertTo-Json -InputObject $after -Compress))
        [ordered]@{ receipt=$receipt; settingsBefore=$before; settingsAfter=$after } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $scratch 'QuickLook-absence-and-restoration.json') -Encoding utf8
        $receipt | ConvertTo-Json -Compress
    }
}
exit $trialExit
