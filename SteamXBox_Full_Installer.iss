; SteamXBox Portable - Installeur Complet "Un Clic"
; Inclut: SteamXBox + ViGEmBus + HidHide (drivers signés Microsoft)
; Compile: iscc SteamXBox_Full_Installer.iss

#define MyAppName "SteamXBox"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "Hoyatla"
#define MyAppURL "https://github.com/Hoyatla/SteamXBox-Explorer"
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
; Outils tiers NON embarques : la case ouvre leur page de telechargement a la fin de
; l'installation, elle n'installe rien. Poppler est sous GPL et ne doit jamais etre livre avec le
; produit ; LibreOffice et Tesseract ne le sont pas davantage, par la meme regle de maintenance.
; Sans eux, SteamXBox fonctionne : les fonctions concernees se detectent absentes et le disent.
french.GetLibreOffice=Ouvrir la page de LibreOffice (conversion de documents)
english.GetLibreOffice=Open the LibreOffice download page (document conversion)
french.GetTesseract=Ouvrir la page de Tesseract OCR (texte des PDF images)
english.GetTesseract=Open the Tesseract OCR download page (text in image PDFs)
french.GetPoppler=Ouvrir la page de Poppler (rendu des pages PDF)
french.GetFfmpeg=Telecharger ffmpeg (agrandissement video) — a decompresser dans Outils\ffmpeg
english.GetFfmpeg=Download ffmpeg (video enlarging) — unzip into Outils\ffmpeg
french.GetPython=Ouvrir la page de Python (outils Real-ESRGAN, ComfyUI)
english.GetPython=Open the Python download page (Real-ESRGAN, ComfyUI tools)
english.GetPoppler=Open the Poppler download page (PDF page rendering)
; Modeles de l'assistant. Trois choix, tous sous licence Apache 2.0 verifiee a la source, donc
; utilisables dans un produit commercial. Un seul suffit : le serveur prend le premier fichier
; .gguf trouve dans Outils\Modeles.
french.ModeleAssistant=Telecharger un modele pour l'assistant (facultatif, un seul suffit)
english.ModeleAssistant=Download a model for the assistant (optional, one is enough)
french.ModeleQwen8=Qwen3-8B — polyvalent, le plus a l'aise pour lancer les outils. 4,7 Go, Apache 2.0
english.ModeleQwen8=Qwen3-8B — all-round, best at driving the tools. 4.7 GB, Apache 2.0
french.ModeleQwen4=Qwen3-4B — pour un ordinateur modeste : deux fois plus leger, un peu moins fin. 2,3 Go, Apache 2.0
english.ModeleQwen4=Qwen3-4B — for a modest computer: half the size, slightly less capable. 2.3 GB, Apache 2.0
french.ModeleGranite=Granite 3.3 8B — d'IBM, concu pour l'appel d'outils et l'usage en entreprise. 4,6 Go, Apache 2.0
english.ModeleGranite=Granite 3.3 8B — from IBM, built for tool calling and business use. 4.6 GB, Apache 2.0

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "vigembus"; Description: "{cm:InstallViGEmBus}"
Name: "hidhide"; Description: "{cm:InstallHidHide}"
; Decochees par defaut : ce sont des telechargements que l'utilisateur choisit, pas des composants
; du produit. Rien ne casse si elles restent decochees.
Name: "getlibreoffice"; Description: "{cm:GetLibreOffice}"; Flags: unchecked
Name: "gettesseract"; Description: "{cm:GetTesseract}"; Flags: unchecked
Name: "getpoppler"; Description: "{cm:GetPoppler}"; Flags: unchecked
; Python n'est pas embarque non plus : c'est un composant systeme que l'utilisateur installe,
; et dont il choisit la version. Les outils qui en ont besoin le detectent, et se taisent sinon.
Name: "getpython"; Description: "{cm:GetPython}"; Flags: unchecked
; ffmpeg porte le decodage et l'encodage de l'outil video. Les deux moteurs sont livres,
; lui non : il est sous LGPL ou GPL selon la compilation choisie par celui qui le distribue,
; et cette incertitude suffit a le garder dehors. Absent, l'outil le dit et s'arrete.
Name: "getffmpeg"; Description: "{cm:GetFfmpeg}"; Flags: unchecked
; L'assistant a besoin d'un modele, et d'un seul. Les trois sont sous Apache 2.0 — verifie a la
; source, pas suppose — donc distribuables dans un produit ferme. Le modele lui-meme n'est jamais
; embarque : plusieurs gigaoctets qui vieillissent vite, et un choix qui appartient a l'utilisateur
; selon la machine qu'il a. Le fichier telecharge se depose dans Outils\Modeles.
Name: "modele"; Description: "{cm:ModeleAssistant}"; Flags: unchecked
Name: "modele\qwen8"; Description: "{cm:ModeleQwen8}"; Flags: exclusive unchecked
Name: "modele\qwen4"; Description: "{cm:ModeleQwen4}"; Flags: exclusive unchecked
Name: "modele\granite"; Description: "{cm:ModeleGranite}"; Flags: exclusive unchecked

[Dirs]
; Cree meme vide : l'utilisateur qui telecharge un modele doit voir ou le poser.
Name: "{app}\Outils\Modeles"

