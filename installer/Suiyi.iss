#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef AppIdentity
  #define AppIdentity "Suiyi.Desktop"
#endif
#ifndef MutexName
  #define MutexName "Local\TranslationFloat.M0"
#endif
#ifndef ExitEventName
  #define ExitEventName "Local\Suiyi.ExitForSetup"
#endif
#ifndef InstallDirectory
  #define InstallDirectory "{localappdata}\Programs\Suiyi"
#endif

[Setup]
AppId={#AppIdentity}
AppName=随译
AppVersion={#AppVersion}
AppPublisher=whoarei
AppPublisherURL=https://github.com/whoarei/suiyi
AppSupportURL=https://github.com/whoarei/suiyi/issues
AppUpdatesURL=https://github.com/whoarei/suiyi/releases
DefaultDirName={#InstallDirectory}
DefaultGroupName=随译
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UsePreviousAppDir=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
WizardStyle=modern
SetupIconFile=..\src\TranslationFloat.App\Assets\Suiyi.ico
UninstallDisplayIcon={app}\TranslationFloat.exe
UninstallDisplayName=随译
AppMutex={#MutexName}
CloseApplications=yes
RestartApplications=no
OutputDir={#SetupOutput}
OutputBaseFilename=suiyi-{#AppVersion}-windows-x64-setup
VersionInfoVersion={#AppFileVersion}
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
ShowLanguageDialog=no

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifndef SmokeTest
[Icons]
Name: "{userprograms}\随译"; Filename: "{app}\TranslationFloat.exe"; AppUserModelID: "Suiyi.Desktop"

[Run]
Filename: "{app}\TranslationFloat.exe"; Description: "运行随译"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateInstall
Filename: "{app}\TranslationFloat.exe"; Flags: nowait; Check: IsUpdateInstall
#endif

[Code]
var
  InstallCompleted: Boolean;

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function IsUpdateInstall: Boolean;
begin
  Result := ExpandConstant('{param:SUIYIUPDATE|0}') = '1';
end;

procedure RequestAppExit;
var
  Handle: THandle;
  Attempt: Integer;
begin
  if not CheckForMutexes('{#MutexName}') then exit;
  Handle := OpenEvent($0002, False, '{#ExitEventName}');
  if Handle <> 0 then begin
    SetEvent(Handle);
    CloseHandle(Handle);
  end;
  if (Handle <> 0) or IsUpdateInstall then
    for Attempt := 1 to 150 do begin
      if not CheckForMutexes('{#MutexName}') then exit;
      Sleep(100);
    end;
end;

function InitializeSetup: Boolean;
begin
  RequestAppExit;
  Result := True;
end;

function InitializeUninstall: Boolean;
begin
  RequestAppExit;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallCompleted := True;
end;

procedure DeinitializeSetup;
var
  ResultCode: Integer;
begin
#ifndef SmokeTest
  { If an update is cancelled or fails, reopen the installed version when it is still available. }
  if IsUpdateInstall and not InstallCompleted and not CheckForMutexes('{#MutexName}') and FileExists(ExpandConstant('{app}\TranslationFloat.exe')) then
    Exec(ExpandConstant('{app}\TranslationFloat.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
#endif
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  Command: String;
begin
#ifndef SmokeTest
  { Remove only this installation's Run entry; keep all user settings and credentials. }
  if (CurStep = usPostUninstall) and RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Suiyi', Command) then
    if CompareText(Command, '"' + ExpandConstant('{app}\TranslationFloat.exe') + '"') = 0 then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Suiyi');
#endif
end;
