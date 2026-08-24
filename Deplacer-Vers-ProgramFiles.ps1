# Deplace le projet vers C:\Program Files\SteamXBox, Outils compris.
#
# Outils pese 84 Go — ComfyUI et ses 72 839 fichiers
# Python, les modeles, ffmpeg, llama.cpp. Le produit, lui, resout tout depuis le dossier de son
# executable : Outils\, Plugins\, Modeles\, Travaux\. Deplacer l'executable sans Outils casserait
# le generateur, le modele de langage, l'agrandissement et le montage.
#
# Une jonction de repertoire resout ce conflit sans une ligne de code : Windows fait suivre
# C:\Program Files\SteamXBox\Outils vers le dossier d'origine, et AppContext.BaseDirectory + "Outils"
# continue de tomber juste.
#
# RIEN N'EST EFFACE. Le dossier d'origine reste entier et devient la sauvegarde, comme demande.
#
# A lancer dans un PowerShell ADMINISTRATEUR : Program Files refuse l'ecriture autrement.

param(
    [string]$Source = "D:\sauv.minecraft\Modspack perso\Mods\Projets\SteamXBox-portable-win-x64",
    [string]$Cible  = "C:\Program Files\SteamXBox",
    [switch]$SansBinaires,
    [switch]$OutilsEnJonction,
    [switch]$PourDeVrai
)

# Par defaut Outils SUIT, et c'est tout l'interet.
#
# Mesure du 23 aout : lecture sequentielle depuis le T7 = 40 Mo/s ; depuis le NVMe interne
# (WD Blue SN580) = 2 348 Mo/s. Un facteur cinquante-neuf. Le modele de langage met 296 s a
# charger, le generateur 130 a 200 s, et ce sont ces lectures qui les expliquent.
#
# -OutilsEnJonction garde l'ancien comportement : Outils reste sur son disque et une jonction
# le fait suivre. A n'employer que si la place manque sur C:.

$ErrorActionPreference = 'Stop'

function Dire($texte, $couleur = 'Gray') { Write-Host $texte -ForegroundColor $couleur }

# --- Verifications avant de toucher a quoi que ce soit ---------------------------------------

$moi = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $moi.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Dire "Ce script doit tourner dans un PowerShell ADMINISTRATEUR." 'Red'
    Dire "Program Files refuse l'ecriture autrement." 'Yellow'
    exit 1
}

if (-not (Test-Path $Source)) { Dire "Source introuvable : $Source" 'Red'; exit 1 }
if (-not (Test-Path (Join-Path $Source 'Outils'))) {
    Dire "Pas de dossier Outils dans la source : ce n'est pas l'installation attendue." 'Red'; exit 1
}

if (Test-Path $Cible) {
    $dedans = Get-ChildItem -Force $Cible -ErrorAction SilentlyContinue
    if ($dedans) {
        Dire "La cible existe deja et n'est pas vide : $Cible" 'Red'
        Dire "Videz-la ou choisissez un autre chemin ; je ne recouvre rien." 'Yellow'
        exit 1
    }
}

# --- Ce qui ne suit pas ----------------------------------------------------------------------

# bin et obj se regenerent a la compilation ;
# les exclure ne coute rien et evite de copier des gigaoctets d'artefacts.
$exclus = @('bin', 'obj')
if ($OutilsEnJonction) { $exclus += 'Outils' }
if ($SansBinaires) { $exclus += @('dist', 'publish', 'preuves-3.2.0') }

# La place, avant de commencer plutot qu'a mi-parcours.
$aCopier = (Get-ChildItem $Source -Force |
    Where-Object { $exclus -notcontains $_.Name } |
    Get-ChildItem -Recurse -File -Force -ErrorAction SilentlyContinue |
    Measure-Object Length -Sum).Sum

$libre = (Get-Volume -DriveLetter ($Cible[0])).SizeRemaining

Dire ("A copier : {0:N1} Go   Libre sur {1}: {2:N1} Go" -f ($aCopier/1GB), $Cible[0], ($libre/1GB))

if ($aCopier * 1.05 -gt $libre) {
    Dire "Pas assez de place. Relancez avec -OutilsEnJonction, ou -SansBinaires." 'Red'
    exit 1
}

$mode = if ($PourDeVrai) { @() } else { @('/L') }

Dire ""
Dire "Source : $Source"
Dire "Cible  : $Cible"
Dire "Exclus : $($exclus -join ', ')"
Dire ""

if (-not $PourDeVrai) {
    Dire "ESSAI A BLANC — rien ne sera copie. Relancez avec -PourDeVrai pour agir." 'Yellow'
    Dire ""
}

# --- La copie ---------------------------------------------------------------------------------

# robocopy plutot que Copy-Item : il gere les chemins longs, reprend ce qui manque, et rend un
# compte-rendu. Ses codes de sortie sous 8 sont des succes, ce que PowerShell ignore.
$arguments = @($Source, $Cible, '/E', '/R:1', '/W:1', '/NFL', '/NDL', '/NP') + $mode
foreach ($e in $exclus) { $arguments += @('/XD', (Join-Path $Source $e)) }

Dire "Copie en cours..." 'Cyan'
& robocopy @arguments | Select-Object -Last 12

if ($LASTEXITCODE -ge 8) {
    Dire "robocopy a rendu $LASTEXITCODE : la copie a echoue." 'Red'
    exit 1
}

if (-not $PourDeVrai) {
    Dire ""
    Dire "Essai a blanc termine. Relancez avec -PourDeVrai." 'Yellow'
    exit 0
}

# --- La jonction vers Outils -------------------------------------------------------------------

if ($OutilsEnJonction) {
    $jonction = Join-Path $Cible 'Outils'
    $vise     = Join-Path $Source 'Outils'

    if (Test-Path $jonction) {
        Dire "Un Outils existe deja dans la cible : je n'y touche pas." 'Yellow'
    } else {
        New-Item -ItemType Junction -Path $jonction -Target $vise | Out-Null
        Dire "Jonction creee : $jonction  ->  $vise" 'Green'
        Dire "Attention : les modeles restent sur le disque lent, rien ne sera plus rapide." 'Yellow'
    }
}

# --- Verification ------------------------------------------------------------------------------

$aVerifier = @(
    'SteamXBox.Desktop.exe',
    'Plugins',
    'Outils\Modeles',
    'Outils\ComfyUI\main.py',
    'Outils\llama.cpp\llama-server.exe'
)

Dire ""
Dire "Verification :" 'Cyan'
$manque = 0
foreach ($v in $aVerifier) {
    $chemin = Join-Path $Cible $v
    if (Test-Path $chemin) { Dire "  OK      $v" 'Green' }
    else { Dire "  MANQUE  $v" 'Red'; $manque++ }
}

Dire ""
if ($manque -eq 0) {
    Dire "Deplacement termine. Le dossier d'origine est intact et sert de sauvegarde." 'Green'
    Dire "Lancez desormais : $Cible\SteamXBox.Desktop.exe" 'Green'
} else {
    Dire "$manque element(s) manquant(s) : ne lancez pas depuis la cible avant d'avoir compris." 'Red'
}
