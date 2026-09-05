@echo off
setlocal

set "SenSÉ_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SenSÉ.lnk"

if exist "%SenSÉ_SHORTCUT%" (
  del /q "%SenSÉ_SHORTCUT%"
  echo [OK] Demarrage automatique SenSÉ retire.
) else (
  echo [OK] Aucun raccourci de demarrage automatique SenSÉ a retirer.
)

echo.
echo Press any key to close this window.
pause >nul
