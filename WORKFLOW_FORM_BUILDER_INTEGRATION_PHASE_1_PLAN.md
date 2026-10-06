# Workflow Form Builder Integration Phase 1 Plan

This plan covers Tasks 1 and 2: organization scope and workflow type selections when creating main and child workflow definitions. It records the existing implementation, the requested behavior, and the changes needed across creation, the designer, validation, and execution.

Target branch: `Workflow-Form-Builder-Integration-Phase-1`.

Reviewed on 2026-09-30 at commit `9b6ed68`. The branch was already checked out and the working tree was clean before this plan was added. Implementation was subsequently approved and completed on this branch. The baseline section below records the behavior before these changes.

## Confirmed requirements

Creation-form visibility is explicit: **Activity Type appears only while creating a Main workflow. Task Types appears only while creating a Child workflow.** These two workflow-level dropdowns must never appear together in the creation form. This rule does not change existing activity-node editors.

| Setting | Creating a Main workflow | Creating a Child workflow |
| --- | --- | --- |
| Organization scope | Cluster, CBU, Branch, Operation Area | The same four selectors |
| Organization selection | Multiple selections in each dropdown | Multiple selections in each dropdown |
| Required depth | Allow stopping at Cluster, CBU, or the applicable lower level | The same partial-depth behavior |
| Activity Type | Visible; select from Lookups > Field Activity Types | Hidden |
| Task Types | Hidden | Visible; select from Lookups > Task Types |

The user explicitly confirmed multiple selections per organization dropdown and preservation of the existing lookup hierarchy:

- Cluster contains CBUs.
- CBU contains Branches and Operation Areas.
- Branch and Operation Area are siblings. Selecting an Operation Area must not require selecting a Branch.

This request concerns workflow definition creation and its editable settings. Existing activity-node settings, form bindings, and SLA configuration are separate features; any change to their behavior needs a stated requirement.

## Baseline before implementation

| Capability | Current implementation | Required change |
| --- | --- | --- |
| Main workflow organization selectors | All four selectors exist, each selecting one value. Both Cluster and CBU are required. | Support multiple values and permit Cluster-only scope. |
| Shared organization selector | Has single-location and add-scope-row modes. Even its current multi mode uses single-value dropdowns. | Provide actual multiple selection within each of the four dropdowns. Merely switching to the existing multi mode is insufficient. |
| Child workflow organization scope | Creation hides the selector and the server discards submitted child location fields. Publication rejects child location fields. Execution inherits the parent location. | Show and persist child definition scope, validate it, and define its runtime effect. |
| Field Activity Types | Lookup records and an active workflow lookup endpoint exist. Types belong to departments. The designer uses them on activity nodes. | Add the separate main-workflow Activity Type selector and persist its selection in workflow settings. |
| Task Types | The Lookups page, Tasks module, frontend service, and active-list endpoint already exist. | Add the child-workflow selector, workflow-facing validation, and appropriate read access. |
| Workflow persistence | Versioned workspace JSON contains kind and one organization location. | Introduce a scope collection and the two kind-specific type references with backward-compatible reading. |
| Runtime and access filtering | Instances carry one location. Catalog and instance access checks primarily match CBU, Branch, and Operation Area. | Handle multi-scope definitions and explicitly address Cluster-only execution and access. |

Key source locations:

