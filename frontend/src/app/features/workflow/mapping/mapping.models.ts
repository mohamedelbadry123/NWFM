/**
 * Handwritten mirror of the backend mapping contract (`DesignerMappingContracts.cs`, `IMappingInvocations.cs`,
 * `IStepMappingRuntime.cs`). The generated client does not cover these yet; keep the member names exactly as the
 * server's camelCase JSON, because the server rejects unknown members.
 *
 * Identity is by stable IDs and exact version pins, never by label. `fieldKey` is one literal key (dots are NOT
 * nesting); `jsonPointer` is a separate RFC 6901 path inside that value.
 */
export type MappingSchemaType = 'string' | 'number' | 'integer' | 'boolean' | 'object' | 'array';

export interface MappingSchema {
  type: MappingSchemaType;
  required?: boolean;
  nullable?: boolean;
  properties?: Record<string, MappingSchema> | null;
  items?: MappingSchema | null;
  /** Absent means "no default"; an explicit `null` is a real default. */
  default?: unknown;
  inferred?: boolean;
  additionalProperties?: boolean;
  additionalPropertySchema?: MappingSchema | null;
}

export type ParameterLocation = 'path' | 'query' | 'header';
export interface EndpointParameter {
  location: ParameterLocation;
  name: string;
  schema: MappingSchema;
}
export interface EndpointDefinition {
  method: string;
  path: string;
  parameters: EndpointParameter[];
  body?: MappingSchema | null;
  response?: MappingSchema | null;
}

export type SourceKind =
  'WorkflowInput' | 'FormField' | 'RoutingOutput' | 'EventOutput' | 'InvocationInput' | 'Response';
export interface SourceReference {
  workflowDefinitionId: string;
  workflowVersionId: string;
  nodeKey: string;
  kind: SourceKind;
  fieldKey: string;
  jsonPointer: string;
  formStepId?: string | null;
  formId?: string | null;
  formVersion?: number | null;
  selectorId?: string | null;
}
export type SourceAvailability =
  'InvocationStart' | 'SubmittedForm' | 'CompletedOutput' | 'SchemaRequired' | string;
export interface SourceDescriptor {
  reference: SourceReference;
  label: string;
  breadcrumbs: string[];
  schema: MappingSchema;
  availability: SourceAvailability;
  sensitive?: boolean;
}

export type MissingMode = 'Attention' | 'Wait' | 'Fallback';
export interface MissingDataPolicy {
  mode: MissingMode;
  deadlineSeconds?: number | null;
  timeoutOutcome?: string | null;
  /** Present only for `Fallback`. */
  fallback?: unknown;
}

export type TransformOp =
  | 'literal'
  | 'source'
  | 'item'
  | 'object'
  | 'concat'
  | 'add'
  | 'subtract'
  | 'multiply'
  | 'divide'
  | 'text'
  | 'number'
  | 'integer'
  | 'boolean'
  | 'coalesce'
  | 'map'
  | 'filter'
  | 'count'
  | 'sum'
  | 'equal'
  | 'greater';
export interface TransformNode {
  op: TransformOp;
  /** Literal value. Absent is distinct from `null`. */
  value?: unknown;
  source?: SourceReference | null;
  args?: TransformNode[] | null;
  properties?: Record<string, TransformNode> | null;
  jsonPointer?: string;
  missing?: MissingDataPolicy | null;
  literalSchema?: MappingSchema | null;
}

export interface InputBinding {
  location: ParameterLocation | 'body';
  name: string;
  value: TransformNode;
}
export interface OutputDefinition {
  id: string;
  name: string;
  schema: MappingSchema;
  value: TransformNode;
  sensitive?: boolean;
}
export type StepBindingMode = 'ReadOnly' | 'Prefill';
export interface StepFieldBinding {
  id: string;
  formStepId: string;
  formId: string;
  formVersion: number;
  fieldKey: string;
  mode: StepBindingMode;
  schema: MappingSchema;
  value: TransformNode;
}
export interface CallableTarget {
  workflowDefinitionId: string;
  workflowVersionId: string;
  nodeKey: string;
}
export interface MappingTreePin {
  workflowDefinitionId: string;
  workflowVersionId: string;
}
export type FollowUpMode = 'MapOnly' | 'Run';
export interface FollowUpAction {
  id: string;
  target: CallableTarget;
  mode: FollowUpMode;
  inputs: TransformNode;
  unavailableTarget?: MissingDataPolicy | null;
}
export type KeyKind = 'BusinessKey' | 'CorrelationKey';
export type MultipleMatches = 'Attention' | 'Latest';
export interface RelatedInstanceSelector {
  id: string;
  workflowDefinitionId: string;
  workflowVersionId: string;
  keyKind: KeyKind;
  keyValue: TransformNode;
  startedByUserId?: TransformNode | null;
  status?: string | null;
  windowSeconds?: number | null;
  multipleMatches: MultipleMatches;
  missing?: MissingDataPolicy | null;
}
export interface DesignerMappingConfiguration {
  contractVersion: number;
  endpoint: EndpointDefinition | null;
  inputs: InputBinding[];
  selectors: RelatedInstanceSelector[];
  outputs: OutputDefinition[];
  stepBindings: StepFieldBinding[];
  actions: FollowUpAction[];
  callable: boolean;
  invocationInputs?: MappingSchema | null;
  treeRoot?: MappingTreePin | null;
}

