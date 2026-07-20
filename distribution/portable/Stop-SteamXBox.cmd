@echo off
setlocal
cd /d "%~dp0"

set "STATE_DIR=%LOCALAPPDATA%\SteamXBox"
set "STOP_FILE=%STATE_DIR%\stop.requested"

if not exist "%STATE_DIR%" mkdir "%STATE_DIR%" >nul 2>nul
> "%STOP_FILE%" echo stop

SteamXBox.Core.exe stop
echo.
echo Press any key to close this window.
pause >nul
