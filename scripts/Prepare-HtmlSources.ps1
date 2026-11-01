[CmdletBinding()]
param([string]$Sources='D:/Temp/pathweft-s8-delivery/corresponding-sources',[string]$Cache='D:/BuildCache/Pathweft/nuget-packages')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vendor=Join-Path $root 'vendor/HTML';[void][IO.Directory]::CreateDirectory($vendor)
$pins=@(
 @{id='HtmlSanitizer';version='9.2.1039';repo='mganss/HtmlSanitizer';revision='0417018d81765e3e15126a65f5a49d71e55f0920';dll='HtmlSanitizer.dll'},
 @{id='AngleSharp';version='1.7.2';repo='AngleSharp/AngleSharp';revision='3718d6804dd3a3781efcd1ccd7bdcb10a4352edd';dll='AngleSharp.dll'},
 @{id='AngleSharp.Css';version='1.0.2';repo='AngleSharp/AngleSharp.Css';revision='d158478f58882dad1d595dc7bb2286f3da4bf6ea';dll='AngleSharp.Css.dll'}
)
$records=@(Get-Content (Join-Path $Sources 'source-manifest.json') -Raw|ConvertFrom-Json)
$runtime=foreach($pin in $pins){
 $package=Join-Path $Cache ($pin.id.ToLowerInvariant()+'/'+$pin.version)
 [xml]$metadata=Get-Content (Join-Path $package ($pin.id.ToLowerInvariant()+'.nuspec'))
 if($metadata.package.metadata.repository.commit -ne $pin.revision){throw ('Pinned NuGet source differs: '+$pin.id)}
 $name=$pin.id+'-'+$pin.version+'-source.zip';$url='https://codeload.github.com/'+$pin.repo+'/zip/'+$pin.revision;$file=Join-Path $Sources $name
 if(!(Test-Path $file)){Invoke-WebRequest $url -OutFile $file}
 $hash=(Get-FileHash $file).Hash;$size=(Get-Item $file).Length
 $old=@($records|Where-Object name -eq $name)
 if($old.Count -and ($old[0].sha256 -ne $hash -or $old[0].revision -ne $pin.revision)){throw 'Preserve changed corresponding source.'}
 if(!$old.Count){$records+=@{name=$name;url=$url;revision=$pin.revision;sha256=$hash;bytes=$size}}
 $zip=[IO.Compression.ZipFile]::OpenRead($file)
 try{
  $license=@($zip.Entries|Where-Object {$_.FullName -match '^[^/]+/(LICENSE|LICENSE\.txt|LICENSE\.md)$'})
  if($license.Count -ne 1){throw ('Inspect source license: '+$pin.id)}
  $reader=[IO.StreamReader]::new($license[0].Open())
  try{[IO.File]::WriteAllText((Join-Path $vendor ($pin.id+'-LICENSE.txt')),$reader.ReadToEnd().Replace("`r`n","`n").TrimEnd()+"`n")}finally{$reader.Dispose()}
 }finally{$zip.Dispose()}
 $asset=@('net10.0','net8.0','netstandard2.0'|Where-Object {Test-Path (Join-Path $package ('lib/'+$_+'/'+$pin.dll))})[0]
 if(!$asset){throw ('Missing compatible parser assembly: '+$pin.id)}
 $dll=Join-Path $package ('lib/'+$asset+'/'+$pin.dll)
 [ordered]@{id=$pin.id;version=$pin.version;targetAsset=$asset;repository='https://github.com/'+$pin.repo;revision=$pin.revision;dll=$pin.dll;sha256=(Get-FileHash $dll).Hash;sourceArchive=$name;sourceSha256=$hash;license='MIT'}
}
$records|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Sources 'source-manifest.json')
[IO.File]::WriteAllText((Join-Path $vendor 'runtime-manifest.json'),($runtime|ConvertTo-Json -Depth 8).Replace("`r`n","`n")+"`n")
Write-Output 'Pinned HTML parser/sanitizer source archives, runtime hashes and MIT notices verified.'
