[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$handoff='D:\Tools\CraftTools\maintenance\2026-11-04'
$evidence=Join-Path $PSScriptRoot '../.local/evidence/2026-11-04-specialists'
$expected=@{
    wordcraft=@('0.15.1','08afab50b6a1ed19f9af55bb9f74d18f5e17bfa0','09b04133739ff7dacff50382ecba431d52df877fbcfd98005c69506b0e7c7232')
    gridcraft=@('0.16.0','85e51e009c24fad47575f04d3c08f12342615d82','3056e4d22ab2816c344666a9a65886e8728a8606a65335a8bbb64842981cf50c')
    deckcraft=@('0.15.0','3e178d8f5a59c787a6a53a7ed0f052b3e4a1489f','10a5b5548edbf808b2496cb6115f2ea49e2465059084ea41dafaece3a459f895')
}
$results=@()
foreach($app in Get-Content -LiteralPath (Join-Path $handoff 'staged-office-apps.json') -Raw | ConvertFrom-Json){
    $pin=$expected[$app.repo]
    if(!$pin -or $app.version -ne $pin[0] -or $app.source -ne $pin[1]){throw 'Unexpected candidate identity.'}
    $directory=[IO.Path]::GetFullPath($app.directory)
    if(!$directory.StartsWith('D:\Tools\'+$app.app+'\releases\'+$app.version+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected retained candidate path.'}
    $actual=@(Get-ChildItem -LiteralPath $directory -Recurse -File)
    if($actual.Count -ne $app.files.Count){throw 'Candidate member set changed.'}
    $members=@()
    foreach($file in $app.files){
        $path=[IO.Path]::GetFullPath((Join-Path $directory $file.relative_path))
        if(!$path.StartsWith($directory+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest member escaped candidate root.'}
        $info=Get-Item -LiteralPath $path
        $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if($info.Length -ne $file.bytes -or $hash -ne $file.sha256){throw "Candidate member changed: $path"}
        $members+=@{path=$file.relative_path;bytes=$info.Length;sha256=$hash}
    }
    $signature=Get-AuthenticodeSignature -LiteralPath $app.cli
    $cli=Get-Item -LiteralPath $app.cli
    $cliHash=(Get-FileHash -LiteralPath $app.cli).Hash.ToLowerInvariant()
    if($cliHash -ne $pin[2] -or $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Learning Machines'){throw 'Candidate CLI hash/publisher qualification failed.'}
    $notices=@($members|Where-Object {$_.path -match '(^|\\)(LICENSE|NOTICE|ATTRIBUTION|ASSETS|OFL-)'} )
    $results+=@{app=$app.app;version=$app.version;source=$app.source;release=$app.release_url;directory=$directory;
        cli=$app.cli;cli_sha256=$cliHash;cli_bytes=$cli.Length;portable_bytes=($members|Measure-Object bytes -Sum).Sum;
        cli_and_notice_bytes=$cli.Length+($notices|Measure-Object bytes -Sum).Sum;
        signature=@{status="$($signature.Status)";subject=$signature.SignerCertificate.Subject;thumbprint=$signature.SignerCertificate.Thumbprint};
        members=$members;native_execution_verified=$false}
}
if($results.Count -ne 3){throw 'Three exact candidates required.'}
$report=@{checked_utc=[DateTime]::UtcNow.ToString('O');apps=$results;candidate_only=$true;adopted=$false}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $evidence 'craft-candidates.json')
$results|Select-Object app,version,cli_bytes,portable_bytes,cli_and_notice_bytes,native_execution_verified|ConvertTo-Json
