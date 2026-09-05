@echo off
setlocal
cd /d "%~dp0"

set "SenSÉ_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SenSÉ.lnk"
set "SenSÉ_TARGET=%~dp0SenSÉ-Autostart.vbs"
set "SenSÉ_WORKDIR=%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut($env:SenSÉ_SHORTCUT); $shortcut.TargetPath = $env:SenSÉ_TARGET; $shortcut.WorkingDirectory = $env:SenSÉ_WORKDIR; $shortcut.IconLocation = (Join-Path $env:SenSÉ_WORKDIR 'SenSÉ.exe') + ',0'; $shortcut.Description = 'Start SenSÉ resident launcher at Windows logon'; $shortcut.Save()"
if errorlevel 1 (
  echo [ERROR] Impossible d'installer le demarrage automatique.
  exit /b 1
)

echo [OK] SenSÉ demarrera automatiquement a l'ouverture de session Windows.
echo [OK] Raccourci cree: "%SenSÉ_SHORTCUT%"
echo.
echo Press any key to close this window.
pause >nul
