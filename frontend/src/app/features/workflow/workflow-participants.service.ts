import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowParticipantsGet } from '@shared/models/fn/workflow-participants/api-workflow-participants-get';
import { apiWorkflowParticipantsPost } from '@shared/models/fn/workflow-participants/api-workflow-participants-post';
import { apiWorkflowParticipantsIdDelete } from '@shared/models/fn/workflow-participants/api-workflow-participants-id-delete';
import type { WorkflowParticipantDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-participant-dto';
import type { RegisterParticipantRequest } from '@shared/models/models/Workflow/Api/Controllers/register-participant-request';
import type { PaginatedResultOfWorkflowParticipantDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-workflow-participant-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowParticipantsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  getPaged(page: number, pageSize: number, search?: string): Observable<PaginatedResultOfWorkflowParticipantDto> {
    return apiWorkflowParticipantsGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => r.body as PaginatedResultOfWorkflowParticipantDto)
    );
  }

  register(req: RegisterParticipantRequest): Observable<WorkflowParticipantDto> {
    return apiWorkflowParticipantsPost(this.http, this.config.rootUrl, { body: req }).pipe(
      map(r => r.body)
    );
  }

  deactivate(id: string): Observable<void> {
    return apiWorkflowParticipantsIdDelete(this.http, this.config.rootUrl, { id }).pipe(
      map(() => undefined)
    );
  }
}
