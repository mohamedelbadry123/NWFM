/** API shapes for field tasks and task types — the Tasks module's DTOs, one to one. */

export const TASKS_PATH = '/api/v1/tasks';
export const TASK_TYPES_PATH = '/api/v1/task-types';

export interface TaskListItem {
  id: string;
  taskNumber: string;
  taskTypeId: string;
  taskTypeCode: string | null;
  taskTypeNameEn: string | null;
  taskTypeNameAr: string | null;
  formDefinitionId: string;
  formVersionNo: number;
  /** Ahead of `formVersionNo` when a newer version was published since the task was raised. */
  formCurrentVersionNo: number | null;
  status: string;
  priority: string;
  source: string;
  title: string | null;
  externalReference: string | null;
  latitude: number;
  longitude: number;
  address: string | null;
  cbuCode: string | null;
  branchCode: string | null;
  operationAreaCode: string | null;
  departmentCode: string | null;
  assignedTeamId: string | null;
  assignedTeamName: string | null;
  dueDate: string | null;
  completionDueDate: string | null;
  assignedDate: string | null;
  submittedDate: string | null;
  submissionCount: number;
  returnReasonCode: string | null;
  returnReason: string | null;
  returnedDate: string | null;
  returnCount: number;
  /** The C2M field activity the task settles, if any. */
  faId: string | null;
  /** WFM's ticket for that activity — C2M's MOBId. */
  wfmTicketId: number | null;
  /** Where closing it in C2M stands (`C2M_CLOSURE_STATUSES`); null when the task closes nothing in C2M. */
  c2mStatus: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface TaskAssignment {
  id: string;
  teamId: string;
  teamName: string | null;
  status: string;
  assignedBy: string | null;
  assignedDate: string;
  dueDate: string | null;
  submittedDate: string | null;
  note: string | null;
  isActive: boolean;
}

export interface TaskDetail {
  task: TaskListItem;
  notes: string | null;
  fillSlaHours: number | null;
  completionSlaHours: number | null;
  assignedBy: string | null;
  lastFilledBy: string | null;
  completedBy: string | null;
  completedDate: string | null;
  returnedBy: string | null;
  expiredBy: string | null;
  expiredDate: string | null;
  createdBy: string | null;
  c2mAttempts: number;
  c2mLastAttemptAt: string | null;
  /** The task's type closes C2M field activities — with an FA id, approving it will. */
  typeClosesC2mActivity: boolean;
  formCode: string | null;
  formNameEn: string | null;
  formNameAr: string | null;
  /** The pinned version's form-builder document. */
  schemaJson: string | null;
  assignments: TaskAssignment[];
}

export interface TaskHistoryEntry {
  id: string;
  fromStatus: string | null;
  toStatus: string;
  changedBy: string | null;
  changedDate: string;
  note: string | null;
}

export interface TaskFill {
  submissionId: string;
  versionNo: number;
  submittedBy: string | null;
  submittedByName: string | null;
  submittedDate: string | null;
  /** As the form's table stores them — the renderer's `fromStoredAnswers` reads this shape. */
  answers: Record<string, unknown>;
  /** The same answers labelled and rendered by the server, in the form's order. Blank answers are left out. */
  display: TaskAnswerView[];
}

/** One answer ready to read: the question's labels and the answer in each language. */
export interface TaskAnswerView {
  dataName: string;
  fieldType: string;
  labelEn: string | null;
  labelAr: string | null;
  displayEn: string;
  displayAr: string;
  /** Set for a geolocation answer, so it can be shown on a map. */
  point: { latitude: number; longitude: number; address: string | null } | null;
}

export interface TaskFile {
  fileId: string;
  submissionId: string | null;
  dataName: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status: string;
  createdAt: string;
}

export interface EligibleTeam {
  teamId: string;
  name: string;
  mobile: string | null;
  activeTaskCount: number;
}

export interface TaskFillResult {
  submissionId: string;
  versionNo: number;
  isReplay: boolean;
  status: string;
}

export interface TaskListQuery {
  pageNumber: number;
  pageSize: number;
  search?: string | null;
  statuses?: string[] | null;
  taskTypeId?: string | null;
  source?: string | null;
  priority?: string | null;
  clusterCode?: string | null;
  cbuCode?: string | null;
  branchCode?: string | null;
  operationAreaCode?: string | null;
  departmentCode?: string | null;
  teamId?: string | null;
  returnReasonCode?: string | null;
  createdFrom?: string | null;
  createdTo?: string | null;
  overdueOnly?: boolean;
  sortField?: string | null;
  sortDescending?: boolean;
}

export interface TaskLocationPayload {
  latitude: number;
  longitude: number;
  address: string | null;
  cbuCode: string | null;
  branchCode: string | null;
  operationAreaCode: string | null;
  departmentCode: string | null;
}

export interface TaskDetailsPayload extends TaskLocationPayload {
  title: string | null;
  notes: string | null;
  priority: string;
  externalReference: string | null;
  faId: string | null;
  wfmTicketId: number | null;
  dueDate: string | null;
  completionDueDate: string | null;
}

export interface CreateTaskPayload extends TaskDetailsPayload {
  taskTypeId: string;
  taskNumber: string | null;
}

export interface AssignTaskPayload {
  teamId: string;
  dueDate: string | null;
  completionDueDate: string | null;
  note: string | null;
}

export interface FillTaskPayload {
  clientSubmissionId: string;
  clientFilledAt: string;
  answers: Record<string, unknown>;
}

export interface ReturnTaskPayload {
  reasonCode: string;
  reason: string;
  reassignToTeamId: string | null;
}

export interface TaskType {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  descriptionEn: string | null;
  descriptionAr: string | null;
  formDefinitionId: string;
  formCode: string | null;
  formNameEn: string | null;
  formNameAr: string | null;
  /** Null when the bound form has no version that takes fills — new tasks of this type are refused. */
  formCurrentVersionNo: number | null;
  departmentCode: string | null;
  fillSlaHours: number | null;
  completionSlaHours: number | null;
  /** Approving one of its tasks that carries an FA id closes that field activity in C2M. */
  closesC2mActivity: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TaskTypePayload {
  nameEn: string;
  nameAr: string;
  descriptionEn: string | null;
  descriptionAr: string | null;
  formDefinitionId: string;
  departmentCode: string | null;
  fillSlaHours: number | null;
  completionSlaHours: number | null;
  closesC2mActivity: boolean;
}

export interface CreateTaskTypePayload extends TaskTypePayload {
  code: string;
}

export interface FormOption {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  currentVersionNo: number;
}

/** Where closing a task's C2M field activity stands — the server's `C2mClosureStatuses`. */
export const C2M_CLOSURE_STATUSES = {
  Pending: 'PENDING',
  Closed: 'CLOSED',
  Skipped: 'SKIPPED',
  Rejected: 'REJECTED',
  Failed: 'FAILED',
} as const;

/** One attempt to close a task's field activity in C2M. */
export interface C2mDispatchLog {
  id: string;
  attemptNumber: number;
  faId: string;
  /** `C` (completed) or `X` (cancelled) — what C2M was asked to record. */
  opStatus: string;
  /** `PENDING`, `SUCCEEDED`, `FAILED` or `SKIPPED`. */
  status: string;
  responseCode: string | null;
  errorMessage: string | null;
  requestJson: string;
  responseJson: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface C2mRetryResult {
  c2mStatus: string | null;
  outcome: string;
  message: string | null;
  responseCode: string | null;
}

export const C2M_ACTION_MAPPINGS_PATH = '/api/v1/c2m-action-mappings';

/** What one Action Taken answer tells C2M when a task closes its field activity. */
export interface C2mActionMapping {
  id: string;
  actionCode: string;
  faStatus: string;
  cancelReason: string | null;
  closureReason: string | null;
  nameEn: string;
  nameAr: string;
  isActive: boolean;
  updatedAt: string;
}

export interface C2mActionMappingPayload {
  faStatus: string;
  cancelReason: string | null;
  closureReason: string | null;
  nameEn: string;
  nameAr: string;
  isActive: boolean;
}
