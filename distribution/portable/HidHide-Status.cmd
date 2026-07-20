@echo off
setlocal
cd /d "%~dp0"
SteamXBox.Core.exe hidhide-status
echo.
echo Press any key to close this window.
pause >nul
