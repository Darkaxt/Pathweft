<#
SPEC IDX-03 / IDX-08 check: an elevated connection from the folder root's owner
(Pathweft's administrator view) must not make the service scan the root with
the elevated token, whose logon session has none of the user's drive mappings.

Runs a private console instance per round (own pipe and config, no NTFS
volumes, no content index); it never touches the installed service.

  Standard side (the user's normal shell), once per run, before the elevated side:
    subst Q: <folder>    (a mapping that exists only in the standard logon session)
    Test-ElevatedClient.ps1 -Role standard -Run DIR -Rounds old,new -Pwindex PWINDEX
  Elevated side (administrator broker), started while the standard side waits:
    Test-ElevatedClient.ps1 -Role elevated -Run DIR -Rounds old,new -Pwindex PWINDEX -Exes OLD_EXE,NEW_EXE

For each round the elevated side starts the instance and signals; the standard
side adds Q:\ as a folder root and waits until it is scanned; the elevated side
then connects (status), asks for a rescan of Q: and waits for its result.
Result per round in DIR\<round>.result.json: passed when the rescan succeeds
and the instance never logs "cannot list Q:".
#>
param(
    [Parameter(Mandatory)][ValidateSet('standard', 'elevated')][string]$Role,
    [Parameter(Mandatory)][string]$Run,
    [Parameter(Mandatory)][string[]]$Rounds,
    [Parameter(Mandatory)][string]$Pwindex,
    [string[]]$Exes = @(),
    [string]$Letter = 'Q:'
)
$ErrorActionPreference = 'Continue'   # native stderr must not throw in Windows PowerShell 5.1; failures are checked explicitly
# powershell -File passes "old,new" as one string.
$Rounds = @($Rounds | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Exes = @($Exes | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

function Status([string]$pipe) {
    $json = & $Pwindex --pipe $pipe status --json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { return $null }
    $doc = ($json -join "`n") | ConvertFrom-Json
    $doc.stores | Where-Object { $_.name -eq $Letter } | Select-Object -First 1
}
function WaitFor([string]$path, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while (-not (Test-Path -LiteralPath $path)) {
        if ((Get-Date) -gt $deadline) { throw "timed out waiting for $path" }
        Start-Sleep -Milliseconds 250
    }
}

for ($i = 0; $i -lt $Rounds.Count; $i++) {
    $round = $Rounds[$i]
    $pipe = "pwidx03-$round-$PID"
    $dir = Join-Path $Run $round
    if ($Role -eq 'standard') {
        WaitFor "$dir.instance-ready" 300
        $pipe = (Get-Content -LiteralPath "$dir.instance-ready" -Raw).Trim()
        & $Pwindex --pipe $pipe folder add "$Letter\"
        if ($LASTEXITCODE -ne 0) { throw "folder add failed in round $round" }
        $deadline = (Get-Date).AddSeconds(120)
        do {
            Start-Sleep -Milliseconds 500
            $store = Status $pipe
        } while (($null -eq $store -or $store.state -ne 'ready') -and (Get-Date) -lt $deadline)
        @{ round = $round; state = $store.state; items = $store.items; error = $store.error } | ConvertTo-Json | Set-Content -LiteralPath "$dir.standard.json"
        Set-Content -LiteralPath "$dir.standard-done" -Value 'done'
        continue
    }

    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $process = Start-Process -FilePath $Exes[$i] -PassThru -WindowStyle Hidden `
        -ArgumentList @('console', '--pipe', $pipe, '--volumes', ',', '--config', "`"$dir\config.json`"", '--exit-after', '900') `
        -RedirectStandardOutput "$dir\service.out.log" -RedirectStandardError "$dir\service.err.log"
    try {
        $deadline = (Get-Date).AddSeconds(30)
        while ($null -eq (& $Pwindex --pipe $pipe status --json 2>$null) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
        Set-Content -LiteralPath "$dir.instance-ready" -Value $pipe
        WaitFor "$dir.standard-done" 300
        $before = Status $pipe                       # first elevated connection
        $elevatedStatus = (& $Pwindex --pipe $pipe status) -join "`n"
        & $Pwindex --pipe $pipe rescan $Letter | Out-Null
        $deadline = (Get-Date).AddSeconds(120)
        do {
            Start-Sleep -Milliseconds 500
            $after = Status $pipe
            $log = (Get-Content -LiteralPath "$dir\service.out.log", "$dir\service.err.log" -Raw -ErrorAction SilentlyContinue) -join "`n"
            $failed = $log -match [regex]::Escape("$Letter`: cannot list")
        } while (-not $failed -and ($null -eq $after -or $after.reloads -lt 1 -or $after.state -ne 'ready') -and (Get-Date) -lt $deadline)
        $sizes = (& $Pwindex --pipe $pipe sizes "$Letter\") -join "`n"
        $passed = (-not $failed) -and $after.state -eq 'ready' -and $after.reloads -ge 1 -and -not $after.error
        [ordered]@{
            round = $round; executable = $Exes[$i]; passed = $passed
            before = $before; after = $after; cannot_list_logged = $failed
            elevated_sizes = $sizes; elevated_status_caller = ($elevatedStatus -split "`n")[0] -replace 'S-1-5-21-[0-9-]+', '<sid>'
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$dir.result.json"
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    }
}
