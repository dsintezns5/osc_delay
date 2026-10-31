@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist "OSC_Delay.exe" (
    start "" "OSC_Delay.exe"
    exit /b 0
)
echo.
echo OSC_Delay.exe ne nayden. Soderzhite build.bat.
echo Instruktsiya: README_RU.md
pause
