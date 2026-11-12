import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { userEvent } from 'vitest/browser';
import { LocaleService } from '@core/i18n/locale.service';
import { WORKFLOW_MAPPING_HOST } from './mapping-host';
import { emptyConfiguration, emptyEndpoint, emptyNode } from './mapping-config';
import { WorkflowEventMapperComponent } from './workflow-event-mapper.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import type {
  DesignerMappingConfiguration,
  MappingCatalog,
  SourceDescriptor,
  SourceReference,
} from './mapping.models';

const ROOT = { definitionId: 'root-def', versionId: 'root-ver' };
const OWNER = { workflowDefinitionId: 'child-def', workflowVersionId: 'child-ver', nodeKey: 'E1' };
const reference: SourceReference = {
  workflowDefinitionId: 'root-def',
  workflowVersionId: 'root-ver',
  nodeKey: 'Task_1',
  kind: 'FormField',
  fieldKey: 'amount',
  jsonPointer: '',
  formStepId: 'step-1',
  formId: 'form-1',
  formVersion: 3,
};
const source: SourceDescriptor = {
  reference,
  label: 'Amount',
  breadcrumbs: ['Order', 'Review', 'Step', 'Form', 'Amount'],
  schema: { type: 'string' },
  availability: 'SubmittedForm',
};
const catalog: MappingCatalog = { sources: [source], targets: [], diagnostics: [] };

/** Real pointer input (Playwright's own mouse) against the mounted mapper, no synthetic events. */
describe('Event mapper with a real pointer', () => {
  const service = {
    catalog: vi.fn(),
    relatedDefinitions: vi.fn(),
    relatedCatalog: vi.fn(),
  };
  const host = {
    owner: () => OWNER,
    root: () => ROOT,
    readonly: () => false,
    ensureSaved: () => Promise.resolve(),
    isChild: () => false,
  };

  beforeEach(() => {
    service.catalog.mockReturnValue(of(catalog));
    service.relatedDefinitions.mockReturnValue(of([]));
    service.relatedCatalog.mockReturnValue(of(catalog));
    TestBed.configureTestingModule({
      imports: [WorkflowEventMapperComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowMappingService, useValue: service },
        { provide: WORKFLOW_MAPPING_HOST, useValue: host },
      ],
    });
  });

  async function open(configuration: DesignerMappingConfiguration) {
    const fixture = TestBed.createComponent(WorkflowEventMapperComponent);
    fixture.componentRef.setInput('configuration', configuration);
    fixture.componentRef.setInput('readonly', false);
    const emitted: DesignerMappingConfiguration[] = [];
    fixture.componentInstance.configurationChange.subscribe((c) => emitted.push(c));
    document.body.appendChild(fixture.nativeElement);
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    await settle();
    return { fixture, el: fixture.nativeElement as HTMLElement, emitted, settle };
  }

  const authored = (): DesignerMappingConfiguration => ({
    ...emptyConfiguration(),
    endpoint: {
      ...emptyEndpoint(),
      method: 'GET',
      path: '/orders',
      parameters: [{ location: 'query', name: 'r', schema: { type: 'string' } }],
    },
    inputs: [{ location: 'query', name: 'r', value: emptyNode() }],
  });

  it('lands on the stage whose tab is clicked, for every stage', async () => {
    const { el, settle } = await open(authored());
    for (const t of Array.from(el.querySelectorAll<HTMLElement>('[role="tab"]'))) {
      const r = t.getBoundingClientRect();
      const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      // Each tab is sized by its label and its centre hits itself (a fixed-size chip overlaps its neighbours).
      expect(r.width).toBeGreaterThan(40);
      expect((hit as HTMLElement | null)?.closest('[role="tab"]')).toBe(t);
    }
    const selected = () =>
      el
        .querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')!
        .id.replace('mx-tab-', '');
    for (const stage of ['inputs', 'outputs', 'actions', 'test', 'outputs', 'inputs', 'endpoint']) {
      await userEvent.click(el.querySelector<HTMLElement>(`#mx-tab-${stage}`)!);
      await settle();
      expect(selected()).toBe(stage);
    }
  });

  it('maps a source leaf onto a slot with a real drag and drop', async () => {
    const { el, emitted, settle } = await open(authored());
    await userEvent.click(el.querySelector<HTMLElement>('#mx-tab-inputs')!);
    await settle();
    const search = el.querySelector<HTMLInputElement>('input[type="search"], input[placeholder]')!;
    search.value = 'Amount';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    const leaf = el.querySelector<HTMLElement>('[data-source="amount"]')!;
    const slot = el.querySelector<HTMLElement>('[data-slot="input:query:r"]')!;
    expect(leaf).not.toBeNull();
    expect(slot).not.toBeNull();
    await userEvent.dragAndDrop(leaf, slot);
    await settle();
    expect(emitted.at(-1)!.inputs[0].value.op).toBe('source');
    expect(emitted.at(-1)!.inputs[0].value.source!.fieldKey).toBe('amount');
  });
});
