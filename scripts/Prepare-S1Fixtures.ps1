[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\[^\\]+$') { throw 'Use an exact task-owned D:\Temp build/fixture root.' }
$fixture = Join-Path $scratch 'fixture'
$scope = Join-Path $fixture 'scope spaced é'
$nested = Join-Path $scope '子'
$outside = Join-Path $fixture 'scope spaced é-other'
$destination = Join-Path $fixture 'destination'
foreach ($folder in @($scope, $nested, $outside, $destination, (Join-Path $fixture 'large'))) {
    [void][IO.Directory]::CreateDirectory($folder)
}
for ($i = 0; $i -lt 620; $i++) {
    $name = 'pwfts1_hit_{0:d4}.txt' -f $i
    $parent = if ($i -lt 310) { $scope } else { $nested }
    [IO.File]::WriteAllText((Join-Path $parent $name), ('x' * ($i + 1)))
}
[IO.File]::WriteAllText((Join-Path $scope 'pwfts1_évidence space.txt'), 'unicode fixture')
[IO.File]::WriteAllText((Join-Path $outside 'pwfts1_hit_outside.txt'), 'outside scope')
[IO.File]::WriteAllText((Join-Path $scope 'pwfts1_clipboard.txt'), 'clipboard copy')
[IO.File]::WriteAllText((Join-Path $scope 'pwfts1_drag.txt'), 'drag copy')
[IO.File]::WriteAllText((Join-Path $scope 'pwfts1_ole_drag.txt'), 'native OLE drag fixture')
[IO.File]::WriteAllText((Join-Path $scope 'pwfts1_removed.txt'), 'delete-after-query fixture')
for ($i = 0; $i -lt 2400; $i++) {
    [IO.File]::WriteAllText((Join-Path $fixture ('large\item_{0:d4}.txt' -f $i)), 'listing fixture')
}
[ordered]@{ fixture=$fixture; scope=$scope; pagedMatches=620; outside=$outside; largeListing=2400 } | ConvertTo-Json
