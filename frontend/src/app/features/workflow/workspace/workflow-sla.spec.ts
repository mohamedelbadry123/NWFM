import { ElementRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { ToastService } from '@core/notifications/toast.service';
import { WorkflowActivitySlaComponent } from './workflow-activity-sla.component';
import { WorkflowSlaEditorComponent } from './workflow-sla-editor.component';
import { SlaCalendarOption, SlaContext, SlaRule, SlaRuleChange, WorkflowSlaRulesService } from './workflow-sla-rules.service';
import { WorkflowWorkspaceService } from './workflow-workspace.service';
import { parseThresholds, slaErrorMessage, slaUnitName, toDraft, toRule, validateSlaDraft } from './workflow-sla-rule.validation';

const calendar: SlaCalendarOption = { id: 'cal', name: 'Main', timeZone: 'UTC', hasWorkingPeriods: true };
const rule: SlaRule = { id: 'r1', name: 'Inspection', departmentCode: 'D1', fieldActivityCode: 'FA1', duration: 8, durationUnit: 'Hours', calendarId: 'cal', reminderMinutes: [60], overdueMinutes: [0], isActive: true };
const usage = { draftActivities: 2, draftWorkflows: 1, draftWorkflowNames: ['Other'], publishedVersions: 3 };
const context = (r: SlaRule | null, inactiveRules: SlaRule[] = []): SlaContext => ({ rule: r, calendarActive: true, inactiveRules, usage });
const t = (en: string) => en;

describe('SLA rule validation', () => {
  it('parses comma lists (Latin or Arabic comma) and rejects non-integers', () => {
    expect(parseThresholds('60, 15')).toEqual([60, 15]);
    expect(parseThresholds('60، 15')).toEqual([60, 15]);
    expect(parseThresholds('')).toEqual([]);
    expect(parseThresholds('1.5')).toBeNull();
    expect(parseThresholds('ten')).toBeNull();
  });

  it('mirrors the server rules', () => {
    const ok = toDraft(rule);
    expect(validateSlaDraft(ok, [calendar], 'r1')).toEqual({});
    expect(Object.keys(validateSlaDraft({ ...ok, duration: 0 }, [calendar]))).toEqual(['duration']);
    expect(Object.keys(validateSlaDraft({ ...ok, duration: 87601 }, [calendar]))).toEqual(['duration']);
    expect(Object.keys(validateSlaDraft({ ...ok, durationUnit: 'Weeks' }, [calendar]))).toEqual(['durationUnit']);
    expect(Object.keys(validateSlaDraft({ ...ok, reminders: '0' }, [calendar]))).toEqual(['reminders']);
    expect(Object.keys(validateSlaDraft({ ...ok, reminders: Array(11).fill(5).join(',') }, [calendar]))).toEqual(['reminders']);
    expect(Object.keys(validateSlaDraft({ ...ok, overdue: '-1' }, [calendar]))).toEqual(['overdue']);
    expect(Object.keys(validateSlaDraft({ ...ok, calendarId: '' }, [calendar]))).toEqual(['calendarId']);
    expect(Object.keys(validateSlaDraft(ok, []))).withContext('an inactive calendar is not offered').toEqual(['calendarId']);
    expect(Object.keys(validateSlaDraft({ ...ok, durationUnit: 'BusinessDays' }, [{ ...calendar, hasWorkingPeriods: false }]))).toEqual(['calendarId']);
    expect(Object.keys(validateSlaDraft({ ...ok, departmentCode: '', fieldActivityCode: '' }, [calendar]))).toEqual(['departmentCode', 'fieldActivityCode']);
  });

  it('blocks a second active rule for the same combination but allows an inactive one', () => {
    const second = { ...toDraft(null, { departmentCode: 'D1', fieldActivityCode: 'FA1' }), name: 'Second', calendarId: 'cal' };
    expect(Object.keys(validateSlaDraft(second, [calendar], 'r1'))).toEqual(['isActive']);
    expect(validateSlaDraft({ ...second, isActive: false }, [calendar], 'r1')).toEqual({});
  });

  it('round-trips a rule and reads snapshot units stored by value', () => {
    expect(toRule(toDraft(rule))).toEqual(rule);
    expect(slaUnitName(2)).toBe('BusinessHours');
    expect(slaUnitName('Days')).toBe('Days');
  });

  it('prefers the server message and explains permission and connectivity failures', () => {
    expect(slaErrorMessage(new HttpErrorResponse({ status: 400, error: { code: 'Sla.Invalid', message: 'An active SLA rule already exists.' } }), t)).toBe('An active SLA rule already exists.');
    expect(slaErrorMessage(new HttpErrorResponse({ status: 403 }), t)).toContain('permission');
    expect(slaErrorMessage(new HttpErrorResponse({ status: 0 }), t)).toContain('reach the server');
  });
});

describe('Activity SLA tab', () => {
  let changes: Subject<SlaRuleChange>;
  let api: jasmine.SpyObj<WorkflowSlaRulesService> & { changes$: Subject<SlaRuleChange> };
  let permissions: string[];
  function create() {
    TestBed.configureTestingModule({ providers: [
      { provide: LocaleService, useValue: { locale: () => 'en' } },
      { provide: AuthStore, useValue: { roles: () => [], hasAnyPermission: (...p: string[]) => p.some(x => permissions.includes(x)) } },
      { provide: WorkflowSlaRulesService, useValue: api },
    ] });
    const component = TestBed.runInInjectionContext(() => new WorkflowActivitySlaComponent());
    component.ngOnInit();
    return component;
  }
  const configure = (component: WorkflowActivitySlaComponent, config: object, readonly = false) => { component.configuration = JSON.stringify(config); component.readonly = readonly; component.ngOnChanges(); };
  beforeEach(() => {
    changes = new Subject(); permissions = ['ManageSlaPolicies'];
    api = Object.assign(jasmine.createSpyObj<WorkflowSlaRulesService>('api', ['context']), { changes$: changes });
  });

  it('names the missing selections instead of looking up a rule', () => {
    const component = create();
    configure(component, { departmentCode: 'D1' });
    expect(component.missing()).toEqual(['fieldActivityCode']);
    expect(api.context).not.toHaveBeenCalled();
  });

  it('ignores a late response for a combination the activity no longer has', () => {
    const first = new Subject<SlaContext>(); const second = new Subject<SlaContext>();
    api.context.and.callFake((_d: string, f: string) => f === 'FA1' ? first : second);
    const component = create();
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA1' });
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA2' });
    second.next(context(null));
    first.next(context(rule));
    expect(component.context()!.rule).toBeNull();
  });

  it('does not re-query when unrelated activity settings change', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create();
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA1' });
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA1', rejectTargetNodeKey: 'x' });
    expect(api.context).toHaveBeenCalledTimes(1);
  });

  it('refreshes after a matching rule is saved anywhere, and only then', () => {
    api.context.and.returnValue(of(context(null)));
    const component = create();
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA1' });
    changes.next({ rule: { ...rule, departmentCode: 'D2' } });
    expect(api.context).toHaveBeenCalledTimes(1);
    api.context.and.returnValue(of(context(rule)));
    changes.next({ rule });
    expect(api.context).toHaveBeenCalledTimes(2);
    expect(component.context()!.rule!.id).toBe('r1');
    changes.next({ rule: { ...rule, departmentCode: 'D9' }, previous: { departmentCode: 'D1', fieldActivityCode: 'FA1' } });
    expect(api.context).withContext('a rule moved away from this combination').toHaveBeenCalledTimes(3);
  });

  it('shows the published snapshot on read-only versions without querying live rules', () => {
    const component = create();
    configure(component, { departmentCode: 'D1', fieldActivityCode: 'FA1', publishedSla: { name: 'Captured', duration: 2, durationUnit: 4, timeZone: 'UTC', reminderMinutes: [], overdueMinutes: [0] } }, true);
    expect(component.snapshot()!.name).toBe('Captured');
    expect(component.unit(component.snapshot()!.durationUnit)).toBe('Business days');
    expect(api.context).not.toHaveBeenCalled();
    changes.next({ rule });
    expect(api.context).not.toHaveBeenCalled();
  });

  it('only offers management to users who can manage SLA rules', () => {
    permissions = [];
    expect(create().canManage()).toBeFalse();
  });
});

