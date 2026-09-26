# Advanced workflow test you can follow

This walkthrough uses the local **Water isolation** demo to exercise the current workflow system. Use a disposable development database: the demo creates definitions, users, groups, connections, and instances. Record each result before moving to the next step.

## 1. Prepare the local demo

You need SQL Server, .NET 10, Node.js 22.12 or later, and an administrator account. Open three PowerShell windows in the repository root.

**Window 1 — local test providers**

```powershell
node scripts/workflow-demo-transports.mjs
```

**Window 2 — API and demo data**

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__DefaultConnection='Server=localhost;Database=NWFM_ManualWorkflowDemo;Integrated Security=True;TrustServerCertificate=True'
$env:WorkflowDemo__Enabled='true'
$env:WorkflowDemo__CallbackKey='nwfm-local-demo-callback-key-testing-only'
$env:WorkflowSettings__AllowPrivateConnections='true'
dotnet run --project backend/src/NWFM.Api --no-launch-profile --urls http://localhost:5080
```

**Window 3 — web app**

```powershell
cd frontend
npm ci
npm start
```

Open <http://localhost:4200>. On a fresh development database, sign in with `administrator@localhost` / `Administrator1!`. If the demo is already seeded, restart does not reset its progress. Use another new disposable database name when you need a clean run.

**Pass:** You can see the Workflows, New Instance, Instances, SLA, and Lookups pages. The local provider window reports HTTP port 5091 and SMTP port 2525.

## 2. Inspect setup before starting

1. Open **Lookups**. Check Cluster → Region → City. Change the cluster and confirm the Region and City choices reset. Check **Water Network** and its field activity types, including **Isolation**, **Demo — Request review**, and **Demo — Closure**.
2. Open **SLA**. Check the demo calendar's Sunday–Thursday working periods and the rules for the Water Network activities. The **Demo — Short SLA test** rule is deliberately short for an overdue test.
3. Open **Workflows** and search for **Demo — Water isolation — Visual**. Open **Versions and designer**. Inspect the three Main Activities, their pinned child definitions, the sequence **Send completion record** API Request, and the event nodes linked by dashed trigger lines. Open a child definition such as **Demo — Request Review steps — Visual** and inspect its User Task.
4. Open **Admin → Workflow → Integrations**. Confirm the demo HTTP, email, and callback connections exist. The callback entry shows its webhook URL. These are local test connections.

**Pass:** The main workflow and its children are published. Each Main Activity shows a department, Field Activity Type, assignment group, SLA, Accept and Reject, and a child workflow. Triggered event nodes have a source activity and no normal sequence arrow.

## 3. Run the complete approval and rework story

Use a unique reference such as `MANUAL-WATER-001`.

1. Open **New Instance**, select **Demo — Water isolation — Visual**, enter the reference, select **Demo instance**, and start it.
2. On the instance page, expand **Request Review**. Its phase is **Waiting for child workflow**. The parent Accept action should be unavailable while the child checklist is open.
3. Under **Test as user**, choose **Demo reviewer**. Write `Site documents received` and click **Add comment** on the child. Check **History and comments**: the comment appears, and the child task remains open.
4. Click **Accept** on the child checklist. Refresh or wait for automatic refresh. The child completes and the Request Review parent approval becomes available.
5. Enter `Missing permit — send back` and click **Reject** on the Request Review parent. A new Request Review attempt and child checklist appear; the earlier execution remains in history. A blank Reject comment should be refused.
6. Accept the new child checklist, then Accept the Request Review parent. The workflow enters **Isolation Execution**.
7. Select **Demo isolation engineer**, Accept that child checklist, then Accept its parent Main Activity.
8. Select **Demo closure officer**, Accept the Closure child checklist, then Accept its parent Main Activity.
9. Refresh until the root instance is **Completed**. Check that **Send completion record** ran before End.

**Pass:** Parent approval is gated by child completion; comments do not advance; Reject creates a new execution with a new child; each stage is assigned to its intended group; the root completes once. **History and comments** shows who acted and when. Try viewing the instance as a different demo user: actions outside that user's assigned group should be disabled. Ordinary instances have no demo-user selector and require actual group membership.

## 4. Check integrations and SLA on that instance

1. Expand **Events and integrations**. Look for REST on Closure Accept, SOAP on completion, email and SMS background deliveries, and the sequence **Send completion record** API Request. The worker may take a few seconds.
2. Open **Response details** for REST and SOAP. REST should have a local provider response; SOAP should contain the accepted result. Check attempt counts and required/background labels.
3. Compare the activity due dates and SLA names with the rules you inspected earlier. The main clock includes child work.
4. Start **Demo — Water isolation — OVERDUE — Visual** as a demo instance, or open the seeded **Demo — Overdue SLA — Visual** instance. Wait for the short deadline, refresh, and check the overdue indication plus reminder/breach event history. Do not expect an SLA alert to reassign the task.

**Pass:** Required REST/SOAP work finishes before progression; email/SMS are recorded as background operations; due dates and breach information are visible. The local SMTP server accepts email for testing but does not deliver it to an inbox.

## 5. Test failure and recovery

1. Open the seeded **Demo — Integration failure — Visual** instance. Its REST call uses `/fail`, which initially returns HTTP 503. In **Events and integrations**, find the failed required operation and note its attempt count and error. The workflow must not silently complete through that failure.
2. In another PowerShell window, restore the local provider:

   ```powershell
   Invoke-WebRequest -Method Post -Uri http://127.0.0.1:5091/recover
   ```

3. Return to the instance and click **Retry delivery** on the failed operation. Refresh until the operation succeeds and the instance progresses. Verify the operation's history and attempt count.

**Pass:** Failure is visible, required work blocks advancement, and retry completes after provider recovery. Retrying preserves the operation identity for external deduplication.

## 6. Test an authenticated callback

1. Open the seeded **Demo — Callback waiting — Visual** instance and record its exact reference, normally `Demo — Callback waiting — Visual`.
2. In **Admin → Workflow → Integrations**, find **Demo — Callback** and copy its displayed webhook URL.
3. Run this in PowerShell after replacing the URL if yours differs:

   ```powershell
   $body = @{
     eventId = [guid]::NewGuid().ToString()
     eventKey = 'demo.field.completed'
     correlationId = 'Demo — Callback waiting — Visual'
     payload = @{ confirmed = $true }
   } | ConvertTo-Json -Depth 4
   Invoke-RestMethod -Method Post -Uri '<paste displayed webhook URL>' -Headers @{ 'X-Workflow-Key' = 'nwfm-local-demo-callback-key-testing-only' } -ContentType 'application/json' -Body $body
   ```

4. Refresh the instance. The wait should finish and the next activity should start. Send the **same body** again; it should not create a second advancement. A wrong key should be rejected.

**Pass:** The matching callback advances once, duplicate delivery is recorded safely, and unauthorized delivery cannot advance the instance. Keep the same `eventId` only for a retry of the same event.

## 7. Check designer and administration features

Use **new drafts** for these checks so the published demo remains reusable. For each draft: save, leave the page, reopen it, validate, and inspect any node-specific error before publication. Publish only a graph whose configuration and local connections you have checked.

| Feature | Small test and expected result |
| --- | --- |
| Versions | Clone a published demo version to a draft, change its summary, and confirm the published version remains read-only. Validate the draft before Publish. |
| Start, End, User Task | Draw Start → User Task → End. Configure group, instructions, required form field, Accept/Reject and SLA. A missing required field or blank Reject comment must block submission. |
| Decision | Route a declared variable through two labelled conditions and a default. Run values for each route; exactly one path should continue. |
| Inclusive Gateway | Configure two matching branches and a Join; check that only activated branches are waited for. Repeat with one match and fallback. |
| Parallel Fork and Join | Put separate user tasks on two branches; complete them in opposite orders. End should be reached only after both finish. |
| Timer | Place a short duration Timer in a disposable draft. After publication, check that it waits, fires once, and survives a page refresh or API restart. |
| API Request | Configure the demo HTTP connection, method/path/body and response mapping. **Send Test** should show status, elapsed time, and mapped output without advancing an instance. Then run it as a sequence node. |
| Email and SMS | Use the local connections. Check recipient/message templates, required versus background behavior, and delivery records. |
| Wait for Event | Use the demo callback connection with an event key, correlation variable, timeout and explicit timeout route. Check matching and mismatched correlation values. |
| Main Activity / child | Confirm its pinned published child runs first and final parent Accept waits for child completion. Reject reruns the chosen earlier activity. |
| Assignment | In the organization workflow pages, inspect Participants and Assignment Groups. Test claim, release, delegate, and completion with accounts that are actually members of the selected group. |
| Monitoring | Check Tasks, Requests, Workload, Notifications, Instances, Incidents and Dead Letters. A failed operation should be diagnosable from history and, where offered, recoverable. |
| Binding | If the module catalog is configured, create a test binding for a published workflow, simulate it, check group mappings and readiness, then exercise Shadow and Active modes with a test event. Do not switch a business binding to Active as part of this disposable test. |

The visual palette has Start, End, Decision, Inclusive Gateway, Parallel Fork, Join, Main Activity, User Task, API Request, Email, SMS, Timer, and Wait for Event. Older **Call Activity** and **Set Variables** nodes can still load in existing graphs but are omitted from the new-node palette. Test them on an existing compatible definition or through the dedicated integration tests, not by searching for new palette tiles.

## 8. Final record

For each test, record **Pass / Fail / Not configured**, the instance reference, definition/version, time, and a screenshot of the relevant History or Events panel. A missing permission, group member, SLA rule, published child, or connection is a setup issue; record it separately from a workflow runtime failure.

For automated regression on the same disposable demo database, set `$env:NWFM_API_URL='http://localhost:5080'`, then run `node scripts/workflow-visual-test.mjs` and `node scripts/workflow-workspace-test.mjs` from the repository root while the API and local providers are running. These scripts create and act on test records; use the manual steps above to see the behavior in the UI.
