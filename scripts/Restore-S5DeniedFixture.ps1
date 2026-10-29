[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$path = 'D:\Temp\pathweft-s5-native\fixtures\failure-target\denied.txt'
$principal = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = Get-Acl -LiteralPath $path
$rule = [Security.AccessControl.FileSystemAccessRule]::new($principal,[Security.AccessControl.FileSystemRights]::WriteData,[Security.AccessControl.AccessControlType]::Deny)
$acl.RemoveAccessRuleSpecific($rule)
Set-Acl -LiteralPath $path -AclObject $acl
Write-Output 'Removed only the task-created WriteData deny rule from the exact disposable fixture.'
