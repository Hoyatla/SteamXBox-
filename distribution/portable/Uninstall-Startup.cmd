@echo off
setlocal

set "STEAMXBOX_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SteamXBox.lnk"

if exist "%STEAMXBOX_SHORTCUT%" (
  del /q "%STEAMXBOX_SHORTCUT%"
  echo [OK] Demarrage automatique SteamXBox retire.
) else (
  echo [OK] Aucun raccourci de demarrage automatique SteamXBox a retirer.
)

echo.
echo Press any key to close this window.
pause >nul
