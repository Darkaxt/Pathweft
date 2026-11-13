param(
 [Parameter(Mandatory)][ValidateSet('ScoopUninstall','RestorePoint')][string]$Action,
 [string]$Package
)
$ErrorActionPreference='Stop'
# The parent assigns the process to its kill-on-close job before allowing any
# Windows query or change. EOF means its owner disappeared before admission.
if([Console]::ReadLine() -ne 'owned'){exit 2}
try {
 switch($Action) {
  'ScoopUninstall' {
   if($Package -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $Package -eq 'scoop'){throw 'Invalid Scoop app name'}
   # Scoop's own uninstaller for the current user's app; Pathweft never deletes the app folder itself.
   $scoop=Get-Command scoop -CommandType ExternalScript,Application -ErrorAction Stop | Select-Object -First 1
   $output=& $scoop.Source uninstall $Package 2>&1 | Out-String
   if($LASTEXITCODE){throw ('Scoop uninstall failed: '+$output.Trim())}
   @{message='Removed by Scoop.'}|ConvertTo-Json -Compress
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
