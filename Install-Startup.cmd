@echo off
setlocal
cd /d "%~dp0"

set "STEAMXBOX_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SteamXBox.lnk"
set "STEAMXBOX_TARGET=%~dp0SteamXBox-Autostart.vbs"
set "STEAMXBOX_WORKDIR=%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut($env:STEAMXBOX_SHORTCUT); $shortcut.TargetPath = $env:STEAMXBOX_TARGET; $shortcut.WorkingDirectory = $env:STEAMXBOX_WORKDIR; $shortcut.IconLocation = (Join-Path $env:STEAMXBOX_WORKDIR 'SteamXBox.exe') + ',0'; $shortcut.Description = 'Start SteamXBox resident launcher at Windows logon'; $shortcut.Save()"
if errorlevel 1 (
  echo [ERROR] Impossible d'installer le demarrage automatique.
  exit /b 1
)

echo [OK] SteamXBox demarrera automatiquement a l'ouverture de session Windows.
echo [OK] Raccourci cree: "%STEAMXBOX_SHORTCUT%"
echo.
echo Press any key to close this window.
pause >nul
