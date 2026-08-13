; SteamXBox Portable Installer - Inno Setup Script
; Compile with: iscc SteamXBox_Installer.iss

#define MyAppName "SteamXBox"
#define MyAppVersion "4.7"
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
Source: "Sc2XboxedPads.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Sc2XboxedSticks.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Desktop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Indexer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox-Moniteur.exe"; DestDir: "{app}"; Flags: ignoreversion

; Scripts
Source: "Stop-SteamXBox.cmd"; DestDir: "{app}"; Flags: ignoreversion

; Documentation
Source: "ChangeLog.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "USAGE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

; Icon
Source: "SteamXBox.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Ecrite par l'application, pas par cet installeur — declaree ici pour que la desinstallation
; l'emporte. Voir SteamXBox_Full_Installer.iss : celle de la 3.2 a survecu a sa propre
; desinstallation en pointant vers un fichier disparu.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "SteamXBox"; Flags: dontcreatekey uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"; Tasks: desktopicon

[Run]

[UninstallRun]
; Rendre les manettes avant de partir : desinstaller pendant que le masquage HidHide est actif
; laisse une manette invisible pour tous les jeux.
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "stop"; Flags: runhidden; RunOnceId: "StopCore"
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "hidhide-off"; Flags: runhidden; RunOnceId: "ReleasePads"

; Les traces des manettes virtuelles creees par le produit. Voir le commentaire etendu dans
; SteamXBox_Full_Installer.iss : Windows garde indefiniment l'enregistrement de tout appareil apparu
; une fois, chaque manette virtuelle en laisse trois, et leur accumulation empeche SteamXBox
; d'associer un slot XInput a une manette.
;
; Apres le stop et le hidhide-off, et limite a ce que le registre a note.
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "pads-cleanup"; Flags: runhidden; RunOnceId: "CleanPadRecords"

[Code]
// Voir SteamXBox_Full_Installer.iss pour le detail : les curseurs de Windows survivent a la
// desinstallation, et l'enregistrement des curseurs d'origine part avec les donnees d'application.
function InitializeUninstall(): Boolean;
begin
  Result := True;

  if FileExists(ExpandConstant('{localappdata}\SteamXBox\windows-state-backup.json')) then
  begin
    if MsgBox('SteamXBox a remplace les curseurs de Windows.' + #13#10#13#10 +
              'Ils resteront en place apres la desinstallation, et l''enregistrement de vos ' +
              'curseurs d''origine se trouve dans vos donnees d''application.' + #13#10#13#10 +
              'Voulez-vous continuer la desinstallation ?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
  end;
end;
