@echo off
REM VoidWarp Windows Firewall Setup - TCP/UDP 42424, UDP 5353 (mDNS). Run as Administrator.
setlocal
if "%~1"=="silent" goto :silent

echo.
echo ========================================
echo VoidWarp - Firewall Rules (Port 42424)
echo ========================================
echo.

:do_rules
REM Remove old rules (ignore errors)
netsh advfirewall firewall delete rule name="VoidWarp" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp Transfer" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp Discovery" >nul 2>&1
netsh advfirewall firewall delete rule name="VoidWarp mDNS" >nul 2>&1
if "%~1"=="silent" goto :silent_quiet
echo Removing old VoidWarp firewall rules...
echo.

REM TCP 42424
echo Adding TCP 42424 Inbound + Outbound...
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=public >nul 2>&1
echo.

REM UDP 42424
echo Adding UDP 42424 Inbound + Outbound...
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=public >nul 2>&1
echo.

REM mDNS UDP 5353
echo Adding UDP 5353 (mDNS)...
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=public >nul 2>&1
echo.

echo ========================================
echo Done! VoidWarp firewall rules added.
echo ========================================
echo.
pause
exit /b 0

:silent
REM Installer / automated: no echo, no pause
goto :do_rules
:silent_quiet
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=TCP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=TCP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=in action=allow protocol=UDP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp" dir=out action=allow protocol=UDP localport=42424 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=in action=allow protocol=UDP localport=5353 profile=public >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=private >nul 2>&1
netsh advfirewall firewall add rule name="VoidWarp mDNS" dir=out action=allow protocol=UDP localport=5353 profile=public >nul 2>&1
exit /b 0
