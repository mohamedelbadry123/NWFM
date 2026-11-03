@echo off
setlocal
cd /d "%~dp0"

rem Runs every NWFM application in Docker against the SQL Server installed on this machine,
rem then opens each app in the browser. Usage: start-docker.bat, start-docker.bat stop, start-docker.bat logs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-docker.ps1" %*
if errorlevel 1 (
    echo.
    echo NWFM Docker start failed. Review the messages above.
    pause
    exit /b 1
)
exit /b 0
