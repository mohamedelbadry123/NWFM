import { ElementRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowActivityFormsComponent } from './workflow-activity-forms.component';
import { WorkflowFormPreviewComponent } from './workflow-form-preview.component';
import { ActivityForm, ActivityFormPage, ActivityFormPreview, WorkflowActivityFormsService } from './workflow-activity-forms.service';
import { WorkflowWorkspaceService } from './workflow-workspace.service';

const form = (code: string, usable = true, extra: Partial<ActivityForm> = {}): ActivityForm => ({
  id: code, code, nameEn: code + ' EN', nameAr: code + ' AR', category: 'INSPECTION', status: usable ? 'PUBLISHED' : 'DRAFT',
  departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', currentVersionNo: usable ? 1 : null, versionNos: usable ? [1] : [], isUsable: usable,
  updatedAt: '2026-09-29T00:00:00Z', ...extra,
});
const page = (items: ActivityForm[], totalCount = items.length, usableCount = items.filter(f => f.isUsable).length, pageNumber = 1): ActivityFormPage =>
  ({ items, totalCount, usableCount, pageNumber, pageSize: 20 });

describe('WorkflowActivityFormsComponent (Form tab)', () => {
  let api: jasmine.SpyObj<WorkflowActivityFormsService>;
  let locale: 'en' | 'ar';
  function create() {
    TestBed.configureTestingModule({ providers: [
      { provide: LocaleService, useValue: { locale: () => locale } },
      { provide: WorkflowActivityFormsService, useValue: api },
      { provide: WorkflowWorkspaceService, useValue: { references: (kind: string) => of(kind === 'departments'
        ? [{ id: '1', code: '10', nameEn: 'Water Network', nameAr: 'شبكة المياه' }]
        : [{ id: '2', code: 'LEAK_REPAIR', nameEn: 'Leak repair', nameAr: 'إصلاح التسربات' }]) } },
    ] });
    return TestBed.runInInjectionContext(() => new WorkflowActivityFormsComponent());
  }
  const configure = (component: WorkflowActivityFormsComponent, config: object) => { component.configuration = JSON.stringify(config); component.ngOnChanges(); };
  const context = { departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR' };
  beforeEach(() => { locale = 'en'; api = jasmine.createSpyObj<WorkflowActivityFormsService>('api', ['list', 'preview']); });

  it('names the missing selections and never asks for the whole catalog', () => {
    const component = create();
    configure(component, { departmentCode: '10' });
    expect(component.missing()).toEqual(['fieldActivityCode']);
    configure(component, {});
    expect(component.missing()).toEqual(['departmentCode', 'fieldActivityCode']);
    expect(api.list).not.toHaveBeenCalled();
    const go = jasmine.createSpy('editContext');
    component.editContext.subscribe(go);
    component.editContext.emit(component.missing()[0]);
    expect(go).toHaveBeenCalledWith('departmentCode');
  });

  it('loads the forms of both codes as soon as they are chosen, usable ones apart from the rest', () => {
    api.list.and.returnValue(of(page([form('DEMO-LEAK-INSPECTION'), form('DEMO-LEAK-REPAIR-COMPLETION'), form('DEMO-LEAK-REPAIR-CHECKLIST', false)])));
    const component = create();
    configure(component, context);
    expect(api.list).toHaveBeenCalledOnceWith('10', 'LEAK_REPAIR', 1);
    expect(component.usable().map(f => f.code)).toEqual(['DEMO-LEAK-INSPECTION', 'DEMO-LEAK-REPAIR-COMPLETION']);
    expect(component.unusable().map(f => f.code)).toEqual(['DEMO-LEAK-REPAIR-CHECKLIST']);
    expect(component.departmentName()).toBe('Water Network (10)');
    expect(component.versionText(component.unusable()[0])).toContain('Never published');
  });

  it('keeps the list for unrelated edits and reloads for a different combination', () => {
    api.list.and.returnValue(of(page([form('A')])));
    const component = create();
    configure(component, context);
    configure(component, { ...context, rejectTargetNodeKey: 'review' });
    expect(api.list).toHaveBeenCalledTimes(1);
    api.list.and.returnValue(of(page([])));
    configure(component, { departmentCode: '10', fieldActivityCode: '01' });
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(component.items()).toEqual([]);
    expect(component.total()).toBe(0);
  });

  it('drops a late response for a combination the activity no longer has', () => {
    const slow = new Subject<ActivityFormPage>(), fast = new Subject<ActivityFormPage>();
    api.list.and.returnValues(slow, fast);
    const component = create();
    configure(component, context);
    configure(component, { departmentCode: '10', fieldActivityCode: '01' });
    fast.next(page([form('ISOLATION')]));
    slow.next(page([form('LEAK-A'), form('LEAK-B')]));
    expect(component.items().map(f => f.code)).toEqual(['ISOLATION']);
    expect(component.loading()).toBeFalse();
  });

  it('clears stale results while the new combination loads', () => {
    const next = new Subject<ActivityFormPage>();
    api.list.and.returnValues(of(page([form('LEAK-A')])), next);
    const component = create();
    configure(component, context);
    configure(component, { departmentCode: '10', fieldActivityCode: '01' });
    expect(component.items()).toEqual([]);
    expect(component.loading()).toBeTrue();
  });

  it('pages through every match instead of stopping at the first page', () => {
    const first = Array.from({ length: 20 }, (_, i) => form(`F${String(i).padStart(2, '0')}`));
    api.list.and.returnValues(of(page(first, 23)), of(page([form('F20'), form('F21'), form('F22')], 23, 23, 2)));
    const component = create();
    configure(component, context);
    expect(component.summary()).toContain('Showing 20 of 23');
    component.loadMore();
    expect(api.list.calls.mostRecent().args).toEqual(['10', 'LEAK_REPAIR', 2]);
    expect(component.items().length).toBe(23);
  });

  it('ignores a next page that arrives after the context changed', () => {
    const more = new Subject<ActivityFormPage>();
    api.list.and.returnValues(of(page([form('LEAK-A')], 21)), more, of(page([form('ISOLATION')])));
    const component = create();
    configure(component, context);
    component.loadMore();
    configure(component, { departmentCode: '10', fieldActivityCode: '01' });
    more.next(page([form('LEAK-Z')], 21, 21, 2));
    expect(component.items().map(f => f.code)).toEqual(['ISOLATION']);
  });

  it('shows no matches, a permission problem, and a retryable failure distinctly', () => {
    api.list.and.returnValue(of(page([])));
    const component = create();
    configure(component, context);
    expect(component.total()).toBe(0);
    expect(component.error()).toBe('');

    api.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    component.load();
    expect(component.forbidden()).toBeTrue();
    expect(component.error()).toBe('');

    api.list.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.load();
    expect(component.forbidden()).toBeFalse();
    expect(component.error()).toContain('could not be loaded');

    api.list.and.returnValue(of(page([form('LEAK-A')])));
    component.load();
    expect(component.error()).toBe('');
    expect(component.items().length).toBe(1);
  });

  it('does not look forms up for a legacy workflow, and mentions preserved inline questions', () => {
    const component = create();
    component.legacy = true;
    configure(component, { ...context, formFields: [{ key: 'note' }, { key: 'count' }] });
    expect(api.list).not.toHaveBeenCalled();
    expect(component.legacyFields()).toBe(2);
  });

  it('opens the preview at the current version, with the activity context', () => {
    api.list.and.returnValue(of(page([form('LEAK-A', true, { currentVersionNo: 3, versionNos: [3, 2, 1] })])));
    const component = create();
    configure(component, context);
    const opened = jasmine.createSpy('preview');
    component.preview.subscribe(opened);
    component.open(component.usable()[0]);
    expect(opened).toHaveBeenCalledWith(jasmine.objectContaining({ departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', versionNo: 3 }));
  });

  it('speaks Arabic', () => {
    locale = 'ar';
    api.list.and.returnValue(of(page([form('LEAK-A')])));
    const component = create();
    configure(component, context);
    expect(component.name(component.usable()[0])).toBe('LEAK-A AR');
    expect(component.status(component.usable()[0])).toBe('منشور');
    expect(component.fieldActivityName()).toBe('إصلاح التسربات (LEAK_REPAIR)');
  });
});

describe('WorkflowFormPreviewComponent', () => {
  let api: jasmine.SpyObj<WorkflowActivityFormsService>;
  function create(versionNos = [2, 1]) {
    TestBed.configureTestingModule({ providers: [
      { provide: LocaleService, useValue: { locale: () => 'en' } },
      { provide: WorkflowActivityFormsService, useValue: api },
      { provide: ElementRef, useValue: new ElementRef(document.createElement('div')) },
    ] });
    const component = TestBed.runInInjectionContext(() => new WorkflowFormPreviewComponent());
    component.request = { form: form('LEAK-A', true, { currentVersionNo: 2, versionNos }), departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', versionNo: 2 };
    component.ngOnInit();
    return component;
  }
  const preview = (versionNo: number) => of({ formId: 'LEAK-A', code: 'LEAK-A', nameEn: 'A', nameAr: 'A', status: 'PUBLISHED', versionNo, isUsable: true, schemaJson: `{"name_en":"v${versionNo}","elements":[]}` });
  beforeEach(() => { api = jasmine.createSpyObj<WorkflowActivityFormsService>('api', ['list', 'preview']); });

  it('reads the pinned version through the activity context and never submits', () => {
    api.preview.and.callFake((_id, versionNo) => preview(versionNo));
    const component = create();
    expect(api.preview).toHaveBeenCalledWith('LEAK-A', 2, '10', 'LEAK_REPAIR');
    expect(component.definition()).toEqual({ name_en: 'v2', elements: [] });
    component.select(1);
    expect(api.preview.calls.mostRecent().args).toEqual(['LEAK-A', 1, '10', 'LEAK_REPAIR']);
    expect(component.definition()).toEqual({ name_en: 'v1', elements: [] });
    // Only reads: the service offers no way to write, and the preview asked for nothing else.
    expect(api.list).not.toHaveBeenCalled();
  });

  it('drops a slower response for a version no longer selected', () => {
    const slow = new Subject<ActivityFormPreview>();
    api.preview.and.returnValues(slow, preview(1));
    const component = create();
    component.select(1);
    slow.next({ formId: 'LEAK-A', code: 'LEAK-A', nameEn: 'A', nameAr: 'A', status: 'PUBLISHED', versionNo: 2, isUsable: true, schemaJson: '{"name_en":"v2","elements":[]}' });
    expect(component.definition()).toEqual({ name_en: 'v1', elements: [] });
  });

  it('explains a form that is no longer filed under the activity, without offering a retry', () => {
    api.preview.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const component = create();
    expect(component.error()).toContain('no longer filed');
    expect(component.canRetry()).toBeFalse();
  });

  it('closes on Escape', () => {
    api.preview.and.callFake((_id, versionNo) => preview(versionNo));
    const component = create();
    const closed = jasmine.createSpy('closed');
    component.closed.subscribe(closed);
    component.onEscape(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toHaveBeenCalled();
  });
});
