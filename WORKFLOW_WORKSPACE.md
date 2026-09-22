# Workflow workspace: architecture, deployment and walkthrough

**Current visual workspace revision:** see [WORKFLOW_VISUAL_EDITOR.md](WORKFLOW_VISUAL_EDITOR.md) for the five-page navigation, fixed Accept/Reject actions, automatic SLA rules, visual event nodes, migration and current test evidence. The remainder records the original workspace foundation.

The workspace runs persisted workflows through the existing engine. Its four pages are **Workflows**, **New Instance**, **Instances** and **Lookups**. Auth login/logout, user administration and role permissions remain available according to permission grants. The complete designer palette remains available, including the new **Main Activity**.

## Architecture decisions

- **Auth owns reference data.** Workflow accesses `IWorkflowReferenceData`; the Auth adapter translates Cluster → CBU → Branch into Cluster → Region → City. Departments remain in Auth. Field Activity Types belong to one department; Isolation is included in the Water Network demo. Both publication and instance creation check active geographic relationships. Publication checks department/FA compatibility.
- **A main activity wraps a child and final approval.** Its persisted phases are entry events → child execution → final approval → required outcome/completion events → selected route. Ordinary User Tasks are leaves. Main activities may be nested. A failed/cancelled child cannot expose parent approval. A mapping failure also leaves approval blocked.
- **Publication pins the child graph.** Main and Call Activity child versions are pinned for workspace definitions. Recursive definition dependencies and nesting beyond the engine's 16-level limit are rejected. Retiring a child version does not change an already published parent's pinned child. Existing definitions without workspace metadata retain their execution behavior.
- **Geography is immutable execution context.** A main version stores the geographic chain. Starting an instance snapshots it into `GeographyJson`; descendants inherit that snapshot. Editable variables and child mappings cannot replace it. Event rendering uses the snapshot for ClusterCode, RegionCode and CityCode.
- **The existing runtime owns concurrency.** SQL application locks serialize actions across the entire root/descendant tree. Child links, task IDs, phase, due date and action receipts persist. Repeated submissions reuse their request ID; conflicting reuse is rejected. Rejection requires a comment and enters the configured activity as a new execution, including a fresh child. A first activity can explicitly re-enter itself.
- **Assignment is an interface.** `IWorkflowGroupDirectory` uses the local participant/group projection in this phase. Demo users are distinct, marked participants. A future group-owning module can replace the adapter. For normal accounts, project the Auth user ID into Workflow participant `userId` and maintain its actual group memberships; an Auth permission alone does not make someone a task assignee.
- **SLA starts when the main activity begins.** Child execution and required final processing count toward the main deadline. Child tasks have their own deadlines. Breaches create history and configured escalation events. Suspension blocks descendant actions, timers, callbacks and advancement; cancellation propagates to descendants.

## Activity events and integrations

Activity settings expose entry, approval, rejection, comment, completion, failure and SLA breach events. Each occurrence has its own durable operation key. Separate comments create separate jobs; retries preserve the original operation key.

REST and SOAP default to required delivery. Email and SMS default to background delivery. Required delivery failures keep the activity in its waiting phase; response mappings apply before routing. Background results are diagnostic data and cannot rewrite workflow variables or reopen a completed route. Accepted background jobs may finish or be retried after completion. Failure notifications from an already finished/failed activity run in the background because that activity has no remaining transition to block.

Jobs are recorded with the triggering workflow action under its transaction. Network I/O runs through the existing durable worker outside that transaction. Persisted results resume without another delivery. A failure-recovery scan also picks up failed activities needing failure notifications. Request summaries and bounded, credential-redacted responses are retained. Transport delivery is **at least once** after uncertain network/process failures: providers must honor the operation key for external deduplication; SMTP acceptance can be duplicated after a crash.

Configure connections inside an activity's integration editor using **Manage connections**, then refresh the connection list. Credentials remain in protected connection storage, outside the workflow XML. REST supports the existing None, Basic, Bearer, API key and OAuth client-credentials authentication, headers/query/body, expected status codes, timeouts, retries and mappings.

- **SOAP:** manual SOAP 1.1/1.2 envelopes, action, namespace prefixes, XPath response mappings and SOAP Fault detection. XML entities are disabled and substituted values are XML-escaped. WSDL import and WS-Security are outside this phase.
- **SMS:** an HTTP provider connection plus recipient/message templates. Use `{{smsTo}}` and `{{smsMessage}}` in the provider's request body and map its response like any REST result.
- **Email:** SMTP connection, recipients, subject/body, HTML, timeouts/retries and protected credentials. Delivery success means SMTP acceptance.
- **Wait for Event:** a separate incoming, authenticated callback activity. Configure source connection, event key, correlation variable, timeout and response mappings. API key/HMAC authentication and receipt deduplication remain intact. It does not send outgoing API calls.

