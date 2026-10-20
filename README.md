# NWFM

An independent workflow application extracted from the reference project's Workflow module. It contains an ASP.NET Core backend and an Angular frontend, using a fresh SQL Server database.

The application uses Auth login and permissions with one server-configured default tenant. The workflow workspace provides Workflows, New Instance, Instances and Lookups, while retaining the complete designer and runtime. See the [workspace guide](WORKFLOW_WORKSPACE.md) for architecture, migrations, local demo setup and a walkthrough.

## Start locally

Security rollout: initialization now runs separately from the request host.
Use an explicitly initialized, tenant-bound database and a restricted request
login. Existing administrator-based startup needs the
[tenant security rollout](docs/security/tenant-security.md#provisioning-and-rollout)
before serving requests. The isolated E2E launcher provisions both identities
automatically for disposable tests.

Prerequisites: .NET 10 SDK, Node.js 22.22.3+ (22.x), npm, SQL Server and the Microsoft `sqlcmd` tools. The [container setup](#containers) provisions these database identities for a local Docker deployment.

First initialize the database and provision its request identity below. Then run **start-dev.bat** from a terminal carrying the configured tenant, signing key and restricted connection string, or save those settings with .NET user secrets before double-clicking it. It opens separate backend and frontend server windows and installs frontend dependencies if missing. It checks both ports before starting: existing healthy NWFM services are reused, while another application or an unhealthy service causes a clear error. It never silently switches ports.

Reserve ports **4200** (frontend) and **5081** (API) for NWFM development on this machine. Other local applications and IIS sites must use separate ports. Stop the development servers before running the Docker configuration, which uses the same ports. The API launch profile, Angular proxy and Docker configuration all agree on port 5081; do not change just one to work around a conflict.

For a new local SQL Server database, run this once from PowerShell in the project directory using a Windows identity authorized to initialize it. Choose one immutable tenant GUID and independent random values for the signing key and request password. The request password must contain 32–128 ASCII letters, digits, `_`, `!` or `-` and satisfy SQL Server's password policy. Place real secrets only in your local environment or secret store.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:NWFM_TENANT_ID = "<tenant-guid>"
$env:Application__TenantId = $env:NWFM_TENANT_ID
$env:JwtSettings__SigningKey = "<random signing key of at least 32 bytes>"
$env:NWFM_RUNTIME_PASSWORD = "<independent strong request password>"
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=NWFM;Integrated Security=True;TrustServerCertificate=True"
dotnet restore backend/NWFM.sln
dotnet run --project backend/src/NWFM.Api --no-launch-profile -- --DatabaseStartup:InitializeOnly=true --DatabaseStartup:ApplyMigrations=true --DatabaseStartup:SeedData=true --DatabaseStartup:ApplySqlObjects=true
.\scripts\provision-local-runtime.ps1
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=NWFM;User Id=nwfm_request;Password=$env:NWFM_RUNTIME_PASSWORD;TrustServerCertificate=True;Max Pool Size=20"
Remove-Item Env:NWFM_RUNTIME_PASSWORD
```

The initialization command migrates, seeds local demo accounts/reference data and exits. The provisioning script only targets `localhost` / `NWFM`, validates the existing tenant binding and grants the request identity its required schema access. It never replaces data or an existing login's password. Use separately reviewed operator provisioning for another SQL Server or a business deployment; leave example-data seeding disabled there.

Keep the tenant ID, signing key, storage root and restricted connection configured for subsequent starts. The ordinary request host starts with every `DatabaseStartup` flag false:

```powershell
dotnet run --project backend/src/NWFM.Api
```

In another terminal:

```powershell
cd frontend
npm ci
npm start
```

Open http://localhost:4200. The API listens on http://localhost:5081 and its API explorer is at http://localhost:5081/swagger.

The checked-in Windows-authentication connection is an initialization template. A request host using an administrative identity is refused. All module migrations and SQL objects now run only in the separate initialization command; ordinary startup never updates the schema. Workflow examples additionally require `WorkflowDemo:Enabled=true` during initialization and local demo transports. Use a fresh NWFM database, never a reference project's database.

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
- `ICurrentTenant` resolves the configured tenant. Auth's JWT supplies the actor; participant headers cannot select another identity. Tenant headers, route values or body values cannot override the tenant.
- Normal Workflow participant `UserId` values match their Auth user IDs. Demo participants are separately marked and usable only through authorized demo-instance actions.
- Former workflow administration endpoints operate inside the configured tenant. Database filters include workflow versions and compiled child records; writes to another tenant are rejected.
- SQL schemas include `Auth`, `Workflow` and `Tenancy`; reference-project business data is not copied.
- Workflow XML retains its original `https://privora.io/workflow/v1` schema namespace for compatibility with the extracted compiler and designer. This is a format identifier; it makes no network request.
- The standalone outcome handler acknowledges results already stored in workflow history. Future integrations can implement capability, action, and outcome contracts without importing another product's modules.
- REST, SOAP, SMS and email are configured inside activity settings. Required delivery blocks advancement; background delivery remains visible after completion. Wait for Event handles authenticated incoming callbacks separately.

## Containers

Copy `.env.example` to `.env` and fill the tenant ID, separate random SQL administrator/request passwords, and a random JWT signing key. Use a unique Compose project name for each tenant. The sample secret values are deliberately empty so an unconfigured launch fails.

```text
docker compose --profile initialize run --build --rm provision-runtime
docker compose up --build -d backend frontend
```

The first command starts SQL Server, runs the API once with administrative initialization credentials, then provisions `nwfm_request` with narrow schema grants. The second starts the API with that restricted login and all initialization flags disabled. The API container runs as a non-root user. No SQL port is published; API/frontend ports bind only to loopback.

Open http://localhost:4200. The database, tenant-bound media and data-protection key volumes persist across restarts. Reusing those volumes with another tenant ID fails. After a schema update, stop the request host, repeat initialization/provisioning, then restart it. Initialization does not reactivate an offboarded tenant. Changing an existing request password requires an explicit SQL credential rotation; provisioning never silently resets it.

This configuration seeds development accounts and stores local secrets in the ignored `.env` and isolated local volumes. Use the [security rollout requirements](docs/security/tenant-security.md) for production secret injection, encrypted key storage, infrastructure isolation, backups, quotas and retention. The disposable SQL E2E suite separately verifies the request grants, binding guards and recovery lifecycle.

## Contribution quality gates

See [quality gates and end-to-end tests](docs/quality-gates.md) for setup, local checks, ChatGPT sign-in, CI replay, and the GitHub-only merge policy.
