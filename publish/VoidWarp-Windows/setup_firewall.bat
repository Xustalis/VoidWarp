@echo off
REM ========================================
REM VoidWarp Windows Firewall Setup
REM Allows TCP and UDP on port 42424 for discovery and transfer.
REM Run as Administrator for best results.
REM ========================================
setlocal
set "SCRIPT_DIR=%~dp0"
set "EXE_PATH=%SCRIPT_DIR%VoidWarp.Windows.exe"

echo.
echo ========================================
echo VoidWarp - Firewall Rules (Port 42424)
echo ========================================
echo.

REM Remove old rules by name (ignore errors)
echo Removing old VoidWarp firewall rules...
netsh advfirewall firewall delete rule name="VoidWarp" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp Transfer" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp Discovery" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp mDNS" >nul 2>&1
echo.

REM TCP 42424 - Inbound (receive connections + discovery)
echo Adding TCP 42424 Inbound (Private + Public)...
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=private
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=public
echo.

REM TCP 42424 - Outbound
echo Adding TCP 42424 Outbound (Private + Public)...
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=private
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=public
echo.

REM UDP 42424 - Inbound (discovery beacon listener)
echo Adding UDP 42424 Inbound (Private + Public)...
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=private
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=public
echo.

REM UDP 42424 - Outbound (discovery beacon sender)
echo Adding UDP 42424 Outbound (Private + Public)...
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=private
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=public
echo.

REM Optional: mDNS (UDP 5353) for standard mDNS discovery
echo Adding UDP 5353 (mDNS) Inbound + Outbound...
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=private
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=public
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=private
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=public
echo.

echo ========================================
echo Done! VoidWarp firewall rules added.
echo ========================================
echo.
echo Port 42424: TCP + UDP (Private and Public)
echo Port 5353:  UDP mDNS (Private and Public)
echo.
echo If the app is in a different folder, copy this script next to VoidWarp.Windows.exe and run again.
echo.
pause
