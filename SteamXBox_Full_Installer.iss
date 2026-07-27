; SteamXBox Portable - Installeur Complet "Un Clic"
; Inclut: SteamXBox + ViGEmBus + HidHide (drivers signés Microsoft)
; Compile: iscc SteamXBox_Full_Installer.iss

#define MyAppName "SteamXBox"
#define MyAppVersion "2.1"
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
OutputBaseFilename=SteamXBox_Full_Setup_{#MyAppVersion}_win-x64
SetupIconFile=SteamXBox.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

UsePreviousAppDir=no
UninstallDisplayIcon={app}\SteamXBox.ico

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "vigembus"; Description: "Installer ViGEmBus (bus virtuel manette Xbox/DS4)"
Name: "hidhide"; Description: "Installer HidHide (masque manettes physiques)"

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

; Pilotes ViGEmBus + HidHide (embarqués)
Source: "ViGEmBus_1.22.0_x64_x86_arm64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: vigembus
Source: "HidHide_1.5.230_x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: hidhide

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"; Tasks: desktopicon

[Run]
; 1. ViGEmBus (silencieux, admin)
Filename: "{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation ViGEmBus (bus virtuel manette)..."; Tasks: vigembus; Flags: waituntilterminated shellexec

; 2. HidHide (silencieux, admin)
Filename: "{tmp}\HidHide_1.5.230_x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation HidHide (masquage manettes)..."; Tasks: hidhide; Flags: waituntilterminated shellexec

[UninstallRun]

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Msg: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if IsTaskSelected('vigembus') or IsTaskSelected('hidhide') then
    begin
      Msg := 'L''installation des pilotes (ViGEmBus / HidHide) necessite un redemarrage de Windows.' + #13#10 +
             'Voulez-vous redemarrer maintenant ?';
      if MsgBox(Msg, mbConfirmation, MB_YESNO) = IDYES then
      begin
        Exec('shutdown.exe', '/r /t 5 /c "Redemarrage requis pour ViGEmBus/HidHide"', '', SW_HIDE, ewNoWait, ResultCode);
      end;
    end;
  end;
end;
