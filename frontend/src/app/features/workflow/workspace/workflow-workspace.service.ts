import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { WorkItemDto } from '@shared/models/models/Workflow/Application/DTOs/work-item-dto';
import { OrgLocation, OrgScopeLevel } from '@shared/components/org-scope/org-scope.model';

/**
 * A workflow's versioned settings. A main workflow is placed in the shared org hierarchy
 * (cluster → CBU → branch | operation area); a child takes its parent's place when it starts.
 */
export interface WorkspaceSettings {
  schemaVersion?: number;
  organizationScopes?: WorkflowOrganizationScope[];
  fieldActivityTypeId?: string;
  departmentCode?: string;
  fieldActivityCode?: string;
  taskTypeId?: string;
  designerVersion?: number;
  kind: 'Main' | 'Child';
  clusterCode?: string;
  cbuCode?: string;
  branchCode?: string;
  operationAreaCode?: string;
  /** @deprecated Pre-hierarchy key, only ever a CBU code. Read by {@link normalizeWorkspaceSettings}; never written. */
  regionCode?: string;
  /** @deprecated Pre-hierarchy key, only ever a branch code. Read by {@link normalizeWorkspaceSettings}; never written. */
  cityCode?: string;
}

/**
 * Settings saved before the shared hierarchy stored `regionCode` and `cityCode`, which were only ever
 * chosen from Auth's CBU and branch lookups — so they are read as `cbuCode` and `branchCode`. When a
 * version carries both a legacy key and its replacement with different codes nothing is chosen: both
 * are kept so the conflict stays visible (and blocks publishing) until someone re-selects the location.
 * Remove together with the backend adapter in `WorkflowWorkspaceDefinition.Read`.
 */
export function normalizeWorkspaceSettings(raw: WorkspaceSettings): WorkspaceSettings {
  const settings = { ...raw };
  const migrate = (legacy: 'regionCode' | 'cityCode', current: 'cbuCode' | 'branchCode') => {
    const old = settings[legacy]?.trim();
    const now = settings[current]?.trim();
    if (!old) { delete settings[legacy]; return; }
    if (!now) { settings[current] = old; delete settings[legacy]; return; }
    if (now.toUpperCase() === old.toUpperCase()) delete settings[legacy];
  };
  migrate('regionCode', 'cbuCode');
  migrate('cityCode', 'branchCode');
  return settings;
}

/** True while the saved settings still name two different CBUs or branches. */
export function hasLegacyLocationConflict(settings: WorkspaceSettings): boolean {
  return !!settings.regionCode || !!settings.cityCode;
}

export function workspaceLocation(settings: WorkspaceSettings): OrgLocation {
  return {
    clusterCode: settings.clusterCode || null,
    cbuCode: settings.cbuCode || null,
    branchCode: settings.branchCode || null,
    operationAreaCode: settings.operationAreaCode || null,
  };
}

/** A main workflow must name at least its cluster and CBU — the level coverage is matched at. */
export function hasRequiredLocation(settings: WorkspaceSettings): boolean {
  if (settings.schemaVersion === 2 || settings.organizationScopes) return !hasLegacyLocationConflict(settings)
    && !!settings.organizationScopes?.length && settings.organizationScopes.length <= 500
    && settings.organizationScopes.every(s => !!s.clusterCode && !!s.code &&
      (s.level === 'Cluster' ? s.code === s.clusterCode : !!s.cbuCode));
  return settings.kind === 'Child' || (!!settings.clusterCode && !!settings.cbuCode && !hasLegacyLocationConflict(settings));
}

export interface WorkflowOrganizationScope { level: OrgScopeLevel; code: string; clusterCode: string; cbuCode?: string }
export function scopeLocation(scope: WorkflowOrganizationScope): OrgLocation {
  return { clusterCode: scope.clusterCode, cbuCode: scope.cbuCode || null,
    branchCode: scope.level === 'Branch' ? scope.code : null,
    operationAreaCode: scope.level === 'OperationArea' ? scope.code : null };
}
export function scopeContains(scope: WorkflowOrganizationScope, location: OrgLocation): boolean {
  return scope.clusterCode === location.clusterCode && (scope.level === 'Cluster' || scope.cbuCode === location.cbuCode)
    && (scope.level === 'Cluster' || scope.level === 'Cbu' || scope.level === 'Branch' && scope.code === location.branchCode
      || scope.level === 'OperationArea' && scope.code === location.operationAreaCode);
}
export function hasRequiredClassification(settings: WorkspaceSettings): boolean {
  if (settings.schemaVersion !== 2) return true;
  return settings.kind === 'Main' ? !!settings.fieldActivityTypeId && !!settings.departmentCode && !!settings.fieldActivityCode && !settings.taskTypeId
    : !!settings.taskTypeId && !settings.fieldActivityTypeId;
}

