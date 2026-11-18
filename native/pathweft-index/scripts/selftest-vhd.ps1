# Disposable-volume self-test for pathweft-indexd (SPEC IDX-14).
# Run elevated (through the administrator broker). Creates a 1 GB expandable
# VHDX under D:\Temp\pathweft-index\vhd, formats it NTFS, runs
# `pathweft-indexd selftest` on it, then detaches and deletes the VHDX.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Report,
    [string]$Root = 'D:\Temp\pathweft-index\vhd'
)
$ErrorActionPreference = 'Stop'
$vhd = Join-Path $Root 'pwtest.vhdx'
$scriptFile = Join-Path $Root 'diskpart.txt'
$log = Join-Path $Root 'selftest-vhd.log'
New-Item -ItemType Directory -Force -Path $Root | Out-Null
if (Test-Path $vhd) { throw "$vhd already exists; refusing to reuse it" }

function Invoke-DiskPart([string[]]$lines) {
    Set-Content -Path $scriptFile -Value $lines -Encoding ascii
    $out = & "$env:WINDIR\System32\diskpart.exe" /s $scriptFile 2>&1
    Add-Content -Path $log -Value $out
    if ($LASTEXITCODE -ne 0) { throw "diskpart failed ($LASTEXITCODE): $($out -join ' ')" }
}

$used = (Get-PSDrive -PSProvider FileSystem).Name
$letter = @('W','V','U','S','R','Q','P','O','N','L','K') | Where-Object { $used -notcontains $_ } | Select-Object -First 1
if (-not $letter) { throw 'no free drive letter' }

$exit = 1
$attached = $false
try {
    Invoke-DiskPart @(
        "create vdisk file=`"$vhd`" maximum=1024 type=expandable",
        "select vdisk file=`"$vhd`"",
        'attach vdisk',
        'create partition primary',
        'format fs=ntfs quick label=PWTEST',
        "assign letter=$letter"
    )
    $attached = $true
    Start-Sleep -Seconds 2
    # Windows PowerShell turns native stderr lines into errors; with 'Stop'
    # the first log line would abort the run, so capture streams to files.
    $outFile = Join-Path $Root 'selftest.out.log'
    $errFile = Join-Path $Root 'selftest.err.log'
    $ErrorActionPreference = 'Continue'
    & $Exe selftest --volume "${letter}:" --report $Report > $outFile 2> $errFile
    $exit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Get-Content $outFile, $errFile | Add-Content -Path $log
    Get-Content $outFile
}
finally {
    if ($attached -or (Test-Path $vhd)) {
        try { Invoke-DiskPart @("select vdisk file=`"$vhd`"", 'detach vdisk') } catch { Add-Content -Path $log -Value "detach: $_" }
    }
    Remove-Item -LiteralPath $vhd -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scriptFile -Force -ErrorAction SilentlyContinue
    $gone = -not (Test-Path $vhd)
    Add-Content -Path $log -Value "vhdx removed: $gone"
    Write-Output "vhdx removed: $gone"
}
Write-Output "selftest exit code: $exit"
exit $exit