- `frontend/src/app/features/workflow/workspace/workflow-workspace.component.ts`: creation form and client validation.
- `frontend/src/app/features/workflow/workspace/workflow-location.component.ts`: Main/Child selection and location controls; changing kind currently clears location.
- `frontend/src/app/features/workflow/workspace/workflow-workspace.service.ts`: settings contract and required-location checks.
- `frontend/src/app/shared/components/org-scope/org-scope-selector.component.ts` and `.html`: shared hierarchy controls.
- `frontend/src/app/features/workflow/designer/workflow-designer.component.ts` and `.html`: editing and round-tripping workspace settings.
- `backend/src/Modules/Workflow/Workflow.Domain/Entities/WorkflowWorkspaceDefinition.cs`: workspace JSON parsing and legacy adapters.
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowWorkspace.cs`: creation, catalog, starting, and workspace access.
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowWorkspacePublisher.cs`: publication and location validation.
- `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowRuntimeEngine.cs`: instance location and child inheritance.
- `backend/src/NWFM.Shared/Organization/OrgScopeSet.cs`, `OrgLocation.cs`, and `backend/src/Modules/Workflow/Workflow.Infrastructure/Services/WorkflowScopeFilter.cs`: scope matching.
- `backend/src/Modules/Auth/Auth.Infrastructure/Services/WorkflowReferenceData.cs`: existing Field Activity Type lookup adapter.
- `frontend/src/app/core/tasks/task-types.service.ts` and `backend/src/Modules/Tasks/Tasks.Api/Controllers/TaskTypesController.cs`: existing Task Type catalog.

## Task 1 Organization scope

### Selection behavior

1. Show four searchable multiselect dropdowns with selected-value chips and English/Arabic labels.
2. Require at least one Cluster for new definitions. CBU, Branch, and Operation Area are optional; this minimum is the planning interpretation of stopping at level one.
3. Populate CBU choices from the union of selected Clusters. Populate Branch and Operation Area choices independently from the union of selected CBUs.
4. Show parent names where needed to distinguish similarly named options from different Clusters or CBUs.
5. Removing a parent removes only its now-invalid descendants. Preserve selections under parents that remain selected.
6. Ignore stale lookup responses after a parent changes. Display loading, empty, and error states separately.
7. Read all relevant lookup pages. The current `listAll` helper only fetches the first 500 rows, so it must not silently truncate multiselect choices.
8. Reuse lookup services and hierarchy rules. Add a dedicated workflow scope editor or an explicitly separate shared-selector mode so existing user/team scope screens keep their current behavior.

### Implemented meaning of partial selections

The approved plan uses these scope semantics:

- A Cluster with no selected CBUs covers that Cluster. A selected CBU with no selected Branches or Operation Areas covers that CBU.
- More detailed choices narrow only their own parent. For example, selecting Clusters A and B and CBU A1 means A1 plus all of B, not every CBU in A.
- Selected Branches and Operation Areas form a union of eligible units under their CBUs. Do not invent a Branch-to-Operation-Area relationship or create a Cartesian product of unrelated selections.
- Display a readable scope summary so users can see which parents remain broad and which are narrowed.

Store normalized scope entries with explicit level, code, and resolvable ancestry rather than reusing scalar location fields for arrays. Use the owning lookup records to validate ancestry on the server. Treat an empty workflow scope as invalid, even though the existing user-coverage model treats no rows as unrestricted.

## Task 2 Workflow type selections and child scope

### Main workflow Activity Type

- Display this dropdown only when the creation form is set to **Main workflow**. Do not display Task Types in this form.
- Add a required, single-choice dropdown labeled **Activity Type**, populated from active Field Activity Types whose departments are active.
- Use the lookup record ID as the selection identity. Field Activity Type codes are department-qualified; code alone must not be treated as globally unique.
- Include the department in the option label and derive its code from the selected record, avoiding an additional required Department dropdown in creation.
- Persist the selected reference and its department-qualified identity in versioned settings. Validate lookup existence, activity status, and department consistency on the server.
- Preserve the selection when opening and saving the designer and when creating a subsequent draft.

### Child workflow Task Types

- Display this dropdown only when the creation form is set to **Child workflow**. Do not display Activity Type in this form.
- Show the same organization multiselect editor as for a main workflow.
- Add a required dropdown labeled **Task Types**, sourced from active Task Types in Lookups. The planning default is one Task Type per child workflow; the user's multiple-selection confirmation applies to organization dropdowns.
- Hide the workflow-level Activity Type selector when Child is selected. Hide Task Types when Main is selected.
- Persist the Task Type ID. Do not hardcode choices, create a duplicate catalog, or assume Task Types and Field Activity Types have a one-to-one relationship.
- Use a Workflow-facing read/validation interface implemented by the Tasks module. Keep Workflow independent of the Tasks database context.
- Provide read-only catalog access to authorized workflow designers. The current Task Type reader policy accepts task-view or task-type-management permissions, so a designer-only account can otherwise receive an authorization error. Reading choices must not grant permission to manage Task Types.
- Selecting a Task Type records workflow classification in this scope of work. It does not by itself authorize automatically creating Tasks records, changing activity forms, or applying task SLAs.

