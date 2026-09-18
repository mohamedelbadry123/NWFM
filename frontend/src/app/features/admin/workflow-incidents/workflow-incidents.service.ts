import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowIncidentsGet } from '@shared/models/fn/workflow-incidents/api-workflow-incidents-get';
import { apiWorkflowIncidentsIdGet } from '@shared/models/fn/workflow-incidents/api-workflow-incidents-id-get';
import type { WorkflowIncidentDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-incident-dto';
import type { PaginatedResultOfWorkflowIncidentDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-incident-dto';
import type { WorkflowIncidentStatus } from '@shared/models/models/Workflow/Domain/Enums/workflow-incident-status';

@Injectable({ providedIn: 'root' })
export class WorkflowIncidentsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  list(params: {
    page?: number;
    pageSize?: number;
    organizationId?: string;
    status?: WorkflowIncidentStatus;
  } = {}): Observable<PaginatedResultOfWorkflowIncidentDto> {
    return apiWorkflowIncidentsGet(this.http, this.config.rootUrl, params).pipe(
      map(r => r.body as PaginatedResultOfWorkflowIncidentDto)
    );
  }

  getById(id: string): Observable<WorkflowIncidentDto> {
    return apiWorkflowIncidentsIdGet(this.http, this.config.rootUrl, { id }).pipe(
      map(r => r.body as WorkflowIncidentDto)
    );
  }
}