export interface MappingSample {
  source: SourceReference;
  value: unknown;
}
export interface PreparedMappingRequest {
  method: string;
  path: string;
  query: Record<string, string>;
  headers: Record<string, string>;
  body?: unknown;
}
export type MappingPreviewStage = 'Request' | 'Completion' | 'Step';
export interface MappingPreviewRequest {
  configuration: DesignerMappingConfiguration;
  samples: MappingSample[];
  owner: CallableTarget;
  stage: MappingPreviewStage;
}
export interface PreparedFollowUp {
  actionId: string;
  target: CallableTarget;
  mode: FollowUpMode;
  inputs: unknown;
  unavailableTarget?: MissingDataPolicy | null;
}
export interface MappingPreviewResult {
  request?: PreparedMappingRequest | null;
  errors: string[];
  outputs?: Record<string, unknown> | null;
  actions?: PreparedFollowUp[] | null;
  stepValues?: Record<string, unknown> | null;
}
export interface MappingAuthoringRequest {
  owner: CallableTarget;
  configuration: DesignerMappingConfiguration;
}
export interface MappingValidationResult {
  valid: boolean;
  errors: string[];
  canPublish: boolean;
}
export interface CallableTargetDescriptor {
  target: CallableTarget;
  label: string;
  breadcrumbs: string[];
  inputs: MappingSchema;
  callable: boolean;
}
export interface MappingDependency {
  from: CallableTarget;
  to: CallableTarget;
  kind: string;
}
export interface MappingCatalog {
  sources: SourceDescriptor[];
  targets: CallableTargetDescriptor[];
  treeNodes?: CallableTarget[] | null;
  dependencies?: MappingDependency[] | null;
  diagnostics?: string[] | null;
  relatedTrees?: { root: MappingTreePin; nodes: CallableTarget[] }[] | null;
}
export interface MappingDefinitionChoice {
  definitionId: string;
  versionId: string;
  versionNumber: number;
  name: string;
}
export interface SchemaImportRequest {
  document: string;
  path: string;
  method: string;
}
/** Result of the existing HTTP test endpoint (`IntegrationResult`). */
export interface HttpTestResult {
  success: boolean;
  statusCode?: number | null;
  body: string;
  error?: string | null;
  retryable?: boolean;
  timedOut?: boolean;
  headers?: Record<string, string> | null;
  requestSummary?: string | null;
  elapsedMilliseconds?: number | null;
}

// ── Runtime ────────────────────────────────────────────────────────────────

export interface ResolvedMappingSource {
  state: string;
  instanceId?: string | null;
  submissionId?: string | null;
  resultId?: string | null;
  invocationId?: string | null;
  completedAt?: string | null;
  source: SourceReference;
  activityInstanceId?: string | null;
  formVisitId?: string | null;
  dependencies?: ResolvedMappingSource[] | null;
}
export interface StepMappingValue {
  bindingId: string;
  formStepId: string;
  formId: string;
  formVersion: number;
  fieldKey: string;
  mode: StepBindingMode;
  /** Typed JSON exactly as the server will compare it; may be `null`. */
  value: unknown;
}
export interface StepMappingSnapshot {
  invocationId: string;
  visitId: string;
  status: string;
  values: StepMappingValue[];
  provenance?: ResolvedMappingSource[] | null;
}
export type MappingInvocationStatus =
  'Preparing' | 'Waiting' | 'Prepared' | 'Delivered' | 'Completed' | 'Attention' | 'Cancelled';
export type MappingExecutionKind =
  'Graph' | 'OptionalGraph' | 'RequiredEvent' | 'OptionalEvent' | 'Callable' | 'MapOnly' | 'Step';
export interface MappingInvocationDto {
  id: string;
  rootInstanceId: string;
  originActivityId: string;
  originTokenId?: string | null;
  targetInstanceId: string;
  targetVersionId: string;
  targetNodeKey: string;
  executionKind: MappingExecutionKind | string;
  parentInvocationId?: string | null;
  actionId: string;
  status: MappingInvocationStatus | string;
  windowAnchor: string;
  deadline?: string | null;
  prepared: boolean;
  delivered: boolean;
  applied: boolean;
  graphAdvanced: boolean;
  error?: string | null;
  completedAt?: string | null;
  /** The integration operation delivering this invocation. Once set, retry is refused and operation replay is the supported path. */
  operationId?: string | null;
}
export interface PreparedMappingInputsDto {
  invocationId: string;
  rootInstanceId: string;
  targetInstanceId: string;
  target: CallableTarget;
  parentInvocationId?: string | null;
  actionId: string;
  inputs: unknown;
  provenance: ResolvedMappingSource[];
}
