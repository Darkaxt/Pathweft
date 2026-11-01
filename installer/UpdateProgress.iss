{ The installer owns this window, so updating the application cannot dismiss it. }
var
  DeploymentForm: TSetupForm;
  DeploymentHeading, DeploymentStatus, DeploymentDetail, DeploymentPercent: TNewStaticText;
  DeploymentTrack, DeploymentFill: TPanel;
  DeploymentClose: TNewButton;
  DeploymentDone: Boolean;
  DeploymentFailure: String;
  DeploymentAccent: LongWord;
type
  TDeploymentMessage = array[0..63] of Byte; { opaque storage for the native MSG }
function MsgWaitForMultipleObjects(Count: LongWord; var Handle: THandle; WaitAll: Boolean; Milliseconds, Mask: LongWord): LongWord;
  external 'MsgWaitForMultipleObjects@user32.dll stdcall';
function PeekMessage(var Message: TDeploymentMessage; Handle: THandle; First, Last, Remove: LongWord): Boolean;
  external 'PeekMessageW@user32.dll stdcall';
function TranslateMessage(var Message: TDeploymentMessage): Boolean;
  external 'TranslateMessage@user32.dll stdcall';
function DispatchMessage(var Message: TDeploymentMessage): Integer;
  external 'DispatchMessageW@user32.dll stdcall';

function WaitDeploymentHandle(Handle: THandle): LongWord;
var
  Message: TDeploymentMessage;
begin
  { The actual process/event owns completion. Continue painting and responding
    while it is alive; no polling timer, deadline or duplicate launch. }
  while True do begin
    Result := MsgWaitForMultipleObjects(1, Handle, False, $FFFFFFFF, $04FF);
    if Result = 0 then Exit;
    if Result <> 1 then Exit;
    while PeekMessage(Message, 0, 0, 0, 1) do begin
      TranslateMessage(Message); DispatchMessage(Message);
    end;
  end;
end;

#ifdef ProgressVerification
var
  LastProgressSample: Integer;
  ProgressHook: THandle;
#endif

function UpdateWindow(Handle: THandle): Boolean;
  external 'UpdateWindow@user32.dll stdcall';
function GetDpiForWindow(Handle: THandle): LongWord;
  external 'GetDpiForWindow@user32.dll stdcall';
function DwmSetWindowAttribute(Handle: THandle; Attribute: Integer; var Value: LongWord; Size: Integer): Integer;
  external 'DwmSetWindowAttribute@dwmapi.dll stdcall';
