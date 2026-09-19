# Workflow improvement: first implementation milestone

Branch: `workflow-improvement`

Date: 2026-09-19

Scope: initial source audit, targeted runtime corrections, and shared configuration validation. The full [enhancement plan](WORKFLOW_ENGINE_ENHANCEMENT_PLAN.md) remains in progress.

## Changes in this PR

- Failed service tasks stop the instance at the failed node, including retryable failures. The existing explicit retry operation now re-executes that task rather than skipping its outgoing edge. Automatic background retries are not yet implemented.
- Service providers receive node configuration and activity-instance identity. The operation idempotency key remains stable across failed attempts and changes after a completed traversal. This does not by itself guarantee external exactly-once delivery.
- Wait for Event recognizes the designer's `eventKey` and the legacy `signalKey`. Missing/mismatched keys and missing active waits cannot consume the wait. The canonical key takes precedence when both are present.
- External-signal timers cannot be picked up by the clock worker or invoked through time-based resumption.
- Due dates are converted to actual UTC instants on both frontend and backend, including explicit offsets. Editing a stored date preserves seconds and milliseconds.
- Set Variables executes the designer's object representation while preserving support for legacy arrays and `assignments` objects.
- Editing guided settings retains additional configuration fields. Notification channels/recipients are no longer discarded; obsolete timer fields and replaced legacy aliases are removed deliberately.
- Validate, publication preview, and Publish share configuration checks. Unregistered service actions, malformed settings, unsupported variable-based event correlation, external-signal timers, and unimplemented notification Retry policies are blocked for new publications.
- Resuming a failed instance clears its stale failure summary; the failed activity retains its failure history.

## Activity evidence and remaining gaps

“Covered” below refers to named automated paths only. It is not a production-readiness certification. Engine tests use EF's in-memory provider and cannot establish SQL Server concurrency or crash guarantees.

| Category | Activity | Evidence / current state | Still required |
| --- | --- | --- | --- |
| Flow | Start | Existing runtime paths start instances and run synchronous nodes. | Browser audit of all creation paths, duplicate/concurrent starts with SQL Server. |
| Flow | End | Existing and new sequential paths complete; repeated event delivery cannot create a second End in the tested path. | Multiple-end and active-branch completion semantics. |
| Flow | Decision | Transition evaluator tests cover conditions; approval runtime paths exist. | Full designer/runtime matrix for priorities and fallback conditions. |
| Flow | Inclusive Gateway | Runtime implementation and enum tests exist. | Runtime evidence for all/one/no matching routes, nested joins, asynchronous branches. |
| Flow | Parallel Fork | Existing runtime test covers two successful service branches. | Durable ownership for asynchronous branches, failure/retry and restart cases. |
| Flow | Join | Existing fork/join path passes. | Nested/repeated joins and concurrent completion without selecting the wrong token. |
| Tasks | User Task | Existing claim, completion, redirect, and approval path tests remain green. | Every inspector field, action hooks, SLA, forms, and mappings verified in the browser. |
| Automation | Service Task | New tests prove stop/retry behavior, stable operation key, and configuration delivery to a mocked provider. Missing providers are rejected at publication. | Real HTTP provider, guided request/response/authentication form, connection store, durable jobs/retries, parallel retry ownership. |
| Automation | Call Activity | Existing implementation inspected; configuration now requires an actual non-empty definition key and Boolean wait flag. | Parent/child mapping, immediate/asynchronous completion, independent child completion, cancellation and recursion tests. |
| Automation | Set Variables | New runtime tests cover designer objects and legacy formats; frontend tests cover legacy editing. | Full type semantics, whitelist diagnostics and expression UX. |
| Automation | Notification | Frontend regression test preserves channel/recipient configuration. Unsupported Retry is blocked for new publications. | Real email recipients/templates/authentication, durable sending, per-channel outcomes; current diagnostic mail path is not production-ready. |
| Events | Timer | Existing duration test plus new offset-date tests. External signals are excluded from due processing and rejected on publication. | SQL-backed restart/suspension/concurrency tests, full timezone UI review. |
| Events | Wait for Event | New tests cover canonical/legacy keys, mismatch/missing key, missing active wait, and repeat delivery after completion. | Authenticated external ingress, correlation/payload mapping, early-event buffering, per-activity subscriptions, timeout races, operation-aware inbox recovery and replay. |

## Compatibility and intentional changes

No database schema or public HTTP endpoint changes are introduced. Existing published definitions are not rewritten or automatically revalidated. New publication now rejects configurations that previously appeared valid but could not perform their advertised behavior. A previously published external-signal timer will remain waiting instead of incorrectly firing on a clock; it needs an operator-reviewed replacement using the future event-delivery implementation.

Provider configuration fields added to the internal execution contract are optional constructor arguments, so existing source callers remain compatible. Explicit retry in this milestone supports failed Service Task activities. Unsupported retries return an error before changing the failed instance back to Running. Complex parallel recovery remains an open item.

The application still uses a trusted-development participant model without login. This PR neither exposes a callback endpoint nor sends live integration tests to external services.

## Verification

- Baseline: 217 backend tests passed; production frontend build passed.
- Runtime regression coverage: service failures/retries, event matching and non-consumption, external-signal exclusion, variable assignment formats, UTC due dates.
- Publication coverage: malformed/unsupported settings and agreement between Validate, preview, and Publish even with a stale validation badge.
- Frontend coverage: notification field preservation, legacy event/variable formats, timer-mode changes, and date round trips.
- Final verification: 259 backend tests passed, 45 frontend tests passed in headless Chrome, and the production frontend build passed. This adds 42 backend and 10 frontend regression cases over the original suite.
- Full SQL Server integration, fault injection, load testing, and browser end-to-end audit are not completed in this milestone.

## Remaining implementation sequence

1. Persist activity/branch execution ownership and explicit waiting/retry outcomes; distinguish start versus signal inbox recovery.
2. Add protected connections and HTTP action provider, durable jobs, retry policies, and request/response forms.
3. Add authenticated callback ingress, durable event subscriptions, correlation, payload mapping, and timeout handling.
4. Replace diagnostic notification delivery with configured recipients, templates, authenticated mail and durable delivery attempts.
5. Complete the remaining activity/browser audit and real-world scenarios against SQL Server.

Keep the PR in draft while these broader capabilities are outstanding; this milestone starts the enhancement rather than marking the full plan complete.
