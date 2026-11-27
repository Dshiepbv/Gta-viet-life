#define MyAppName "GTA Viet Life"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
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
[Files]
Source: "..\publish\launcher\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\publish\runtime\*"; DestDir: "{app}\runtime"; Flags: recursesubdirs ignoreversion
Source: "..\publish\game\*"; DestDir: "{app}\game"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\GTA Viet Life"; Filename: "{app}\GtaVietLife.Launcher.exe"
Name: "{autodesktop}\GTA Viet Life"; Filename: "{app}\GtaVietLife.Launcher.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài màn hình"; GroupDescription: "Lối tắt:"
[Run]
Filename: "{app}\GtaVietLife.Launcher.exe"; Description: "Mở GTA Viet Life"; Flags: postinstall nowait skipifsilent
