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

rem Avoid starting duplicate servers when the launcher is opened again.
powershell.exe -NoProfile -Command "if (Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"
if errorlevel 1 (
    echo Port 5080 is already in use. No additional backend was started.
) else (
    start "NWFM Backend" /D "%~dp0" "%ComSpec%" /k ""%~f0" backend"
)

powershell.exe -NoProfile -Command "if (Get-NetTCPConnection -LocalPort 4200 -State Listen -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"
if errorlevel 1 (
    echo Port 4200 is already in use. No additional frontend was started.
) else (
    start "NWFM Frontend" /D "%~dp0" "%ComSpec%" /k ""%~f0" frontend"
)

echo.
echo Frontend:     http://localhost:4200
echo API explorer: http://localhost:5080/swagger
echo.
echo Wait for both server windows to report that they are ready.
echo SQL Server must be running with the configured NWFM connection.
echo To stop an application, press Ctrl+C in its server window.
exit /b 0

:backend
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
