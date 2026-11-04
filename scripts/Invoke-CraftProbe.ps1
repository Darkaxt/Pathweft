#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('wordcraft','gridcraft','deckcraft')][string]$App,
    [Parameter(Mandatory)][ValidateSet('info','render','convert','help')][string]$Operation,
    [string]$InputName,
    [string]$Sheet,
    [int]$Page=1,
    [string]$Label
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $repo '.local/evidence/2026-11-04-specialists'
$record=Get-Content -LiteralPath (Join-Path $evidence 'craft-candidates.json') -Raw | ConvertFrom-Json
$candidate=$record.apps|Where-Object {$_.app -eq $App}
if(!$candidate){throw 'Reinspect exact staged candidates before qualification.'}
$scratch=Join-Path 'D:\Temp\pathweft-s13-s9-s11' ('craft-'+$App)
[void][IO.Directory]::CreateDirectory($scratch)
if(!$Label){$Label=$Operation+'-'+$Page}
if($Label -notmatch '^[a-z0-9-]+$'){throw 'Simple exact probe label required.'}
$output=Join-Path $evidence ('craft-'+$App+'-'+$Label+'.log')
if(Test-Path -LiteralPath $output){throw 'Do not overwrite an existing diagnostic result; use a new label.'}
$native=@()
if($Operation -eq 'help'){$native=@('--help')}
else{
    if(!$InputName -or [IO.Path]::GetFileName($InputName) -ne $InputName){throw 'An exact retained fixture basename is required.'}
    $source=Join-Path (Join-Path $evidence 'inputs') $InputName
    $stagedInput=Join-Path $scratch $InputName
    if(!(Test-Path -LiteralPath $stagedInput)){Copy-Item -LiteralPath $source -Destination $stagedInput}
    $before=(Get-FileHash -LiteralPath $source).Hash
    if((Get-FileHash -LiteralPath $stagedInput).Hash -ne $before){throw 'Staged input differs from retained source.'}
    if($Operation -eq 'info'){
        $native=@('info',$stagedInput)
        if($App -eq 'gridcraft'){$native+='--json'}
    }elseif($Operation -eq 'convert'){
        $native=@('convert',$stagedInput,(Join-Path $scratch ($Label+'.pdf')))
        if($Sheet){$native+=@('--sheet',$Sheet)}
    }elseif($App -eq 'wordcraft'){
        $native=@('render',$stagedInput,(Join-Path $scratch ($Label+'.png')),'--page',"$Page",'--scale','1')
    }elseif($App -eq 'deckcraft'){
        $native=@('render',$stagedInput,'--all','--scale','1',(Join-Path $scratch $Label))
    }else{throw 'GridCraft uses its actual PDF export; it has no headless image-render command.'}
}
$helper=Join-Path $env:USERPROFILE ('.agents/skills/'+$App+'/scripts/invoke_native_gate.py')
$arguments=@('-B',$helper,'exec','--tool',$App,'--candidate-executable',$candidate.cli,
    '--candidate-sha256',$candidate.cli_sha256,'--scratch-root',$scratch,
    '--owner-task',('Pathweft S13 Office qualification: '+$App+' '+$Label),
    '--thread-name','Pathweft',
    '--')+$native
$previousRegion=$env:WORDCRAFT_REGION
try{
    $env:WORDCRAFT_REGION='en-GB'
    $result=& python @arguments 2>&1
    $nativeExit=$LASTEXITCODE
}finally{$env:WORDCRAFT_REGION=$previousRegion}
$result|Set-Content -LiteralPath $output
if($Operation -ne 'help' -and ((Get-FileHash -LiteralPath $source).Hash -ne $before -or (Get-FileHash -LiteralPath $stagedInput).Hash -ne $before)){throw 'Native qualification altered its input.'}
Write-Output ($result -join "`n")
Write-Output ('Retained gate/native output: '+$output)
exit $nativeExit
