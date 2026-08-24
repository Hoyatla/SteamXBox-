<# 
.SYNOPSIS
    Installeur complet SteamXBox Portable + dépendances (ViGEmBus + HidHide)
.DESCRIPTION
    Télécharge et installe automatiquement :
    - ViGEmBus (bus virtuel manette Xbox/DS4)
    - HidHide (masque manettes physiques)
    - Configure SteamXBox Portable
.NOTES
    Exécuter en tant qu'Administrateur requis pour les drivers
#>

param(
    [switch]$Silent,
    [switch]$NoViGEmBus,
    [switch]$NoHidHide,
    [switch]$NoSteamXBoxSetup,
    [switch]$NoModeles,

    # L'identifiant d'un jeu de modeles a installer sans rien demander, pour un deploiement
    # automatise. Sans lui, le choix est propose ; avec -Silent et sans lui, rien n'est telecharge.
    [string]$Modeles = "",

    # Le dossier ou le produit est installe : celui de ce script, et non un chemin ecrit en dur.
    #
    # Il portait « D:\sauv.minecraft\... », le chemin de la machine de developpement. La migration
    # du 24 aout vers C:\Program Files\SteamXbox l'a rendu faux, et c'etait la seule reference
    # absolue de tout le projet. Le corriger aurait suffi jusqu'au prochain deplacement.
    #
    # PSScriptRoot est juste partout, sans entretien : le script vit a la racine de l'installation,
    # donc son dossier EST l'installation. Un client qui deballe ailleurs n'a rien a regler.
    [string]$InstallDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$Message, [ConsoleColor]$Color = 'White')
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor $Color
}

function Test-Admin {
    $principal = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Download-File {
    param([string]$Url, [string]$OutFile, [string]$Description)
    Write-Log "Téléchargement $Description..." -Color Yellow
    try {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing -ErrorAction Stop
        Write-Log "OK : $OutFile" -Color Green
        return $true
    } catch {
        Write-Log "ERREUR téléchargement $Description : $_" -Color Red
        return $false
    }
}

function Install-ViGEmBus {
    if ($NoViGEmBus) { Write-Log "ViGEmBus ignoré (--NoViGEmBus)" -Color Yellow; return }
    
    Write-Log "=== Installation ViGEmBus ===" -Color Cyan
    $url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.21.442.0/ViGEmBus_1.21.442_x64_x86_arm64.exe"
    $installer = "$env:TEMP\ViGEmBus_Setup.exe"
    
    if (-not (Download-File $url $installer "ViGEmBus")) { return $false }
    
    $args = "/quiet /norestart"
    if ($Silent) { $args += " /passive" }
    
    Write-Log "Installation ViGEmBus (requiert admin)..." -Color Yellow
    $proc = Start-Process -FilePath $installer -ArgumentList $args -Wait -PassThru -Verb RunAs
    if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
        Write-Log "ViGEmBus installé avec succès" -Color Green
        if ($proc.ExitCode -eq 3010) { $global:NeedsReboot = $true }
        return $true
    } else {
        Write-Log "Échec installation ViGEmBus (code $($proc.ExitCode))" -Color Red
        return $false
    }
}

function Install-HidHide {
    if ($NoHidHide) { Write-Log "HidHide ignoré (--NoHidHide)" -Color Yellow; return }
    
    Write-Log "=== Installation HidHide ===" -Color Cyan
    $url = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
    $installer = "$env:TEMP\HidHide_Setup.exe"
    
    if (-not (Download-File $url $installer "HidHide")) { return $false }
    
    $args = "/quiet /norestart"
    if ($Silent) { $args += " /passive" }
    
    Write-Log "Installation HidHide (requiert admin + redémarrage)..." -Color Yellow
    $proc = Start-Process -FilePath $installer -ArgumentList $args -Wait -PassThru -Verb RunAs
    if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
        Write-Log "HidHide installé avec succès" -Color Green
        $global:NeedsReboot = $true
        return $true
    } else {
        Write-Log "Échec installation HidHide (code $($proc.ExitCode))" -Color Red
        return $false
    }
}

