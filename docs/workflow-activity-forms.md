# Workflow designer: the activity Form tab

A **User Task** in the workflow designer has a **Form** tab (General · Assignment · Form · SLA). It lists every Form Engine
form filed under the activity's **Department** and **Field Activity Type**, as soon as both are chosen in General settings.

- **Discovery and preview only.** Listing a form does not attach it to the activity, make it required, or submit anything.
  There is no activity-to-form binding in the workflow model today; the legacy inline task questions (`formFields`) are kept
  unchanged and mentioned on the tab when an activity still has them.
- **Main Activity** nodes have no Form tab (General · Assignment · SLA). Their forms belong to the activities of the linked
  child workflow; General has an "Open child workflow in a new tab" link once a child is chosen.
- Gateways, Start/End, Timer, Service/Notification tasks and Wait events have no tabs and no Form.
- General no longer shows an Accept and Reject section. Existing outcomes, the rejection destination and legacy actions
  stay in the stored configuration and are preserved when the workflow is edited, saved or published.

## Lifecycle

| Form state | Listed | Usable | Preview |
| --- | --- | --- | --- |
| Published | yes | yes | its published versions |
| Draft revision of a published form | yes | yes, at its last published version ("newer draft in progress") | published versions |
| Draft, never published | under "Not available for use" | no | none |
| Deprecated / Archived | under "Not available for use" | no | published versions, for history |

## API and permissions

`GET /api/workflow/workspace/activity-forms?departmentCode=&fieldActivityCode=&pageNumber=&pageSize=` (page size ≤ 100) and
`GET /api/workflow/workspace/activity-forms/{formId}/versions/{versionNo}?departmentCode=&fieldActivityCode=`.

- Both codes are required (400 `ActivityForms.ContextRequired`); there is no catalog-wide fallback.
- The preview re-checks on the server that the form is filed under that exact Department + FA Type (404 otherwise).
- Policy `ActivityFormReaders`: **Manage definitions** or **View forms**. It grants nothing of form administration.
- Workflow reads forms only through `IFormGateway` (`ListForFieldActivityAsync`, `FindFieldActivityFormAsync`); it never
  touches FormEngine tables.

## Demonstration data

Seeded by the FormEngine seed when `DatabaseStartup:SeedData` is true (development default), through the normal publisher,
so fields are registered and submission tables created. Idempotent: an existing form — published, or the seeded draft — is
never modified or republished, so edits made in the Form Engine survive restarts.

| Department | Field Activity Type | Code | Name | State |
| --- | --- | --- | --- | --- |
| `10` Water Network | `LEAK_REPAIR` Leak repair | `DEMO-LEAK-INSPECTION` | Leak Inspection (Computed Columns Demo) | Published v1 |
| `10` Water Network | `LEAK_REPAIR` Leak repair | `DEMO-LEAK-REPAIR-COMPLETION` | Leak Repair Completion Report | Published v1 |
| `10` Water Network | `LEAK_REPAIR` Leak repair | `DEMO-LEAK-REPAIR-CHECKLIST` | Leak Repair Safety Checklist (Draft) | Draft, never published |

## Try it

1. Start the app (`start-dev.bat`; API on 5081, web on 4200) and sign in as a workflow designer.
2. Open a draft workflow in the designer and select (or drop) a **User Task**.
3. Open **Form**: with no context it names what is missing — use **Go to General settings**.
4. In General choose Department **Water Network (10)** and Field Activity Type **Leak repair (LEAK_REPAIR)**, then open **Form**:
   the two published forms are listed, and the checklist appears under "Not available for use".
5. Choose Field Activity Type **01** (or any other combination) — the list is replaced; "no forms" when none are filed there.
6. **Preview** a form: it opens over the designer, nothing is submitted, and Escape or Close returns to the same activity.
7. Select a **Main Activity**: no Form tab; if Form was open, General is shown instead.
