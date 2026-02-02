@echo off
REM ========================================
REM VoidWarp - Build Windows + Android
REM ========================================
REM 若出现「拒绝访问」请先关闭正在运行的 VoidWarp 及 IDE，再在普通 cmd 中执行此脚本。
echo.

cd /d "%~dp0"

echo ========== 1/2 Windows ==========
call build_windows.bat
if errorlevel 1 (
    echo.
    echo [提示] 若因「拒绝访问」失败，请关闭 VoidWarp 与 Cursor 后重试。
    exit /b 1
)

echo.
echo ========== 2/2 Android ==========
call build_android.bat
if errorlevel 1 (
    echo Android 构建失败。
    exit /b 1
)

echo.
echo 在 platforms\android 下执行 gradlew assembleDebug 生成 APK...
cd platforms\android
call gradlew.bat assembleDebug
cd ..\..
if errorlevel 1 (
    echo Gradle 构建失败。请确保已安装 Android SDK 与 NDK。
    exit /b 1
)

echo.
echo ========================================
echo 全部构建完成
echo ========================================
echo Windows: platforms\windows\bin\Release\net8.0-windows\VoidWarp.Windows.exe
echo Android: platforms\android\app\build\outputs\apk\debug\app-debug.apk
echo.
pause
