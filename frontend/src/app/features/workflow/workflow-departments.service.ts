import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowDepartmentsGet } from '@shared/models/fn/workflow-departments/api-workflow-departments-get';
import { apiWorkflowDepartmentsPost } from '@shared/models/fn/workflow-departments/api-workflow-departments-post';
import { apiWorkflowDepartmentsIdPut } from '@shared/models/fn/workflow-departments/api-workflow-departments-id-put';
import { apiWorkflowDepartmentsIdMembersPost } from '@shared/models/fn/workflow-departments/api-workflow-departments-id-members-post';
import { apiWorkflowDepartmentsIdMembersParticipantIdDelete } from '@shared/models/fn/workflow-departments/api-workflow-departments-id-members-participant-id-delete';
import type { WorkflowDepartmentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-department-dto';
import type { WorkflowDepartmentMemberDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-department-member-dto';
import type { CreateDepartmentRequest } from '@shared/models/models/Workflow/Api/Controllers/create-department-request';
import type { UpdateDepartmentRequest } from '@shared/models/models/Workflow/Api/Controllers/update-department-request';
import type { AddDepartmentMemberRequest } from '@shared/models/models/Workflow/Api/Controllers/add-department-member-request';
import type { PaginatedResultOfWorkflowDepartmentDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-department-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowDepartmentsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getPaged(page: number, pageSize: number, search?: string): Observable<PaginatedResultOfWorkflowDepartmentDto> {
    return apiWorkflowDepartmentsGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowDepartmentDto)
    );
  }

  create(req: CreateDepartmentRequest): Observable<WorkflowDepartmentDto> {
    return apiWorkflowDepartmentsPost(this.http, this.config.rootUrl, { body: req }).pipe(
      map(r => r.body)
    );
  }

  update(id: string, req: UpdateDepartmentRequest): Observable<WorkflowDepartmentDto> {
    return apiWorkflowDepartmentsIdPut(this.http, this.config.rootUrl, { id, body: req }).pipe(
      map(r => r.body)
    );
  }

  addMember(departmentId: string, req: AddDepartmentMemberRequest): Observable<WorkflowDepartmentMemberDto> {
    return apiWorkflowDepartmentsIdMembersPost(this.http, this.config.rootUrl, { id: departmentId, body: req }).pipe(
      map(r => r.body)
    );
  }

  removeMember(departmentId: string, participantId: string): Observable<void> {
    return apiWorkflowDepartmentsIdMembersParticipantIdDelete(
      this.http, this.config.rootUrl, { id: departmentId, participantId }
    ).pipe(map(() => undefined));
  }
}
