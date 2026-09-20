import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { WorkItemDto } from '@shared/models/models/Workflow/Application/DTOs/work-item-dto';

export interface WorkspaceSettings { kind: 'Main' | 'Child'; clusterCode?: string; regionCode?: string; cityCode?: string }
export interface ReferenceItem { id: string; code: string; nameEn: string; nameAr: string; parentCode?: string }
export interface WorkspaceWorkflow { id: string; name: string; nameAr?: string; versionId: string; versionNumber: number; workspaceJson: string; definitionKey?: string }
export interface WorkspaceInstance { id: string; name: string; status: string; reference?: string; startedAt: string; isDemo: boolean; geographyJson?: string }
export interface WorkspaceTask { task: WorkItemDto & { instructionsEn?: string; instructionsAr?: string; formFields?: {key: string; labelEn: string; labelAr: string; type: string; required: boolean; options?: string[]}[]; formValues?: Record<string, unknown> }; canAct: boolean; disabledReason?: string }
export interface WorkspaceActivity { id: string; nodeKey: string; name: string; type: string; status: string; phase?: string; startedAt: string; completedAt?: string; dueAt?: string; departmentCode?: string; fieldActivityCode?: string; assignedGroup?: string; tasks: WorkspaceTask[] }
export interface WorkspaceExecution { id: string; parentInstanceId?: string; parentActivityInstanceId?: string; name: string; status: string; activities: WorkspaceActivity[] }
export interface WorkspaceDetail { id: string; isDemo: boolean; geographyJson?: string; tree: WorkspaceExecution[]; history: { id: string; instanceId: string; type: string; nodeKey?: string; actorName?: string; occurredAt: string; payloadJson?: string }[]; operations: { id: string; activityId: string; name?: string; kind: string; trigger?: string; required: boolean; status: string; attempts: number; error?: string; statusCode?: number; responseJson?: string }[]; demoActors: { userId: string; name: string }[] }

@Injectable({ providedIn: 'root' })
export class WorkflowWorkspaceService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/workflow/workspace';
  references(kind: string, parentCode?: string) { return this.http.get<ReferenceItem[]>(`${this.base}/lookups/${kind}`, { params: parentCode ? { parentCode } : {} }); }
  create(name: string, nameAr: string, settings: WorkspaceSettings) { return this.http.post<{ definitionId: string; versionId: string }>(`${this.base}/definitions`, { name, nameAr, settings }); }
  catalog() { return this.http.get<WorkspaceWorkflow[]>(`${this.base}/catalog`); }
  children() { return this.http.get<WorkspaceWorkflow[]>(`${this.base}/children`); }
  start(id: string, requestId: string, reference: string, isDemo: boolean) { return this.http.post<{ instanceId: string }>(`${this.base}/definitions/${id}/instances`, { requestId, reference, isDemo }); }
  instances(search = '') { return this.http.get<WorkspaceInstance[]>(`${this.base}/instances`, { params: { search } }); }
  detail(id: string, demoActorId?: string) { return this.http.get<WorkspaceDetail>(`${this.base}/instances/${id}`, { params: demoActorId ? { demoActorId } : {} }); }
  act(id: string, body: { requestId: string; action: string; comment?: string; formValues?: Record<string, unknown>; demoActorId?: string }) { return this.http.post<void>(`${this.base}/tasks/${id}/${body.action === 'comment' ? 'comments' : 'actions'}`, body); }
}