export interface WorkspaceLocationPart { level: OrgScopeLevel; code: string; nameEn?: string; nameAr?: string }
export interface ReferenceItem { id: string; code: string; nameEn: string; nameAr: string; parentCode?: string }
export interface WorkspaceWorkflow { id: string; name: string; nameAr?: string; versionId: string; versionNumber: number; workspaceJson: string; definitionKey?: string; startScopes?: WorkflowOrganizationScope[] }
export interface WorkspaceInstance { id: string; name: string; status: string; reference?: string; startedAt: string; isDemo: boolean; location: OrgLocation }
export interface WorkspaceTask { task: WorkItemDto & { instructionsEn?: string; instructionsAr?: string; formFields?: {key: string; labelEn: string; labelAr: string; type: string; required: boolean; options?: string[]}[]; formValues?: Record<string, unknown> }; canAct: boolean; completionBlocked?: boolean; disabledReason?: string }
export interface WorkspaceActivity { id: string; nodeKey: string; name: string; type: string; status: string; phase?: string; startedAt: string; completedAt?: string; dueAt?: string; departmentCode?: string; fieldActivityCode?: string; assignedGroup?: string; sla?: {name:string;duration:number;durationUnit:string;timeZone:string}; tasks: WorkspaceTask[] }
export interface WorkspaceExecution { id: string; parentInstanceId?: string; parentActivityInstanceId?: string; name: string; status: string; activities: WorkspaceActivity[] }
export interface WorkspaceDetail { id: string; isDemo: boolean; location: WorkspaceLocationPart[]; tree: WorkspaceExecution[]; history: { id: string; instanceId: string; type: string; nodeKey?: string; actorName?: string; occurredAt: string; payloadJson?: string }[]; operations: { id: string; activityId: string; eventNodeKey?: string; name?: string; kind: string; trigger?: string; required: boolean; status: string; attempts: number; error?: string; statusCode?: number; responseJson?: string }[]; demoActors: { userId: string; name: string }[] }

@Injectable({ providedIn: 'root' })
export class WorkflowWorkspaceService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/workflow/workspace';
  references(kind: string, parentCode?: string) { return this.http.get<ReferenceItem[]>(`${this.base}/lookups/${kind}`, { params: parentCode ? { parentCode } : {} }); }
  create(name: string, nameAr: string, settings: WorkspaceSettings) { return this.http.post<{ definitionId: string; versionId: string }>(`${this.base}/definitions`, { name, nameAr, settings }); }
  catalog() { return this.http.get<WorkspaceWorkflow[]>(`${this.base}/catalog`); }
  children(selectedVersionId?: string) { return this.http.get<WorkspaceWorkflow[]>(`${this.base}/children`, { params: selectedVersionId ? { selectedVersionId } : {} }); }
  start(id: string, requestId: string, reference: string, isDemo: boolean, location?: OrgLocation) { return this.http.post<{ instanceId: string }>(`${this.base}/definitions/${id}/instances`, { requestId, reference, isDemo, location }); }
  instances(search = '') { return this.http.get<WorkspaceInstance[]>(`${this.base}/instances`, { params: { search } }); }
  detail(id: string, demoActorId?: string) { return this.http.get<WorkspaceDetail>(`${this.base}/instances/${id}`, { params: demoActorId ? { demoActorId } : {} }); }
  act(id: string, body: { requestId: string; action: string; comment?: string; formValues?: Record<string, unknown>; demoActorId?: string }) { return this.http.post<void>(`${this.base}/tasks/${id}/${body.action === 'comment' ? 'comments' : 'actions'}`, body); }
}
