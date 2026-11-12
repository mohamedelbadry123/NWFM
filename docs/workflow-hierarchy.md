# Task Types, Steps, and Events

New workflows use designer version 4 when routing authoring is enabled. The creation screen creates a Main workflow with organization scope and **Activity**. Existing version 1/2 definitions keep their previous execution behavior.

## Designer and ownership

- The palette contains Task Type, Step, and Events. A Main workflow allows Task Types and Events; a child allows Steps and Events. Start and End remain structural nodes.
- Select a Task Type, then choose **Create child workflow**. Its child opens in a large modal with breadcrumbs and Minimize, Full screen / Restore, and Close controls. Minimized children appear in a bottom dock; restoring them keeps the same editor instance, canvas position, selection, and pending edits. The main workflow stays visible behind the dimmed backdrop, with its canvas and selection preserved. Keyboard focus stays inside the child while it is open. Save and Back in the parent save its open children before continuing. Closing a child saves it first; a failed save keeps it open.
- A child owns its graph, variables, drafts, and published versions. The parent stores its definition key and selected version, not a copy of its nodes. Publish the child before publishing the parent.
- `POST /api/workflow/workspace/children` accepts `{ parentVersionId, nodeKey }`, checks tenant and Task Type ownership, and reuses a matching child. Opening a published parent resolves its pinned child, including a retired pinned version. It never creates a replacement for a published parent.
- Child workspace JSON records `parentDefinitionId`, `parentNodeKey`, and `taskTypeId`, with the parent's organization scope. Child settings are read-only in the designer. Publication rejects mismatched owners, scopes, or Task Types.
- Each Step must select exactly one published form associated with its owning Task Type. Replacing the form asks for confirmation before replacing that step's existing routing configuration. Main Task Types do not have a Form tab.

## Routes and events

### Version 4: Routing & Coordination

Version 4 adds **Condition**, **Fork**, and **Join / Wait**. Its XML graph is authoritative; the old Actions and arrow-expression editors are replaced by the routing panel. Versions 1–3 retain the behavior described below.

- Select a Step or Event and choose **Add route**. Connecting it to another element inserts a visible Condition. Choose a typed rule over completed results or explicitly select **Always**, then a destination and unmatched behavior. Incomplete configurations autosave but cannot publish. Saved and Ready to publish are independent states.
- A Condition normally requires exactly one match. Conflicting matches stop for attention. Explicit priority mode chooses the first matching rule. Nested AND/OR combines fields inside a rule; it does not coordinate branches.
- Fork starts every branch or every matching branch from one input snapshot. Pair it with a specific Join. Nested forks have independent activations; boundaries cannot be crossed or bypassed. Multiple human continuations require a Fork.
- ALL waits for every activated branch to qualify in the current activation. Skipped branches are not counted. ANY releases once after the first qualifying completion. Its default keeps remaining branches running; cancellation stops pending local work, descendants, timers and subscriptions. Dispatched external actions are not undone.
- An impossible Join or expired deadline follows its failure route, or stops for attention. Late results never release the continuation again. End waits for outstanding uncancelled workflow work; background deliveries are tracked separately.
- Form answers, completed Event outputs and workflow inputs are typed sources. Declare Event output fields in the inspector and map integration responses to those names. Results remain isolated by branch and activity visit. ALL exposes completed branch results under their source names; ANY also exposes its winning result under the Join's declared output fields.
- **Simulate routing** accepts mock form/Event results and clock advancement. It uses the same typed evaluator, records all branches, and makes no external calls or business tasks. The instance workspace shows coordination progress for the authorized execution tree, including children.

The workflow outline and canvas support keyboard selection. The configuration panel collapses into a dedicated panel on narrow screens. The draft editor supports English/Arabic, RTL, dark mode, undo/redo and announced save/error states. Review the three initial design studies in [the local preview](routing-coordination-preview.html).

### Guided authoring and rollout

Apply the additive `RoutingCoordination` migration and deploy compatible runtime code first. `WorkflowSettings:RoutingV4Enabled` (environment variable `WorkflowSettings__RoutingV4Enabled`) defaults to **false** and controls v4 authoring/publication only. Existing v4 runs continue if it is disabled.

The `NWFM` local development launch profile explicitly enables routing authoring. Restart the API after changing its launch settings. Launching the compiled API directly does not use that profile; set `WorkflowSettings__RoutingV4Enabled=true` explicitly for a local authoring session. A disabled authoring flag blocks saving existing v4 drafts, including the parent save required to open a child workflow. Save failures retain local edits and display the API error; draft revision conflicts still require reloading the server version before saving again.

