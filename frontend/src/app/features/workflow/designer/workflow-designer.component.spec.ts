import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';

import { CanvasNode, NodeType, WorkflowDesignerComponent } from './workflow-designer.component';
import { WritableSignal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { WorkflowActionsCatalogService } from '../workflow-actions-catalog.service';
import { WorkflowBindingsService } from '../workflow-bindings.service';
import { WorkflowSlaPoliciesService } from '../workflow-sla-policies.service';
import { ToastService } from '@core/notifications/toast.service';
import { ThemeService } from '@core/theme/theme.service';
import { LocaleService } from '@core/i18n/locale.service';

/**
 * Soft acceptance for §36 designer scale: 100 and 200 nodes must add
 * under a generous budget so regressions (O(n²) canvas work) fail CI.
 */
describe('WorkflowDesignerComponent scale', () => {
  let fixture: ComponentFixture<WorkflowDesignerComponent>;
  let component: WorkflowDesignerComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowDesignerComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (k: string) =>
                  k === 'definitionId'
                    ? '00000000-0000-0000-0000-000000000001'
                    : '00000000-0000-0000-0000-000000000002',
              },
            },
            paramMap: of({
              get: (k: string) =>
                k === 'definitionId'
                  ? '00000000-0000-0000-0000-000000000001'
                  : '00000000-0000-0000-0000-000000000002',
            }),
          },
        },
        {
          provide: WorkflowVersionsService,
          useValue: {
            getById: () =>
              of({
                id: 'v1',
                status: 'Draft',
                versionNumber: 1,
                xmlContent:
                  '<Workflow xmlns="https://privora.io/workflow/v1"><Activities/><Transitions/></Workflow>',
              }),
            saveXml: () => of({}),
            validate: () => of({ isValid: true, errors: [], warnings: [] }),
            publish: () => of({}),
            clone: () => of({}),
            retire: () => of({}),
            getPublishPreview: () => of({}),
          },
        },
        { provide: WorkflowDefinitionsService, useValue: { getById: () => of({ id: 'd1', name: 'Perf' }) } },
        { provide: WorkflowActionsCatalogService, useValue: { getAll: () => of([]) } },
        { provide: WorkflowBindingsService, useValue: { listByDefinition: () => of([]), listOrgGroups: () => of([]) } },
        { provide: WorkflowSlaPoliciesService, useValue: { getAll: () => of([]), getPaged: () => of({ items: [] }) } },
        { provide: ToastService, useValue: { success: () => undefined, error: () => undefined } },
        { provide: ThemeService, useValue: { isDark: () => false } },
        { provide: LocaleService, useValue: { current: () => 'en', isRtl: () => false } },
      ],
    });

    fixture = TestBed.createComponent(WorkflowDesignerComponent);
    component = fixture.componentInstance;
    const c = component as unknown as {
      isLoading: { set: (v: boolean) => void };
      version: { set: (v: { id: string; status: string; versionNumber: number }) => void };
    };
    c.isLoading.set(false);
    c.version.set({ id: 'v1', status: 'Draft', versionNumber: 1 });
  });

  function fillNodes(count: number): void {
    const add = (
      component as unknown as { addNode: (t: string, x: number, y: number) => void }
    ).addNode.bind(component);
    for (let i = 0; i < count; i++) {
      add('UserTask', 40 + (i % 20) * 80, 40 + Math.floor(i / 20) * 70);
    }
  }

  function nodeCount(): number {
    return (component as unknown as { nodes: () => unknown[] }).nodes().length;
  }

  it('adds 100 nodes under 2s', () => {
    const started = performance.now();
    fillNodes(100);
    const elapsed = performance.now() - started;
    expect(nodeCount()).toBeGreaterThanOrEqual(100);
    expect(elapsed).toBeLessThan(2000);
  });

  it('adds 200 nodes under 4s', () => {
    const started = performance.now();
    fillNodes(200);
    const elapsed = performance.now() - started;
    expect(nodeCount()).toBeGreaterThanOrEqual(200);
    expect(elapsed).toBeLessThan(4000);
  });

  function access() {
    return component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      propsForm: FormGroup;
      patchPropsFromNode(node: CanvasNode): void;
      buildConfigurationJson(type: NodeType, value: Record<string, unknown>): string;
    };
  }

  it('loads a cloned draft when route parameters change on the existing designer', () => {
    const params = new Subject<{ get(key: string): string }>();
    (TestBed.inject(ActivatedRoute) as unknown as {paramMap: unknown}).paramMap = params;
    const versions = TestBed.inject(WorkflowVersionsService);
    const load = spyOn(versions, 'getById').and.callFake((_definition, version) => of({id:version,status:version === 'published' ? 'Published' : 'Draft',activities:[],transitions:[],variables:[]}));
    component.ngOnInit();
    params.next({get:key => key === 'definitionId' ? 'definition' : 'published'});
    const editor = component as unknown as {version:()=>{status:string};isReadonly:()=>boolean};
    expect(editor.isReadonly()).toBeTrue();
    params.next({get:key => key === 'definitionId' ? 'definition' : 'cloned'});
    expect(editor.version().status).toBe('Draft');
    expect(editor.isReadonly()).toBeFalse();
    expect(load).toHaveBeenCalledWith('definition','cloned');
    fixture.destroy();
  });

  function configure(type: NodeType, configuration: Record<string, unknown>) {
    const editor = access();
    editor.addNode(type, 100, 100);
    const node = { ...editor.nodes()[editor.nodes().length - 1], configurationJson: JSON.stringify(configuration) };
    editor.nodes.update(nodes => nodes.map(n => n.id === node.id ? node : n));
    editor.selectedNodeId.set(node.id);
    editor.patchPropsFromNode(node);
    return editor;
  }

  it('keeps notification channels and recipients when editing its template', () => {
    const editor = configure('NotificationTask', { templateKey: 'before', channels: 'Email', recipientUserIds: ['recipient-id'] });
    editor.propsForm.patchValue({ templateKey: 'after' });
    const saved = JSON.parse(editor.buildConfigurationJson('NotificationTask', editor.propsForm.value));
    expect(saved.templateKey).toBe('after');
    expect(saved.channels).toBe('Email');
    expect(saved.recipientUserIds).toEqual(['recipient-id']);
  });

  it('loads legacy signal keys and saves the canonical event key without losing extension fields', () => {
    const editor = configure('WaitEvent', { signalKey: 'order.ready', extension: { source: 'orders' } });
    expect(editor.propsForm.value.eventKey).toBe('order.ready');
    const saved = JSON.parse(editor.buildConfigurationJson('WaitEvent', editor.propsForm.value));
    expect(saved.eventKey).toBe('order.ready');
    expect(saved.signalKey).toBeUndefined();
    expect(saved.extension).toEqual({ source: 'orders' });
  });

  it('round trips a due date without shifting its timezone', () => {
    const editor = configure('Timer', { timerType: 'DueDate', dueAt: '2026-09-19T12:30:45.000Z', extension: true });
    const saved = JSON.parse(editor.buildConfigurationJson('Timer', editor.propsForm.value));
    expect(saved.dueAt).toBe('2026-09-19T12:30:45.000Z');
    expect(saved.extension).toBeTrue();
  });

  it('removes obsolete timer settings when changing timer type', () => {
    const editor = configure('Timer', { timerType: 'DueDate', dueAt: '2026-09-19T12:30:00Z' });
    editor.propsForm.patchValue({ timerType: 'Duration', duration: '00:30:00' });
    const saved = JSON.parse(editor.buildConfigurationJson('Timer', editor.propsForm.value));
    expect(saved.dueAt).toBeUndefined();
    expect(saved.duration).toBe('00:30:00');
  });

  it('suspends canvas shortcuts while the SLA editor is open', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void; nodes: WritableSignal<CanvasNode[]>; selectedNodeId: WritableSignal<string | null>;
      slaEditor: WritableSignal<unknown>; onKeyDown(event: KeyboardEvent): void;
    };
    designer.addNode('UserTask', 100, 100);
    const node = designer.nodes().find(n => n.type === 'UserTask')!;
    designer.selectedNodeId.set(node.id);
    designer.slaEditor.set({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    for (const key of ['Delete', 'Backspace', 'Escape']) designer.onKeyDown(new KeyboardEvent('keydown', { key }));
    expect(designer.nodes().some(n => n.id === node.id)).toBeTrue();
    expect(designer.selectedNodeId()).toBe(node.id);
    designer.slaEditor.set(null);
    designer.onKeyDown(new KeyboardEvent('keydown', { key: 'Delete' }));
    expect(designer.nodes().some(n => n.id === node.id)).toBeFalse();
  });

  it('shows Form instead of Actions on user tasks, and no Form on main activities or other nodes', () => {
    const ids = (type: NodeType) => WorkflowDesignerComponent.tabsFor(type).map(t => t.id);
    expect(ids('UserTask')).toEqual(['general', 'assignment', 'form', 'sla']);
    expect(ids('MainActivity')).toEqual(['general', 'assignment', 'sla']);
    for (const type of ['Start', 'End', 'ExclusiveGateway', 'ParallelGateway', 'Timer', 'ServiceTask', 'NotificationTask', 'WaitEvent'] as NodeType[]) {
      expect(ids(type)).withContext(type).toEqual([]);
    }
    expect(WorkflowDesignerComponent.tabsFor('UserTask').map(t => t.labelKey)).not.toContain('workflow.designer.tab_actions');
  });

  it('falls back to General when the selected activity has no Form tab', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void; nodes: WritableSignal<CanvasNode[]>; selectedNodeId: WritableSignal<string | null>;
      inspectorTab: WritableSignal<string>; activeInspectorTab: () => string; onNodeClick(event: MouseEvent, id: string): void;
    };
    designer.addNode('UserTask', 100, 100);
    designer.addNode('MainActivity', 400, 100);
    const [task, main] = [designer.nodes().find(n => n.type === 'UserTask')!, designer.nodes().find(n => n.type === 'MainActivity')!];
    designer.onNodeClick(new MouseEvent('click'), task.id);
    designer.inspectorTab.set('form');
    expect(designer.activeInspectorTab()).toBe('form');
    // Even before the click handler runs, a MainActivity never renders the Form panel.
    designer.selectedNodeId.set(main.id);
    expect(designer.activeInspectorTab()).toBe('general');
    designer.selectedNodeId.set(task.id);
    designer.onNodeClick(new MouseEvent('click'), main.id);
    expect(designer.inspectorTab()).toBe('general');
    designer.inspectorTab.set('sla');
    designer.onNodeClick(new MouseEvent('click'), task.id);
    expect(designer.inspectorTab()).withContext('a tab both have is kept').toBe('sla');
  });

  it('keeps the rejection destination and legacy task questions through General edits', () => {
    const editor = configure('UserTask', { departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', rejectTargetNodeKey: 'earlier_review', formFields: [{ key: 'note', labelEn: 'Note', labelAr: 'ملاحظة', type: 'text', required: false }] });
    const node = editor.nodes().find(n => n.id === editor.selectedNodeId())!;
    editor.patchPropsFromNode(node);
    editor.propsForm.patchValue({ name: 'Renamed' });
    const saved = JSON.parse(editor.buildConfigurationJson('UserTask', editor.propsForm.value));
    expect(saved.rejectTargetNodeKey).toBe('earlier_review');
    expect(saved.departmentCode).toBe('10');
    expect(saved.fieldActivityCode).toBe('LEAK_REPAIR');
    expect(saved.formFields.map((f: { key: string }) => f.key)).toEqual(['note']);
  });

  it('does not render an Accept and Reject section on the General tab', () => {
    fixture.detectChanges();
    const state = component as unknown as {
      isLoading: { set: (v: boolean) => void }; loadError: { set: (v: string | null) => void };
      version: { set: (v: { id: string; status: string; versionNumber: number }) => void };
    };
    state.isLoading.set(false);
    state.loadError.set(null);
    state.version.set({ id: 'v1', status: 'Draft', versionNumber: 1 });
    for (const type of ['UserTask', 'MainActivity'] as NodeType[]) {
      configure(type, { departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', rejectTargetNodeKey: 'earlier_review' });
      fixture.detectChanges();
      const panel = (fixture.nativeElement as HTMLElement).querySelector('#wf-activity-panel');
      expect(panel).withContext(type).not.toBeNull();
      expect(panel!.querySelector('#wf-activity-outcomes')).withContext(type).toBeNull();
      expect(panel!.querySelector('#wf-activity-reject-target')).withContext(type).toBeNull();
      expect(panel!.textContent).withContext(type).not.toMatch(/Accept and Reject|Return to/);
    }
  });

  it('suspends canvas shortcuts while a form preview is open', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void; nodes: WritableSignal<CanvasNode[]>; selectedNodeId: WritableSignal<string | null>;
      formPreview: WritableSignal<unknown>; onKeyDown(event: KeyboardEvent): void;
    };
    designer.addNode('UserTask', 100, 100);
    const node = designer.nodes().find(n => n.type === 'UserTask')!;
    designer.selectedNodeId.set(node.id);
    designer.formPreview.set({ form: { id: 'f' }, departmentCode: '10', fieldActivityCode: 'LEAK_REPAIR', versionNo: 1 });
    for (const key of ['Delete', 'Backspace', 'Escape']) designer.onKeyDown(new KeyboardEvent('keydown', { key }));
    designer.onKeyDown(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true }));
    expect(designer.nodes().some(n => n.id === node.id)).toBeTrue();
    expect(designer.selectedNodeId()).toBe(node.id);
  });

  it('shows the published configuration, with its SLA snapshot, on read-only versions', () => {
    const merge = (status: string) => (component as unknown as { mergeWithBackend(state: unknown, v: unknown): { nodes: CanvasNode[] } }).mergeWithBackend(
      { nodes: [{ id: 'canvas-1', nodeKey: 'review', type: 'UserTask', configurationJson: '{"departmentCode":"D1"}' }], edges: [], variables: [] },
      { status, activities: [{ id: 'a1', nodeKey: 'review', activityType: 'UserTask', configurationJson: '{"departmentCode":"D1","publishedSla":{"name":"Captured"}}' }], transitions: [] });
    expect(JSON.parse(merge('Published').nodes[0].configurationJson).publishedSla.name).toBe('Captured');
    expect(JSON.parse(merge('Draft').nodes[0].configurationJson).publishedSla).toBeUndefined();
  });

  it('loads legacy variable assignments and replaces them with edited assignments', () => {
    const editor = configure('ScriptTask', { assignments: { amount: 10 }, extension: true });
    expect(JSON.parse(editor.propsForm.value.setVariablesJson)).toEqual({ amount: 10 });
    editor.propsForm.patchValue({ setVariablesJson: '{"amount":20}' });
    const saved = JSON.parse(editor.buildConfigurationJson('ScriptTask', editor.propsForm.value));
    expect(saved.setVariables).toEqual({ amount: 20 });
    expect(saved.assignments).toBeUndefined();
    expect(saved.extension).toBeTrue();
  });
});
