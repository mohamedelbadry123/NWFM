# Tenant security implementation and operating requirements

Source: [OWASP Multi-Tenant Application Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multi_Tenant_Security_Cheat_Sheet.html), retrieved 2026-10-20.
The unmodified article and its CC-BY-SA-4.0 license are in `vendor/`. The adapted
requirement mapping in `tenant-controls.json` is also attributed to the OWASP Cheat
Sheet Series Team and provided under CC-BY-SA-4.0.

## Supported boundary

NWFM serves **one configured tenant per deployment, database, worker fleet and
storage root**. Auth, FormEngine and Tasks have deployment-owned tables without
row tenant columns. Workflow adds row ownership filters and write checks as
defense in depth. Sharing one database between tenant deployments is unsupported.
The startup guard rejects reassignment of a bound database or a database containing
another tenant's workflow records. It also rejects an administrative request login.

PostgreSQL RLS, PostgreSQL tenant session variables, SQLAlchemy, S3 buckets and
presigned URLs in the article are illustrative technologies, not NWFM features.
Their underlying authorization and isolation requirements are mapped to the actual
implementation. A future shared-database hosting model requires a separate design,
schema migration and negative-path tests for every table; the present work does
not advertise that model as safe.

## Implemented controls

| Area                               | Implementation                                                                                                                                                                                                                                                     | Evidence                                                                                                       |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| Identity and current authorization | Tenant/session/security-stamp claims; current account, team, membership and permissions; atomic refresh consumption; immediate logout/account revocation; selector rejection and request-context cleanup; invalid scope edits rejected before account/role changes | Session, request-context, endpoint inventory and scope mutation tests; two-tenant E2E                          |
| Database                           | Immutable deployment marker; separate initialization process; limited request role; database pool bound; row ownership immutable on tracked/detached writes; classified EF model inventory                                                                         | `TenantWriteBoundaryTests`, `TenantModelInventoryTests`, actual SQL startup/role/cross-database probes         |
| Cache and sessions                 | Environment/tenant namespace for every cache operation; current grants validated independently of cache; isolated SSO cookie protection and tenant-scoped workflow credential protection; no-store API responses                                                   | `TenantCacheIsolationTests`, session and E2E revocation tests                                                  |
| Service credentials                | Explicit tenant/environment/scope and hashed random API keys; canonical versioned webhook signing; production rejection of missing or checked-in JWT keys                                                                                                          | Service credential and JWT configuration tests, webhook tests and E2E                                          |
| Resource use                       | Tenant, principal/IP and auth rate limits; request concurrency, bounded SQL pool; transactional pending queue limits; dedicated per-tenant worker processing                                                                                                       | `TenantResourceLimitTests`, `WorkflowQueueCapacityTests`, E2E concurrent overload and unaffected second tenant |
| Async work                         | Active-tenant admission; scoped queue records and idempotency; authenticated ingress; bounded processing and retries                                                                                                                                               | `WorkflowWorkerTenantTests`, existing integration suites, E2E                                                  |
| Files                              | Durable root ownership marker; strict relative paths; symlink/reparse rejection; bounded streaming; no silent overwrites; exact database/API authorization                                                                                                         | `TenantFileStorageTests`, `SubmissionMediaIsolationTests`, cross-deployment media E2E                          |
| Lifecycle                          | Separate provisioning, active-state admission, operator tooling and retention/restore verification                                                                                                                                                                 | See operator tooling and deployment evidence below                                                             |
| Audit                              | Verified tenant and trace context; rejected-access records; sensitive OTP/message content removed; tenant-owned workflow audit reads                                                                                                                               | Request-context/service-credential tests and E2E; external collector evidence remains required                 |

## Every PR

The existing **Security** check now validates every article prose block, list
point, strategy row and complete code example against the pinned catalog, checks
the current upstream article for drift, and executes the tenant security
regressions. Source or evidence omissions fail. Upstream changes require a fresh
review and updated mapping; unavailable upstream verification fails closed.

The complete Backend and Frontend suites and builds still run. The existing E2E
check includes deterministic security tests against two independently provisioned
SQL Server databases and API deployments, using the actual restricted request
roles and local integration transports. Test users, keys and database passwords
are disposable. No model calls or OAuth credentials are needed for security tests.
All existing workflow E2E recordings remain subject to strict replay.

CI publishes a coverage inventory and test results. Inventory completeness is
**traceability**, not a semantic proof that arbitrary future code is secure.
Behavioral tests, discovered model/controller/background-worker inventories and
owner review supply additional checks. PR review must examine changed authorization, persistence, cache, storage,
worker and operational paths against the catalog; newly introduced paths need
positive same-tenant and negative cross-tenant tests.

