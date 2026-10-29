; nightOwl installer (Phase 5). Per-user: no admin prompt, installs to %LocalAppData%\Programs\nightOwl.
; Build with tools\build-installer.ps1, which publishes the app and passes AppVersion and SourceDir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\publish"
#endif

[Setup]
AppId={{6E1C2B7A-4F0D-4C8E-9B57-2D9A0F1E6C31}
AppName=nightOwl
AppVersion={#AppVersion}
AppVerName=nightOwl {#AppVersion}
AppPublisher=nightOwl (personal build)
DefaultDirName={autopf}\nightOwl
DefaultGroupName=nightOwl
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\dist
OutputBaseFilename=nightOwl-Setup-{#AppVersion}
SetupIconFile=..\src\NightOwl\assets\nightowl.ico
UninstallDisplayIcon={app}\nightOwl.exe
UninstallDisplayName=nightOwl
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; nightOwl is closed through its quit event in [Code], not by Restart Manager.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml,last-crash.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\nightOwl"; Filename: "{app}\nightOwl.exe"
Name: "{autodesktop}\nightOwl"; Filename: "{app}\nightOwl.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\nightOwl.exe"; Description: "Start nightOwl"; Flags: nowait postinstall

[UninstallDelete]
Type: files; Name: "{app}\last-crash.txt"
Type: dirifempty; Name: "{app}"

[Code]
const
  SYNCHRONIZE = $00100000;
  EVENT_MODIFY_STATE = $0002;

function OpenMutex(Access: DWORD; Inherit: BOOL; Name: string): THandle; external 'OpenMutexW@kernel32.dll stdcall';
function OpenEvent(Access: DWORD; Inherit: BOOL; Name: string): THandle; external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(H: THandle): BOOL; external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(H: THandle): BOOL; external 'CloseHandle@kernel32.dll stdcall';

function CoreRunning: Boolean;
var H: THandle;
begin
  H := OpenMutex(SYNCHRONIZE, False, 'Local\nightOwl.core');
  Result := H <> 0;
  if Result then CloseHandle(H);
end;

{ Ask a running nightOwl (from any folder) to quit, so files can be replaced and the new copy takes over. }
function QuitRunningCore: String;
var H: THandle; I: Integer;
begin
  Result := '';
  if not CoreRunning then exit;
  H := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\nightOwl.quit');
  if H = 0 then begin
    Result := 'nightOwl is running. Quit it from its tray icon, then click Next.';
    exit;
  end;
  SetEvent(H);
  CloseHandle(H);
  for I := 1 to 75 do begin
    if not CoreRunning then exit;
    Sleep(200);
  end;
  Result := 'nightOwl did not quit. Quit it from its tray icon, then try again.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := QuitRunningCore;
  Sleep(1000); { let the wallpaper and guard helpers close }
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var Args: String; Code: Integer;
begin
  if Step <> usUninstall then exit;
  Args := '--uninstall-cleanup';
  if UninstallSilent or
     (MsgBox('Also delete your nightOwl settings, themes, wallpaper list and its Windhawk mods?' + #13#10 +
             'Your wallpaper files themselves are not touched.', mbConfirmation, MB_YESNO) = IDYES) then
    Args := Args + ' --purge';
  { Stops nightOwl and Windhawk (unloading every mod from Explorer), shows the taskbar, removes the Run key. }
  Exec(ExpandConstant('{app}\nightOwl.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
