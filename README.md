# NWFM

An independent workflow application extracted from the reference project's Workflow module. It contains an ASP.NET Core backend and an Angular frontend, using a fresh SQL Server database.

The application uses Auth login and permissions with one server-configured default tenant. The workflow workspace provides Workflows, New Instance, Instances and Lookups, while retaining the complete designer and runtime. See the [workspace guide](WORKFLOW_WORKSPACE.md) for architecture, migrations, local demo setup and a walkthrough.

## Start locally

Prerequisites: .NET 10 SDK, Node.js 22.12+ (22.x), npm, and SQL Server.

On Windows, double-click **start-dev.bat** in the project directory. It opens separate backend and frontend server windows and installs frontend dependencies if missing. Ports already in use are left alone. Open http://localhost:4200 once both servers are ready; press Ctrl+C in each server window to stop them.

For manual startup, use the following commands.

From the project directory:

```powershell
dotnet restore backend/NWFM.sln
dotnet run --project backend/src/NWFM.Api
```

In another terminal:

```powershell
cd frontend
npm ci
npm start
```

Open http://localhost:4200. The API listens on http://localhost:5080 and its API explorer is at http://localhost:5080/swagger.

The default connection uses Windows authentication to SQL Server on `localhost`, database `NWFM`. The startup identity needs permission to create that database. Override `ConnectionStrings__DefaultConnection` for another SQL Server; use a new NWFM database, never the reference database. No reference credentials or data are copied.

Startup applies workflow migrations and initializes the default tenant. Auth migrations/seeding use the `DatabaseStartup` settings. Workflow examples are created only when `WorkflowDemo:Enabled=true`; the [demo setup](WORKFLOW_WORKSPACE.md#local-demo-setup) also enables local transports. Leave demo and Auth data seeding disabled for business deployments.

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

With `DatabaseStartup:SeedData` on, startup seeds two task types (bound to the two seeded forms), four
sample tasks around Riyadh, and the reference app's C2M action mappings. To generate a Tasks migration,
from `backend`:

```powershell
dotnet ef migrations add <Name> --project src/Modules/Tasks/Tasks.Infrastructure --startup-project src/NWFM.Api --output-dir Persistence/Migrations
```

The two modules used to live in the `FE` and `TK` schemas. On startup each module first moves its
migration history table into the new schema, then a migration moves its tables, including every
per-form `SUB_` table. Apply these by starting the API rather than with `dotnet ef database update`,
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

Copy `.env.example` to `.env`, set a local SQL Server password, and run `docker compose up --build`. The frontend is available at http://localhost:4200. The database volume is separate from any reference project volume. This is a local development configuration, not a production deployment.

The workspace was verified with SQL Server, 319 backend tests, 60 frontend tests, production builds, authenticated API checks, real process restarts and browser checks. See the workspace guide for evidence and remaining build warnings. The containers have not been run for this workspace change.
