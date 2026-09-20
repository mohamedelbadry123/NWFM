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
