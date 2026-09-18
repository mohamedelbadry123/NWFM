import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowSlaPoliciesGet } from '@shared/models/fn/sla-policies/api-workflow-sla-policies-get';
import type { SlaPolicyDto } from '@shared/models/models/Workflow/Application/DTOs/sla-policy-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowSlaPoliciesService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getAll(pageSize = 100): Observable<SlaPolicyDto[]> {
    return apiWorkflowSlaPoliciesGet(this.http, this.config.rootUrl, { page: 1, pageSize }).pipe(
      map(r => (r.body as { items?: SlaPolicyDto[] })?.items ?? [])
    );
  }
}
