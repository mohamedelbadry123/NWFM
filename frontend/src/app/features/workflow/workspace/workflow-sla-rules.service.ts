import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject, tap } from 'rxjs';
import { ApiConfiguration } from '@shared/models/api-configuration';
export interface SlaRule { id: string; name: string; departmentCode: string; fieldActivityCode: string; duration: number; durationUnit: string; calendarId: string; calendarName?: string; timeZone?: string; reminderMinutes: number[]; overdueMinutes: number[]; isActive: boolean; }
export interface SlaCalendarOption { id: string; name: string; timeZone: string; hasWorkingPeriods: boolean; }
/** Who else follows a Department + FA Type rule. Drafts pick up edits on publish; published versions keep their snapshot. */
export interface SlaUsage { draftActivities: number; draftWorkflows: number; draftWorkflowNames: string[]; publishedVersions: number; }
export interface SlaContext { rule: SlaRule | null; calendarActive: boolean; inactiveRules: SlaRule[]; usage: SlaUsage; }
/** Emitted after a rule is saved so every open SLA display for the same combination refreshes. */
export interface SlaRuleChange { rule: SlaRule; previous?: { departmentCode: string; fieldActivityCode: string }; }
export const slaKey = (departmentCode: string, fieldActivityCode: string) => departmentCode + '\u0000' + fieldActivityCode;
@Injectable({ providedIn: 'root' })
export class WorkflowSlaRulesService {
  private http = inject(HttpClient); private base = inject(ApiConfiguration).rootUrl + '/api/workflow/workspace/sla';
  private readonly changed = new Subject<SlaRuleChange>();
  readonly changes$ = this.changed.asObservable();
  list() { return this.http.get<SlaRule[]>(this.base); }
  calendars() { return this.http.get<SlaCalendarOption[]>(this.base + '/calendars'); }
  resolve(departmentCode: string, fieldActivityCode: string) { return this.http.get<SlaRule | null>(this.base + '/resolve', {params: {departmentCode, fieldActivityCode}}); }
  context(departmentCode: string, fieldActivityCode: string, excludeVersionId?: string) {
    return this.http.get<SlaContext>(this.base + '/context', {params: excludeVersionId ? {departmentCode, fieldActivityCode, excludeVersionId} : {departmentCode, fieldActivityCode}});
  }
  save(rule: SlaRule, previous?: SlaRuleChange['previous']): Observable<SlaRule> {
    const request = rule.id ? this.http.put<SlaRule>(this.base + '/' + rule.id, rule) : this.http.post<SlaRule>(this.base, rule);
    return request.pipe(tap(saved => this.changed.next({ rule: saved, previous })));
  }
}
