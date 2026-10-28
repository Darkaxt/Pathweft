[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$scratch = [IO.Path]::GetFullPath($ScratchRoot)
if ($scratch -notmatch '^D:\\Temp\\pathweft-s3-[^\\]+$') { throw 'Use registered S3 native scratch.' }
$pin = '2ec219aa18416c6e3b89d6aad3d82d10610f917f'
$files = @('QuickLook.Common/Plugin/IViewer.cs','QuickLook.Common/Plugin/ContextObject.cs',
    'QuickLook.Plugin/QuickLook.Plugin.TextViewer/Plugin.cs',
    'QuickLook.Plugin/QuickLook.Plugin.MarkdownViewer/Plugin.cs',
    'QuickLook.Plugin/QuickLook.Plugin.MarkdownViewer/MarkdownPanel.cs',
    'QuickLook.Plugin/QuickLook.Plugin.ImageViewer/Plugin.cs',
    'QuickLook.Plugin/QuickLook.Plugin.ArchiveViewer/Plugin.cs',
    'QuickLook.Plugin/QuickLook.Plugin.FontViewer/Plugin.cs','QuickLook.Common/LICENSE','LICENSE-GPL.txt')
$root = Join-Path $scratch 'donor'
$manifest = foreach ($file in $files) {
    $path = Join-Path $root $file
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $url = "https://raw.githubusercontent.com/QL-Win/QuickLook/$pin/$file"
    Invoke-WebRequest -Uri $url -OutFile $path
    [ordered]@{ file=$file; commit=$pin; url=$url; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'source-manifest.json') -Encoding utf8
Write-Output "Pinned QuickLook viewer sources retrieved to $root"
