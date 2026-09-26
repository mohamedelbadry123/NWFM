# Form engine: forms, fields and submissions

This document explains how a form is built and published, what its fields are, and what happens to
an answer from the moment someone picks a photo to the moment it sits in a SQL table and appears on
a task's PDF report. It is written for developers working on NWFM.

The form engine is the **FormEngine** module (SQL schema `FormEngine`). Other modules — today, Tasks — use
it only through `IFormGateway` in `NWFM.Shared`.

## Contents

1. [The moving parts](#1-the-moving-parts)
2. [A form's life](#2-a-forms-life)
3. [The schema document](#3-the-schema-document)
4. [Field types and their columns](#4-field-types-and-their-columns)
5. [Publishing](#5-publishing)
6. [Where submissions are stored](#6-where-submissions-are-stored)
7. [Submitting a fill](#7-submitting-a-fill)
8. [Photos, signatures and files](#8-photos-signatures-and-files)
9. [Reading submissions back](#9-reading-submissions-back)
10. [What a fill belongs to: ContextType and ContextId](#10-what-a-fill-belongs-to-contexttype-and-contextid)
11. [Fields across forms](#11-fields-across-forms)
12. [Forms inside tasks](#12-forms-inside-tasks)
13. [Closing C2M field activities](#13-closing-c2m-field-activities)
14. [Limits and failure modes](#14-limits-and-failure-modes)
15. [Where the code is](#15-where-the-code-is)

---

## 1. The moving parts

| Piece | What it holds |
|---|---|
| `FormEngine.FormDefinitions` | One row per form: code, names, status, the **working draft** schema, the current version number, and the name of its submissions table. |
| `FormEngine.FormVersions` | One row per published version: the frozen schema. Never edited after publish. |
| `FormEngine.FormFields` | The form's field registry: every `data_name` any version of the form has used, with its type and labels, and the first and last version it appeared in. |
| `FormEngine.SUB_<CODE>` | The form's own submissions table — one typed column per field. Created at first publish. |
| `FormEngine.SubmissionFiles` | One row per uploaded file: where the bytes are, which form and field they belong to, and which submission claimed them. |
| File storage | The bytes themselves, under `FileStorage:Root` (`Media` by default). |

```
 builder (web)                          FormEngine                                  SQL Server
 ─────────────                          ──────────                                  ──────────
 design fields ── PUT schema ─────────▶ FormDefinition.SchemaJson (draft) ────────▶ FormEngine.FormDefinitions
 publish ──────── POST publish ───────▶ FormPublisher ──┬─ freeze version ───────▶ FormEngine.FormVersions
                                                        ├─ register fields ──────▶ FormEngine.FormFields
                                                        └─ create/widen table ───▶ FormEngine.SUB_<CODE>
 fill (renderer)
   pick a photo ─ POST uploads ───────▶ UploadFormFile ─── bytes ───────────────▶ Media/pending/…
                                                     └──── row (PENDING) ───────▶ FormEngine.SubmissionFiles
   submit ─────── POST submissions ───▶ FormSubmissionService ── insert row ────▶ FormEngine.SUB_<CODE>
                                                     └──── link files (LINKED) ─▶ Media/forms/<CODE>/<id>/…
```

---

## 2. A form's life

A form has one of four statuses:

| Status | Can be edited | Accepts fills | Notes |
|---|---|---|---|
| `DRAFT` | yes | only if it was published before | A new form, or a published form someone has started editing again. |
| `PUBLISHED` | yes (reopens as a draft) | yes | Has at least one frozen version. |
| `DEPRECATED` | no | **no** | Kept for reading. Only a published form can be deprecated. |
| `ARCHIVED` | no | **no** | Also deactivated. |

The rule for accepting fills is `AcceptsSubmissions = has a published version AND not deprecated or
archived`. A published form that is being edited again stays open for fills against the version it
already published — editing never breaks work that is pinned to a version.

**Versions are immutable.** Publishing copies the working draft into a new `FormEngine.FormVersions` row
(`v1`, `v2`, …) and never touches earlier rows. Every submission records the version it answered,
and is always shown back through that version's schema.

**Cloning** copies the working draft into a new form with a new code. Version history does not come
with it, and the clone gets its own submissions table at its first publish.

**The code is permanent.** It names the submissions table, so it cannot change after creation.

---

## 3. The schema document

The builder saves the form as one JSON document:

```json
{
  "name_en": "Meter inspection",
  "name_ar": "فحص العداد",
  "elements": [
    {
      "type": "section", "data_name": "details", "label_en": "Details", "label_ar": "التفاصيل",
      "visible_conditions": { "match": "all", "conditions": [
        { "field": "is_open", "operator": "equal", "value": "yes" } ] },
      "elements": [
        { "type": "text", "data_name": "inspector_name", "label_en": "Inspector", "label_ar": "المفتش",
          "required": true, "min_length": 3, "max_length": 20,
          "pattern": { "regex": "^[A-Za-z ]+$", "description": "letters only" } },
        { "type": "single_choice", "data_name": "pipe_material", "label_en": "Material", "label_ar": "المادة",
          "allow_other": true,
          "choices": [ { "value": "steel", "label_en": "Steel", "label_ar": "صلب" },
                       { "value": "pvc",   "label_en": "PVC",   "label_ar": "بلاستيك" } ] }
      ]
    },
    { "type": "yes_no",  "data_name": "is_open", "label_en": "Open", "label_ar": "مفتوح" },
    { "type": "numeric", "data_name": "depth_m", "label_en": "Depth", "label_ar": "العمق",
      "format": "integer", "min": 1, "max": 10 }
  ]
}
```

### Element properties

| Property | Applies to | Meaning |
|---|---|---|
| `type` | all | The field type — see [section 4](#4-field-types-and-their-columns). |
| `data_name` | all | The field's key, and the name of its SQL column. See the rules below. |
| `label_en`, `label_ar` | all | The question, in each language. |
| `required` | answerable fields | Must be answered — unless the field is hidden, disabled, or its visibility rule does not hold. |
| `hidden`, `disabled` | all | Static visibility and read-only. A hidden or disabled field is never required. |
| `visible_conditions` | all, including sections | Show the field only when the rule holds. A section's rule applies to everything inside it. |
| `required_conditions` | answerable fields | Replaces `required` with a rule: the field is required exactly when the rule holds. |
| `min_length`, `max_length`, `pattern` | `text` | Length bounds and a regular expression (with a human description). |
| `format`, `min`, `max` | `numeric` | `integer` or decimal, and the allowed range. |
| `date_rule`, `min_date`, `max_date` | `date`, `date_time` | Relative to today (`before`, `on_or_before`, `after`, `on_or_after`) or a fixed range. |
| `choices` | `single_choice`, `multiple_choice` | The options: a machine `value` plus `label_en`/`label_ar`. |
| `allow_other` | choice fields | Adds an "Other" option with free text (see below). |
| `allowed_extensions` | media fields | The file types an upload may have. |
| `elements` | `section` | The section's children. Sections nest, carry no value, and have no column. |
| `c2m_parameter_name` | answerable fields | The C2M `ParameterName` the answer is sent under when a task closes its field activity. See [section 13](#13-closing-c2m-field-activities). |
| `c2m_fa_status`, `c2m_reason` | options of `wfm_action_taken` | The C2M outcome (`C` completed, `X` cancelled) and reason that option closes the activity with. |

### Rules

A rule is `{ "match": "all" | "any", "conditions": [ { "field", "operator", "value" } ] }`. The
operators are `equal`, `not_equal`, `contains`, `starts_with`, `greater_than`, `less_than`,
`is_empty` and `is_not_empty`.

The browser evaluates rules as the user types, and the server evaluates the **same rules the same
way** on submit (`FormRuleEngine`). The server copy deliberately follows JavaScript's semantics —
`Number(null)` is `0`, an absent key is not the same as a null one — because a server that disagrees
with the form would reject fills the user was told were valid.

### Data names

A `data_name` becomes a SQL column, so it must be a legal identifier:

- It starts with a letter or `_`, then contains only letters, digits and `_`.
- It is at most 128 characters long.
- It must not clash with a base column (`Id`, `VersionNo`, `Status`, `ContextType`, `ContextId`,
  `SubmittedBy`, `SubmittedByName`, `SubmittedDate`, `ClientSubmissionId`, `Created`) — publishing
  refuses one with `FormEngine.Schema.ReservedDataName`.
- It is matched case-insensitively.

Publishing enforces these rules (`FormDataName`), and the submission store checks every name again
before it reaches a SQL statement.

### "Other" answers

A choice field with `allow_other` stores the sentinel value `__other__` as its answer, and the typed
text in a **companion** column named `<data_name>_other`. Keeping the sentinel lets a rule still test
"the user chose Other". The companion is registered in `FormEngine.FormFields` with `IsCompanion = 1` and
gets its own column.

---

## 4. Field types and their columns

| `type` | What the user does | Column type | What is stored |
|---|---|---|---|
| `text` | Types a line | `NVARCHAR(MAX)` | The text. |
| `memo` | Types paragraphs | `NVARCHAR(MAX)` | The text. |
| `numeric` | Types a number | `DECIMAL(28,4)` | The number. 28 digits so a 15-digit meter number fits. |
| `yes_no` | Toggles | `BIT` | `1` or `0`. |
| `date` | Picks a day | `DATE` | The calendar day, as read on site. |
| `time` | Picks a time | `TIME` | The clock time. |
| `date_time` | Picks both | `DATETIME2(0)` | The local wall clock — no time zone offset. |
| `calendar_with_hours` | Fills a from/to time per weekday | `NVARCHAR(MAX)` | JSON keyed by day code. |
| `single_choice` | Picks one option | `NVARCHAR(400)` | The option's `value` (not its label). |
| `multiple_choice` | Picks several | `NVARCHAR(MAX)` | JSON array of option values. |
| `geolocation` | Drops a pin, or types coordinates | `NVARCHAR(500)` | JSON `{ "lat", "lng", "address" }`. |
| `barcode` | Scans a code | `NVARCHAR(400)` | The decoded text. |
| `photo`, `video`, `audio`, `file`, `signature` | Uploads | `NVARCHAR(MAX)` | JSON array of file references — never the bytes. |
| `section` | — | none | A container. |

Choices store the **value**, not the label. A form can relabel an option later without rewriting
past fills. When a fill is shown to a person, the label is looked up from the version the fill
answered (`FormAnswerDescriber`).

---

## 5. Publishing

Publishing (`POST /forms/{id}/publish`, `FormPublisher`) happens in one database transaction:

1. **Parse and check the schema.** It must have at least one field, and every `data_name` must be
   valid.
2. **Lock this form.** `sp_getapplock` on `FormEngine.Form.{id}`. Publishing one form never waits on another.
3. **Check types against the form's own registry.** A field keeps one type **within its form**:
   republishing `depth_m` as `text` after it was `numeric` fails with `FormEngine.Field.TypeConflict`
   (HTTP 409). Another form may use the same name with a different type — that is its own column in
   its own table.
4. **Count the fields.** A form may store at most **500** fields, including companions, so a row
   stays inside SQL Server's 8,060-byte limit. More fails with `FormEngine.Schema.TooManyFields`.
5. **Update `FormEngine.FormFields`.** Add new names, and refresh labels and the last version each name
   appeared in.
6. **Name the table**, on first publish only: `SUB_` + the code in upper case, with anything outside
   `A–Z 0–9 _` turned into `_`. If two codes collapse to the same name, `_2`, `_3`… is appended.
   Naming takes a second, global lock so two first publishes cannot pick the same name.
7. **Freeze the version.** `form.Publish` adds the `FormEngine.FormVersions` row and sets the status to
   `PUBLISHED`.
8. **Create or widen the table.** Create `FormEngine.SUB_<CODE>` if missing, add a column for each new
   field, and **widen** a column whose type has grown (a longer `NVARCHAR`, a wider `DECIMAL`).
   Columns are never dropped or narrowed, so every existing row stays valid.
9. **Commit**, then clear the field-catalog cache.

If any step fails, nothing is kept: no version, no registry change, no half-built table.

A field removed from the form in a later version keeps its column, so answers already given to it
stay readable. The registry records the last version that field was in.

---

## 6. Where submissions are stored

**Each form has its own table.** Every form used to write into one shared table. That table would
have run into SQL Server's 1,024-columns-per-table limit and held one type per data name for the
whole application. Now each form's limits apply only to that form.

```sql
CREATE TABLE [FormEngine].[SUB_SRV_FIELD_SURVEY_002] (
    [Id]                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [VersionNo]          INT              NOT NULL,  -- the version this fill answered
    [Status]             NVARCHAR(50)     NULL,
    [ContextType]        NVARCHAR(100)    NULL,      -- what the fill belongs to, e.g. 'Task'
    [ContextId]          NVARCHAR(100)    NULL,      -- …and its id
    [SubmittedBy]        NVARCHAR(256)    NULL,
    [SubmittedByName]    NVARCHAR(256)    NULL,
    [SubmittedDate]      DATETIMEOFFSET   NULL,
    [ClientSubmissionId] UNIQUEIDENTIFIER NULL,      -- the client's retry key
    [Created]            DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    -- then one typed column per field, e.g.
    [meter_reading]      DECIMAL(28,4)  NULL,
    [site_photo]         NVARCHAR(MAX)  NULL
);
-- indexes: SubmittedDate DESC; (ContextType, ContextId) where set; unique ClientSubmissionId where set
```

The SQL is not hand-written in C#. All statements live in
`FormEngine.Infrastructure/sql/form-submissions.sql.json`, with `{table}`, `{column}` and `{type}`
placeholders. Table names come only from `FormDefinitions.SubmissionTable`, checked against
`^SUB_[A-Z0-9_]+$`. Column names come only from the `FormEngine.FormFields` registry. Both are
bracket-quoted. Nothing a caller posts ever becomes an identifier.

**Self-repair.** At startup (`ApplySqlObjects`), the initializer makes sure every published form has
its table and columns. A submit that finds a declared column missing (for example, after a database
restore) repairs the table before writing.

---

## 7. Submitting a fill

`POST /api/v1/form-engine/forms/{formId}/submissions` → `SubmitFormCommand` → `FormSubmissionService`.
The Tasks module goes through the same service, via `IFormGateway.SubmitAsync`.

```json
{
  "versionNo": 2,
  "contextType": "Task",
  "contextId": "5b0c…",
  "clientSubmissionId": "8f1e…",
  "clientFilledAt": "2026-09-21T10:15:00+03:00",
  "answers": {
    "meter_reading": 1234,
    "pipe_material": "__other__",
    "pipe_material_other": "Copper",
    "site_photo": [ { "fileId": "6f1c…", "path": "…", "name": "front.jpg", "type": "image/jpeg", "size": 81234 } ]
  }
}
```

The service, in order:

1. **Loads the version.** The version asked for, or the current one if none is given. Fails if the
   form does not accept fills (never published, deprecated or archived) with 409.
2. **Narrows the table to that version's fields.** The table holds every field any version
   introduced, but a fill of v1 cannot write a field only v3 asks.
3. **Replays a retry.** If `clientSubmissionId` was already stored, it answers with the **original**
   submission id and `isReplay: true` instead of writing again. A client that lost the response can
   simply resend. Two racing retries are settled by a unique index — the loser reads back the winner's
   row.
4. **Validates the answers** (`FormAnswerValidator`) against the version's rules: required (after
   visibility), text length and pattern, number format and range, allowed choices, date rules.
   "Today" is measured in `FormEngine:TimeZoneId` (Asia/Riyadh), from the client's fill time when it
   sent one — never later than the server's clock — so an offline fill synced the next morning is
   judged on the day the work was done.
   - `FormEngine:EnforceFieldRules` switches the scalar rules off. Date rules always apply.
   - A regex is time-limited: an author's pattern must not become a denial-of-service on the server.
   - Every failure names its field; the response is HTTP 400 with the list.
5. Then, **inside one transaction**:
   1. **Signatures sent as data URLs** are saved as files first, so every signature is a file
      reference.
   2. **The row is inserted.** Each answer is converted to its column's type: `"yes"` → `BIT 1`,
      `"2026-09-21"` → `DATE`, a JSON array → text. A value that will not convert fails with a message
      naming the field.
   3. **The media is claimed** — see [section 8](#8-photos-signatures-and-files).
   4. **Commit.**

The response is `{ submissionId, versionNo, isReplay }` — 201 for a new fill, 200 for a replay.

---

## 8. Photos, signatures and files

Media is uploaded **when the user picks the file**, not when they submit. That keeps the submit small,
and lets a slow upload finish while the user carries on filling in the form.

1. **Upload** — `POST /api/v1/form-engine/uploads` with the form id, version, `data_name` and the
   file. The server checks:
   - the size (`FileStorage:MaxFileSizeMb`, 25 MB by default) — both the declared size and the bytes
     actually written,
   - the content type, when `FileStorage:AllowedContentTypes` lists any,
   - that the form and version exist,
   - the field's `allowed_extensions`, when it declares them.

   The file is stored under its new file id, never under the name the client sent. The bytes go to `Media/pending/…` and a `FormEngine.SubmissionFiles` row is written with status
   **`PENDING`**. The client puts the returned reference (`fileId`, `path`, `name`, `type`, `size`)
   into the answer.
2. **Claim on submit** — `SubmissionMediaLinker` reads the file ids out of the media answers and claims
   only files that are **`PENDING`** and **uploaded against the same form**. A made-up or reused file
   id cannot take a file from another submission. Each claimed file is moved to
   `Media/forms/<CODE>/<submissionId>/`, marked **`LINKED`**, and stamped with the submission id and
   the fill's `ContextType`/`ContextId`.
3. **Orphans.** A file picked and then abandoned — or uploaded against a field that is not a media
   field, which linking never reads — stays `PENDING`. Nothing sweeps these yet; because no
   submission refers to them, a cleanup job can delete `PENDING` files past a cutoff safely.

**Who can download** (`GET /uploads/{fileId}`, `FormFileAccess`):
- an administrator, or whoever uploaded the file;
- for a linked file: anyone with `ViewSubmissions`, and — when the fill belongs to a task — anyone
  with `ViewTasks`.

Nobody else can read another user's pending file.

Downloads go through `HttpClient` (the token is not a cookie), so the web client fetches each file
once as a blob and reuses it for the thumbnail, the viewer and the download button
(`MediaObjectUrlService`).

---

## 9. Reading submissions back

- **List / get** — `GET /forms/{formId}/submissions[/{id}]` reads the form's own table. Each row is
  returned with its base columns and its answers keyed by `data_name`.
- **Rendering** — the web client draws a fill through **the version it answered**, read-only, with
  the same renderer used to fill it (`app-dynamic-form-renderer` with `readOnly`).
  `fromStoredAnswers` turns column values back into form values (a `BIT` back to `yes`/`no`, JSON
  text back to arrays and points).
- **Labelled answers** — `FormAnswerDescriber` turns a stored row into
  `{ label_en, label_ar, display_en, display_ar, point }`, in the form's order, leaving out blank
  answers:
  - a choice reads as its option's label,
  - "Other" reads as the typed text,
  - a yes/no as Yes/No (نعم/لا),
  - a media answer as its file names,
  - a geolocation answer carries its point so the client can show it on a map.

  The task details screen and the PDF report both use this, so neither re-implements the rules.

---

## 10. What a fill belongs to: ContextType and ContextId

A form does not know what it is used for. In the app, forms are filled for field tasks; a fill posted
straight to the form engine's API with no context is a standalone fill, and workflow steps are
expected later. Each fill says what it belongs to with two columns that every submissions table has:

| Column | Holds | Example |
|---|---|---|
| `ContextType` | What kind of thing owns the fill | `Task` |
| `ContextId` | That thing's id, as text | `5b0c9e4a-8d2f-4c1e-9a7b-3f6d2e1c0b9a` (the task's id) |

In the `SUB_DEMO_ALL_INPUT_TYPES` table, a fill made for a task looks like this:

| Id | VersionNo | ContextType | ContextId | SubmittedBy | SubmittedDate | … answers … |
|---|---|---|---|---|---|---|
| `8f1e…` | 2 | `Task` | `5b0c9e4a-…` | crew-a | 2026-09-21 10:15 | … |
| `9a2c…` | 2 | *null* | *null* | sara | 2026-09-21 11:02 | … (a standalone fill) |

**The rules**

- Both or neither. A fill with only one of the two is refused.
- At most 100 characters each. The values are free text, so a new integration does not need a form
  engine change.
- The values NWFM writes are in `FormContextTypes`:
  - `Task`: a field task. `ContextId` is the task's id.
  - `WorkItem`: reserved for workflow steps. `ContextId` would be the work item id.
- A standalone fill leaves both empty.

**Why the link lives on the fill, not on the owner.** A task does not keep a list of submission ids.
It asks the form engine for "the fills of Task *X*":

- `IFormGateway.ListByContextAsync(formId, "Task", taskId)` returns every fill of the task, newest
  first.
- `IFormGateway.GetLatestByContextAsync(...)` returns the newest fill.

Each `SUB_` table has an index on `(ContextType, ContextId)` (filtered to rows that have a context), so
that lookup is one index seek on the form's own table. Two consequences:

- A fill and its owner cannot disagree: there is no second copy of the link to go out of date.
- A retried fill keeps pointing at the same task, because the client key (`ClientSubmissionId`) replays
  the stored row, context and all.

**Files carry the context too.** When a fill claims its uploads (section 8), each
`FormEngine.SubmissionFiles` row is stamped with the fill's `ContextType` and `ContextId`. That is how:

- a task's files gallery is one query (`ListFilesByContextAsync("Task", taskId)`), and
- a person who can view tasks (`ViewTasks`) may download a task's photos even though they are not a
  form-engine reviewer.

**Who may write a context.** A context is a claim that the fill belongs to something, so only the module
that owns that something may make it:

- Task fills are recorded by `POST /api/v1/tasks/{id}/fill`. That endpoint checks the task's status,
  the caller's territory and team, and only then calls the form engine through the gateway.
- The form engine's own endpoint, `POST /forms/{id}/submissions`, **refuses** `ContextType = Task`.
  Otherwise anyone who can submit forms could post a fill in any task's name. The list of refused
  contexts is `FormContextTypes.OwnedByModules`.
- `WorkItem` is not on that list yet: the form engine's endpoint still accepts it, for workflow screens
  that post a fill directly. When workflow records its fills through a module of its own, add
  `WorkItem` to the list.

**Reading by context.** The submissions API filters by context:
`GET /forms/{formId}/submissions?contextType=Task&contextId=<taskId>`.

---

## 11. Fields across forms

Every form has its own field registry (`FormEngine.FormFields`) and its own table (`SUB_<CODE>`). The
same `data_name` used in two forms is therefore **two columns in two tables**, each with its own type:

| Form | Table | `meter_reading` column |
|---|---|---|
| `SRV-FIELD-SURVEY-002` | `SUB_SRV_FIELD_SURVEY_002` | `DECIMAL(28,4)` (a numeric field) |
| `METER-CHECK` | `SUB_METER_CHECK` | `NVARCHAR(MAX)` (a text field) |

**Within one form, across its versions:**

- **The table holds every field any version declared.** A fill writes only the fields of the version
  it answered (the store narrows the table to that version), so a v1 fill cannot write a field only v3
  asks. Columns a version does not have stay null.
- **Removing a field** from a later version keeps its column. Old answers stay readable, and newer fills
  leave it null.
- **Renaming a field** means a new `data_name`, so a new column. The old column keeps the old answers.
  There is no automatic copy.
- **Changing a field's type** is refused (`FormEngine.Field.TypeConflict`). Give the field a new
  `data_name` instead.
- **Growing a type** is automatic. A column that became too short for its field type, such as the
  longer geolocation column with an address, is widened on the next publish. Nothing is ever narrowed.
- The `<data_name>_other` companion of a choice field with "Other" is a column of its own, in that
  form's table only.

**Across forms:**

- **Types are independent.** A name can be numeric in one form and text in another. Publishing never
  compares forms.
- **The field catalog groups names across forms.** The **Field catalog** tab under **Lookups** and the builder's
  autocomplete read `FormFields` grouped by `data_name`. They show how many forms use a name, and flag
  names whose type differs between forms. Reuse an existing name when a field means the same thing in
  another form (`meter_reading`, `wfm_action_taken`). A report can then line the forms up by name.
- **Files are the one shared table.** Uploads live in `FormEngine.SubmissionFiles` for every form, keyed
  by form id, `data_name` and submission.
- **There is no single table to query across forms.** To read one field across several forms, ask the
  registry which tables carry it, then `UNION ALL` them, converting to a common type when the forms
  differ:

  ```sql
  -- Which forms have a meter_reading, and where their fills are.
  SELECT d.Code, d.SubmissionTable, f.FieldType
  FROM FormEngine.FormFields f
  JOIN FormEngine.FormDefinitions d ON d.Id = f.FormDefinitionId
  WHERE f.DataName = N'meter_reading';

  -- Then one query over those tables.
  SELECT 'SRV-FIELD-SURVEY-002' AS FormCode, Id, SubmittedDate, ContextId,
         CAST(meter_reading AS NVARCHAR(100)) AS meter_reading
  FROM FormEngine.SUB_SRV_FIELD_SURVEY_002
  UNION ALL
  SELECT 'METER-CHECK', Id, SubmittedDate, ContextId, meter_reading
  FROM FormEngine.SUB_METER_CHECK;
  ```

  Grid columns computed from answers across forms were deliberately left for later. Because the tables
  are typed, a later read model or view can be built over them without changing how fills are stored.
- **Task types and forms.** A task pins one form and one version, so all of a task's fills sit in one
  table. Pointing a task type at another form affects only new tasks. The fills of that type's tasks
  can then span two tables; each task still reads its own through its pinned form.

---

## 12. Forms inside tasks

A **task type** names a published form. The whole flow:

1. **Create.** When a task is created, it **pins** the form's current version (`FormVersionNo`). A
   later republish does not change the form for tasks already in flight. An unfilled task can be
   moved to the newer version explicitly ("move to newer form version").
2. **Fill.** The Tasks module calls `IFormGateway.SubmitAsync` with the pinned version,
   `ContextType = "Task"`, `ContextId` = the task id, and the client's retry key. The row lands in
   **that form's own table**. Then the task records the fill (`RecordFill` — idempotent for the same
   submission).

   The form row and the task row are written by two modules, so there are two saves. If the second
   fails, the client retries with the same key: the form engine replays the stored submission and
   the task update completes. No duplicate row is written.
3. **Read.** The task's fills are `ListByContextAsync(formId, "Task", taskId)`; its files are the
   `SubmissionFiles` stamped with the same context.
4. **Details screen.** It shows:
   - the latest fill as labelled answers, with a map button for location answers;
   - earlier fills on the Records tab;
   - a **Preview form** button that draws the pinned version read-only with the chosen fill;
   - a files gallery with downloads.
5. **PDF report** — `GET /api/v1/tasks/{id}/export-pdf?language=en|ar`, drawn with QuestPDF:
   - the task's facts;
   - the latest fill's labelled answers;
   - its signatures and photos embedded;
   - a list of every file.

   Only the latest fill's images are embedded: a photo from a returned fill would read as evidence
   for answers it does not belong to. An image that is missing, larger than 10 MB, or will not decode
   (a HEIC photo, a truncated upload) is listed as not embedded rather than breaking the report.
   Arabic reports run right to left, with Noto fonts embedded in the assembly so every host renders
   Arabic the same way.

---

## 13. Closing C2M field activities

Some field work belongs to C2M: a field activity (an **FA**) that WFM dispatched and C2M expects to be
closed. When a task is that work, approving the task closes the activity in C2M with what the crew
recorded. This is ported from the reference app, and it is **off** until `C2m:Enabled` is set.

### What decides whether a task closes an activity

Both of these must be true:

- **The task carries an FA id.** It is entered on the task (with WFM's ticket id, sent as C2M's
  `MOBId`), and can be corrected until the task is approved.
- **The task's type is a closing type.** "Closes the C2M field activity" is switched on for the type,
  which makes its form the closing form.

A task without an FA id, or of any other type, is NWFM's own work and closes nothing in C2M.

### What the form contributes

The closing form carries C2M metadata that the builder writes and the form engine parses (see
section 3):

- **`wfm_action_taken`** is the field whose answer decides the outcome. Each of its options can name
  one:
  - `c2m_fa_status` is `C` (completed) or `X` (cancelled);
  - `c2m_reason` is the reason sent with it. For `X` it is the cancel reason, and the option's value is
    used when it is blank. For `C` it is the closure reason.

  The builder shows these two inputs only on that field's options.
- **`c2m_parameter_name`** on any field sends its answer in the closure's `ParametersList` under that
  name. For example, `wfm_building_units` is sent as `CM_BUNIT`. A field without one stays in NWFM.
- **`wfm_remarks`** is sent as the closure's `comment` (`-` when empty).

### How the outcome is resolved

The Action Taken answer is looked up in this order (`C2mActionMappingResolver`):

1. **The answered option on the form**, when it names a `c2m_fa_status`.
2. **The C2M action mapping table** (**Lookups → C2M action mappings**, table
   `Task.C2mActionMappings`). Each mapping is an action code with its status and one reason. C2M
   rejects a reason that does not match the status, so a mapping keeps only the matching one. The
   table is cached and evicted on every change. Startup seeds the reference app's codes when
   `DatabaseStartup:SeedData` is on: `OCUL01` completes; `MMFCNR1` to `MMFCNR4` cancel.
3. **The built-in rule.** `OCUL01` completes; any other code cancels, carrying itself as the reason.
   An empty answer also cancels. Closing work as done when it cannot be accounted for is the worse
   mistake, so an unknown answer never completes an activity.

### What is sent

`POST {C2m:BaseUrl}/NwcCompass/MobilityCcbDirUpdateFARes/updateFAResponse`, with Basic authentication
(`C2m:Username`, `C2m:Password`):

```json
{
  "FADetails": {
    "FAId": "FA-1001", "FAStatus": "X", "MOBId": "77",
    "cancelReason": "MMFCNR2", "closureReason": null,
    "comment": "Gate locked", "completionDTTM": "2026-09-21-08.00.00",
    "imageURL": null, "meterDisconnectionType": "",
    "ParametersList": { "Parameters": [ { "ParameterName": "CM-MTRRD", "ParameterValue": "1250.5" } ] },
    "userId": "SPNWCALLDP"
  },
  "sourceApp": "WFM",
  "transactionId": "FA-1001"
}
```

- **Parameters.** Only fields with a parameter name that were answered are sent. Blank answers are
  left out, never defaulted.
- **Value formatting.** Values are sent as compact text: a decimal drops SQL's trailing zeros
  (`1250.5000` → `1250.5`), a yes/no is `Y`/`N`, and a date is `yyyy-MM-dd`.
- **What is read.** The answers come from the task's **latest** fill, through the version it answered.
- **Acceptance.** C2M accepts a closure by answering `status: "OK"` with `responseCode: "0"`.

### When it is sent

With **`C2m:WaitForAcknowledgement` on** (the default), approving the task sends the closure first,
waiting at most `AcknowledgementTimeoutSeconds` (15):

| C2M's answer | The task | The reviewer sees |
|---|---|---|
| Accepted | Approved in the same save; closure `CLOSED` | Success |
| Refused | Stays filled (`SUBMITTED`); closure `REJECTED` | 409 `Tasks.C2m.Rejected`, with C2M's message and code |
| No answer | Stays filled; closure `FAILED` | 503 `Tasks.C2m.Unavailable`; approving again retries |
| Integration off or bypassed | Approved; closure `SKIPPED` | Success |

With **`WaitForAcknowledgement` off**:

1. The approval commits at once, with the closure queued as `PENDING`.
2. A background sender (`C2mClosureHostedService`) checks every `RetryIntervalSeconds` (300) for
   approved tasks with a pending closure and sends each one.
3. If C2M does not answer, the closure stays queued until `MaxAttempts` (5) tries, then becomes
   `FAILED`. A refusal becomes `REJECTED` straight away.

The queue is the tasks table itself, so it survives restarts. Each task is claimed by saving its
attempt time before the call. The row version then makes a second server working the same queue lose
that race instead of sending the closure twice.

**Sending again.** An approved task whose closure is `REJECTED` or `FAILED` has a **Send to C2M again**
button on its **C2M** tab (`POST /api/v1/tasks/{id}/c2m/retry`, needs `ReviewTasks`). The task stays
approved whatever C2M answers.

**Never twice.** Once any attempt for a task has been accepted, the next one is not sent: it answers
"already acknowledged". A retry after a lost response is therefore safe.

### What is recorded

- **The dispatch log.** Every attempt is a row in `Task.C2mDispatchLogs`, even when nothing was sent
  (`SKIPPED`, with the reason). Each row holds:
  - the attempt number;
  - the outcome asked for (`C`/`X`);
  - the full request JSON;
  - C2M's response JSON and code;
  - the error.

  The row is saved as `PENDING` **before** the call, so a crash mid-request still leaves evidence that
  C2M may have been told.
- **The task.** It carries the current state (`C2mStatus`), the number of attempts and the last
  attempt time.
- **On screen.**
  - The worklist shows a `C2M · Closed/Refused/…` tag.
  - The task details show the FA id and whether the task closes on approval.
  - The **C2M** tab lists every attempt, with its request and response.

### Configuration (`C2m` in appsettings)

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Off: every closure is recorded as `SKIPPED` and nothing is sent. |
| `ByPassClosingInCcb` | `false` | Skip the call during a cutover without switching the integration off. |
| `BaseUrl` | — | C2M's scheme and host. |
| `Username`, `Password` | — | Basic credentials. Set them with user secrets or `C2m__Username` / `C2m__Password`, never in the file. |
| `UserId`, `SourceApp` | `SPNWCALLDP`, `WFM` | Who C2M attributes the closure to. |
| `ImageUrlTemplate` | — | Where C2M can view the photos; `{faId}` is substituted. |
| `TimeoutSeconds` | 30 | Bound on a background call. |
| `WaitForAcknowledgement` | `true` | Approval waits for C2M (see above). |
| `AcknowledgementTimeoutSeconds` | 15 | Bound on the wait while a reviewer is watching. |
| `RetryIntervalSeconds` | 300 | How often the background sender runs, and the least time between two tries. |
| `MaxAttempts` | 5 | Tries before an unanswered closure is `FAILED`. |

**Not ported.** Only the closing side of the reference's C2M/WFM integration was ported. These parts
were not:

- field activities arriving from WFM (the Oracle intake and reconciliation jobs);
- activities pushed by C2M to create work;
- the reference's seeded closing form;
- the courtesy notification to WFM after C2M accepts.

In NWFM, the FA id is entered on the task.

---

## 14. Limits and failure modes

| Situation | What happens |
|---|---|
| A field changes type in a republish | 409 `FormEngine.Field.TypeConflict`; nothing is published. |
| More than 500 stored fields | 400 `FormEngine.Schema.TooManyFields`. |
| Invalid `data_name` | 400 `FormEngine.Schema.InvalidDataName`, naming the offending names. |
| Fill against a deprecated/archived/unpublished form | 409 `FormEngine.Form.NotPublished`. |
| An answer breaks a rule | 400 `FormEngine.Submission.AnswersInvalid`, one message per field. |
| An answer will not convert to its column | 400 `FormEngine.Submission.AnswerRejected`, naming the field; nothing written. |
| The same `clientSubmissionId` sent twice | The first submission is returned (`isReplay: true`). |
| Upload too large / wrong extension | Refused at upload, before the fill is submitted. |
| File row exists but bytes are gone | Download: 404 `FormEngine.File.NotFound`. Report: listed as unavailable. |
| Database restored without the per-form tables | Recreated at startup, or on the next fill. |
| Another publish holds the form's lock | The publish waits, then times out with a clear message. |
| A fill posted to the form engine in a task's name | 400 on `ContextType`: task fills go through the task's fill endpoint. |
| C2M refuses a closure while a reviewer approves | 409 `Tasks.C2m.Rejected`; the task stays filled. |
| C2M does not answer while a reviewer approves | 503 `Tasks.C2m.Unavailable`; the task stays filled. |
| C2M does not answer a background closure | Retried every interval until `MaxAttempts`, then `FAILED`; a person can send it again. |

---

## 15. Where the code is

| Concern | Location |
|---|---|
| Entities | `FormEngine.Domain/Entities` — `FormDefinition`, `FormVersion`, `FormField`, `SubmissionFile` |
| Schema parsing, rules, validation, display | `FormEngine.Application/Common/Schema` — `FormSchemaParser`, `FormRuleEngine`, `FormAnswerValidator`, `FormAnswerDisplay`, `FormAnswerDescriber`, `FormDataName`, `FormSubmissionTableName` |
| Publishing | `FormEngine.Application/Forms/Common/FormPublisher.cs` |
| Submitting | `FormEngine.Application/Submissions/Common/FormSubmissionService.cs`, `SubmissionMediaLinker.cs`, `SignatureDataUrlNormalizer.cs` |
| Uploads | `FormEngine.Application/Uploads` |
| Per-form table SQL | `FormEngine.Infrastructure/Submissions/FormSubmissionStore.cs`, `FormEngine.Infrastructure/sql/form-submissions.sql.json` |
| Contract for other modules | `NWFM.Shared/Integration/Forms/IFormGateway.cs`, implemented in `FormEngine.Infrastructure/Integration/FormGateway.cs` |
| Task PDF report | `Tasks.Application/Tasks/Queries/ExportTaskPdf`, `Tasks.Infrastructure/Reports` |
| C2M closure | `Tasks.Application/C2m` (resolver, dispatcher, `TaskC2mClosure`, retry, mappings), `Tasks.Infrastructure/C2m` (HTTP client, background sender), `Tasks.Domain/Constants/C2mConstants.cs` |
| C2M builder inputs | `frontend/src/app/features/form-engine/builder/components/field-editor-dialog.component.*` |
| C2M screens | `frontend/src/app/features/tasks/c2m`, the **C2M** tab in `task-detail-dialog.component.html` |
| Web builder | `frontend/src/app/features/form-engine/builder` |
| Web renderer | `frontend/src/app/shared/components/dynamic-form`, `frontend/src/app/shared/form-schema` |
| Task screens | `frontend/src/app/features/tasks` |
