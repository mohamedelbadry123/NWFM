param(
    [ValidateSet('up', 'stop', 'logs')]
    [string]$Action = 'up'
)

# Runs the four NWFM applications in Docker against the SQL Server installed on this machine.
#   up    create .env if needed, provision the host logins, start the stack and open the apps
#   stop  remove the containers (host databases and the media volume are kept)
#   logs  follow the containers' logs
$ErrorActionPreference = 'Stop'
trap {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$envFile = Join-Path $root '.env'
$sqlPasswordKeys = 'NWFM_SQL_OWNER_PASSWORD', 'NWFM_RUNTIME_PASSWORD', 'NWFM_CATALOG_READER_PASSWORD', 'NWFM_PLATFORM_ADMIN_PASSWORD'
$signingKeyKeys = 'NWFM_JWT_SIGNING_KEY', 'NWFM_PLATFORM_JWT_SIGNING_KEY'
$defaults = [ordered]@{
    NWFM_COMPOSE_PROJECT = 'nwfm-local'
    NWFM_TENANT_ID       = '10000000-0000-0000-0000-000000000001'
    NWFM_SQL_HOST        = 'host.docker.internal'
    NWFM_SQL_PORT        = '1433'
    NWFM_SQL_OWNER_USER  = 'nwfm_docker_owner'
}
$services = @(
    @{ Name = 'platform'; Port = 5180 },
    @{ Name = 'backend'; Port = 5081 },
    @{ Name = 'frontend'; Port = 4200 },
    @{ Name = 'platform-web'; Port = 4300 }
)
$appUrls = @(
    'http://localhost:4200',
    'http://localhost:4300',
    'http://localhost:4300/console',
    'http://localhost:5081/swagger',
    'http://localhost:5180/swagger'
)

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed with exit code $LASTEXITCODE." }
}

$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

function Get-RandomIndex([int]$Count) {
    $bytes = New-Object byte[] 4
    $rng.GetBytes($bytes)
    return [int]([BitConverter]::ToUInt32($bytes, 0) % [uint32]$Count)
}

function New-Secret([int]$Length) {
    # Starts with a letter and holds upper, lower, digit and symbol characters, satisfying SQL
    # Server's and Identity's password policies and the provisioning scripts' allowlist.
    $sets = 'ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!-_'
    $all = -join $sets
    $chars = New-Object char[] ($Length - 1)
    for ($i = 0; $i -lt $chars.Length; $i++) {
        $set = if ($i -lt $sets.Count) { $sets[$i] } else { $all }
        $chars[$i] = $set[(Get-RandomIndex $set.Length)]
    }
    for ($i = $chars.Length - 1; $i -gt 0; $i--) {
        $j = Get-RandomIndex ($i + 1)
        $chars[$i], $chars[$j] = $chars[$j], $chars[$i]
    }
    $letters = $sets[0] + $sets[1]
    return [string]$letters[(Get-RandomIndex $letters.Length)] + (-join $chars)
}

