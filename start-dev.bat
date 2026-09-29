@echo off
setlocal
cd /d "%~dp0"

if /i "%~1"=="backend" goto backend
if /i "%~1"=="frontend" goto frontend

echo Starting NWFM development applications...
echo.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install the .NET 10 SDK, then try again.
    goto failed
)
where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install Node.js 22.12 or newer in the 22.x release line.
    goto failed
)
where npm >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm was not found. Reinstall Node.js with npm included.
    goto failed
)

rem Verify service identity before reusing occupied ports. Check both before starting either.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" backend
set "backendState=%errorlevel%"
if %backendState% GEQ 2 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" frontend
set "frontendState=%errorlevel%"
if %frontendState% GEQ 2 goto failed
if "%backendState%"=="0" (
    start "NWFM Backend" /D "%~dp0" "%ComSpec%" /k ""%~f0" backend"
)

if "%frontendState%"=="0" (
    start "NWFM Frontend" /D "%~dp0" "%ComSpec%" /k ""%~f0" frontend"
)

echo.
echo Frontend:     http://localhost:4200
echo API explorer: http://localhost:5081/swagger
echo.
echo Wait for both server windows to report that they are ready.
echo SQL Server must be running with the configured NWFM connection.
echo To stop an application, press Ctrl+C in its server window.
exit /b 0

:backend
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" backend
if errorlevel 2 exit /b 1
if errorlevel 1 exit /b 0
title NWFM Backend
echo Starting the backend. SQL Server must be available.
dotnet run --project "%~dp0backend\src\NWFM.Api" --launch-profile NWFM
if errorlevel 1 (
    echo.
    echo Backend stopped with an error. Review the messages above.
    exit /b 1
)
exit /b 0

:frontend
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" frontend
if errorlevel 2 exit /b 1
if errorlevel 1 exit /b 0
title NWFM Frontend
cd /d "%~dp0frontend"
if not exist "node_modules\@angular\cli\bin\ng.js" (
    echo Installing frontend dependencies for the first run...
    call npm ci
    if errorlevel 1 exit /b 1
)
call npm start -- --host 127.0.0.1 --port 4200
exit /b %errorlevel%

:failed
echo.
pause
exit /b 1
