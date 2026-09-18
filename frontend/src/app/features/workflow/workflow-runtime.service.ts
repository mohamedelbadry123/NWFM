import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowRuntimeInstancesPost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-post';
import { apiWorkflowRuntimeInstancesGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-get';
import { apiWorkflowRuntimeInstancesInstanceIdGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-get';
import { apiWorkflowRuntimeInstancesInstanceIdProgressGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-progress-get';
import { apiWorkflowRuntimeInstancesInstanceIdTimelineGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-timeline-get';
import { apiWorkflowRuntimeInstancesInstanceIdCancelPost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-cancel-post';
import { apiWorkflowRuntimeInstancesInstanceIdSuspendPost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-suspend-post';
import { apiWorkflowRuntimeInstancesInstanceIdResumePost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-resume-post';
import { apiWorkflowRuntimeAdminInstancesGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-admin-instances-get';
import { apiWorkflowRuntimeAdminInstancesInstanceIdGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-admin-instances-instance-id-get';
import { apiWorkflowRuntimeAdminInstancesInstanceIdSuspendPost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-admin-instances-instance-id-suspend-post';
import { apiWorkflowRuntimeAdminInstancesInstanceIdResumePost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-admin-instances-instance-id-resume-post';
import { apiWorkflowRuntimeAdminInstancesInstanceIdRetryPost } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-admin-instances-instance-id-retry-post';
import { apiWorkflowRuntimeInstancesInstanceIdIncidentsGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-incidents-get';
import { apiWorkflowRuntimeInstancesInstanceIdTimersGet } from '@shared/models/fn/workflow-runtime/api-workflow-runtime-instances-instance-id-timers-get';
import type { WorkflowInstanceDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-instance-dto';
import type { WorkflowProgressDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-progress-dto';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';
import type { WorkflowIncidentSummaryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-summary-dto';
import type { WorkflowTimerDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-timer-dto';
import type { StartWorkflowInstanceRequest } from '@shared/models/models/Workflow/Api/Controllers/start-workflow-instance-request';
import type { PaginatedResultOfWorkflowInstanceDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-instance-dto';
import type {
  WorkflowLiveGraphView,
  WorkflowRequestListParams,
  WorkflowRequestPageView,
  WorkflowRequestView,
} from '@core/models/workflow-ops.models';

@Injectable({ providedIn: 'root' })
export class WorkflowRuntimeService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  startInstance(req: StartWorkflowInstanceRequest): Observable<WorkflowInstanceDto> {
    return apiWorkflowRuntimeInstancesPost(this.http, this.config.rootUrl, { body: req }).pipe(
      map(r => r.body as WorkflowInstanceDto)
    );
  }

  getInstances(page: number, pageSize: number): Observable<PaginatedResultOfWorkflowInstanceDto> {
    return apiWorkflowRuntimeInstancesGet(this.http, this.config.rootUrl, { page, pageSize }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowInstanceDto)
    );
  }

  getInstanceById(instanceId: string): Observable<WorkflowInstanceDto> {
    return apiWorkflowRuntimeInstancesInstanceIdGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowInstanceDto)
    );
  }

  getProgress(instanceId: string): Observable<WorkflowProgressDto> {
    return apiWorkflowRuntimeInstancesInstanceIdProgressGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowProgressDto)
    );
  }

  getTimeline(instanceId: string): Observable<WorkflowHistoryEvent[]> {
    return apiWorkflowRuntimeInstancesInstanceIdTimelineGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowHistoryEvent[])
    );
  }

  cancel(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeInstancesInstanceIdCancelPost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  suspend(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeInstancesInstanceIdSuspendPost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  resume(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeInstancesInstanceIdResumePost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  // ── SuperAdmin ──────────────────────────────────────────────────────────────

  adminGetInstances(page: number, pageSize: number, organizationId?: string): Observable<PaginatedResultOfWorkflowInstanceDto> {
    return apiWorkflowRuntimeAdminInstancesGet(this.http, this.config.rootUrl, { page, pageSize, organizationId }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowInstanceDto)
    );
  }

  adminGetInstanceById(instanceId: string): Observable<WorkflowProgressDto> {
    return apiWorkflowRuntimeAdminInstancesInstanceIdGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowProgressDto)
    );
  }

  adminSuspend(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeAdminInstancesInstanceIdSuspendPost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  adminResume(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeAdminInstancesInstanceIdResumePost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  adminRetry(instanceId: string): Observable<void> {
    return apiWorkflowRuntimeAdminInstancesInstanceIdRetryPost(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(() => undefined)
    );
  }

  getIncidentsForInstance(instanceId: string): Observable<WorkflowIncidentSummaryDto[]> {
    return apiWorkflowRuntimeInstancesInstanceIdIncidentsGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowIncidentSummaryDto[])
    );
  }

  getTimersForInstance(instanceId: string): Observable<WorkflowTimerDto[]> {
    return apiWorkflowRuntimeInstancesInstanceIdTimersGet(this.http, this.config.rootUrl, { instanceId }).pipe(
      map(r => r.body as WorkflowTimerDto[])
    );
  }

  listRequests(params: WorkflowRequestListParams): Observable<WorkflowRequestPageView> {
    let httpParams = new HttpParams()
      .set('page', String(params.page))
      .set('pageSize', String(params.pageSize));
    if (params.search) httpParams = httpParams.set('search', params.search);
    if (params.status) httpParams = httpParams.set('status', params.status);
    if (params.service) httpParams = httpParams.set('service', params.service);
    if (params.currentStep) httpParams = httpParams.set('currentStep', params.currentStep);
    if (params.originalGroupId) httpParams = httpParams.set('originalGroupId', params.originalGroupId);
    if (params.fromUtc) httpParams = httpParams.set('fromUtc', params.fromUtc);
    if (params.toUtc) httpParams = httpParams.set('toUtc', params.toUtc);
    if (params.slaStatus) httpParams = httpParams.set('slaStatus', params.slaStatus);
    if (params.sortBy) httpParams = httpParams.set('sortBy', params.sortBy);
    return this.http.get<WorkflowRequestPageView>(
      `${this.config.rootUrl}/api/workflow/requests`,
      { params: httpParams },
    );
  }

  getRequestById(requestId: string): Observable<WorkflowRequestView> {
    return this.http.get<WorkflowRequestView>(
      `${this.config.rootUrl}/api/workflow/requests/${requestId}`,
    );
  }

  getRequestByInstanceId(instanceId: string): Observable<WorkflowRequestView> {
    return this.http.get<WorkflowRequestView>(
      `${this.config.rootUrl}/api/workflow/requests/by-instance/${instanceId}`,
    );
  }

  getLiveGraph(instanceId: string): Observable<WorkflowLiveGraphView> {
    return this.http.get<WorkflowLiveGraphView>(
      `${this.config.rootUrl}/api/workflow/runtime/instances/${instanceId}/live-graph`,
    );
  }

  adminGetLiveGraph(instanceId: string): Observable<WorkflowLiveGraphView> {
    return this.http.get<WorkflowLiveGraphView>(
      `${this.config.rootUrl}/api/workflow/runtime/admin/instances/${instanceId}/live-graph`,
    );
  }
}
