@echo off
setlocal
cd /d "%~dp0"
SenSÉ.Core.exe hid-probe
echo.
echo Press any key to close this window.
pause >nul
