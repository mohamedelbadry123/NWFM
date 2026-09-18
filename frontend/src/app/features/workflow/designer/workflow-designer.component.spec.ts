import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { WorkflowDesignerComponent } from './workflow-designer.component';
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
});
