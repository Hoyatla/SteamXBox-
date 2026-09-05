@echo off
chcp 65001 >nul
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"
echo ==========================================
echo  SenSÉ - DualSense BT link cut test
echo ==========================================
echo.
echo Stop SenSÉ first. Watch the pad light:
echo it should go OFF (the pad powers itself off).
echo.
"%~dp0SenSÉ.Core.exe" ds-bt-cut > "%TEMP%\ds-bt-cut-result.txt" 2>&1
echo.
type "%TEMP%\ds-bt-cut-result.txt"
echo.
echo (La sortie est aussi dans "%TEMP%\ds-bt-cut-result.txt")
echo.
pause
