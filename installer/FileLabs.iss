; Inno Setup script for File Labs. Build it through build.ps1, which publishes the app first and
; passes the version in.

#define AppName "File Labs"
#define AppExe "FileLabs.exe"
#define AppPublisher "Protagonist Labs"
#define AppIdGuid "{7E4C2B9A-3F1D-4C8E-9A6B-51D0C2E8F4A7}"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Never change AppId: it is what ties an update to the installation it replaces, and Shelf, Reel, SpaceScan,
; Backup Labs and Stash find File Labs through its uninstall entry (DisplayIcon) when its card is missing.
AppId={{#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; Per machine since 1.0.48 (ecosystem v3, vault Proiecte/Ecosistem-v3-foldere.md): every studio app in
; Program Files\ProtagonistLabs, data in %AppData%\ProtagonistLabs\<app>. Installing and updating ask for UAC.
; The HKCU keys (default file manager, Run) are still per user: the app writes them, started as the signed-in
; user (runasoriginaluser below).
PrivilegesRequired=admin
DefaultDirName={autopf}\ProtagonistLabs\{#AppName}
; the previous install was per user, in %LocalAppData%\Programs: never reuse that folder
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; {userappdata} etc. below are the signed-in user's when they elevate their own account; the app repeats the data move
; at its start for anyone else
UsedUserAreasWarning=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

CloseApplications=force
RestartApplications=no

OutputDir=..\dist
OutputBaseFilename=FileLabs-{#AppVersion}-setup
SetupIconFile=..\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "defaultfm"; Description: "Use File Labs instead of File Explorer (opening folders and drives, Win+E). Can be changed later in Settings."; GroupDescription: "Windows integration:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; As the signed-in user: moves the data from before 1.0.48 if [Code] couldn't, points the HKCU entries (Run, default file
; manager, Win+E) at this exe, and writes the ecosystem card the sister apps read (App.xaml.cs, --installed).
Filename: "{app}\{#AppExe}"; Parameters: "--installed"; Flags: runhidden waituntilterminated runasoriginaluser
; The app writes the keys itself (same code as the Settings switch), pointing at the installed exe.
Filename: "{app}\{#AppExe}"; Parameters: "--set-default"; StatusMsg: "Setting File Labs as the default file manager..."; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: defaultfm
; An update closes every File Labs process, the Win+E agent included: bring it back if it was on.
Filename: "{app}\{#AppExe}"; Parameters: "--agent"; Flags: nowait runhidden runasoriginaluser; Check: AgentWanted
; A silent install is File Labs updating itself (Updater.cs): start it again, as the user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Registry]
; Settings → "Start with Windows, hidden next to the clock" writes this value; nothing is written here, it only goes on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "File Labs"; Flags: uninsdeletevalue dontcreatekey

[UninstallRun]
; Hand folders back to File Explorer before the exe the keys point at disappears.
Filename: "{app}\{#AppExe}"; Parameters: "--unset-default"; Flags: runhidden waituntilterminated; RunOnceId: "UnsetDefault"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\ProtagonistLabs\{#AppName}"
Type: filesandordirs; Name: "{userappdata}\FileLabs"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdGuid}_is1';

{ Setup cannot replace files the running app holds open, so stop it first. }
procedure StopFileLabs();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

{ The agent's autostart entry exists while File Labs is the default file manager. When the
  defaultfm task runs, --set-default starts the agent itself, so this only covers updates. }
function AgentWanted(): Boolean;
begin
  Result := RegValueExists(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'File Labs Agent')
            and not WizardIsTaskSelected('defaultfm');
end;

function HasEntries(const Dir: String): Boolean;
var
  F: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Dir) + '*', F) then
  try
    repeat
      if (F.Name <> '.') and (F.Name <> '..') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(F);
  finally
    FindClose(F);
  end;
end;

