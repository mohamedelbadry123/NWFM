# Workflow integrations: setup and operations

## Architecture and installation

Use **Service Task** for outgoing API requests, **Notification** for email and **Wait for Event** for asynchronous callbacks. A submission response and a later business event are separate steps.

The existing engine remains authoritative. SQL Server stores integration jobs, receipts, subscriptions, execution tokens and child links. Workers lease jobs, perform network I/O outside database transactions, persist results, then advance under an instance lock. Task commands use that lock too. Event matching/timeout selection is serialized; mappings, consumption and continuation commit together. Persisted Delivered results recover without resending. A crash before result persistence can require redelivery, so external effects are at-least-once.

Deploy backend/frontend together and apply the `WorkflowIntegrations` migration through the normal `WorkflowDbContext` migration process after backing up the target database. Existing published definitions are not rewritten. The migration adds integration tables, execution ownership and submitted task forms.

Connections are tenant-scoped; secrets are protected with ASP.NET Core Data Protection and omitted from XML and read responses. Preserve the protected key ring with database backups. Multiple hosts need a shared persistent protected key store and application discriminator. Ephemeral container keys cannot decrypt saved connections after replacement. Job configuration/input is snapshotted; current connection addresses and credentials are resolved per attempt, so rotation affects queued/replayed jobs.

Auth login and permission policies now protect administrative endpoints. Workflow actor context comes from the signed-in identity; arbitrary participant headers cannot impersonate another user. Webhooks retain their separate source authentication. See [the workspace guide](WORKFLOW_WORKSPACE.md) for nested activities and activity-level required/background events.

## Configure an API call

Open an activity's integration editor and use **Manage connections** to create an HTTP connection with an HTTPS base URL. Choose None, Basic, Bearer, API key (header/query), or OAuth client credentials. The standalone Integrations page is hidden in workspace mode. OAuth token URLs must be HTTPS and obey the destination policy; client credentials are sent as form fields. Blank credential edits preserve existing values; replacement requires the complete required set. Referenced connections cannot be deleted.

In Service Task, select **Call API** and configure method, endpoint, query, headers, content type/body, success codes, timeout, retries and response mappings. URLs must stay on the connection origin; redirects/proxies are disabled. DNS is validated and sockets connect to those validated addresses. A leading `/` resolves from the host root; relative paths follow the base URL path.

Use `{{variableName}}` in text. JSON whole-value substitutions preserve type: `{"amount":"{{amount}}"}` sends a number for numeric `amount`. Embedded JSON strings are escaped; query values are URL-encoded. Text/form bodies are sent as supplied after substitution, so form content must be appropriately encoded.

Response mappings are destination variable -> source path:

```json
{"externalId":"body.id","httpStatus":"status","provider":"headers.x-provider"}
```

Headers use lowercase names. Dot paths, numeric array segments and `$` root are supported. Missing mapping fields fail explicitly. Preview mappings using a sample response before **Send test request**, which performs a real request and can change external data.

Private HTTP destinations require both server `WorkflowSettings:AllowPrivateConnections=true` and the connection setting. They permit plain HTTP for controlled internal testing. Metadata/link-local, wildcard and multicast addresses remain blocked. Other HTTP destinations require HTTPS/public addresses. SMTP uses its explicitly configured host/TLS setting.

Retries cover transport/timeouts, HTTP 408/429 and 5xx. Default attempts is one; delay increases with attempt count. For requests that change external data, the provider must honor the stable idempotency header. Manual replay preserves that operation ID. SMTP cannot guarantee exactly-once sending.

Use explicit arrows such as `OutcomeKey == 'success'`, `OutcomeKey == 'error'` and `OutcomeKey == 'timeout'`. Without an explicit matching error route, the workflow fails with an incident; a default success arrow cannot swallow failure. Conditions support comparisons, AND/OR and parentheses; numeric comparisons require numbers, not quoted strings.

## Configure incoming callbacks

Create a webhook connection with a random API key or HMAC secret of at least 32 characters. In Wait for Event select source, event key, the workflow variable containing the correlation value, timeout and payload output mappings. An earlier API output can supply the correlation variable.

The v1 endpoint is `/api/workflow/integrations/webhooks/{connectionId}`. The connection identifies the source within the configured tenant. Send:

```json
{"eventId":"provider-message-123","eventKey":"order.ready","correlationId":"order-42","payload":{"approved":true}}
```

- API key: `X-Workflow-Key` header.
- HMAC: `X-Workflow-Timestamp` is Unix seconds; `X-Workflow-Signature` is hex HMAC-SHA256 of the UTF-8 bytes of `timestamp + "." + exactRequestBody`, using the secret. The window is five minutes. Re-sign old retries with the same event ID.

