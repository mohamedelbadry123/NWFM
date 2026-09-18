import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowCalendarsGet } from '@shared/models/fn/business-calendars/api-workflow-calendars-get';
import { apiWorkflowCalendarsPost } from '@shared/models/fn/business-calendars/api-workflow-calendars-post';
import type { BusinessCalendarDto } from '@shared/models/models/Workflow/Application/DTOs/business-calendar-dto';
import type { CreateBusinessCalendarRequest } from '@shared/models/models/Workflow/Api/Controllers/create-business-calendar-request';
import type { PaginatedResultOfBusinessCalendarDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-business-calendar-dto';

@Injectable({ providedIn: 'root' })
export class WorkflowCalendarsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);

  list(page = 1, pageSize = 100, search?: string): Observable<BusinessCalendarDto[]> {
    return apiWorkflowCalendarsGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => (r.body as PaginatedResultOfBusinessCalendarDto).items ?? [])
    );
  }

  create(body: CreateBusinessCalendarRequest): Observable<BusinessCalendarDto> {
    return apiWorkflowCalendarsPost(this.http, this.config.rootUrl, { body }).pipe(
      map(r => r.body as BusinessCalendarDto)
    );
  }
}