No GitHub protection or review policy is weakened. The owner retains the final
merge; independent approval count remains zero. No additional approval layer is
introduced.

## Provisioning and rollout

1. Allocate a dedicated database, request login, storage volume and worker/service
   identity for the tenant. Select an opaque random `Application:TenantId`.
   Apply network rules so tenant services cannot connect to other tenant stores.
2. Run initialization with the administrative connection, explicit
   `DatabaseStartup:InitializeOnly=true`, and the needed
   `ApplyMigrations`, `ApplySqlObjects`, and `SeedData` flags. Initialization binds
   the database and exits without listening for requests. Production must keep
   development/demo seeding disabled.
3. Grant the request identity only necessary CRUD/EXECUTE on Auth, FormEngine,
   Task and Workflow schemas, plus SELECT on `Tenancy.DeploymentBoundary` and
   VIEW DEFINITION (not data modification) on `Tenancy.LifecycleAudit`.
   Form publishing currently requires CREATE TABLE at database scope and ALTER
   on FormEngine for dynamic submission tables. This is a deliberate elevated
   application capability inside the isolated database, not a shared-table RLS
   boundary. Bootstrap assigns FormEngine to a dedicated non-login schema owner
   to prevent a form trigger's ownership chain from modifying protected schemas;
   runtime verifies this separation. Grant VIEW DEFINITION on that owner so the
   request identity can inspect its metadata. Restrict publishing to its existing permission. Do not grant
   CONTROL, db_owner, db_securityadmin, db_ddladmin or ownership-marker writes.
   Preserve `VIEW ANY DATABASE` metadata visibility (normally inherited from
   `public`) so startup can verify the complete database access inventory; it does
   not grant access to database contents. Specific login/user impersonation grants
   are rejected as well as administrative roles.
4. Run the request host with the restricted connection and every startup mutation
   flag false. The guard checks actual SQL permissions, not just connection text.
   Runtime credentials must not be able to read other tenant databases or
   change their own binding. Preserve encrypted transport and trusted server
   certificates outside disposable local tests.
5. Give each storage root an OS identity/ACL accessible only by its tenant
   service. The `.nwfm-tenant` marker prevents accidental root reuse, but is not
   an OS authorization boundary. For an existing unmarked volume, independently
   verify its ownership before first startup binds it. Never delete a marker to
   repurpose a volume with existing data.
6. Use separate production signing keys, service keys and Data Protection key
   custody per tenant/environment. Redis namespaces prevent key collisions;
   separate Redis credentials/instances are still needed when infrastructure-level
   isolation is required. Raw access to a shared Redis service is not restricted
   by an application key prefix.
7. Roll out with forced sign-in: old access/refresh tokens and temporary SSO cookies
   intentionally fail after tenant/session binding changes. Existing encrypted
   Workflow connection credentials retain their original protection purpose;
   preserve their Data Protection key ring during rollout. Update
   webhook senders to the versioned canonical signature. Dormant API-key endpoints
   require explicit scope and hashed-key configuration before they can be enabled.

The old development startup pattern that serves requests using a migration
administrator is intentionally rejected. The disposable E2E launcher is a complete
worked example of two-stage startup and minimal SQL grants. Adapt that pattern to
your deployment's secret manager; never deploy its development seed accounts.

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
use. Current deployment isolation uses separate backups.

The implemented [operator commands](../tenant-lifecycle-operations.md) perform
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
  deployment and global host/cluster safety limits. Application limits are per
  process; distributed replicas need an aggregate gateway/quota budget.
- Connect structured security events to a protected collector. Alert on repeated
  tenant mismatch/authorization denials, failed binding/role checks and abnormal
  queue or resource exhaustion; test notification delivery with synthetic events.
  The API emits JSON event `4201/TenantAccessDenied` for each denied response.
  Event `4202/TenantIsolationAlert` triggers at 20 denials within 60 seconds,
  at most once per minute per deployment. Both include the server tenant ID. Configure the
  collector to notify on event 4202 and failed startup boundary validation.
- Keep operator/lifecycle audit records append-only with separate retention access.
  Neither ordinary tenant users nor request database identities should edit them.
- Execute the configured retention jobs across active stores, cache, object
  versions, replicas, exports and backups. Record legal holds and expiry evidence.
- Run and retain a restore drill, verify authorized tenant data only, deny an
  unauthorized operator and apply current memberships after recovery.
- Verify encryption, key isolation, key rotation and recoverability against the
  selected managed services.

## Validation results

Validated locally on 2026-10-20:

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
