@echo off
REM VoidWarp Uninstaller Script

echo ========================================
echo   VoidWarp Uninstaller
echo ========================================
echo.

set INSTALL_DIR=%ProgramFiles%\VoidWarp
set START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs

echo Removing VoidWarp...

REM Remove shortcuts
if exist "%START_MENU%\VoidWarp.lnk" del "%START_MENU%\VoidWarp.lnk"
if exist "%USERPROFILE%\Desktop\VoidWarp.lnk" del "%USERPROFILE%\Desktop\VoidWarp.lnk"

REM Remove installation directory
if exist "%INSTALL_DIR%" rmdir /S /Q "%INSTALL_DIR%"

echo.
echo VoidWarp has been uninstalled.
echo.
pause
