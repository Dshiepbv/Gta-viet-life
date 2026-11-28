#define MyAppName "GTA Viet Life"
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef MyAppVersion
  #define MyAppVersion "0.4.3"
#endif
[Setup]
AppId={{7D4A6E21-93C1-4D8E-A8F7-6F3A1C5D90B2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\GtaVietLife
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts
OutputBaseFilename=GtaVietLife-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
[Code]
// Dich vu cua ban cu con chay se giu file exe va lam cai dat that bai (DeleteFile code 5).
procedure StopRunningServices;
var
  Names: array[0..2] of String;
  I, ResultCode: Integer;
begin
  Names[0] := 'GtaVietLife.Launcher.exe';
  Names[1] := 'GtaVietLifeEngine.exe';
  Names[2] := 'GtaVietLifeBridge.exe';
  for I := 0 to 2 do
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM ' + Names[I], '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningServices;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  StopRunningServices;
  Result := True;
end;

[InstallDelete]
; module cu (.cs) trong thu muc cai dat khong con duoc phat hanh
Type: filesandordirs; Name: "{app}\game"
[Files]
Source: "{#PublishDir}\launcher\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "{#PublishDir}\runtime\*"; DestDir: "{app}\runtime"; Flags: recursesubdirs ignoreversion
Source: "{#PublishDir}\game\*"; DestDir: "{app}\game"; Flags: recursesubdirs ignoreversion
Source: "{#PublishDir}\manifest.json"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\GTA Viet Life"; Filename: "{app}\GtaVietLife.Launcher.exe"
Name: "{autodesktop}\GTA Viet Life"; Filename: "{app}\GtaVietLife.Launcher.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình"; GroupDescription: "Lối tắt:"
[Run]
Filename: "{app}\GtaVietLife.Launcher.exe"; Description: "Mở GTA Viet Life"; Flags: postinstall nowait skipifsilent
