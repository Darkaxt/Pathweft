[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot,[switch]$Verify,[string]$ProductDirectory,[switch]$RevealProbe,[switch]$ProgressReceiver,[switch]$MenuDesktop)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$destination=[IO.Path]::GetFullPath($OutputRoot)
if($destination -notmatch '^D:\\Temp\\[^\\]+\\'){throw 'Native compiler output requires exact registered D: scratch.'}
$vs=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'The Windows C++ toolchain is required for the Shell boundary.'}
$vc=Join-Path $vs ('VC/Tools/MSVC/'+(Get-Content (Join-Path $vs 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim())
$sdk='C:/Program Files (x86)/Windows Kits/10'
$sdkVersion='10.0.26100.0'
if($ProductDirectory -and !$Verify){throw 'Delivered native binaries are consumed only by verification.'}
foreach($arch in @('x64','x86')){
    $output=Join-Path $destination $arch
    [void][IO.Directory]::CreateDirectory($output)
    $arguments=@('/nologo','/MT','/O2','/EHsc','/W4','/WX','/std:c++17','/DUNICODE','/D_UNICODE',('/I'+(Join-Path $vc 'include')))
    foreach($kind in @('shared','um','ucrt','winrt')){$arguments+='/I'+(Join-Path $sdk "Include/$sdkVersion/$kind")}
    $link=@('/link',('/LIBPATH:'+(Join-Path $vc "lib/$arch")))
    foreach($kind in @('um','ucrt')){$link+='/LIBPATH:'+(Join-Path $sdk "Lib/$sdkVersion/$kind/$arch")}
    $link+=@('kernel32.lib','ole32.lib','oleaut32.lib','shell32.lib','shlwapi.lib','uuid.lib','advapi32.lib','user32.lib')
    if($MenuDesktop -and $arch -eq 'x64'){
        $test=@((Join-Path $root 'tests/MenuDesktop.cpp'),('/Fo'+$output+'\'))+$link+@(('/OUT:'+(Join-Path $output 'MenuDesktop.exe')),'/SUBSYSTEM:CONSOLE')
        & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @test
        if($LASTEXITCODE -ne 0){throw 'Private menu desktop fixture compilation failed.'}
        # Shell Open may delegate process creation; this finite receiver creates
        # no windows and has no console even if Windows selects another desktop.
        $receiver=@((Join-Path $root 'tests/MenuDesktop.cpp'),('/Fo'+$output+'\'))+$link+@(('/OUT:'+(Join-Path $output 'MenuOpenProbe.exe')),'/SUBSYSTEM:WINDOWS','/ENTRY:wmainCRTStartup')
        & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @receiver
        if($LASTEXITCODE -ne 0){throw 'Windowless Shell-open fixture compilation failed.'}
    }
    if($ProgressReceiver){
        $test=@((Join-Path $root 'tests/InstallerProgressReceiver.cpp'),('/Fo'+$output+'\'))+$link+@(('/OUT:'+(Join-Path $output 'InstallerProgressReceiver.exe')),'/SUBSYSTEM:WINDOWS')
        & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @test
        if($LASTEXITCODE -ne 0){throw "Native $arch installer fixture compilation failed."}
    }
    if($RevealProbe){
        $test=@((Join-Path $root 'tests/ShellRevealProbe.cpp'),('/Fo'+$output+'\'))+$link+@(('/OUT:'+(Join-Path $output 'ShellRevealProbe.exe')),'/SUBSYSTEM:CONSOLE')
        & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @test
        if($LASTEXITCODE -ne 0){throw "Native $arch reveal diagnostic compilation failed."}
    }
    $dll=@('/LD',(Join-Path $root 'native/pathweft-shell/Router.cpp'),('/Fo'+$output+'\'))+$link+@(('/DEF:'+(Join-Path $root 'native/pathweft-shell/Router.def')),('/OUT:'+(Join-Path $output 'PathweftShell.dll')),('/IMPLIB:'+(Join-Path $output 'PathweftShell.lib')))
    if($ProductDirectory){
        $manifest=Get-Content (Join-Path $ProductDirectory 'release-manifest.json') -Raw|ConvertFrom-Json
        $name="PathweftShell-$($manifest.version)-$arch.dll"
        $member=@($manifest.files|Where-Object path -eq $name)
        if($member.Count -ne 1 -or (Get-FileHash (Join-Path $ProductDirectory $name)).Hash -ne $member[0].sha256){throw 'Delivered native Shell identity differs.'}
        Copy-Item (Join-Path $ProductDirectory $name) -Destination (Join-Path $output 'PathweftShell.dll')
    }else{
        & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @dll
        if($LASTEXITCODE -ne 0){throw "Native $arch compilation failed."}
    }
    if($Verify){
        foreach($unit in @(@{source='ShellRouterProbe';output='ShellRouterProbe';subsystem='CONSOLE'},@{source='ShellRouterReceiver';output='Pathweft';subsystem='WINDOWS'})){
            $test=@((Join-Path $root "tests/$($unit.source).cpp"),('/Fo'+$output+'\'))+$link+@(('/OUT:'+(Join-Path $output ($unit.output+'.exe'))),('/SUBSYSTEM:'+$unit.subsystem))
            & (Join-Path $vc "bin/Hostx64/$arch/cl.exe") @arguments @test
            if($LASTEXITCODE -ne 0){throw "Native $arch test compilation failed."}
        }
        $fixture=Join-Path $output ('folder spaced '+[char]0xE9+' '+[char]0x5B50)
        [void][IO.Directory]::CreateDirectory($fixture)
        $probe=Start-Process -FilePath (Join-Path $output 'ShellRouterProbe.exe') -ArgumentList @(('"'+(Join-Path $output 'PathweftShell.dll')+'"'),('"'+$fixture+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'probe.stdout.txt') -RedirectStandardError (Join-Path $output 'probe.stderr.txt')
        $probe.WaitForExit();$probe.Refresh()
        Get-Content (Join-Path $output 'probe.stdout.txt')
        if($probe.ExitCode -ne 0){throw "Native $arch browser/launch verification failed: $($probe.ExitCode)"}
        if([IO.File]::ReadAllText((Join-Path $output 'received.txt'),[Text.Encoding]::Unicode) -ne [IO.Path]::GetPathRoot($fixture)){throw 'External drive launch lost its trailing separator.'}
        $probe.Dispose()
        if($RevealProbe){
            [IO.File]::WriteAllText((Join-Path $fixture 'selected.txt'),'Native Shell selection fixture.')
            $reveal=Start-Process -FilePath (Join-Path $output 'ShellRevealProbe.exe') -ArgumentList @(('"'+(Join-Path $output 'PathweftShell.dll')+'"'),('"'+$fixture+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'reveal.stdout.txt') -RedirectStandardError (Join-Path $output 'reveal.stderr.txt')
            $reveal.WaitForExit();$reveal.Refresh();Get-Content (Join-Path $output 'reveal.stdout.txt')
            if($reveal.ExitCode -ne 0){throw "Delivered $arch native Shell selection failed: $($reveal.ExitCode)"}
            $reveal.Dispose()
        }
    }
}
