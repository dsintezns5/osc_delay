@echo off
chcp 65001 >nul
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Kompylator csc.exe NET Framework 4 ne nayden.
    pause
    exit /b 1
)
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
