@echo off
REM VoidWarp Windows Installer Script
REM This script installs VoidWarp to the user's Program Files directory

echo ========================================
echo   VoidWarp Installer
echo ========================================
echo.

set INSTALL_DIR=%ProgramFiles%\VoidWarp
set START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs

echo Installing to: %INSTALL_DIR%
echo.

REM Create installation directory
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM Copy files
echo Copying files...
copy /Y "%~dp0VoidWarp.Windows.exe" "%INSTALL_DIR%\"
copy /Y "%~dp0voidwarp_core.dll" "%INSTALL_DIR%\"

REM Create Start Menu shortcut
echo Creating shortcuts...
powershell -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%START_MENU%\VoidWarp.lnk'); $s.TargetPath = '%INSTALL_DIR%\VoidWarp.Windows.exe'; $s.WorkingDirectory = '%INSTALL_DIR%'; $s.Save()"

REM Create Desktop shortcut
powershell -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%USERPROFILE%\Desktop\VoidWarp.lnk'); $s.TargetPath = '%INSTALL_DIR%\VoidWarp.Windows.exe'; $s.WorkingDirectory = '%INSTALL_DIR%'; $s.Save()"

echo.
echo ========================================
echo   Installation Complete!
echo ========================================
echo.
echo VoidWarp has been installed to:
echo   %INSTALL_DIR%
echo.
echo Shortcuts created on Desktop and Start Menu.
echo.
pause
