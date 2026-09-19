# Workflow Engine Enhancement

Date: 2026-09-19

Branch: `workflow-improvement`

Status: Implementation started. The first draft PR delivers runtime/configuration corrections and an initial capability audit; integration connectors and durable event delivery remain open. See [implementation audit](WORKFLOW_ACTIVITY_AUDIT.md) for evidence and remaining work.

## Objective and scope

Make every Workflow Designer activity usable from its configuration panel through saving, publishing, execution, monitoring, and recovery. A visible activity must either work end to end or clearly explain its missing prerequisites before publication.

Support real workflows that assign people tasks, make decisions, execute parallel work, call APIs and services, send email, wait for external responses, handle timeouts, and run child workflows.

Preserve the existing four categories, English/Arabic support, published workflow versions, tenant scoping, and participant-based task attribution. Application login and access control remain a separate project under the existing `PLAN.md`; outbound service authentication and inbound webhook authentication belong to this integration work. Internet-facing production use will also require the separate application access-control work or an appropriately controlled hosting boundary.

## Initial findings

These are source-level findings, not results of an execution audit.

| Area | Evidence and implication |
| --- | --- |
| Palette | All 13 activities exist in the designer and runtime switch. Their presence alone does not establish complete configuration or reliable execution. |
| Service Task | The panel exposes an action key and raw JSON. No concrete `IWorkflowActionProvider` implementation was found in application source. The execution context does not carry the node configuration. A configurable HTTP connector needs both a real provider and an updated execution contract. |
| Wait for Event | The designer writes `eventKey` and `correlationVariable`; signal resumption reads `signalKey`. Correlation-variable matching and event-payload mapping are absent from that resumption path. Missing signal keys can bypass matching. |
| External event entry | An internal trigger service can resume an existing instance, but the inspected controllers do not expose a dedicated external event/webhook contract. Resumption relies on one current node, which requires review for concurrent waits in parallel branches. |
| Event recovery | The inbox recovery worker calls `StartAsync`; it does not distinguish recovery of a workflow-start message from recovery of a signal message. Replay must preserve the intended operation. |
| Timer | The panel offers `ExternalSignal`, but due-timer selection does not filter by timer type. External-signal timers can enter time-based processing. Due-date serialization also appends `Z` to a local-looking value and needs timezone verification. |
| Service retry | A retryable service failure returns success to its caller, which then selects the next transition. This creates a control-flow risk: waiting for retry and successful completion need distinct execution results. |
| Notification/email | The panel saves template and failure policy only, although runtime configuration supports channels and recipients. The publisher sends diagnostic email to the SMTP FromAddress instead of resolving recipient participants, and creates a generic subject/body. SMTP authentication is not configured in the sender. |
| Notification reliability | The runtime explicitly treats Retry like Continue rather than scheduling a retry. The publisher can retain email as queued without raising an exception, so policy behavior must cover delivery failures as well as exceptions. Channel-specific delivery status also needs attention. |
| Validation and tests | Shared graph validation and several runtime path tests exist. Dedicated execution coverage for every activity, configuration field, callback, and failure/recovery case must be established. Existing test counts in README are historical, not a current baseline. |

Primary source locations:

