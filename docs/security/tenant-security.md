# Tenant security implementation and operating requirements

Source: [OWASP Multi-Tenant Application Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html), retrieved 2026-10-20.
The unmodified article and its CC-BY-SA-4.0 license are in `vendor/`. The adapted
requirement mapping in `tenant-controls.json` is also attributed to the OWASP Cheat
Sheet Series Team and provided under CC-BY-SA-4.0.

## Supported boundary

NWFM is a **database-per-tenant** service. Each workspace (tenant) has its own SQL
Server database, its own restricted runtime login, and its own media storage root.
A single tenant API (`NWFM.Api`) serves every workspace and binds each request to
exactly one of them before any handler runs. A separate control plane
(`NWFM.Platform.Api`) owns the tenant catalog (`NWFM_Catalog`): signup, approval,
provisioning, migrations and staff operations.

Auth, FormEngine and Tasks have tenant-owned tables without row tenant columns;
their isolation is the database itself. Workflow keeps row ownership filters and
write checks as defense in depth. Two workspaces never share a database. The
control plane refuses to bind a database that is already bound to another
workspace, or that contains another tenant's workflow records. The tenant API
refuses to serve a workspace whose runtime credential can administer its database
or reach another one.

PostgreSQL RLS, PostgreSQL tenant session variables, SQLAlchemy, S3 buckets and
presigned URLs in the article are illustrative technologies, not NWFM features.
Their underlying authorization and isolation requirements are mapped to the actual
implementation. Sharing one database between workspaces (row-level multi-tenancy)
would need a separate design, schema migration, and negative-path tests for every
table. This work does not offer that model.

### Request binding

`TenantResolutionMiddleware` (Tenancy.Api) runs after authentication and before
authorization. It considers evidence in this order:

1. **The signed `tid` claim.** `TenantSessionBinder` binds it while the bearer
   token is validated. It also re-checks the session against that workspace's own
   database: security stamp, account state, and `DeploymentBoundary.IsActive`.
2. **The request host.** This is a registered `TenantHost` row or
   `{key}.{Tenancy:RootDomain}`. Reserved labels such as `www`, `api` and
   `platform` never resolve.
3. **The `X-Tenant` header.** It is honoured only when `Tenancy:AllowTenantHeader`
   is on (development) **and** the call carries no bearer token.
4. **The refresh-token prefix.** This applies only to the anonymous refresh call.

A host or header that names a different workspace from the token gets
`400 Tenant.Mismatch`. An unknown workspace key in the header or refresh token
gets `404 Tenant.NotFound`. A host that names no workspace, such as an
unregistered subdomain, gets `400 Tenant.Required`. A tenant-scoped path that
cannot be bound is refused; it is never served from a default. After resolution, the request passes these gates:

- the catalog status and subscription
- `503 Tenant.SchemaUpgradePending` while the database is behind the deployed schema
- `503 Tenant.Unverified` while the runtime credential fails `TenantDatabaseVerifier`

Every refusal emits event `4203/TenantResolutionRefused`.

`RequestContextMiddleware` still rejects `x-tenant-id` / `x-organization-id`
headers and `organizationId` / `tenantId` selectors that differ from the bound
workspace. It also re-asserts `tid` against the bound workspace (event 4101).

## Implemented controls