Connection security, request size limits, key-ring storage and provider configuration are described in [WORKFLOW_INTEGRATIONS.md](WORKFLOW_INTEGRATIONS.md).

## Public interfaces

All paths below start with `/api/workflow/workspace`. Tenant resolution is server-side. Normal actions use the JWT identity; participant headers cannot impersonate another user.

| Method and path | Purpose / permission |
| --- | --- |
| `GET lookups/{kind}?parentCode=...` | Active clusters, regions, cities, departments, field-activity-types; ViewWorkflows. |
| `GET groups`, `GET children` | Assigned groups / published child choices; ManageDefinitions. |
| `POST definitions` | Name, optional Arabic name, Main/Child settings and main geography; ManageDefinitions. Returns definition/version IDs for navigation. |
| `GET catalog` | Published main workflows; StartWorkflows. |
| `POST definitions/{id}/instances` | Request ID, optional reference and administrator-only demo flag; StartWorkflows. Resolves default tenant and internal binding. |
| `GET instances?search=...` | Latest 200 roots filtered by workflow name/reference; ViewInstances. |
| `GET instances/{id}?demoActorId=...` | Tree, tasks, permitted actions, deadlines, comments/history and delivery results; ViewInstances. Demo actor only on demo instances for administrators. |
| `POST tasks/{id}/actions` | Configured outcome, comment, form values and stable request ID; ClaimTasks plus actual effective-user membership. |
| `POST tasks/{id}/comments` | Standalone comment; never completes a task. Same authorization. |
| `POST demo/tasks/{id}/actions` | Explicit demo action route; Administrator and ClaimTasks. Requires a marked demo instance and demo participant. The common action contract also validates an optional demo actor identically. |

Existing definition/version/XML APIs extend their contracts with `workspaceJson`; business activity configuration contains department/FA, child, SLA, rejection target and event configuration. Child pins are stored with the published version. The existing integration replay and authenticated webhook endpoints are retained.

## Migrations and deployment

Auth was merged into `workflow-improvement` first (`df6c26e`, Auth tip `af8bcb1`). The merged baseline passed 300 backend and 57 frontend tests before workspace changes.

Deploy backend and frontend together. Apply the Auth branch migrations plus these additive migrations:

- Auth: `20260920044352_FieldActivityTypes` adds the FA table, department relationship and unique department/code constraint.
- Workflow: `20260920044329_WorkflowWorkspace` adds nullable version/instance/activity metadata, demo flags defaulting to false, event-job metadata and unique tenant/request action receipts.

No existing published XML or running instance is rewritten. Existing records without workspace metadata remain usable by the backend. Use a database backup and retain the ASP.NET Data Protection key ring. Do not roll down these migrations after creating workspace instances: their state is required for recovery.

From the repository root, using the deployment connection string in the environment:

```powershell
dotnet build backend/NWFM.sln -c Release
dotnet ef database update --project backend/src/Modules/Auth/Auth.Infrastructure --startup-project backend/src/NWFM.Api --context AuthDbContext --configuration Release --no-build
dotnet ef database update --project backend/src/Modules/Workflow/Workflow.Infrastructure --startup-project backend/src/NWFM.Api --context WorkflowDbContext --configuration Release --no-build
```

For reviewed SQL, replace `database update` with `migrations script --idempotent --output <file.sql>`. Both contexts were checked for pending model changes, applied to a disposable SQL database, and their complete idempotent scripts were reapplied successfully.

Keep `WorkflowDemo:Enabled=false` and `DatabaseStartup:SeedData=false` for business deployments. Auth's existing seed routine is separate from the additive workflow demo seed and is intended for initial development setup. Configure Auth signing/SSO secrets and live HTTP/SMTP/SMS credentials through deployment configuration. Workflow startup continues its existing automatic migration behavior; `DatabaseStartup:ApplyMigrations` controls Auth migration startup.

The reversible frontend setting is `workflowWorkspace` in `frontend/src/environments/environment.ts` and `environment.prod.ts`. Setting it to false and rebuilding restores the hidden navigation and direct UI routes. Backend capabilities remain present in either mode.

## Local demo setup

Use a disposable SQL database. The local transports capture messages and never contact a real provider:

```powershell
node scripts/workflow-demo-transports.mjs
```

In a second terminal, from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__DefaultConnection='Server=localhost;Database=NWFM_WorkspaceDemo;Integrated Security=True;TrustServerCertificate=True'
$env:WorkflowDemo__Enabled='true'
$env:WorkflowDemo__CallbackKey='nwfm-local-demo-callback-key-testing-only'
$env:WorkflowSettings__AllowPrivateConnections='true'
dotnet run --project backend/src/NWFM.Api --no-launch-profile --urls http://localhost:5080
```

Run `npm ci` and `npm start` in `frontend`, then open `http://localhost:4200`. The existing Auth development initializer supplies `administrator@localhost` / `Administrator1!` on a fresh development database. These are local seed credentials, not deployment credentials. If Auth seeding is disabled, an existing administrator and an active geographic chain must already exist.

