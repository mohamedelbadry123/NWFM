import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowDefinitionsGet } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-get';
import { apiWorkflowDefinitionsIdGet } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-id-get';
import { apiWorkflowDefinitionsPost } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-post';
import { apiWorkflowDefinitionsIdPut } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-id-put';
import { apiWorkflowDefinitionsIdActivatePost } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-id-activate-post';
import { apiWorkflowDefinitionsIdDeactivatePost } from '@shared/models/fn/workflow-definitions/api-workflow-definitions-id-deactivate-post';
import type { WorkflowDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-definition-dto';
import type { CreateWorkflowDefinitionRequest } from '@shared/models/models/Workflow/Api/Controllers/create-workflow-definition-request';
import type { UpdateWorkflowDefinitionRequest } from '@shared/models/models/Workflow/Api/Controllers/update-workflow-definition-request';
import type { PaginatedResultOfWorkflowDefinitionDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-definition-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowDefinitionsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getPaged(page: number, pageSize: number, search?: string, organizationId?: string): Observable<PaginatedResultOfWorkflowDefinitionDto> {
    return apiWorkflowDefinitionsGet(this.http, this.config.rootUrl, { page, pageSize, search, organizationId }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowDefinitionDto)
    );
  }

  getById(id: string): Observable<WorkflowDefinitionDto> {
    return apiWorkflowDefinitionsIdGet(this.http, this.config.rootUrl, { id }).pipe(
      map(r => r.body as WorkflowDefinitionDto)
    );
  }

  create(req: CreateWorkflowDefinitionRequest): Observable<WorkflowDefinitionDto> {
    return apiWorkflowDefinitionsPost(this.http, this.config.rootUrl, { body: req }).pipe(
      map(r => r.body as WorkflowDefinitionDto)
    );
  }

  update(id: string, req: UpdateWorkflowDefinitionRequest): Observable<WorkflowDefinitionDto> {
    return apiWorkflowDefinitionsIdPut(this.http, this.config.rootUrl, { id, body: req }).pipe(
      map(r => r.body as WorkflowDefinitionDto)
    );
  }

  activate(id: string): Observable<WorkflowDefinitionDto> {
    return apiWorkflowDefinitionsIdActivatePost(this.http, this.config.rootUrl, { id }).pipe(
      map(r => r.body as WorkflowDefinitionDto)
    );
  }

  deactivate(id: string): Observable<WorkflowDefinitionDto> {
    return apiWorkflowDefinitionsIdDeactivatePost(this.http, this.config.rootUrl, { id }).pipe(
      map(r => r.body as WorkflowDefinitionDto)
    );
  }
}
