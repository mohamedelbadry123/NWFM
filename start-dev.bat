@echo off
setlocal
cd /d "%~dp0"

if /i "%~1"=="platform" goto platform
if /i "%~1"=="backend" goto backend
if /i "%~1"=="frontend" goto frontend
if /i "%~1"=="platform-web" goto platformweb

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

rem Verify service identity before reusing occupied ports. Check all before starting any.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" platform
set "platformState=%errorlevel%"
if %platformState% GEQ 2 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" backend
set "backendState=%errorlevel%"
if %backendState% GEQ 2 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" frontend
set "frontendState=%errorlevel%"
if %frontendState% GEQ 2 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" platform-web
set "platformWebState=%errorlevel%"
if %platformWebState% GEQ 2 goto failed

rem The control plane first: it migrates the catalog and every configuration tenant's database.
if "%platformState%"=="0" (
    start "NWFM Platform API" /D "%~dp0" "%ComSpec%" /k ""%~f0" platform"
)
if "%backendState%"=="0" (
    start "NWFM Backend" /D "%~dp0" "%ComSpec%" /k ""%~f0" backend"
)
if "%frontendState%"=="0" (
    start "NWFM Frontend" /D "%~dp0" "%ComSpec%" /k ""%~f0" frontend"
)
if "%platformWebState%"=="0" (
    start "NWFM Platform Web" /D "%~dp0" "%ComSpec%" /k ""%~f0" platform-web"
)

echo.
echo Workspace (nwfm):  http://localhost:4200
echo Other workspaces:  http://^<key^>.localhost:4200
echo Landing + signup:  http://localhost:4300
echo Staff console:     http://localhost:4300/console
echo Tenant API:        http://localhost:5081/swagger
echo Platform API:      http://localhost:5180/swagger
echo.
echo Wait for every server window to report that it is ready.
echo SQL Server must be running with the configured catalog and NWFM connections.
echo To stop an application, press Ctrl+C in its server window.
exit /b 0

:platform
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" platform
if errorlevel 2 exit /b 1
if errorlevel 1 exit /b 0
title NWFM Platform API
echo Starting the control plane. It migrates the catalog and the configured tenant databases.
dotnet run --project "%~dp0backend\src\NWFM.Platform.Api" --launch-profile NWFM.Platform
if errorlevel 1 (
    echo.
    echo Platform API stopped with an error. Review the messages above.
    exit /b 1
)
exit /b 0

:backend
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" backend
if errorlevel 2 exit /b 1
if errorlevel 1 exit /b 0
title NWFM Backend
echo Starting the tenant API. SQL Server and the tenant catalog must be available.
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

:platformweb
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check-dev-port.ps1" platform-web
if errorlevel 2 exit /b 1
if errorlevel 1 exit /b 0
title NWFM Platform Web
cd /d "%~dp0frontend"
if not exist "node_modules\@angular\cli\bin\ng.js" (
    echo Installing frontend dependencies for the first run...
    call npm ci
    if errorlevel 1 exit /b 1
)
call npm run start:platform -- --host 127.0.0.1 --port 4300
exit /b %errorlevel%

:failed
echo.
pause
exit /b 1
