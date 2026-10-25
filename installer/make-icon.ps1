<#
    app.ico since 24.10.2026 is the studio's new File Labs icon (variant "c1": a dark folder with two panes of
    list lines on a pink → orange tile, full bleed so it reads at 24 px in a 4K taskbar). It is drawn from SVG in
    G:\Claude Projects\Marketing\icons\filelabs-new\v8 (c1-margin40.svg for 40 px and up, c1-small.svg for 16–32 px)
    and assembled there as filelabs.ico. This script copies that file in; the build picks up app.ico.
#>
Copy-Item 'G:\Claude Projects\Marketing\icons\filelabs-new\v8\filelabs.ico' (Join-Path $PSScriptRoot '..\app.ico') -Force
