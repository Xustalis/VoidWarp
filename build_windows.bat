@echo off
REM ========================================
REM VoidWarp Windows Client Build Script
REM Version: 1.0.0
REM ========================================
echo.
echo ========================================
echo VoidWarp Windows Client - Build Script
echo ========================================
echo.

cd /d "%~dp0"

REM Step 1: Check for Rust toolchain
echo [Step 1/4] Checking Rust toolchain...
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
echo [Step 2/4] Building Rust core library...
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
echo   ^> Rust core built successfully: target\release\voidwarp_core.dll
echo.

REM Step 3: Verify DLL exists
echo [Step 3/4] Verifying DLL...
if not exist "target\release\voidwarp_core.dll" (
    echo ERROR: voidwarp_core.dll not found in target\release\
    echo.
    pause
    exit /b 1
)
echo   ^> DLL verified: target\release\voidwarp_core.dll
echo.

REM Step 4: Build C# Windows client
echo [Step 4/4] Building C# Windows client...
cd platforms\windows
echo   ^> Running: dotnet build -c Release
dotnet build -c Release
if errorlevel 1 (
    echo ERROR: C# build failed!
    echo.
    cd ..\..
    pause
    exit /b 1
)
cd ..\..
echo   ^> C# client built successfully
echo.

REM Show output location
echo ========================================
echo BUILD COMPLETED SUCCESSFULLY!
echo ========================================
echo.
echo Output location:
echo   ^> EXE: platforms\windows\bin\Release\net8.0-windows\VoidWarp.Windows.exe
echo   ^> DLL: platforms\windows\bin\Release\net8.0-windows\voidwarp_core.dll
echo.
echo To run the application:
echo   cd platforms\windows\bin\Release\net8.0-windows
echo   VoidWarp.Windows.exe
echo.
pause
