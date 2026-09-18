import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiConfiguration } from '@shared/models/api-configuration';

export interface WorkflowNotificationLogView {
  id: string;
  organizationId: string;
  templateKey: string;
  channels: string;
  status: string;
  correlationId?: string | null;
  createdAt: string;
  variablesJsonTruncated?: string | null;
}

export interface WorkflowNotificationLogPageView {
  items: WorkflowNotificationLogView[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class WorkflowNotificationsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  list(page = 1, pageSize = 20): Observable<WorkflowNotificationLogPageView> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<WorkflowNotificationLogPageView>(
      `${this.config.rootUrl}/api/workflow/notifications`,
      { params }
    );
  }
}
