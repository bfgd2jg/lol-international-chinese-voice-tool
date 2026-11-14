#ifndef AppVersion
  #error AppVersion is required
#endif
#define AppName "LOL国际服改中文语音工具"
#define AppExe AppName + ".exe"
#define AppMutex "Local\LeagueChineseLocaleLauncher"
#ifndef PackageAppId
  #define PackageAppId "LeagueChineseLocaleLauncher"
#endif

[Setup]
AppId={#PackageAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=bfgd2jg
AppPublisherURL=https://github.com/bfgd2jg/lol-international-chinese-voice-tool
DefaultDirName={localappdata}\Programs\LeagueChineseVoiceTool
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
MinVersion=10.0
AppMutex={#AppMutex}
CloseApplications=no
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\assets\app-icon.ico
OutputDir=..\artifacts\installer
OutputBaseFilename=LOL-Chinese-Voice-Tool-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "..\artifacts\installer-tools\ChineseSimplified.isl"

[Files]
Source: "..\artifacts\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait skipifnotsilent; Check: IsToolUpdate

[Code]
function IsToolUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function InitializeSetup: Boolean;
var
  Attempt: Integer;
begin
  if IsToolUpdate then
    for Attempt := 1 to 40 do
    begin
      if not CheckForMutexes('{#AppMutex}') then Break;
      Sleep(250);
    end;
  Result := True;
end;