| Area                               | Implementation                                                                                                                                                                                                                                                                                                                                                     | Evidence                                                                                                                                                                          |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Tenant resolution                  | Signed `tid` claim first; host or development-only anonymous header otherwise; mismatches refused; no default workspace; refusals audited (4203)                                                                                                                                                                                                                   | `TenantResolutionTests`, `TenancyKernelTests`, two-workspace E2E (cross-host token replay)                                                                                        |
| Identity and current authorization | Tenant/session/security-stamp claims; current account, team, membership and permissions; atomic refresh consumption; immediate logout/account revocation; selector rejection and request-context cleanup; support sessions re-validated against the catalog and read-only unless granted otherwise                                                                 | Session, request-context, endpoint inventory and scope mutation tests; E2E                                                                                                        |
| Database                           | Database per workspace, bound by `TenantBoundaryModule` to the catalog id; owner credential only in the control plane; restricted runtime login granted by `TenantRuntimeGrantsModule`; `TenantDatabaseVerifier` checks the actual SQL permissions before a workspace is served; row ownership immutable on tracked/detached writes; classified EF model inventory | `TenantWriteBoundaryTests`, `TenantModelInventoryTests`, `PlatformBoundaryTests`, E2E launcher probes (administrative, mis-wired, impersonating and foreign-database credentials) |
| Catalog                            | The tenant API reads the catalog through a reader login with column-level grants. It never selects owner connection strings and never generates Data Protection keys. Connection strings are encrypted per purpose (runtime, owner).                                                                                                                               | `CatalogTests`, `scripts/provision-catalog-reader.sql`                                                                                                                            |
| Cache and sessions                 | The cache key prefix is derived per call from the bound workspace, and a call without one throws. Current grants are validated independently of cache. The SSO cookie protector includes the tenant id in its purpose, workflow credentials are protected per tenant, and API responses are no-store.                                                              | `TenantCacheIsolationTests`, session and E2E revocation tests                                                                                                                     |
| Service credentials                | Explicit tenant/environment/scope and hashed random API keys, accepted only on their own workspace; canonical versioned webhook signing; production rejection of missing or checked-in JWT keys; separate platform and tenant signing keys                                                                                                                         | Service credential and JWT configuration tests, webhook tests and E2E                                                                                                             |
| Resource use                       | Pre-authentication global concurrency and per-address budgets. Per-workspace request and concurrency partitions (plan overrides apply), principal partitions keyed by workspace, and auth rate limits. Bounded SQL pools, transactional pending-queue limits per workspace, and bounded worker parallelism.                                                        | `TenantResourceLimitTests`, `WorkflowQueueCapacityTests`, E2E concurrent overload of one workspace                                                                                |
| Async work                         | Each worker pass fans out over serving workspaces only (`ITenantFanOut`) under that workspace's scope, with per-workspace failure isolation. Queue records are scoped and idempotent, ingress is authenticated, and processing and retries are bounded.                                                                                                            | `WorkflowWorkerTenantTests`, `TenancyKernelTests`, existing integration suites, E2E                                                                                               |
| Files                              | Per-workspace storage root with a durable ownership marker; strict relative paths; symlink/reparse rejection; bounded streaming; no silent overwrites; exact database/API authorization                                                                                                                                                                            | `TenantFileStorageTests`, `SubmissionMediaIsolationTests`, E2E                                                                                                                    |
| Lifecycle                          | Signup with e-mail verification and staff approval; checkpointed provisioning; verification before activation; one-time activation links; operator offboarding and recovery tooling                                                                                                                                                                                | `CatalogTests`, `PlatformBoundaryTests`, lifecycle drill in the E2E launcher                                                                                                      |
| Audit                              | Verified tenant and trace context; rejected-access records; per-workspace denial windows; platform audit trail for staff and support actions; sensitive OTP/message content removed                                                                                                                                                                                | Request-context/service-credential tests and E2E; external collector evidence remains required                                                                                    |

## Every PR

The existing **Security** check validates every article prose block, list point,
strategy row and complete code example against the pinned catalog. It also checks
the current upstream article for drift and runs the tenant security regressions
(including `NWFM.Tests.Tenancy` and `NWFM.Tests.Platform`). A missing source or
missing evidence fails the check. An upstream change needs a fresh review and an
updated mapping. If upstream verification is unavailable, the check fails.

The complete Backend and Frontend suites and builds still run for both web apps.
The E2E launcher builds this disposable environment:

- one SQL Server container
- the control plane, with two configuration workspaces (`alpha` at `localhost` and
  `beta` at `beta.localhost`), each in its own database
- one tenant API, running under each workspace's restricted login and the catalog
  reader login

Before the security tests run, the launcher proves fail-closed behaviour with
short-lived probe hosts:

- An administrative credential is refused (`Tenant.Unverified`).
- A credential wired to another workspace's database is refused.
- A login that can impersonate is refused, and so is a login without metadata
  visibility.
- A third workspace pointed at a bound database is refused adoption and put into
  maintenance.

Test users, keys and database passwords are disposable. Security tests need no
model calls or OAuth credentials. All existing workflow E2E recordings remain
subject to strict replay.

CI publishes a coverage inventory and test results. Inventory completeness is
**traceability**, not a semantic proof that arbitrary future code is secure.
Behavioral tests, discovered model/controller/background-worker inventories and
owner review supply additional checks. PR review must examine changed
authorization, persistence, cache, storage, worker and operational paths against
the catalog. Newly introduced paths need positive same-tenant and negative
cross-tenant tests.

No GitHub protection or review policy is weakened. The owner retains the final
merge; independent approval count remains zero. No additional approval layer is
introduced.

## Provisioning and rollout

1. **Deploy the control plane first.** It needs the owner credentials:
   - a catalog database login
   - a server login per managed `DatabaseServer`

   It also needs its own `PlatformJwt` signing key and the bootstrap platform
   administrator password, the latter only until the first sign-in. On start it
   migrates the catalog, seeds the plans, registers configuration workspaces,
   ensures a current key in the shared Data Protection key ring, and migrates
   those workspaces.

2. **Provision the catalog reader** with `scripts/provision-catalog-reader.sql`.
   The tenant API connects to the catalog only with this login.
