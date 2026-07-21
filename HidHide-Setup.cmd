@echo off
setlocal
cd /d "%~dp0"
SteamXBox.Core.exe hidhide-setup
echo.
echo Press any key to close this window.
pause >nul
