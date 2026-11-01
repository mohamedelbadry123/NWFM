# NWFM

An independent workflow application extracted from the reference project's Workflow module. It contains an ASP.NET Core backend and an Angular frontend, using a fresh SQL Server database.

The application is multi-tenant, with a database per tenant:

- **Each workspace (tenant) has its own SQL Server database.** One tenant API serves every workspace and selects the workspace from the request host or the signed session.
- **A control plane manages the workspaces.** It owns the tenant catalog, signup, approval, provisioning and migrations.
- **Each workspace has the full workflow app.** It provides Workflows, New Instance, Instances and Lookups, and keeps the complete designer and runtime. See the [workspace guide](WORKFLOW_WORKSPACE.md) for architecture, migrations, local demo setup and a walkthrough.

| Application                            | Project                          | Port |
| -------------------------------------- | -------------------------------- | ---- |
| Control plane API                      | `backend/src/NWFM.Platform.Api`  | 5180 |
| Tenant API                             | `backend/src/NWFM.Api`           | 5081 |
| Tenant app                             | `frontend` (project `frontend`)  | 4200 |
| Landing page, signup and staff console | `frontend/projects/platform-web` | 4300 |

`http://localhost:4200` is the bootstrap workspace (`nwfm`). Any other workspace is `http://<key>.localhost:4200`. An organisation owner signs up at `http://localhost:4300`. They choose a database we provide or bring their own SQL Server. Staff approve the request at `http://localhost:4300/console`.

## Start locally

