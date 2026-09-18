import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiConfiguration } from '@shared/models/api-configuration';
import type { WorkflowWorkloadDto } from '@core/models/workflow-ops.models';

@Injectable({ providedIn: 'root' })
export class WorkflowWorkloadService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getWorkload(): Observable<WorkflowWorkloadDto> {
    return this.http.get<WorkflowWorkloadDto>(`${this.config.rootUrl}/api/workflow/workload`);
  }
}
