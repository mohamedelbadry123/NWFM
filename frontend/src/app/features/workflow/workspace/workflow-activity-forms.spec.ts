import { beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
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

describe('WorkflowActivityFormsComponent (Task Type forms)', () => {
  let api:Record<string, Mock>;
  beforeEach(()=>{api={'listForTaskType': vi.fn(),'previewForTaskType': vi.fn()};api.listForTaskType.mockReturnValue(of(page([form('A'),form('B'),form('DRAFT',false)])));TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'en'}},{provide:WorkflowActivityFormsService,useValue:api}]});});
  function create(config:object={}){const c=TestBed.runInInjectionContext(()=>new WorkflowActivityFormsComponent());c.configuration=JSON.stringify(config);c.ngOnChanges();return c;}
  it('never loads the catalog without a Task Type',()=>{const c=create();expect(c.items()).toEqual([]);expect(api.listForTaskType).not.toHaveBeenCalled();});
  it('lists all associated forms, including unavailable ones',()=>{const c=create({taskTypeId:'T'});expect(api.listForTaskType).toHaveBeenCalledOnceWith('T');expect(c.items().map(f=>f.id)).toEqual(['A','B','DRAFT']);});
  it('adds an ordered pinned form and supplies missing SLA context',()=>{const c=create({taskTypeId:'T',rejectTargetNodeKey:'previous'});const saved=vi.fn();c.configurationChange.subscribe(saved);c.select(c.items()[1]);const config=JSON.parse(saved.mock.lastCall![0]);expect(config.taskTypeId).toBe('T');expect(config.rejectTargetNodeKey).toBe('previous');expect(config.formSteps[0]).toEqual(expect.objectContaining({formId:'B',versionNo:1,otherwise:{kind:'Next'}}));expect(config.departmentCode).toBe('10');expect(config.fieldActivityCode).toBe('LEAK_REPAIR');});
  it('preserves existing SLA context and forbids selecting unavailable forms',()=>{const c=create({taskTypeId:'T',departmentCode:'old',fieldActivityCode:'OLD'});const saved=vi.fn();c.configurationChange.subscribe(saved);c.select(c.items()[0]);expect(JSON.parse(saved.mock.lastCall![0]).departmentCode).toBe('old');c.select(c.items()[2]);expect(saved).toHaveBeenCalledTimes(1);c.readonly=true;c.select(c.items()[1]);expect(saved).toHaveBeenCalledTimes(1);});
  it('previews a saved version without changing the selection',()=>{const c=create({taskTypeId:'T',formDefinitionId:'A',formVersionNo:1});const preview=vi.fn(),saved=vi.fn();c.preview.subscribe(preview);c.configurationChange.subscribe(saved);c.open(c.items()[1]);expect(preview.mock.lastCall![0].taskTypeId).toBe('T');expect(saved).not.toHaveBeenCalled();});
  it('ignores an old Task Type response',()=>{const slow=new Subject<ActivityFormPage>();api.listForTaskType.mockReturnValueOnce(slow).mockReturnValueOnce(of(page([form('NEW')])));const c=create({taskTypeId:'OLD'});c.configuration=JSON.stringify({taskTypeId:'NEW'});c.ngOnChanges();slow.next(page([form('OLD')]));expect(c.items().map(f=>f.id)).toEqual(['NEW']);});
  it('flags a saved form removed from the Task Type',()=>{const c=create({taskTypeId:'T',formDefinitionId:'REMOVED',formVersionNo:1});expect(c.selectedAvailable()).toBe(false);});
  it('shows a recoverable loading error',()=>{api.listForTaskType.mockReturnValue(throwError(()=>new HttpErrorResponse({status:500})));const c=create({taskTypeId:'T'});expect(c.error()).toBeTruthy();api.listForTaskType.mockReturnValue(of(page([form('OK')])));c.load();expect(c.error()).toBe('');expect(c.items().length).toBe(1);});
});

describe('WorkflowFormPreviewComponent', () => {
  let api: Record<string, Mock>;
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
  beforeEach(() => { api = {'list': vi.fn(),'preview': vi.fn()}; });

  it('reads the pinned version through the activity context and never submits', () => {
    api.preview.mockImplementation((_id, versionNo) => preview(versionNo));
    const component = create();
    expect(api.preview).toHaveBeenCalledWith('LEAK-A', 2, '10', 'LEAK_REPAIR');
    expect(component.definition()).toEqual({ name_en: 'v2', elements: [] });
    component.select(1);
    expect(api.preview.mock.lastCall!).toEqual(['LEAK-A', 1, '10', 'LEAK_REPAIR']);
    expect(component.definition()).toEqual({ name_en: 'v1', elements: [] });
    // Only reads: the service offers no way to write, and the preview asked for nothing else.
    expect(api.list).not.toHaveBeenCalled();
  });

  it('drops a slower response for a version no longer selected', () => {
    const slow = new Subject<ActivityFormPreview>();
    api.preview.mockReturnValueOnce(slow).mockReturnValueOnce(preview(1));
    const component = create();
    component.select(1);
    slow.next({ formId: 'LEAK-A', code: 'LEAK-A', nameEn: 'A', nameAr: 'A', status: 'PUBLISHED', versionNo: 2, isUsable: true, schemaJson: '{"name_en":"v2","elements":[]}' });
    expect(component.definition()).toEqual({ name_en: 'v1', elements: [] });
  });

  it('explains a form that is no longer filed under the activity, without offering a retry', () => {
    api.preview.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const component = create();
    expect(component.error()).toContain('no longer available');
    expect(component.canRetry()).toBe(false);
  });

  it('closes on Escape', () => {
    api.preview.mockImplementation((_id, versionNo) => preview(versionNo));
    const component = create();
    const closed = vi.fn();
    component.closed.subscribe(closed);
    component.onEscape(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toHaveBeenCalled();
  });
});
