@echo off
REM VoidWarp Firewall Helper
REM Adds inbound firewall rules for:
REM - mDNS (UDP 5353) discovery
REM - VoidWarp file receive (TCP 0-65535 for the app binary; simplest for dev)
REM
REM NOTE: Requires Administrator privileges.

echo ========================================
echo   VoidWarp Firewall Rules
echo ========================================
echo.

set APP_DIR=%~dp0
set EXE=%APP_DIR%VoidWarp.Windows.exe

if not exist "%EXE%" (
  echo ERROR: %EXE% not found.
  echo Run this script from the publish folder that contains VoidWarp.Windows.exe
  echo.
  pause
  exit /b 1
)

echo Adding mDNS inbound rule (UDP 5353)...
netsh advfirewall firewall add rule name="VoidWarp mDNS Discovery (UDP 5353)" dir=in action=allow protocol=UDP localport=5353 program="%EXE%" profile=any >nul 2>&1

echo Adding VoidWarp app inbound rule (TCP)...
netsh advfirewall firewall add rule name="VoidWarp File Receive (TCP)" dir=in action=allow protocol=TCP program="%EXE%" profile=any >nul 2>&1

echo Adding VoidWarp app inbound rule (UDP)...
netsh advfirewall firewall add rule name="VoidWarp Transport (UDP)" dir=in action=allow protocol=UDP program="%EXE%" profile=any >nul 2>&1

echo.
echo Done.
echo If rules already exist, Windows may report an error but it is safe.
echo.
pause

