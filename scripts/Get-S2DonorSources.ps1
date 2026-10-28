[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s2-[^\\]+$') { throw 'Use registered S2 native scratch.' }
$pin = 'a4379f181ea8e9b4d3ff8b0e5178d8d39491830c'
$files = @('EverythingToolbar/Services/TaskbarLayoutProbe.cs', 'EverythingToolbar/Services/WindowsPolicy.cs',
    'EverythingToolbar/TaskbarWindow.xaml.cs', 'EverythingToolbar/Controls/ToolbarControl.xaml.cs',
    'EverythingToolbar/Controls/ToolbarControl.xaml', 'EverythingToolbar/Controls/SearchBox.xaml.cs',
    'EverythingToolbar/SearchWindow.xaml.cs', 'EverythingToolbar.Launcher/TaskbarWindowHost.cs',
    'EverythingToolbar.Launcher/Launcher.cs', 'LICENSE')
$root = Join-Path $scratch 'donor'
$manifest = foreach ($file in $files) {
    $path = Join-Path $root $file
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $url = "https://raw.githubusercontent.com/srwi/EverythingToolbar/$pin/$file"
    Invoke-WebRequest -Uri $url -OutFile $path
    [ordered]@{ file=$file; commit=$pin; url=$url; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Encoding utf8
Write-Output "Pinned donor sources retrieved to $root"
