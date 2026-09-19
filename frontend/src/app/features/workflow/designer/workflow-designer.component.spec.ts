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
