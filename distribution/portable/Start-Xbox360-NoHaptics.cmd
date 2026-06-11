@echo off
setlocal
cd /d "%~dp0"
SteamXBox.exe xbox-run --restart --no-haptics --switch-button steam-or-quick-access
echo.
echo SteamXBox stopped. Press any key to close this window.
pause >nul
