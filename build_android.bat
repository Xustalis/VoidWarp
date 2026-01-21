@echo off
setlocal

echo ========================================
echo   Building VoidWarp Core for Android
echo ========================================

:: Check for NDK configuration
if not exist ".cargo\config.toml" (
    echo Error: .cargo\config.toml not found!
    echo Please verify NDK configuration.
    exit /b 1
)

:: Build ARM64 (Device)
echo.
echo [1/3] Building for ARM64 (arm64-v8a)...
cd core
cargo build --target aarch64-linux-android --release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    cd ..
    exit /b %ERRORLEVEL%
)
cd ..

echo Copying .so to jniLibs...
copy "core\target\aarch64-linux-android\release\libvoidwarp_core.so" "platforms\android\app\src\main\jniLibs\arm64-v8a\" >nul

:: Build ARMv7 (Old Devices) - Optional, uncomment if needed
:: echo.
:: echo [2/3] Building for ARMv7 (armeabi-v7a)...
:: cd core
:: cargo build --target armv7-linux-androideabi --release
:: cd ..
:: copy "core\target\armv7-linux-androideabi\release\libvoidwarp_core.so" "platforms\android\app\src\main\jniLibs\armeabi-v7a\" >nul

:: Build x86_64 (Emulator) - Optional, uncomment if needed
:: echo.
:: echo [3/3] Building for x86_64 (Emulator)...
:: cd core
:: cargo build --target x86_64-linux-android --release
:: cd ..
:: copy "core\target\x86_64-linux-android\release\libvoidwarp_core.so" "platforms\android\app\src\main\jniLibs\x86_64\" >nul

echo.
echo ========================================
echo   Android Build Complete! ✅
echo ========================================
