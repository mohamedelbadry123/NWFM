import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiConfiguration } from '@shared/models/api-configuration';
import type {
  PaginatedWorkflowMessages,
  WorkflowIntegrationMessageDto,
} from '@core/models/workflow-ops.models';

@Injectable({ providedIn: 'root' })
export class WorkflowDeadLettersService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  listInboxDeadLetters(page = 1, pageSize = 50): Observable<PaginatedWorkflowMessages> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PaginatedWorkflowMessages>(
      `${this.config.rootUrl}/api/workflow/integration-messages/inbox/dead-letters`,
      { params }
    );
  }

  listFailedOutbox(page = 1, pageSize = 50): Observable<PaginatedWorkflowMessages> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PaginatedWorkflowMessages>(
      `${this.config.rootUrl}/api/workflow/integration-messages/outbox/failed`,
      { params }
    );
  }

  replayInbox(id: string): Observable<void> {
    return this.http.post<void>(
      `${this.config.rootUrl}/api/workflow/integration-messages/inbox/${id}/replay`,
      {}
    );
  }

  replayOutbox(id: string): Observable<void> {
    return this.http.post<void>(
      `${this.config.rootUrl}/api/workflow/integration-messages/outbox/${id}/replay`,
      {}
    );
  }
}