Mapping `approved` -> `approved` reads the payload. Correlation uses the envelope field, not arbitrary payload filters. Required mapping paths validate field presence; general JSON Schema validation is not provided. Reuse eventId for redelivery; new business events need new IDs.

Acknowledgment follows durable receipt storage. Early callbacks buffer for seven days. Matching uses connection, key, correlation and deadline. On-time events received while suspended beat timeout when resumed. Unauthorized, malformed, late and ambiguous messages do not advance. Multiple active matching waits produce Ambiguous; replay must select exactly one activity. Use distinct correlation values for concurrent/repeated business operations.

Add an explicit timeout route or allow failure with an incident. Legacy waits without a webhook source support internal `eventKey`/`signalKey` only. ExternalSignal timers cannot be newly published; replace them with Wait for Event.

## Email, tasks and child workflows

SMTP connections specify host, port, sender, TLS and optional username/password. Notification exposes channels, To/Cc/Bcc, participant user IDs, subject/body, HTML and failure policy. Active participant IDs resolve to their email addresses. Addresses accept comma/semicolon separators and variable templates. Template name labels the inline subject/body; it does not load an external template.

The workflow waits for SMTP acceptance, which is not inbox delivery. Continue records failure and advances; Retry uses bounded attempts then fails; FailWorkflow fails immediately unless an explicit error route handles it. In-app delivery creates the existing notification log independently. A crash after SMTP accepts a message but before persistence can cause a duplicate.

Task fields support text, textarea, number, date, email, checkbox and select. Required/type checks run on the server. Inputs use `variables.amount`; outputs use `output.amount`, `output.outcome`, `output.comment`. Instructions render in the selected language. Group-member claiming is required; fallback group key is used if primary resolution fails. SLA uses selected policy or inline duration/escalation. Required attachments and arbitrary JSON Schema forms are blocked; form key is a reference label. Nonzero task queue priority and alternative claim semantics are also blocked rather than ignored.

Task hooks support OnEnter, OnOutcome, OnComplete, OnFailure and bounded retry/timeout policies. Built-in `workflow.setVariables` maps input to output. Custom providers must honor hook idempotency for side effects. Put HTTP work in a separate Service Task.

Set Variables has a typed editor and advanced legacy JSON. New guided settings use `assignmentFormat: "typed"`, preserving string `"150"` separately from number 150. Legacy safe literal/copy semantics remain; arbitrary code is never evaluated. Declare destination variables before assigning them.

Call Activity selects latest or fixed published child version, parent-input/child-output mappings and wait/independent mode. Waiting children resume the exact parent activity; independent children do not. Waiting-child cancellation follows parent cancellation. Cycles are rejected; nesting limit is 16. Select an explicit matching Join for nested forks.

## Monitor and recover

The instance workspace displays events, integration operations and retry controls. The retained administration API lists operations, receipts and waits. Job states are Pending, Running, Delivered, Completed, Failed and Cancelled. Delivered means only workflow advancement remains. Leases last three minutes; workers poll every two seconds with four parallel deliveries per host. Timers use the existing configured interval.

Replay a failed operation after fixing its provider/connection, retaining its operation ID. Legacy integration-activity replay reopens only its failed activity and rejects completed/cancelled/suspended workflows. Required activity events retry while their execution remains active; background activity events can retry after workflow completion without reopening it. Suspended/cancelled ancestors block delivery and progression. Callback replay only consumes an unconsumed event against one active matching wait.

Limits: HTTP body/response and webhook body 256 KiB; HTTP timeout 1–120 seconds; at most 10 attempts; delay 1–3600 seconds multiplied by attempt; event wait 1 second–1 year; unmatched early callback retention seven days. Hooks allow three retries, five-second delay, 30-second total timeout. Database retention, production volume targets and ingress rate limits are deployment responsibilities. Business input/results may contain sensitive data: protect the database and define retention. Credential encryption/echo redaction is not general personal-data redaction.

## Reproduce the acceptance checks

Use a disposable SQL database and a seeded test API on port 5081. Enable private HTTP connections for local mocks only:

```powershell
$env:NWFM_API_URL='http://localhost:5081'
node scripts/smoke-test.mjs
node scripts/workflow-integration-test.mjs
node scripts/workflow-recovery-test.mjs prepare
# Restart ONLY this test API using the SAME database and key ring.
node scripts/workflow-recovery-test.mjs verify
```

Scripts create uniquely named test definitions/connections/instances, loopback HTTP/SMTP listeners, and fake credentials/recipients. Results/state are written under `.work`. No real email/provider is contacted. See the [activity audit](WORKFLOW_ACTIVITY_AUDIT.md) for completed verification and production boundaries.
