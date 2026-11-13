#!/usr/bin/env bash
set -euo pipefail

# Restricted to this local Compose service, its fixed internal host and its
# initialized NWFM database. No production connection argument is accepted.
if [[ "${NWFM_LOCAL_COMPOSE:-}" != "1" ]]; then
  echo "Use the local Compose initialize profile to provision the request identity." >&2
  exit 1
fi
if [[ ! "${NWFM_TENANT_ID:-}" =~ ^[[:xdigit:]]{8}-([[:xdigit:]]{4}-){3}[[:xdigit:]]{12}$ ]]; then
  echo "A canonical tenant GUID is required." >&2
  exit 1
fi
# sqlcmd substitutes these environment variables in a SQL literal. A strict
# character allowlist prevents quoting, command substitution and SQL injection.
if [[ ! "${NWFM_RUNTIME_PASSWORD:-}" =~ ^[A-Za-z0-9_!-]{32,128}$ ]]; then
  echo "The runtime password must be 32-128 ASCII letters, digits, underscores, exclamation marks or hyphens." >&2
  exit 1
fi
if [[ -z "${SQLCMDPASSWORD:-}" || "$NWFM_RUNTIME_PASSWORD" == "$SQLCMDPASSWORD" ]]; then
  echo "Separate bootstrap and request SQL credentials are required." >&2
  exit 1
fi

exec /opt/mssql-tools18/bin/sqlcmd -S database -U sa -d NWFM -C -I -b \
  -i /provision/provision-local-runtime.sql
