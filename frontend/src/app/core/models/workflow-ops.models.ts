/** Hand-written view models for workflow ops endpoints not yet in OpenAPI. */

export interface WorkflowBindingSimulateRequest {
  organizationId?: string | null;
  samplePayloadJson?: string | null;
}

export interface WorkflowBindingSimulateStep {
  nodeKey?: string;
  activityType?: string;
  note?: string;
}

export interface WorkflowBindingSimulateResult {
  steps?: WorkflowBindingSimulateStep[];
  warnings?: string[];
  errors?: string[];
}

export interface WorkflowIntegrationMessageDto {
  id?: string;
  organizationId?: string;
  messageId?: string;
  moduleKey?: string;
  businessEntityType?: string;
  businessEntityId?: string;
  triggerEvent?: string;
  outcomeKey?: string;
  status?: string;
  attemptCount?: number;
  errorMessage?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface PaginatedWorkflowMessages {
  items?: WorkflowIntegrationMessageDto[];
  totalCount?: number;
  page?: number;
  pageSize?: number;
}

export interface WorkflowWorkloadGroupDto {
  groupId?: string;
  name?: string;
  pendingCount?: number;
  overdueCount?: number;
  claimedCount?: number;
}

export interface WorkflowWorkloadTotalsDto {
  open?: number;
  overdue?: number;
  completedToday?: number;
}

export interface WorkflowWorkloadDto {
  groups?: WorkflowWorkloadGroupDto[];
  totals?: WorkflowWorkloadTotalsDto;
}

/** Hand-written until `npm run generate:api` picks up WorkflowRequest DTOs. */
export interface WorkflowRequestView {
  id?: string;
  organizationId?: string;
  requestNumber?: string;
  workflowBindingId?: string;
  workflowInstanceId?: string;
  businessEntityType?: string;
  businessEntityId?: string;
  serviceKey?: string;
  serviceNameEn?: string;
  serviceNameAr?: string | null;
  screenKey?: string | null;
  triggerEventKey?: string;
  requestDate?: string;
  requesterUserId?: string | null;
  status?: string;
  currentActivityInstanceId?: string | null;
  currentActivityNameEn?: string | null;
  currentActivityNameAr?: string | null;
  originalAssignedGroupId?: string | null;
  originalAssignedGroupName?: string | null;
  currentAssignedGroupId?: string | null;
  currentAssignedGroupName?: string | null;
  currentTaskSlaMinutes?: number | null;
  currentTaskDueAtUtc?: string | null;
  remainingSlaMinutes?: number | null;
  completedAtUtc?: string | null;
  correlationId?: string | null;
  currentClaimedByUserId?: string | null;
  createdAt?: string;
  updatedAt?: string;
}

export interface WorkflowRequestKpiView {
  total?: number;
  inProgress?: number;
  completed?: number;
  breached?: number;
}

export interface WorkflowRequestListParams {
  page: number;
  pageSize: number;
  search?: string | null;
  status?: string | null;
  service?: string | null;
  currentStep?: string | null;
  originalGroupId?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  slaStatus?: string | null;
  sortBy?: string | null;
}

export interface WorkflowRequestPageView {
  items?: WorkflowRequestView[];
  totalCount?: number;
  pageNumber?: number;
  pageSize?: number;
  kpis?: WorkflowRequestKpiView;
}

export interface WorkflowActivityOutcomeView {
  id?: string;
  outcomeKey?: string;
  name?: string;
  nameAr?: string | null;
  requiresComment?: boolean;
  requiresAttachment?: boolean;
  isDefault?: boolean;
  resultValue?: string | null;
}

/** Extra WorkItem fields returned by the runtime APIs (additive; safe after OpenAPI regen). */
export interface WorkflowWorkItemExtras {
  requestNumber?: string | null;
  serviceNameEn?: string | null;
  serviceNameAr?: string | null;
  requestDate?: string | null;
  currentStepNameEn?: string | null;
  currentStepNameAr?: string | null;
  slaDurationMinutes?: number | null;
  remainingSlaMinutes?: number | null;
  originalGroupName?: string | null;
  availableOutcomes?: WorkflowActivityOutcomeView[] | null;
}

export interface CompleteWorkItemPayload {
  actionTaken: string;
  comment?: string | null;
  redirectAssignmentGroupId?: string | null;
  redirectDepartmentId?: string | null;
}

export type WorkflowLiveGraphRuntimeStatus = 'Pending' | 'Active' | 'Completed' | 'Failed';

export interface WorkflowLiveGraphNodeView {
  nodeKey: string;
  activityType: string;
  name: string;
  x: number;
  y: number;
  runtimeStatus: WorkflowLiveGraphRuntimeStatus;
}

export interface WorkflowLiveGraphEdgeView {
  fromNodeKey: string;
  toNodeKey: string;
  isDefault: boolean;
}

export interface WorkflowLiveGraphView {
  currentNodeKey: string;
  instanceStatus: string;
  nodes: WorkflowLiveGraphNodeView[];
  edges: WorkflowLiveGraphEdgeView[];
}

/** Enriched timeline row (additive JSON; generated WorkflowEventDto stays untouched). */
export interface WorkflowHistoryEvent {
  id?: string;
  eventType?: string;
  activityNodeKey?: string | null;
  actorUserId?: string | null;
  payloadJson?: string | null;
  occurredAt?: string;
  activityNameEn?: string | null;
  activityNameAr?: string | null;
  actorName?: string | null;
  actorNameAr?: string | null;
  comment?: string | null;
  actionTaken?: string | null;
  attachmentName?: string | null;
  attachmentUrl?: string | null;
}
