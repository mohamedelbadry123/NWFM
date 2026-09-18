import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowModuleCatalogGet } from '@shared/models/fn/workflow-module-catalog/api-workflow-module-catalog-get';
import type { ModuleCatalogEntry } from '@shared/models/models/Workflow/Application/Constants/module-catalog-entry';

@Injectable({ providedIn: 'root' })
export class WorkflowModuleCatalogService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getCatalog(): Observable<ModuleCatalogEntry[]> {
    return apiWorkflowModuleCatalogGet(this.http, this.config.rootUrl).pipe(
      map(r => r.body as ModuleCatalogEntry[])
    );
  }
}
