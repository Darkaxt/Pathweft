<# Reuse a retained, checksum-verified dependency source delivery. Existing
   staging members are preserved and must match; the packaging manifest checks
   still qualify every selected current dependency before final publication. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DeliveryRoot,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$target=[IO.Path]::GetFullPath($Destination)
if($target -notmatch '^D:\\Temp\\[^\\]+\\corresponding-sources$'){throw 'Use exact native/build source staging scratch.'}
$archives=@(Get-ChildItem -LiteralPath $DeliveryRoot -Filter '*-dependency-sources.zip' -File)
if($archives.Count -ne 1){throw 'Exactly one retained source delivery required.'}
$archive=$archives[0]
$line=@(Get-Content -LiteralPath (Join-Path $DeliveryRoot 'SHA256SUMS.txt')|Where-Object {$_ -match ('^[a-fA-F0-9]{64}  '+[regex]::Escape($archive.Name)+'$')})
if($line.Count -ne 1 -or (Get-FileHash -LiteralPath $archive.FullName).Hash -ne $line[0].Substring(0,64)){throw 'Retained source delivery checksum differs.'}
$helper='C:/Users/darka/.agents/skills/transactional-cleanup/scripts/invoke_transactional_cleanup.py'
$raw=& python $helper begin --workspace (Split-Path -Parent $PSScriptRoot) --scan-root $target
if($LASTEXITCODE -ne 0){throw 'Source staging inventory failed.'}
$transaction=($raw -join "`n"|ConvertFrom-Json).transaction_id
$raw=& python $helper register --transaction $transaction --path $target --kind compiler-output --evidence ('Verified source staging from retained '+$archive.FullName+'; active final-package consumer')
if($LASTEXITCODE -ne 0){throw 'Source staging registration failed.'}
Write-Output ('Cleanup transaction: '+$transaction)
$count=0
$zip=[IO.Compression.ZipFile]::OpenRead($archive.FullName)
try{
    foreach($entry in $zip.Entries){
        $path=[IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
        if($path -eq $target){continue}
        if(!$path.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Source archive entry escapes staging.'}
        if($entry.FullName.EndsWith('/')){[void][IO.Directory]::CreateDirectory($path);continue}
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        if(Test-Path -LiteralPath $path){
            $input=$entry.Open()
            try{$expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input))}finally{$input.Dispose()}
            if((Get-FileHash -LiteralPath $path).Hash -ne $expected){throw ('Existing source staging differs: '+$entry.FullName)}
        }else{
            $input=$entry.Open();$output=[IO.File]::Create($path)
            try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
        }
        $count++
    }
}finally{$zip.Dispose()}
if(!(Test-Path -LiteralPath (Join-Path $target 'source-manifest.json'))){throw 'Source root manifest is missing.'}
@{source=$archive.FullName;destination=$target;members=$count;transaction=$transaction}|ConvertTo-Json