function Setup-SteamXBox {
    if ($NoSteamXBoxSetup) { Write-Log "Setup SteamXBox ignoré" -Color Yellow; return }
    
    Write-Log "=== Configuration SteamXBox Portable ===" -Color Cyan
    
    if (-not (Test-Path $InstallDir)) {
        Write-Log "ERREUR: Dossier introuvable : $InstallDir" -Color Red
        return $false
    }
    
    Set-Location $InstallDir
    
    # Vérifier les exécutables
    $required = @("SteamXBox.exe", "SteamXBox.Core.exe", "SteamXBox-Resident.cmd")
    foreach ($f in $required) {
        if (-not (Test-Path $f)) {
            Write-Log "MANQUANT: $f" -Color Red
            return $false
        }
    }
    Write-Log "Tous les fichiers SteamXBox présents" -Color Green
    
    # Créer raccourci Démarrage si demandé
    $startupDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
    $shortcut = "$startupDir\SteamXBox.lnk"
    if (-not (Test-Path $shortcut)) {
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($shortcut)
        $sc.TargetPath = Join-Path $InstallDir "SteamXBox.exe"
        $sc.Arguments = "xbox-run --restart --switch-button steam-or-quick-access"
        $sc.WorkingDirectory = $InstallDir
        $sc.Description = "SteamXBox - Manette Xbox pour Steam (résident)"
        $sc.Save()
        Write-Log "Raccourci Démarrage créé" -Color Green
    } else {
        Write-Log "Raccourci Démarrage déjà existant" -Color Yellow
    }
    
    # Configurer HidHide pour masquer manettes Xbox/DS4 si installé
    if (Test-Path "$env:ProgramFiles\HidHide\HidHideClient.exe") {
        Write-Log "Configuration HidHide (masquage manettes physiques)..." -Color Yellow
        $hidHideClient = "$env:ProgramFiles\HidHide\HidHideClient.exe"
        # Ajouter règles pour masquer contrôleurs Xbox/DS4 virtuels
        & $hidHideClient add --vid 0x045E --pid 0x02D1 2>$null  # Xbox 360
        & $hidHideClient add --vid 0x045E --pid 0x028E 2>$null  # Xbox One
        & $hidHideClient add --vid 0x054C --pid 0x05C4 2>$null  # DS4
        & $hidHideClient add --vid 0x054C --pid 0x09CC 2>$null  # DS5
        Write-Log "Règles HidHide ajoutées" -Color Green
    }
    
    Write-Log "SteamXBox Portable configuré dans : $InstallDir" -Color Green
    return $true
}

# ===== JEUX DE MODELES =====
#
# La declaration vit dans Modeles\jeux-modeles.json, jamais ici. Ajouter un jeu se fait en editant
# ce fichier ; personne ne retouche l'installeur, et un client peut proposer les siens. C'est la
# meme regle que pour les manifestes d'outils : ce qui change au rythme du monde exterieur n'a rien
# a faire dans un script.
#
# Ce que l'installeur apporte en propre, c'est la seule chose qu'il est seul a savoir : ce que la
# carte de CETTE machine peut porter.

function Get-MemoireVideoLibreMo {
    # Ce qui reste apres le bureau, et non ce que la carte annonce : Windows en garde une part qui
    # ne se rend pas. Mesure sur la machine de developpement : 1404 Mio sur 12282 avant qu'un seul
    # modele ne soit charge. Zero quand on ne sait pas — aucune carte NVIDIA, ou pas de pilote.
    try {
        $lu = & nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader,nounits 2>$null
        if (-not $lu) { return 0 }
        $bouts = ($lu | Select-Object -First 1) -split ','
        if ($bouts.Count -lt 2) { return 0 }
        $utilise = [int]$bouts[0].Trim()
        $total = [int]$bouts[1].Trim()
        if ($total -le 0) { return 0 }
        return [Math]::Max(0, $total - $utilise)
    } catch { return 0 }
}

function Get-MemoireJeuMo {
    param($Jeu)
    # Deduite du plus gros fichier, pas de la somme : ComfyUI charge l'encodeur de texte, s'en sert,
    # le libere, puis charge le modele — les deux ne sont jamais sur la carte ensemble. Un jeu de
    # 13 Go tient donc sur une carte de 12.
    #
    # Deduite plutot que declaree, parce qu'un chiffre ecrit a la main ment : ce produit a annonce
    # pendant des semaines un pic de 20000 Mio sur une carte qui en a 12282, et un modele de 16 Go
    # a ete installe sur cette foi, pour tourner quinze minutes sans jamais calculer.
    if ($Jeu.PSObject.Properties.Name -contains 'memoireVideoMo' -and $Jeu.memoireVideoMo -gt 0) {
        return [int]$Jeu.memoireVideoMo
    }
    $plusGros = ($Jeu.fichiers | Measure-Object -Property octets -Maximum).Maximum
    return [int]([Math]::Floor($plusGros / 1MB))
}