describe('SLA editor', () => {
  let api: jasmine.SpyObj<WorkflowSlaRulesService>;
  let toast: jasmine.SpyObj<ToastService>;
  function create(request: WorkflowSlaEditorComponent['request']) {
    TestBed.configureTestingModule({ providers: [
      { provide: LocaleService, useValue: { locale: () => 'en', isRtl: () => false } },
      { provide: AuthStore, useValue: { roles: () => ['Administrator'], hasAnyPermission: () => true } },
      { provide: WorkflowSlaRulesService, useValue: api },
      { provide: WorkflowWorkspaceService, useValue: { references: (kind: string) => of(kind === 'departments' ? [{ id: '1', code: 'D1', nameEn: 'Operations', nameAr: 'العمليات' }] : [{ id: '2', code: 'FA1', nameEn: 'Inspection', nameAr: 'فحص' }]) } },
      { provide: ToastService, useValue: toast },
      { provide: ElementRef, useValue: new ElementRef(document.createElement('div')) },
    ] });
    const component = TestBed.runInInjectionContext(() => new WorkflowSlaEditorComponent());
    component.request = request; component.ngOnInit();
    return component;
  }
  beforeEach(() => {
    api = jasmine.createSpyObj<WorkflowSlaRulesService>('api', ['calendars', 'context', 'save']);
    toast = jasmine.createSpyObj<ToastService>('toast', ['success', 'error']);
    api.calendars.and.returnValue(of([calendar]));
  });

  it('prefills a new rule from the activity and saves it for that combination only', () => {
    api.context.and.returnValue(of(context(null)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1', versionId: 'v1', currentWorkflowMatches: 2 });
    expect(api.context).toHaveBeenCalledWith('D1', 'FA1', 'v1');
    expect(component.isNew()).toBeTrue();
    expect(component.draft().name).toBe('Operations · Inspection');
    const saved = new Subject<SlaRule>(); api.save.and.returnValue(saved);
    const closed: (SlaRule | null)[] = []; component.closed.subscribe(r => closed.push(r));
    component.patch({ calendarId: 'cal' });
    component.save(); component.save();
    expect(api.save).toHaveBeenCalledTimes(1);
    expect(api.save.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ id: '', departmentCode: 'D1', fieldActivityCode: 'FA1', calendarId: 'cal' }));
    component.requestClose();
    expect(closed).withContext('cannot close while saving').toEqual([]);
    saved.next({ ...rule, id: 'new' });
    expect(closed).toEqual([{ ...rule, id: 'new' }]);
    expect(toast.success.calls.mostRecent().args[0]).toContain('not saved or published');
    expect(component.impact().join(' ')).toContain('In this workflow: 2 activities');
  });

  it('keeps the input after a failed save and succeeds on retry', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    component.patch({ duration: 12, reminders: '30, 10' });
    api.save.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'The calendar timezone is invalid.' } })));
    component.save();
    expect(component.saveError()).toBe('The calendar timezone is invalid.');
    expect(component.draft().duration).toBe(12);
    expect(component.draft().reminders).toBe('30, 10');
    expect(component.saving()).toBeFalse();
    api.save.and.returnValue(of({ ...rule, duration: 12 }));
    component.save();
    expect(api.save).toHaveBeenCalledTimes(2);
    expect(api.save.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ id: 'r1', duration: 12, reminderMinutes: [30, 10] }));
  });

  it('does not submit invalid input', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    component.patch({ duration: 0, overdue: 'soon' });
    component.save();
    expect(api.save).not.toHaveBeenCalled();
    expect(component.err('duration')).toContain('whole number');
    expect(component.err('overdue')).toContain('whole minutes');
  });

  it('asks before discarding edits and never saves on cancel', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    const closed: (SlaRule | null)[] = []; component.closed.subscribe(r => closed.push(r));
    component.patch({ name: 'Changed' });
    component.requestClose();
    expect(component.confirmDiscard()).toBeTrue();
    expect(closed).toEqual([]);
    component.keepEditing();
    expect(component.draft().name).toBe('Changed');
    component.requestClose(); component.discard();
    expect(closed).toEqual([null]);
    expect(api.save).not.toHaveBeenCalled();
  });

  it('keeps the unsaved rule while calendars are managed and applies the chosen calendar', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    component.patch({ duration: 3 });
    component.showCalendars();
    api.calendars.and.returnValue(of([calendar, { id: 'cal2', name: 'Night', timeZone: 'Asia/Riyadh', hasWorkingPeriods: true }]));
    component.reloadCalendars();
    component.useCalendar('cal2');
    expect(component.view()).toBe('rule');
    expect(component.draft()).toEqual(jasmine.objectContaining({ duration: 3, calendarId: 'cal2' }));
    expect(component.errors()).toEqual({});
  });

  it('offers to reactivate an inactive rule instead of duplicating it', () => {
    const inactive = { ...rule, id: 'old', isActive: false };
    api.context.and.returnValue(of(context(null, [inactive])));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    expect(component.inactiveCandidate()!.id).toBe('old');
    component.reuse(inactive);
    expect(component.draft()).toEqual(jasmine.objectContaining({ id: 'old', isActive: true }));
    expect(component.dirty()).toBeTrue();
  });

  it('explains the effect of deactivating a shared rule', () => {
    api.context.and.returnValue(of(context(rule)));
    const component = create({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    component.patch({ isActive: false });
    expect(component.impact().join(' ')).toContain('cannot be published until an active rule exists');
    expect(component.impact().join(' ')).toContain('3 published versions keep');
  });
});
