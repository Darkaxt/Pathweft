[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s2-[^\\]+$') { throw 'Use registered S2 native scratch.' }
$root = Join-Path $scratch 'fixture'
$scope = Join-Path $root 'scope spaced é'
foreach ($folder in @($scope, (Join-Path $root 'destination'), (Join-Path $root 'scope spaced é-other'))) {
    [void][IO.Directory]::CreateDirectory($folder)
}
for ($i = 0; $i -lt 220; $i++) {
    [IO.File]::WriteAllText((Join-Path $scope ('pwfts2_hit_{0:d4}.txt' -f $i)), ('x' * ($i + 1)))
}
[IO.File]::WriteAllText((Join-Path $root 'scope spaced é-other/pwfts2_hit_outside.txt'), 'outside')
[IO.File]::WriteAllText((Join-Path $scope 'pwfts2_évidence space.txt'), 'unicode fixture')
$opened = [IO.Directory]::CreateDirectory((Join-Path $scope 'pwfts2_folder')).FullName
[IO.File]::WriteAllText((Join-Path $opened 'pwfts2_inside.txt'), 'folder open fixture')
Write-Output 'S2 real-index fixtures prepared (220 scoped matches plus outside sibling).'
