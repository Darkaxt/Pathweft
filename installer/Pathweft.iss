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
; Held while Setup runs: the shell router opens folders in Windows instead of
; starting a half-installed Pathweft that would lock files still to be replaced.
SetupMutex=Global\PathweftSetup
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
#ifdef LightBase
; LIGHT-01: only the files that changed since {#LightBase}; build.ps1 supplies the lists.
OutputBaseFilename=Pathweft-{#AppVersion}-update
#else
OutputBaseFilename=Pathweft-{#AppVersion}-setup
#endif
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

[InstallDelete]
#ifdef LightBase
; LIGHT-01: exactly the files the base release had and this one no longer ships. The
; cleanups below assume a full copy follows (some are copied back), so they stay out.
#include LightRemoved
#else
; An update only overwrites files, so members earlier releases shipped stay
; behind. These run before [Files] copies the current set: S56 (1.1.80) removed
; the bundled Everything engine and SDK, and each .NET servicing update renames
; the versioned DAC (the current one is copied back right after).
Type: filesandordirs; Name: "{app}\Everything"
Type: files; Name: "{app}\Everything64.dll"
Type: filesandordirs; Name: "{app}\vendor\EverythingSDK"
Type: files; Name: "{app}\mscordaccore_amd64_amd64_*.dll"
; S19 (1.1.83): the index service runs from {app}\services\file_index; `install` moves an older
; registration from %ProgramFiles%\PathweftIndex there and removes that copy.
Type: filesandordirs; Name: "{app}\index"
; DELIVERY-05 (1.1.83): the MCP notes moved to docs; the runtime earlier releases bundled is removed.
Type: files; Name: "{app}\Performance-MCP.md"
Type: files; Name: "{app}\*.xml"
#include "BundledRuntime.iss"
#endif

[Files]
; SHELL-06: the native handlers keep one fixed name. A Shell caller (Explorer,
; a browser) may still have the previous copy loaded: it is moved aside just
; before the new copy is written, so no app is closed. Uninstall never
; force-closes that caller to remove its DLL. They are copied first, so the new
; Pathweft.exe never runs without its handler, even if Setup stops early.
#ifdef LightBase
Source: "{#LightRoot}\PathweftShell-*.dll"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete skipifsourcedoesntexist; BeforeInstall: MoveHandlerAside
Source: "{#LightRoot}\*"; DestDir: "{app}"; Excludes: "PathweftShell-*.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
; LIGHT-03: the base files checked before anything changes, and the new files checked after
Source: "{#LightCheck}"; DestName: "light-base.txt"; Flags: dontcopy
Source: "{#LightVerify}"; DestName: "light-new.txt"; Flags: dontcopy
#else
Source: "{#PublishRoot}\PathweftShell-*.dll"; DestDir: "{app}"; Flags: ignoreversion uninsrestartdelete; BeforeInstall: MoveHandlerAside
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Excludes: "PathweftShell-*.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Registry]
#ifndef ProgressVerification
; Packaged desktop callers cannot see the per-user CLSID but can see the
; machine class. The DLL and adjacent executable live under protected {app}.
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\PathweftShell-x64.dll"
Root: HKLM64; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\PathweftShell-x86.dll"
Root: HKLM32; Subkey: "Software\Classes\CLSID\{{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
#endif

[UninstallDelete]
; Copies moved aside while a Shell caller held them; still-held ones go at restart.
Type: files; Name: "{app}\PathweftShell-*"

[Icons]
Name: "{group}\Pathweft"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Darkaxt.Pathweft"
Name: "{autodesktop}\Pathweft"; Filename: "{app}\{#AppExe}"; AppUserModelID: "Darkaxt.Pathweft"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--desktop-launch"; Description: "Launch Pathweft"; Flags: nowait postinstall skipifsilent

; SEARCH-08/09: setup installs the index service and removing Pathweft removes
; it (its settings and folder roots in ProgramData are kept); "not installed" is
; a successful no-op.
[UninstallRun]
Filename: "{app}\services\file_index\pathweft-indexd.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "PathweftIndexUninstall"

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

var
  { CallerSettled: the requesting window has exited. PathweftReopened: setup has opened Pathweft again. }
  CallerSettled, PathweftReopened: Boolean;

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
     (CompareText(Existing, ExpandConstant('{app}\PathweftShell-' + Architecture + '.dll')) = 0) then begin
    RegDeleteValue(Root, Key, '');
    if RegQueryStringValue(Root, Key, 'ThreadingModel', Existing) and (CompareText(Existing, 'Apartment') = 0) then
      RegDeleteValue(Root, Key, 'ThreadingModel');
    RegDeleteKeyIfEmpty(Root, Key);
    RegDeleteKeyIfEmpty(Root, MachineClassKey());
  end;
end;

#include "UpdateProgress.iss"

{ SHELL-06: delete a handler file, or, while a Shell caller still has it
  loaded, rename it aside (Windows allows that for a loaded DLL) and delete it
  at the next restart. }
procedure RetireHandler(const Path: String);
var
  Aside: String;
begin
  if not FileExists(Path) then Exit;
  if DeleteFile(Path) then begin
    Log('Removed the shell handler: ' + Path);
    Exit;
  end;
  if CompareText(ExtractFileExt(Path), '.old') = 0 then
    Aside := Path
  else begin
    Aside := Path + '.' + GetDateTimeString('yyyymmddhhnnss', #0, #0) + '.old';
    if not RenameFile(Path, Aside) then begin
      Log('The shell handler in use could not be moved aside: ' + Path);
      Exit;
    end;
    Log('Moved the shell handler in use aside: ' + Aside);
  end;
  if IsAdmin() then RestartReplace(Aside, '');
end;

procedure MoveHandlerAside;
begin
  RetireHandler(ExpandConstant(CurrentFileName));
end;

{ Removes the per-version copies of earlier releases and any copy moved aside
  that is no longer loaded. The fixed-name handlers just installed stay. }
procedure RetireOldHandlers;
var
  Found: TFindRec;
  Directory: String;
begin
  Directory := ExpandConstant('{app}');
  Log('Removing earlier shell handlers from ' + Directory);
  if FindFirst(Directory + '\PathweftShell-*', Found) then
    try
      repeat
        if (CompareText(Found.Name, 'PathweftShell-x64.dll') <> 0) and
           (CompareText(Found.Name, 'PathweftShell-x86.dll') <> 0) then
          RetireHandler(AddBackslash(Directory) + Found.Name);
      until not FindNext(Found);
    finally
      FindClose(Found);
    end;
end;

var
  IntegrationExitCode: Integer;

#ifdef LightBase
// LIGHT-03: '' when every listed file under the application folder has its SHA-256,
// else the first that differs or is missing. Each line is "<sha256> <relative path>".
function LightFileDiffers(const List: String): String;
var
  Lines: TArrayOfString;
  I, Space, Count: Integer;
  Line, Path: String;
begin
  Result := '';
  ExtractTemporaryFile(List);
  if not LoadStringsFromFile(ExpandConstant('{tmp}\' + List), Lines) then begin
    Result := List;
    Exit;
  end;
  Count := GetArrayLength(Lines);
  for I := 0 to Count - 1 do begin
    Line := Trim(Lines[I]);
    if Line = '' then Continue;
    Space := Pos(' ', Line);
    Path := Copy(Line, Space + 1, Length(Line));
    try
      if CompareText(GetSHA256OfFile(ExpandConstant('{app}\') + Path), Copy(Line, 1, Space - 1)) <> 0 then Result := Path;
    except
      Result := Path;
    end;
    if Result <> '' then Exit;
    if (I mod 100) = 0 then DeploymentChecked(I, Count);
  end;
end;

procedure ReopenForFullUpdate;
var
  Code: Integer;
begin
  if RestartRequested() then
    PathweftReopened := Exec(ExpandConstant('{app}\{#AppExe}'), '--desktop-launch --full-update', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
#endif

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

// DELIVERY-05: Pathweft is framework-dependent. Setup installs the Microsoft .NET 10 Desktop Runtime (x64)
// when it is missing, from Microsoft's download, only after checking that Microsoft signed it.
// The check resets PSModulePath: an inherited PowerShell 7 module path stops Windows PowerShell loading
// Microsoft.PowerShell.Security, and the signature check would then refuse a valid installer.
function DesktopRuntimeInstalled: Boolean;
var
  Find: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Find) then begin
    try
      repeat
        if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then Result := True;
      until Result or not FindNext(Find);
    finally
      FindClose(Find);
    end;
  end;
end;

function EnsureDesktopRuntime: String;
var
  Installer: String;
  Code: Integer;
begin
  Result := '';
  if DesktopRuntimeInstalled then Exit;
  DeploymentPhase('Installing the .NET Desktop Runtime', 'Downloading the Microsoft .NET 10 Desktop Runtime (x64).');
  try
    DownloadTemporaryFile('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-win-x64.exe', '', nil);
  except
    Result := 'The Microsoft .NET 10 Desktop Runtime could not be downloaded (' + GetExceptionMessage + '). Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run Setup again. No files have been replaced.';
    Exit;
  end;
  Installer := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -Command "$env:PSModulePath=[Environment]::GetEnvironmentVariable(''PSModulePath'',''Machine''); $s=Get-AuthenticodeSignature -LiteralPath ''' + Installer + '''; if($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -like ''*O=Microsoft Corporation*''){exit 0}; exit 1"',
      '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
    Result := 'The downloaded .NET Desktop Runtime installer does not carry a valid Microsoft signature. Nothing has been installed.';
    Exit;
  end;
  DeploymentPhase('Installing the .NET Desktop Runtime', 'Installing the Microsoft .NET 10 Desktop Runtime (x64).');
  { 3010: installed, restart recommended; 1638: this or a newer version is already installed. }
  if not Exec(Installer, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code) or ((Code <> 0) and (Code <> 3010) and (Code <> 1638)) then begin
    Result := Format('The Microsoft .NET 10 Desktop Runtime could not be installed (exit %d). No Pathweft files have been replaced.', [Code]);
    Exit;
  end;
  if not DesktopRuntimeInstalled then
    Result := 'The Microsoft .NET 10 Desktop Runtime is still missing after its installer finished. No Pathweft files have been replaced.';
end;

{ The Pathweft window that started this update (WAITPID, with its creation time) must have exited before any
  file is replaced, and before a failed update reopens Pathweft. Normal running-app ownership is checked
  independently by PrepareExisting. }
function WaitForCaller(): String;
var
  Pid: Integer;
  Handle: THandle;
  Created, Exited, Kernel, User: TCreationTime;
  Expected: String;
begin
  Result := '';
  if CallerSettled then Exit;
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
        { A reused PID is no longer the requesting process. }
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
  CallerSettled := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Differs: String;
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
  Result := EnsureDesktopRuntime;
  if Result <> '' then Exit;
#endif
  DeploymentPhase('Closing Pathweft', 'Waiting for the current window to finish closing.');
  DeploymentFailure := 'The updating process could not finish preparing its installation.';
  Result := WaitForCaller();
  if Result <> '' then Exit;
#ifdef LightBase
  { LIGHT-03: recheck the base right before changing anything. On a difference nothing
    is changed and Pathweft reopens to install the full update. }
  DeploymentPhase('Checking installed files', 'The light update needs the files of Pathweft {#LightBase}.');
  Differs := LightFileDiffers('light-base.txt');
  if Differs <> '' then begin
    Log('Light update not used; this installed file differs from {#LightBase}: ' + Differs);
    DeploymentPhase('Using the full update', 'An installed file differs from Pathweft {#LightBase}, so nothing has been changed. Pathweft reopens and installs the full update.');
    DeploymentFailure := '';
    ReopenForFullUpdate;
    Result := 'An installed file differs from Pathweft {#LightBase} (' + Differs + '). The light update changed nothing.';
    Exit;
  end;
#endif
  Result := PrepareExisting('--prepare-update');
#ifndef ProgressVerification
  { The service runs from the files this update replaces: stop it first; `install` starts it again. }
  if (Result = '') and FileExists(ExpandConstant('{app}\services\file_index\pathweft-indexd.exe')) then begin
    DeploymentPhase('Stopping the index service', 'The Pathweft index service restarts once the update is installed.');
    if not Exec(ExpandConstant('{app}\services\file_index\pathweft-indexd.exe'), 'stop', ExpandConstant('{app}\services\file_index'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      Result := Format('The Pathweft index service could not be stopped for the update (exit %d). No files have been replaced.', [Code]);
  end;
#endif
  DeploymentFailure := Result;
  if Result = '' then DeploymentPhase('Installing update', 'Replacing application files.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
#ifdef LightBase
  Differs: String;
#endif
begin
#ifdef LightBase
  { LIGHT-03: the files this update wrote must be the release's. }
  if CurStep = ssPostInstall then begin
    Differs := LightFileDiffers('light-new.txt');
    if Differs <> '' then begin
      Log('Light update file differs after installing: ' + Differs);
      IntegrationExitCode := 9;
      DeploymentFailure := '';
      ReopenForFullUpdate;
      RaiseException('The light update wrote ' + Differs + ', but it differs from the release. Pathweft reopens and installs the full update.');
    end;
  end;
#endif
  if CurStep = ssPostInstall then begin
    RetireOldHandlers;
    if MigrationRequested() then begin
      DeploymentPhase('Installing protected files', 'The account migration will restore your enabled routes after the old installation is retired.');
      DeploymentFailure := 'Protected files were installed, but account migration has not completed.';
    end else begin
      DeploymentPhase('Restoring integration', 'Restoring your enabled folder routes and keyboard helper.');
      DeploymentFailure := 'Files were installed, but Windows integration could not be restored. Reopen Pathweft to review its status.';
    end;
  end;
#ifndef ProgressVerification
  // SEARCH-09: the index service is Pathweft's only file index. `install` is
  // idempotent: it updates a running service in place and keeps its folder roots.
  if (CurStep = ssPostInstall) and FileExists(ExpandConstant('{app}\services\file_index\pathweft-indexd.exe')) then begin
    DeploymentPhase('Updating the index service', 'Installing or updating the Pathweft index service.');
    if not Exec(ExpandConstant('{app}\services\file_index\pathweft-indexd.exe'), 'install', ExpandConstant('{app}\services\file_index'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      Log(Format('Pathweft index service install failed (exit %d).', [Code]));
      SuppressibleMsgBox('Pathweft was installed, but its index service could not be installed or updated (exit ' + IntToStr(Code) + '). Searches stay unavailable until Settings > Windows integration > Pathweft index installs it.', mbError, MB_OK, IDOK);
    end;
  end;
#endif
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
    PathweftReopened := True;
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

{ UPDATE-04: an update that could not finish reopens Pathweft once the window that started it has exited. Pathweft
  compares its version with the one it started and reports the failure, with this line and the rest of setup's
  log (the updater runs setup with /LOG); setup's own failure window is shown only when Pathweft cannot reopen. }
procedure DeinitializeSetup;
var
  Code: Integer;
begin
  if (DeploymentFailure <> '') and RestartRequested() and (not MigrationRequested()) then begin
    Log('Pathweft update failure: ' + DeploymentFailure);
#ifndef ProgressVerification
    if (not PathweftReopened) and (WaitForCaller() = '') and FileExists(ExpandConstant('{app}\{#AppExe}')) then
      if Exec(ExpandConstant('{app}\{#AppExe}'), '--desktop-launch', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then
        DeploymentFailure := '';
#endif
  end;
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