{ The data rule (spec §2, the same as DataFolder.Move in Settings.cs): rename the old folder to the new one when the new
  one is missing or empty; one rename on the same drive, never a partial copy. The new one with data: nothing is mixed.
  A failed rename (a locked file) leaves everything in place; the app uses it from there and retries at its start. }
function MoveData(const OldDir, NewDir: String): Boolean;
begin
  Result := True;
  if not DirExists(OldDir) then Exit;
  if HasEntries(NewDir) then
  begin
    Log(OldDir + ' left in place: ' + NewDir + ' already has data');
    Exit;
  end;
  ForceDirectories(ExtractFileDir(NewDir));
  if DirExists(NewDir) then RemoveDir(NewDir);
  Result := RenameFile(OldDir, NewDir);
  if Result then Log('moved ' + OldDir + ' to ' + NewDir)
  else Log('could not move ' + OldDir + ' to ' + NewDir + '; the app retries at its start');
end;

{ Up to 1.0.47: settings, folder sizes, Quick access, editor state in %AppData%\FileLabs; the editor's WebView2 data
  in %LocalAppData%\FileLabs\WebView2. All of it now lives in %AppData%\ProtagonistLabs\File Labs. }
procedure MoveOldData();
var
  NewDir: String;
begin
  NewDir := ExpandConstant('{userappdata}\ProtagonistLabs\{#AppName}');
  if MoveData(ExpandConstant('{userappdata}\FileLabs'), NewDir) then
    if MoveData(ExpandConstant('{localappdata}\FileLabs\WebView2'), NewDir + '\WebView2') then
      RemoveDir(ExpandConstant('{localappdata}\FileLabs'));
end;

{ A shortcut the user kept from the per-user install (desktop, pinned to the taskbar) points at the exe that is
  about to go: point it here instead of deleting it. }
procedure Repoint(const Lnk: String);
begin
  if FileExists(Lnk) then
    CreateShellLink(Lnk, '', ExpandConstant('{app}\{#AppExe}'), '', ExpandConstant('{app}'), '', 0, SW_SHOWNORMAL);
end;

{ The per-user install up to 1.0.47 (same AppId, HKCU uninstall entry, %LocalAppData%\Programs\File Labs). Its own
  uninstaller is not run: it deletes %AppData%\FileLabs and hands folders back to Explorer. Its data has already
  moved (PrepareToInstall); this removes its program folder, Start menu entries and uninstall entry. }
procedure RemoveOldInstall();
var
  Location, Expected: String;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, UninstallKey, 'InstallLocation', Location) then Exit;
  Repoint(ExpandConstant('{userdesktop}\{#AppName}.lnk'));
  Repoint(ExpandConstant('{userappdata}\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\{#AppName}.lnk'));
  DeleteFile(ExpandConstant('{userprograms}\{#AppName}\{#AppName}.lnk'));
  DeleteFile(ExpandConstant('{userprograms}\{#AppName}\Uninstall {#AppName}.lnk'));
  RemoveDir(ExpandConstant('{userprograms}\{#AppName}'));
  { only the default folder: one the user chose could hold other things }
  Expected := ExpandConstant('{localappdata}\Programs\{#AppName}');
  if CompareText(RemoveBackslashUnlessRoot(Location), Expected) = 0 then
  begin
    if DelTree(Expected, True, True, True) then Log('removed the per-user install in ' + Expected)
    else Log('could not remove all of ' + Expected);
  end
  else Log('per-user install in ' + Location + ' left in place (not the default folder)');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, UninstallKey);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
begin
  { an update started from File Labs (always silent): give its window time to close and save before it is killed }
  if WizardSilent then
    for I := 1 to 50 do
      if CheckForMutexes('FileLabs-' + GetUserNameString) then Sleep(200) else Break;
  StopFileLabs();
  MoveOldData();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then RemoveOldInstall();
end;

function InitializeUninstall(): Boolean;
begin
  StopFileLabs();
  Result := True;
end;
