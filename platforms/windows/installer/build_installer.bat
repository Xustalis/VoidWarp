@echo off
REM ========================================
REM VoidWarp Windows Installer Build Script
REM Run from project root publish_windows.bat first
REM ========================================

cd /d "%~dp0"

REM Find ISCC.exe
set "ISCC="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if exist "F:\Inno Setup 6\ISCC.exe" set "ISCC=F:\Inno Setup 6\ISCC.exe"

REM Search in PATH if not found in default locations
if "%ISCC%"=="" (
    for /f "tokens=*" %%i in ('where ISCC.exe 2^>nul') do set "ISCC=%%i"
)

if "%ISCC%"=="" (
    echo ERROR: Inno Setup 6 not found.
    echo Please install from: https://jrsoftware.org/isinfo.php
    pause
    exit /b 1
)

set "PUBLISH=..\..\..\publish\VoidWarp-Windows"
if not exist "%PUBLISH%\VoidWarp.Windows.exe" (
    echo ERROR: Publish folder not ready. 
    echo Please run publish_windows.bat from project root first.
    pause
    exit /b 1
)

echo Building installer with Inno Setup...
echo Path: "%ISCC%"
"%ISCC%" "VoidWarp.iss"
if errorlevel 1 (
    echo ERROR: ISCC failed.
    pause
    exit /b 1
)

echo.
echo SUCCESS! Output: publish\output\VoidWarp-Windows-x64-v1.0.0-Setup.exe
echo.
pause