[Files]
; SteamXBox executables (self-contained single-file)
Source: "SteamXBox.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Core.exe"; DestDir: "{app}"; Flags: ignoreversion
; Une incrustation par famille, produite par son propre projet. Un seul binaire etait copie a la
; main sous deux noms : PS5 et Xbox partageaient la meme copie, et rien dans la compilation ne
; produisait ces noms, donc une compilation sans la copie livrait des executables perimes.
Source: "Sc2XboxedSteam.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Sc2XboxedPS5.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Sc2XboxedXbox.Osk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Desktop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox.Indexer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "SteamXBox-Moniteur.exe"; DestDir: "{app}"; Flags: ignoreversion
; Le serveur MCP. Pose et retire avec le produit, jamais lance par lui : c'est le client qui le
; demarre et l'arrete. Sans client installe, ce fichier ne fait rien et n'ouvre aucun port.
Source: "SteamXBox.Mcp.exe"; DestDir: "{app}"; Flags: ignoreversion

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

; Tuiles d'outils. Le chargeur les lit dans {app}\Plugins ; sans elles, le centre de controle
; n'affiche aucun outil. Cinq megaoctets de manifestes et d'icones.
Source: "Plugins\*"; DestDir: "{app}\Plugins"; Flags: ignoreversion recursesubdirs createallsubdirs

; Les graphes de generation. Du contenu du produit, nomme par les manifestes des outils : sans eux,
; « Animer une image » pointe sur un fichier absent et l'outil echoue au clic. Ils vivaient sous
; Outils\ComfyUI\user, un dossier tiers que le depot de ComfyUI ignore et qu'aucun installeur ne
; copiait — les outils livres dependaient donc de fichiers presents sur la seule machine de leur
; auteur.
Source: "Flux\*"; DestDir: "{app}\Flux"; Flags: ignoreversion recursesubdirs createallsubdirs

; Moteurs d'agrandissement et d'interpolation video. Compiles, autonomes, 70 Mo a eux deux, sous
; licences permissives : BSD-3 pour Real-ESRGAN et ncnn, MIT pour RIFE. Ce sont les seuls
; composants tiers que le produit a le droit de livrer. Voir THIRD-PARTY-NOTICES.txt.
Source: "Outils\Real-ESRGAN-ncnn\*"; DestDir: "{app}\Outils\Real-ESRGAN-ncnn"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Outils\rife-ncnn-vulkan\*"; DestDir: "{app}\Outils\rife-ncnn-vulkan"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Outils\LISEZ-MOI.txt"; DestDir: "{app}\Outils"; Flags: ignoreversion skipifsourcedoesntexist

; CE QUI N'EST PAS LIVRE, et ce n'est pas un oubli :
;
;   Outils\ComfyUI       GPL v3. Le livrer imposerait d'ouvrir le produit. Detecte, jamais embarque.
;   Outils\Python        9,5 Go, et sa mise a jour appartient a l'utilisateur. La case "Ouvrir la
;                        page de Python" s'en charge a la fin de l'installation.
;   Outils\Real-ESRGAN   version PyTorch : inutilisable sans Python, et remplacee par la version
;                        ncnn ci-dessus pour la video.
;
; L'outil video ne reclame plus rien d'autre : son pilote est dans le produit (VideoUpscale.cs) et
; les deux moteurs sont ci-dessus. Seul l'outil d'agrandissement d'IMAGES passe encore par Python.

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

; 2 bis. Outils tiers, ouverture de leur page de telechargement seulement.
;
; L'installeur n'embarque ni ne telecharge aucun des trois : il ouvre la page, et l'utilisateur
; decide. Pour Poppler c'est une obligation, pas un choix de confort — sa licence est GPL, et le
; livrer avec un produit ferme exposerait le code de SteamXBox. Pour LibreOffice et Tesseract, c'est
; la meme regle que le projet s'applique depuis le debut : ce qui n'est pas embarque n'est pas a
; corriger chez le client.
;
; Coche ou non, rien ne change dans le produit : les fonctions concernees cherchent ces programmes
; au lancement et se desactivent en le disant si elles ne les trouvent pas.
Filename: "https://www.libreoffice.org/download/download-libreoffice/"; Tasks: getlibreoffice; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://github.com/UB-Mannheim/tesseract/wiki"; Tasks: gettesseract; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://poppler.freedesktop.org/"; Tasks: getpoppler; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://www.python.org/downloads/windows/"; Tasks: getpython; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"; Tasks: getffmpeg; Flags: shellexec nowait postinstall skipifsilent
; Modeles de l'assistant. Liens directs vers le fichier, pas vers une page : l'utilisateur n'a
; qu'a le deposer dans Outils\Modeles. Quantification Q4_K_M, le compromis habituel entre poids
; et qualite. Un seul modele a la fois — le serveur prend le premier .gguf qu'il trouve.
Filename: "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf"; Tasks: modele\qwen8; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf"; Tasks: modele\qwen4; Flags: shellexec nowait postinstall skipifsilent
Filename: "https://huggingface.co/ibm-granite/granite-3.3-8b-instruct-GGUF/resolve/main/granite-3.3-8b-instruct-Q4_K_M.gguf"; Tasks: modele\granite; Flags: shellexec nowait postinstall skipifsilent

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

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Outils\Real-ESRGAN-ncnn"
Type: filesandordirs; Name: "{app}\Outils\rife-ncnn-vulkan"

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
