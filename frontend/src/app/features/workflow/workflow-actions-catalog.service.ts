import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowActionsCatalogGet } from '@shared/models/fn/workflow-actions-catalog/api-workflow-actions-catalog-get';
import type { WorkflowActionCatalogEntryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-action-catalog-entry-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowActionsCatalogService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getAll(): Observable<WorkflowActionCatalogEntryDto[]> {
    return apiWorkflowActionsCatalogGet(this.http, this.config.rootUrl).pipe(
      map(r => (r.body as WorkflowActionCatalogEntryDto[]) ?? [])
    );
  }
}
