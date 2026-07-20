@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "STATE_DIR=%LOCALAPPDATA%\SteamXBox"
set "STOP_FILE=%STATE_DIR%\stop.requested"
set "RESTART_DELAY_SECONDS=5"
set "PING_DELAY_COUNT=6"

if "%~1"=="" (
  echo Usage: %~nx0 ^<SteamXBox arguments^>
  exit /b 64
)

if not exist "%STATE_DIR%" mkdir "%STATE_DIR%" >nul 2>nul
if exist "%STOP_FILE%" del /q "%STOP_FILE%" >nul 2>nul

:WAIT_FOR_DEVICE
if exist "%STOP_FILE%" goto STOPPED

".\SteamXBox.Core.exe" hid-list | findstr /i /c:"No Valve HID device found." >nul
if not errorlevel 1 (
  echo =====================================
  echo [WAIT] Aucun Steam Controller Valve detecte. Nouvelle tentative dans %RESTART_DELAY_SECONDS% secondes...
  ping -n %PING_DELAY_COUNT% 127.0.0.1 >nul
  goto WAIT_FOR_DEVICE
)

:RUN
if exist "%STOP_FILE%" goto STOPPED

echo =====================================
echo [START] SteamXBox.exe %*
".\SteamXBox.Core.exe" %*
set "APP_EXIT=%ERRORLEVEL%"

if exist "%STOP_FILE%" goto STOPPED
if "%APP_EXIT%"=="0" goto NORMAL_EXIT

echo.
echo =====================================
echo [RESTART] SteamXBox s'est arrete avec le code %APP_EXIT%.
echo [RESTART] Relance dans %RESTART_DELAY_SECONDS% secondes. Utilisez Stop-SteamXBox.cmd pour l'arret volontaire.
ping -n %PING_DELAY_COUNT% 127.0.0.1 >nul
goto WAIT_FOR_DEVICE

:STOPPED
if exist "%STOP_FILE%" del /q "%STOP_FILE%" >nul 2>nul
echo =====================================
echo [STOP] Arret volontaire detecte. Le lanceur resident se ferme.
exit /b 0

:NORMAL_EXIT
echo =====================================
echo [STOP] SteamXBox s'est ferme normalement. Pas de relance automatique.
exit /b 0
