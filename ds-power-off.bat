@echo off
chcp 65001 >nul
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"
echo ==========================================
echo  SteamXBox - DualSense power-off test
echo ==========================================
echo.
"%~dp0SteamXBox.Core.exe" ds-power-off > "%TEMP%\ds-off-result.txt" 2>&1
echo.
type "%TEMP%\ds-off-result.txt"
echo.
echo (La sortie est aussi dans "%TEMP%\ds-off-result.txt")
echo.
pause