- `frontend/src/app/features/workflow/designer/workflow-designer.component.ts` and `.html`
- `backend/src/Modules/Workflow/Workflow.Application/Helpers/WorkflowGraphValidator.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowRuntimeEngine.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowTriggerService.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/AuditingWorkflowNotificationPublisher.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowSmtpEmailSender.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Background/WorkflowInboxHostedService.cs`
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Persistence/Repositories/WorkflowTimerRepository.cs`
- `backend/src/NWFM.Shared/Integration/Workflow/WorkflowActionExecutionContext.cs`

## Activity acceptance matrix

For every row, verify: add by click/drag, configure, validate, save/reopen, XML import/export, clone, publish, execute, inspect history, and recover where applicable. Include English/Arabic and published-version read-only behavior. Track evidence and final status per activity as Verified, Needs configuration, or Blocked by defect.

| Category | Activity | Required behavior and key checks |
| --- | --- | --- |
| Flow | Start | Exactly one valid entry; initial variables available; duplicate start requests do not create duplicate instances. |
| Flow | End | Correct result and completion history; multiple ends have defined semantics; one completed branch cannot prematurely finish other active work. |
| Flow | Decision | Exactly one eligible route, deterministic priority, default fallback, typed conditions, clear invalid-expression feedback. |
| Flow | Inclusive Gateway | Execute all matching branches, use fallback when none match, join only the branches actually activated. |
| Flow | Parallel Fork | Start each branch once; support human tasks, timers, callbacks, and service calls that finish at different times. |
| Flow | Join | Resume once after required branches finish; verify nested forks, repeated traversal, duplicate completion, cancellation, and restart. |
| Tasks | User Task | Assignment and fallback, claims, delegation, outcomes, required form fields, input/output mapping, action hooks, SLA and escalation all affect runtime as configured. |
| Automation | Service Task | Select a registered action or HTTP connector; persist configuration; send mapped input; consume response; follow configured success, error, timeout, and retry behavior. |
| Automation | Call Activity | Select an available child definition/version policy; map inputs/outputs; support wait and independent execution; define child failure, cancellation, recursion limits, and immediate child completion. |
| Automation | Set Variables | Guided typed assignments and supported expressions; validate names/types and missing values; no arbitrary script execution. |
| Automation | Notification | Select channel and recipients; preview real template data; send to intended recipients; distinguish queued, sent, failed, and retried per channel. |
| Events | Timer | Valid duration or explicit timezone-aware date; fire once; survive restart; honor cancellation/suspension and overdue behavior. |
| Events | Wait for Event | Match event and business correlation to the intended active wait; validate/map payload; consume once; support timeout, restart, and concurrent waits. |

## Configuration experience

### API calls and service integrations

Keep outgoing API calls under Automation > Service Task, with a clear “Call API” option. An outgoing request can complete immediately or return an external reference followed by a separate Wait for Event step.

Provide a guided form with:

- **Connection:** reusable connection name, environment/base URL, request method, endpoint, path/query parameters, and headers.
- **Authentication:** None, API key in header/query, Basic, Bearer token, or OAuth 2.0 client credentials. Select a protected credential reference; keep secrets out of workflow XML, browser responses, exports, and execution logs. Other OAuth flows and vendor-specific connectors are later extensions.
- **Request:** content type, JSON/text/form body, variable picker, required inputs, and a rendered request preview with sensitive fields hidden.
- **Response:** expected success codes, response schema/sample, status/header/body mapping to typed workflow variables, and missing-field behavior.
- **Execution:** timeout, bounded retry count and delay, retryable outcomes, stable operation identifier across retries, and success/error/timeout routes. Avoid blind retries of calls that may already have produced an external side effect.
- **Testing:** local validation and mock response first; an explicit Test request action against the selected test connection, showing status, duration, response, and mapping results. Clearly identify that this action actually contacts the service.

Run requests on the backend. Configure allowed destinations, restrict protocols and redirects, validate resolved addresses, and bound request/response sizes. Allow required internal enterprise destinations through explicit administrator configuration. Persist connection references and schema versions; document how credential rotation and connection changes affect already published workflows.

### Incoming events and callbacks

Keep Events > Wait for Event focused on pausing and resuming a workflow. Provide:

- Event name and source/connection selection.
- A generated callback endpoint and copyable example request, with authentication instructions.
- Correlation configuration: select a workflow variable (for example `externalRequestId`) and the incoming payload field that must match it.
- Expected payload structure, required fields, matching filters, and payload-to-variable mappings.
- Timeout duration and explicit event-received/timeout/error paths.
- A sample-payload validator and controlled test event action.

Define one versioned event envelope containing event ID, event name, source, correlation value, timestamp, and payload. Derive tenant/source scope from the authenticated connection rather than trusting a tenant identifier in the body. Use signed webhook or service credentials with replay protection.

Persist receipt before acknowledging it, then match and process durably. Event identity and activity-execution identity must prevent duplicate advancement. Register waits per activity/token rather than relying only on the instance's current node. Apply payload changes, consume the wait, and schedule the next step atomically.

Explicitly handle:

- Duplicate, mismatched, malformed, unauthorized, stale, and out-of-order events.
- A callback arriving before the outgoing request has completed or before the wait is active: buffer it for a bounded retention period and match later.
- An event racing with a timeout: one winner, with the losing delivery recorded.
- Multiple waits with the same business key: deterministic single-consumer matching by default; report ambiguity instead of silently resuming several activities.
- Suspended/cancelled/completed workflows, worker restarts, replay, and dead-letter recovery.

Prefer Wait for Event for external signals and Timer for time delays. Migrate or compatibly support the existing ExternalSignal timer setting; do not silently reinterpret published definitions. Support legacy `signalKey` and current `eventKey` through a documented configuration normalization/versioning rule.

### Email and notifications

Extend Notification with channel selection, reusable mail connection, participant/group/variable-based recipients, To/Cc/Bcc, subject, template/body, variable preview, and failure behavior. Include real participant email resolution and SMTP credentials or an email-provider connection. Attachments and delivery/open tracking are subsequent extensions unless required by a selected workflow.

Persist a delivery job before sending. Record attempts and outcomes per recipient/channel, provide bounded retries and recovery, and remove the diagnostic send-to-self path. Distinguish provider acceptance from delivery to a recipient's inbox. Define whether the workflow advances after durable queueing or waits for sending; both the UI and failure policy must reflect the selected behavior.

## Implementation order and completion gates

### Phase 1 — Establish a reproducible audit baseline

Inventory all 13 activities and every associated inspector tab/field. Trace UI -> serialization -> compiler -> validation -> persistence -> runtime -> history/recovery. Run current backend/frontend tests and production build; record failures before changes. Create small reproducible workflow fixtures for every row in the matrix, plus mixed workflows with asynchronous branches.

**Exit:** a completed evidence matrix and prioritized defects; no activity declared verified solely because it can be placed on the canvas.

### Phase 2 — Correct execution and configuration contracts

Introduce explicit execution outcomes (completed, waiting, retry scheduled, failed), beginning with the service retry fall-through risk. Fix event-key mismatch, distinguish start/signal inbox operations, and separate time-based timers from signals. Verify token-aware resumption, join completion, child-workflow lifecycle, and action-hook failure handling.

Define typed, versioned activity configurations and shared server-side validators. Validate registered actions, referenced definitions, mappings, required settings, and transition topology before publication. Report errors against the relevant node/field. Preserve unknown supported fields during UI round trips; keep old published versions executable and give legacy drafts clear migration diagnostics.

**Exit:** regression tests reproduce and resolve identified defects; invalid or unsupported configurations cannot appear ready to publish; existing workflows retain their documented behavior.

### Phase 3 — Add integration foundations and API forms

Implement scoped connection/credential storage, action catalog configuration metadata, HTTP action execution, typed request/response mapping, durable execution jobs, retries, stable idempotency, and execution records. Extend the action context to include validated node settings and activity-execution identity. Update API contracts and regenerate the Angular client. Build the guided Service Task form and mock/test experience.

**Exit:** an API workflow configured entirely through the designer sends a correct test request, maps its response, handles failure/timeout, and recovers after restart without silently skipping work.

### Phase 4 — Complete event waiting

Implement authenticated event ingress, durable receipts/subscriptions, correlation and payload mapping, timeouts, event buffering, and operation-aware replay. Add database uniqueness/concurrency controls and worker recovery. Build the Wait for Event form, callback example, and event inspection controls.

**Exit:** an API request followed by a callback resumes the correct activity once, including duplicate delivery, early callback, concurrent wait, timeout race, and restart cases.

### Phase 5 — Complete notifications and remaining panels

Implement recipient resolution, templates, mail authentication, delivery jobs and meaningful retry policies. Finish guided variable assignment, action selection, child-workflow selection/mapping, timer timezone controls, and any remaining User Task inspector gaps found during Phase 1. Preserve advanced configuration only where needed.

**Exit:** every matrix row has executable evidence; normal API/email/event workflows can be configured without manually editing JSON or XML.

### Phase 6 — Prove real-world workflows and operational readiness

Exercise complete workflows through the browser, API, and durable workers using a local mock service and mail capture. Use SQL Server integration tests for persistence, transaction, concurrency, and restart guarantees that mocks cannot establish. Repeat relevant builds/tests after changes and run the existing smoke checks.

Required scenarios:

1. Approval -> API call -> map external reference -> email result -> End.
2. API submission -> Wait for Event -> map callback -> Decision -> user review or completion; include timeout/escalation.
3. Parallel user approval and external callback -> Join -> completion, with reversed completion order and restart.
4. Inclusive routing with one/multiple/no matching branches, including default fallback and correct joining.
5. Parent -> child workflow with input/output mapping, immediate/delayed completion, failure, and cancellation.
6. Transient API/email failures, exhausted retries, invalid credentials, malformed responses, duplicate/late callbacks, and manual recovery.
7. Duration and due-date timers across timezone boundaries, suspension, restart, and cancellation.

Monitor node status, operation ID, attempt count, response summaries, waiting event/correlation, timeout, and incident recovery without exposing secrets. Use separate test connections and controlled test recipients. Measure queue latency and throughput with a representative load, then document supported limits rather than inventing a capacity guarantee.

**Exit:** all 13 activities have a final evidence status, representative workflows pass, unresolved limitations are explicit, and setup/operations documentation enables another person to configure and troubleshoot the same examples.

## Deliverables and decisions

Deliver the capability audit, defect fixes, configuration contracts/migrations, connection management, API/email/event forms, durable runtime improvements, targeted regression and end-to-end tests, example workflows, and an operator guide.

Default decisions for implementation: keep the existing engine and four categories; reuse compatible inbox/outbox/incident infrastructure; use generic HTTP and SMTP first; use mock services and mail capture for verification; preserve old published versions; keep application authentication separate. Concrete service providers, email server, credential-storage backend, business volume targets, and the first pilot workflow can be selected before their integration-specific implementation. They do not block the activity audit or core correctness work.

The first implementation milestone is Phase 1 plus the critical runtime/configuration defects in Phase 2. Initial fixes and regression checks are recorded in the implementation audit. They do not establish that the current application is ready for production integrations.
