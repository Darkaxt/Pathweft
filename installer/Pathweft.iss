; One per-user installation. Build via build.ps1; all paths are explicit inputs.
#define AppName "Pathweft"
#define AppExe "Pathweft.exe"
#define AppIdGuid "{A52C677C-5F24-45E2-B814-66DCAABFB71A}"
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
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Pathweft
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

[Icons]
Name: "{group}\Pathweft"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Pathweft"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Flags: nowait runhidden; Check: RestartRequested
Filename: "{app}\{#AppExe}"; Description: "Launch Pathweft"; Flags: nowait postinstall skipifsilent

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
          if WaitForSingleObject(Handle, $FFFFFFFF) <> 0 then begin
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
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('defaultfm') then begin
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--set-default', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Pathweft was installed, but folder integration was not enabled. Use Settings > Windows integration to inspect or restore it.');
  end;
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{app}\{#AppExe}'), '--resume-agent', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('Pathweft was installed, but its Win+E helper could not restart. Open Settings > Windows integration to inspect its status.');
  end;
end;

function InitializeUninstall(): Boolean;
var
  Error: String;
begin
  Error := PrepareExisting('--prepare-uninstall');
  Result := Error = '';
  if not Result then MsgBox(Error, mbError, MB_OK);
end;
