# Computed columns

A **computed column** is a value worked out of a form's answers and shown next to the task in the
**Field Tasks** grid. Examples: a *Severity* worked out from the leak rate, the *Water lost* worked out
from rate × hours, or a *Response* that says "Immediate" when there is a safety hazard.

The form author defines the columns once, in the form designer. After that, each fill of that form
works out the values automatically. Nobody types them, and the grid can sort by them.

---

## Contents

1. [Try it with the demo data](#1-try-it-with-the-demo-data)
2. [Add a computed column to a form](#2-add-a-computed-column-to-a-form)
3. [Writing a value](#3-writing-a-value)
4. [Conditions](#4-conditions)
5. [Recipes](#5-recipes)
6. [How it behaves](#6-how-it-behaves)
7. [When something is refused](#7-when-something-is-refused)
8. [For developers](#8-for-developers)
9. [How to improve it](#9-how-to-improve-it)

---

## 1. Try it with the demo data

On start-up, the API seeds a ready-made example when `DatabaseStartup:SeedData` is on. It is on by
default. Restart the API once to get it.

| What | Where |
|---|---|
| Form **Leak Inspection (Computed Columns Demo)**, code `DEMO-LEAK-INSPECTION` | **Forms**. It is filed under department *10 — Water Network*, field activity *Leak repair* |
| Task type **Leak Inspection (Computed Demo)**, code `DEMO_LEAK_INSPECTION` | **Lookups → Task types** |
| Five tasks `TSK-LEAK-0001` … `TSK-LEAK-0005` around Riyadh | **Field Tasks** |

The first four tasks come already filled, one for each outcome. The fifth is left for you to fill:

| Task | Answers | Severity | Water lost (m³) | Response | Metered use (m³) |
|---|---|---|---|---|---|
| TSK-LEAK-0001 | 1200 l/h for 6 h, 180 customers, safety hazard | Critical | 7.2 | Immediate | — |
| TSK-LEAK-0002 | 650 l/h for 12 h, 20 customers, meter 10450 → 10458.4 | High | 7.8 | Same day | 8.4 |
| TSK-LEAK-0003 | 150 l/h for 48 h, 4 customers | Medium | 7.2 | Same day | — |
| TSK-LEAK-0004 | 12.5 l/h for 72 h, 1 customer, meter 2210 → 2210.9 | Low | 0.9 | Scheduled | 0.9 |
| TSK-LEAK-0005 | not filled yet | — | — | — | — |

To see it:

1. Open **Field Tasks** and filter **Task type** to *Leak Inspection (Computed Demo)*.
2. The **Severity**, **Water Lost** and **Response** columns appear on the right. Click a header to
   sort by it.
3. Open the **Computed columns** picker in the toolbar and tick **Metered Use**. It is defined but
   not shown by default.
4. Fill `TSK-LEAK-0005`. Its values appear as soon as the fill is saved.
5. Open **Forms → Leak Inspection → Design → Computed columns** to see how the four columns are
   written.

The seed never overwrites anything. If you delete the demo tasks, they come back on the next start.
Tasks that already exist are left as they are.

---

## 2. Add a computed column to a form

1. Open the form in the designer: **Forms → ✎ Design**.
2. Click **Computed columns (n)** in the side panel.
3. Click **Add column** and fill in:

   | Field | What to put |
   |---|---|
   | **Column title (English / Arabic)** | The grid header. Both are required. |
   | **Output** | **Text** for labels such as "High", or **Number** for amounts. A number column sorts numerically and aligns right. |
   | **Key** | Made for you from the English title (`Leak severity` → `leak_severity`). Leave it alone unless you have a reason. See [the key](#the-key). |
   | **Show in the task grid by default** | Ticked: the column appears when the grid is filtered to this form's task type. Unticked: people can still add it from the column picker. |

4. Give the column its value:
   - **Always the same formula** (for example, water lost = rate × hours): leave the rules empty and
     type the formula under **Value**.
   - **Depends on the answers** (for example, severity): **Add rule**, click **Set conditions** to say
     when it applies, and type what the value is **then**. Add more rules as **Else if**. Put the
     fallback under **Otherwise**.
5. Click a field under **Insert a field…** to drop its name into the value box you last clicked. You
   never need to type a data name.
6. **Apply**, then **Save** the form. **Publish** it for new tasks to use it (see
   [versions](#versions)).

Problems are listed at the bottom of the dialog, and **Apply** stays disabled until they are fixed.

### The key

The key is the column's internal name. It must start with a lower-case letter and use only
lower-case letters, digits and `_`. The grid uses it to sort (`computed:{formId}:{key}`) and to
remember which columns each person chose to show.

- **New column:** the key follows the English title as you type, and is kept unique (`severity`,
  `severity_2`, …).
- **Column that came with the form:** the key is kept. Click **Change** only if you must. Renaming a
  published column's key is like adding a new column: tasks filled before the rename keep their
  values under the old key, so the grid treats the two as different columns.

---

## 3. Writing a value

A value is a short expression.

| You write | You get |
|---|---|
| `'High'` | the text *High*. Text goes in single quotes; a quote inside is doubled: `'it''s'` |
| `42`, `0.5` | a number |
| `leak_rate_lph` | that field's answer, by its data name |
| `a + b`, `a - b`, `a * b`, `a / b`, `( … )` | arithmetic, with the usual precedence |
| `-a` | a negative number |

Functions:

| Function | Does |
|---|---|
| `sum(a, b, …)` | adds; **skips blank** answers |
| `min(a, b, …)`, `max(a, b, …)` | smallest / largest; **skip blanks** |
| `round(x)`, `round(x, 2)` | rounds, half away from zero, to 0–10 decimals |
| `abs(x)` | absolute value |
| `coalesce(a, b, …)` | the first one that isn't blank, e.g. `coalesce(actual, estimated, 0)` |
| `concat(a, ' - ', b)` | joins as text |

What happens with blanks and bad input:

- In `+ - * /`, a **blank or non-numeric** answer makes the whole result blank. Use `sum` or
  `coalesce` when a blank should count as zero: `sum(a, b)` or `coalesce(a, 0) + b`.
- Dividing by zero gives blank.
- A value can never fail a fill. If the formula can't be worked out, the cell is simply empty.
- A **Number** column whose value comes out as text (for example `'High'`) is blank.
- A choice field gives its stored **value** (for example `cast_iron`), not its label.
- A yes/no field gives `true` or `false`.

---

## 4. Conditions

A rule's conditions use the same editor as a field's visibility rules: pick a field, an operator and
a value. Choose **all** (every condition must hold) or **any** (one is enough).

| Operator | Holds when the answer … |
|---|---|
| equal / not equal | is / isn't exactly the value (text comparison) |
| contains / starts with | contains / starts with the value |
| greater than / less than | is a number above / below the value |
| is empty / is not empty | is blank / has anything in it |

Tips:

- **Yes/no:** compare with `true` or `false`, e.g. *is_safety_hazard equal true*.
- **Choices:** compare with the option's **value**, not its label, e.g. *pipe_material equal cast_iron*.
- **Order matters.** The first rule whose conditions hold wins, so put the most specific rule first:
  *Critical* before *High* before *Medium*.
- **A rule needs at least one condition.** For a value that always applies, use **Value / Otherwise**.

---

## 5. Recipes

| Want | Output | Rules → then | Otherwise / Value |
|---|---|---|---|
| Severity band | Text | `leak_rate_lph > 500` → `'High'`; `leak_rate_lph > 100` → `'Medium'` | `'Low'` |
| Hazard overrides everything | Text | first rule: `is_safety_hazard = true` → `'Critical'` | … |
| Water lost in m³ | Number | — | `round(leak_rate_lph * hours_leaking / 1000, 2)` |
| Difference only when both readings exist | Number | `meter_start` not empty **and** `meter_end` not empty → `meter_end - meter_start` | *(blank)* |
| Total of optional parts | Number | — | `sum(labour_cost, material_cost, other_cost)` |
| Average of two readings | Number | — | `round((reading_1 + reading_2) / 2, 1)` |
| Pass / fail against a limit | Text | `pressure_bar < 1.5` → `'Fail'` | `'Pass'` |
| Readable summary | Text | — | `concat(pipe_material, ' / ', diameter_mm, ' mm')` |
| Escalate on either signal | Text | **any**: `customers_affected > 50`, `is_safety_hazard = true` → `'Immediate'` | `'Scheduled'` |

---

## 6. How it behaves

- **When values are worked out.** Each time a form on a task is filled, the server works out that
  form's columns from the fill's answers, using the form version that fill answered. Filling the form
  again replaces them. Nothing is recalculated in the grid or in the browser.
- **Versions.** The columns are part of the form document, so they are versioned with it:
  - Editing them and publishing affects **fills made after that**.
  - Tasks already filled keep the values their fill produced.
  - An unfilled task pinned to an older version uses that version's columns when filled. Move the task to the new version first (the ↗ button in the grid) if it should use the new columns.
- **Several forms on one task.** Each form has its own columns. The grid shows them side by side,
  with the form's code in the header tooltip.
- **What the grid shows.**
  - Filtered by a task type: that type's forms' columns marked *Show in the task grid*.
  - Not filtered: no computed columns until you pick some in the **Computed columns** picker, which lists every type's.
  - Your choice is remembered per task type in your browser.
  - A task without a value shows `—`.
- **Sorting** works on any computed column: numbers by value, text alphabetically. Blanks sort
  together.
- **Filtering** by a computed column is not available yet (see [improvements](#9-how-to-improve-it)).

---

## 7. When something is refused

The designer checks everything before **Apply**. The server checks again on **Save** and **Publish**
and answers `FormEngine.Schema.InvalidComputedColumn` with a message per problem:

| Message says | Fix |
|---|---|
| the key must start with a lower-case letter… | Click **Change** and use e.g. `water_lost`. |
| another column has the same key | Each key must be unique within the form. |
| needs an English and an Arabic title | Fill in both titles. |
| needs a rule or a value | Add a rule, or type a **Value**. |
| rule n needs at least one condition | Set its conditions, or move its value to **Otherwise**. |
| … is not a field of the form | A field was renamed or deleted. Pick it again. |
| … ends too early / … is not expected here (character n) | A typo in the formula, at that position: a missing `)`, a stray symbol, or text without quotes. |
| … is not a known function | Only `sum min max round abs coalesce concat` exist. |

---

## 8. For developers

| Piece | Where |
|---|---|
| Schema shape (`computed_columns` at the document root) | `docs/form-engine.md` §12, `frontend/src/app/shared/form-schema/form-schema.types.ts` |
| Expression language: parse and evaluate (server, the authority) | `FormEngine.Application/Common/Schema/Expressions/FormExpression.cs` |
| Expression syntax check (designer only) | `frontend/src/app/shared/form-schema/form-computed-expression.ts` |
| Picking a value from the rules | `FormComputedColumnEvaluator.cs` |
| Save and publish checks | `FormComputedColumnValidator.cs`, `SaveFormSchemaCommandHandler`, `FormPublisher` |
| Gateway for other modules | `IFormGateway.ComputeAsync` and `GetComputedColumnsAsync` |
| Stored values | table `Task.TaskComputedValues`, written by `SubmitTaskFillCommand` |
| Grid data | `GET /api/v1/tasks/computed-columns?taskTypeId=`, `TaskListItemDto.ComputedValues`, `sortField=computed:{formId}:{key}` |
| Designer dialog | `features/form-engine/builder/components/computed-columns-dialog.component.ts` |
| Demo seed | `FormEngine.Infrastructure/Persistence/Seed/Forms/leak-inspection-computed.json`, `TaskSeedData.cs`, `AuthLookupSeedData.cs` |
| Tests | `FormComputedColumnTests`, `FormSeedDataTests`, `TaskComputedColumnTests`, `form-computed-expression.spec.ts` |

Limits: 20 columns per form, 20 rules per column, 500 characters and 32 nesting levels per
expression, and 400 characters of stored text.

---

## 9. How to improve it

Ordered roughly by value for the effort.

### Make authoring easier

1. **Live preview in the designer.** A "Try it" panel where the author types sample answers and sees
   each column's value. The syntax checker is already ported to TypeScript; porting the evaluator as
   well, or adding a `POST /forms/{id}/computed-preview` endpoint that calls the server evaluator,
   gives exact results.
2. **Starter templates.** "Band by thresholds", "Difference of two fields", "Total of fields" and
   "Pass/fail against a limit" would fill in the rules from a couple of pickers, so most columns need
   no typing.
3. **An `if()` function.** `if(leak_rate_lph > 500, 'High', 'Low')` lets short columns skip the rules
   editor. The parser needs comparison operators for it.
4. **Choice labels.** A `label(pipe_material)` function returning the option's label in the reader's
   language, instead of the stored value.
5. **Autocomplete in the value box**, for field names and functions.

### Make values more useful

6. **Recompute on demand.** An admin action, "Recalculate computed columns for this form", that
   re-reads each task's latest fill and works the values out again with the current version. It is
   useful after fixing a formula. Everything it needs exists: `GetLatestByContextAsync`,
   `ComputeAsync` and `TaskForm.ReplaceComputed`.
7. **Filter by a computed column** in the grid: equals/contains for text, a range for numbers. The
   values are already in `Task.TaskComputedValues` with indexes on `(FormDefinitionId, Key)`.
8. **Show them in more places:** the task details screen (per form), the PDF report and the
   Excel/CSV export.
9. **Formatting:** decimals, a unit suffix (`m³`), and colour rules ("High" in red) declared on the
   column and applied in the grid.
10. **Columns referring to other columns:** e.g. a *Response* built from *Severity*. Evaluate the
    columns in order and pass earlier results in as named values; the validator must refuse cycles.
11. **Dates:** functions such as `days_between(start_date, end_date)` and `hours_since(reported_at)`.

### Make it fit teams better

12. **Grid layouts saved on the server**, per user or per team, instead of per browser.
13. **Columns declared on the task type** that combine answers from several of its forms.
14. **Dashboards:** counts and sums of a computed column by branch, team or week. The values are
    already stored as typed numbers and text, ready to aggregate.
