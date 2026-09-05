@echo off
:: Lanceur Installeur SenSÉ Portable (exécute en Admin)
:: Usage: Install-SenSÉ.bat [--silent] [--no-vigem] [--no-hidhide] [--no-setup] [--dir "C:\path"]

set SCRIPT_DIR=%~dp0
set PS_SCRIPT=%SCRIPT_DIR%Install-SenSÉ.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "Start-Process powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%PS_SCRIPT%\" %*' -Verb RunAs -Wait"

if errorlevel 1 (
    echo.
    echo ERREUR: L'installation a echoue (code %errorlevel%)
    echo Verifiez que vous avez lance en tant qu'Administrateur.
    pause
)