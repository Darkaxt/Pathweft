; Protected machine installation. Build via build.ps1; all paths are explicit inputs.
#define AppName "Pathweft"
#define AppExe "Pathweft.exe"
#ifndef AppIdGuid
  #define AppIdGuid "{A52C677C-5F24-45E2-B814-66DCAABFB71A}"
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishRoot
  #error PublishRoot is required
#endif
#ifndef DeliveryRoot
  #error DeliveryRoot is required
#endif

[Setup]
AppId={{#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Darkaxt
VersionInfoVersion={#AppVersion}
#ifdef ProgressVerification
; The isolated progress fixture never registers a machine-wide shell class.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Pathweft
#else
PrivilegesRequired=admin
DefaultDirName={autopf}\Pathweft
#endif
UsePreviousAppDir=no
DefaultGroupName=Pathweft
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Pathweft
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
OutputDir={#DeliveryRoot}
OutputBaseFilename=Pathweft-{#AppVersion}-setup
SetupIconFile=..\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\COPYING-GPL-3.0.txt

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "defaultfm"; Description: "Open filesystem folders and drives in Pathweft"; GroupDescription: "Windows integration (optional):"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Excludes: "PathweftShell-*.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
; Versioned native handlers can coexist with a prior version still loaded in a
; Shell caller. Uninstall never force-closes that caller to remove its DLL.
Source: "{#PublishRoot}\PathweftShell-*.dll"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete

[Registry]
#ifndef ProgressVerification
; Packaged desktop callers cannot see the per-user CLSID but can see the
; machine class. The DLL and adjacent executable live under protected {app}.
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\PathweftShell-{#AppVersion}-x64.dll"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\PathweftShell-{#AppVersion}-x86.dll"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
#endif

[Icons]
Name: "{group}\Pathweft"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Darkaxt.Pathweft"
Name: "{autodesktop}\Pathweft"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Darkaxt.Pathweft"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--desktop-launch"; Description: "Launch Pathweft"; Flags: nowait postinstall skipifsilent

; User settings, tabs, history, caches and recovery records are never swept by
; the uninstaller. The executable restores only registrations it still owns.
[Code]
type
  TCreationTime = record Low, High: LongWord; end;
function OpenProcess(Access: LongWord; Inherit: Boolean; Pid: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetProcessTimes(Handle: THandle; var Created, Exited, Kernel, User: TCreationTime): Boolean;
  external 'GetProcessTimes@kernel32.dll stdcall';

function RestartRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RESTARTAPP|0}') = '1';
end;

function MigrationRequested(): Boolean;
begin
  Result := ExpandConstant('{param:MIGRATE|0}') = '1';
end;

function MachineClassKey(): String;
begin
  Result := 'Software\Classes\CLSID\{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}';
end;

function MachineClassOwned(Root: Integer; const Architecture: String; const Directory: String): Boolean;
var
  Existing: String;
begin
  Result := not RegQueryStringValue(Root, MachineClassKey() + '\InprocServer32', '', Existing);
  if not Result then
    Result := (Pos(Uppercase(Directory + '\PathweftShell-'), Uppercase(Existing)) = 1) and
      (Pos(Uppercase('-' + Architecture + '.dll'), Uppercase(Existing)) = Length(Existing) - Length(Architecture) - 4);
end;

procedure RemoveOwnedMachineClass(Root: Integer; const Architecture: String);
var
  Key, Existing: String;
begin
  Key := MachineClassKey() + '\InprocServer32';
  if RegQueryStringValue(Root, Key, '', Existing) and
     (CompareText(Existing, ExpandConstant('{app}\PathweftShell-{#AppVersion}-' + Architecture + '.dll')) = 0) then begin
    RegDeleteValue(Root, Key, '');
    if RegQueryStringValue(Root, Key, 'ThreadingModel', Existing) and (CompareText(Existing, 'Apartment') = 0) then
      RegDeleteValue(Root, Key, 'ThreadingModel');
    RegDeleteKeyIfEmpty(Root, Key);
    RegDeleteKeyIfEmpty(Root, MachineClassKey());
  end;
end;

#include "UpdateProgress.iss"

var
  IntegrationExitCode: Integer;

function GetCustomSetupExitCode(): Integer;
begin
  Result := IntegrationExitCode;
end;

procedure InitializeWizard;
begin
  InitializeDeployment;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  DeploymentProgress(CurProgress, MaxProgress);
end;

function PrepareExisting(const Argument: String): String;
var
  Code: Integer;
begin
  Result := '';
  if not FileExists(ExpandConstant('{app}\{#AppExe}')) then Exit;
  if not Exec(ExpandConstant('{app}\{#AppExe}'), Argument, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := 'Pathweft could not prepare its existing installation. No files have been replaced.'
  else if Code = 2 then
    Result := 'Close Pathweft and its editor windows, then try again. Active windows are not force-closed.'
  else if Code <> 0 then
    Result := 'Pathweft could not stop its helper or restore its integration. Keep the installation and recovery record, resolve the integration in Settings, then try again.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Pid: Integer;
  Handle: THandle;
  Created, Exited, Kernel, User: TCreationTime;
  Expected: String;
begin
  Result := '';
#ifndef ProgressVerification
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{autopf}\Pathweft')) <> 0 then begin
    Result := 'Pathweft shell integration requires its protected Program Files installation path.';
    Exit;
  end;
  if (not MachineClassOwned(HKLM64, 'x64', ExpandConstant('{app}'))) or
     (not MachineClassOwned(HKLM32, 'x86', ExpandConstant('{app}'))) then begin
    Result := 'Another application owns the shell class. No registration has been replaced.';
    Exit;
  end;
#endif
  DeploymentPhase('Closing Pathweft', 'Waiting for the current window to finish closing.');
  DeploymentFailure := 'The updating process could not finish preparing its installation.';
  Pid := StrToIntDef(ExpandConstant('{param:WAITPID|0}'), 0);
  if Pid <> 0 then begin
    Expected := ExpandConstant('{param:WAITCREATED|}');
    if Expected = '' then begin
      Result := 'The updating process identity is incomplete.';
      Exit;
    end;
    Handle := OpenProcess($00101000, False, Pid);
    if Handle <> 0 then begin
      try
        if not GetProcessTimes(Handle, Created, Exited, Kernel, User) then begin
          Result := 'The updating process identity could not be checked.';
          Exit;
        end;
        { A reused PID is no longer the requesting process. Normal running-app
          ownership is checked independently below before replacing any files. }
        if CompareText(Format('%.8x%.8x', [Created.High, Created.Low]), Expected) = 0 then begin
          Log('Waiting for the identified requesting process to exit.');
          if WaitDeploymentHandle(Handle) <> 0 then begin
            Result := 'The updating Pathweft process could not be observed to exit.';
            Exit;
          end;
          Log('The identified requesting process exited.');
        end else begin
          Log('Requesting process creation time differs; no wait for a reused PID.');
        end;
      finally
        CloseHandle(Handle);
      end;
    end;
  end;
  Result := PrepareExisting('--prepare-update');
  DeploymentFailure := Result;
  if Result = '' then DeploymentPhase('Installing update', 'Replacing application files.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if MigrationRequested() then begin
      DeploymentPhase('Installing protected files', 'The account migration will restore your enabled routes after the old installation is retired.');
      DeploymentFailure := 'Protected files were installed, but account migration has not completed.';
    end else begin
      DeploymentPhase('Restoring integration', 'Restoring your enabled folder routes and keyboard helper.');
      DeploymentFailure := 'Files were installed, but Windows integration could not be restored. Reopen Pathweft to review its status.';
    end;
  end;
  if (CurStep = ssPostInstall) and (not MigrationRequested()) and WizardIsTaskSelected('defaultfm') then begin
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--desktop-launch --set-default', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    begin
      IntegrationExitCode := 9;
      DeploymentFailure := 'Pathweft was installed, but folder integration was not enabled. Use Settings > Windows integration to inspect or restore it.';
      RaiseException(DeploymentFailure);
    end;
  end;
  if (CurStep = ssPostInstall) and (not MigrationRequested()) then begin
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--desktop-launch --resume-agent', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    begin
      IntegrationExitCode := 9;
      DeploymentFailure := 'Pathweft was installed, but its Win+E helper could not restart. Open Settings > Windows integration to inspect its status.';
      RaiseException(DeploymentFailure);
    end;
  end;
  if (CurStep = ssPostInstall) and (not MigrationRequested()) and RestartRequested() then begin
    DeploymentPhase('Opening Pathweft', 'Launching the updated version in your desktop session.');
    DeploymentFailure := 'The update was installed, but Pathweft could not reopen. Open it from Start.';
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--desktop-launch', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    begin
      IntegrationExitCode := 9;
      RaiseException(DeploymentFailure);
    end;
  end;
  if CurStep = ssInstall then DeploymentFailure := 'The update could not replace its application files. Close any app using them, then try the update again.';
  if CurStep = ssDone then begin
    if IntegrationExitCode <> 0 then begin
      DeploymentPhase('Update incomplete', DeploymentFailure);
      Exit;
    end;
    DeploymentFailure := '';
    if MigrationRequested() then
      DeploymentPhase('Protected files installed', 'Account migration is ready to retire the old installation and restore its routes.')
    else
      DeploymentPhase('Update installed', 'Pathweft has been launched.');
  end;
end;

procedure DeinitializeSetup;
begin
  FinishDeployment;
end;

function InitializeUninstall(): Boolean;
var
  Error: String;
begin
  Error := PrepareExisting('--prepare-uninstall');
  Result := Error = '';
  if not Result then MsgBox(Error, mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
#ifndef ProgressVerification
  if CurUninstallStep = usUninstall then begin
    RemoveOwnedMachineClass(HKLM64, 'x64');
    RemoveOwnedMachineClass(HKLM32, 'x86');
  end;
#endif
end;
