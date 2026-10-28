; Inno Setup script for Pathweft. Build it through build.ps1, which publishes the app first and
; passes the version in.

#define AppName "Pathweft"
#define AppExe "Pathweft.exe"
#define AppPublisher "Darkaxt"
#define AppIdGuid "{A52C677C-5F24-45E2-B814-66DCAABFB71A}"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
; Independent fork identity; never reuse the File Labs AppId.
AppId={{#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; Program files belong to Pathweft; data lives in %AppData%\Pathweft. No upstream data migration.
; The HKCU keys (default file manager, Run) are still per user: the app writes them, started as the signed-in
; user (runasoriginaluser below).
PrivilegesRequired=admin
DefaultDirName={autopf}\Darkaxt\{#AppName}
; the previous install was per user, in %LocalAppData%\Programs: never reuse that folder
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
UsedUserAreasWarning=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

CloseApplications=force
RestartApplications=no

OutputDir=..\dist
OutputBaseFilename=Pathweft-{#AppVersion}-setup
SetupIconFile=..\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "defaultfm"; Description: "Use Pathweft instead of File Explorer (opening folders and drives, Win+E). Can be changed later in Settings."; GroupDescription: "Windows integration:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; The app writes the keys itself (same code as the Settings switch), pointing at the installed exe.
Filename: "{app}\{#AppExe}"; Parameters: "--set-default"; StatusMsg: "Setting Pathweft as the default file manager..."; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: defaultfm
; An update closes every Pathweft process, the Win+E agent included: bring it back if it was on.
Filename: "{app}\{#AppExe}"; Parameters: "--agent"; Flags: nowait runhidden runasoriginaluser; Check: AgentWanted
; A silent install is Pathweft updating itself (Updater.cs): start it again, as the user, not elevated.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Registry]
; Settings → "Start with Windows, hidden next to the clock" writes this value; nothing is written here, it only goes on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Pathweft"; Flags: uninsdeletevalue dontcreatekey

[UninstallRun]
; Hand folders back to File Explorer before the exe the keys point at disappears.
Filename: "{app}\{#AppExe}"; Parameters: "--unset-default"; Flags: runhidden waituntilterminated; RunOnceId: "UnsetDefault"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Pathweft"

[Code]
{ Setup cannot replace files the running app holds open, so stop it first. }
procedure StopPathweft();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

{ The agent's autostart entry exists while Pathweft is the default file manager. When the
  defaultfm task runs, --set-default starts the agent itself, so this only covers updates. }
function AgentWanted(): Boolean;
begin
  Result := RegValueExists(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Pathweft Agent')
            and not WizardIsTaskSelected('defaultfm');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
begin
  { an update started from Pathweft (always silent): give its window time to close and save before it is killed }
  if WizardSilent then
    for I := 1 to 50 do
      if CheckForMutexes('Pathweft-' + GetUserNameString) then Sleep(200) else Break;
  StopPathweft();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopPathweft();
  Result := True;
end;
