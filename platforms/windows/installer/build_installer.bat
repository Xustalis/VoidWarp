@echo off
REM 在项目根目录运行 publish_windows.bat 后，再运行本脚本生成 Setup.exe
REM 需要已安装 Inno Setup 6: https://jrsoftware.org/isinfo.php

cd /d "%~dp0"

set "ISCC="
if exist "F:\Inno Setup 6\ISCC.exe" set "ISCC=F:\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
where ISCC.exe >nul 2>&1 && for /f "tokens=*" %%i in ('where ISCC.exe') do set "ISCC=%%i"

if "%ISCC%"=="" (
    echo ERROR: Inno Setup 6 not found.
    echo Please install from: https://jrsoftware.org/isinfo.php
    echo Default path: C:\Program Files (x86)\Inno Setup 6\ISCC.exe
    pause
    exit /b 1
)

set "PUBLISH=..\..\..\publish\VoidWarp-Windows"
if not exist "%PUBLISH%\VoidWarp.Windows.exe" (
    echo ERROR: Publish folder not ready. Run from project root: publish_windows.bat
    pause
    exit /b 1
)

echo Building installer with Inno Setup...
"%ISCC%" "VoidWarp.iss"
if errorlevel 1 (
    echo ERROR: ISCC failed.
    pause
    exit /b 1
)

echo.
echo SUCCESS. Output: publish\output\VoidWarp-Windows-x64-Setup.exe
pause