Seeding is repeatable and does not update existing business definitions/lookups. Reserved demo IDs are checked before reuse. It creates the three demo groups/users, FA Types, published children, main workflows, local connections and seven sample instances. Restarting does not reset progressed samples. An existing partially seeded definition without a published version stops seeding for inspection rather than overwriting it; use a new disposable database for a clean demo.

HTTP transports listen on `127.0.0.1:5091`, SMTP on `127.0.0.1:2525`. The failure route returns 503 until `POST http://127.0.0.1:5091/recover`; restart the transport to reset it. No live provider credentials are needed.

## Walkthrough

1. In **Lookups**, inspect Cluster → Region → City and Water Network → Isolation. Choose a different parent and confirm the dependent choices reset.
2. In **Workflows**, create a Child definition. Use the existing canvas to add a User Task, select its department/FA, group, SLA and outcomes, and connect Start → Task → End. Reject must require a comment and specify its rework destination. Save, validate and publish.
3. Create a Main definition with geography. Add a Main Activity and select the published child. Set its department/FA, assigned group, SLA, outcomes and rework target. Add activity events through the embedded integration editor. Save, validate and publish. No manual binding or tenant selection is needed.
4. In **New Instance**, select **Demo — Water isolation** and enable the demo checkbox. Start it with a reference. The first main activity displays **Waiting for child workflow** and has no parent approval task.
5. In **Instances**, choose **Demo reviewer**. Add a comment to the child checklist: history changes but progress does not. Approve the checklist; the main Request Review approval appears after the worker resumes it.
6. Reject the main activity with a comment. A new Request Review child execution appears while the previous attempt remains in history. Complete that child and approve the main activity to enter Isolation Execution. Switch to **Demo isolation engineer**, then **Demo closure officer** for their assignments.
7. Complete Closure. REST and SOAP are required; SMS and email finish in the background. Inspect Events and integrations for attempts, responses and failures.
8. Open the seeded **Overdue SLA**, **Integration failure**, **Rework**, **Completion**, **Pending main approval**, **Child execution** and **Callback waiting** examples. Recover the failure transport and use Retry delivery. For the callback example, get the callback URL/configuration from its Wait for Event activity and send `demo.field.completed`, correlation `Demo — Callback waiting`, a unique event ID and the configured `X-Workflow-Key`.
9. Start an ordinary instance. It has no demo-user selector. Its task actions require the signed-in user's real group membership.

## Verification evidence

Completed on 2026-09-20 using disposable SQL Server database `NWFM_WorkspaceAcceptanceTest` and local transports:

- Release solution build; **319 backend tests**, all passing.
- Angular production build; **60 headless Chrome tests**, all passing. The build retains the existing initial-bundle budget warning (748.48 kB versus the 500 kB warning budget). Two redundant package-reference warnings remain in the .NET build.
- **66 authenticated workspace API checks**: login failure, tenant boundaries, invalid geography/department/FA/SLA publication, save/reload/publish, idempotent starts/comments/approvals, group membership, normal-instance impersonation denial, rejection/rework, suspension/cancellation, required delivery/retry, background completion, SLA breach and authenticated callback deduplication.
- Existing authenticated smoke and integration scripts: all 13 original activities, typed task forms, real local HTTP retry with stable operation ID, SMTP, callback buffering, nested parallel/inclusive joins, timers, immediate child mapping and concurrent claims.
- Actual API process restart: persisted child/task identity and geography survive; approval advances once. Separate restart test verifies suspended on-time callbacks, timer recovery, one parallel join, receipt deduplication and descendant cancellation.
- Browser checks: four-menu navigation, create workflow, full designer palette, cascading department/FA, event editor, new persisted demo instance, expandable child execution, permitted demo-user actions and standalone comment history without advancement.
- Both database contexts report no pending model changes; both idempotent SQL migration scripts reapply successfully. The normal development database was not changed by verification.

Reproduce from the repository root after starting the demo API and transports:

```powershell
$env:NWFM_API_URL='http://localhost:5080'
node scripts/smoke-test.mjs
node scripts/workflow-integration-test.mjs
node scripts/workflow-workspace-test.mjs
node scripts/workflow-recovery-test.mjs prepare
node scripts/workflow-workspace-recovery.mjs prepare
# Restart only the test API, preserving its database and Data Protection keys.
node scripts/workflow-recovery-test.mjs verify
node scripts/workflow-workspace-recovery.mjs verify
```

The tests create their own records and use `NWFM_TEST_USER` / `NWFM_TEST_PASSWORD` when supplied. Set `NWFM_DEMO_CALLBACK_KEY` if the local callback key differs from the example. API-check counts include polling requests and may vary slightly with worker timing. The restart scripts intentionally have separate prepare/verify phases. No live delivery, public deployment or production capacity certification was performed.
