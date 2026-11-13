# Workflow designer release notes

This change adds scope-entry rows, embedded form editing, explicitly ordered first-match Conditions, and typed REST/JSON event mapping with durable invocations. Existing published routing semantics and pinned form versions remain supported. Changing a draft does not upgrade a running instance.

## Deployment sequence

1. Validate the release in staging with its normal permissions and organization hierarchy. Exercise creation, embedded form publishing, route ordering, related-instance selection, mapped form values, and independent callable Events using local or staging integration endpoints.
2. Take a consistent database backup and verify the recovery procedure before applying the additive Workflow and FormEngine migrations. Apply migrations through the normal deployment process before starting the updated API and workers; deploy the matching generated frontend client.
3. Keep integration consumers on the same release as the API. Verify existing delivery/retry processing resumes and durable waits retain their selected instance, deadline, and frozen request inputs. Do not manually replay an external request to recover output application that was already recorded.
4. Run a staging smoke check with both a legacy published workflow and a new draft using the new features. Confirm English/Arabic layouts, authorized source browsing, read-only mapped field enforcement, and retry/restart behavior. Roll out to production only after the deployment owner accepts the staging result.

No production deployment, commit, push, or merge is performed by the implementation task. Database/transport settings used by automated tests are disposable and must not be copied into production.

### Tenant-security integration

Run database initialization separately with `DatabaseStartup:InitializeOnly=true`. Workflow schema preparation precedes cross-module seeds and respects `ApplyMigrations`; Auth, FormEngine and Tasks then initialize before Workflow demo seeds. Request hosts require the restricted tenant-bound database identity and all initialization flags disabled. Never run the former administrative LocalDB request-host launcher against this release.

Retain migration `20261020164057_TenantOwnershipConcurrency` before routing/mapping/receipt migrations. Mapping invocations (including waits and attention) and routing coordination waits consume `WorkflowSettings:MaxPendingPerQueue` capacity. A full queue leaves recovery retryable; it must not discard frozen payloads or resend successfully recorded delivery. Tenant admission gates designer recovery and is rechecked per selected row. Enable `WorkflowSettings:RoutingV4Enabled` explicitly where routing authoring is intended; local launch-settings changes are not shipped.

For isolated API-client preparation, install root/frontend dependencies and use the supported Node runtime, then run `scripts/e2e/start-app.mjs` with `NWFM_E2E_ISOLATED=1` and `NWFM_E2E_PREPARE_ONLY=1`. This uses fresh labeled SQL containers, privileged initialization followed by restricted request hosts, startup refusal and cross-deployment probes, and the lifecycle drill. It generates clients through `scripts/e2e/generate-compat.mjs` from the combined API and exits after owned cleanup, without starting browser tests or making model calls. Swagger and alias evidence are retained in `.work/api-preparation/<run-id>/`. Do not use a configured application database for this command.

## Receipt migration rollback boundary

Workflow migration `20261111141112_WorkflowFormReceiptOwnership` adds attempt identity, completion receipt pointers, and receipt reservations. Its Down operation deliberately throws SQL error **51041** before destructive changes if attempt or receipt references exist. Even an unclaimed attempt may own a committed FormEngine receipt after a Workflow transaction rollback. Up initializes attempts for eligible unclaimed legacy tasks, so a database can reach this boundary immediately after upgrade.

Do not erase attempt identifiers, receipt pointers, or reservation rows to force a downgrade. Use a forward-compatible corrective deployment, or restore a consistent pre-upgrade backup using the deployment owner's recovery procedure. A later destructive migration needs its own rollback protection. FormEngine migration `20261111142432_WorkflowReceiptKeyReservation` rebuilds reservations from native submission rows when applied.

`formVisitId` identifies the form activation. `formReceiptKey` identifies an actor-owned submission attempt; new receipts must not be reconstructed from the visit ID alone. Legacy receipts remain readable. `formDraftKey` is separate opaque draft-continuity metadata, available on detail responses even before Claim. It is neither a submission receipt nor an authorization token. Clients must discard a cached draft when this key changes or continuity cannot be established.

## Draft preservation and operational limits

Unsaved task answers are scoped to the verified user, tenant/effective participant, work item, form version, visit, mapping activation, and ownership attempt. Same-tab reload and same-attempt Claim preserve them. Logout clears them; ownership changes invalidate them. Read-only mapped values come from the server.

Draft persistence uses browser session storage. Closing the tab, unavailable storage, or failure to verify the signed-in profile can leave edits only in memory. Startup attempts to repair older sessions with a missing verified user ID; it is bounded by a five-second timeout. A failed profile verification can leave an otherwise usable session unverified, so reload preservation is unavailable until verification succeeds. A concurrent request that receives 401 while another refresh is active fails without signing out the session; it is not automatically queued.

Local validation includes complete frontend/backend unit suites, real disposable SQL recovery tests, and deterministic browser journeys. Authenticated model-backed lifecycle tests require the repository's separate user-confirmed ChatGPT sign-in and are not implied by deterministic browser results. Local checks do not replace staging validation with deployment-specific permissions, connections, and data volume.
