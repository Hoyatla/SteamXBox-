@echo off
setlocal
cd /d "%~dp0"

start "" "%~dp0SteamXBox.exe" xbox-run --restart --start-mode native --switch-button steam-or-quick-access
