import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowSlaPoliciesGet } from '@shared/models/fn/sla-policies/api-workflow-sla-policies-get';
import { apiWorkflowSlaPoliciesPost } from '@shared/models/fn/sla-policies/api-workflow-sla-policies-post';
import type { SlaPolicyDto } from '@shared/models/models/Workflow/Application/DTOs/sla-policy-dto';
import type { CreateSlaPolicyRequest } from '@shared/models/models/Workflow/Api/Controllers/create-sla-policy-request';
import type { PaginatedResultOfSlaPolicyDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-sla-policy-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowSlaPoliciesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  list(page = 1, pageSize = 100, search?: string): Observable<SlaPolicyDto[]> {
    return apiWorkflowSlaPoliciesGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => (r.body as PaginatedResultOfSlaPolicyDto).items ?? [])
    );
  }

  create(body: CreateSlaPolicyRequest): Observable<SlaPolicyDto> {
    return apiWorkflowSlaPoliciesPost(this.http, this.config.rootUrl, { body }).pipe(
      map(r => r.body as SlaPolicyDto)
    );
  }
}
