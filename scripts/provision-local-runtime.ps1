$ErrorActionPreference = 'Stop'

# This convenience script only addresses the default local SQL Server and NWFM
# database using the current Windows administrator identity. It accepts no server
# or database override; business deployment credentials use operator provisioning.
$tenantGuid = [Guid]::Empty
if (-not [Guid]::TryParseExact($env:NWFM_TENANT_ID, 'D', [ref]$tenantGuid) -or $tenantGuid -eq [Guid]::Empty) {
    throw 'NWFM_TENANT_ID must be a non-empty canonical tenant GUID.'
}
if ($env:NWFM_RUNTIME_PASSWORD -cnotmatch '^[A-Za-z0-9_!-]{32,128}$') {
    throw 'NWFM_RUNTIME_PASSWORD must be 32-128 ASCII letters, digits, underscores, exclamation marks or hyphens.'
}
if ($env:NWFM_CATALOG_READER_PASSWORD -cnotmatch '^[A-Za-z0-9_!-]{32,128}$' -or $env:NWFM_CATALOG_READER_PASSWORD -ceq $env:NWFM_RUNTIME_PASSWORD) {
    throw 'NWFM_CATALOG_READER_PASSWORD must be 32-128 such characters and differ from NWFM_RUNTIME_PASSWORD.'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'Install the Microsoft SQL Server command-line tools before provisioning the local request identity.'
}

& sqlcmd -S localhost -E -d NWFM -C -I -b -i (Join-Path $PSScriptRoot 'provision-local-runtime.sql')
if ($LASTEXITCODE -ne 0) {
    throw 'Local request identity provisioning failed. Review the SQL error; existing data was not reset.'
}

# The tenant API's catalog login. Start NWFM.Platform.Api once first: it creates the catalog.
& sqlcmd -S localhost -E -d master -C -I -b -i (Join-Path $PSScriptRoot 'provision-catalog-reader.sql')
if ($LASTEXITCODE -ne 0) {
    throw 'Catalog reader provisioning failed. Review the SQL error; existing data was not reset.'
}
