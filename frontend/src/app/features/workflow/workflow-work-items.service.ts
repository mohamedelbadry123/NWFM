import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowWorkItemsMyGet } from '@shared/models/fn/work-items/api-workflow-work-items-my-get';
import { apiWorkflowWorkItemsWorkItemIdGet } from '@shared/models/fn/work-items/api-workflow-work-items-work-item-id-get';
import { apiWorkflowWorkItemsWorkItemIdClaimPost } from '@shared/models/fn/work-items/api-workflow-work-items-work-item-id-claim-post';
import { apiWorkflowWorkItemsWorkItemIdReleasePost } from '@shared/models/fn/work-items/api-workflow-work-items-work-item-id-release-post';
import { apiWorkflowWorkItemsGroupGroupIdGet } from '@shared/models/fn/work-items/api-workflow-work-items-group-group-id-get';
import type { WorkItemDto } from '@shared/models/models/Workflow/Application/DTOs/work-item-dto';
import type { CompleteWorkItemPayload, WorkflowWorkItemExtras } from '@core/models/workflow-ops.models';

export type WorkflowWorkItemView = WorkItemDto & WorkflowWorkItemExtras;

@Injectable({ providedIn: 'root' })
export class WorkflowWorkItemsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getMyWorkItems(): Observable<WorkflowWorkItemView[]> {
    return apiWorkflowWorkItemsMyGet(this.http, this.config.rootUrl).pipe(
      map(r => (r.body as WorkflowWorkItemView[]) ?? [])
    );
  }

  getAvailable(): Observable<WorkflowWorkItemView[]> {
    return this.http.get<WorkflowWorkItemView[]>(
      `${this.config.rootUrl}/api/workflow/work-items/available`,
    );
  }

  getOverdue(): Observable<WorkflowWorkItemView[]> {
    return this.http.get<WorkflowWorkItemView[]>(
      `${this.config.rootUrl}/api/workflow/work-items/overdue`,
    );
  }

  getById(workItemId: string): Observable<WorkflowWorkItemView> {
    return apiWorkflowWorkItemsWorkItemIdGet(this.http, this.config.rootUrl, { workItemId }).pipe(
      map(r => r.body as WorkflowWorkItemView)
    );
  }

  getGroupWorkItems(groupId: string): Observable<WorkItemDto[]> {
    return apiWorkflowWorkItemsGroupGroupIdGet(this.http, this.config.rootUrl, { groupId }).pipe(
      map(r => r.body as WorkItemDto[])
    );
  }

  claim(workItemId: string): Observable<WorkflowWorkItemView> {
    return apiWorkflowWorkItemsWorkItemIdClaimPost(this.http, this.config.rootUrl, { workItemId }).pipe(
      map(r => r.body as WorkflowWorkItemView)
    );
  }

  complete(workItemId: string, req: CompleteWorkItemPayload): Observable<WorkflowWorkItemView> {
    return this.http.post<WorkflowWorkItemView>(
      `${this.config.rootUrl}/api/workflow/work-items/${workItemId}/complete`,
      req,
    );
  }

  release(workItemId: string): Observable<void> {
    return apiWorkflowWorkItemsWorkItemIdReleasePost(this.http, this.config.rootUrl, { workItemId }).pipe(
      map(() => undefined)
    );
  }

  reassign(workItemId: string, newAssignmentGroupId: string): Observable<WorkflowWorkItemView> {
    return this.http.post<WorkflowWorkItemView>(
      `${this.config.rootUrl}/api/workflow/work-items/${workItemId}/reassign`,
      { newAssignmentGroupId },
    );
  }
}
