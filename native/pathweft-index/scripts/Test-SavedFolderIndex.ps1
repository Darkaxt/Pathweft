<#
SPEC IDX-20 check: a folder root's index is saved and answers at once after a restart.

Runs a private console instance (own pipe, config and folder cache, no NTFS volumes,
no content index) twice on a fixture under -Run; it never touches the installed service.

  Round 1: add the fixture as a folder root, wait until it is ready, let the instance exit
           (it saves the folder index at shutdown and after the scan).
  Round 2: restart; the store must answer within a few seconds, the log must say it was
           loaded from the saved index, and its folder sizes must match the fixture.
           Removing the root must delete the saved file.

  Test-SavedFolderIndex.ps1 -Exe pathweft-indexd.exe -Pwindex pwindex.exe -Run D:\Temp\<task>\idx20
Result in -Run\result.json: passed when every check holds.
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Pwindex,
    [Parameter(Mandatory)][string]$Run
)
$ErrorActionPreference = 'Continue'   # native stderr must not throw; failures are checked explicitly
New-Item -ItemType Directory -Force -Path $Run | Out-Null
$fixture = Join-Path $Run 'fixture'
$cache = Join-Path $Run 'folders'
$config = Join-Path $Run 'config.json'
foreach ($p in @($fixture, $cache, $config)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force } }
# 40 folders x 5 files of 1000 bytes, two levels deep.
$expected = 0
for ($i = 0; $i -lt 8; $i++) {
    for ($j = 0; $j -lt 5; $j++) {
        $d = New-Item -ItemType Directory -Force -Path (Join-Path $fixture "a$i\b$j")
        for ($k = 0; $k -lt 5; $k++) { [IO.File]::WriteAllBytes((Join-Path $d.FullName "f$k.bin"), [byte[]]::new(1000)); $expected += 1000 }
    }
}
$checks = [System.Collections.Generic.List[object]]::new()
function Check([bool]$ok, [string]$what) { $checks.Add([pscustomobject]@{ passed = $ok; check = $what }); Write-Output ("{0} {1}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' }), $what)) }

function Start-Instance([string]$name, [int]$seconds) {
    $pipe = "pwidx20-$name-$PID"
    $log = Join-Path $Run "$name.log"
    $p = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden -RedirectStandardError $log -RedirectStandardOutput (Join-Path $Run "$name.out") `
        -ArgumentList @('console', '--pipe', $pipe, '--config', "`"$config`"", '--volumes', '""', '--folder-cache', "`"$cache`"", '--exit-after', "$seconds")
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 200; & $Pwindex --pipe $pipe status *> $null } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $deadline)
    [pscustomobject]@{ Process = $p; Pipe = $pipe; Log = $log }
}
function Store([string]$pipe) {
    $json = & $Pwindex --pipe $pipe status --json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { return $null }
    (($json -join "`n") | ConvertFrom-Json).stores | Where-Object { $_.kind -ne 'ntfs' } | Select-Object -First 1
}
function Size([string]$pipe) {
    $line = & $Pwindex --pipe $pipe sizes $fixture 2>$null | Select-Object -First 1
    if ($line -match "`t(\d+)$") { [long]$Matches[1] } else { -1 }
}

# Round 1: index the fixture, then shut down (saves).
$one = Start-Instance 'first' 30
& $Pwindex --pipe $one.Pipe folder add $fixture | Out-Null
Check ($LASTEXITCODE -eq 0) 'folder add on the private instance'
$deadline = (Get-Date).AddSeconds(25)
do { Start-Sleep -Milliseconds 250; $s = Store $one.Pipe } while (($null -eq $s -or $s.state -ne 'ready') -and (Get-Date) -lt $deadline)
Check ($null -ne $s -and $s.state -eq 'ready') "first scan ready ($($s.state))"
Check ((Size $one.Pipe) -eq $expected) 'first instance answers the fixture total'
$one.Process.WaitForExit(60000) | Out-Null
$saved = @(Get-ChildItem -LiteralPath $cache -Filter '*.pwfs' -ErrorAction SilentlyContinue)
Check ($saved.Count -eq 1) "one saved folder index after shutdown ($($saved.Count))"
Check ((Get-Content -LiteralPath $one.Log -Raw) -match 'saved the index') 'the first instance logs that it saved the index'

# Round 2: restart; the saved tree answers at once.
$started = Get-Date
$two = Start-Instance 'second' 30
$deadline = (Get-Date).AddSeconds(5)
do { $s = Store $two.Pipe; $size = Size $two.Pipe; if ($size -eq $expected) { break }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
$answered = ((Get-Date) - $started).TotalMilliseconds
Check ($size -eq $expected) "the restarted instance answers the fixture total from the saved index within 5 s ($([int]$answered) ms after start, state $($s.state))"
Check ($s.state -in @('ready', 'scanning')) "the restarted store is ready or rescanning, never loading ($($s.state))"
Start-Sleep -Milliseconds 500
Check ((Get-Content -LiteralPath $two.Log -Raw) -match 'loaded \d+ entries from the saved index') 'the restarted instance logs that it loaded the saved index'
& $Pwindex --pipe $two.Pipe folder remove $fixture | Out-Null
Start-Sleep -Milliseconds 500
$left = @(Get-ChildItem -LiteralPath $cache -Filter '*.pwfs' -ErrorAction SilentlyContinue)
Check ($LASTEXITCODE -eq 0 -and $left.Count -eq 0) "removing the root deletes its saved index ($($left.Count) left)"
$two.Process.WaitForExit(60000) | Out-Null

$passed = -not ($checks | Where-Object { -not $_.passed })
[pscustomobject]@{ passed = $passed; expected = $expected; checks = $checks } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Run 'result.json') -Encoding UTF8
if ($passed) { exit 0 } else { exit 1 }
