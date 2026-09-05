@echo off
setlocal
cd /d "%~dp0"

rem Banc de mesure DualSense. Lecture seule : rien n'est ecrit sur la manette.
rem Le protocole complet est dans docs\protocole-dualsense-bt.md.
rem
rem Avant de lancer : SenSÉ arrete (Stop-SenSÉ.cmd), HidHide desactive
rem (HidHide-Off.cmd), Steam ferme, une seule manette connectee, en Bluetooth.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo Le SDK .NET 10 est requis pour construire l'instrument de banc.
  echo https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

dotnet run --project tools\DualSenseBench\DualSenseBench.csproj -c Release -- %*

echo.
pause
