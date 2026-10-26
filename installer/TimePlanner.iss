#ifndef AppVersion
#define AppVersion "0.0.0-local"
#endif

#ifndef AppNumericVersion
#define AppNumericVersion "0.0.0.0"
#endif

#ifndef SourceDir
#define SourceDir "..\publish\widget"
#endif

[Setup]
AppId={{B7E3C1A2-5D4F-4E8B-9A6C-2F1D0E3B7A95}
AppName=Time Planner
AppVersion={#AppVersion}
VersionInfoVersion={#AppNumericVersion}
AppPublisher=Grit Solutions
DefaultDirName={autopf}\TimePlanner
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
UninstallDisplayName=Time Planner
UninstallDisplayIcon={app}\TimePlanner.Widget.exe
SetupIconFile=..\TimePlanner.Widget\Assests\insy.ico
OutputDir=..\dist
OutputBaseFilename=TimePlanner-Setup-{#AppVersion}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Add a desktop shortcut"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Time Planner"; Filename: "{app}\TimePlanner.Widget.exe"
Name: "{autodesktop}\Time Planner"; Filename: "{app}\TimePlanner.Widget.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TimePlanner.Widget.exe"; Description: "Open Time Planner"; Flags: nowait postinstall

[CustomMessages]
WelcomeText=This installs Time Planner for your Windows account. You don't need an administrator. Your timesheets are kept if Time Planner is already installed.

[Messages]
WelcomeLabel2={cm:WelcomeText}

[Code]
const
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
external 'OpenEventW@kernel32.dll stdcall';

function SetEvent(hEvent: THandle): BOOL;
external 'SetEvent@kernel32.dll stdcall';

function CloseHandle(hObject: THandle): BOOL;
external 'CloseHandle@kernel32.dll stdcall';

var
  UninstallAiChoice: Boolean;
  UninstallDataChoice: Boolean;

procedure AskWidgetToQuit;
var
  hEvent: THandle;
begin
  hEvent := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\TimePlanner.Widget.Quit');
  if hEvent <> 0 then
  begin
    SetEvent(hEvent);
    CloseHandle(hEvent);
  end;
end;

function WaitForWidgetToClose: Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 0 to 40 do
  begin
    if not CheckForMutexes('Local\TimePlanner.Widget') then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;
end;

function EnsureWidgetClosed: Boolean;
var
  promptResult: Integer;
begin
  Result := True;
  if CheckForMutexes('Local\TimePlanner.Widget') then
  begin
    AskWidgetToQuit;
    if not WaitForWidgetToClose then
    begin
      while CheckForMutexes('Local\TimePlanner.Widget') do
      begin
        promptResult := SuppressibleMsgBox(
          'Time Planner is still open. Right-click its icon near the clock and choose Exit, then select Retry.',
          mbError,
          MB_RETRYCANCEL,
          IDCANCEL);
        if promptResult = IDCANCEL then
        begin
          Result := False;
          Exit;
        end;
      end;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := EnsureWidgetClosed;
end;

function InitializeUninstall: Boolean;
begin
  Result := EnsureWidgetClosed;
  if not Result then
  begin
    Exit;
  end;

  UninstallAiChoice := (SuppressibleMsgBox(
    'Also remove Time Planner''s voice AI? This removes the voice models and the AI engine (Ollama) if Time Planner installed it. Your own Ollama is never removed.',
    mbConfirmation,
    MB_YESNO,
    IDYES) = IDYES);

  UninstallDataChoice := (SuppressibleMsgBox(
    'Delete your Time Planner data on this PC too? Timesheets you haven''t sent to the dashboard will be lost. Choose No to keep them.',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2,
    IDNO) = IDYES);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  exePath: string;
  resultCode: Integer;
  runKeyVal: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    if UninstallAiChoice then
    begin
      exePath := ExpandConstant('{app}\TimePlanner.Widget.exe');
      if FileExists(exePath) then
      begin
        Log('Running AI uninstaller: ' + exePath);
        if not Exec(exePath, '--uninstall-ai', '', SW_HIDE, ewWaitUntilTerminated, resultCode) then
        begin
          Log('Failed to launch AI uninstaller');
        end
        else if resultCode <> 0 then
        begin
          Log(Format('AI uninstaller returned exit code %d', [resultCode]));
        end;
      end;
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimePlanner', runKeyVal) then
    begin
      if Pos(ExpandConstant('{app}'), runKeyVal) > 0 then
      begin
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimePlanner');
      end;
    end;

    if UninstallDataChoice then
    begin
      DelTree(ExpandConstant('{localappdata}\TimePlanner'), True, True, True);
    end;
  end;
end;
