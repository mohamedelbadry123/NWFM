import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { LookupItem } from '@core/lookups/lookups.service';
import { buildWorkflowScopes, pruneWorkflowSelection, selectionFromScopes, WorkflowScopeLookups } from './workflow-scope-selection';
import { hasRequiredClassification, hasRequiredLocation, ReferenceItem, WorkspaceSettings, WorkflowWorkspaceService } from './workflow-workspace.service';
import { WorkflowTypeSelectorComponent } from './workflow-type-selector.component';
import { WorkflowLocationComponent } from './workflow-location.component';
import { buildNWFMXml } from '../designer/workflow-designer.component';

const item = (code: string, parentCode?: string): LookupItem => ({ id: code, code, parentCode, nameEn: code, nameAr: code, isActive: true });
const lookups: WorkflowScopeLookups = {
  clusters: [item('A'), item('B')], cbus: [item('A1', 'A'), item('A2', 'A'), item('B1', 'B')],
  branches: [item('AB1', 'A1'), item('BB1', 'B1')], areas: [item('AA1', 'A1'), item('BA1', 'B1')],
};

describe('Workflow definition scopes', () => {
  it('allows several clusters without requiring a CBU for either workflow kind', () => {
    const scopes = buildWorkflowScopes({ clusters: ['A', 'B'], cbus: [], branches: [], areas: [] }, lookups);
    expect(scopes.map(s => s.level)).toEqual(['Cluster', 'Cluster']);
    for (const kind of ['Main', 'Child'] as const) expect(hasRequiredLocation({ kind, schemaVersion: 2, organizationScopes: scopes })).toBeTrue();
    expect(hasRequiredLocation({ kind: 'Child', schemaVersion: 2, organizationScopes: [] })).toBeFalse();
  });

  it('narrows only parents that have selected descendants', () => {
    const scopes = buildWorkflowScopes({ clusters: ['A', 'B'], cbus: ['A1'], branches: [], areas: [] }, lookups);
    expect(scopes).toEqual([{ level: 'Cbu', code: 'A1', clusterCode: 'A', cbuCode: 'A1' }, { level: 'Cluster', code: 'B', clusterCode: 'B' }]);
  });

  it('keeps operation areas independent of branches and does not create cross-parent combinations', () => {
    const scopes = buildWorkflowScopes({ clusters: ['A', 'B'], cbus: ['A1', 'B1'], branches: ['AB1'], areas: ['BA1'] }, lookups);
    expect(scopes).toEqual([{ level: 'Branch', code: 'AB1', clusterCode: 'A', cbuCode: 'A1' },
      { level: 'OperationArea', code: 'BA1', clusterCode: 'B', cbuCode: 'B1' }]);
    expect(selectionFromScopes(scopes).areas).toEqual(['BA1']);
  });

  it('removes only descendants of a removed parent', () => {
    expect(pruneWorkflowSelection({ clusters: ['B'], cbus: ['A1', 'B1'], branches: ['AB1', 'BB1'], areas: ['AA1', 'BA1'] }, lookups))
      .toEqual({ clusters: ['B'], cbus: ['B1'], branches: ['BB1'], areas: ['BA1'] });
  });

  it('retains both independent leaf choices beneath the same CBU', () => {
    const scopes = buildWorkflowScopes({ clusters: ['A'], cbus: ['A1'], branches: ['AB1'], areas: ['AA1'] }, lookups);
    expect(scopes.map(s => s.level)).toEqual(['Branch', 'OperationArea']);
  });

  it('requires the correct classification and clears only classification when kind changes', () => {
    TestBed.configureTestingModule({ providers: [{ provide: LocaleService, useValue: { locale: () => 'en' } }] });
    const component = TestBed.runInInjectionContext(() => new WorkflowLocationComponent());
    component.settings = { kind: 'Main', schemaVersion: 2, designerVersion: 2,
      organizationScopes: [{ level: 'Cluster', code: 'A', clusterCode: 'A' }], fieldActivityTypeId: 'FA1', departmentCode: 'D1', fieldActivityCode: 'FA' };
    expect(hasRequiredClassification(component.settings)).toBeTrue();
    component.kind('Child');
    expect(component.settings.organizationScopes?.[0].code).toBe('A');
    expect(component.settings.fieldActivityTypeId).toBeUndefined();
    expect(hasRequiredClassification(component.settings)).toBeFalse();
    expect(hasRequiredClassification({ ...component.settings, taskTypeId: 'T1' })).toBeTrue();
  });

  it('round-trips all creation settings through designer XML', () => {
    const settings: WorkspaceSettings = { kind: 'Child', schemaVersion: 2, taskTypeId: 'T1',
      organizationScopes: buildWorkflowScopes({ clusters: ['A', 'B'], cbus: ['A1'], branches: [], areas: ['AA1'] }, lookups) };
    const xml = new DOMParser().parseFromString(buildNWFMXml({ nodes: [], edges: [], variables: [], workspace: settings }), 'application/xml');
    expect(JSON.parse(xml.documentElement.getAttribute('workspaceJson')!)).toEqual(settings);
  });
});

