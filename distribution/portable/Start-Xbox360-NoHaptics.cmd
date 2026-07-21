@echo off
setlocal
cd /d "%~dp0"

start "" "%~dp0SteamXBox.exe" xbox-run --restart --no-haptics --switch-button steam-or-quick-access
