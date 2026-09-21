# NWFM

An independent workflow application extracted from the reference project's Workflow module. It contains an ASP.NET Core backend and an Angular frontend, using a fresh SQL Server database.

Version one opens directly into one default tenant. Participants identify who performs a workflow action; they have no accounts or passwords. Authentication and access control are intentionally absent and will be implemented separately. Run this version locally or in a trusted development environment.

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

Startup applies the new migrations and initializes one tenant, two reviewers, a Reviewers group, and a Simple approval workflow. Initialization is transactional and repeatable. To manage migrations separately, set `Application__InitializeDatabase=false` after initialization.

## First workflow

1. Open **Start workflow**, select the standalone binding, and start a request.
2. Open **Tasks** and claim the review task.
3. Open **Action**, enter `Approve`, and submit completion.
4. Check **Requests** or **Execution monitor** for progress and history.
5. Use **Designer & definitions** to create definitions and new versions. Configure groups before assigning user tasks; publish a version and create an active binding to make it available on the start screen.

The header selects the acting participant. New participants can be created directly under **Participants**; the header list updates automatically. The tenant selector has one available tenant in this version. The configured default participant cannot be deactivated until another default is configured.

## Forms

The **FormEngine** module (SQL schema `FE`) holds the forms people fill in: a drag-and-drop builder, a
versioned publish lifecycle, and the submissions themselves.

1. Open **Forms → Manage forms**, create a form, then **Design fields** to lay it out. Saving keeps a
   working draft; nothing is registered until you publish.
2. **Publish** freezes the design as a version. It also records each field's data name and type in
   `FE.FormFields`, and creates or widens the form's own submissions table.
3. **Fill a form** lists everything with a published version. Media fields upload as soon as a file is
   picked, so a submission carries references rather than bytes.
4. **Submissions** shows what has been filled in, rendered through the version each row answered.

Each form stores its submissions in its own table, `FE.SUB_<CODE>`. The name is chosen at first
publish from the form code and never changes after that. A table holds only its own form's columns, so
no single table has to carry every field the application has ever seen. SQL Server's 1,024 columns per
table and 8,060 bytes per row now apply to one form, not to all of them. Publishing is locked per form,
so one form's publish does not block another's.

A data name is a SQL column, so it must be a legal identifier. It keeps one type **within its form**:
republishing a field under a different type is refused. Another form may use the same name with a
different type, and the **Field catalog** page flags such names. A form can store at most 500 fields.

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

The **Tasks** module (SQL schema `TK`) holds field work: each task is a place to visit and a form to
fill there.

1. **Admin → Field teams** creates a crew together with the login its members sign in with, and gives
   it a territory (org scopes: department, cluster, CBU, branch or operation area).
2. **Field tasks → Task types** binds each kind of work to a published form and its SLA hours. Types
   can use different forms, so each kind of task collects its own inputs.
3. **Field tasks → Tasks** raises a task at a location on the map. The task pins the type's form at its
   current published version.
4. **Assign** lists only active teams whose territory covers the task. Deadlines default from the SLA.
5. **Fill** saves the answers to the form's own table, linked to the task. A retried fill with the same
   client key is replayed, not stored twice.
6. A reviewer **approves** or **returns** the fill with a reason. A returned task is filled again. Any
   open task can be **expired**.

Every list and every action is limited to the caller's territory. A task outside it is reported as not
found. A team login sees only the tasks currently assigned to its team. Administrators and monitors are
unrestricted.

With `DatabaseStartup:SeedData` on, startup seeds two task types (bound to the two seeded forms) and
four sample tasks around Riyadh. To generate a Tasks migration, from `backend`:

```powershell
dotnet ef migrations add <Name> --project src/Modules/Tasks/Tasks.Infrastructure --startup-project src/NWFM.Api --output-dir Persistence/Migrations
```

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

The smoke test creates a completed sample request and a deactivated test participant. It verifies API availability, tenant/participant validation, participant registration, default participant protection, duplicate execution protection, concurrent claims, ownership, completion, and the reduced API surface.

To regenerate the Angular client after an API change, start the backend, then run `npm run generate:api` in `frontend`.

## Boundaries and extension points

- The only feature module is Workflow. The backend host supplies a minimal tenant and participant context.
- `ICurrentTenant` and `IWorkflowActorContext` are the future authentication integration points. The `UserId` fields retained in Workflow are stable actor identifiers, not references to an Identity database.
- The frontend sends `X-Workflow-Participant-Id`; this is attribution, not proof of identity. Tenant selection comes from backend configuration. Tenant headers, route values, or body values cannot override it.
- Former workflow administration endpoints operate inside the configured tenant. Database filters include workflow versions and compiled child records; writes to another tenant are rejected.
- SQL schemas are `Workflow` and `Tenancy`. No privacy/compliance, Identity, platform administration, or original business data is included.
- Workflow XML retains its original `https://privora.io/workflow/v1` schema namespace for compatibility with the extracted compiler and designer. This is a format identifier; it makes no network request.
- The standalone outcome handler acknowledges results already stored in workflow history. Future integrations can implement capability, action, and outcome contracts without importing another product's modules.
- In-app notifications work locally. SMTP is optional; without SMTP configuration, email deliveries remain queued, matching the retained engine's behavior.

## Containers

Copy `.env.example` to `.env`, set a local SQL Server password, and run `docker compose up --build`. The frontend is available at http://localhost:4200. The database volume is separate from any reference project volume. This is a local development configuration, not a production deployment.

The local .NET/Angular setup was verified with SQL Server, 217 backend tests, 35 frontend tests, a production frontend build, HTTP smoke checks, and browser workflow completion. The container configuration was validated; the containers have not been run.
