[CmdletBinding()]
param([Parameter(Mandatory)][string]$CaseRoot,[Parameter(Mandatory)][uint32]$TargetProcessId)
$ErrorActionPreference='Stop'
$case=[IO.Path]::GetFullPath($CaseRoot)
if(!$case.StartsWith('D:\Temp\pathweft-s5-native\instrumented-benchmarks\',[StringComparison]::OrdinalIgnoreCase)){throw 'Owned benchmark case required.'}
$configuration=Get-Content -LiteralPath (Join-Path $case 'case.json') -Raw|ConvertFrom-Json
if((Get-Process -Id $TargetProcessId).Path -ne $configuration.program){throw 'Selected child executable differs.'}
$previousTemp=$env:TEMP;$previousTmp=$env:TMP
try{
    $env:TEMP=$env:TMP=Join-Path $case 'temp'
    Add-Type -Path (Join-Path $PSScriptRoot '../tests/TransferBenchmarkProcess.cs')
    $result=[TransferBenchmarkProcess]::Observe($TargetProcessId,(Join-Path $case 'native-handle-ready.txt'))
    $result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $case 'process.json') -Encoding utf8
    $entry=Get-Content -LiteralPath (Join-Path $case 'entry.json') -Raw|ConvertFrom-Json
    if($entry.pid -ne $TargetProcessId -or $result.ExitCode -ne 0){throw 'Invalid native process/entry result.'}
    $checks=foreach($file in Get-ChildItem -LiteralPath $configuration.source -File){
        $output=Join-Path $configuration.target ((Split-Path -Leaf $configuration.source)+'\'+$file.Name)
        $expected=(Get-FileHash -LiteralPath $file.FullName).Hash;$actual=(Get-FileHash -LiteralPath $output).Hash
        if($expected -ne $actual){throw 'Independent benchmark content mismatch.'}
        [ordered]@{file=$file.Name;bytes=$file.Length;sha256=$actual;sourceAttributes=$file.Attributes.ToString();destinationAttributes=([IO.File]::GetAttributes($output)).ToString();sourceReportedStorageBytes=[TransferBenchmarkProcess]::ReportedStorageBytes($file.FullName);destinationReportedStorageBytes=[TransferBenchmarkProcess]::ReportedStorageBytes($output)}
    }
    if(@(Get-ChildItem -LiteralPath $configuration.target -File -Recurse).Count -ne $configuration.files){throw 'Unexpected output membership.'}
    $limitations=if(Test-Path -LiteralPath (Join-Path $case 'metadata-profiler.js')){'Metadata state probe adds queries; neither timing nor operation counts qualify as performance comparison.'}elseif(Test-Path -LiteralPath (Join-Path $case 'profiler.js')){'Diagnostic file-I/O hooks enabled; timing is not a performance comparison. NT and Win32 counters are separate layers, not additive.'}else{'Same Frida 17.23.2 presentation script for all engines; first observed GetCommandLineW through native exit; earlier initialization/injection excluded; peak memory includes instrumentation; no file-I/O hooks. Local warm-cache samples, not a general speed promise.'}
    $receipt=[ordered]@{case=$configuration;entry=$entry;process=$result;wallMilliseconds=($result.ExitedFileTime-[long]$entry.startedFileTime)/10000.0;cpuMilliseconds=($result.Kernel100ns+$result.User100ns-[long]$entry.kernel100ns-[long]$entry.user100ns)/10000.0;peakWorkingSetBytes=$result.PeakWorkingSetBytes;checks=$checks;limitations=$limitations}
    $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $case 'result.json') -Encoding utf8
    [ordered]@{case=$case;wallMilliseconds=$receipt.wallMilliseconds;cpuMilliseconds=$receipt.cpuMilliseconds;peakWorkingSetBytes=$receipt.peakWorkingSetBytes;files=$configuration.files;bytes=$configuration.bytes}|ConvertTo-Json
}finally{$env:TEMP=$previousTemp;$env:TMP=$previousTmp}
