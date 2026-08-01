; SteamXBox Portable Installer - Inno Setup Script
; Compile with: iscc SteamXBox_Installer.iss

#define MyAppName "SteamXBox"
#define MyAppVersion "3.2"
#define MyAppPublisher "Hoyatla"
#define MyAppURL "https://github.com/Hoyatla/SteamXBox"
#define MyAppExeName "SteamXBox.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=SteamXBox_Setup_{#MyAppVersion}_win-x64
SetupIconFile=SteamXBox.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; SteamXBox executables (self-contained single-file)
Source: "SteamXBox.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Core.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Sc2Xboxed.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion

; Scripts
Source: "Stop-SteamXBox.cmd"; DestDir: "{app}"; Flags: ignoreversion

; Documentation
Source: "ChangeLog.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "USAGE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "COPYING-GPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE-LGPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion

; Icon
Source: "SteamXBox.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"; Tasks: desktopicon

[Run]

[UninstallRun]

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
  end;
end;
