import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiConfiguration } from '@shared/models/api-configuration';
export interface SlaRule { id: string; name: string; departmentCode: string; fieldActivityCode: string; duration: number; durationUnit: string; calendarId: string; calendarName?: string; timeZone?: string; reminderMinutes: number[]; overdueMinutes: number[]; isActive: boolean; }
@Injectable({ providedIn: 'root' })
export class WorkflowSlaRulesService {
  private http = inject(HttpClient); private base = inject(ApiConfiguration).rootUrl + '/api/workflow/workspace/sla';
  list() { return this.http.get<SlaRule[]>(this.base); }
  resolve(departmentCode: string, fieldActivityCode: string) { return this.http.get<SlaRule | null>(this.base + '/resolve', {params: {departmentCode, fieldActivityCode}}); }
  save(rule: SlaRule) { return rule.id ? this.http.put<SlaRule>(this.base + '/' + rule.id, rule) : this.http.post<SlaRule>(this.base, rule); }
}
