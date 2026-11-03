# Run NWFM locally

How to start, stop and troubleshoot every NWFM application on this machine. What to do once it is running is in the [platform](platform/README.md) and [tenant](tenant/README.md) guides.

**Development only:** the passwords in these guides are seeded development defaults. Never use them on a shared or production server.

## Start and stop

| Command                 | What it does                                                                                                                   |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| `start-docker.bat`      | Builds and runs all four apps in Docker against the host SQL Server, waits until they are ready, and opens them in the browser |
| `start-dev.bat`         | Runs the same four apps from source (`dotnet run` and `ng serve`), each in its own window                                      |
| `stop-all.bat`          | Stops everything, whichever way it was started: Docker containers, dev servers and their windows                               |
| `start-docker.bat logs` | Follows the logs of every Docker container                                                                                     |

Use one setup at a time. Both use the same ports (4200, 4300, 5081, 5180) and the same host databases. Run `stop-all.bat` before switching.

### Prerequisites

- **Both setups:** SQL Server on this machine (default instance, mixed authentication, TCP 1433) and the `sqlcmd` tools.
- **Docker:** Docker Desktop, running.
- **Dev:** the .NET 10 SDK and Node.js 22.x.

### First Docker run

`start-docker.bat` does all of this for you:

1. Creates `.env` from `.env.example` and fills every secret with a random value.
2. Creates the `nwfm_docker_owner` SQL login on the host. The containerized control plane uses it.
3. Starts the control plane, which creates and migrates `NWFM_Catalog` and the `NWFM` workspace database.
4. Creates the restricted `nwfm_request` and `nwfm_catalog_reader` logins.
5. Starts the tenant API and both web apps, then opens them in the browser.

## Apps at a glance

| Side     | App                           | URL                               | Guide                          |
| -------- | ----------------------------- | --------------------------------- | ------------------------------ |
| Platform | Landing page and signup       | http://localhost:4300             | [platform](platform/README.md) |
| Platform | Staff console                 | http://localhost:4300/console     | [platform](platform/README.md) |
| Platform | Platform API (Swagger)        | http://localhost:5180/swagger     | [platform](platform/README.md) |
| Tenant   | Tenant app (workspace `nwfm`) | http://localhost:4200             | [tenant](tenant/README.md)     |
| Tenant   | Other workspaces              | http://&lt;key&gt;.localhost:4200 | [tenant](tenant/README.md)     |
| Tenant   | Tenant API (Swagger)          | http://localhost:5081/swagger     | [tenant](tenant/README.md)     |

## Test logins

| Where                                        | User                      | Password          |
| -------------------------------------------- | ------------------------- | ----------------- |
| Staff console, http://localhost:4300/console | `platform@localhost`      | `PlatformAdmin1!` |
| Tenant app, http://localhost:4200            | `administrator@localhost` | `Administrator1!` |

Each account is created only when its database has none. The staff console password depends on which setup created the catalog first. If `start-dev.bat` did, it is `PlatformAdmin1!`, which is the case on this machine. If Docker did, it is `NWFM_PLATFORM_ADMIN_PASSWORD` in `.env`. Change both passwords after your first sign-in if the machine is shared.

## E-mails and logs

| What                                                      | Where                                                                                                 |
| --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Signup, verification and activation e-mails (both setups) | `data/emails` in the repository root, one `.txt` file per e-mail. The link is at the top of each file |
| Platform API log (Docker)                                 | `docker compose logs -f platform`                                                                     |
| Tenant API log (Docker)                                   | `docker compose logs -f backend`                                                                      |
| All container logs                                        | `start-docker.bat logs`                                                                               |
| Logs with `start-dev.bat`                                 | Each app's own server window                                                                          |

## SQL logins

The SQL login passwords live only in `.env`, which git ignores. Don't copy them anywhere else.

| Login                 | Used by                                | Password in `.env`             |
| --------------------- | -------------------------------------- | ------------------------------ |
| `nwfm_docker_owner`   | Docker control plane (owner, sysadmin) | `NWFM_SQL_OWNER_PASSWORD`      |
| `nwfm_request`        | Tenant API, workspace database         | `NWFM_RUNTIME_PASSWORD`        |
| `nwfm_catalog_reader` | Tenant API, catalog (read-mostly)      | `NWFM_CATALOG_READER_PASSWORD` |

`start-dev.bat` uses your Windows identity for the control plane. The tenant API's restricted logins come from user secrets, as described in the [README](../README.md#configure-the-tenant-api).

## Troubleshooting

| Symptom                                       | Fix                                                                                                                                                                                  |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| "Port(s) … are in use by another application" | Run `stop-all.bat`. If it lists a non-NWFM program, close that program.                                                                                                              |
| "Docker is not running"                       | Start Docker Desktop and wait until it reports that it is running.                                                                                                                   |
| Control plane times out or cannot reach SQL   | Check that SQL Server listens on TCP 1433 with mixed authentication, and that Windows Firewall allows inbound 1433 from the Docker network. Then run `docker compose logs platform`. |
| "The existing … login has another password"   | `.env` was regenerated, but the login already exists. Put the original password back in `.env`, or rotate the login's password in SQL Server deliberately.                           |
| Staff console rejects the password            | Try the other password from [Test logins](#test-logins). The one that applies depends on which setup created the catalog first.                                                      |
| No e-mail in `data/emails`                    | Check `docker compose logs platform` for e-mail errors.                                                                                                                              |
