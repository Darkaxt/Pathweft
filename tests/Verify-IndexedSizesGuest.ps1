param()
$ErrorActionPreference='Stop'
$work='C:\Coordinator\Work'
trap{[IO.File]::WriteAllText((Join-Path $work 'script-error.txt'),($_|Out-String)+$_.ScriptStackTrace);exit 1}
if(!(Test-Path -LiteralPath $work) -or !(Test-Path -LiteralPath 'C:\PathweftApp\Pathweft.exe')){throw 'Exact owned guest mappings required'}
$root=Join-Path $work 'trial';$profile=Join-Path $root 'profile';$temp=Join-Path $root 'temp'
foreach($path in @($root,$profile,$temp)){[void][IO.Directory]::CreateDirectory($path)}
[IO.File]::WriteAllLines((Join-Path $profile 'settings.txt'),@('theme.native=1','theme.mode=dark','animations=0','tray=0','wine=0','taskbarsearch=0','preview=0','stats=0',"tabs.Left=$root","tabs.Right=$root"))
$start=[Diagnostics.ProcessStartInfo]::new('C:\PathweftApp\Pathweft.exe')
$start.Arguments='--verify-s8-appearance='+$root
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden';$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.EnvironmentVariables['PATHWEFT_PROFILE']=$profile;$start.EnvironmentVariables['PATHWEFT_TEST_HIDDEN']='1'
$start.EnvironmentVariables['PATHWEFT_VERIFY_SPECIALISTS13']='1';$start.EnvironmentVariables['PATHWEFT_SPECIALIST_SCENARIO']='indexed-sizes';$start.EnvironmentVariables['PATHWEFT_VERIFY_SANDBOX']='1'
$start.EnvironmentVariables['DOTNET_ROOT']='C:\Dotnet';$start.EnvironmentVariables['DOTNET_ROOT_X64']='C:\Dotnet'
$start.EnvironmentVariables['TEMP']=$temp;$start.EnvironmentVariables['TMP']=$temp
$process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.WaitForExit()
[IO.File]::WriteAllText((Join-Path $work 'stdout.txt'),$stdout.GetAwaiter().GetResult());[IO.File]::WriteAllText((Join-Path $work 'stderr.txt'),$stderr.GetAwaiter().GetResult())
if(!(Test-Path -LiteralPath (Join-Path $root 'results.json'))){throw ('Original guest app exited '+$process.ExitCode+' without a receipt')}
$result=Get-Content (Join-Path $root 'results.json') -Raw|ConvertFrom-Json
if($result.pid -ne $process.Id -or $result.result -ne $process.ExitCode){throw 'Guest process/receipt identity mismatch'}
$result|ConvertTo-Json -Depth 10
if($process.ExitCode -ne 0){throw 'Actual guest app qualification failed'}
