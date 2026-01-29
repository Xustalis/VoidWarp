@echo off
REM ========================================
REM VoidWarp Windows Client - Publish
REM ========================================
echo.
echo ========================================
echo VoidWarp Windows Client - Publish
echo ========================================
echo.

cd /d "%~dp0"

REM Step 1: Build Release version
echo [Step 1/4] Building Release version...
call build_windows.bat
if errorlevel 1 (
    echo ERROR: Build failed!
    exit /b 1
)
echo.

REM Step 2: Create publish directory
echo [Step 2/4] Creating publish package...
set PUBLISH_DIR=publish\VoidWarp-Windows
if exist "%PUBLISH_DIR%" (
    echo   ^> Cleaning old publish directory...
    rmdir /s /q "%PUBLISH_DIR%"
)
mkdir "%PUBLISH_DIR%"
echo   ^> Created: %PUBLISH_DIR%
echo.

REM Step 3: Copy files
echo [Step 3/4] Copying files...

set BIN_DIR=platforms\windows\bin\Release\net8.0-windows
if not exist "%BIN_DIR%\VoidWarp.Windows.exe" (
    echo ERROR: Build output not found. Expected: %BIN_DIR%\VoidWarp.Windows.exe
    exit /b 1
)
if not exist "%BIN_DIR%\voidwarp_core.dll" (
    echo ERROR: voidwarp_core.dll not found in %BIN_DIR%\
    exit /b 1
)

REM Copy executable and DLL
echo   ^> Copying executable and DLLs...
copy /Y "%BIN_DIR%\VoidWarp.Windows.exe" "%PUBLISH_DIR%\"
copy /Y "%BIN_DIR%\VoidWarp.Windows.dll" "%PUBLISH_DIR%\"
copy /Y "%BIN_DIR%\voidwarp_core.dll" "%PUBLISH_DIR%\"
if errorlevel 1 (
    echo ERROR: Copy failed!
    exit /b 1
)

REM Copy runtime config
copy /Y "%BIN_DIR%\*.runtimeconfig.json" "%PUBLISH_DIR%\" >nul 2>&1
copy /Y "%BIN_DIR%\*.deps.json" "%PUBLISH_DIR%\" >nul 2>&1

REM Copy firewall and install scripts
echo   ^> Copying scripts...
copy /Y "platforms\windows\setup_firewall.bat" "%PUBLISH_DIR%\setup_firewall.bat"
if exist "platforms\windows\publish\install.bat" copy /Y "platforms\windows\publish\install.bat" "%PUBLISH_DIR%\"
if exist "platforms\windows\publish\uninstall.bat" copy /Y "platforms\windows\publish\uninstall.bat" "%PUBLISH_DIR%\"

REM Create README
echo   ^> Creating README...
(
echo VoidWarp Windows Client
echo =======================
echo.
echo 使用方法：
echo   1. 解压后双击运行 VoidWarp.Windows.exe（或先运行 install.bat 安装）
echo   2. 若 Android 扫描不到本机：请以管理员身份运行 setup_firewall.bat
echo.
echo 功能：
echo   - 设备发现（mDNS + UDP 广播，多网卡兼容）
echo   - 文件发送（选择文件后发送）
echo   - 文件接收（自动监听）
echo   - 实时进度显示
echo   - 日志记录
echo.
echo 系统要求：
echo   - Windows 10/11
echo   - .NET 8.0 Runtime（如未安装，首次运行会提示）
echo.
echo 问题排查：
echo   - Android 发现不了 Windows -^> 1^) 以管理员运行 setup_firewall.bat  2^) 确保 Android 端为最新构建
echo   - 缺少 voidwarp_core.dll -^> 从完整安装包重新解压或运行 build_windows.bat
echo.
) > "%PUBLISH_DIR%\README.txt"

REM Copy LICENSE
if exist "LICENSE" (
    copy /Y "LICENSE" "%PUBLISH_DIR%\" >nul
)

REM Verify required files in publish dir
if not exist "%PUBLISH_DIR%\VoidWarp.Windows.exe" (
    echo ERROR: VoidWarp.Windows.exe missing in %PUBLISH_DIR%
    exit /b 1
)
if not exist "%PUBLISH_DIR%\voidwarp_core.dll" (
    echo ERROR: voidwarp_core.dll missing in %PUBLISH_DIR%
    exit /b 1
)
echo   ^> Files copied and verified
echo.

REM Step 4: Create ZIP install package (requires PowerShell)
echo [Step 4/4] Creating ZIP package...
set ZIP_NAME=VoidWarp-Windows-x64.zip
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%' -DestinationPath 'publish\%ZIP_NAME%' -Force" 2>nul
if exist "publish\%ZIP_NAME%" (
    echo   ^> Created: publish\%ZIP_NAME%
) else (
    echo   ^> ZIP skipped (manual: compress %PUBLISH_DIR% to get install package)
)

REM Show summary
echo.
echo ========================================
echo PUBLISH COMPLETED!
echo ========================================
echo.
echo Package folder: %PUBLISH_DIR%
if exist "publish\%ZIP_NAME%" echo Install package ZIP: publish\%ZIP_NAME%
echo.
echo Contents:
dir /b "%PUBLISH_DIR%"
echo.
echo To distribute: share the folder or publish\%ZIP_NAME%
echo To build .exe installer (optional): install Inno Setup 6, then from project root run:
echo   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" platforms\windows\installer\VoidWarp.iss
echo   Output: publish\output\VoidWarp-Windows-x64-Setup.exe
echo.
echo To test: cd %PUBLISH_DIR% ^& VoidWarp.Windows.exe
echo.
pause
