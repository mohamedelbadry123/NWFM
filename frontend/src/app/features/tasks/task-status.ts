/**
 * Task lifecycle vocabulary, mirrored from the API's `TaskStatuses` / `TaskPriorities` /
 * `TaskSources` / `TaskReturnReasons`. The API stores these exact strings; the UI labels them through
 * `tasks.status.*`, `tasks.priority.*`, `tasks.source.*` and `tasks.returnReason.*`.
 */
export const TaskStatus = {
  Created: 'CREATED',
  Assigned: 'ASSIGNED',
  InProgress: 'IN_PROGRESS',
  Submitted: 'SUBMITTED',
  Approved: 'APPROVED',
  Returned: 'RETURNED',
  Expired: 'EXPIRED',
} as const;

export type TaskStatusValue = (typeof TaskStatus)[keyof typeof TaskStatus];

export const TASK_STATUSES: readonly TaskStatusValue[] = [
  TaskStatus.Created,
  TaskStatus.Assigned,
  TaskStatus.InProgress,
  TaskStatus.Submitted,
  TaskStatus.Approved,
  TaskStatus.Returned,
  TaskStatus.Expired,
];

export const TaskPriority = {
  Low: 'LOW',
  Normal: 'NORMAL',
  High: 'HIGH',
  Urgent: 'URGENT',
} as const;

export const TASK_PRIORITIES: readonly string[] = [
  TaskPriority.Low,
  TaskPriority.Normal,
  TaskPriority.High,
  TaskPriority.Urgent,
];

export const TASK_SOURCES: readonly string[] = ['MANUAL', 'API', 'IMPORT'];

export const TaskReturnReason = {
  IncompleteData: 'INCOMPLETE_DATA',
  WrongLocation: 'WRONG_LOCATION',
  PoorMedia: 'POOR_MEDIA',
  NeedsRevisit: 'NEEDS_REVISIT',
  Other: 'OTHER',
} as const;

export const TASK_RETURN_REASONS: readonly string[] = [
  TaskReturnReason.IncompleteData,
  TaskReturnReason.WrongLocation,
  TaskReturnReason.PoorMedia,
  TaskReturnReason.NeedsRevisit,
  TaskReturnReason.Other,
];

export type TaskTagSeverity = 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast';

/** Colour of the status tag. Terminal-good is green, terminal-bad grey, in-flight blue/amber. */
export function taskStatusSeverity(status?: string | null): TaskTagSeverity {
  switch (status) {
    case TaskStatus.Assigned:
    case TaskStatus.InProgress:
      return 'info';
    case TaskStatus.Submitted:
      return 'warn';
    case TaskStatus.Approved:
      return 'success';
    case TaskStatus.Returned:
      return 'danger';
    case TaskStatus.Expired:
      return 'contrast';
    default:
      return 'secondary';
  }
}

export function taskPrioritySeverity(priority?: string | null): TaskTagSeverity {
  switch (priority) {
    case TaskPriority.Urgent:
      return 'danger';
    case TaskPriority.High:
      return 'warn';
    case TaskPriority.Low:
      return 'secondary';
    default:
      return 'info';
  }
}

/**
 * Colour of the return-reason tag. A returned task is already flagged red by its status, so the
 * reason sits beside it in a shade that says what kind of problem it is: amber for "fix what you
 * sent", red for "go back to site".
 */
export function taskReturnReasonSeverity(reasonCode?: string | null): TaskTagSeverity {
  switch (reasonCode) {
    case TaskReturnReason.IncompleteData:
    case TaskReturnReason.PoorMedia:
      return 'warn';
    case TaskReturnReason.WrongLocation:
    case TaskReturnReason.NeedsRevisit:
      return 'danger';
    default:
      return 'secondary';
  }
}

/** The lifecycle actions the API will accept for a task in this status. */
export type TaskAction = 'assign' | 'fill' | 'complete' | 'return' | 'expire' | 'edit';

const ACTION_PRECONDITIONS: Record<TaskAction, readonly string[]> = {
  // Only an unfilled task may move to another team — see `canReassign`, which also checks the fill
  // count; these three statuses are simply the ones that can carry no fill.
  assign: [TaskStatus.Created, TaskStatus.Assigned, TaskStatus.InProgress],
  // Everything up to approval accepts a fill; an approved or expired task is closed.
  fill: [TaskStatus.Created, TaskStatus.Assigned, TaskStatus.InProgress, TaskStatus.Submitted, TaskStatus.Returned],
  // A reviewer acts straight on a filled task.
  complete: [TaskStatus.Submitted],
  return: [TaskStatus.Submitted],
  // A correction the back office can apply at any open point; a finished record is off limits.
  expire: [TaskStatus.Created, TaskStatus.Assigned, TaskStatus.InProgress, TaskStatus.Submitted, TaskStatus.Returned],
  edit: [TaskStatus.Created, TaskStatus.Assigned, TaskStatus.InProgress, TaskStatus.Submitted, TaskStatus.Returned],
};

/**
 * Whether the domain would accept this transition. The server guards it regardless — this only keeps
 * the menu from offering a click that is guaranteed to come back refused.
 */
export function canRunTaskAction(action: TaskAction, status?: string | null): boolean {
  return !!status && ACTION_PRECONDITIONS[action].includes(status);
}

/**
 * Whether the task can still be handed to a (different) team, or moved: a filled task is locked to
 * the crew that collected the answers, so the fill count decides it as much as the status does.
 */
export function canReassign(status?: string | null, submissionCount?: number | null): boolean {
  return canRunTaskAction('assign', status) && (submissionCount ?? 0) === 0;
}

/** A newer form version can be pinned only while nothing has been filled against the current one. */
export function canMigrateVersion(task: {
  status?: string | null;
  submissionCount?: number | null;
  formVersionNo: number;
  formCurrentVersionNo: number | null;
}): boolean {
  return canReassign(task.status, task.submissionCount)
    && task.formCurrentVersionNo !== null
    && task.formCurrentVersionNo > task.formVersionNo;
}

/**
 * True when the latest fill landed after a return — the crew delivered rework. The fill refreshes
 * `submittedDate`; a return stamps `returnedDate`.
 */
export function wasSubmittedAgain(task: { submittedDate?: string | null; returnedDate?: string | null }): boolean {
  if (!task.submittedDate || !task.returnedDate) {
    return false;
  }

  return new Date(task.submittedDate).getTime() > new Date(task.returnedDate).getTime();
}

/** Past its fill deadline and not yet filled or closed. */
export function isOverdue(task: { status?: string | null; dueDate?: string | null }, now = Date.now()): boolean {
  if (!task.dueDate) {
    return false;
  }

  const closedOrFilled = task.status === TaskStatus.Submitted
    || task.status === TaskStatus.Approved
    || task.status === TaskStatus.Expired;

  return !closedOrFilled && new Date(task.dueDate).getTime() < now;
}
