#define MyAppName "NetTactical Agent"
#ifndef MyAppVersion
  #define MyAppVersion "3.0.1"
#endif
#ifndef ExeSource
  #define ExeSource "..\nettactical.exe"
#endif
#define MyAppPublisher "NetTactical"
#define MyAppURL "https://github.com/ArieSandjaya/NetTactical"
#define MyAppExeName "nettactical.exe"
#define MESHEXE "meshagent.exe"
#define MESHDIR "{sd}\Program Files\Mesh Agent"

[Setup]
AppId={{6B1F7C0E-3A52-4D8B-9E47-2C5A8F10B7D3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName="{sd}\Program Files\NetTacticalAgent"
DisableDirPage=yes
#ifdef Arch64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
SetupLogging=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
RestartApplications=no
CloseApplications=no
MinVersion=6.1
VersionInfoVersion={#MyAppVersion}.0
VersionInfoOriginalFileName=nettactical.exe
AppCopyright="Copyright (C) 2026 {#MyAppPublisher}"

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#ExeSource}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "-m cleanup"; RunOnceId: "cleanuprm";
Filename: "{cmd}"; Parameters: "/c taskkill /F /IM nettactical.exe"; RunOnceId: "killnettactical";
Filename: "{app}\{#MESHEXE}"; Parameters: "-fulluninstall"; RunOnceId: "meshrm";

[UninstallDelete]
Type: filesandordirs; Name: "{app}";
Type: filesandordirs; Name: "{#MESHDIR}";

[Code]
const
  LegacyKey = 'SOFTWARE\TacticalRMM';
  NewKey = 'SOFTWARE\NetTactical';
  LegacyDir = '{sd}\Program Files\TacticalAgent';

procedure RunCmd(const Params: String);
var
  ResultCode: Integer;
begin
  Exec('cmd.exe', '/c ' + Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log(Params + ' -> ' + IntToStr(ResultCode));
end;

{ A computer that still runs the old "tacticalrmm" agent: take over its identity and remove it. The old agent's settings (server address,
  agent id, token) are copied to the new registry key, so the asset keeps its history, checks and tasks and does not enrol again. }
procedure MigrateLegacyAgent();
begin
  if not RegKeyExists(HKEY_LOCAL_MACHINE, LegacyKey) then exit;
  Log('Old tacticalrmm agent found, migrating');
  RunCmd('net stop tacticalrmm');
  RunCmd('sc delete tacticalrmm');
  RunCmd('taskkill /F /IM tacticalrmm.exe');
  if not RegKeyExists(HKEY_LOCAL_MACHINE, NewKey) then
    RunCmd('reg copy "HKLM\' + LegacyKey + '" "HKLM\' + NewKey + '" /s /f');
  if RegKeyExists(HKEY_LOCAL_MACHINE, NewKey) then
  begin
    RunCmd('reg delete "HKLM\' + LegacyKey + '" /f');
    RunCmd('reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{0D34D278-5FAF-4159-A4A0-4E2D2C08139D}_is1" /f');
    RunCmd('ping 127.0.0.1 -n 3 && rd /s /q "' + ExpandConstant(LegacyDir) + '"');
  end
  else
    Log('Copying the old settings failed; the old files are left in place');
end;
function InitializeSetup(): boolean;
var
  ResultCode: Integer;
begin
  MigrateLegacyAgent();
  Exec('cmd.exe', '/c ping 127.0.0.1 -n 2 && net stop nettactical', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('Stop nettactical: ' + IntToStr(ResultCode));

  Exec('cmd.exe', '/c taskkill /F /IM nettactical.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('taskkill: ' + IntToStr(ResultCode));

  Result := True;
end;

procedure DeinitializeSetup();
var
  ResultCode: Integer;
  WorkingDir:   String;
begin

  WorkingDir := ExpandConstant('{sd}\Program Files\NetTacticalAgent');
  Exec('cmd.exe', ' /c nettactical.exe -m installsvc', WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('install service: ' + IntToStr(ResultCode));

  Exec('cmd.exe', '/c net start nettactical', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('Start nettactical: ' + IntToStr(ResultCode));
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('cmd.exe', '/c ping 127.0.0.1 -n 2 && net stop nettactical', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('cmd.exe', '/c taskkill /F /IM nettactical.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec('cmd.exe', '/c sc delete nettactical', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('delete nettactical: ' + IntToStr(ResultCode));

  Result := True;
end;
