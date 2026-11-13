# Workflow assignment to account roles

The designer's Assignment tab selects multiple existing identity roles for Task Types and Steps.
This does not create roles or change anyone's account roles or permission grants.

The selected role IDs are stored in the activity's versioned `configurationJson.assignmentRoleIds`.
Choosing roles replaces that activity's legacy assignment group, assignment key and group fallback.
Saved group assignments remain visible until the designer explicitly replaces them. Published
versions remain read only.

Human tasks create one work item with Role candidates and eligible participant snapshots.
The existing non-null `AssignmentGroupId` is `Guid.Empty` for role-assigned work; Role candidates
are the source of assignment identity. No synthetic assignment group or schema migration is needed.
Legacy group tasks keep their original representation and membership rules.

Any selected role qualifies a user; all roles are not required. The account must still be enabled,
the user must be an active workflow participant in the task's tenant, and normal endpoint permissions
and organization scope checks still apply. Claiming uses the existing optimistic concurrency guard.
Role membership is read again for claiming, completing, delegation and workspace actions, so candidate
snapshots cannot retain authorization after a role is revoked. Task Type containers in hierarchical
workflows still advance automatically after their child workflow; selecting roles does not add a
new parent approval task. Each human Step has its own assignment.

Validation rejects unavailable, duplicate, malformed or excessive role IDs and mixed role/group
assignments. New role assignments are not a fallback to a broader group. The available and overdue
inboxes include eligible role tasks alongside legacy group tasks, and display the assigned role names.
