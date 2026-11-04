[CmdletBinding()]
param([string]$ScratchRoot='D:\Temp\pathweft-s13-s9-s11\office-fallback')
$ErrorActionPreference='Stop'
trap { Write-Error ($_.Exception.Message+"`n"+$_.ScriptStackTrace) -ErrorAction Continue; exit 1 }
$scratch=[IO.Path]::GetFullPath($ScratchRoot)
if($scratch -ne 'D:\Temp\pathweft-s13-s9-s11\office-fallback'){throw 'Use the registered exact extraction root.'}
$msi='D:\BuildCache\Pathweft\office\libreoffice\archives\LibreOffice_26.2.7_Win_x86-64.msi'
if((Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -ne '480fecf759fc6393bd614fbf43753eb95a8198fc1a065d41b727e3b664cd1e59'){throw 'Official MSI identity differs.'}
$raw=Join-Path $scratch 'cab-files';$runtime=Join-Path $scratch 'runtime'
if((Test-Path -LiteralPath $raw) -or (Test-Path -LiteralPath $runtime)){throw 'Inspect the existing extraction before another attempt.'}
# Read-only Windows Installer database access. No install/admin/custom action,
# registration, repair, MSI execute sequence or system application changes.
$installer=New-Object -ComObject WindowsInstaller.Installer
$database=$installer.OpenDatabase($msi,0)
function Rows([string]$sql,[int]$columns){
    $view=$database.OpenView($sql)
    try{
        [void]$view.Execute()
        while($record=$view.Fetch()){
            $values=@();for($i=1;$i -le $columns;$i++){$values+=$record.StringData($i)}
            ,$values
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record)
        }
    }finally{[void]$view.Close();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}
}
try{
    $directories=@{};foreach($row in Rows 'SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`' 3){$directories[$row[0]]=@($row[1],$row[2])}
    $components=@{};foreach($row in Rows 'SELECT `Component`,`Directory_` FROM `Component`' 2){$components[$row[0]]=$row[1]}
    $files=@(Rows 'SELECT `File`,`Component_`,`FileName`,`FileSize` FROM `File`' 4)
    @{directories=$directories;components=$components;files=$files}|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $scratch 'msi-layout.json')
}finally{
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
}
& 'C:/Users/darka/scoop/shims/7z.exe' x $msi ('-o'+$raw) -y -bd
if($LASTEXITCODE -ne 0){throw 'Read-only MSI CAB extraction failed.'}
function RelativeDirectory([string]$id){
    if($id -eq 'INSTALLLOCATION'){return ''}
    if(!$directories.ContainsKey($id)){return $null}
    $row=$directories[$id];if(!$row[0]){return $null}
    $parent=RelativeDirectory $row[0];if($null -eq $parent){return $null}
    $name=($row[1] -split ':')[0];$name=($name -split '\|')[-1]
    if($name -eq '.'){return $parent}
    if(!$parent){return $name};return Join-Path $parent $name
}
$copied=0
foreach($file in $files){
    $relative=RelativeDirectory $components[$file[1]]
    if($null -eq $relative){continue}
    $name=($file[2] -split '\|')[-1]
    $source=Join-Path $raw $file[0]
    if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw "MSI file member missing: $($file[0])"}
    $target=[IO.Path]::GetFullPath((Join-Path (Join-Path $runtime $relative) $name))
    if(!$target.StartsWith($runtime+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'MSI output path escapes the owned runtime root.'}
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    if(Test-Path -LiteralPath $target){throw "Duplicate MSI output: $target"}
    Move-Item -LiteralPath $source -Destination $target
    $copied++
}
if(!(Test-Path -LiteralPath (Join-Path $runtime 'program/mergedlo.dll'))){throw 'Extracted runtime is missing its document core.'}
@{runtime=$runtime;files=$copied;installed=$false;sourceMsi=$msi}|ConvertTo-Json
