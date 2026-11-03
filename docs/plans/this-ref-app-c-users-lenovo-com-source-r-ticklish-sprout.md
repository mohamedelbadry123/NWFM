# Plan: NWFM SaaS tenancy with a database per tenant, a Platform API, platform-web and a landing page

## Context

Today NWFM serves one tenant per deployment:

- The tenant is set in config (`Application:TenantId`).
- Each deployment has its own DB, process and storage.
- Commit `fe1db84` hardened this model against the OWASP Multi-Tenant Security Cheat Sheet. `docs/security/tenant-controls.json` maps all 141 units of the cheat sheet to controls.

We want the SaaS model of the reference app (`DigitalKeyWorkspace\AI_Booking_Engine`):

- An organisation owner raises a subscription request from a public landing page.
- The owner chooses a **platform-managed DB** or **their own SQL Server (BYO)**.
- Platform staff approve the request, and the tenant gets **its own database**, provisioned automatically.
- One shared tenant API serves every tenant and switches the database per request.
- A separate control-plane **Platform API** plus a **platform-web** app (landing page, signup and staff console) manage tenants.

**Decisions taken:**

- Rebuild fresh from the reference app. Do not use the `feature/platform-api` / `feature/tenancy-runtime` branches.
- Add a separate `NWFM.Platform.Api` host.
- Use a shared runtime with per-request tenant resolution.
- Give each tenant DB **two principals**: an owner for migrations and a restricted runtime login.
- Seed new tenants with roles, permissions and the role matrix, plus the example forms and task types.
- Require staff approval by default.
- In scope: the **public workspaces list** (opt-in per tenant) and **support access sessions**.
- Out of scope: per-tenant SAML (SAML stays limited to configuration-defined tenants) and billing (plans enforce limits only).

Follow NWFM conventions, not the reference app's, where they differ:

- MVC `[ApiController]` controllers, not minimal APIs.
- `X.Application/<Feature>/{Commands,Queries}/<UseCase>/{Command,Handler,Validator}.cs`.
- `Result<T>` return values.
- `NWFM.Shared` stays at `backend/src/NWFM.Shared`.
- One test project, `backend/tests/NWFM.Tests`.

## Core design invariants

1. **The catalog `Tenant.Id` equals the `OrganizationId` stored in the tenant DB, and equals `Tenancy.DeploymentBoundary.TenantId`.** The bootstrap tenant keeps `10000000-0000-0000-0000-000000000001`, so `Tenant.Create` needs an overload that takes an explicit id.
2. **`TenantDatabase` stores two encrypted connection strings:**
   - `EncryptedAdminConnectionString`: owner/migrator, used only by the Platform API.
   - `EncryptedConnectionString`: restricted runtime login, used by `NWFM.Api`. `RuntimePrincipal` records the runtime user name.

   The resolver picks one by `Tenancy:ConnectionRole` (`Runtime` or `Administration`). Configuration-mode tenants use a `ConnectionStringName`, which each host resolves from its own config.

3. **`ICurrentTenant` is kept.** It is implemented by `AmbientCurrentTenant => ITenantContext.Current?.Id ?? Guid.Empty`. `BaseDbContext`, the Workflow services and about 30 tests stay unchanged.
4. **Module projects never reference `Tenancy.*` or `Platform.*`; they use NWFM.Shared abstractions only.** `NWFM.Api` never references `Platform.*`. Architecture tests enforce both rules.
5. **Hangfire is not used.** NWFM doesn't have it. The provisioning queue is a `Channel<Guid>` BackgroundService with a startup sweep and a periodic sweep. Each run takes `sp_getapplock` per signup, and the step checkpoints make re-runs safe.

## Project layout

