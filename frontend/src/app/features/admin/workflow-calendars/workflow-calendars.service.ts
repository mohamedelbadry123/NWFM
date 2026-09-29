import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiConfiguration } from '@shared/models/api-configuration';
import { apiWorkflowCalendarsGet } from '@shared/models/fn/business-calendars/api-workflow-calendars-get';
import { apiWorkflowCalendarsPost } from '@shared/models/fn/business-calendars/api-workflow-calendars-post';
import { apiWorkflowCalendarsIdGet } from '@shared/models/fn/business-calendars/api-workflow-calendars-id-get';
import { apiWorkflowCalendarsIdPut } from '@shared/models/fn/business-calendars/api-workflow-calendars-id-put';
import type { BusinessCalendarDto } from '@shared/models/models/Workflow/Application/DTOs/business-calendar-dto';
import type { CreateBusinessCalendarRequest } from '@shared/models/models/Workflow/Api/Controllers/create-business-calendar-request';
import type { UpdateBusinessCalendarRequest } from '@shared/models/models/Workflow/Api/Controllers/update-business-calendar-request';
import type { PaginatedResultOfBusinessCalendarDto } from '@shared/models/models/NWFM/Shared/Results/paginated-result-of-business-calendar-dto';

export interface CalendarPeriodInput { dayOfWeek: string; startTime: string; endTime: string; isWorkingTime?: boolean }
export interface CalendarHolidayInput { name: string; nameAr?: string | null; holidayDate: string; isRecurring: boolean }

@Injectable({ providedIn: 'root' })
export class WorkflowCalendarsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ApiConfiguration);
  private get base(): string { return this.config.rootUrl + '/api/workflow/calendars'; }

  list(page = 1, pageSize = 100, search?: string): Observable<BusinessCalendarDto[]> {
    return apiWorkflowCalendarsGet(this.http, this.config.rootUrl, { page, pageSize, search }).pipe(
      map(r => (r.body as PaginatedResultOfBusinessCalendarDto).items ?? [])
    );
  }

  get(id: string): Observable<BusinessCalendarDto> {
    return apiWorkflowCalendarsIdGet(this.http, this.config.rootUrl, { id }).pipe(map(r => r.body as BusinessCalendarDto));
  }

  create(body: CreateBusinessCalendarRequest): Observable<BusinessCalendarDto> {
    return apiWorkflowCalendarsPost(this.http, this.config.rootUrl, { body }).pipe(
      map(r => r.body as BusinessCalendarDto)
    );
  }

  update(id: string, body: UpdateBusinessCalendarRequest): Observable<BusinessCalendarDto> {
    return apiWorkflowCalendarsIdPut(this.http, this.config.rootUrl, { id, body }).pipe(map(r => r.body as BusinessCalendarDto));
  }

  /** Times are `HH:mm`; the API takes a TimeSpan. */
  addPeriod(id: string, period: CalendarPeriodInput): Observable<unknown> {
    return this.http.post(`${this.base}/${id}/periods`, {
      ...period, startTime: period.startTime + ':00', endTime: period.endTime + ':00', isWorkingTime: period.isWorkingTime ?? true,
    });
  }

  addHoliday(id: string, holiday: CalendarHolidayInput): Observable<unknown> {
    return this.http.post(`${this.base}/${id}/holidays`, holiday);
  }

  removeItem(id: string, kind: 'periods' | 'holidays', itemId: string): Observable<unknown> {
    return this.http.delete(`${this.base}/${id}/${kind}/${itemId}`);
  }
}
