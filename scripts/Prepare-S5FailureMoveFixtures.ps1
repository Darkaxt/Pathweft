[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = 'D:\Temp\pathweft-s5-native\fixtures'
$cross = 'E:\Temp\pathweft-s5-review\transfer-target'
foreach ($directory in @('failure-source','failure-target','same-move-source','same-move-target','cross-move-source')) {
    $path = Join-Path $root $directory
    if (Test-Path -LiteralPath $path) { throw 'Existing scenario must be inspected, not recreated.' }
    [void][IO.Directory]::CreateDirectory($path)
}
if (Test-Path -LiteralPath $cross) { throw 'Existing cross-volume fixture must be preserved.' }
[void][IO.Directory]::CreateDirectory($cross)
$denied = Join-Path $root 'failure-target\denied.txt'
[IO.File]::WriteAllText((Join-Path $root 'failure-source\denied.txt'),'new fixture content')
[IO.File]::WriteAllText($denied,'existing fixture content')
[IO.File]::WriteAllText((Join-Path $root 'same-move-source\move.txt'),'same-volume fixture')
[IO.File]::WriteAllText((Join-Path $root 'cross-move-source\move.txt'),'cross-volume fixture')
$acl = Get-Acl -LiteralPath $denied
$principal = [Security.Principal.WindowsIdentity]::GetCurrent().User
$rule = [Security.AccessControl.FileSystemAccessRule]::new($principal,[Security.AccessControl.FileSystemRights]::WriteData,[Security.AccessControl.AccessControlType]::Deny)
$acl.AddAccessRule($rule)
Set-Acl -LiteralPath $denied -AclObject $acl
[ordered]@{deniedFixture=$denied; principal=$principal.Value; deniedRight='WriteData'; crossTarget=$cross; sourceRoot=$root; userDataModified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'failure-move-setup.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $root 'failure-move-setup.json')