function Get-EtatJeu {
    param($Jeu, [string]$Racine)
    # Constate par la taille exacte et non par la seule presence : un telechargement interrompu
    # laisse un fichier qui existe et qui ment, decouvert bien plus tard sous la forme d'une erreur
    # de format au premier usage.
    $entiers = 0
    foreach ($f in $Jeu.fichiers) {
        $ou = Join-Path $Racine ($f.vers -replace '/', '\')
        if ((Test-Path $ou) -and ((Get-Item $ou).Length -eq $f.octets)) { $entiers++ }
    }
    if ($entiers -eq 0) { return 'absent' }
    if ($entiers -eq $Jeu.fichiers.Count) { return 'installe' }
    return 'partiel'
}

function Get-Poids {
    param([long]$Octets)
    if ($Octets -ge 1GB) { return ('{0:N1} Go' -f ($Octets / 1GB)) }
    return ('{0:N0} Mo' -f ($Octets / 1MB))
}

function Install-JeuModeles {
    param($Jeu, [string]$Racine)

    foreach ($f in $Jeu.fichiers) {
        $ou = Join-Path $Racine ($f.vers -replace '/', '\')
        $nom = Split-Path $ou -Leaf
        $dossier = Split-Path $ou -Parent

        if ((Test-Path $ou) -and ((Get-Item $ou).Length -eq $f.octets)) {
            Write-Log "$nom deja present" -Color Green
            continue
        }

        if (-not (Test-Path $dossier)) { New-Item -ItemType Directory -Force -Path $dossier | Out-Null }

        $part = "$ou.part"
        Write-Log "$nom - $(Get-Poids $f.octets)" -Color Yellow

        # curl reprend la ou il s'est arrete (-C -). Treize gigaoctets sur une ligne domestique,
        # c'est une heure pendant laquelle une coupure est plausible ; recommencer a zero a chaque
        # incident rendrait l'installation impossible sur une mauvaise ligne.
        & curl.exe -L -C - --retry 5 --retry-delay 5 -o $part $f.url
        if ($LASTEXITCODE -ne 0) {
            Write-Log "ECHEC telechargement $nom (curl $LASTEXITCODE). Le fichier partiel est garde, relancer reprendra." -Color Red
            return $false
        }

        $recu = (Get-Item $part).Length
        if ($recu -ne $f.octets) {
            # Le cas qui a mordu : un depot a acces restreint rend une page d'erreur de quelques
            # centaines d'octets, et curl la considere comme un succes. Sans cette verification, le
            # fichier existerait, et rien ne dirait pourquoi le modele refuse de se charger.
            Write-Log "TAILLE INATTENDUE $nom : $recu octets au lieu de $($f.octets). Depot ferme, ou lien change." -Color Red
            return $false
        }

        # Renomme seulement maintenant : un fichier a moitie ecrit qui porterait deja son vrai nom
        # apparaitrait dans la liste des modeles, et le generateur le proposerait.
        Move-Item $part $ou -Force
        Write-Log "OK $nom" -Color Green
    }

    return $true
}

function Setup-Modeles {
    if ($NoModeles) { Write-Log "Jeux de modeles ignores (--NoModeles)" -Color Yellow; return $true }

    Write-Log "=== Jeux de modeles ===" -Color Cyan

    $declaration = Join-Path $InstallDir "Modeles\jeux-modeles.json"
    if (-not (Test-Path $declaration)) {
        Write-Log "Aucune declaration en $declaration - rien a proposer." -Color Yellow
        return $true
    }

    try {
        $jeux = @((Get-Content $declaration -Raw -Encoding UTF8 | ConvertFrom-Json).jeux)
    } catch {
        Write-Log "Declaration illisible : $_" -Color Red
        return $false
    }
    if ($jeux.Count -eq 0) { Write-Log "Declaration vide." -Color Yellow; return $true }

    $racine = Join-Path $InstallDir "Outils\ComfyUI\models"
    $libre = Get-MemoireVideoLibreMo

    if ($libre -gt 0) {
        Write-Log "Memoire video libre : $libre Mio" -Color Gray
    } else {
        Write-Log "Memoire video inconnue : tous les jeux sont montres, a vous de juger." -Color Gray
    }

    # Un conseil par role, et jamais un seul pour tous.
    #
    # Le plus exigeant qui tienne encore : a qualite croissante avec la taille, c'est celui qui
    # exploite la carte sans la deborder. Mais « plus lourd » ne veut dire « meilleur » qu'entre
    # choses comparables — sans le role, la regle conseillait un modele image-video a qui voulait
    # dessiner a partir d'une phrase, au seul motif qu'il etait le plus gros de la liste.
    $conseils = @{}
    if ($libre -gt 0) {
        foreach ($role in ($jeux | ForEach-Object { $_.role } | Select-Object -Unique)) {
            $meilleur = $jeux |
                Where-Object { $_.role -eq $role -and (Get-MemoireJeuMo $_) -le $libre } |
                Sort-Object -Property @{ Expression = { Get-MemoireJeuMo $_ } } -Descending |
                Select-Object -First 1
            if ($meilleur) { $conseils[$role] = $meilleur.id }
        }
    }

    Write-Log ""
    $rang = 0
    $roleCourant = $null
    foreach ($j in ($jeux | Sort-Object -Property role, @{ Expression = { Get-MemoireJeuMo $_ }; Descending = $true })) {
        $rang++

        if ($j.role -ne $roleCourant) {
            $roleCourant = $j.role
            $titre = $roleCourant
            if ($roleCourant -eq 'texte-image') { $titre = "Creer une image a partir d'une description" }
            if ($roleCourant -eq 'image-video') { $titre = "Animer une image en video" }
            Write-Log "  --- $titre ---" -Color Cyan
        }

        $memoire = Get-MemoireJeuMo $j
        $poids = Get-Poids (($j.fichiers | Measure-Object -Property octets -Sum).Sum)
        $etat = Get-EtatJeu $j $racine
        $tient = ($libre -eq 0) -or ($memoire -le $libre)
        $estConseille = $conseils.ContainsKey($j.role) -and $conseils[$j.role] -eq $j.id

        $marque = ""
        if ($estConseille) { $marque = "  <- conseille ici" }
        if ($etat -eq 'installe') { $marque = "  (deja installe)" }
        if (-not $tient) { $marque = "  <- NE TIENT PAS sur cette carte" }

        $couleur = 'White'
        if ($estConseille) { $couleur = 'Green' }
        if (-not $tient) { $couleur = 'DarkGray' }

        Write-Log "  [$rang] $($j.nom)$marque" -Color $couleur
        Write-Log "      $($j.pour)" -Color Gray
        Write-Log "      $poids a telecharger - $memoire Mio de memoire video - licence $($j.licence)" -Color Gray
        if ($j.modules -and $j.modules.Count -gt 0) {
            Write-Log "      module ComfyUI requis : $($j.modules -join ', ')" -Color Gray
        }
    }
    Write-Log ""

    # La liste affichee est triee ; les numeros doivent designer la meme chose.
    $ordonnes = @($jeux | Sort-Object -Property role, @{ Expression = { Get-MemoireJeuMo $_ }; Descending = $true })

    # Choisi d'avance, pour un deploiement automatise.
    if ($Modeles) {
        $choisi = $jeux | Where-Object { $_.id -eq $Modeles } | Select-Object -First 1
        if (-not $choisi) { Write-Log "Jeu inconnu : $Modeles" -Color Red; return $false }
    }
    elseif ($Silent) {
        # Treize gigaoctets sans que personne l'ait demande, ce n'est pas une installation
        # silencieuse, c'est une surprise. Le choix reste a faire, depuis la tuile du produit.
        Write-Log "Mode silencieux : aucun modele telecharge. Choisissez-les depuis la tuile 'Jeux de modeles'." -Color Yellow
        return $true
    }
    else {
        $reponse = Read-Host "Quel jeu installer ? Numero, ou Entree pour passer"

        if ($reponse -match '^\d+$' -and [int]$reponse -ge 1 -and [int]$reponse -le $ordonnes.Count) {
            $choisi = $ordonnes[[int]$reponse - 1]
        }
        else {
            # Rien plutot qu'un defaut : telecharger plusieurs gigaoctets parce que quelqu'un a
            # appuye sur Entree pour passer serait une surprise, pas un service. La tuile du produit
            # permet de le faire plus tard, en connaissance de cause.
            Write-Log "Aucun modele installe. La tuile 'Jeux de modeles' permet de le faire plus tard." -Color Yellow
            return $true
        }
    }

    $memoire = Get-MemoireJeuMo $choisi
    if ($libre -gt 0 -and $memoire -gt $libre) {
        # Refuse, et pas seulement deconseille. C'est l'erreur exacte que cet ecran existe pour
        # empecher : seize gigaoctets telecharges sur une carte de douze, puis quinze minutes de
        # calcul a treize pour cent d'occupation, la carte n'echangeant plus qu'avec la memoire vive.
        Write-Log "« $($choisi.nom) » reclame $memoire Mio et la carte n'en offre que $libre." -Color Red
        Write-Log "Il tournerait en echangeant avec la memoire vive, des dizaines de fois plus lentement. Rien n'est telecharge." -Color Red
        return $false
    }

    if ($choisi.modules -and $choisi.modules.Count -gt 0) {
        Write-Log "Ce jeu demande le module ComfyUI : $($choisi.modules -join ', ')" -Color Yellow
    }

    return (Install-JeuModeles $choisi $racine)
}

# Une ligne de bilan, sans opérateur ternaire.
#
# Le résumé employait « $x ? 'a' : 'b' », qui n'existe qu'à partir de PowerShell 7 : sous le
# powershell.exe livré avec Windows, le script ne démarrait pas du tout — il mourait sur une erreur
# de syntaxe, avant la première ligne, ce qui est la pire des premières impressions pour un
# installeur. Le if/else marche partout et se lit aussi bien.
function Write-Bilan {
    param([string]$Quoi, [bool]$Bon, [string]$SiNon, [ConsoleColor]$CouleurSiNon)

    if ($Bon) { Write-Log "${Quoi}: OK" -Color Green }
    else { Write-Log "${Quoi}: $SiNon" -Color $CouleurSiNon }
}

function Show-Summary {
    Write-Log "=== RÉSUMÉ ===" -Color Cyan
    Write-Bilan "ViGEmBus " $global:ViGEmBusOK "ÉCHEC/IGNORÉ" 'Red'
    Write-Bilan "HidHide  " $global:HidHideOK "ÉCHEC/IGNORÉ" 'Red'
    Write-Bilan "SteamXBox" $global:SteamXBoxOK "ÉCHEC/IGNORÉ" 'Red'
    Write-Bilan "Modèles  " $global:ModelesOK "AUCUN/IGNORÉ" 'Yellow'
    
    if ($global:NeedsReboot) {
        Write-Log "⚠ REDÉMARRAGE REQUIS pour les drivers" -Color Yellow
    }
    
    Write-Log ""
    Write-Log "Pour lancer SteamXBox :"
    Write-Log "  $InstallDir\SteamXBox.exe xbox-run --restart --switch-button steam-or-quick-access"
    Write-Log ""
    Write-Log "Pour arrêter :"
    Write-Log "  $InstallDir\SteamXBox.exe stop"
}

# ===== MAIN =====
Write-Log "=== INSTALLATEUR STEAMXBOX PORTABLE ===" -Color Cyan
Write-Log "Répertoire cible : $InstallDir" -Color Gray

if (-not (Test-Admin)) {
    Write-Log "ERREUR: Exécuter en tant qu'Administrateur requis !" -Color Red
    Write-Log "Relancez PowerShell en Admin et réessayez." -Color Red
    exit 1
}

$global:ViGEmBusOK = $false
$global:HidHideOK = $false
$global:SteamXBoxOK = $false
$global:ModelesOK = $false
$global:NeedsReboot = $false

$global:ViGEmBusOK = Install-ViGEmBus
$global:HidHideOK = Install-HidHide
$global:SteamXBoxOK = Setup-SteamXBox

# Apres la configuration : le choix se fait en connaissance de la machine, et telecharger plusieurs
# gigaoctets avant de savoir si le produit est seulement complet serait du temps perdu.
$global:ModelesOK = Setup-Modeles

Show-Summary

if ($global:NeedsReboot -and -not $Silent) {
    $choice = Read-Host "Redémarrer maintenant ? (O/N)"
    if ($choice -match '^[oOyY]') { Restart-Computer -Force }
}

Write-Log "Terminé." -Color Green