/**
 * Hand-written workflow binding view models / request payloads.
 * Extends generated OpenAPI types with SaaS fields not yet regenerated.
 */

export type WorkflowExecutionPolicy =
  | 'StartNewInstance'
  | 'SignalExistingInstance'
  | 'StartIfNoRunningInstance'
  | 'RestartAfterTerminal';

export type WorkflowBindingModeExt = 'Disabled' | 'Shadow' | 'Active' | 'Paused';

export interface CreateWorkflowBindingPayload {
  organizationId: string;
  moduleKey?: string | null;
  entityType?: string | null;
  triggerEvent?: string | null;
  description?: string | null;
  mode?: WorkflowBindingModeExt;
  versionPolicy?: 'Latest' | 'Fixed';
  executionPolicy?: WorkflowExecutionPolicy;
  fixedWorkflowVersionId?: string | null;
  startEventKey?: string | null;
  startConditionExpression?: string | null;
  /** Link to a workflow screen, e.g. "workflow.start" */
  screenKey?: string | null;
  inputMappingJson?: string | null;
  outcomeMappingJson?: string | null;
  conditionJson?: string | null;
}

export interface UpdateWorkflowBindingPayload {
  moduleKey?: string | null;
  entityType?: string | null;
  triggerEvent?: string | null;
  description?: string | null;
  mode?: WorkflowBindingModeExt;
  versionPolicy?: 'Latest' | 'Fixed';
  executionPolicy?: WorkflowExecutionPolicy;
  fixedWorkflowVersionId?: string | null;
  startEventKey?: string | null;
  startConditionExpression?: string | null;
  screenKey?: string | null;
  inputMappingJson?: string | null;
  outcomeMappingJson?: string | null;
  conditionJson?: string | null;
}

export interface WorkflowBindingViewModel {
  id?: string;
  organizationId?: string;
  workflowDefinitionId?: string;
  workflowDefinitionName?: string | null;
  moduleKey?: string | null;
  entityType?: string | null;
  triggerEvent?: string | null;
  description?: string | null;
  mode?: WorkflowBindingModeExt;
  versionPolicy?: 'Latest' | 'Fixed';
  executionPolicy?: WorkflowExecutionPolicy;
  fixedWorkflowVersionId?: string | null;
  startEventKey?: string | null;
  startConditionExpression?: string | null;
  screenKey?: string | null;
  inputMappingJson?: string | null;
  outcomeMappingJson?: string | null;
  conditionJson?: string | null;
  isActive?: boolean;
  createdAt?: string;
  updatedAt?: string;
}

export interface KeyValueRow {
  key: string;
  value: string;
}
