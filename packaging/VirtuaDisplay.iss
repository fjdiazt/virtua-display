#ifndef MyAppVersion
  #error MyAppVersion must be supplied by build-installer.ps1
#endif

#define MyAppName "Virtua Display"
#define MyAppExeName "VirtuaDisplay.exe"

[Setup]
AppId={{dfc943fb-8a0c-4c66-93c4-de7e92f75d3f}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher=Virtua
AppPublisherURL=https://github.com/fjdiazt/virtua-display
DefaultDirName={autopf}\Virtua Display
DefaultGroupName=Virtua Display
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=VirtuaDisplay-Setup-{#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
UsedUserAreasWarning=no
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Virtua.Display\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\Virtua.Display
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\VirtuaDisplay.exe"; Flags: dontcopy
Source: "driver\sudovda\*"; Flags: dontcopy
Source: "tools\nefconc.exe"; Flags: dontcopy
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\Virtua Display"; Filename: "{app}\VirtuaDisplay.exe"
Name: "{autodesktop}\Virtua Display"; Filename: "{app}\VirtuaDisplay.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Virtua\Display"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Virtua Display"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\VirtuaDisplay.exe"; Description: "Launch Virtua Display"; Flags: nowait postinstall skipifsilent

[Code]
const
  DriverReady = 0;
  DriverMissing = 2;
  DriverIncompatible = 3;
  DriverError = 4;

function TempFile(const Name: String): String;
begin
  Result := ExpandConstant('{tmp}\') + Name;
end;

function FirstOutputLine(const Output: TExecOutput; const Fallback: String): String;
begin
  if GetArrayLength(Output.StdOut) > 0 then
    Result := Output.StdOut[0]
  else if GetArrayLength(Output.StdErr) > 0 then
    Result := Output.StdErr[0]
  else
    Result := Fallback;
end;

function ProbeDriver(var Message: String): Integer;
var
  Output: TExecOutput;
  ResultCode: Integer;
begin
  if not ExecAndCaptureOutput(
      TempFile('{#MyAppExeName}'), '--driver-status', ExpandConstant('{tmp}'),
      SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode, Output) then
  begin
    Result := DriverError;
    Message := 'Could not run the Virtua Display driver check.';
    exit;
  end;

  Result := ResultCode;
  case ResultCode of
    DriverReady: Message := 'The installed SudoVDA driver is ready.';
    DriverMissing: Message := 'The SudoVDA driver is not installed.';
    DriverIncompatible: Message := 'The installed SudoVDA driver is incompatible with Virtua Display.';
    DriverError: Message := 'Virtua Display could not inspect the installed SudoVDA driver.';
  else
    Message := Format('Unexpected driver check result: %d.', [ResultCode]);
  end;
  Message := FirstOutputLine(Output, Message);
  Log(Format('Driver probe: %d, %s', [ResultCode, Message]));
end;

function VerifyFile(const Name, ExpectedHash: String): Boolean;
begin
  Result := CompareText(GetSHA256OfFile(TempFile(Name)), ExpectedHash) = 0;
  if not Result then
    Log('SHA-256 mismatch: ' + Name);
end;

function VerifyPayload: Boolean;
begin
  Result :=
    VerifyFile('SudoVDA.inf', 'AD69AC682756F0CF339B081FAC7E6E8159FDF2CA01CA69DF8945C7246C286925') and
    VerifyFile('SudoVDA.dll', '47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63') and
    VerifyFile('sudovda.cat', '2F9189DE5604BEC9D86F51640CC540639E394D9AD0F8E689129375E95F2D22F8') and
    VerifyFile('sudovda.cer', '6ACCDCD519F6179D967DB4EAA20ECF25A732BA30E87F4CFFEBC768B2C13C9007') and
    VerifyFile('nefconc.exe', 'DD5D6EE28800A328BAE8CDFC3809D38E47E10EB7755D1845255CA9067A32DD0A');
end;

function RunCommand(
  const FileName, Parameters, Description: String;
  var NeedsRestart: Boolean;
  var ErrorMessage: String): Boolean;
var
  Output: TExecOutput;
  ResultCode: Integer;
begin
  Result := ExecAndCaptureOutput(
    FileName, Parameters, ExpandConstant('{tmp}'), SW_SHOWNORMAL,
    ewWaitUntilTerminated, ResultCode, Output);
  if not Result then
  begin
    ErrorMessage := 'Could not start ' + Description + '.';
    exit;
  end;

  Log(Format('%s exit code: %d', [Description, ResultCode]));
  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    ErrorMessage := FirstOutputLine(Output,
      Format('%s failed (exit code %d).', [Description, ResultCode]));
    Result := False;
    exit;
  end;

  if ResultCode = 3010 then
    NeedsRestart := True;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Status: Integer;
  Message: String;
begin
  Result := '';
  ExtractTemporaryFile('{#MyAppExeName}');
  Status := ProbeDriver(Message);
  if Status = DriverReady then
    exit;
  if Status <> DriverMissing then
  begin
    Result := Message;
    exit;
  end;

  try
    ExtractTemporaryFile('SudoVDA.inf');
    ExtractTemporaryFile('SudoVDA.dll');
    ExtractTemporaryFile('sudovda.cat');
    ExtractTemporaryFile('sudovda.cer');
    ExtractTemporaryFile('nefconc.exe');
    if not VerifyPayload then
    begin
      Result := 'Bundled driver verification failed.';
      exit;
    end;
  except
    Log(GetExceptionMessage);
    Result := 'Bundled driver verification failed.';
    exit;
  end;

  if not RunCommand(ExpandConstant('{sys}\certutil.exe'),
      '-addstore -f root ' + AddQuotes(TempFile('sudovda.cer')),
      'SudoVDA root certificate installation', NeedsRestart, Result) then
    exit;
  if not RunCommand(ExpandConstant('{sys}\certutil.exe'),
      '-addstore -f TrustedPublisher ' + AddQuotes(TempFile('sudovda.cer')),
      'SudoVDA publisher certificate installation', NeedsRestart, Result) then
    exit;
  if not RunCommand(TempFile('nefconc.exe'),
      '--create-device-node --class-name Display --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" --hardware-id "root\sudomaker\sudovda" --no-duplicates',
      'SudoVDA device creation', NeedsRestart, Result) then
    exit;
  if not RunCommand(TempFile('nefconc.exe'),
      '--install-driver --inf-path ' + AddQuotes(TempFile('SudoVDA.inf')),
      'SudoVDA driver installation', NeedsRestart, Result) then
    exit;

  Status := ProbeDriver(Message);
  if Status <> DriverReady then
    Result := Message;
end;
