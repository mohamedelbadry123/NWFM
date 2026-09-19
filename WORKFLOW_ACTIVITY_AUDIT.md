# Workflow improvement verification

Branch: `workflow-improvement` · 2026-09-19

The implementation retains the existing engine and all four palette categories. HTTP and email use persisted jobs; callbacks use authenticated receipts and per-activity waits. SQL transactions serialize advancement. See [setup and operations](WORKFLOW_INTEGRATIONS.md).

## Activity evidence

All 13 activities executed in the SQL acceptance fixture. This is representative evidence, not certification of every possible topology or external provider.

| Category | Activity | Behavior and evidence |
| --- | --- | --- |
| Flow | Start | Pinned version, defaults, correlation and duplicate-start protection; SQL approval smoke. |
| Flow | End | Waits for active branch tokens; dispatches root outcomes only; SQL completion once. |
| Flow | Decision | Typed comparisons, AND/OR, parentheses, priority and fallback; unit cases and SQL approval decision. |
| Flow | Inclusive Gateway | Activates matching routes, fallback otherwise; two branches inside a nested SQL fork. |
| Flow | Parallel Fork | Unique identity per fork execution and ownership of asynchronous branches; callbacks plus timer in recovery test. |
| Flow | Join | Groups by fork execution, consumes once, restores parent token; nested acceptance and concurrent/restart checks. Multiple joins require explicit selection. |
| Tasks | User Task | Group assignment/fallback, claims, delegation, outcomes, required typed fields/options, mappings, instructions, SLA and installed action hooks. SQL form validation, mapping, SLA, completion hook and concurrent claim checks. |
| Automation | Service Task | Guided HTTP, protected authentication references, request/response mappings, persisted retry/replay and failure routes. Real local HTTP verifies Bearer, typed JSON, header mapping and stable operation ID. |
| Automation | Call Activity | Published child/version, input/output mapping, waiting or independent child, recursion guard. SQL immediate completion/output and waiting-child cancellation. |
| Automation | Set Variables | Typed value editor, legacy compatibility, declared/existing variable whitelist, no executable code. Tests preserve numeric-looking strings, quotes and newlines. |
| Automation | Notification | SMTP authentication, real To/Cc/Bcc/participant recipients, templates, durable attempts, channel logs and failure policies. Local mail capture verifies intended recipient and rendered amount. |
| Events | Timer | Duration/timezone-aware date, token continuation, persistence/suspension; SQL restart and offset-date unit cases. |
| Events | Wait for Event | API key/HMAC, source/event/correlation, buffering, deduplication, mappings, timeout, ambiguity/replay. Early callback and 24 concurrent deliveries producing exactly two receipts verified in SQL. |

## Browser checks

All categories/palette items, published read-only controls, clone-to-draft navigation, API save/reload, sample response mapping, callback example/correlation, email controls and English/Arabic labels were inspected. No console errors occurred in the verified flow. Regression tests cover clone navigation, configuration preservation and typed values. Correlation fields accept variables produced by earlier API outputs.

## Verification

- Backend: 300 tests, no failures/skips; Release build.
- Frontend: 57 headless Chrome tests; production build.
- `scripts/smoke-test.mjs`: original approval flow, tenant/participant checks, duplicate start, concurrent claim, ownership and projection.
- `scripts/workflow-integration-test.mjs`: SQL, all activities, task form/hook, real HTTP retry, early authenticated callback, SMTP capture, nested joins, timer and child output.
- `scripts/workflow-recovery-test.mjs prepare`, actual API restart, then `verify`: suspended callbacks/timer persist, on-time events beat elapsed deadlines, one join/End, waiting-child cancellation. Observed local 24-request callback batch: 30 ms; this is not a throughput guarantee.
- Migration applied to disposable `NWFM_WorkflowImprovementTest`; the normal development database was not migrated by these tests.

## Explicit boundaries

Required attachments, arbitrary JSON Schema forms, non-group assignment rules, disabled claim semantics and nonzero task queue priority are rejected before publication. Form key is a reference label, not an external form loader. Generic HTTP/SMTP and OAuth client credentials are provided; provider-specific authorization flows, attachments and email tracking are later extensions.

External delivery is at-least-once after uncertain network/crash outcomes. API providers must honor idempotency keys; SMTP can duplicate after uncertain acceptance. The application retains its trusted-participant model and needs a controlled hosting boundary until separate login/access-control work is completed. No production provider credentials, public deployment, capacity certification or production-data migration was performed.