### Switching Main and Child

When changing kind in the creation form, immediately swap the visible type dropdown: Main shows Activity Type; Child shows Task Types. Keep the organization scope and workflow name. Clear only the incompatible type reference, then require a valid selection for the new kind. The server must enforce the same kind-specific contract, including direct API submissions.

## Definition scope and instance execution

Multiple definition scopes cannot safely be stored as the existing single instance location. The implementation must establish this distinction before changing the runtime.

Implemented execution behavior:

1. Definition scope describes where a workflow can be used. It can contain several organization units at different depths.
2. Each execution continues to have one immutable organization location. When the definition permits multiple locations, select one permitted location at start; prefill only when unambiguous. Do not silently choose the first value or start several instances.
3. Permit an execution at Cluster-only or CBU-only depth where the applicable workflow scopes allow that depth. Add explicit Cluster matching to both in-memory and database access checks. A user assigned to only one lower unit must not automatically gain access to a whole-Cluster execution.
4. A child definition stores its own eligibility scope. Its execution inherits the actual parent-instance location only when that location is allowed by the child's scope. A broadly reusable child may be used by different parents; it need not have its entire definition scope contained by each parent definition.
5. Validate parent/child compatibility when publishing pinned child references and again when starting an execution. Reject incompatible locations before creating tasks. Account for nested children and applicable execution paths; do not rely only on a nonempty scope overlap that still allows an unsupported start location.
6. Apply the same rules to catalog visibility, start requests, instance lists and details, task actions, and direct runtime entry points. Include the chosen location in start-request idempotency comparison.

Each instance retains one execution location, and children inherit it. The eligible scope is checked against the pinned descendant tree before work starts. Publication requires a common eligible unit across that tree; incompatible sibling Branch/Operation Area constraints are rejected rather than inventing a relationship between those lookup levels.

## Implementation sequence

1. **Contracts and compatibility.** Add a settings schema version separate from `designerVersion`, which already controls designer behavior. Define scope entries and the Main Activity Type / Child Task Type references. Update frontend contracts, backend readers, and serialization together.
2. **Organization editor.** Implement the four true multiselects, parent filtering, partial-depth validation, pruning, and readable scope summary. Use the same editor in creation and designer settings.
3. **Type lookups.** Connect existing Field Activity Types and Task Types, including department-qualified identity, read permissions, loading states, and server validation.
4. **Creation and publication.** Stop discarding child scope; replace the old CBU-required and child-location-prohibited rules. Enforce kind-specific type fields. Save and reload all selected values without data loss.
5. **Execution integration.** Implement the agreed definition/instance distinction, child compatibility, Cluster-only access, start validation, and idempotency behavior. Update any affected contract generation and existing demo/setup code.
6. **Verification and documentation.** Run focused unit/integration checks, build both applications, and exercise the flows in English and Arabic. Update current workflow documentation to explain scope and type selection.

No database migration is required. New definition settings use workspace JSON schema version 2, while instance location remains in the existing execution-context fields. The settings schema version is independent of the designer version.

## Existing workflow compatibility

- Keep published workspace JSON and content hashes unchanged. Apply new metadata through drafts and publication rather than rewriting published versions.
- Continue reading legacy `regionCode` and `cityCode` through the existing CBU/Branch adapter, preserving conflict detection.
- Preserve the original behavior of old scalar-location definitions and running instances; do not convert a legacy location into a broader union of scopes silently.
- Existing child definitions without scope retain their legacy inheritance behavior. New definitions require the new fields; upgrading an old draft requires completing its settings before publication.
- Existing published workflows missing the new type references remain usable under their recorded schema. Display missing/inactive selections clearly during editing; never silently replace them with another lookup record.
- Keep per-activity Department/Field Activity Type configuration, forms, and SLA resolution intact unless a later task explicitly changes their relationship to workflow-level classification.