With authoring enabled, hierarchical version 3 drafts are prepared automatically when opened. A durable pre-preparation snapshot is retained. Incomplete destinations and unsupported expressions remain as visible component repair items; they must be completed explicitly before publication. Published versions and running instances retain their existing behavior. Versions 1–2 retain their existing designer.

The palette shows Condition, Fork and Join directly. Adding a Fork creates its paired Join and two incomplete branch slots in one undo operation. Live validation runs after 600 ms of inactivity on the current draft snapshot, without saving it first. Canvas badges, configuration feedback and the issue drawer share one diagnostics state. Save status and publication readiness are separate. Stale responses are discarded; failed checks show Retry. Review [the guided design studies](guided-designer-preview.html).

Version-scoped endpoints are under `/api/workflow/definitions/{definitionId}/versions/{versionId}/routing`: `GET context`, `POST simulate`, `POST prepare`, and `POST validate-design`. The older upgrade endpoints remain for API compatibility; the designer does not expose them. `prepare` is idempotent and accepts `expectedRevision`; `validate-design` accepts `xmlContent`, optional `designerJson`, and `editId`, returning revision-tagged diagnostics without writing draft state. Draft saves require the revision returned with the version; stale writes return HTTP 409. Instance progress is available through `/api/workflow/workspace/instances/{id}/routing` after workspace authorization. These responses do not expose captured business-result values.

Arbitrary cross-workflow joins, quorum joins, changing live predicates and temporal coincidence windows remain outside this release. External effects use idempotency controls; they are not guaranteed to execute exactly once.

### Versions 1–3

**Go to** creates and connects a new Step/Task Type or Event, connects to an existing destination, or connects to End. Replacing the default forward destination preserves conditional and outcome routes. Conditions and their priorities remain configurable on the canvas connections.

The Step's Actions tab evaluates rules over its pinned form's saved answers. Rules can target a Step, an Event in the execution sequence, or End in the same child. A rule cannot jump into an event attached to a lifecycle trigger. Legacy multi-form routing remains supported for older definitions.

Events reuse the durable integration executor, request inputs, response-to-variable mappings, timeouts, retries, and idempotency controls. **Sync** requires successful completion before advancing. **Async** queues background delivery; **Must succeed before advancing** also gates an Async event when later work depends on its result. Ungated background responses do not select a new route or overwrite workflow variables after advancement. These modes describe workflow progression; outbound requests still execute through the durable worker.

A version 3 Task Type waits for its child, applies the child's output mappings, runs completion events, and follows its configured route. It does not create a second human approval work item in the parent. Required completion events hold the parent in `WaitingForTaskTypeEvents`; resuming is idempotent. Earlier designer versions retain their existing parent approval behavior.

## UNRC walkthrough and remaining decisions

Create UNRC with the intended organization scope and Activity. Add Meter Installation as a Task Type, then open its child. Add Registration, Go to Location, and Fill preliminary feedback as Steps, selecting one associated form per Step. Route preliminary feedback to a Release with contractor Event, configure its request and mappings, and require success before the contractor step. Continue with contractor and consultant steps, and use a closing Event before End. A Main workflow Event can then select the next Task Type using mapped variables and configured routes.

The separate approval-level configuration and multiple-assignee model remain pending. The owner explicitly deferred choosing direct Roles versus Assignment Groups; existing single-group step assignment remains in place until that decision is specified. The [local UNRC example](unrc-local-example.md) now configures the full sequence, with approval levels represented as ordinary Steps and status numbers in their labels. It uses local simulated external systems.

No existing workflow records are deleted or rewritten by this change. The recommended reset is limited to a confirmed local development/demo tenant: inventory definitions and references, remove only unused disposable examples, and deactivate history-bearing examples while retaining executions, form submissions, and audit records. `WorkflowDemo:Enabled` is already false by default; a reset must also account for any local override so deleted demos are not recreated. Production/shared records are outside that reset recommendation.

## Verification

Backend regression coverage includes hierarchy placement, one-form steps, Task Type membership, owned child reopening, retired version pins, event mode validation, form-to-event routing, parent completion without another work item, and required completion-event gating.

Frontend coverage includes palette placement, inherited Task Types, preserving the same rendered editor across minimize/restore, one-form replacement, execution modes, and applicable event triggers. `tests/workflow-hierarchy.e2e.ts` exercises the real API and browser, creates a child, edits a form-linked step, closes and reopens the modal without duplicating the child, and checks persistence after refresh. Run it through e2e against a disposable database and local integration transports; it uses exact browser interactions and makes no model calls.

These settings are additive fields in existing versioned JSON. No database migration is introduced by this change. See [the activity-form documentation](workflow-activity-forms.md) for the existing form-progress migration and legacy behavior.
