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

#ifdef TauriBuild
[InstallDelete]
#include "legacy-wpf-files.iss"
#endif

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

#ifdef TauriBuild
const
  WebViewClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2: Boolean;
var
  Version: String;
begin
#ifdef SmokeTest
  Result := RegQueryStringValue(HKCU, '{#WebViewProbeKey}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
#else
  Result := (RegQueryStringValue(HKLM32, WebViewClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, WebViewClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Bootstrapper, QuotedBootstrapper, Script, Parameters: String;
  ExitCode: Integer;
  InstallCount: Cardinal;
begin
  Result := '';
  if HasWebView2 then begin
    Log('WebView2 Runtime detected; no download required.');
    exit;
  end;
#ifdef SmokeTest
  { Only this test's fake registry key changes; no network or system installation. }
  RegQueryDWordValue(HKCU, '{#WebViewProbeKey}', 'InstallCount', InstallCount);
  RegWriteDWordValue(HKCU, '{#WebViewProbeKey}', 'InstallCount', InstallCount + 1);
  RegWriteStringValue(HKCU, '{#WebViewProbeKey}', 'pv', '157.0.0.0');
  Log('TEST: installed missing WebView2 fixture.');
  if not HasWebView2 then Result := 'WebView2 测试替身安装失败。';
#else
  try
    WizardForm.PreparingLabel.Caption := '正在下载并安装 Microsoft WebView2 Runtime…';
    DownloadTemporaryFile('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '', nil);
    Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
    QuotedBootstrapper := Bootstrapper;
    StringChangeEx(QuotedBootstrapper, '''', '''''', True);
    Script := '$s=Get-AuthenticodeSignature -LiteralPath ''' + QuotedBootstrapper + '''; if($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -match ''(^|, )O=Microsoft Corporation(,|$)''){exit 0}else{exit 1}';
    Parameters := '-NoProfile -NonInteractive -WindowStyle Hidden -Command "' + Script + '"';
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then begin
      Result := 'WebView2 安装程序的微软签名校验失败，请检查网络后重试。';
      exit;
    end;
    if (not Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) or (not HasWebView2) then
      Result := '未能安装 WebView2 Runtime，请从微软网站安装后重试。';
  except
    Result := '无法下载 WebView2 Runtime，请检查网络后重试，或先手动安装微软 WebView2 Runtime。';
  end;
#endif
end;
#endif

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
