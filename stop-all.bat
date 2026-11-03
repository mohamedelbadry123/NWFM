@echo off
setlocal
cd /d "%~dp0"

rem Stops every NWFM application: the Docker stack (start-docker.bat) and the development servers
rem (start-dev.bat), including their server windows. Host databases and Docker volumes are kept.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop-all.ps1"
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
exit /b 0
