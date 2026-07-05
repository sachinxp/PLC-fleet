@echo off
title PLC Simulator (Network Mode)
cd /d "%~dp0"

echo ========================================
echo   PLC Simulator - Network Mode
echo ========================================
echo.

REM --- Add firewall rules (admin required, skip if fails) ---
echo [*] Adding firewall rules (may need admin)...
netsh advfirewall firewall add rule name="PLC Simulator Backend" dir=in action=allow protocol=TCP localport=5000 >nul 2>&1
netsh advfirewall firewall add rule name="PLC Simulator Frontend" dir=in action=allow protocol=TCP localport=5173 >nul 2>&1

REM --- Kill leftover processes ---
echo [*] Cleaning up previous instances...
taskkill /f /im dotnet.exe >nul 2>&1
taskkill /f /im node.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM --- Get LAN IP ---
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4" ^| findstr /v "127.0"') do set "IP=%%a"
set IP=%IP: =%
if "%IP%"=="" set IP=localhost

echo [*] LAN IP: %IP%
echo.

REM --- Start backend ---
echo [*] Starting backend (port 5000)...
start "PLC Backend" /B dotnet run --project src\PLC.Supervisor --urls "http://0.0.0.0:5000"
timeout /t 5 /nobreak >nul

REM --- Start frontend ---
echo [*] Starting frontend (port 5173)...
start "PLC Frontend" /B cmd /c "cd /d "%~dp0frontend" && npx vite --host --port 5173"
timeout /t 3 /nobreak >nul

echo.
echo ========================================
echo   Both services should be running.
echo ========================================
echo.
echo   Local:
echo     Backend:  http://localhost:5000
echo     Frontend: http://localhost:5173
echo.
echo   Network (other machines):
echo     Backend:  http://%IP%:5000
echo     Frontend: http://%IP%:5173
echo.
echo   Press any key to stop both services...
pause >nul

echo [*] Stopping...
taskkill /f /im dotnet.exe >nul 2>&1
taskkill /f /im node.exe >nul 2>&1
echo [*] Done.