| Project                                       | Contents (port from the ref app at `...\AI_Booking_Engine\src\backend\src`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `NWFM.Shared/MultiTenancy/*` (extend)         | Port these from `Concierge.Shared\MultiTenancy\*`: `ITenantContext`, `TenantContextAccessor` (AsyncLocal; `BeginScope`/`BeginPlatformScope`), `TenantInfo`, `TenantStatus`, `ITenantStore`, `ITenantDatabaseModule` + `TenantSchema` (add `int Order`), `ITenantConnectionStringResolver`, `ISecretProtector` + `SecretPurposes`, `TenantClaimTypes`, `TenantRefreshTokens`, `TenantAccessException`, `TenantStorageLayout`. New: `ITenantFanOut`, `AmbientCurrentTenant`, and `Options/TenancyOptions`. Move `FailureResultFilter` here.                                |
| `Modules/Tenancy/Tenancy.Domain`              | Catalog entities: Tenant, TenantKey, TenantHost, TenantDatabase (two connection strings + RuntimePrincipal), DatabaseServer, Plan, Subscription, SignupRequest + SignupProvisioningEvent (BYO owner + runtime creds), PlatformUser, PlatformAuditEntry, TenantSettings (+`ListedPublicly`), **SupportAccessGrant, SupportSession**, PlatformEnums, PlatformRoles.                                                                                                                                                                                                        |
| `Modules/Tenancy/Tenancy.Infrastructure`      | `CatalogDbContext`: schema `catalog`, own DB `NWFM_Catalog`; it also holds the DataProtection key ring. Ported: `CatalogTenantStore` (30s snapshot plus `Invalidate`), `TenantConnectionStringResolver` (role-aware, fail-closed, `EF.IsDesignTime` fallback), `CatalogXmlRepository`, `DataProtectionSecretProtector`, `PlatformAuditWriter`. New: `TenantBoundaryModule`, `TenantDatabaseVerifier`, `TenantFanOut`. Entry point: `AddTenancyRuntime(role)`.                                                                                                            |
| `Modules/Tenancy/Tenancy.Api`                 | `TenantResolutionMiddleware`, `TenantRoutes`, `UseTenantRequestScope()`/`UseTenantResolution()`, and `TenantClaimBinder` (called from JwtBearer `OnTokenValidated`). The support-session token re-validation lives here too.                                                                                                                                                                                                                                                                                                                                             |
| `Modules/Platform/Platform.Application`       | Use cases (see the API section), `ICatalogDbContext`, `PlatformAuthorizeAttribute` + `PlatformAuthorizationBehavior`, `PlatformPolicies`, and the provisioning, inspection, email and captcha interfaces.                                                                                                                                                                                                                                                                                                                                                                |
| `Modules/Platform/Platform.Infrastructure`    | Catalog **migrations** plus the design-time factory. Provisioning: `SignupProvisioner`, `ManagedDatabaseCreator`, `ByoDatabaseInspector`, `TenantAdministratorProvisioner`, `SignupProvisioningWorker`. Services: `TenantDatabaseManager`, `TenantMigrationRunner`, `TenantAdministratorDirectory`, `TenantConnectionViewer`, `PlatformInitialiser`, `PlanSeedData`. Security: `PlatformCredentials`, `CaptchaVerifier`. Messaging: SMTP, plus a mock that writes mail to a folder. Lifecycle: `TenantLifecycleOperations`, moved from `Auth.Infrastructure/Operations`. |
| `Modules/Platform/Platform.Api`               | MVC controllers, `PlatformScopeMiddleware`, `CurrentPlatformUser`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `backend/src/NWFM.Platform.Api` (host, :5180) | Composition: `AddTenancyRuntime(Administration)`, module **persistence** DI, Platform infra, MediatR (Platform plus the module handlers the seeds use). `TenantSeeds/` holds the workflow demo seed, moved here from NWFM.Api. No module `.Api` parts. Do not declare a public `Program` partial, so it doesn't clash with NWFM.Api in tests.                                                                                                                                                                                                                            |
| `NWFM.Api` (:5081)                            | Adds `AddTenancyRuntime(Runtime)` and the resolution middleware. Loses all migration and seed code.                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `tools/NWFM.TenantAdmin`                      | A thin break-glass CLI over `Platform.Infrastructure/Lifecycle`. Optional `--tenant-key` with `NWFM_CATALOG_CONNECTION`.                                                                                                                                                                                                                                                                                                                                                                                                                                                 |

**Module DI split**, following the ref's `AddAuthPersistence`/`AddAuthSecurity`:

- **Auth** splits into `AddAuthPersistence` (DbContext, `AddIdentityCore`, stores, token providers, `AuthTenantDatabase`) and `AddAuthSecurity` (JWT, SAML, policies, `TenantSessionValidator`).
- **Workflow and Tasks:** hosted services move into `AddWorkflowWorkers()` and `AddTasksWorkers()`. Only NWFM.Api calls these.
- **DbContexts:** every module DbContext is registered as
  `AddDbContext<X>((sp,o)=>o.UseSqlServer(sp.GetRequiredService<ITenantConnectionStringResolver>().ResolveForCurrentTenant(), …))`.
  The existing history-table options and interceptors stay.

## Mapping the fe1db84 controls to per-request controls

| fe1db84 piece                                                                                                         | New form                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Application:TenantId`, `ApplicationOptions`, `TenantIsolationOptions`, `ITenantAvailability`/`SqlTenantAvailability` | Deleted. Replaced by `TenantInfo.IsServing` (catalog status + subscription). `TenantSessionValidator` also reads `DeploymentBoundary.IsActive` uncached on every request, as an immediate kill switch.                                                                                                                                                                                                                                                                                                                                                                                      |
| `NWFM.Api/Services/ApplicationContext.cs`                                                                             | Keeps `IWorkflowActorContext` only.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Resolution (new; ref `Tenancy.Api\Middleware\TenantResolutionMiddleware.cs`)                                          | The signed `tid` claim wins. Otherwise the host is used: a `TenantHost` row or `{key}.{RootDomain}`, excluding reserved labels. Otherwise `X-Tenant`, which is honoured only when `AllowTenantHeader` is on (Development) **and** the request is anonymous. If the host or header disagrees with `tid`, the response is 400 `Tenant.Mismatch`. Gates after resolution: status/subscription; schema-behind → 503; unverified DB → 503 `Tenant.Unverified`. Anonymous refresh resolves the tenant from the refresh-token prefix. Support-session tokens are re-validated against the catalog. |
| `RequestContextMiddleware.cs`                                                                                         | Runs right after resolution. It still rejects `x-tenant-id`/`x-organization-id` and `organizationId`/`tenantId` selectors that differ from `Current.Id`. It still asserts `tid == Current.Id` (event 4101) and still selects the actor.                                                                                                                                                                                                                                                                                                                                                     |
| `TenantScopeFilter.cs`                                                                                                | Unchanged. It compares against the ambient `ICurrentTenant`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `TenantDatabaseBoundary.cs` **init mode**                                                                             | Becomes `TenantBoundaryModule` (Order 0), run by the platform Migrate step **before** the EF migrations. It applies the same DDL: the Tenancy schema, `DeploymentBoundary` bound to the catalog id (51001 refuses to adopt a foreign DB), `LifecycleAudit`, `nwfm_form_storage_owner` plus ownership of the `FormEngine` schema, and the contaminated-row scan. After the module migrations, a **RuntimeGrants** finaliser applies the grants from `scripts/provision-local-runtime.sql`, parameterised by `RuntimePrincipal`.                                                              |
| `TenantDatabaseBoundary.cs` **request mode**                                                                          | Becomes `TenantDatabaseVerifier`. It keeps the same checks (no admin roles, no cross-DB access, no impersonation, the binding row, form-schema ownership) plus the connection-string rules (pool ≤ 100, no system DBs, no `NWFM_RECOVERY_`). It runs at ValidateDatabase and Activate in provisioning, and on demand (`POST …/database/verify`). NWFM.Api runs it lazily per (tenant, connection-string hash): a pass is cached for 10 min, a fail for 30 s, and failures deny access.                                                                                                      |
| `TenantResourceLimits.cs`                                                                                             | A pre-auth global budget (process concurrency and per-IP). After resolution, a `PartitionedRateLimiter` keyed by tenant, with plan overrides. Principal partitions are `{tenantId}:{user/ip}`.                                                                                                                                                                                                                                                                                                                                                                                              |
| `TenantSecuritySignals.cs`                                                                                            | Bounded per-tenant windows plus an "unresolved" bucket. New event **4203 TenantResolutionRefused**.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| JWT `tenant_id`                                                                                                       | Renamed to `tid`. The refresh token becomes `TenantRefreshTokens.Compose`. Existing sessions are invalidated, so everyone logs in again.                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `RequireApiKeyAttribute`, the SSO temp-cookie protector, `AppContextController`                                       | The consumer's tenant must equal the ambient tenant. A `TenantAwareDataProtector` adds the tenant id to the purpose. AppContext returns `{Id, Key, Name}` from `ITenantContext`.                                                                                                                                                                                                                                                                                                                                                                                                            |
| `HybridCacheService`                                                                                                  | Builds the key prefix per call from `ITenantContext`, and throws when no tenant is bound. Platform keys go under `platform:`.                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `LocalFileStorage`                                                                                                    | Per-tenant root `{FileStorage:Root}/{key}` or `TenantInfo.StoragePath`. The bootstrap tenant keeps the current root. The `.nwfm-tenant` marker is checked lazily per root and cached.                                                                                                                                                                                                                                                                                                                                                                                                       |
| Hosted services (Workflow ×4, C2M) via `WorkflowTenantAdmission.cs`                                                   | `ITenantFanOut.ForEachServingTenantAsync`: `BeginScope(tenant)`, a new DI scope and a log scope; try/catch per tenant; bounded parallelism. The message `OrganizationId` must equal `Current.Id`.                                                                                                                                                                                                                                                                                                                                                                                           |
| DataProtection                                                                                                        | One catalog key ring for both hosts (`SetApplicationName("NWFM")`). NWFM.Api calls `DisableAutomaticKeyGeneration()`. A **one-time import of the legacy `DataProtection-Keys` XML** keeps Workflow connection secrets readable.                                                                                                                                                                                                                                                                                                                                                             |
| Catalog access from NWFM.Api                                                                                          | Through a catalog **reader login** (SELECT only). Only the Platform API writes to the catalog.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |

## Tenant database modules (`ITenantDatabaseModule`, run by `TenantDatabaseManager` in this order)

| Order | Module                                                                                                                       | Migrate                                           | Seed                                                                                                            |
| ----- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| 0     | `TenantBoundaryModule` (versioned id `…_Boundary_v1`)                                                                        | Boundary DDL                                      | none                                                                                                            |
| 10    | `AuthTenantDatabase` (refactors `AuthDatabaseInitializer`; port ref `Auth.Infrastructure\Persistence\AuthTenantDatabase.cs`) | EF + SQL objects                                  | Roles, permissions, matrix; the default admin only for the Bootstrap profile                                    |
| 20    | `FormEngineTenantDatabase`                                                                                                   | History move → EF → `EnsureSubmissionTablesAsync` | Example forms                                                                                                   |
| 30    | `TasksTenantDatabase`                                                                                                        | EF                                                | Task types bound to the forms                                                                                   |
| 40    | `WorkflowTenantDatabase`                                                                                                     | EF                                                | The Workflow `Tenant` row (Id = catalog id)                                                                     |
| —     | `WorkflowDemoSeed` (platform `TenantSeeds/`)                                                                                 | none                                              | Bootstrap profile + `WorkflowDemo:Enabled` only (moved here from `DatabaseInitializer`/`WorkflowWorkspaceDemo`) |
| last  | RuntimeGrants finaliser                                                                                                      | Grants for `RuntimePrincipal`                     | none                                                                                                            |

**Local dev keeps working.**

- The Platform `appsettings.Development.json` sets `Tenancy:ConfigurationTenants[]`, which holds `nwfm` with the existing id, `ConnectionStringName: DefaultConnection`, `RuntimePrincipal: nwfm_request`, the existing storage root, `Hosts: ["localhost","127.0.0.1"]`, the Bootstrap seed profile and the enterprise plan.
- `PlatformInitialiser` runs on startup. It migrates the catalog, seeds the plans, registers the configuration tenants, then migrates and seeds them, and creates the bootstrap platform admin.
- `http://localhost:4200` keeps resolving to `nwfm`. Every other tenant is reached at `{key}.localhost:4200`.

## Platform API: controllers in `Platform.Api`, use cases in `Platform.Application`

Use-case logic is ported from the ref's `Platform.Application\{Signup,Tenants,Plans,DatabaseServers,Users,Audit,Auth,Subscriptions}`. The ref's support endpoints are ported too.

| Controller                                                                                                                                                  | Routes                                                                                                                                                                                                                   |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `PublicSignupController` (anonymous, rate-limited, captcha on submit) `api/v1/public/signup`                                                                | `GET options`, `GET key-availability`, `POST` (returns id + **status token**), `GET {id}?token=`, `POST {id}/verify`, `POST {id}/resend`, `GET database/script`, `POST {id}/database/test`, `POST {id}/database`         |
| `PublicCatalogController` (anonymous) `api/v1/public`                                                                                                       | `GET plans`, `GET workspaces` (only `ListedPublicly` tenants)                                                                                                                                                            |
| `PlatformAuthController` `api/v1/platform/auth`                                                                                                             | `POST login`, `GET me`, `POST change-password`                                                                                                                                                                           |
| `PlatformTenantsController`                                                                                                                                 | CRUD; `POST {id}/status`; `PUT {id}/subscription`; `GET {id}/migrations/pending`; `POST {id}/migrate`; `POST migrate-all`; `GET {id}/database/connection`; `POST …/reveal` (Admin, audited); `POST {id}/database/verify` |
| `PlatformTenantAdminsController`                                                                                                                            | List, create, status, resend credentials                                                                                                                                                                                 |
| `PlatformTenantLifecycleController` (Admin)                                                                                                                 | `POST offboard`, `GET retention`, `POST verify-restore`                                                                                                                                                                  |
| `PlatformSignupsController`                                                                                                                                 | List, get (with events), approve, reject, retry                                                                                                                                                                          |
| `PlatformPlansController`, `PlatformDatabaseServersController` (Admin, plus `POST {id}/test`), `PlatformUsersController` (Admin), `PlatformAuditController` | —                                                                                                                                                                                                                        |
| `PlatformSupportController`                                                                                                                                 | Start, end, list support sessions (requires a tenant-granted `SupportAccessGrant`; one-time code handoff)                                                                                                                |

**Tenant API additions**, in Auth.Api, with Auth.Application use cases:

- `POST api/v1/auth/activate` (anonymous; port ref `ActivateAccount`)
- `POST api/v1/auth/support/exchange` (anonymous; swaps a one-time code for a read-only support token)
- Tenant-admin endpoints to grant and revoke support access
- `SupportReadOnlyBehavior`: a MediatR pipeline behaviour that blocks commands when `support_scope=readonly`

**Provisioning steps** (port ref `SignupProvisioner`; idempotent per-step checkpoints):

1. **ReserveTenant.** Saved on its own, so the unique key settles races.
2. **CreateDatabase.** For a managed DB, `ManagedDatabaseCreator` picks the least-loaded `DatabaseServer`. It creates the DB plus **owner and runtime principals**, using contained users when the server supports them. For BYO, it encrypts and stores both credentials.
3. **ValidateDatabase.** For BYO, `ByoDatabaseInspector` checks: SSRF-screened DNS/IP, connect, encryption, SQL Server 2016+, owner permissions, and that the DB is empty or holds an NWFM schema with a matching binding. For a managed DB, it checks the connection.
4. **Migrate.** Boundary → modules → RuntimeGrants.
5. **Seed.** Standard profile.
6. **CreateAdministrator.**
7. **Activate.** Runs the runtime verifier first, then sets Active and calls `Invalidate()`.
8. **SendActivationEmail.** Sends `{TenantAppUrlTemplate}/activate?userId=&token=`.

**BYO script:** `BuildSetupScript` emits an owner user (`db_owner`) plus a runtime user with no role memberships.

## Frontend

**`frontend/projects/platform-web`**: a new project in the existing `angular.json`. It builds to `dist/platform-web`, serves on :4300, and proxies via `proxy.platform.conf.json` (`/api` → :5180).

**Sharing with the tenant app.** platform-web is self-contained and reuses the design system read-only through new root `tsconfig.json` aliases:

- `@nwfm/ui` → `src/app/shared/components/index.ts`
- `@nwfm/theme` → `src/app/core/theme/water-preset.ts`
- `@nwfm/i18n` → `src/app/core/i18n/locale.service.ts`

An ESLint `no-restricted-imports` rule for `projects/platform-web/**` bans `@core/*`, `@features/*`, `@models` and relative paths into `src/`.

**Structure** (port ref `src\frontend\projects\platform-web\src\app\*`, rewritten with NWFM idioms: standalone components, signals, PrimeNG + Tailwind, ngx-translate en/ar with RTL):

- **`core/`:**
  - `api` (ng-openapi-gen with `ng-openapi-gen.platform.json`, plus the `public-api` and `platform-api` facades)
  - `auth/platform-auth.store.ts` (staff token in **sessionStorage**)
  - `config`
  - `guards` (platform / admin / guest)
  - `interceptors`
- **`features/landing/sections/*`:** nav, hero, features, how-it-works, pricing (from `GET public/plans`, linking to `/signup?plan=`), workspaces, FAQ, CTA and footer.
- **`features/signup`:**
  - The wizard: organisation and tenant key with a debounced availability check, administrator, plan, DB mode (Managed / BYO), review.
  - `signup-verify`.
  - `signup-status` (polls with the status token).
  - `signup-database` (BYO): generate the script, enter owner + runtime credentials, test, save.
- **`features/console/*`:**
  - Tenants list and detail: status, subscription, migrations, connection view/reveal, admins, lifecycle, support.
  - Signups (approve / reject / retry).
  - Plans, database servers, platform users, audit.
- **i18n:** `projects/platform-web/public/assets/i18n/{en,ar}.json`.

**Tenant app (`frontend/src/app`):**

- **Tenancy services:**
  - `core/tenancy/tenant-context.service.ts`, ported. It reads the tenant key from the subdomain, with reserved labels excluded. `?tenant=` works only when `allowTenantQuery` is on.
  - `core/interceptors/tenant.interceptor.ts`, ported. It sends `X-Tenant` on anonymous calls only.
  - Interceptor order: `[auth, tenant, context, error]`.
- **Error mapping:** `errorInterceptor` maps the `Tenant.*` refusal codes to `/workspace-unavailable?reason=`.
- **New pages:** `workspace-unavailable`, `activate` (`/activate`), and support-access management in the admin area.
- **Session:** `AuthStore` stores the tenant key and clears the session when the key changes.
- **Dev serving:** `proxy.conf.json` sets `changeOrigin:false` so the Host header reaches Kestrel, and `allowedHosts` includes `.localhost`.
- Regenerate the API models and add en/ar keys.

## Dev, ops, CI and docs

- **Ports:** Platform API **:5180**, platform-web **:4300**, tenant API :5081, tenant app :4200.
- **Dev scripts:**
  - `start-dev.bat` starts the platform first, then backend, frontend and platform-web.
  - `scripts/check-dev-port.ps1` checks the new ports.
  - New root npm scripts: `start:platform`, `build:platform`, `generate:api:platform`. Existing scripts name the `frontend` project explicitly.
- **Config:**
  - NWFM.Api: remove the `Application` section; add `ConnectionStrings:CatalogConnection` and `Tenancy{RootDomain, AllowTenantHeader (Development only), SnapshotRefreshSeconds, ConnectionRole:"Runtime", WorkerParallelism, VerificationMinutes}`; set `AllowedHosts` to include `*.localhost`.
  - Platform appsettings: `PlatformJwt` (its own key), `Platform{RequireSignupApproval:true, TenantAppUrlTemplate, EgressIps, ManagedDatabaseNamePrefix, Captcha}`, and `Email:UseMock` in dev.
- **Docker:**
  - `backend/Dockerfile` takes `ARG APP`; `frontend/Dockerfile` takes `ARG APP` and uses `nginx.<app>.conf` (the tenant nginx forwards `Host`).
  - `docker-compose.yml` replaces the `bootstrap` profile with a `platform` service, adds `platform-web` and the catalog DB and reader login, and keeps `provision-runtime` for the bootstrap tenant.
- **CI (`.github/workflows/ci.yml`):** the frontend job tests and builds both projects. The security job adds an `mssql` service so `Category=SqlIntegration` tests run.
- **E2E (`scripts/e2e/start-app.mjs`):**
  - Start the SQL container, then the Platform API (configuration tenants `alpha` → `NWFM_A` and `beta` → `NWFM_B`, mock email, the container registered as a DatabaseServer), then **one** NWFM.Api, then both SPAs.
  - Drop the second API and `tenant-proxy.mjs`.
  - Node probes send an explicit `Host` header.
  - Rewrite `tests/tenant-security.e2e.ts` to cover: cross-host token replay, the header ignored when a bearer is present, an unknown key, suspension, the schema-behind gate, and the selectors.
  - Add `tests/platform-signup.e2e.ts`: landing → managed signup → verify link from the mock mail folder → console approve → Completed → activate → log in at `{key}.localhost:4200`.
  - Update `tenant-lifecycle-drill.mjs`.
  - Read `.agents/skills/e2e/SKILL.md` before writing e2e tests. Recording replay receipts needs the user's ChatGPT sign-in, per AGENTS.md.
- **Docs:**
  - Rewrite `docs/security/tenant-security.md` and `docs/tenant-lifecycle-operations.md`.
  - Update `docs/security/tenant-controls.json` (implementation and test paths, and the model description) **in the same PR** as each file rename, because `scripts/security/tenant-coverage.mjs` fails on missing evidence.
  - Update `README.md` with the new dev flow.

## Delivery phases

Work on branch `feature/saas-tenancy`, created off `codex/owasp-tenant-security-platform`. Each phase is a stacked PR and is buildable and testable on its own. First, delete the stale ignored `bin/obj`-only folders: `backend/src/NWFM.Platform.Api`, `backend/src/Modules/{Platform,Tenancy}` and `backend/src/Shared`.

| #   | PR                                               | Content                                                                                                                                                                                                                                                                                         |
| --- | ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P1  | Ambient tenant kernel                            | Port MultiTenancy into NWFM.Shared, add `AmbientCurrentTenant`, and a temporary `ConfigurationTenantStore` still driven by `Application:TenantId`. Cache, storage, signals, limits, worker admission, the SSO protector and AppContext switch to `ITenantContext`. Add a single-tenant fan-out. |
| P2  | Per-tenant DbContexts and DB modules             | Resolver-driven DbContexts; the DI split; Boundary, the four modules, RuntimeGrants and `TenantDatabaseManager`; `TenantDatabaseVerifier`. NWFM.Api's `InitializeOnly` temporarily delegates to the manager. Includes a SQL spike on contained users vs. the verifier checks.                   |
| P3  | Catalog and Platform API host                    | Tenancy.Domain/Infrastructure, catalog migrations, `NWFM.Platform.Api` (initialiser, plans, configuration tenants, platform admin, platform JWT, migrate endpoints, key-ring import). NWFM.Api reads the catalog pinned to one tenant (transitional). start-dev and e2e switch over.            |
| P4  | Per-request resolution (the security PR)         | Remove the pinned mode and the legacy options and boundary code; add resolution middleware, `tid`, the token binder, the gates and catalog fan-out. Update tenant-controls.json and the docs. E2E runs two tenants on one API.                                                                  |
| P5a | Staff provisioning (can run in parallel with P4) | `ManagedDatabaseCreator`, provisioner and worker; the tenants, admins, plans, DB servers, users, audit, lifecycle and support controllers; the CLI rework.                                                                                                                                      |
| P5b | Public signup, activation, support access        | The SignupRequest flow, BYO inspector and script, approve/reject/retry, email, captcha, rate limits; `auth/activate`, support exchange and grants, `SupportReadOnlyBehavior`.                                                                                                                   |
| P6  | platform-web (can run in parallel with P7)       | Landing page, signup, console, i18n, the ESLint guard, and CI for both apps.                                                                                                                                                                                                                    |
| P7  | Tenant app tenancy UI                            | Tenant context, interceptor, and the unavailable, activate and support-access pages.                                                                                                                                                                                                            |
| P8  | Ops and e2e                                      | Dockerfiles, nginx, compose, the CI mssql service, `platform-signup.e2e.ts`, and the final docs.                                                                                                                                                                                                |

## Verification

**Backend:** `dotnet build backend/NWFM.sln -c Release` and `dotnet test backend/tests/NWFM.Tests` pass after every phase.

**New tests in `NWFM.Tests/Tenancy` and `NWFM.Tests/Platform`:**

- `TenantContextAccessor`
- `CatalogTenantStore` (host, subdomain, reserved labels, refresh)
- the resolver (fail-closed, roles)
- `TenantSchema`
- manager ordering
- fan-out (skips tenants that aren't serving, isolates failures)
- the `SignupRequest` state machine
- provisioner resume from each step
- BYO inspector SSRF
- secret-purpose separation
- `PlatformAuthorizationBehavior`
- `PlatformEndpointBoundaryTests`
- architecture references
- the catalog model inventory (secret columns are only `Encrypted*`)
- **SQL integration tests** (`Category=SqlIntegration`, run when `NWFM_TEST_SQL_CONNECTION` is set): boundary bind and refuse; the verifier rejects `db_owner` and passes the restricted principal; managed tenant creation; two tenants in one process.

**Updated tests:** the Security/* suites move to per-request semantics: one tenant exhausting its budget doesn't throttle another; platform and tenant JWTs never cross-validate; per-tenant signals and 4203; `TenantEndpointBoundaryTests` covers the new anonymous actions.

**Frontend and checks:**

- `npm run test:frontend` covers both projects: tenant-context, the interceptor, platform guards, wizard validators, en/ar key parity.
- `npm run build:frontend` and `npm run build:platform`.
- `npm run security:tenancy` (controls coverage) and `npm run quality`.

**E2E:** `npm run test:e2e` with tenant-security and platform-signup.

**Manual check:** run `start-dev.bat`, then:

1. Open `http://localhost:4300`. The landing page should show the plans.
2. Sign up "acme" with a managed DB and open the verify link from `data/emails`.
3. Approve the signup in `/console`. It should reach Completed.
4. Open the activation link at `acme.localhost:4200/activate`, then log in. You should see an empty workspace with the example forms.
5. Confirm `localhost:4200` still serves the `nwfm` tenant.
6. Confirm an acme token replayed against `localhost:4200` is refused.

## Risks

- **The catalog is the crown jewel.** It holds every encrypted connection string and the key ring. Mitigations: a reader-only login for the data plane, no key generation in NWFM.Api, per-purpose protectors.
- **The `tenant_id` → `tid` rename and the refresh-token format change force everyone to log in again.** Webhook and API-key callers must call the tenant host.
- **Deploy order.** Run `migrate-all` before rolling NWFM.Api, because of the schema-behind gate. Keep migrations additive.
- **Connection pools.** Each tenant gets its own SQL connection pool, so the total grows with the tenant count. Cap Max Pool Size per tenant and bound worker parallelism.
- **The catalog snapshot can be up to 30 s stale for anonymous calls.** Authenticated calls are covered by the per-request `DeploymentBoundary.IsActive` check.