#ifdef ProgressVerification
function OpenEvent(Access: LongWord; Inherit: Boolean; const Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function GetSystemMetrics(Index: Integer): Integer;
  external 'GetSystemMetrics@user32.dll stdcall';
function SetWindowLong(Handle: THandle; Index: Integer; Value: LongWord): LongWord;
  external 'SetWindowLongW@user32.dll stdcall';
function GetWindowLong(Handle: THandle; Index: Integer): LongWord;
  external 'GetWindowLongW@user32.dll stdcall';
function GetCurrentThreadId(): LongWord;
  external 'GetCurrentThreadId@kernel32.dll stdcall';
function SetWindowsHookEx(Kind: Integer; Callback: LongWord; Module: THandle; Thread: LongWord): THandle;
  external 'SetWindowsHookExW@user32.dll stdcall';
function UnhookWindowsHookEx(Hook: THandle): Boolean;
  external 'UnhookWindowsHookEx@user32.dll stdcall';
function CallNextHookEx(Hook: THandle; Code: Integer; WParam: LongWord; LParam: Integer): Integer;
  external 'CallNextHookEx@user32.dll stdcall';
function IsChild(Parent, Child: THandle): Boolean;
  external 'IsChild@user32.dll stdcall';

function ProgressActivationHook(Code: Integer; WParam: LongWord; LParam: Integer): Integer;
begin
  if ((Code = 5) or (Code = 9)) and ((WParam = DeploymentForm.Handle) or IsChild(DeploymentForm.Handle, WParam)) then Result := 1
  else Result := CallNextHookEx(ProgressHook, Code, WParam, LParam);
end;
#endif

function DeploymentX(Value: Integer): Integer;
begin
  Result := Round(Value * GetDpiForWindow(WizardForm.Handle) / 96.0);
#ifdef ProgressVerification
  Result := Result * StrToIntDef(ExpandConstant('{param:TESTSCALE|100}'), 100) div 100;
#endif
end;

function DeploymentY(Value: Integer): Integer;
begin
  Result := Round(Value * GetDpiForWindow(WizardForm.Handle) / 96.0);
#ifdef ProgressVerification
  Result := Result * StrToIntDef(ExpandConstant('{param:TESTSCALE|100}'), 100) div 100;
#endif
end;

function DeploymentLabel(Top, Height: Integer; Text: String; Ink: LongWord; Size: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(DeploymentForm);
  Result.Parent := DeploymentForm;
  Result.AutoSize := False;
  Result.SetBounds(DeploymentX(20), DeploymentY(Top), DeploymentX(480), DeploymentY(Height));
  Result.Caption := Text;
  Result.WordWrap := True;
  Result.Font.Color := Ink;
  Result.Font.Size := Size;
#ifdef ProgressVerification
  Result.Font.Size := Size * StrToIntDef(ExpandConstant('{param:TESTSCALE|100}'), 100) div 100;
#endif
end;

procedure DeploymentCloseQuery(Sender: TObject; var CanClose: Boolean);
begin
  CanClose := DeploymentDone;
  if not CanClose then DeploymentForm.Hide; { Dismiss progress without cancelling file replacement. }
end;

procedure DeploymentDismiss(Sender: TObject);
begin
  DeploymentForm.Close;
end;

#ifdef ProgressVerification
procedure CaptureDeployment(const Phase: String; Percent: Integer);
var
  Ready, Proceed: THandle;
  Report: String;
begin
  Report := ExpandConstant('{param:TESTREPORT|}');
  if Report = '' then Exit;
  SaveStringToFile(Report, Phase + #13#10 + IntToStr(DeploymentForm.Handle) + #13#10 +
    IntToStr(Percent) + #13#10 + DeploymentHeading.Caption + #13#10 +
    DeploymentStatus.Caption + #13#10 + DeploymentDetail.Caption + #13#10 +
    IntToStr(DeploymentForm.Color) + #13#10 + IntToStr(DeploymentAccent) + #13#10 +
    IntToStr(DeploymentForm.ClientWidth) + #13#10 + IntToStr(DeploymentForm.ClientHeight), False);
  Ready := OpenEvent($0002, False, ExpandConstant('{param:TESTEVENT|}') + '-ready');
  Proceed := OpenEvent($00100000, False, ExpandConstant('{param:TESTEVENT|}') + '-continue');
  if (Ready = 0) or (Proceed = 0) then RaiseException('Progress verification owner is absent.');
  try
    SetEvent(Ready);
    if WaitDeploymentHandle(Proceed) <> 0 then RaiseException('Progress verification continuation failed.');
  finally
    CloseHandle(Ready); CloseHandle(Proceed);
  end;
end;
#endif

procedure DeploymentPhase(const Phase, Detail: String);
begin
  Log('Pathweft update phase: ' + Phase);
  if DeploymentForm = nil then Exit;
  DeploymentStatus.Caption := Phase;
  DeploymentDetail.Caption := Detail;
  DeploymentPercent.Caption := '';
  DeploymentFill.Visible := False;
  UpdateWindow(DeploymentForm.Handle);
#ifdef ProgressVerification
  CaptureDeployment(Phase, -1);
#endif
end;

procedure InitializeDeployment;
var
  Light: LongWord;
  Back, Ink, Muted, Frame, Track, Dark: LongWord;
begin
  if not RestartRequested() then Exit;
  Light := 1;
  RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'AppsUseLightTheme', Light);
  if Light = 0 then begin Back := $141414; Ink := $F6EEE8; Muted := $AEAEAE; end
  else begin Back := $F3F3F3; Ink := $202020; Muted := $565656; end;
  Back := StrToIntDef(ExpandConstant('{param:UPDATEBACK|}'), Back);
  Ink := StrToIntDef(ExpandConstant('{param:UPDATEINK|}'), Ink);
  Muted := StrToIntDef(ExpandConstant('{param:UPDATEMUTED|}'), Muted);
  if Light = 0 then Track := $2C2C2C else Track := $DADADA;
  Track := StrToIntDef(ExpandConstant('{param:UPDATETRACK|}'), Track);
  Frame := $FFD78A;
  RegQueryDWordValue(HKCU, 'Software\Microsoft\Windows\DWM', 'ColorizationColor', Frame);
  DeploymentAccent := ((Frame and $FF) shl 16) or (Frame and $FF00) or ((Frame shr 16) and $FF);
  DeploymentAccent := StrToIntDef(ExpandConstant('{param:UPDATEACCENT|}'), DeploymentAccent);
  DeploymentForm := CreateCustomForm(DeploymentX(520), DeploymentY(210), False, False);
  DeploymentForm.Caption := 'Updating Pathweft';
  DeploymentForm.Color := Back;
  DeploymentForm.Font.Name := 'Segoe UI';
  DeploymentForm.ClientWidth := DeploymentX(520);
  DeploymentForm.ClientHeight := DeploymentY(210);
  DeploymentForm.OnCloseQuery := @DeploymentCloseQuery;
  DeploymentHeading := DeploymentLabel(16, 24, 'Updating Pathweft to {#AppVersion}', Ink, 11);
  DeploymentHeading.Font.Name := 'Segoe UI Semibold';
  DeploymentStatus := DeploymentLabel(174, 24, 'Preparing update', Muted, 9);
  DeploymentStatus.Width := DeploymentX(340);
  DeploymentDetail := DeploymentLabel(42, 64, 'Pathweft will reopen when the update is installed.', Muted, 9);
  DeploymentPercent := DeploymentLabel(174, 24, '', Muted, 9);
  DeploymentPercent.Left := DeploymentX(370);
  DeploymentPercent.Width := DeploymentX(130);
  DeploymentPercent.Alignment := taRightJustify;
  DeploymentTrack := TPanel.Create(DeploymentForm);
  DeploymentTrack.Parent := DeploymentForm;
  DeploymentTrack.SetBounds(DeploymentX(20), DeploymentY(152), DeploymentX(480), DeploymentY(4));
  DeploymentTrack.BevelOuter := bvNone;
  DeploymentTrack.ParentBackground := False;
  DeploymentTrack.Color := Track;
  DeploymentFill := TPanel.Create(DeploymentForm);
  DeploymentFill.Parent := DeploymentTrack;
  DeploymentFill.SetBounds(0, 0, 0, DeploymentTrack.Height);
  DeploymentFill.BevelOuter := bvNone;
  DeploymentFill.ParentBackground := False;
  DeploymentFill.Color := DeploymentAccent;
  DeploymentClose := TNewButton.Create(DeploymentForm);
  DeploymentClose.Parent := DeploymentForm;
  DeploymentClose.Caption := 'Close';
  DeploymentClose.SetBounds(DeploymentX(412), DeploymentY(172), DeploymentX(88), DeploymentY(28));
  DeploymentClose.OnClick := @DeploymentDismiss;
  DeploymentClose.Visible := False;
  Dark := 0;
  if ((Back and $FF) + ((Back shr 8) and $FF) + ((Back shr 16) and $FF)) < 384 then Dark := 1;
  DwmSetWindowAttribute(DeploymentForm.Handle, 20, Dark, 4);
  DwmSetWindowAttribute(DeploymentForm.Handle, 35, Back, 4);
  DwmSetWindowAttribute(DeploymentForm.Handle, 36, Ink, 4);
  DwmSetWindowAttribute(DeploymentForm.Handle, 34, DeploymentAccent, 4);
#ifdef ProgressVerification
  DeploymentForm.Left := GetSystemMetrics(76) + GetSystemMetrics(78) + DeploymentX(8192);
  DeploymentForm.Top := 0;
  SetWindowLong(DeploymentForm.Handle, -20, GetWindowLong(DeploymentForm.Handle, -20) or $08000080);
  ProgressHook := SetWindowsHookEx(5, CreateCallback(@ProgressActivationHook), 0, GetCurrentThreadId());
  if ProgressHook = 0 then RaiseException('The owned progress-window activation guard is absent.');
  DeploymentForm.Show; { instantiate actual VCL controls; the owned CBT hook rejects activation }
  LastProgressSample := -1;
#else
  DeploymentForm.Show;
#endif
  DeploymentPhase('Preparing update', 'Version {#AppVersion}');
end;

procedure DeploymentProgress(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if (DeploymentForm = nil) or (MaxProgress <= 0) then Exit;
  Percent := Round(100.0 * CurProgress / MaxProgress);
  DeploymentStatus.Caption := 'Installing files';
  DeploymentPercent.Caption := IntToStr(Percent) + '% of files';
  DeploymentDetail.Caption := 'Your settings and folder history are preserved.';
  DeploymentFill.Width := Round(1.0 * DeploymentTrack.Width * CurProgress / MaxProgress);
  DeploymentFill.Visible := True;
  UpdateWindow(DeploymentForm.Handle);
#ifdef ProgressVerification
  if (LastProgressSample < 0) or ((Percent >= 50) and (LastProgressSample < 50)) or ((Percent = 100) and (LastProgressSample < 100)) then begin
    LastProgressSample := Percent;
    CaptureDeployment('Installing files', Percent);
  end;
#endif
end;

procedure FinishDeployment;
begin
  if DeploymentForm = nil then Exit;
  DeploymentDone := True;
  if DeploymentFailure <> '' then begin
    DeploymentPhase('Update could not finish', DeploymentFailure);
#ifndef ProgressVerification
    DeploymentForm.Hide;
    DeploymentClose.Visible := True;
    DeploymentForm.ShowModal;
#endif
  end;
  DeploymentForm.Free;
  DeploymentForm := nil;
#ifdef ProgressVerification
  if ProgressHook <> 0 then UnhookWindowsHookEx(ProgressHook);
#endif
end;
