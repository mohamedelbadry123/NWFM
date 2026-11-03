# Stops every running NWFM application, whichever way it was started:
#   - the Docker stack (start-docker.bat): containers removed; host databases and media kept
#   - the development servers (start-dev.bat): the processes holding the NWFM ports and their windows
# Only NWFM's own processes are stopped. Anything else holding an NWFM port is reported, not killed.
$ErrorActionPreference = 'Stop'
trap {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Set-Location (Split-Path $PSScriptRoot -Parent)

$ports = 4200, 4300, 5081, 5180
# Processes start-dev.bat runs: the two .NET hosts (dotnet run) and the Angular dev servers (node).
$devProcessNames = 'NWFM.Api', 'NWFM.Platform.Api', 'dotnet', 'node'
# Docker Desktop's port forwarders; the containers behind them are removed by compose instead.
$dockerProcessNames = 'com.docker.backend', 'com.docker.proxy', 'vpnkit', 'wslrelay', 'docker-proxy'

function Get-PortOwners {
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $ports -contains $_.LocalPort } |
        ForEach-Object {
            $process = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            [pscustomobject]@{ Port = $_.LocalPort; Id = $_.OwningProcess; Name = $process.ProcessName }
        } | Sort-Object Port, Id -Unique
}

# 1. Docker stack.
if (Get-Command docker -ErrorAction SilentlyContinue) {
    & cmd /c 'docker info >nul 2>&1'
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Stopping the NWFM Docker containers...'
        & docker compose down --remove-orphans
        if ($LASTEXITCODE -ne 0) { throw 'docker compose down failed. Review the messages above.' }
    } else {
        Write-Host 'Docker is not running; no containers to stop.'
    }
}

# 2. Development servers, with their process trees (npm and cmd parents included).
$stopped = @()
foreach ($owner in Get-PortOwners) {
    if ($dockerProcessNames -contains $owner.Name) { continue }
    if ($devProcessNames -notcontains $owner.Name) { continue }
    Write-Host "Stopping $($owner.Name) (pid $($owner.Id)) on port $($owner.Port)..."
    & taskkill.exe /PID $owner.Id /T /F | Out-Null
    $stopped += $owner.Id
}
# NWFM hosts that are running but not (yet) listening, e.g. still migrating on startup.
Get-Process -Name 'NWFM.Api', 'NWFM.Platform.Api' -ErrorAction SilentlyContinue |
    Where-Object { $stopped -notcontains $_.Id } |
    ForEach-Object {
        Write-Host "Stopping $($_.ProcessName) (pid $($_.Id))..."
        & taskkill.exe /PID $_.Id /T /F | Out-Null
    }
# The server windows start-dev.bat opened (cmd /k keeps them open after the server exits).
& cmd /c 'taskkill /F /T /FI "IMAGENAME eq cmd.exe" /FI "WINDOWTITLE eq NWFM *" >nul 2>&1'

Start-Sleep -Seconds 2
$remaining = Get-PortOwners
if ($remaining) {
    Write-Host ''
    Write-Host 'These ports are still held by applications this script does not stop:' -ForegroundColor Yellow
    $remaining | ForEach-Object { Write-Host "  port $($_.Port): $($_.Name) (pid $($_.Id))" }
    exit 1
}
Write-Host ''
Write-Host 'All NWFM applications are stopped. Ports 4200, 4300, 5081 and 5180 are free.' -ForegroundColor Green
