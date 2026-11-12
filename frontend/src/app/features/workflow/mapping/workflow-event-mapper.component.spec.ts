import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import { WORKFLOW_MAPPING_HOST, type WorkflowMappingHost } from './mapping-host';
import {
  emptyConfiguration,
  emptyEndpoint,
  emptyNode,
  literalNode,
  sourceNode,
} from './mapping-config';
import { MappingEditContext } from './mapping-edit-context';
import { newSelector } from './mapping-selectors.component';
import { WorkflowEventMapperComponent, stageOfError } from './workflow-event-mapper.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import type {
  DesignerMappingConfiguration,
  MappingCatalog,
  MappingDefinitionChoice,
  SourceDescriptor,
  SourceReference,
} from './mapping.models';

const ROOT = { definitionId: 'root-def', versionId: 'root-ver' };
const OWNER = {
  workflowDefinitionId: 'child-def',
  workflowVersionId: 'child-ver',
  nodeKey: 'Event_1',
};

const field = (over: Partial<SourceReference> = {}): SourceReference => ({
  workflowDefinitionId: 'root-def',
  workflowVersionId: 'root-ver',
  nodeKey: 'Task_1',
  kind: 'FormField',
  fieldKey: 'customer.id',
  jsonPointer: '',
  formStepId: 'step-1',
  formId: 'form-1',
  formVersion: 3,
  ...over,
});
const descriptor = (reference = field()): SourceDescriptor => ({
  reference,
  label: 'Customer id',
  breadcrumbs: ['Order', 'Review', 'Step', 'Form', 'Customer id'],
  schema: { type: 'string' },
  availability: 'SubmittedForm',
});
const catalog: MappingCatalog = { sources: [descriptor()], targets: [], diagnostics: [] };

describe('stageOfError', () => {
  it('puts a server message next to the stage it is about', () => {
    expect(stageOfError('The endpoint path is invalid.')).toBe('endpoint');
    expect(stageOfError('Header is not permitted')).toBe('endpoint');
    expect(stageOfError('Callable target is not in this tree.')).toBe('actions');
    expect(stageOfError('Output "total" has a forward reference.')).toBe('outputs');
    expect(stageOfError('Something about a parameter')).toBe('inputs');
  });
});

