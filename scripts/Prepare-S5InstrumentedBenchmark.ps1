[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Baseline','Changed','TeraCopy')][string]$Implementation,
      [Parameter(Mandatory)][ValidateSet('small','large','mixed','storage','smoke','profile')][string]$Workload,
      [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$RunId,
      [switch]$Serial,[switch]$Metadata)
$ErrorActionPreference='Stop'
if($Serial -and $Implementation -eq 'TeraCopy'){throw 'Serial is a Pathweft queue diagnostic; donor settings remain qualified separately.'}
if($Metadata -and $Workload -ne 'profile'){throw 'Metadata diagnostics use the bounded profile workload.'}
$root='D:\Temp\pathweft-s5-native'
$source=Join-Path $root "workloads\$Workload"
if($Workload -eq 'storage' -and !(Test-Path -LiteralPath $source)){
    & (Join-Path $root 'contracts-build/artifacts/bin/TransferContracts/debug/TransferContracts.exe') --storage-fixture $source
    if($LASTEXITCODE -ne 0){throw 'Native storage fixture creation failed.'}
}
if(!(Test-Path -LiteralPath $source)) {
    [void][IO.Directory]::CreateDirectory($source)
    $random=[Random]::new(5705)
    if($Workload -eq 'large') {
        $buffer=[byte[]]::new(8MB);$random.NextBytes($buffer)
        $stream=[IO.File]::Open((Join-Path $source 'large.bin'),[IO.FileMode]::CreateNew)
        try {for($i=0;$i -lt 64;$i++){$stream.Write($buffer)}}finally{$stream.Dispose()}
    } elseif($Workload -eq 'mixed') {
        [IO.File]::WriteAllBytes((Join-Path $source 'empty.bin'),[byte[]]::new(0))
        $buffer=[byte[]]::new(4096)
        for($i=0;$i -lt 300;$i++){$random.NextBytes($buffer);[IO.File]::WriteAllBytes((Join-Path $source ("small-{0:D5}.bin" -f $i)),$buffer)}
        $buffer=[byte[]]::new(2MB)
        for($i=0;$i -lt 4;$i++){$random.NextBytes($buffer);[IO.File]::WriteAllBytes((Join-Path $source ("medium-{0:D2}.bin" -f $i)),$buffer)}
        $stream=[IO.File]::Open((Join-Path $source 'large.bin'),[IO.FileMode]::CreateNew)
        try {for($i=0;$i -lt 16;$i++){$random.NextBytes($buffer);$stream.Write($buffer)}}finally{$stream.Dispose()}
    } else {
        $count=if($Workload -eq 'small'){3000}elseif($Workload -eq 'profile'){100}else{1}
        $buffer=[byte[]]::new(4096)
        for($i=0;$i -lt $count;$i++){$random.NextBytes($buffer);[IO.File]::WriteAllBytes((Join-Path $source ("item-{0:D5}.bin" -f $i)),$buffer)}
    }
}
$case=Join-Path $root "instrumented-benchmarks\$Implementation-$RunId"
if(Test-Path -LiteralPath $case){throw 'Preserve existing case.'}
$target=Join-Path $case 'target';$temporary=Join-Path $case 'temp';$profile=Join-Path $case 'profile'
foreach($path in @($case,$target,$temporary,$profile)){[void][IO.Directory]::CreateDirectory($path)}
$files=@(Get-ChildItem -LiteralPath $source -File)
foreach($file in $files){$stream=[IO.File]::OpenRead($file.FullName);try{$stream.CopyTo([IO.Stream]::Null)}finally{$stream.Dispose()}}
if($Implementation -eq 'TeraCopy'){
    $program=Join-Path $root 'runtime-4.0.5\TeraCopy.exe';$arguments=@('Copy',$source,$target,'/OverwriteAll','/Close')
    if((Get-FileHash -LiteralPath $program).Hash -ne '1F0CCA0BE4E6A22E856C3B0DE6655A1AF8059A06CC66133398307DE335098C6F'){throw 'Donor identity changed.'}
}else{
    $build=if($Implementation -eq 'Baseline'){'baseline-build'}else{'changed-build'}
    $program=Join-Path $root "$build\artifacts\bin\FileExplorer\debug\Pathweft.exe"
    $request=Join-Path $case 'request.json'
    [ordered]@{Source=$source;Destination=$target;Receipt=(Join-Path $case 'engine.json');Verify=$true;Parallel=(!$Serial)}|ConvertTo-Json|Set-Content -LiteralPath $request -Encoding utf8
    $arguments=@("--benchmark-s5=$request")
}
$config=[ordered]@{temporary=$temporary;profile=$profile;entryReceipt=(Join-Path $case 'entry.json')}|ConvertTo-Json -Compress
$script=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'S5BenchmarkPresentation.js') -Raw).Replace('__CONFIG__',$config)
[IO.File]::WriteAllText((Join-Path $case 'observer.js'),$script)
if($Workload -in @('profile','storage')) {
    $receipt=if($Metadata){'metadata-profile.json'}else{'io-profile.json'}
    $template=if($Metadata){'S5MetadataProfile.js'}else{'S5IoProfile.js'}
    $profileConfig=[ordered]@{source=$source;target=$target;receipt=(Join-Path $case $receipt)}|ConvertTo-Json -Compress
    $profiler=(Get-Content -LiteralPath (Join-Path $PSScriptRoot $template) -Raw).Replace('__CONFIG__',$profileConfig)
    [IO.File]::WriteAllText((Join-Path $case $(if($Metadata){'metadata-profiler.js'}else{'profiler.js'})),$profiler)
}
$assemblySha256=if($Implementation -eq 'TeraCopy'){$null}else{(Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $program) 'Pathweft.dll')).Hash}
[ordered]@{implementation=$Implementation;workload=$Workload;program=$program;arguments=$arguments;source=$source;target=$target;files=$files.Count;bytes=($files|Measure-Object Length -Sum).Sum;filesystem=([IO.DriveInfo]::new('D:\')).DriveFormat;cache='Source sequentially warmed before each invocation; no global eviction; each engine retains its own I/O policy';parallel=(!$Serial);verify='xxHash3-64 enabled in all engines; independent parent SHA-256 excluded from measurements';scriptSha256=(Get-FileHash -LiteralPath (Join-Path $case 'observer.js')).Hash;executableSha256=(Get-FileHash -LiteralPath $program).Hash;assemblySha256=$assemblySha256}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $case 'case.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $case 'case.json')
