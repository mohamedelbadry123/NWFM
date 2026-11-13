# Local UNRC example

The example uses designer version 3: one Main workflow, a Meter Installation child, and a Connection Activation child. **Connection Activation is an illustrative next Task Type**, since the actual next business process has not been specified. The Activity is **Unregistered Connection (UNRC)**. The example selects the first active CBU in the local organization hierarchy; change that scope before adapting it for business use.

The seeder is opt-in, accepts only Development with a local SQL Server, and does not clear existing data. It publishes through the normal validation pipeline. Rerunning it preserves an existing published UNRC definition. Existing drafts with its reserved `UNRC-*-LOCAL` keys are completed during a retry, so use different keys for your own drafts.

From `backend/src/NWFM.Api`, after building the solution:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet bin/Release/net10.0/NWFM.Api.dll --UnrcDemo:Enabled=true --UnrcDemo:SeedOnly=true --WorkflowSettings:AllowPrivateConnections=true
```

This uses the configured local database. For tests, use only the repository's disposable database launcher; it enables the same example and starts the loopback transports. The new e2e test makes no authenticated model calls.

Start the simulator from the repository root while running the example:

```powershell
node scripts/workflow-demo-transports.mjs
```

It listens on `127.0.0.1:5091` (HTTP) and `127.0.0.1:2525` (the existing email simulator). Its operation cache lasts for that process. It is a demonstration provider, not a durable substitute for the external contractor or closing system. The workflow's own jobs and retry state are persisted in SQL Server.

## Configured flow

| Place            | Configuration                                                                                                                                                             |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Main             | Start → Meter Installation → Determine next Task Type event → Connection Activation or End                                                                                |
| Meter child      | Registration → Go to Location → Preliminary feedback → Release with contractor → Contractor form → Consultant form → Approve levels 1, 2, 3 → Close in other system → End |
| Activation child | Confirm connection activation → End                                                                                                                                       |

Every human step selects exactly one published form belonging to its Task Type. Approval levels are ordinary sequential Steps with review forms and Accept/Reject actions. They use **UNRC — Local reviewers**, containing the local administrator. The separate approval-level panel and multiple Roles/Assignment Groups remain pending the deferred assignment decision. Labels `1`, `1.1`–`1.8`, and `2.1` illustrate the requested status levels; they do not add a status numbering engine.

Reject returns to the preceding human step and requires a comment. The first step can return to itself. In Preliminary feedback, enter `yes` in “Ready to release?” to fire the release event. Other answers create a fresh feedback step; they cannot reach the contractor step.

## Events, inputs and saved results

| Event                     | Mode                                         | Inputs                                                             | Results and routing                                                                                             |
| ------------------------- | -------------------------------------------- | ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------- |
| Registration notification | Async background, OnComplete of Registration | `requestReference`, mapped from the saved registration form        | Location opens without waiting; delivery has its own retry status                                               |
| Release with contractor   | Sync, required                               | `requestReference`                                                 | `body.id` → `contractorReleaseId`; `body.accepted` → `releaseAccepted`; only accepted releases reach Contractor |
| Close in other system     | Sync, required                               | `contractorReleaseId`, `meterSerial` from Contractor form          | `externalClosureId`, `closureAccepted`; child cannot complete on transport failure                              |
| Determine next Task Type  | Sync, required, in Main                      | `externalClosureId`, `requestReference`, copied from child outputs | `body.nextTask` → `nextTask`; `activation` opens the next child, `complete` ends Main                           |

Form answers are stored as immutable FormEngine submissions and linked in work-item form history. Configured outputs become workflow instance variables. Requests, operation keys, attempts, response status and mapped results are recorded by the durable integration executor. A required failure holds the workflow and must be recovered; retries never authorize the next step by themselves.

Open **Workflow data** on a Task Type to edit child input/output mappings. On a Step, use the same panel to map saved form answers, with source paths such as `output.meter_serial`. Choose **Save data mappings**, then save the workflow draft. Event response mappings remain in the event editor.

Each request has a stable idempotency key. The example allows three attempts, a 10-second request timeout, and a 2-second retry base delay multiplied by the attempt number. The simulator deliberately fails the first contractor release and first notification with HTTP 503, then succeeds. Replaying an already completed operation is rejected.

To exercise exhausted retries, use a request reference beginning `FAIL-` in the **Registration form**. Release fails all three attempts. Recover that exact reference locally:

```powershell
Invoke-RestMethod http://127.0.0.1:5091/unrc/recover -Method Post -ContentType application/json -Body '{"reference":"FAIL-your-reference"}'
```

Then retry the failed operation from the instance's integration controls. Its saved input and operation key are reused, and exactly one contractor step should open. A registration reference beginning `COMPLETE-` makes the main routing event select End instead of Connection Activation. Normal references select Connection Activation.

The actual external URLs, credentials, request/response contracts, and final assignment model are still needed before configuring real systems.

## Validation

`tests/workflow-unrc.e2e.ts` runs the complete flow in a disposable SQL database with the local simulator. It checks form-driven event firing, retry exhaustion without advancement, explicit recovery, duplicate replay rejection, closure, both main routing branches, child completion without a parent approval task, and persisted completion after refresh.

Archived definitions are hidden from the main workflow list by default. **Show archived workflows** reveals them so retained execution history can still be traced to its original configuration.
