@echo off
REM ========================================
REM VoidWarp Windows Client - Publish (Self-Contained)
REM Creates a standalone package that requires no environment
REM ========================================
echo.
echo ========================================
echo VoidWarp Windows Client - Self-Contained Publish
echo ========================================
echo.

cd /d "%~dp0"

REM Step 1: Check for Rust toolchain
echo [Step 1/5] Checking Rust toolchain...
where cargo >nul 2>&1
if errorlevel 1 (
    echo ERROR: Cargo not found! Please install Rust from https://rustup.rs/
    echo.
    pause
    exit /b 1
)
echo   ^> Rust toolchain found
echo.

REM Step 2: Build Rust core library
echo [Step 2/5] Building Rust core library...
cd core
echo   ^> Running: cargo build --release
cargo build --release
if errorlevel 1 (
    echo ERROR: Rust build failed!
    echo.
    cd ..
    pause
    exit /b 1
)
cd ..
echo   ^> Rust core built successfully
echo.

REM Step 3: Verify DLL exists
echo [Step 3/5] Verifying DLL...
if not exist "target\release\voidwarp_core.dll" (
    echo ERROR: voidwarp_core.dll not found in target\release\
    echo.
    pause
    exit /b 1
)
echo   ^> DLL verified: target\release\voidwarp_core.dll
echo.

REM Step 4: Build Self-Contained Windows client
echo [Step 4/5] Building self-contained Windows client...
echo   ^> This produces a standalone package with embedded .NET Runtime
cd platforms\windows
echo   ^> Running: dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false
if errorlevel 1 (
    echo ERROR: dotnet publish failed!
    echo.
    cd ..\..
    pause
    exit /b 1
)
cd ..\..
echo   ^> Self-contained client built successfully
echo.

REM Step 5: Create publish directory
echo [Step 5/5] Creating publish package...
set PUBLISH_DIR=publish\VoidWarp-Windows
if exist "%PUBLISH_DIR%" (
    echo   ^> Cleaning old publish directory...
    rmdir /s /q "%PUBLISH_DIR%"
)
mkdir "%PUBLISH_DIR%"
echo   ^> Created: %PUBLISH_DIR%
echo.

REM Copy files from self-contained publish output
set BIN_DIR=platforms\windows\bin\Release\net8.0-windows\win-x64\publish
if not exist "%BIN_DIR%\VoidWarp.Windows.exe" (
    echo ERROR: Build output not found. Expected: %BIN_DIR%\VoidWarp.Windows.exe
    exit /b 1
)

REM Copy all files from publish folder (includes .NET runtime)
echo   ^> Copying self-contained files (includes .NET Runtime)...
xcopy /E /Y /Q "%BIN_DIR%\*" "%PUBLISH_DIR%\"
if errorlevel 1 (
    echo ERROR: Copy failed!
    exit /b 1
)

REM Ensure voidwarp_core.dll is copied
if not exist "%PUBLISH_DIR%\voidwarp_core.dll" (
    copy /Y "target\release\voidwarp_core.dll" "%PUBLISH_DIR%\"
)

REM Copy firewall setup script
echo   ^> Copying scripts...
copy /Y "platforms\windows\setup_firewall.bat" "%PUBLISH_DIR%\setup_firewall.bat"
if exist "platforms\windows\publish\install.bat" copy /Y "platforms\windows\publish\install.bat" "%PUBLISH_DIR%\"
if exist "platforms\windows\publish\uninstall.bat" copy /Y "platforms\windows\publish\uninstall.bat" "%PUBLISH_DIR%\"

REM Create README
echo   ^> Creating README...
(
echo VoidWarp Windows Client - Self-Contained Edition
echo =================================================
echo.
echo Usage:
echo   1. Run VoidWarp.Windows.exe directly
echo   2. Or run install.bat to create shortcuts
echo   3. If Android cannot find Windows: Run setup_firewall.bat as Administrator
echo.
echo Features:
echo   - No .NET Runtime installation required
echo   - No VC++ Redistributable required
echo   - Ready to use out of the box
echo.
echo System Requirements:
echo   - Windows 10/11 x64
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

REM Create ZIP install package (requires PowerShell)
echo Creating ZIP package...
set ZIP_NAME=VoidWarp-Windows-x64.zip
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%' -DestinationPath 'publish\%ZIP_NAME%' -Force" 2>nul
if exist "publish\%ZIP_NAME%" (
    echo   ^> Created: publish\%ZIP_NAME%
) else (
    echo   ^> ZIP skipped
)

REM Show summary
echo.
echo ========================================
echo SELF-CONTAINED PUBLISH COMPLETED!
echo ========================================
echo.
echo Package folder: %PUBLISH_DIR%
if exist "publish\%ZIP_NAME%" echo Install package ZIP: publish\%ZIP_NAME%
echo.
echo NEXT STEP: Build installer (optional)
echo   cd platforms\windows\installer
echo   build_installer.bat
echo.
echo This package is SELF-CONTAINED:
echo   - No .NET Runtime installation required
echo   - No VC++ Redistributable required
echo   - Just run VoidWarp.Windows.exe directly!
echo.
pause
