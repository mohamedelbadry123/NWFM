param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('backend', 'frontend')]
    [string]$Component
)

# Exit codes: 0 = free, 1 = healthy NWFM service, 2 = conflict or unhealthy service.
$ErrorActionPreference = 'Stop'
$port = if ($Component -eq 'backend') { 5081 } else { 4200 }
try {
    $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    if (-not ($listeners | Where-Object { $_.Port -eq $port })) { exit 0 }

    if ($Component -eq 'backend') {
        $health = Invoke-RestMethod "http://localhost:$port/health" -TimeoutSec 5
        if ($health.application -eq 'NWFM' -and $health.status -eq 'ok') {
            Write-Host "NWFM backend is already running on port $port."
            exit 1
        }
    } else {
        $page = Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 5
        if ($page.StatusCode -eq 200 -and $page.Content -match '<title>NWFM[^<]*</title>') {
            Write-Host "NWFM frontend is already running on port $port."
            exit 1
        }
    }
} catch {
    Write-Host "Could not verify the NWFM ${Component}: $($_.Exception.Message)"
}

Write-Host "ERROR: Port $port is occupied, but a healthy NWFM $Component could not be verified."
Write-Host 'Resolve the conflicting application or unhealthy server, then rerun start-dev.bat.'
Write-Host 'NWFM development uses frontend 4200 and backend 5081. No alternate port will be selected.'
exit 2
