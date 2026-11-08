param(
 [Parameter(Mandatory)][ValidateSet('ListApps','RemoveApp','RestorePoint')][string]$Action,
 [string]$Package
)
$ErrorActionPreference='Stop'
# The parent assigns the process to its kill-on-close job before allowing any
# Windows query or change. EOF means its owner disappeared before admission.
if([Console]::ReadLine() -ne 'owned'){exit 2}
try {
 switch($Action) {
  'ListApps' {
   $packages=@(Get-AppxPackage | Select-Object -ExpandProperty Name -Unique)
   ConvertTo-Json -InputObject $packages -Compress
  }
  'RemoveApp' {
   if($Package -notmatch '^[A-Za-z0-9][A-Za-z0-9.]+$'){throw 'Invalid package identity'}
   # Exact current-user identity only. Do not remove all-user packages or provisioning.
   $items=@(Get-AppxPackage -Name $Package | Where-Object {$_.Name -ceq $Package})
   if(!$items.Count){throw 'Package is not installed for this user'}
   foreach($item in $items){Remove-AppxPackage -Package $item.PackageFullName -ErrorAction Stop}
   if(@(Get-AppxPackage -Name $Package | Where-Object {$_.Name -ceq $Package}).Count){throw 'Windows still reports the package installed'}
   @{message='Removed for the current user. Reinstall through Microsoft Store if needed.'}|ConvertTo-Json -Compress
  }
  'RestorePoint' {
   $before=@(Get-ComputerRestorePoint | Select-Object -ExpandProperty SequenceNumber)
   Checkpoint-Computer -Description 'Pathweft maintenance' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
   $created=@(Get-ComputerRestorePoint | Where-Object {$_.SequenceNumber -notin $before -and $_.Description -eq 'Pathweft maintenance'})
   if(!$created.Count){throw 'Windows did not create a new restore point. Check System Protection and its restore-point frequency policy.'}
   @{message='Windows restore point created.'}|ConvertTo-Json -Compress
  }
 }
 exit 0
} catch {
 [Console]::Error.WriteLine($_.Exception.Message)
 exit 1
}
