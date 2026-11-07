[CmdletBinding()]
param([Parameter(Mandatory)][string]$Assets,[string]$CacheRoot='D:/BuildCache/Pathweft')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$vendor=Join-Path $repo 'vendor/Performance'
$cache=Join-Path $CacheRoot 'performance'
[void][IO.Directory]::CreateDirectory($vendor);[void][IO.Directory]::CreateDirectory($cache)
$graph=Get-Content $Assets -Raw|ConvertFrom-Json -AsHashtable
$pins=@(
 @{id='Microsoft.Diagnostics.Tracing.TraceEvent';version='4.0.0';repo='microsoft/perfview';commit='10e5d271cebcdf5d6cf122c6ab9c863860a7a2f1'},
 @{id='Microsoft.Diagnostics.NETCore.Client';version='0.2.661903';repo='dotnet/diagnostics';commit='d7b455b46332b31fd9ba3a3f3e020387984c511a'},
 @{id='Microsoft.Extensions.Logging.Abstractions';version='8.0.3';repo='dotnet/runtime';commit='eba546b0f0d448e0176a2222548fd7a2fbf464c0'},
 @{id='Microsoft.Extensions.DependencyInjection.Abstractions';version='8.0.2';repo='dotnet/runtime';commit='81cabf2857a01351e5ab578947c7403a5b128ad1'},
 @{id='System.Reflection.TypeExtensions';version='4.7.0';repo=$null;commit=$null}
)
$archives=@();$packages=@()
foreach($pin in $pins){
 $key=$pin.id+'/'+$pin.version
 if(!$graph.libraries.ContainsKey($key)){throw "Selected package missing: $key"}
 $packageRoot=Join-Path $CacheRoot ('nuget-packages/'+$key.ToLowerInvariant())
 [xml]$metadata=Get-Content (Join-Path $packageRoot ($pin.id.ToLowerInvariant()+'.nuspec'))
 if($pin.commit -and $metadata.package.metadata.repository.commit -and $metadata.package.metadata.repository.commit -ne $pin.commit){throw "Package source pin differs: $key"}
 $name=($pin.id+'.'+$pin.version+'.nupkg').ToLowerInvariant();$package=Join-Path $packageRoot $name
 Copy-Item $package $cache
 $packages+=@{id=$pin.id;version=$pin.version;license='MIT';packageArchive=$name;packageSha256=(Get-FileHash $package).Hash;repository=$pin.repo;revision=$pin.commit}
 if(!$pin.commit){continue} # Framework-forwarded TypeExtensions ships through .NET10.0.14, not as an extra assembly.
 $name=$pin.id+'-'+$pin.commit+'-source.zip';$file=Join-Path $cache $name
 $url='https://codeload.github.com/'+$pin.repo+'/zip/'+$pin.commit
 if(!(Test-Path $file)){Invoke-WebRequest $url -OutFile $file}
 $zip=[IO.Compression.ZipFile]::OpenRead($file)
 try{
  $license=@($zip.Entries|Where-Object FullName -Match '^[^/]+/LICENSE(\.TXT|\.md|\.txt)?$')
  if($license.Count -ne 1){throw "Inspect source license: $key"}
  $reader=[IO.StreamReader]::new($license[0].Open())
  try{[IO.File]::WriteAllText((Join-Path $vendor ($pin.id+'-LICENSE.txt')),$reader.ReadToEnd())}finally{$reader.Dispose()}
 }finally{$zip.Dispose()}
 $archives+=@{name=$name;url=$url;revision=$pin.commit;sha256=(Get-FileHash $file).Hash;bytes=(Get-Item $file).Length}
}
$existing=Join-Path $vendor 'source-manifest.json'
$manifest=@{packages=$packages;archives=$archives;graph=$graph.targets}
if(Test-Path $existing){
 $old=Get-Content $existing -Raw|ConvertFrom-Json
 foreach($archive in $archives){$prior=$old.archives|Where-Object name -eq $archive.name;if($prior -and $prior.sha256 -ne $archive.sha256){throw 'Preserve changed source archive'}}
}
[IO.File]::WriteAllText($existing,($manifest|ConvertTo-Json -Depth 30).Replace("`r`n","`n")+"`n")
$manifest|Select-Object packages,archives|ConvertTo-Json -Depth 5
