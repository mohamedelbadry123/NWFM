# Workflow activity forms and linked child previews

Main Activities and User Tasks expose **General · Assignment · Form · Actions · SLA**. General selects the activity's own Task Type. Department and Field Activity Type are no longer manually selected there.

## Form selection

The Form tab shows cards for all forms associated with the selected Task Type, with names, codes, lifecycle status, published version and preview. Add multiple usable forms and arrange their order with the move controls. At least one form is required for every business activity; start/end, gateways, timers and automated nodes are exempt.

The activity stores `taskTypeId` and an ordered `formSteps` array. Each step has a stable `id`, `formId`, `versionNo`, `rules`, and `otherwise` destination. Adding a form pins its current published version. Selecting another version preserves the rules and requires review in Actions. Changing Task Type preserves the sequence for review and requires confirmation in Form. Removed fields, choices and targets must be repaired before publication. Legacy single-form bindings are adapted when editing and retain their existing runtime path in previously published versions.

Existing SLA classification is preserved. An activity without Department and Field Activity Type obtains both from the chosen form; the existing SLA tab, matching rules and published snapshots continue to apply. A form missing classification or an activity without an active SLA rule cannot publish in the workspace.

## Independent child workflow

Selecting a child workflow expands a read-only diagram inside its Main Activity. The selected published version is fixed. The child owns its activities and their settings: no activities or forms are copied, inherited or synchronized into the parent. The preview does not write child nodes or edges into parent XML. Nested calls are shown as activities without recursively loading them, so cycles cannot trigger unbounded preview requests.

Use **Open child workflow** to inspect its own designer in another tab and use the usual draft/publish process for edits. A newer child version must be explicitly selected by the parent. Existing Task Type and organization-scope eligibility checks and execution behavior remain in force.

## Execution

Both workflow task detail and the instance workspace render one active, pinned form through the shared dynamic renderer. Submit and continue validates and saves through `IFormGateway`, then evaluates the current form's ordered rules using its canonical saved answers. The first match wins; otherwise its default route applies. Rules support nested All (AND) and Any (OR) groups, typed comparisons, inclusive ranges and published choice values. Missing answers match only explicit empty checks.

`Next` follows the configured form order and uses the existing forward route after the final form. `Form` targets any form in the current activity, including itself. `Activity` finishes the current execution and enters a business activity or End in the same workflow; business activities start at their first form. Returns start fresh visits; restarting an activity starts a fresh activity execution, and a Main Activity starts a fresh child instance. Explicit rules may skip forms. Publication requires a potential path to End and rejects cross-branch jumps that bypass joins.

Form order is optional for routing: it determines the initial form and destinations configured as `Next`, but never restricts an explicit `Form` or `Activity` target. Configure each form independently in Actions, including its **Otherwise** destination. For example, Form 1 can send `inspectionRequired = true` to Form 2 and otherwise to Form 3; Form 3 can return `needsCorrection = true` to Form 1 or send approved answers to Activity B. Activity B can route corrections back to Activity A, which opens a fresh execution at its first form. Neither forward nor backward explicit routes depend on the forms’ displayed positions. Reordering forms preserves their stable explicit destination IDs.

Moving between forms keeps the work item, assignment and SLA open and does not run activity completion events. Leaving the activity follows the existing required event gates. A Main Activity's forms still wait for child completion, and its form configuration is independent from the child's. Reject, redirect and comment retain their behavior without submitting or evaluating a form.

`FormProgressJson` stores active-step/visit state and immutable submission/routing history on the work item. Multi-form submissions use `ContextType=WorkItem`, context ID `{workItemId}:{visitId}`, and `ClientSubmissionId=visitId`. Both action endpoints carry `formVisitId`; stale visits are rejected and recorded visits replay without submitting or advancing twice. History retains each form's pinned schema and media context in the UI. Previously published single-form and inline-form workflows keep their execution behavior.

Apply the additive Workflow migration **20261009120000_ActivityFormProgress** before running the updated API. It adds a nullable `FormProgressJson` column; it does not rewrite existing instances.

## Interfaces and access

- `GET /api/workflow/workspace/activity-forms/task-types/{taskTypeId}` lists associated forms in Task Type order.
- `GET /api/workflow/workspace/activity-forms/task-types/{taskTypeId}/{formId}/versions/{versionNo}` previews a published version after server-side membership checks.
- The previous Department/Field Activity discovery endpoints remain for compatibility.
- Listing and preview use `ActivityFormReaders` (Manage definitions or View forms). Workflow task actions retain their existing ownership and tenant checks. Direct Form Engine submissions may not forge a WorkItem context.
- Workflow completion permission also permits uploading form media; file ownership checks still apply.

Form Engine remains the owner of schemas and submissions. Workflow reads Task Type associations through `IWorkflowTaskTypeCatalog` and form data through `IFormGateway`.

## End-to-end regression tests

Run `npm run test:e2e:replay` from the repository root in the Linux/WSL environment described in [quality-gates.md](quality-gates.md). The launcher owns a disposable SQL Server database and local transports; it must not target a business database. The new activity-form tests use exact browser interactions and require no model calls or additional recordings.

`tests/workflow-activity-forms.e2e.ts` covers:

- Required-field validation, forward skips, backward routes, repeated visits, and default Next routes.
- Assignment and deadline retention between forms; entered answers survive workspace refresh.
- Historical submission replay and rejection of unknown visits without duplicate advancement.
- Nested numeric routing, parent/child gating, fresh child runs after returning to an activity, and persisted completion history after reload.
- Read-only child previews and rejection of missing form destinations before publication.
- Claiming and submitting forms from task detail, preserving the current visit after reload, and completing the task through its dedicated endpoint.

The fresh-database run also verifies demo startup. Demo workflows now bind an optional approval-notes form to their own task type, satisfying the same publication rules as other workflows without requiring answers for the seeded approval walkthrough.
