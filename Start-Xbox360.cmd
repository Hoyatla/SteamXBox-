@echo off
setlocal
cd /d "%~dp0"

start "" "%~dp0SenSÉ.exe" xbox-run --restart --switch-button steam-or-quick-access
