import {
  TASK_STATUSES,
  TaskStatus,
  canMigrateVersion,
  canReassign,
  canRunTaskAction,
  isOverdue,
  taskPrioritySeverity,
  taskStatusSeverity,
  wasSubmittedAgain,
} from './task-status';

/**
 * The menu only offers what the server's `FieldTask` guards would accept. These pin the client's
 * copy of those rules, so a drift shows up here rather than as a refused click.
 */
describe('task-status', () => {
  describe('canRunTaskAction', () => {
    it('accepts a fill until the task is approved or expired', () => {
      const fillable = TASK_STATUSES.filter((status) => canRunTaskAction('fill', status));

      expect(fillable).toEqual([
        TaskStatus.Created,
        TaskStatus.Assigned,
        TaskStatus.InProgress,
        TaskStatus.Submitted,
        TaskStatus.Returned,
      ]);
    });

    it('reviews only a filled task', () => {
      for (const status of TASK_STATUSES) {
        expect(canRunTaskAction('complete', status)).withContext(status).toBe(status === TaskStatus.Submitted);
        expect(canRunTaskAction('return', status)).withContext(status).toBe(status === TaskStatus.Submitted);
      }
    });

    it('offers nothing on a closed task', () => {
      for (const action of ['assign', 'fill', 'complete', 'return', 'expire', 'edit'] as const) {
        expect(canRunTaskAction(action, TaskStatus.Approved)).withContext(action).toBeFalse();
        expect(canRunTaskAction(action, TaskStatus.Expired)).withContext(action).toBeFalse();
      }
    });

    it('offers nothing when the status is unknown', () => {
      expect(canRunTaskAction('assign', null)).toBeFalse();
      expect(canRunTaskAction('assign', undefined)).toBeFalse();
      expect(canRunTaskAction('assign', 'UNDER_REVIEW')).toBeFalse();
    });
  });

  describe('canReassign', () => {
    it('allows moving an unfilled task', () => {
      expect(canReassign(TaskStatus.Created, 0)).toBeTrue();
      expect(canReassign(TaskStatus.Assigned, 0)).toBeTrue();
      expect(canReassign(TaskStatus.InProgress, null)).toBeTrue();
    });

    it('locks a task to the crew once anything is filled', () => {
      expect(canReassign(TaskStatus.Assigned, 1)).toBeFalse();
      expect(canReassign(TaskStatus.Submitted, 0)).toBeFalse();
      expect(canReassign(TaskStatus.Returned, 1)).toBeFalse();
    });
  });

  describe('canMigrateVersion', () => {
    const task = { status: TaskStatus.Assigned, submissionCount: 0, formVersionNo: 2, formCurrentVersionNo: 3 };

    it('offers a newer version to an unfilled task', () => {
      expect(canMigrateVersion(task)).toBeTrue();
    });

    it('offers nothing when the pinned version is current, or the form is gone', () => {
      expect(canMigrateVersion({ ...task, formCurrentVersionNo: 2 })).toBeFalse();
      expect(canMigrateVersion({ ...task, formCurrentVersionNo: null })).toBeFalse();
    });

    it('keeps a filled task on the version its answers were given against', () => {
      expect(canMigrateVersion({ ...task, submissionCount: 1 })).toBeFalse();
    });
  });

  describe('wasSubmittedAgain', () => {
    it('is true when the latest fill came after the return', () => {
      expect(wasSubmittedAgain({ returnedDate: '2026-09-01T08:00:00Z', submittedDate: '2026-09-02T08:00:00Z' })).toBeTrue();
    });

    it('is false while the rework is outstanding, or when never returned', () => {
      expect(wasSubmittedAgain({ returnedDate: '2026-09-02T08:00:00Z', submittedDate: '2026-09-01T08:00:00Z' })).toBeFalse();
      expect(wasSubmittedAgain({ returnedDate: null, submittedDate: '2026-09-01T08:00:00Z' })).toBeFalse();
    });
  });

  describe('isOverdue', () => {
    const now = new Date('2026-09-21T12:00:00Z').getTime();
    const past = '2026-09-20T12:00:00Z';

    it('flags an open task past its fill deadline', () => {
      expect(isOverdue({ status: TaskStatus.Assigned, dueDate: past }, now)).toBeTrue();
      expect(isOverdue({ status: TaskStatus.Returned, dueDate: past }, now)).toBeTrue();
    });

    it('does not flag a filled or closed task, a future deadline, or no deadline', () => {
      expect(isOverdue({ status: TaskStatus.Submitted, dueDate: past }, now)).toBeFalse();
      expect(isOverdue({ status: TaskStatus.Approved, dueDate: past }, now)).toBeFalse();
      expect(isOverdue({ status: TaskStatus.Expired, dueDate: past }, now)).toBeFalse();
      expect(isOverdue({ status: TaskStatus.Assigned, dueDate: '2026-09-22T12:00:00Z' }, now)).toBeFalse();
      expect(isOverdue({ status: TaskStatus.Assigned, dueDate: null }, now)).toBeFalse();
    });
  });

  describe('severities', () => {
    it('colours each status by where it sits in the lifecycle', () => {
      expect(taskStatusSeverity(TaskStatus.Created)).toBe('secondary');
      expect(taskStatusSeverity(TaskStatus.Assigned)).toBe('info');
      expect(taskStatusSeverity(TaskStatus.Submitted)).toBe('warn');
      expect(taskStatusSeverity(TaskStatus.Approved)).toBe('success');
      expect(taskStatusSeverity(TaskStatus.Returned)).toBe('danger');
      expect(taskStatusSeverity(TaskStatus.Expired)).toBe('contrast');
    });

    it('treats an unknown priority as normal', () => {
      expect(taskPrioritySeverity('URGENT')).toBe('danger');
      expect(taskPrioritySeverity(null)).toBe(taskPrioritySeverity('NORMAL'));
    });
  });
});
