import { TaskListQuery } from '../../../core/tasks/tasks.models';
import { OrgLocation } from '../../../shared/components/org-scope/org-scope.model';

/** PrimeNG column fields → the API's sort fields. Anything else sorts newest first. */
export const TASK_SORT_FIELDS: Readonly<Record<string, string>> = {
  taskNumber: 'taskNumber',
  status: 'status',
  priority: 'priority',
  createdAt: 'createdAt',
  dueDate: 'dueDate',
  submissionCount: 'submissionCount',
};

/** What the worklist's caption and paginator hold at the moment it loads. */
export interface TaskListState {
  readonly page: number;
  readonly pageSize: number;
  readonly search: string;
  readonly status: string | null;
  readonly taskTypeId: string | null;
  readonly priority: string | null;
  readonly source: string | null;
  readonly returnReasonCode: string | null;
  readonly createdFrom: Date | null;
  readonly createdTo: Date | null;
  readonly orgFilter: OrgLocation;
  /** The column PrimeNG reports, not yet mapped to the API's name. */
  readonly sortField: string | null;
  /** PrimeNG's `1` ascending / `-1` descending. */
  readonly sortOrder: number;
}

/**
 * The list request for the worklist's current state. The status picker is single-select but the API
 * takes a set; the org filter sends each level it holds and the server narrows to the finest; an
 * unknown sort column falls back to the server's newest-first default.
 */
export function buildTaskListQuery(state: TaskListState): TaskListQuery {
  return {
    pageNumber: state.page,
    pageSize: state.pageSize,
    search: state.search,
    statuses: state.status ? [state.status] : null,
    taskTypeId: state.taskTypeId,
    priority: state.priority,
    source: state.source,
    returnReasonCode: state.returnReasonCode,
    clusterCode: state.orgFilter.clusterCode,
    cbuCode: state.orgFilter.cbuCode,
    branchCode: state.orgFilter.branchCode,
    operationAreaCode: state.orgFilter.operationAreaCode,
    createdFrom: toDateParam(state.createdFrom),
    createdTo: toDateParam(state.createdTo),
    sortField: state.sortField ? TASK_SORT_FIELDS[state.sortField] ?? null : null,
    sortDescending: state.sortOrder !== 1,
  };
}

/** A picked day as `yyyy-MM-dd`, local — the API treats a bare day as the whole of it. */
export function toDateParam(date: Date | null): string | null {
  if (!date) {
    return null;
  }

  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
