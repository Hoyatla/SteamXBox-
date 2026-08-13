; SteamXBox Portable - Installeur Complet "Un Clic"
; Inclut: SteamXBox + ViGEmBus + HidHide (drivers signés Microsoft)
; Compile: iscc SteamXBox_Full_Installer.iss

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

[CustomMessages]
french.InstallViGEmBus=Installer ViGEmBus (bus virtuel manette Xbox/DS4)
english.InstallViGEmBus=Install ViGEmBus (virtual Xbox/DS4 gamepad bus)
french.InstallHidHide=Installer HidHide (masque les manettes physiques)
english.InstallHidHide=Install HidHide (hides physical controllers)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "vigembus"; Description: "{cm:InstallViGEmBus}"
Name: "hidhide"; Description: "{cm:InstallHidHide}"

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

; Agent de mise a jour. Le fichier de configuration doit porter EXACTEMENT le nom de l'executable
; avec l'extension .json — c'est ainsi que l'agent le trouve — et le nom de l'executable lui-meme
; encode le fabricant et le produit : "Hoyatla_SteamXBox_Updater.exe" construit l'adresse
; .../api/Hoyatla/SteamXBox/updates.json.
;
; Installe dans Program Files et nulle part ailleurs : un agent de mise a jour ecrivable par un
; utilisateur non eleve est un moyen d'executer n'importe quoi avec les droits de la tache planifiee.
Source: "updater\Hoyatla_SteamXBox_Updater.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "updater\Hoyatla_SteamXBox_Updater.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Pilotes ViGEmBus + HidHide (embarqués)
Source: "ViGEmBus_1.22.0_x64_x86_arm64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: vigembus
Source: "HidHide_1.5.230_x64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall; Tasks: hidhide

[Registry]
; La version installee, lue par l'agent de mise a jour. C'est le seul moyen qu'il a de savoir ce qui
; tourne sur la machine : il compare cette valeur a celle du manifeste publie. Sans elle, il ne
; proposera jamais rien.
;
; Sous HKLM parce que l'agent est lance par une tache planifiee qui ne sait pas quel utilisateur
; a installe le produit. "uninsdeletekey" emporte la cle entiere a la desinstallation.
Root: HKLM; Subkey: "SOFTWARE\Hoyatla\SteamXBox"; ValueType: string; ValueName: "Version"; \
    ValueData: "{#MyAppVersion}.0.0"; Flags: uninsdeletekey

; Cette entree n'est PAS creee par l'installeur : c'est l'application qui l'ecrit quand l'utilisateur
; coche "lancer au demarrage" dans les parametres. Elle est declaree ici uniquement pour que la
; desinstallation l'emporte.
;
; Sans cela, elle survit au produit. Constate le 11 aout 2026 : le desinstalleur de la 3.2 a laisse
; derriere lui une entree pointant vers un executable qui n'existait plus, et il a fallu la retirer
; a la main. "dontcreatekey" est ce qui distingue "je nettoie ceci" de "je cree ceci".
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "SteamXBox"; Flags: dontcreatekey uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\SteamXBox.ico"; Tasks: desktopicon

[Run]
; 1. ViGEmBus (silencieux, admin)
Filename: "{tmp}\ViGEmBus_1.22.0_x64_x86_arm64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation ViGEmBus (bus virtuel manette)..."; Tasks: vigembus; Flags: waituntilterminated shellexec

; 2. HidHide (silencieux, admin)
Filename: "{tmp}\HidHide_1.5.230_x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installation HidHide (masquage manettes)..."; Tasks: hidhide; Flags: waituntilterminated shellexec

; 3. Agent de mise a jour : autostart + tache planifiee quotidienne. "--install" ne verifie rien et
; ne telecharge rien ; il enregistre seulement l'agent. La premiere verification a lieu a l'ouverture
; de session suivante.
Filename: "{app}\Hoyatla_SteamXBox_Updater.exe"; Parameters: "--install"; StatusMsg: "Enregistrement des mises a jour automatiques..."; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallRun]
; L'agent d'abord : il faut retirer la tache planifiee et l'autostart tant que l'executable est
; encore la. Dans l'autre ordre, la tache resterait a pointer vers un fichier disparu — exactement
; la faute que le desinstalleur de la 3.2 a commise avec son entree de demarrage.
Filename: "{app}\Hoyatla_SteamXBox_Updater.exe"; Parameters: "--uninstall"; Flags: runhidden skipifdoesntexist; RunOnceId: "RemoveUpdater"

; Rendre les manettes avant de partir. Desinstaller pendant que le masquage HidHide est actif laisse
; une manette invisible pour tous les jeux, et le fichier qui dit comment la rendre se trouve dans
; les donnees d'application de l'utilisateur.
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "stop"; Flags: runhidden; RunOnceId: "StopCore"
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "hidhide-off"; Flags: runhidden; RunOnceId: "ReleasePads"

; Les traces des manettes virtuelles que SteamXBox a creees. Windows enregistre tout appareil apparu
; une fois et garde l'enregistrement indefiniment ; chaque manette virtuelle en laisse trois, dont
; deux portent un numero neuf a chaque creation. Constate le 12 aout 2026 : vingt-neuf enregistrements
; accumules, et SteamXBox qui renonce a associer un slot XInput a une manette faute de pouvoir les
; distinguer.
;
; APRES le stop et le hidhide-off : retirer les enregistrements pendant que le produit tient encore
; ses manettes retirerait des appareils en cours d'utilisation.
;
; Seulement ce que le registre a note. La regle "tout VID_045E&PID_028E absent" viserait aussi la
; vraie manette Xbox 360 filaire d'un client, puisque en etre indiscernable est le but meme de
; l'emulation.
Filename: "{app}\SteamXBox.Core.exe"; Parameters: "pads-cleanup"; Flags: runhidden; RunOnceId: "CleanPadRecords"

[Code]
// Prevenir avant de partir : les curseurs de Windows survivent a la desinstallation, et
// l'enregistrement des curseurs d'origine se trouve dans le dossier d'etat de l'utilisateur, que la
// desinstallation ne touche pas. Constate le 11 aout 2026 : 16 des 19 valeurs de
// HKCU\Control Panel\Cursors differaient de la sauvegarde.
//
// L'ecran de desinstallation du produit le dit deja, mais desinstaller depuis le panneau de
// configuration ne passe pas par cet ecran.
function InitializeUninstall(): Boolean;
var
  Backup: String;
begin
  Result := True;
  Backup := ExpandConstant('{localappdata}\SteamXBox\windows-state-backup.json');

  if FileExists(Backup) then
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