3. **Workspaces from signup.** A confirmed and approved signup is provisioned in
   checkpointed steps:

   1. Reserve the key.
   2. Create or accept the database.
   3. Validate it.
   4. Migrate:
      1. Boundary binding.
      2. Module migrations.
      3. Runtime grants.
   5. Seed (roles, permissions, example forms and task types).
   6. Create the administrator.
   7. Verify the runtime credential.
   8. Activate.
   9. Send the one-time activation link.

   The two database options work differently:
   - **Platform-managed databases** get an owner principal and a separate runtime
     principal.
   - **Bring-your-own databases** need both principals from the customer's setup
     script. They are inspected over an SSRF-screened connection, with encryption
     enforced.

4. **Configuration workspaces**, such as an adopted single-tenant deployment, keep
   their existing database and id. The operator creates the runtime login (see
   `scripts/provision-local-runtime.*`). The control plane grants it the runtime
   permissions at the next migration. The tenant API refuses the workspace until
   the login exists and passes verification.
5. **Runtime permissions.** `TenantRuntimeGrantsModule` grants:
   - CRUD/EXECUTE on Auth, FormEngine, Task and Workflow
   - SELECT on `Tenancy.DeploymentBoundary`
   - VIEW DEFINITION on `Tenancy.LifecycleAudit` and on the form storage owner
   - CREATE TABLE plus ALTER on FormEngine, for dynamic submission tables

   The CREATE TABLE grant is a deliberate elevated capability inside the isolated
   database. The bootstrap assigns FormEngine to a dedicated non-login schema owner,
   so a form trigger's ownership chain cannot modify protected schemas, and the
   verifier checks that separation. Never grant CONTROL, db_owner,
   db_securityadmin, db_ddladmin, impersonation or boundary writes. Keep
   `VIEW ANY DATABASE` metadata visibility (normally inherited from `public`) so
   the verifier can check the complete database access inventory.

6. **Storage.** Each workspace's media root is
   `{FileStorage:Root}/{key}` unless the catalog names one. The `.nwfm-tenant`
   marker prevents accidental reuse but is not an OS authorization boundary. Never
   delete a marker to repurpose a volume.
7. **Keys and caches.** Use separate production signing keys for the platform and
   the tenant API. The Data Protection key ring lives in the catalog. The control
   plane creates and rotates the keys; the tenant API only reads them. Redis
   namespaces prevent key collisions but do not restrict raw access to a shared
   Redis service.
8. **Deploy order.** Run the console's _migrate all_ before rolling out a tenant API
   that expects a newer schema. Workspaces still behind return `503` until they are
   migrated. Keep migrations additive.
9. **Forced sign-in.** The `tenant_id` claim became `tid`, and refresh tokens carry
   a workspace prefix, so existing sessions and temporary SSO cookies need a new
   sign-in. Encrypted Workflow connection credentials keep their original
   protection purpose. Import the legacy key ring into the catalog, or re-enter
   those credentials, before switching hosts. Webhook and API-key callers must call
   their workspace's host.

The disposable E2E launcher (`scripts/e2e/start-app.mjs`) is a complete worked
example of this sequence and of the minimal SQL grants. Adapt it to your
deployment's secret manager, and never deploy its development seed accounts.

## Proposed policy defaults

These values were proposed with the owner's permission on 2026-10-20 and remain
configurable. They are engineering defaults, not a statement of legal compliance.

| Policy                   | Proposed value                                      | Enforcement requirement                                                                                                                             |
| ------------------------ | --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Offboarding access       | Revoke immediately                                  | Stop API and delayed worker effects; revoke sessions/service credentials and rotate credential material before any reactivation                     |
| Active data deletion     | 30 days after offboarding                           | Cover database, files, caches, exports and replicas; legal holds block deletion and restrict access                                                 |
| Backup expiry            | 90 days after offboarding                           | Include immutable backups and object versions; demonstrate deletion or expiry in the backup platform                                                |
| Security audit retention | 365 days                                            | Restrict reads, protect against modification, then expire according to policy                                                                       |
| Export                   | On contract/product requirement                     | Authorized operator, tenant-only destination, encrypted transport/storage, expiry and audit                                                         |
| Encryption               | Managed encryption at rest, separate access context | Use a dedicated tenant key by default when available; require it for regulated/high-assurance tenants. Keep backup and key-recovery access separate |

Do not destroy an encryption key while retained backups or legally held data still
need it. Key creation, rotation, recovery and destruction require evidence from
the deployed key-management service.

## Restore and offboarding

Operator tooling must validate **actual restored data ownership**, the actual SQL
operator role and storage-root marker. A caller-provided JSON tenant label is not
authorization or evidence. Restore a backup into an isolated recovery resource;
never overwrite a live database to recover one tenant. Reject mixed-tenant data,
wrong-tenant ownership and unprivileged operators. Revoke restored sessions and
apply current user/role/retention rules before any authorized promotion.