describe('Workflow type selector', () => {
  const fa: ReferenceItem = { id: 'FA1', code: 'SAME-CODE', parentCode: 'D1', nameEn: 'Activity one', nameAr: 'نشاط' };
  const task: ReferenceItem = { id: 'T1', code: 'TASK', nameEn: 'Task one', nameAr: 'مهمة' };
  const api = { references: (kind: string) => of(kind === 'field-activity-types' ? [fa] : kind === 'task-types' ? [task] : [{ id: 'D1', code: 'D1', nameEn: 'Water', nameAr: 'المياه' }]) };

  beforeEach(() => TestBed.configureTestingModule({ imports: [WorkflowTypeSelectorComponent], providers: [
    { provide: WorkflowWorkspaceService, useValue: api }, { provide: LocaleService, useValue: { locale: () => 'en' } },
  ] }));

  it('renders Activity Type only for Main and Task Types only for Child', async () => {
    const fixture = TestBed.createComponent(WorkflowTypeSelectorComponent);
    fixture.componentRef.setInput('settings', { kind: 'Main', schemaVersion: 2 });
    fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('label').textContent).toContain('Activity Type');
    expect(fixture.nativeElement.querySelectorAll('select').length).toBe(1);
    expect(fixture.nativeElement.textContent).not.toContain('Task Types');
    fixture.componentRef.setInput('settings', { kind: 'Child', schemaVersion: 2 });
    fixture.detectChanges(); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('label').textContent).toContain('Task Types');
    expect(fixture.nativeElement.textContent).not.toContain('Activity Type');
  });

  it('saves the selected activity identity and department, never a task type', () => {
    const component = TestBed.runInInjectionContext(() => new WorkflowTypeSelectorComponent());
    component.ngOnChanges();
    const emitted: WorkspaceSettings[] = [];
    component.settingsChange.subscribe(settings => emitted.push(settings));
    component.choose('FA1');
    expect(emitted[0].fieldActivityTypeId).toBe('FA1');
    expect(emitted[0].departmentCode).toBe('D1');
    expect(emitted[0].fieldActivityCode).toBe('SAME-CODE');
    expect(emitted[0].taskTypeId).toBeUndefined();
  });

  it('discards a delayed Activity Type response after changing to Child', () => {
    const previous = new Subject<ReferenceItem[]>();
    TestBed.overrideProvider(WorkflowWorkspaceService, { useValue: { references: (kind: string) => kind === 'field-activity-types' ? previous : api.references(kind) } });
    const component = TestBed.runInInjectionContext(() => new WorkflowTypeSelectorComponent());
    component.ngOnChanges();
    component.settings = { kind: 'Child', schemaVersion: 2 }; component.ngOnChanges();
    previous.next([fa]); previous.complete();
    expect(component.items()).toEqual([task]);
  });
});