describe('Event mapper', () => {
  const service = {
    catalog: vi.fn(),
    relatedDefinitions: vi.fn(),
    relatedCatalog: vi.fn(),
    validate: vi.fn(),
    validateSchema: vi.fn(),
    preview: vi.fn(),
    importOpenApi: vi.fn(),
    inferResponse: vi.fn(),
    sendTest: vi.fn(),
  };
  const host = {
    owner: vi.fn(),
    root: vi.fn(),
    readonly: vi.fn(),
    ensureSaved: vi.fn(),
    isChild: vi.fn(),
  } satisfies Record<keyof WorkflowMappingHost, unknown>;

  beforeEach(() => {
    for (const fn of [...Object.values(service), ...Object.values(host)]) fn.mockReset();
    service.catalog.mockReturnValue(of(catalog));
    service.relatedDefinitions.mockReturnValue(of([]));
    service.relatedCatalog.mockReturnValue(of(catalog));
    host.owner.mockReturnValue(OWNER);
    host.root.mockReturnValue(ROOT);
    host.readonly.mockReturnValue(false);
    host.ensureSaved.mockResolvedValue(undefined);
    host.isChild.mockReturnValue(false);
    TestBed.configureTestingModule({
      imports: [WorkflowEventMapperComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowMappingService, useValue: service },
        { provide: WORKFLOW_MAPPING_HOST, useValue: host },
      ],
    });
  });

  async function open(
    configuration: DesignerMappingConfiguration | null,
    opts: { readonly?: boolean } = {},
  ) {
    const fixture = TestBed.createComponent(WorkflowEventMapperComponent);
    fixture.componentRef.setInput('configuration', configuration);
    fixture.componentRef.setInput('readonly', opts.readonly ?? false);
    const emitted: DesignerMappingConfiguration[] = [];
    fixture.componentInstance.configurationChange.subscribe((c) => emitted.push(c));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const tab = (stage: string) => el.querySelector<HTMLButtonElement>(`#mx-tab-${stage}`)!;
    const go = async (stage: string) => {
      tab(stage).click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, emitted, tab, go };
  }

  it('saves the draft, then reads the pinned root’s catalog and choices', async () => {
    await open(emptyConfiguration());
    expect(host.ensureSaved).toHaveBeenCalled();
    expect(service.catalog).toHaveBeenCalledWith(ROOT.definitionId, ROOT.versionId);
    expect(service.relatedDefinitions).toHaveBeenCalledWith(ROOT.definitionId, ROOT.versionId);
  });

  it('shows the five stages in order with a completion state for each', async () => {
    const { el, tab } = await open(emptyConfiguration());
    const tabs = Array.from(el.querySelectorAll('[role="tab"]'));
    expect(tabs.map((t) => t.id)).toEqual([
      'mx-tab-endpoint',
      'mx-tab-inputs',
      'mx-tab-outputs',
      'mx-tab-actions',
      'mx-tab-test',
    ]);
    expect(tab('endpoint').textContent).toContain('Not started');
    expect(tab('outputs').textContent).toContain('Optional');
    expect(tab('endpoint').getAttribute('aria-selected')).toBe('true');
  });

  it('marks a configured endpoint done and an unmapped required parameter as needing attention', async () => {
    const config: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        method: 'GET',
        path: '/orders',
        parameters: [{ location: 'query', name: 'id', schema: { type: 'string', required: true } }],
      },
    };
    const { tab } = await open(config);
    expect(tab('endpoint').textContent).toContain('Done');
    expect(tab('inputs').textContent).toContain('Needs attention');
  });

  it('renders every stage’s editor, so every nested template compiles and is reachable', async () => {
    const config: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        response: { type: 'object', properties: { id: { type: 'string' } } },
      },
    };
    const { el, go } = await open(config);
    expect(el.querySelector('app-mapping-endpoint-stage')).not.toBeNull();
    await go('inputs');
    expect(el.querySelector('app-mapping-selectors')).not.toBeNull();
    expect(el.querySelector('app-mapping-inputs-stage')).not.toBeNull();
    expect(el.querySelector('app-mapping-source-browser')).not.toBeNull();
    await go('outputs');
    expect(el.querySelector('app-mapping-outputs-stage')).not.toBeNull();
    await go('actions');
    expect(el.querySelector('app-mapping-actions-stage')).not.toBeNull();
    await go('test');
    expect(el.querySelector('app-mapping-test-stage')).not.toBeNull();
    expect(el.querySelector('app-mapping-source-browser')).toBeNull();
  });

  it('moves between stages with the keyboard and the Back / Next buttons', async () => {
    const { fixture, el, tab } = await open(emptyConfiguration());
    tab('endpoint').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }),
    );
    fixture.detectChanges();
    expect(tab('inputs').getAttribute('aria-selected')).toBe('true');
    tab('inputs').dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();
    expect(tab('test').getAttribute('aria-selected')).toBe('true');
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.trim() === 'Back')!
      .click();
    fixture.detectChanges();
    expect(tab('actions').getAttribute('aria-selected')).toBe('true');
  });

  it('emits a typed configuration when the endpoint is edited, without a tree pin for a main workflow', async () => {
    const { fixture, el, emitted } = await open(emptyConfiguration());
    const path = el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!;
    path.value = '/customers';
    path.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(emitted.at(-1)!.endpoint!.path).toBe('/customers');
    expect('treeRoot' in emitted.at(-1)!).toBe(false);
  });

  it('pins the main tree root on a configuration authored inside a child workflow', async () => {
    host.isChild.mockReturnValue(true);
    const { fixture, el, emitted } = await open(emptyConfiguration());
    const path = el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!;
    path.value = '/x';
    path.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(emitted.at(-1)!.treeRoot).toEqual({
      workflowDefinitionId: ROOT.definitionId,
      workflowVersionId: ROOT.versionId,
    });
  });

  it('is inspectable but not editable for a published version', async () => {
    host.readonly.mockReturnValue(true);
    const config = { ...emptyConfiguration(), endpoint: { ...emptyEndpoint(), path: '/orders' } };
    const { el, emitted, go } = await open(config);
    expect(el.textContent).toContain('read-only');
    expect(el.querySelector('fieldset')!.disabled).toBe(true);
    // A read-only version never saves the draft.
    expect(host.ensureSaved).not.toHaveBeenCalled();
    await go('outputs');
    expect(el.querySelector('app-mapping-outputs-stage')).not.toBeNull();
    expect(emitted).toEqual([]);
  });

  it('routes the server’s validation message to its stage and marks it', async () => {
    service.validate.mockReturnValue(
      of({ valid: false, canPublish: false, errors: ['Output "total" has a forward reference.'] }),
    );
    const { fixture, el, tab } = await open(emptyConfiguration());
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Check the whole mapping'))!
      .click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(service.validate).toHaveBeenCalledWith(
      ROOT.definitionId,
      ROOT.versionId,
      expect.objectContaining({ owner: OWNER }),
    );
    expect(tab('outputs').getAttribute('aria-selected')).toBe('true');
    expect(tab('outputs').textContent).toContain('Needs attention');
    expect(el.textContent).toContain('forward reference');
  });

  it('says so when the server accepts, and shows a failed check instead of pretending', async () => {
    service.validate.mockReturnValueOnce(of({ valid: true, canPublish: true, errors: [] }));
    const { fixture, el } = await open(emptyConfiguration());
    const check = () =>
      Array.from(el.querySelectorAll('button')).find((b) =>
        b.textContent!.includes('Check the whole mapping'),
      )!;
    check().click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('The server accepted this mapping.');
    service.validate.mockReturnValueOnce(
      throwError(() => ({ error: { message: 'Validation is unavailable.' } })),
    );
    check().click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('The server accepted this mapping.');
    expect(el.textContent).toContain('Validation is unavailable.');
  });

  it('keeps an out-of-date reference visible and repins it only when asked, keeping its pointer', async () => {
    const stale = field({ workflowVersionId: 'older-ver', formVersion: 2, jsonPointer: '/id' });
    const config: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        parameters: [{ location: 'query', name: 'id', schema: { type: 'string' } }],
      },
      inputs: [{ location: 'query', name: 'id', value: sourceNode(stale, '/id') }],
    };
    const { fixture, el, emitted } = await open(config);
    const panel = el.querySelector('[data-testid="repin"]')!;
    expect(panel).not.toBeNull();
    // Nothing was changed just by opening it.
    expect(emitted).toEqual([]);
    panel.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    const repinned = emitted.at(-1)!.inputs[0].value.source!;
    expect(repinned.workflowVersionId).toBe('root-ver');
    expect(repinned.formVersion).toBe(3);
    expect(repinned.jsonPointer).toBe('/id');
    expect(el.querySelector('[data-testid="repin"]')).toBeNull();
  });

  it('does not reset local edits when the host echoes the emitted document back', async () => {
    const { fixture, el, emitted } = await open(emptyConfiguration());
    const path = el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!;
    path.value = '/kept';
    path.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    fixture.componentRef.setInput('configuration', structuredClone(emitted.at(-1)!));
    fixture.detectChanges();
    expect(
      el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!.value,
    ).toBe('/kept');
  });

  it('replaces local state when a different document is opened', async () => {
    const { fixture, el } = await open({
      ...emptyConfiguration(),
      endpoint: { ...emptyEndpoint(), path: '/first' },
    });
    await fixture.whenStable();
    fixture.componentRef.setInput('configuration', {
      ...emptyConfiguration(),
      endpoint: { ...emptyEndpoint(), path: '/second' },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!.value,
    ).toBe('/second');
  });

  it('tells the author why sources are missing when the catalog cannot be read', async () => {
    service.catalog.mockReturnValue(
      throwError(() => ({ error: { message: 'The workflow is unavailable.' } })),
    );
    const { el, go } = await open(emptyConfiguration());
    await go('inputs');
    expect(el.textContent).toContain('The workflow is unavailable.');
  });

  it('keeps literal null / zero samples and values intact through the document', async () => {
    const config: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        method: 'GET',
        path: '/x',
        parameters: [{ location: 'query', name: 'q', schema: { type: 'string' } }],
      },
      inputs: [{ location: 'query', name: 'q', value: literalNode(0) }],
    };
    const { fixture, el, emitted } = await open(config);
    const path = el.querySelector<HTMLInputElement>('input[placeholder="/orders/{orderId}"]')!;
    path.value = '/y';
    path.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(emitted.at(-1)!.inputs[0].value.value).toBe(0);
  });

  describe('related workflow catalogs follow the selector’s exact pins', () => {
    const ALPHA: MappingDefinitionChoice = {
      definitionId: 'def-a',
      versionId: 'ver-a',
      versionNumber: 1,
      name: 'Alpha',
    };
    const BETA: MappingDefinitionChoice = {
      definitionId: 'def-b',
      versionId: 'ver-b',
      versionNumber: 2,
      name: 'Beta',
    };
    const relatedCatalogOf = (choice: MappingDefinitionChoice, key: string): MappingCatalog => ({
      sources: [
        {
          reference: field({
            workflowDefinitionId: choice.definitionId,
            workflowVersionId: choice.versionId,
            fieldKey: key,
          }),
          label: key,
          breadcrumbs: [choice.name, 'Task', 'Form', key],
          schema: { type: 'string' },
          availability: 'SubmittedForm',
        },
      ],
      targets: [],
      diagnostics: [],
    });
    const ALPHA_CATALOG = relatedCatalogOf(ALPHA, 'alpha_field');
    const BETA_CATALOG = relatedCatalogOf(BETA, 'beta_field');
    const selector = { ...newSelector(ALPHA, []), id: 'rel-1' };
    const withSelector: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      selectors: [selector],
    };

    /**
     * A request the test answers by hand, so answers can arrive in any order. Like a transport that cannot be aborted
     * it ignores unsubscription, so only the host's own “newest request wins” rule can reject a late answer.
     */
    const pending = () => {
      const answer: { send: (catalog: MappingCatalog) => void; calls: number } = {
        send: () => undefined,
        calls: 0,
      };
      const source = {
        subscribe: (observer: { next: (catalog: MappingCatalog) => void }) => {
          answer.calls++;
          answer.send = (catalog) => observer.next(catalog);
          return { unsubscribe: () => undefined };
        },
      } as unknown as Observable<MappingCatalog>;
      return { answer, source };
    };

    async function openRelated(config = withSelector) {
      const opened = await open(config);
      await opened.go('inputs');
      const search = (text: string) => {
        const box = opened.el.querySelector<HTMLInputElement>('input[type="search"]')!;
        box.value = text;
        box.dispatchEvent(new Event('input', { bubbles: true }));
      };
      const leaf = (key: string) => opened.el.querySelector(`[data-source="${key}"]`);
      const settle = async () => {
        opened.fixture.detectChanges();
        await opened.fixture.whenStable();
        opened.fixture.detectChanges();
      };
      const retarget = async (versionId: string) => {
        const box = opened.el.querySelector<HTMLSelectElement>(
          `[data-selector="${selector.id}"] select`,
        )!;
        box.value = versionId;
        box.dispatchEvent(new Event('change', { bubbles: true }));
        await settle();
      };
      return { ...opened, search, leaf, settle, retarget };
    }

    beforeEach(() => service.relatedDefinitions.mockReturnValue(of([ALPHA, BETA])));

    it('shows only the new workflow’s sources after a retarget, even with the old request still outstanding', async () => {
      const slowAlpha = pending();
      service.relatedCatalog.mockImplementation((_definition: string, version: string) =>
        version === 'ver-a' ? slowAlpha.source : of(BETA_CATALOG),
      );
      const { search, leaf, retarget, settle, emitted } = await openRelated();
      expect(slowAlpha.answer.calls).toBeGreaterThan(0);

      await retarget('ver-b');
      expect(emitted.at(-1)!.selectors[0]).toEqual(
        expect.objectContaining({ workflowDefinitionId: 'def-b', workflowVersionId: 'ver-b' }),
      );
      search('field');
      await settle();
      expect(leaf('beta_field')).not.toBeNull();

      // Alpha’s answer arrives after the retarget: it must not appear under Beta.
      slowAlpha.answer.send(ALPHA_CATALOG);
      await settle();
      expect(leaf('alpha_field')).toBeNull();
      expect(leaf('beta_field')).not.toBeNull();
      expect(service.relatedCatalog).toHaveBeenLastCalledWith('def-b', 'ver-b');
    });

    it('drops the previous workflow’s sources at once while the new workflow’s catalog is still loading', async () => {
      const slowBeta = pending();
      service.relatedCatalog.mockImplementation((_definition: string, version: string) =>
        version === 'ver-a' ? of(ALPHA_CATALOG) : slowBeta.source,
      );
      const { search, leaf, retarget, settle } = await openRelated();
      search('field');
      await settle();
      expect(leaf('alpha_field')).not.toBeNull();

      await retarget('ver-b');
      search('field');
      await settle();
      expect(leaf('alpha_field')).toBeNull();
      expect(leaf('beta_field')).toBeNull();

      slowBeta.answer.send(BETA_CATALOG);
      await settle();
      expect(leaf('beta_field')).not.toBeNull();
      expect(leaf('alpha_field')).toBeNull();
    });

    it('does not reload a related catalog when only another criterion of the selector changes', async () => {
      service.relatedCatalog.mockReturnValue(of(ALPHA_CATALOG));
      const { el, settle } = await openRelated();
      const before = service.relatedCatalog.mock.calls.length;
      const box = el.querySelector<HTMLSelectElement>(
        `[data-selector="${selector.id}"] label select`,
      )!;
      expect(box).not.toBeNull();
      const kind = Array.from(
        el.querySelectorAll<HTMLSelectElement>(`[data-selector="${selector.id}"] select`),
      ).find((s) => Array.from(s.options).some((o) => o.value === 'CorrelationKey'))!;
      kind.value = 'CorrelationKey';
      kind.dispatchEvent(new Event('change', { bubbles: true }));
      await settle();
      expect(service.relatedCatalog.mock.calls.length).toBe(before);
    });

    it('forgets a removed selector’s catalog and loads a fresh one when the workflow is added again', async () => {
      service.relatedCatalog.mockReturnValue(of(ALPHA_CATALOG));
      const { el, search, leaf, settle } = await openRelated();
      search('field');
      await settle();
      expect(leaf('alpha_field')).not.toBeNull();

      Array.from(el.querySelectorAll('button'))
        .find((b) => b.textContent!.includes('Remove related workflow'))!
        .click();
      await settle();
      expect(leaf('alpha_field')).toBeNull();

      const calls = service.relatedCatalog.mock.calls.length;
      const add = Array.from(el.querySelectorAll<HTMLSelectElement>('select')).find((s) =>
        s.parentElement!.textContent!.includes('Add a related workflow'),
      )!;
      add.value = 'ver-a';
      add.dispatchEvent(new Event('change', { bubbles: true }));
      await settle();
      expect(service.relatedCatalog.mock.calls.length).toBe(calls + 1);
      search('field');
      await settle();
      expect(leaf('alpha_field')).not.toBeNull();
    });

    it('keeps a value read through the old workflow flagged as unavailable instead of silently rewriting it', async () => {
      service.relatedCatalog.mockImplementation((_definition: string, version: string) =>
        of(version === 'ver-a' ? ALPHA_CATALOG : BETA_CATALOG),
      );
      const reading: DesignerMappingConfiguration = {
        ...withSelector,
        endpoint: {
          ...emptyEndpoint(),
          parameters: [{ location: 'query', name: 'q', schema: { type: 'string' } }],
        },
        inputs: [
          {
            location: 'query',
            name: 'q',
            value: sourceNode({
              ...ALPHA_CATALOG.sources[0].reference,
              selectorId: selector.id,
            }),
          },
        ],
      };
      const { el, retarget, emitted } = await openRelated(reading);
      expect(el.querySelector('.mx-source-broken')).toBeNull();
      await retarget('ver-b');
      expect(el.querySelector('.mx-source-broken')).not.toBeNull();
      // The authored expression is still there, pointing at what it was written against.
      expect(emitted.at(-1)!.inputs[0].value.source!.fieldKey).toBe('alpha_field');
      expect(emitted.at(-1)!.inputs[0].value.source!.workflowVersionId).toBe('ver-a');
    });

    describe('a source gathered from a related workflow that is then retargeted or removed', () => {
      const alphaRead = { ...ALPHA_CATALOG.sources[0].reference, selectorId: selector.id };
      const authored = (): DesignerMappingConfiguration => ({
        ...withSelector,
        endpoint: {
          ...emptyEndpoint(),
          parameters: [
            { location: 'query', name: 'q', schema: { type: 'string' } },
            { location: 'query', name: 'r', schema: { type: 'string' } },
          ],
        },
        inputs: [
          { location: 'query', name: 'q', value: sourceNode(alphaRead) },
          { location: 'query', name: 'r', value: emptyNode() },
        ],
      });

      async function gatherAlpha() {
        service.relatedCatalog.mockImplementation((_definition: string, version: string) =>
          of(version === 'ver-a' ? ALPHA_CATALOG : BETA_CATALOG),
        );
        const opened = await openRelated(authored());
        const ctx = opened.fixture.debugElement.injector.get(MappingEditContext);
        opened.search('field');
        await opened.settle();
        opened
          .leaf('alpha_field')!
          .querySelector<HTMLInputElement>('input[type="checkbox"]')!
          .click();
        await opened.settle();
        const gathered = ctx.tray()[0];
        expect(gathered.reference.selectorId).toBe(selector.id);
        const target = { id: 't', label: 'q', apply: vi.fn(), occupied: () => false };
        const mapped = () => target.apply.mock.calls.length;
        const stageOps = () =>
          opened.el.querySelectorAll<HTMLSelectElement>(
            '[data-slot="input:query:r"] select.wf-input',
          );
        const chooseSource = async () => {
          const select = stageOps()[0];
          select.value = 'source';
          select.dispatchEvent(new Event('change', { bubbles: true }));
          await opened.settle();
        };
        return { ...opened, ctx, gathered, target, mapped, chooseSource };
      }

      it('is dropped from the tray on a retarget, so it cannot be mapped, picked or offered any more', async () => {
        const { ctx, gathered, target, mapped, retarget, chooseSource, el, emitted, settle } =
          await gatherAlpha();
        expect(ctx.liveTray()).toHaveLength(1);
        await retarget('ver-b');

        expect(ctx.tray()).toEqual([]);
        expect(ctx.liveTray()).toEqual([]);
        // Entry point 1: mapping straight onto a target (also what drop and the Map button call).
        expect(ctx.mapTo(target, gathered)).toBe(false);
        expect(mapped()).toBe(0);
        await settle();
        expect(el.querySelector('p.mx-sr')!.textContent).toContain('nothing was mapped');
        // Entry point 2: the transform editor’s tray picker adopts nothing from the old workflow.
        const before = emitted.length;
        await chooseSource();
        expect(el.querySelector('[data-slot="input:query:r"]')!.textContent).toContain(
          'The tray is empty',
        );
        expect(emitted.slice(before).every((c) => c.inputs[1].value.op === 'literal')).toBe(true);
        // Entry point 3: the browser no longer lists the tray item.
        expect(el.querySelectorAll('[data-testid="tray"] [role="listitem"]')).toHaveLength(0);
      });

      it('does not come back when the selector is pointed at the old workflow again', async () => {
        const { ctx, gathered, target, mapped, retarget } = await gatherAlpha();
        await retarget('ver-b');
        await retarget('ver-a');
        expect(ctx.liveTray()).toEqual([]);
        expect(ctx.mapTo(target, gathered)).toBe(true);
        expect(mapped()).toBe(1);
      });

      it('is dropped from the tray when the selector is removed', async () => {
        const { ctx, gathered, target, mapped, el, settle, chooseSource } = await gatherAlpha();
        Array.from(el.querySelectorAll('button'))
          .find((b) => b.textContent!.includes('Remove related workflow'))!
          .click();
        await settle();
        expect(ctx.tray()).toEqual([]);
        expect(ctx.mapTo(target, gathered)).toBe(false);
        expect(mapped()).toBe(0);
        await chooseSource();
        expect(el.querySelector('[data-slot="input:query:r"]')!.textContent).toContain(
          'The tray is empty',
        );
      });

      it('keeps the authored expression that reads it, shown as broken rather than deleted', async () => {
        const { retarget, el, emitted } = await gatherAlpha();
        await retarget('ver-b');
        expect(el.querySelector('[data-slot="input:query:q"] .mx-source-broken')).not.toBeNull();
        expect(emitted.at(-1)!.inputs[0].value.source).toEqual(alphaRead);
      });

      it('leaves sources of the tree and of another selector in the tray', async () => {
        const { ctx, retarget } = await gatherAlpha();
        const own = descriptor();
        ctx.toggleTray(own);
        await retarget('ver-b');
        expect(ctx.tray().map((d) => d.reference.fieldKey)).toEqual(['customer.id']);
        expect(ctx.liveTray()).toHaveLength(1);
      });
    });
  });

  describe('input bindings the endpoint no longer declares', () => {
    const endpoint = (names: string[]) => ({
      ...emptyEndpoint(),
      method: 'GET',
      path: '/orders/{orderId}',
      parameters: names.map((name) => ({
        location: 'query' as const,
        name,
        schema: { type: 'string' as const },
      })),
    });
    const mapped = (name: string, value = literalNode('A-1')) => ({
      location: 'query' as const,
      name,
      value,
    });
    const configWith = (
      names: string[],
      inputs: DesignerMappingConfiguration['inputs'],
    ): DesignerMappingConfiguration => ({
      ...emptyConfiguration(),
      endpoint: endpoint(names),
      inputs,
    });

    async function openInputs(config: DesignerMappingConfiguration) {
      const opened = await open(config);
      await opened.go('inputs');
      const orphans = () => opened.el.querySelector('[data-testid="orphan-inputs"]');
      const settle = async () => {
        opened.fixture.detectChanges();
        await opened.fixture.whenStable();
        opened.fixture.detectChanges();
      };
      return { ...opened, orphans, settle };
    }

    it('shows a binding whose parameter was renamed and marks the stage as needing attention', async () => {
      const { el, orphans, tab } = await openInputs(
        configWith(['customerId'], [mapped('customer')]),
      );
      expect(orphans()).not.toBeNull();
      expect(orphans()!.textContent).toContain('customer');
      expect(orphans()!.textContent).toContain('Not in the endpoint');
      expect(tab('inputs').textContent).toContain('Needs attention');
      // The authored expression is still displayed, not hidden.
      expect(el.querySelector('[data-orphan="query:customer"] [data-slot]')).not.toBeNull();
    });

    it('shows nothing extra while every binding matches a declared parameter', async () => {
      const { orphans, tab } = await openInputs(configWith(['customer'], [mapped('customer')]));
      expect(orphans()).toBeNull();
      expect(tab('inputs').textContent).toContain('Done');
    });

    it('never deletes an orphan on its own, however the endpoint is edited', async () => {
      const { fixture, el, emitted, go } = await open(
        configWith(['customer'], [mapped('customer')]),
      );
      await go('endpoint');
      const parameterName = Array.from(el.querySelectorAll<HTMLInputElement>('input')).find(
        (i) => i.value === 'customer',
      )!;
      parameterName.value = 'customerId';
      parameterName.dispatchEvent(new Event('input', { bubbles: true }));
      parameterName.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      const last = emitted.at(-1)!;
      expect(last.endpoint!.parameters.map((p) => p.name)).toEqual(['customerId']);
      expect(last.inputs).toEqual([mapped('customer')]);
      await go('inputs');
      expect(el.querySelector('[data-testid="orphan-inputs"]')).not.toBeNull();
    });

    it('moves the authored expression, unchanged, to a current input', async () => {
      const expression = literalNode('A-1');
      const { el, emitted, orphans, settle } = await openInputs(
        configWith(['customerId'], [mapped('customer', expression)]),
      );
      const remap = el.querySelector<HTMLSelectElement>('[data-testid="orphan-remap"]')!;
      expect(Array.from(remap.options).map((o) => o.value)).toEqual(['', 'query:customerId']);
      remap.value = 'query:customerId';
      remap.dispatchEvent(new Event('change', { bubbles: true }));
      await settle();
      expect(emitted.at(-1)!.inputs).toEqual([
        { location: 'query', name: 'customerId', value: expression },
      ]);
      expect(orphans()).toBeNull();
      expect(el.querySelector('[data-input="query:customerId"]')).not.toBeNull();
    });

    it('offers only unmapped current inputs as remap targets', async () => {
      const { el } = await openInputs(configWith(['a', 'b'], [mapped('a'), mapped('gone')]));
      const remap = el.querySelector<HTMLSelectElement>('[data-testid="orphan-remap"]')!;
      expect(Array.from(remap.options).map((o) => o.value)).toEqual(['', 'query:b']);
    });

    it('removes an orphan only when asked, and the server then accepts the repaired mapping', async () => {
      service.validate.mockImplementation(
        (_d: string, _v: string, request: { configuration: DesignerMappingConfiguration }) => {
          const declared = new Set(
            request.configuration.endpoint!.parameters.map((p) => `${p.location}:${p.name}`),
          );
          const unknown = request.configuration.inputs.filter(
            (b) => !declared.has(`${b.location}:${b.name}`),
          );
          return of({
            valid: !unknown.length,
            canPublish: !unknown.length,
            errors: unknown.map((b) => `Unknown mapping destination ${b.location}:${b.name}.`),
          });
        },
      );
      const { fixture, el, emitted, orphans, settle, tab } = await openInputs(
        configWith(['customerId'], [mapped('customer')]),
      );
      const check = () =>
        Array.from(el.querySelectorAll('button')).find((b) =>
          b.textContent!.includes('Check the whole mapping'),
        )!;
      check().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.textContent).toContain('Unknown mapping destination query:customer');
      expect(el.textContent).not.toContain('The server accepted this mapping.');

      el.querySelector<HTMLButtonElement>('[data-orphan] button.mx-danger')!.click();
      await settle();
      expect(emitted.at(-1)!.inputs).toEqual([]);
      expect(orphans()).toBeNull();

      check().click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.textContent).toContain('The server accepted this mapping.');
      expect(tab('inputs').textContent).not.toContain('Needs attention');
    });

    it('keeps the same editor, its focus and its target while an unmatched input is typed into', async () => {
      const { el, emitted, settle } = await openInputs(
        configWith(['customerId'], [mapped('customer', literalNode('')), mapped('other')]),
      );
      const slot = el.querySelector('[data-orphan="query:customer"]')!;
      const box = slot.querySelector<HTMLInputElement>('input[type="text"]')!;
      box.focus();
      expect(document.activeElement).toBe(box);

      let typed = '';
      for (const character of 'A-12') {
        typed += character;
        box.value = typed;
        box.dispatchEvent(new Event('input', { bubbles: true }));
        await settle();
        // The very same elements survive every keystroke; recreating them would drop focus and the caret.
        expect(el.querySelector('[data-orphan="query:customer"]')).toBe(slot);
        expect(slot.querySelector('input[type="text"]')).toBe(box);
        expect(document.activeElement).toBe(box);
        expect(box.value).toBe(typed);
        const last = emitted.at(-1)!.inputs;
        expect(last[0]).toEqual({ location: 'query', name: 'customer', value: literalNode(typed) });
        expect(last).toHaveLength(2);
      }
    });

    it('shows orphan bindings in a read-only version but offers no way to change them', async () => {
      host.readonly.mockReturnValue(true);
      const { el, orphans } = await openInputs(configWith(['customerId'], [mapped('customer')]));
      expect(orphans()).not.toBeNull();
      expect(el.querySelector('[data-testid="orphan-remap"]')).toBeNull();
      expect(el.querySelector('[data-orphan] button.mx-danger')).toBeNull();
    });

    it('shows a binding for a body the endpoint dropped, and for every binding when there is no endpoint', async () => {
      const dropped: DesignerMappingConfiguration = {
        ...emptyConfiguration(),
        endpoint: endpoint([]),
        inputs: [{ location: 'body', name: '$body', value: literalNode('x') }],
      };
      const body = await openInputs(dropped);
      expect(body.el.querySelector('[data-orphan="body:$body"]')).not.toBeNull();
    });
  });

  describe('the mapping target of an editor that is gone', () => {
    const config: DesignerMappingConfiguration = {
      ...emptyConfiguration(),
      endpoint: {
        ...emptyEndpoint(),
        parameters: [
          { location: 'query', name: 'a', schema: { type: 'string' } },
          { location: 'query', name: 'b', schema: { type: 'string' } },
        ],
        response: { type: 'object', properties: { id: { type: 'string' } } },
      },
      inputs: [
        { location: 'query', name: 'a', value: literalNode('') },
        { location: 'query', name: 'b', value: literalNode('') },
      ],
      outputs: [],
    };

    async function openTargets() {
      const opened = await open(config);
      await opened.go('inputs');
      const settle = async () => {
        opened.fixture.detectChanges();
        await opened.fixture.whenStable();
        opened.fixture.detectChanges();
      };
      const useAsTarget = async (slot: string) => {
        opened.el
          .querySelector<HTMLButtonElement>(`[data-slot="${slot}"] .mx-slot-head button`)!
          .click();
        await settle();
      };
      const announcement = () => opened.el.querySelector('p.mx-sr')!.textContent!.trim();
      const targetBanner = () =>
        Array.from(opened.el.querySelectorAll('[role="status"]')).find((n) =>
          n.textContent!.includes('Map buttons write to:'),
        );
      return { ...opened, settle, useAsTarget, announcement, targetBanner };
    }

    it('is released when the author switches to another stage', async () => {
      const { go, useAsTarget, targetBanner, el } = await openTargets();
      await useAsTarget('input:query:a');
      expect(targetBanner()!.textContent).toContain('a');
      await go('outputs');
      await go('inputs');
      expect(targetBanner()).toBeUndefined();
      expect(el.querySelector('.mx-slot-active')).toBeNull();
    });

    const stop = (el: HTMLElement, key: string) =>
      el.querySelector<HTMLButtonElement>(`[data-input="${key}"] button.mx-danger`)!.click();

    it('is released when its input is removed', async () => {
      const { el, settle, useAsTarget, targetBanner } = await openTargets();
      await useAsTarget('input:query:a');
      expect(targetBanner()!.textContent).toContain('“a”');
      stop(el, 'query:a');
      await settle();
      expect(el.querySelector('[data-slot="input:query:a"]')).toBeNull();
      expect(targetBanner()).toBeUndefined();
    });

    it('leaves a newer target alone when an older editor is destroyed afterwards', async () => {
      const { el, settle, useAsTarget, targetBanner } = await openTargets();
      await useAsTarget('input:query:a');
      await useAsTarget('input:query:b');
      expect(targetBanner()!.textContent).toContain('“b”');
      stop(el, 'query:a');
      await settle();
      expect(el.querySelector('[data-slot="input:query:a"]')).toBeNull();
      expect(targetBanner()!.textContent).toContain('“b”');
      expect(el.querySelector('[data-slot="input:query:b"].mx-slot-active')).not.toBeNull();
    });

    it('never reports a success for a mapping onto a destroyed editor', async () => {
      const { fixture, el, settle, useAsTarget, announcement, emitted } = await openTargets();
      await useAsTarget('input:query:a');
      const ctx = fixture.debugElement.injector.get(MappingEditContext);
      const stale = ctx.activeTarget()!;
      expect(stale.alive!()).toBe(true);
      stop(el, 'query:a');
      await settle();
      expect(stale.alive!()).toBe(false);

      const before = emitted.length;
      expect(ctx.mapTo(stale, descriptor())).toBe(false);
      await settle();
      expect(announcement()).toContain('nothing was mapped');
      expect(announcement()).not.toContain('mapped to');
      expect(emitted.length).toBe(before);
      expect(el.querySelector('[data-slot="input:query:a"]')).toBeNull();
    });

    it('does not let the Map button write to a target that was released', async () => {
      const { fixture, el, settle, useAsTarget } = await openTargets();
      await useAsTarget('input:query:a');
      const ctx = fixture.debugElement.injector.get(MappingEditContext);
      ctx.toggleTray(descriptor());
      stop(el, 'query:a');
      await settle();
      expect(ctx.activeTarget()).toBeNull();
      expect(ctx.mapToActive(descriptor())).toBe(false);
    });
  });
});
