@echo off
echo ========================================
echo   VoidWarp Quality Verification
echo ========================================
echo.

echo [1/3] Running Unit Tests...
cargo test
if %ERRORLEVEL% NEQ 0 (
    echo Tests Failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Running Static Analysis (Clippy)...
cargo clippy -- -D warnings
if %ERRORLEVEL% NEQ 0 (
    echo Clippy Failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Checking Code Formatting...
cargo fmt -- --check
if %ERRORLEVEL% NEQ 0 (
    echo Formatting Issues Found! Run 'cargo fmt' to fix.
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo   Verification Passed! ✅
echo ========================================
