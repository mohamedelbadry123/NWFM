# Tenant lifecycle and recovery operations

Each NWFM workspace (tenant) has its own SQL database and its own media storage root. One tenant API serves them all.

- **The catalog owns the workspace id.** The control plane (`NWFM.Platform.Api`) keeps the catalog in `NWFM_Catalog` and binds each database to its catalog id. Neither an operator nor a request can change ownership by editing a request.
- **RLS is not the mechanism.** The PostgreSQL RLS examples in the cheat sheet are not how this application isolates tenants.

## Operator access and initialization

**Only the control plane initializes tenant databases.** It does so with the database owner credential, either on its own start (configuration workspaces) or from the console (_migrate_, _migrate all_). The tenant API refuses every database-initialization flag.

**Initialization steps.** The first migration step (`TenantBoundaryModule`):

1. Records the workspace in `Tenancy.DeploymentBoundary`, with durable `IsActive` state.
2. Creates `Tenancy.LifecycleAudit`.
3. Appends one `Provisioned` event, attributed to `ORIGINAL_LOGIN()`.

The last step grants the workspace's runtime login its request permissions. It includes read access to the boundary, but no right to alter the boundary or the lifecycle audit.

**Never grant the `nwfm_tenant_operator` role** to the application or to any runtime login.

**Suspension.** Workspace status lives in the catalog, and the console's suspend action stops new resolution within the catalog refresh interval. Every request bound to a workspace, and every worker pass, also reads `DeploymentBoundary.IsActive` from that workspace's database without caching. The offboarding command below therefore takes effect immediately, even before the catalog changes. Suspend the workspace in the console as well, so the catalog stops resolving it too.

Platform operators need a separately provisioned SQL identity, explicitly assigned to the database role `nwfm_tenant_operator` **only in the tenant databases they may administer**. The utility checks this role and the actual database tenant binding. It records `ORIGINAL_LOGIN()`, not a caller-supplied identity. Grant the operator only:

- `SELECT` on the boundary, lifecycle audit, and tables being verified; `VIEW DEFINITION` for complete ownership-column discovery.
- `UPDATE` on `Tenancy.DeploymentBoundary`, `Auth.RefreshTokens`, `Auth.SsoAuthorizationCodes`, and `Auth.Users` for offboarding.
- `INSERT` on `Tenancy.LifecycleAudit`; no audit `UPDATE` or `DELETE`.

Enforce complete metadata visibility for recovery verification. Operators authorized only for a tenant must never receive shared backups; restore access belongs to a restricted platform recovery operator. Ship lifecycle audit events to your restricted append-only audit sink. Database administrators can otherwise alter SQL audit rows.

The repository includes the utility at `backend/tools/NWFM.TenantAdmin`. Supply connection strings through the secret-injected environment variables `NWFM_TENANT_ADMIN_CONNECTION` and, for recovery, `NWFM_TENANT_RECOVERY_CONNECTION`. Do not place secrets on command lines.

## Offboarding and retention

```text
dotnet run --project backend/tools/NWFM.TenantAdmin -- offboard <tenant-guid> "Approved termination reference" [--legal-hold]
dotnet run --project backend/tools/NWFM.TenantAdmin -- retention <tenant-guid>
```

Offboarding checks the target tenant and operator role, locks the tenant boundary, suspends the workspace, revokes all refresh/SSO sessions, changes all identity security stamps, disables all accounts, and appends the operator/reason/time to the durable ledger in one transaction. Requests and worker admission read the durable state without caching. In-flight work that was already authorized must be drained before export or deletion.

Approved default deadlines are 30 days for active data, 90 days for backups, and 365 days for security audit records, measured from offboarding. Configure an approved alternative using `NWFM_RETENTION_ACTIVE_DAYS`, `NWFM_RETENTION_BACKUP_DAYS`, and `NWFM_RETENTION_AUDIT_DAYS` in the operator's environment; each must be 1–36500 days, and backup/audit retention must be at least as long as active-data retention. The effective deadlines are recorded at offboarding and are not silently changed by later environment edits. A legal hold reports `OnHold` even after a deadline; it does not restore application access. The utility reports due dates and never automatically deletes a database, storage, or backup.

The infrastructure retention job must consume that schedule and verify deletion across the dedicated database, caches, storage, object versions, replicas, exports, and backups. Keep legally retained records separately restricted. Record completion evidence and any hold or policy exception in the restricted audit system. Set and independently review legal holds before running deletion. Cache entries are tenant/environment namespaced; delete the exact namespace or isolated cache instance and expire local process caches by stopping the deployment.

Export data only when the applicable contract, regulation, or product policy requires it. Run export as a separately authorized tenant operation and apply the same retention, access, and encryption controls to the export.

Use platform-managed encryption with isolated per-workspace database credentials as the default. Provision dedicated tenant keys and isolated cache/worker resources where stronger contractual or compliance isolation is required. The repository does not configure the cloud KMS, network, backup retention, audit sink, or destructive deletion jobs.

## Isolated recovery verification

1. Authorize the platform recovery operator for the target tenant and record the backup reference and reason.
2. Restore the backup to a separate database named `NWFM_RECOVERY_<unique-reference>`; never overwrite the live tenant database. Restrict the recovery network and storage to the operator.
3. Set the recovery database to `READ_ONLY` and `SINGLE_USER`. Provision the operator role before setting read-only mode. Restore the matching storage root separately, including its `.nwfm-tenant` ownership marker.
4. Run:

```text
dotnet run --project backend/tools/NWFM.TenantAdmin -- verify-restore <tenant-guid> <recovery-storage-root> <backup-reference> "Recovery approval reference"
```

The utility authenticates the operator against both target and recovery databases, reads their actual tenant bindings, requires an isolated read-only recovery source, discovers every `OrganizationId`/`TenantId` GUID column, rejects foreign or unowned rows, checks `Tenancy.Tenants` when present, and verifies the actual storage marker. Only the existing deployment-local defaults in `Workflow.BusinessCalendars` and `Workflow.SlaPolicies` may have a null `OrganizationId`. Verification success or denial is appended to the current target's audit ledger after target authorization succeeds.

Verification does not copy data or restore access. Review the data-only copy against the current target's retention, legal holds, memberships, and permissions. Never copy old identity/session tables, tenant lifecycle state, or retention settings from the backup over the current target. Filter expired records and restore only authorized tenant-owned records/files. Validate the result, record outcome, and securely dispose of the recovery environment according to policy.

## Rollout consequences and remaining infrastructure work

Existing JWTs (the tenant claim is now `tid`), refresh credentials (now prefixed with their workspace), and SSO temporary cookies require a new sign-in. API service keys are disabled unless configured with a high-entropy secret's `ApiKeySha256`, explicit `TenantId`, `Environment`, permission `Scopes`, and `IsEnabled=true`; no controller currently uses this optional hook.

Configure production operator roles, restricted recovery networks, audit delivery/alerts, per-store retention execution, key ownership, and backup schedules before claiming those operational controls are deployed. Run the two-tenant disposable SQL tests in CI and repeat the recovery drill against the actual deployment credentials and backup mechanism. No production system is modified by repository tests.