function New-SigningKey {
    $bytes = New-Object byte[] 48
    $rng.GetBytes($bytes)
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function Read-EnvFile {
    $values = [ordered]@{}
    foreach ($line in [System.IO.File]::ReadAllLines($envFile)) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    return $values
}

# Creates .env from .env.example on the first run and fills every missing or empty value, keeping
# values that are already set. Returns the loaded settings.
function Initialize-EnvFile {
    if (-not (Test-Path $envFile)) {
        Copy-Item (Join-Path $root '.env.example') $envFile
        Write-Host 'Created .env from .env.example.'
    }
    $lines = [System.Collections.Generic.List[string]]::new([string[]][System.IO.File]::ReadAllLines($envFile))
    $values = Read-EnvFile
    $generated = @()
    $wanted = [ordered]@{}
    foreach ($key in $defaults.Keys) { $wanted[$key] = { param($k) $defaults[$k] } }
    foreach ($key in $sqlPasswordKeys) { $wanted[$key] = { param($k) New-Secret 40 } }
    foreach ($key in $signingKeyKeys) { $wanted[$key] = { param($k) New-SigningKey } }

    foreach ($key in $wanted.Keys) {
        if ($values.Contains($key) -and $values[$key]) { continue }
        $value = & $wanted[$key] $key
        $index = -1
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^\s*$key\s*=") { $index = $i; break } }
        if ($index -ge 0) { $lines[$index] = "$key=$value" } else { $lines.Add("$key=$value") }
        $values[$key] = $value
        $generated += $key
    }
    if ($generated.Count -gt 0) {
        [System.IO.File]::WriteAllLines($envFile, $lines, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Filled in .env: $($generated -join ', ')"
    }
    foreach ($key in $values.Keys) { [Environment]::SetEnvironmentVariable($key, $values[$key], 'Process') }
    return $values
}

function Test-ServiceRunning([string]$Service) {
    $id = & docker compose ps --status running -q $Service
    return ($LASTEXITCODE -eq 0 -and $id)
}

function Assert-PortsAvailable {
    $listening = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        ForEach-Object { $_.Port }
    $blocked = $services | Where-Object { $listening -contains $_.Port -and -not (Test-ServiceRunning $_.Name) }
    if ($blocked) {
        $ports = ($blocked | ForEach-Object { $_.Port }) -join ', '
        throw "Port(s) $ports are in use by another application. Stop the start-dev.bat servers (or whatever holds them) first; NWFM never switches ports."
    }
}

function Show-ServiceLogs([string]$Service) {
    Write-Host ''
    Write-Host "Last log lines from ${Service}:" -ForegroundColor Yellow
    & docker compose logs --tail 50 $Service
}

# Polls until $Check returns true, failing fast when the service's container has exited.
function Wait-Until([string]$Service, [string]$Description, [int]$TimeoutSeconds, [scriptblock]$Check) {
    Write-Host "Waiting for $Description..." -NoNewline
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try { if (& $Check) { Write-Host ' ready.'; return } } catch { }
        $exited = & docker compose ps --status exited -q $Service
        if ($exited) {
            Write-Host ' stopped.'
            Show-ServiceLogs $Service
            throw "The $Service container stopped. Review the log lines above."
        }
        Write-Host '.' -NoNewline
        Start-Sleep -Seconds 3
    }
    Write-Host ' timed out.'
    Show-ServiceLogs $Service
    throw "$Description did not become ready within $TimeoutSeconds seconds."
}

function Test-Health([int]$Port, [string]$Application) {
    $health = Invoke-RestMethod "http://localhost:$Port/health" -TimeoutSec 5
    return ($health.application -eq $Application -and $health.status -eq 'ok')
}

function Test-Page([int]$Port) {
    return ((Invoke-WebRequest "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200)
}

function Assert-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker was not found. Install Docker Desktop, then try again.' }
    & cmd /c 'docker info >nul 2>&1'
    if ($LASTEXITCODE -ne 0) { throw 'Docker is not running. Start Docker Desktop, wait until it is ready, then try again.' }
}

function Initialize-HostLogins($settings) {
    if ($settings['NWFM_SQL_HOST'] -ne 'host.docker.internal') {
        Write-Host "NWFM_SQL_HOST is $($settings['NWFM_SQL_HOST']): skipping host login provisioning. That server needs the owner, nwfm_request and nwfm_catalog_reader logins." -ForegroundColor Yellow
        return $false
    }
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        throw 'Install the Microsoft SQL Server command-line tools (sqlcmd) to provision the host logins.'
    }
    # sqlcmd substitutes these in SQL literals and identifiers; strict allowlists prevent injection.
    if ($settings['NWFM_SQL_OWNER_USER'] -cnotmatch '^[A-Za-z_][A-Za-z0-9_]{0,63}$') {
        throw 'NWFM_SQL_OWNER_USER must be a plain SQL login name (letters, digits, underscores).'
    }
    if ($settings['NWFM_SQL_OWNER_PASSWORD'] -cnotmatch '^[A-Za-z0-9_!-]{32,128}$') {
        throw 'NWFM_SQL_OWNER_PASSWORD must be 32-128 ASCII letters, digits, underscores, exclamation marks or hyphens.'
    }
    if ($settings['NWFM_SQL_OWNER_PASSWORD'] -ceq $settings['NWFM_RUNTIME_PASSWORD'] -or
        $settings['NWFM_SQL_OWNER_PASSWORD'] -ceq $settings['NWFM_CATALOG_READER_PASSWORD']) {
        throw 'NWFM_SQL_OWNER_PASSWORD must differ from the request and catalog reader passwords.'
    }
    Write-Host 'Provisioning the Docker owner login on the host SQL Server (Windows authentication)...'
    & sqlcmd -S localhost -E -d master -C -I -b -i (Join-Path $PSScriptRoot 'provision-docker-owner.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Owner login provisioning failed. Review the SQL error above.' }
    return $true
}

Assert-Docker

if ($Action -eq 'stop') {
    Invoke-Docker compose down
    Write-Host 'NWFM containers removed. The host databases and the media volume are kept.'
    exit 0
}
if ($Action -eq 'logs') {
    & docker compose logs -f --tail 100
    exit 0
}

$settings = Initialize-EnvFile
Assert-PortsAvailable
$provisionHost = Initialize-HostLogins $settings

# The control plane first: it creates and migrates the catalog and the bootstrap workspace.
Write-Host 'Building and starting the control plane...'
Invoke-Docker compose up -d --build platform
Wait-Until 'platform' 'the control plane (migrations run on the first start)' 300 { Test-Health 5180 'NWFM.Platform' }

if ($provisionHost) {
    Write-Host 'Provisioning the restricted nwfm_request and nwfm_catalog_reader logins...'
    & (Join-Path $PSScriptRoot 'provision-local-runtime.ps1')
}

Write-Host 'Building and starting the tenant API and both web apps...'
Invoke-Docker compose up -d --build
Wait-Until 'backend' 'the tenant API' 180 { Test-Health 5081 'NWFM' }
Wait-Until 'frontend' 'the tenant app' 60 { Test-Page 4200 }
Wait-Until 'platform-web' 'the platform web app' 60 { Test-Page 4300 }

foreach ($url in $appUrls) {
    Start-Process $url
    Start-Sleep -Milliseconds 400
}

Write-Host ''
Write-Host 'NWFM is running in Docker against the host SQL Server.' -ForegroundColor Green
Write-Host '  Workspace (nwfm):  http://localhost:4200'
Write-Host '  Other workspaces:  http://<key>.localhost:4200'
Write-Host '  Landing + signup:  http://localhost:4300'
Write-Host '  Staff console:     http://localhost:4300/console'
Write-Host '  Tenant API:        http://localhost:5081/swagger'
Write-Host '  Platform API:      http://localhost:5180/swagger'
Write-Host '  Mock e-mails:      data\emails'
Write-Host ''
Write-Host 'Test logins, e-mails and logs: docs\local-startup.md'
Write-Host 'Logs: start-docker.bat logs    Stop: stop-all.bat'
