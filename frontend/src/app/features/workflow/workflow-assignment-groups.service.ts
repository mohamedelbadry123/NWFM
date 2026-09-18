import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowAssignmentGroupsGet } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-get';
import { apiWorkflowAssignmentGroupsPost } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-post';
import { apiWorkflowAssignmentGroupsIdGet } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-id-get';
import { apiWorkflowAssignmentGroupsIdPut } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-id-put';
import { apiWorkflowAssignmentGroupsIdMembersPost } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-id-members-post';
import { apiWorkflowAssignmentGroupsIdMembersParticipantIdDelete } from '@shared/models/fn/workflow-assignment-groups/api-workflow-assignment-groups-id-members-participant-id-delete';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { WorkflowAssignmentGroupDetailDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-detail-dto';
import type { WorkflowGroupMemberDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-group-member-dto';
import type { CreateAssignmentGroupRequest } from '@shared/models/models/Workflow/Api/Controllers/create-assignment-group-request';
import type { UpdateAssignmentGroupRequest } from '@shared/models/models/Workflow/Api/Controllers/update-assignment-group-request';
import type { AddGroupMemberRequest } from '@shared/models/models/Workflow/Api/Controllers/add-group-member-request';
import type { PaginatedResultOfWorkflowAssignmentGroupDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-assignment-group-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowAssignmentGroupsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getPaged(page: number, pageSize: number, search?: string): Observable<PaginatedResultOfWorkflowAssignmentGroupDto> {
    return apiWorkflowAssignmentGroupsGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowAssignmentGroupDto)
    );
  }

  getById(id: string): Observable<WorkflowAssignmentGroupDetailDto> {
    return apiWorkflowAssignmentGroupsIdGet(this.http, this.config.rootUrl, { id }).pipe(
      map(r => r.body)
    );
  }

  create(req: CreateAssignmentGroupRequest): Observable<WorkflowAssignmentGroupDto> {
    return apiWorkflowAssignmentGroupsPost(this.http, this.config.rootUrl, { body: req }).pipe(
      map(r => r.body)
    );
  }

  update(id: string, req: UpdateAssignmentGroupRequest): Observable<WorkflowAssignmentGroupDto> {
    return apiWorkflowAssignmentGroupsIdPut(this.http, this.config.rootUrl, { id, body: req }).pipe(
      map(r => r.body)
    );
  }

  addMember(groupId: string, req: AddGroupMemberRequest): Observable<WorkflowGroupMemberDto> {
    return apiWorkflowAssignmentGroupsIdMembersPost(this.http, this.config.rootUrl, { id: groupId, body: req }).pipe(
      map(r => r.body)
    );
  }

  removeMember(groupId: string, participantId: string): Observable<void> {
    return apiWorkflowAssignmentGroupsIdMembersParticipantIdDelete(
      this.http, this.config.rootUrl, { id: groupId, participantId }
    ).pipe(map(() => undefined));
  }
}
