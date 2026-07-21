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
    [string]$InstallDir = "D:\sauv.minecraft\Modspack perso\Mods\Projets\SteamXBox-portable-win-x64"
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

function Show-Summary {
    Write-Log "=== RÉSUMÉ ===" -Color Cyan
    Write-Log "ViGEmBus : $($global:ViGEmBusOK ? 'OK' : 'ÉCHEC/IGNORÉ')" -Color ($global:ViGEmBusOK ? 'Green' : 'Red')
    Write-Log "HidHide  : $($global:HidHideOK ? 'OK' : 'ÉCHEC/IGNORÉ')" -Color ($global:HidHideOK ? 'Green' : 'Red')
    Write-Log "SteamXBox: $($global:SteamXBoxOK ? 'OK' : 'ÉCHEC/IGNORÉ')" -Color ($global:SteamXBoxOK ? 'Green' : 'Red')
    
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
$global:NeedsReboot = $false

$global:ViGEmBusOK = Install-ViGEmBus
$global:HidHideOK = Install-HidHide
$global:SteamXBoxOK = Setup-SteamXBox

Show-Summary

if ($global:NeedsReboot -and -not $Silent) {
    $choice = Read-Host "Redémarrer maintenant ? (O/N)"
    if ($choice -match '^[oOyY]') { Restart-Computer -Force }
}

Write-Log "Terminé." -Color Green