; SteamXBox Portable Installer - Inno Setup Script
; Compile with: iscc SteamXBox_Installer.iss

#define MyAppName "SteamXBox"
#define MyAppVersion "1.0"
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
OutputDir=.
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
Name: "startup"; Description: "Démarrer SteamXBox au démarrage de Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "SteamXBox.Core.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox-Resident.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start-KeyboardMouse.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start-Native.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start-Xbox360-NoHaptics.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Start-Xbox360.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Stop-SteamXBox.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Install-Startup.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Uninstall-Startup.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox-Autostart.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "HID-Probe.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "HidHide-Off.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "HidHide-Setup.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "HidHide-Status.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "ChangeLog.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "USAGE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "COPYING-GPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE-LGPL-3.0.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "launcher\bin\Release\net8.0\win-x64\*"; DestDir: "{app}\launcher"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Install-Startup.cmd"; Description: "Démarrer SteamXBox au démarrage de Windows"; Flags: runhidden waituntilterminated; Tasks: startup; StatusMsg: "Configuration du démarrage automatique..."

[UninstallRun]
Filename: "{app}\Uninstall-Startup.cmd"; Flags: runhidden waituntilterminated; StatusMsg: "Suppression du démarrage automatique..."

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { Optionally copy SteamXBox.ico to app dir if not already there }
  end;
end;