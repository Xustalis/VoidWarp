@echo off
REM VoidWarp Windows Installer Script
REM Run from the same folder as VoidWarp.Windows.exe (e.g. after extracting the install package)

echo ========================================
echo   VoidWarp Installer
echo ========================================
echo.

set "SOURCE_DIR=%~dp0"
set INSTALL_DIR=%ProgramFiles%\VoidWarp
set START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs

if not exist "%SOURCE_DIR%VoidWarp.Windows.exe" (
    echo ERROR: VoidWarp.Windows.exe not found in current folder.
    echo Please run this script from the folder containing the extracted files.
    pause
    exit /b 1
)

echo Installing to: %INSTALL_DIR%
echo.

REM Create installation directory
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM Copy files
echo Copying files...
copy /Y "%SOURCE_DIR%VoidWarp.Windows.exe" "%INSTALL_DIR%\"
copy /Y "%SOURCE_DIR%voidwarp_core.dll" "%INSTALL_DIR%\"
copy /Y "%SOURCE_DIR%VoidWarp.Windows.dll" "%INSTALL_DIR%\" 2>nul
copy /Y "%SOURCE_DIR%*.runtimeconfig.json" "%INSTALL_DIR%\" 2>nul
copy /Y "%SOURCE_DIR%*.deps.json" "%INSTALL_DIR%\" 2>nul
copy /Y "%SOURCE_DIR%setup_firewall.bat" "%INSTALL_DIR%\" 2>nul

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