The product has no shared snapshot download endpoint or tenant-facing bulk
restore capability. A shared backup cannot be made safe by relabeling its tenant;
it needs a separate selective recovery implementation and two-tenant drill before
use. Each workspace database is backed up separately.

The implemented [operator commands](../platform/tenant-lifecycle-operations.md) perform
transactional suspension, token/SSO revocation, account lockout and an audited
retention schedule; they verify actual recovery database rows and storage ownership
under a separate SQL operator role. They do not automatically delete production
resources or copy restored memberships into live access rules.

## Deployment evidence still required

CI cannot inspect a production environment that is not connected to this
repository. These are explicit obligations, not passing automated checks:

- Verify network/OS/SQL/key-manager grants using each deployed request identity,
  including denial against another tenant's stores and backup repositories.
- Configure CPU, memory, disk, storage capacity and database I/O quotas for each
  workspace database and global host/cluster safety limits. Application limits are
  per process; distributed replicas need an aggregate gateway/quota budget. Cap the
  connection pool per workspace: every served workspace adds its own SQL pool.
- Connect structured security events to a protected collector. Alert on repeated
  tenant mismatch/authorization denials, failed binding/role checks and abnormal
  queue or resource exhaustion; test notification delivery with synthetic events.
  The tenant API emits these JSON events:

  | Event | Name                       | When                                                                                                       |
  | ----- | -------------------------- | ---------------------------------------------------------------------------------------------------------- |
  | 4201  | `TenantAccessDenied`       | Each denied response                                                                                       |
  | 4203  | `TenantResolutionRefused`  | Each request that could not be bound to a workspace                                                        |
  | 4202  | `TenantIsolationAlert`     | 20 denials within 60 seconds, at most once per minute per workspace. Unresolved requests share one window. |
  | 4204  | `TenantDatabaseUnverified` | A workspace is refused because its runtime credential failed verification                                  |

  All of them carry the workspace id. Configure the collector to notify on 4202,
  on 4204, and on any control-plane migration failure (`TenantMigrationFailed` in
  the platform audit).

- Keep operator/lifecycle audit records append-only with separate retention access.
  Neither ordinary tenant users nor request database identities should edit them.
- Execute the configured retention jobs across active stores, cache, object
  versions, replicas, exports and backups. Record legal holds and expiry evidence.
- Run and retain a restore drill, verify authorized tenant data only, deny an
  unauthorized operator and apply current memberships after recovery.
- Verify encryption, key isolation, key rotation and recoverability against the
  selected managed services.

## Validation results

Validated locally on 2026-10-27 for the database-per-tenant model:

- Complete backend suite: **951 passed**, zero failures or skips. Release solution
  build passed.
- E2E `tests/tenant-security.e2e.ts`: **5 passed** against one shared tenant API
  serving two workspaces (`alpha` at `localhost`, `beta` at `beta.localhost`) under
  their restricted logins and the catalog reader login. Before the tests, the
  launcher passed every probe, SQL privilege check and lifecycle drill:
  - an administrative credential is refused
  - a mis-wired credential is refused
  - an impersonating credential is refused
  - a credential without metadata visibility is refused
  - foreign-database adoption is refused
  - cross-database reads are denied
  - boundary writes and the trigger escalation are denied
  - offboarding and recovery tooling work
- The other E2E files were not re-run against this model.
- All 141 article units remain mapped (`tenant-coverage.mjs`).
- Managed and bring-your-own signup provisioning were exercised end to end against a
  disposable SQL Server. A full Compose image build was not exercised.

Validated locally on 2026-10-20 (single-tenant deployments, superseded):

- Complete backend suite: **875 passed**, zero failures or skips.
- Complete frontend suite: **252 passed** across 37 files; production build passed.
- Backend Release solution build passed.
- E2E: **13 passed** across all four files, including five tenant security tests;
  seven recorded business actions replayed with zero model calls or model tokens.
- Real SQL Server drills verified separate restricted request identities, rejected
  cross-database access and schema-owner escalation, atomic offboarding,
  append-only audit permissions and actual backup/restore ownership checks.
- All **141 article units** are mapped: 75 repository controls, 40 controls with
  additional deployment obligations and 26 explicitly justified technology or
  architecture examples that do not apply. Upstream comparison passed.
- Local provisioning was exercised against disposable SQL Server databases.
  Compose configuration and provisioning script syntax passed; a full Compose
  image build and startup was not exercised.

Existing package-pruning warnings and the frontend initial-bundle size warning
remain. Production infrastructure, key management, retention execution and alert
delivery require the evidence listed above. A catalog row or a test file's
existence is never reported as a passed execution. The PR records final quality,
dependency/secret-scan and CI outcomes separately.
