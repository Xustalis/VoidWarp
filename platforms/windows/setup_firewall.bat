@echo off
echo Setting up Windows Firewall rules for VoidWarp...

netsh advfirewall firewall delete rule name="VoidWarp Transfer" >nul 2>&1

echo Allowing TCP Port 42424 (Inbound)...
netsh advfirewall firewall add rule name="VoidWarp Transfer" dir=in action=allow protocol=TCP localport=42424

echo Allowing TCP Port 42424 (Outbound)...
netsh advfirewall firewall add rule name="VoidWarp Transfer" dir=out action=allow protocol=TCP localport=42424

echo Allowing UDP Port 5353 (mDNS)...
netsh advfirewall firewall add rule name="VoidWarp Discovery" dir=in action=allow protocol=UDP localport=5353
netsh advfirewall firewall add rule name="VoidWarp Discovery" dir=out action=allow protocol=UDP localport=5353

echo Done!
pause
