[CmdletBinding()]
param([Parameter(Mandatory)][string]$FixtureRoot)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($FixtureRoot)
if (!$root.StartsWith('D:\Temp\pathweft-s5-native\full-volume-',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) { throw 'New, exact owned full-volume fixture required.' }
if (Get-PSDrive -Name P -ErrorAction SilentlyContinue) { throw 'P: is occupied; no existing volume may be changed.' }
$driver='D:\Temp\pathweft-s5-native\contracts-build\artifacts\bin\TransferContracts\debug\TransferContracts.exe'
if (!(Test-Path -LiteralPath $driver -PathType Leaf)) { throw 'Build the current contract driver first.' }
[void][IO.Directory]::CreateDirectory($root)
$image=Join-Path $root 'fixture.vhd'
$setup=Join-Path $root 'create.txt'; $detach=Join-Path $root 'detach.txt'
[IO.File]::WriteAllLines($setup,@("create vdisk file=`"$image`" maximum=96 type=expandable","select vdisk file=`"$image`"",'attach vdisk','create partition primary','format fs=fat32 quick label=PathweftS5','assign letter=P'))
[IO.File]::WriteAllLines($detach,@("select vdisk file=`"$image`"",'detach vdisk'))
$priorTemp=$env:TEMP; $priorTmp=$env:TMP; $priorVolume=$env:PATHWEFT_S5_FULL_VOLUME
$result=1
try {
    $env:TEMP=$env:TMP=$root
    & "$env:WINDIR\System32\diskpart.exe" /s $setup | Set-Content -LiteralPath (Join-Path $root 'create.log')
    if ($LASTEXITCODE -ne 0) { throw 'Owned VHD creation failed; inspect create.log.' }
    $volume=[IO.DriveInfo]::new('P:\')
    if (!$volume.IsReady -or $volume.VolumeLabel -ne 'PathweftS5' -or $volume.DriveFormat -ne 'FAT32' -or $volume.TotalSize -gt 128MB) { throw 'Exact tiny FAT32 fixture qualification failed.' }
    $env:PATHWEFT_S5_FULL_VOLUME='P:\'
    & $driver --full-volume (Join-Path $root 'contracts')
    $result=$LASTEXITCODE
    Get-Content -LiteralPath (Join-Path $root 'contracts/full-volume.json')
}
finally {
    if (Test-Path -LiteralPath $image) {
        & "$env:WINDIR\System32\diskpart.exe" /s $detach | Set-Content -LiteralPath (Join-Path $root 'detach.log')
        $state=Get-DiskImage -ImagePath $image
        if ($state.Attached -or (Test-Path -LiteralPath 'P:\')) { throw 'Owned VHD detachment not verified; retain fixture and diagnose.' }
        [ordered]@{image=$image;attached=$state.Attached;letterAbsent=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'detached.json')
    }
    $env:TEMP=$priorTemp; $env:TMP=$priorTmp; $env:PATHWEFT_S5_FULL_VOLUME=$priorVolume
}
exit $result
