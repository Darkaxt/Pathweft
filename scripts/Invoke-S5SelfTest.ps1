[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Join-Path 'D:\Temp\pathweft-s5-native' ('release-selftest-'+[guid]::NewGuid().ToString('N'))
$temporary=Join-Path $root 'temp';$profile=Join-Path $root 'profile'
foreach($path in @($root,$temporary,$profile)){[void][IO.Directory]::CreateDirectory($path)}
$executable='D:\Temp\pathweft-s5-native\release-build\artifacts\bin\FileExplorer\release\Pathweft.exe'
$start=[Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.ArgumentList.Add('--selftest')
$start.Environment['TEMP']=$temporary;$start.Environment['TMP']=$temporary;$start.Environment['PATHWEFT_PROFILE']=$profile
$process=[Diagnostics.Process]::Start($start)
$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
[IO.File]::WriteAllText((Join-Path $root 'stdout.txt'),$stdout.GetAwaiter().GetResult())
[IO.File]::WriteAllText((Join-Path $root 'stderr.txt'),$stderr.GetAwaiter().GetResult())
[ordered]@{pid=$process.Id;exitCode=$process.ExitCode;assemblySha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $executable) 'Pathweft.dll')).Hash;root=$root}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding utf8
Write-Output "Release selftest: $root\result.json"
Get-Content -LiteralPath (Join-Path $root 'stdout.txt')
if(Test-Path -LiteralPath (Join-Path $temporary 'pathweft-selftest.txt')){Get-Content -LiteralPath (Join-Path $temporary 'pathweft-selftest.txt')}
exit $process.ExitCode