## Acceptance checks

| Area | Expected result |
| --- | --- |
| Partial scope | A new Main or Child can be created with one or several Clusters and no lower selections. |
| Several parents | Selecting several Clusters and CBUs shows only eligible descendants across those parents. |
| Sibling levels | Operation Area works with Cluster and CBU selected and no Branch. Branch and Operation Area selections remain independent. |
| Parent removal | Removing one Cluster/CBU clears its descendants while preserving valid selections elsewhere. |
| Mixed depth | A narrowed scope under one parent and a broad scope under another save and reload with the intended meaning. |
| Main creation | Only Activity Type appears as the type selector; Task Types is hidden. The selected Field Activity Type is persisted correctly. |
| Child creation | Only Task Types appears as the type selector; Activity Type is hidden. Organization scope is visible and both selections are persisted correctly. |
| Kind switching | The type dropdown changes immediately, the two type selectors never appear together, scope survives, and incompatible type data is cleared. |
| Lookup identity | Identical Field Activity Type codes in different departments resolve to the correct records. |
| Invalid input | Missing required scope/type, inactive or unknown lookups, invalid parent combinations, and kind-incompatible type fields are rejected server-side. |
| Catalog permissions | A workflow designer can read the necessary choices without gaining lookup-management permissions. |
| Persistence | Create, reopen, edit, save, publish, and create a new draft preserve all selections. |
| Execution | The agreed location resolution is enforced for root and pinned child workflows, including Cluster-only coverage and repeated start requests. |
| Isolation | Unauthorized users cannot see, start, or act on workflows outside their permitted scope; SQL and in-memory checks agree. |
| Regression | Existing published workflows, running instances, legacy children, node classifications, forms, and SLA behavior continue to work. |
| Usability | Keyboard selection, Arabic layout, long labels, empty catalogs, lookup failures, and rapid parent changes are handled. |

## Verification results

- Backend suite: 682 tests passed, including new scope, classification, draft-preservation, runtime-location, and Cluster-only access checks.
- Focused frontend suite: 136 tests passed across workflow, organization selectors, and lookup pagination.
- Backend solution and frontend development/production builds passed. The production frontend reports an initial-bundle budget warning (770.28 kB against 500 kB); backend builds report the existing NU1510 package warning.
- Browser checks in English and Arabic verified Main/Child dropdown visibility, switching kind while preserving scope, multiple Clusters without CBU, and multiple Operation Areas without Branch.
- Created and reopened one Main and one Child test draft; API reads confirmed their exact saved scope and classification. Saved the Main designer again and verified persistence. Both temporary test definitions were then deactivated.
- Subsequent draft creation copies published workspace settings without modifying the published source. Legacy workflows retain their recorded behavior until explicitly upgraded in the designer.

Tasks 1 and 2 are implemented. Further tasks can be added separately.

## Task 3 — Task Type palette and exact child selection (implemented)

After Tasks 1 and 2, the user approved a Main-designer Task Types category and stricter child selection. Each active type is searchable and draggable (or keyboard/click addable) as a Main Activity, retaining its Task Type ID through canvas edits, undo/redo, duplication, and XML persistence.

The child selector matches the activity Task Type and the entire saved Main organization scope. Scope ordering and duplicates do not matter; level, unit and ancestry do. Central Cluster / RCBU therefore excludes a child scoped only to Central Cluster. Existing choices that become incompatible remain flagged for review, and draft publication rejects the mismatch. New Main draft Call Activity references must also match organization scope. Previously published workflows continue to use their original pinned children and execution behavior. Existing Main drafts require upgraded scope and activity Task Type selections before republishing.

Verification: 690 backend tests and 130 workflow frontend tests passed. The production build passed with the same 770.28 kB initial-bundle budget warning. Live English/Arabic browser checks verified dragging Field Survey, exact RCBU filtering against a broader Central Cluster child, Task Type change warnings, and save/reopen persistence. The three temporary test definitions were deactivated afterward.
