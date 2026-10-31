@echo off
chcp 65001 >nul
cd /d "%~dp0"

rem ============================================================
rem Find csc.exe (.NET Framework 4 compiler). Priority:
rem   1) env var CSC_PATH (user override, highest priority)
rem   2) file csc_path.txt in this folder - one line = full path
rem   3) registry InstallRoot (any drive, any Windows install)
rem   4) standard folders %SystemRoot%\Microsoft.NET\Framework64/Framework
rem   5) scan all v* version folders, newest wins
rem Each phase records its own finding; final pick uses the order above.
rem ============================================================

rem phase 1: env var
set "CSC_ENV="
if defined CSC_PATH if exist "%CSC_PATH%" set "CSC_ENV=%CSC_PATH%"
if defined CSC_PATH if not exist "%CSC_PATH%" echo Warning: env CSC_PATH points to a missing file: %CSC_PATH%

rem phase 2: csc_path.txt
set "CSC_FILE="
if exist "csc_path.txt" set /p CSC_FILE=<"csc_path.txt"
if defined CSC_FILE if not exist "%CSC_FILE%" echo Warning: csc_path.txt points to a missing file: %CSC_FILE%
if defined CSC_FILE if not exist "%CSC_FILE%" set "CSC_FILE="

rem phase 3: registry InstallRoot
set "CSC_REG="
for /f "tokens=3" %%i in ('reg query "HKLM\SOFTWARE\Microsoft\.NETFramework" /v InstallRoot 2^>nul ^| find /i "InstallRoot"') do set "IR=%%i"
if defined IR if "x%IR:~-1%"=="x\" set "IR=%IR:~0,-1"
if defined IR if exist "%IR%\csc.exe" set "CSC_REG=%IR%\csc.exe"
if defined IR if exist "%IR%\v4.0.30319\csc.exe" set "CSC_REG=%IR%\v4.0.30319\csc.exe"

rem phase 4: standard folders
set "CSC_STD="
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC_STD=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if defined CSC_STD goto std_done
if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC_STD=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
:std_done

rem phase 5: scan all version folders, newest wins
set "CSC_SCAN="
for /d %%d in ("%SystemRoot%\Microsoft.NET\Framework64\v*") do if exist "%%d\csc.exe" set "CSC_SCAN=%%d\csc.exe"
for /d %%d in ("%SystemRoot%\Microsoft.NET\Framework\v*") do if exist "%%d\csc.exe" set "CSC_SCAN=%%d\csc.exe"

rem final pick by priority
set "CSC="
if defined CSC_ENV set "CSC=%CSC_ENV%"
if not defined CSC if defined CSC_FILE set "CSC=%CSC_FILE%"
if not defined CSC if defined CSC_REG set "CSC=%CSC_REG%"
if not defined CSC if defined CSC_STD set "CSC=%CSC_STD%"
if not defined CSC if defined CSC_SCAN set "CSC=%CSC_SCAN%"

if not defined CSC (
    echo CSC.EXE NOT FOUND. Windows 10/11 ships it in
    echo   %SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    echo If your compiler is elsewhere, write its full path into
    echo   csc_path.txt  next to build.bat  - one single line
    echo and run build.bat again.
    pause
    exit /b 1
)
echo Using compiler: %CSC%

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:app.ico /out:OSC_Delay.exe delay_proxy.cs Program.cs
if errorlevel 1 (
    echo ERROR BUILD
    pause
    exit /b 1
)
"%CSC%" /nologo /target:exe /optimize+ /codepage:65001 /out:test_proxy.exe delay_proxy.cs test.cs
if errorlevel 1 (
    echo ERROR BUILD TEST
    pause
    exit /b 1
)
"%CSC%" /nologo /target:exe /optimize+ /codepage:65001 /main:OscDelay.UiTest /out:ui_test.exe delay_proxy.cs Program.cs ui_test.cs
if errorlevel 1 (
    echo ERROR BUILD UI TEST
    pause
    exit /b 1
)
echo OK: OSC_Delay.exe test_proxy.exe ui_test.exe
test_proxy.exe
ui_test.exe