Prerequisites: .NET 10 SDK, Node.js 22.22.3+ (22.x), npm, SQL Server and the Microsoft `sqlcmd` tools. The [container setup](#containers) runs the same applications in Docker against the same host SQL Server. Read the [tenant security rollout](docs/security/tenant-security.md#provisioning-and-rollout) before using this outside a development machine.

Reserve ports **5180**, **5081**, **4200** and **4300** for NWFM development. Other local applications and IIS sites must use separate ports. Stop the development servers before running the Docker configuration, which uses the same ports. **start-dev.bat** checks all four ports before it starts anything:

- It reuses existing healthy NWFM services.
- It stops with a clear error if another application, or an unhealthy service, holds a port.
- It never silently switches ports.

### First run

The control plane owns every database migration. Its development settings register the local `NWFM` database as the bootstrap workspace `nwfm`. On startup it migrates the `NWFM_Catalog` catalog and that workspace, using your Windows identity. Then provision the two restricted logins the tenant API uses:

- **`nwfm_request`** reaches the workspace's database.
- **`nwfm_catalog_reader`** reads the catalog. It cannot read owner credentials.

Choose independent random passwords of 32–128 ASCII letters, digits, `_`, `!` or `-` that satisfy SQL Server's password policy. Keep real secrets only in your local environment or secret store.

```powershell
dotnet restore backend/NWFM.sln
dotnet run --project backend/src/NWFM.Platform.Api   # wait for "Now listening", then stop it (Ctrl+C)
$env:NWFM_TENANT_ID = "10000000-0000-0000-0000-000000000001"
$env:NWFM_RUNTIME_PASSWORD = "<independent strong request password>"
$env:NWFM_CATALOG_READER_PASSWORD = "<another independent strong password>"
.\scripts\provision-local-runtime.ps1
```

The provisioning script targets only `localhost`. It checks the existing workspace binding. It never replaces data or the password of an existing login.

### Configure the tenant API

Give the tenant API the restricted logins, for example with user secrets:

```powershell
dotnet user-secrets --project backend/src/NWFM.Api init
dotnet user-secrets --project backend/src/NWFM.Api set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=NWFM;User Id=nwfm_request;Password=<request password>;TrustServerCertificate=True;Max Pool Size=20"
dotnet user-secrets --project backend/src/NWFM.Api set "ConnectionStrings:CatalogConnection" "Server=localhost;Database=NWFM_Catalog;User Id=nwfm_catalog_reader;Password=<reader password>;TrustServerCertificate=True;Max Pool Size=20"
```

### Run

Run **start-dev.bat**. It opens the control plane, the tenant API, the tenant app and platform-web in separate windows, and installs frontend dependencies if they are missing.

| What                             | Where                         |
| -------------------------------- | ----------------------------- |
| Tenant app (bootstrap workspace) | http://localhost:4200         |
| Tenant API explorer              | http://localhost:5081/swagger |
| Staff console                    | http://localhost:4300/console |
| Development e-mails              | `data/emails`                 |

Sign in to the staff console as `platform@localhost` with the development password from `backend/src/NWFM.Platform.Api/appsettings.Development.json`, and change it. Development e-mails (signup verification and activation links) are written as files to `data/emails`.

### How the hosts divide the work

- **The tenant API never migrates or seeds.** It refuses every `DatabaseStartup` flag. It also refuses to serve a workspace whose database credential could administer that database or reach another one.
- **After a schema change,** restart the control plane (development migrates configuration workspaces on start), or use _Migrate_ / _Migrate all_ in the console.
- **Workflow examples** additionally need `WorkflowDemo:Enabled=true` on the control plane and the local demo transports.
- **Use a fresh NWFM database,** never a reference project's database.

## First workflow

1. Sign in, open **Lookups**, and check geography, departments and Field Activity Types.
2. In **Workflows**, create and publish child workflows, then a main workflow containing Main Activities. Each main activity runs its child before final approval.
3. In **New Instance**, select a published main workflow and start it. Tenant and binding are resolved automatically.
4. In **Instances**, open the execution tree and use permitted Approve, Reject or Add Comment actions. Rejection follows the configured rework route.
5. On explicitly marked demo instances, administrators can select a demo user. Ordinary requests always use the signed-in identity and real group membership.

## Forms

The **FormEngine** module (SQL schema `FormEngine`) holds the forms people fill in: a drag-and-drop builder, a
versioned publish lifecycle, and the submissions themselves. [docs/form-engine.md](docs/form-engine.md)
explains forms, fields and submission handling in depth.

1. Open **Forms**, create a form, then **Design fields** to lay it out. Saving keeps a working draft;
   nothing is registered until you publish.
2. **Publish** freezes the design as a version. It also records each field's data name and type in
   `FormEngine.FormFields`, and creates or widens the form's own submissions table.
3. Forms are filled through **Field tasks**: a task pins a form version, and its fill dialog uploads
   media as soon as a file is picked, so a submission carries references rather than bytes.
4. **Submissions** shows what has been filled in, rendered through the version each row answered.

Each form stores its submissions in its own table, `FormEngine.SUB_<CODE>`. The name is chosen at first
publish from the form code and never changes after that. A table holds only its own form's columns, so
no single table has to carry every field the application has ever seen. SQL Server's 1,024 columns per
table and 8,060 bytes per row now apply to one form, not to all of them. Publishing is locked per form,
so one form's publish does not block another's.

A data name is a SQL column, so it must be a legal identifier. It keeps one type **within its form**:
republishing a field under a different type is refused. Another form may use the same name with a
different type, and the **Field catalog** tab under **Lookups** flags such names. A form can store at most 500 fields.

Editing a published form reopens it as a draft; it keeps accepting submissions against its published
version until it is deprecated or archived, so work already pinned to a version is never broken.

Submissions carry a `ContextType`/`ContextId` pair naming what they were filled for. Task fills use
`Task` and the task's id. Other modules reach forms only through `IFormGateway` in `NWFM.Shared`.

After changing the FormEngine entities, generate the migration by hand (this project does not
auto-generate them), from `backend`:

```powershell
dotnet ef migrations add <Name> --project src/Modules/FormEngine/FormEngine.Infrastructure --startup-project src/NWFM.Api --output-dir Persistence/Migrations
```

Uploaded media is written under `FileStorage:Root` (`backend/src/NWFM.Api/Media` by default), which is
excluded from source control. The geolocation field uses Google Maps when
`frontend/public/config/app-config.json` carries a `googleMapsApiKey`, and falls back to manual
latitude/longitude entry when it does not.

## Field tasks and teams

The **Tasks** module (SQL schema `Task`) holds field work: each task is a place to visit and a form to
fill there.

1. **Admin → Field teams** creates a crew together with the login its members sign in with, and gives
   it a territory (org scopes: department, cluster, CBU, branch or operation area).
2. **Lookups → Task types** binds each kind of work to a published form and its SLA hours. Types can
   use different forms, so each kind of task collects its own inputs.
3. **Field tasks** raises a task at a location on the map. The task pins the type's form at its
   current published version.
4. **Assign** lists only active teams whose territory covers the task. Deadlines default from the SLA.
5. **Fill** saves the answers to the form's own table, linked to the task. A retried fill with the same
   client key is replayed, not stored twice.
6. A reviewer **approves** or **returns** the fill with a reason. A returned task is filled again. Any
   open task can be **expired**.

Every list and every action is limited to the caller's territory. A task outside it is reported as not
found. A team login sees only the tasks currently assigned to its team. Administrators and monitors are
unrestricted.

A task that carries a C2M field activity id, and whose type is marked as the closing type, closes that
activity in C2M when it is approved. The outcome (completed or cancelled, and the reason) comes from
the fill's `wfm_action_taken` answer; fields with a C2M parameter name travel with it. The integration
is off until `C2m:Enabled` is set, and every attempt is logged on the task's **C2M** tab. See
[docs/form-engine.md](docs/form-engine.md#13-closing-c2m-field-activities).

With `DatabaseStartup:SeedData` on, explicit initialization seeds two task types (bound to the two seeded forms), four
sample tasks around Riyadh, and the reference app's C2M action mappings. To generate a Tasks migration,
from `backend`:

```powershell
dotnet ef migrations add <Name> --project src/Modules/Tasks/Tasks.Infrastructure --startup-project src/NWFM.Api --output-dir Persistence/Migrations
```

The two modules used to live in the `FE` and `TK` schemas. During initialization each module first moves its
migration history table into the new schema, then a migration moves its tables, including every
per-form `SUB_` table. Apply these with the initialize-only command rather than `dotnet ef database update`,
which does not run that first step.

## Verify

```powershell
dotnet test backend/NWFM.sln
cd frontend
npm run build:prod
npm test -- --watch=false --browsers=ChromeHeadless
```

With the API running, from the project root:

```powershell
node scripts/smoke-test.mjs
```

Run the smoke test against a disposable, Auth-seeded database with workflow demos enabled. It verifies authenticated API availability, tenant isolation, real-user assignment, duplicate starts, concurrent claims, ownership and completion. The [workspace guide](WORKFLOW_WORKSPACE.md#verification-evidence) includes additional integration and process-restart checks.

To regenerate the Angular client after an API change, start the backend, then run `npm run generate:api` in `frontend`.

## Boundaries and extension points

Workflow improvements are tracked in [the implementation plan](WORKFLOW_ENGINE_ENHANCEMENT_PLAN.md) and [activity audit](WORKFLOW_ACTIVITY_AUDIT.md), with verified paths and operating boundaries. Configure API calls, email and authenticated callbacks using the [integration operator guide](WORKFLOW_INTEGRATIONS.md).

- Auth supplies identities, permissions and organizational reference data. Workflow uses reference-data and group-directory interfaces.
- `TenantResolutionMiddleware` binds each request to one workspace, using the signed `tid` claim or the request host, and `ICurrentTenant` reads that binding. Auth's JWT supplies the actor; participant headers cannot select another identity. Tenant headers, route values or body values cannot override the workspace.
- Normal Workflow participant `UserId` values match their Auth user IDs. Demo participants are separately marked and usable only through authorized demo-instance actions.
- Former workflow administration endpoints operate inside the bound workspace. Database filters include workflow versions and compiled child records; writes to another tenant are rejected.
- Each workspace database has the `Auth`, `FormEngine`, `Task`, `Workflow` and `Tenancy` schemas. The control plane's catalog uses the `catalog` schema in `NWFM_Catalog`. Reference-project business data is not copied.
- Workflow XML retains its original `https://privora.io/workflow/v1` schema namespace for compatibility with the extracted compiler and designer. This is a format identifier; it makes no network request.
- The standalone outcome handler acknowledges results already stored in workflow history. Future integrations can implement capability, action, and outcome contracts without importing another product's modules.
- REST, SOAP, SMS and email are configured inside activity settings. Required delivery blocks advancement; background delivery remains visible after completion. Wait for Event handles authenticated incoming callbacks separately.

## Containers

Docker runs the four applications. The database stays on the SQL Server installed on this machine and is not a container. The containers reach it at `host.docker.internal,1433` with SQL logins, because Linux containers cannot use Windows authentication.

Prerequisites:

- Docker Desktop.
- The local default SQL Server instance with mixed authentication and TCP port 1433 enabled.
- The `sqlcmd` tools.

Stop the `start-dev.bat` servers first. Both setups use the same ports and the same host databases.

```text
start-docker.bat          build, provision, start everything and open the apps
start-docker.bat logs     follow the container logs
start-docker.bat stop     remove the containers (databases and media are kept)
stop-all.bat              stop everything: Docker containers and start-dev.bat servers
```

[Run NWFM locally](docs/local-startup.md) lists every URL, test login, e-mail folder and log command. The [platform](docs/platform/README.md) and [tenant](docs/tenant/README.md) guides cover each side.

`start-docker.bat` runs [scripts/start-docker.ps1](scripts/start-docker.ps1). On each run it:

1. **Prepares `.env`.** On the first run it creates `.env` from `.env.example` and fills every empty secret with a random value: the owner, request and catalog-reader SQL passwords, separate tenant and platform JWT signing keys, and the first platform administrator's password. Existing values are kept.
2. **Checks the ports.** It stops if another application holds port 4200, 4300, 5081 or 5180. It never switches ports.
3. **Creates the owner login.** It creates `nwfm_docker_owner` on the host with your Windows identity. This sysadmin login is used only by the containerized control plane, and only for local development.
4. **Starts the control plane.** The control plane migrates `NWFM_Catalog` and the bootstrap workspace `NWFM`. Its port opens only when that is done.
5. **Provisions the restricted logins.** It runs `scripts/provision-local-runtime.ps1` to provision `nwfm_request` and `nwfm_catalog_reader` with narrow grants.
6. **Starts everything else.** It starts the tenant API with only those restricted logins, the tenant app and platform-web. Then it waits for each one and opens a browser tab per app:
   - http://localhost:4200
   - http://localhost:4300
   - http://localhost:4300/console (sign in as `platform@localhost`, password `NWFM_PLATFORM_ADMIN_PASSWORD` from `.env`)
   - the two Swagger pages

Every step is idempotent. Provisioning never resets the password of an existing login: changing one requires an explicit SQL credential rotation.

Notes:

- **Running containers.** The containers run as non-root users, and every web port binds only to loopback.
- **E-mails and media.** Signup and activation e-mails are written to `data/emails`. Tenant media lives in the `nwfm-media` volume. Reusing that volume with another workspace id fails.
- **Shared catalog.** Docker and `start-dev.bat` share one catalog, so whichever control plane starts last registers its storage path for the bootstrap workspace.
- **Database servers for managed signups.** A server that the platform runs in Docker reaches must be registered in the console as `host.docker.internal`, not `localhost`.
- **Connection failures.** If the containers cannot reach SQL Server, allow inbound TCP 1433 from the Docker/WSL network in Windows Firewall.
- **Schema updates.** After a schema update, restart `platform` before rolling the tenant API.
- **Offboarded workspaces.** Migration does not reactivate an offboarded workspace.

This configuration seeds development accounts and keeps local secrets in the ignored `.env`. For production, follow the [security rollout requirements](docs/security/tenant-security.md), which cover secret injection, encrypted key storage, infrastructure isolation, backups, quotas and retention. The disposable SQL E2E suite separately verifies the request grants, binding guards, credential verification and recovery lifecycle.

## Contribution quality gates

See [quality gates and end-to-end tests](docs/quality-gates.md) for setup, local checks, ChatGPT sign-in, CI replay, and the GitHub-only merge policy.
