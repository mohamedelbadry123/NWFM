import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import { sourceNode } from './mapping-config';
import { MappingEditContext } from './mapping-edit-context';
import { WORKFLOW_MAPPING_HOST, type WorkflowMappingHost } from './mapping-host';
import { WorkflowMappingService } from './workflow-mapping.service';
import {
  WorkflowStepMappingComponent,
  bindingKey,
  stepDestinations,
} from './workflow-step-mapping.component';
import type {
  CallableTarget,
  MappingCatalog,
  MappingDefinitionChoice,
  SourceDescriptor,
  StepFieldBinding,
} from './mapping.models';

const OWNER: CallableTarget = {
  workflowDefinitionId: 'def',
  workflowVersionId: 'ver',
  nodeKey: 'Task_1',
};

const field = (
  fieldKey: string,
  label: string,
  over: Partial<SourceDescriptor['reference']> = {},
): SourceDescriptor => ({
  reference: {
    workflowDefinitionId: 'def',
    workflowVersionId: 'ver',
    nodeKey: 'Task_1',
    kind: 'FormField',
    fieldKey,
    jsonPointer: '',
    formStepId: 'step-1',
    formId: 'form-1',
    formVersion: 2,
    ...over,
  },
  label,
  breadcrumbs: ['Order', 'Review', 'Step', 'Form', label],
  schema: { type: 'string', required: true, inferred: true },
  availability: 'SubmittedForm',
});

const otherStepField = field('amount', 'Amount', { nodeKey: 'Task_2' });
const CATALOG: MappingCatalog = {
  sources: [field('customer.id', 'Customer id'), field('notes', 'Notes'), otherStepField],
  targets: [],
  diagnostics: [],
};

describe('stepDestinations', () => {
  it('offers only the form fields of this Step in this workflow', () => {
    expect(stepDestinations(CATALOG.sources, OWNER).map((d) => d.reference.fieldKey)).toEqual([
      'customer.id',
      'notes',
    ]);
    expect(stepDestinations(CATALOG.sources, null)).toEqual([]);
    const otherWorkflow = field('notes', 'Notes', { workflowDefinitionId: 'child' });
    expect(stepDestinations([otherWorkflow], OWNER)).toEqual([]);
  });

  it('keys a binding by Step form, form, version and the literal field key', () => {
    expect(bindingKey({ formStepId: 's', formId: 'f', formVersion: 2, fieldKey: 'a.b' })).toBe(
      's|f|2|a.b',
    );
  });
});

describe('Step field values', () => {
  const service = {
    catalog: vi.fn(),
    relatedDefinitions: vi.fn(),
    relatedCatalog: vi.fn(),
    validate: vi.fn(),
  };
  let host: WorkflowMappingHost & { saved: number };

  async function mount(
    configuration = '{}',
    opts: {
      readonly?: boolean;
      catalog?: MappingCatalog;
      failCatalog?: boolean;
      choices?: MappingDefinitionChoice[];
    } = {},
  ) {
    service.catalog.mockReturnValue(
      opts.failCatalog
        ? throwError(() => ({ error: { message: 'Catalog unavailable.' } }))
        : of(opts.catalog ?? CATALOG),
    );
    service.relatedDefinitions.mockReturnValue(of(opts.choices ?? []));
    host = {
      saved: 0,
      owner: () => OWNER,
      root: () => ({ definitionId: 'def', versionId: 'ver' }),
      readonly: () => opts.readonly ?? false,
      isChild: () => false,
      ensureSaved: async () => {
        host.saved++;
      },
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowMappingService, useValue: service },
        { provide: WORKFLOW_MAPPING_HOST, useValue: host },
      ],
    });
    const fixture = TestBed.createComponent(WorkflowStepMappingComponent);
    const emitted: string[] = [];
    fixture.componentInstance.configurationChange.subscribe((c) => {
      emitted.push(c);
      fixture.componentRef.setInput('configuration', c);
      fixture.detectChanges();
    });
    fixture.componentRef.setInput('configuration', configuration);
    fixture.componentRef.setInput('readonly', opts.readonly ?? false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (text: string) =>
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes(text))!;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const mapped = () =>
      (
        JSON.parse(emitted[emitted.length - 1] ?? configuration) as {
          typedMapping?: { stepBindings: StepFieldBinding[] };
        }
      ).typedMapping;
    return {
      fixture,
      el,
      emitted,
      button,
      settle,
      mapped,
      ctx: fixture.debugElement.injector.get(MappingEditContext),
    };
  }

  const choose = (select: HTMLSelectElement, value: string) => {
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const fieldPicker = (el: HTMLElement) =>
    Array.from(el.querySelectorAll<HTMLSelectElement>('select.wf-input')).find((s) =>
      Array.from(s.options).some((o) => o.value === 'step-1|form-1|2|notes'),
    )!;

  beforeEach(() => Object.values(service).forEach((fn) => fn.mockReset()));

  it('saves the draft, loads the catalog from the pinned root and offers this Step’s fields only', async () => {
    const { el, ctx } = await mount();
    expect(host.saved).toBe(1);
    expect(service.catalog).toHaveBeenCalledWith('def', 'ver');
    const options = Array.from(fieldPicker(el).options).map((o) => o.value);
    expect(options).toEqual(['', 'step-1|form-1|2|customer.id', 'step-1|form-1|2|notes']);
    expect(ctx.hideOwnForm()).toBe(true);
    expect(el.textContent).toContain('No fields are mapped on this Step.');
  });

  it('maps a field read-only by default, pinned to its exact Step form, form and version, with a literal dotted key', async () => {
    const { el, button, settle, mapped } = await mount();
    choose(fieldPicker(el), 'step-1|form-1|2|customer.id');
    await settle();
    button('Map this field').click();
    await settle();
    const [binding] = mapped()!.stepBindings;
    expect(binding).toEqual(
      expect.objectContaining({
        formStepId: 'step-1',
        formId: 'form-1',
        formVersion: 2,
        fieldKey: 'customer.id',
        mode: 'ReadOnly',
      }),
    );
    expect(binding.schema).toEqual({ type: 'string' });
    expect(el.querySelector(`[data-binding="${binding.id}"]`)).not.toBeNull();
    // A field that is mapped is no longer offered again.
    expect(Array.from(fieldPicker(el)?.options ?? []).map((o) => o.value)).not.toContain(
      'step-1|form-1|2|customer.id',
    );
  });

  it('switches a binding between Read-only and Prefill and removes it, dropping the typed mapping when nothing is left', async () => {
    const { el, button, settle, mapped, emitted } = await mount();
    choose(fieldPicker(el), 'step-1|form-1|2|notes');
    await settle();
    button('Map this field').click();
    await settle();

    const behaviour = Array.from(
      el.querySelectorAll<HTMLSelectElement>('[data-binding] select.wf-input'),
    ).find((s) => Array.from(s.options).some((o) => o.value === 'Prefill'))!;
    choose(behaviour, 'Prefill');
    await settle();
    expect(mapped()!.stepBindings[0].mode).toBe('Prefill');

    el.querySelector<HTMLButtonElement>('[data-binding] button.mx-danger')!.click();
    await settle();
    const last = JSON.parse(emitted[emitted.length - 1]) as Record<string, unknown>;
    expect(last).not.toHaveProperty('typedMapping');
  });

  it('keeps unrelated Step configuration untouched when it edits the mapping', async () => {
    const { el, button, settle, emitted } = await mount(
      JSON.stringify({ formId: 'form-1', assignee: { role: 'Reviewer' } }),
    );
    choose(fieldPicker(el), 'step-1|form-1|2|notes');
    await settle();
    button('Map this field').click();
    await settle();
    const saved = JSON.parse(emitted[emitted.length - 1]) as Record<string, unknown>;
    expect(saved['formId']).toBe('form-1');
    expect(saved['assignee']).toEqual({ role: 'Reviewer' });
    expect(saved['typedMapping']).toBeDefined();
  });

  it('shows an existing binding and says when its field no longer matches the current form version', async () => {
    const stale = {
      typedMapping: {
        contractVersion: 1,
        stepBindings: [
          {
            id: 'step1',
            formStepId: 'step-1',
            formId: 'form-1',
            formVersion: 1,
            fieldKey: 'notes',
            mode: 'ReadOnly',
            schema: { type: 'string' },
            value: { op: 'literal', value: 'x' },
          },
        ],
      },
    };
    const { el, button, settle, mapped } = await mount(JSON.stringify(stale));
    expect(el.querySelector('[data-binding="step1"]')).not.toBeNull();
    expect(el.querySelector('[data-binding="step1"] [role="alert"]')!.textContent).toContain(
      'no longer matches',
    );
    button('Repin to the current form').click();
    await settle();
    expect(mapped()!.stepBindings[0]).toEqual(
      expect.objectContaining({ formVersion: 2, formId: 'form-1', fieldKey: 'notes' }),
    );
    expect(el.querySelector('[data-binding="step1"] [role="alert"]')).toBeNull();
  });

  it('explains when the Step has no form fields to map', async () => {
    const { el } = await mount('{}', {
      catalog: { sources: [otherStepField], targets: [], diagnostics: [] },
    });
    expect(el.textContent).toContain('This Step has no form fields yet');
    expect(el.querySelector<HTMLSelectElement>('select[disabled]')).not.toBeNull();
  });

  it('asks the server to check the bindings and shows what it refuses', async () => {
    service.validate.mockReturnValue(
      of({ valid: false, errors: ['Field "notes" is not on form version 2.'] }),
    );
    const { el, button, settle } = await mount();
    button('Check these bindings').click();
    await settle();
    expect(service.validate).toHaveBeenCalledWith(
      'def',
      'ver',
      expect.objectContaining({ owner: OWNER, configuration: expect.anything() }),
    );
    expect(el.textContent).toContain('Field "notes" is not on form version 2.');
    expect(el.textContent).not.toContain('The server accepted these bindings.');

    service.validate.mockReturnValue(of({ valid: true, errors: [] }));
    button('Check these bindings').click();
    await settle();
    expect(el.textContent).toContain('The server accepted these bindings.');
  });

  it('keeps the UI honest when the server cannot be asked', async () => {
    service.validate.mockReturnValue(throwError(() => ({})));
    const { el, button, settle } = await mount();
    button('Check these bindings').click();
    await settle();
    expect(el.textContent).toContain('The server could not check these bindings.');
  });

  it('shows the catalog failure with a way to try again, and loads again on request', async () => {
    const { el, button, settle } = await mount('{}', { failCatalog: true });
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Catalog unavailable.');
    service.catalog.mockReturnValue(of(CATALOG));
    button('Try again').click();
    await settle();
    await settle();
    expect(el.querySelector('p.mx-error')).toBeNull();
    expect(service.catalog).toHaveBeenCalledTimes(2);
  });

  it('is read-only for a published version: it never saves, and nothing can be added, changed or removed', async () => {
    const config = JSON.stringify({
      typedMapping: {
        contractVersion: 1,
        stepBindings: [
          {
            id: 'step1',
            formStepId: 'step-1',
            formId: 'form-1',
            formVersion: 2,
            fieldKey: 'notes',
            mode: 'Prefill',
            schema: { type: 'string' },
            value: { op: 'literal', value: 'x' },
          },
        ],
      },
    });
    const { el, emitted } = await mount(config, { readonly: true });
    expect(host.saved).toBe(0);
    expect(el.querySelector('[data-binding="step1"]')).not.toBeNull();
    expect(el.querySelector('[data-binding] button.mx-danger')).toBeNull();
    expect(
      Array.from(el.querySelectorAll<HTMLSelectElement>('[data-binding] > .mx-row select')).every(
        (s) => s.disabled,
      ),
    ).toBe(true);
    expect(
      Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim()),
    ).not.toContain('Map this field');
    expect(emitted).toEqual([]);
  });

  describe('related workflow catalogs follow the selector’s exact pins', () => {
    const choice = (id: string, name: string): MappingDefinitionChoice => ({
      definitionId: `def-${id}`,
      versionId: `ver-${id}`,
      versionNumber: 1,
      name,
    });
    const ALPHA = choice('a', 'Alpha');
    const BETA = choice('b', 'Beta');
    const relatedOf = (c: MappingDefinitionChoice, key: string): MappingCatalog => ({
      sources: [
        {
          ...field(key, key, {
            workflowDefinitionId: c.definitionId,
            workflowVersionId: c.versionId,
          }),
          breadcrumbs: [c.name, 'Task', 'Form', key],
        },
      ],
      targets: [],
      diagnostics: [],
    });
    const configuration = JSON.stringify({
      typedMapping: {
        contractVersion: 1,
        selectors: [
          {
            id: 'rel-1',
            workflowDefinitionId: ALPHA.definitionId,
            workflowVersionId: ALPHA.versionId,
            keyKind: 'BusinessKey',
            keyValue: { op: 'literal', value: 'K-1' },
            multipleMatches: 'Attention',
            missing: { mode: 'Attention' },
          },
        ],
        stepBindings: [],
      },
    });

    /** Like a transport that cannot be aborted: it ignores unsubscription and is answered by hand. */
    const unabortable = () => {
      const answer: { send: (catalog: MappingCatalog) => void; calls: number } = {
        send: () => undefined,
        calls: 0,
      };
      const source = {
        subscribe: (observer: { next: (c: MappingCatalog) => void }) => {
          answer.calls++;
          answer.send = (c) => observer.next(c);
          return { unsubscribe: () => undefined };
        },
      };
      return { answer, source };
    };

    async function openRelated() {
      const opened = await mount(configuration, { choices: [ALPHA, BETA] });
      const search = async (text: string) => {
        const box = opened.el.querySelector<HTMLInputElement>('input[type="search"]')!;
        box.value = text;
        box.dispatchEvent(new Event('input', { bubbles: true }));
        await opened.settle();
      };
      const leaf = (key: string) => opened.el.querySelector(`[data-source="${key}"]`);
      const retarget = async (versionId: string) => {
        choose(
          opened.el.querySelector<HTMLSelectElement>('[data-selector="rel-1"] select')!,
          versionId,
        );
        await opened.settle();
      };
      return { ...opened, search, leaf, retarget };
    }

    it('shows only the new workflow’s sources after a retarget, even if the old answer arrives late', async () => {
      const slowAlpha = unabortable();
      service.relatedCatalog.mockImplementation((_d: string, v: string) =>
        v === ALPHA.versionId ? slowAlpha.source : of(relatedOf(BETA, 'beta_field')),
      );
      const { search, leaf, retarget, emitted, settle } = await openRelated();
      expect(slowAlpha.answer.calls).toBeGreaterThan(0);

      await retarget(BETA.versionId);
      const saved = JSON.parse(emitted.at(-1)!) as {
        typedMapping: { selectors: Array<{ workflowVersionId: string }> };
      };
      expect(saved.typedMapping.selectors[0].workflowVersionId).toBe(BETA.versionId);
      await search('field');
      expect(leaf('beta_field')).not.toBeNull();

      slowAlpha.answer.send(relatedOf(ALPHA, 'alpha_field'));
      await settle();
      expect(leaf('alpha_field')).toBeNull();
      expect(leaf('beta_field')).not.toBeNull();
      expect(service.relatedCatalog).toHaveBeenLastCalledWith(BETA.definitionId, BETA.versionId);
    });

    it('drops the old workflow’s sources while the new catalog is still loading', async () => {
      const slowBeta = unabortable();
      service.relatedCatalog.mockImplementation((_d: string, v: string) =>
        v === ALPHA.versionId ? of(relatedOf(ALPHA, 'alpha_field')) : slowBeta.source,
      );
      const { search, leaf, retarget, settle } = await openRelated();
      await search('field');
      expect(leaf('alpha_field')).not.toBeNull();

      await retarget(BETA.versionId);
      await search('field');
      expect(leaf('alpha_field')).toBeNull();

      slowBeta.answer.send(relatedOf(BETA, 'beta_field'));
      await settle();
      expect(leaf('beta_field')).not.toBeNull();
      expect(leaf('alpha_field')).toBeNull();
    });

    it('forgets a removed selector’s catalog and loads a fresh one when it is added again', async () => {
      service.relatedCatalog.mockReturnValue(of(relatedOf(ALPHA, 'alpha_field')));
      const { el, search, leaf, settle } = await openRelated();
      await search('field');
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
      choose(add, ALPHA.versionId);
      await settle();
      expect(service.relatedCatalog.mock.calls.length).toBe(calls + 1);
      await search('field');
      expect(leaf('alpha_field')).not.toBeNull();
    });

    describe('a source gathered from a related workflow that is then retargeted or removed', () => {
      const withBinding = JSON.stringify({
        typedMapping: {
          ...(JSON.parse(configuration) as { typedMapping: object }).typedMapping,
          stepBindings: [
            {
              id: 'step1',
              formStepId: 'step-1',
              formId: 'form-1',
              formVersion: 2,
              fieldKey: 'notes',
              mode: 'Prefill',
              schema: { type: 'string' },
              value: { op: 'literal', value: '' },
            },
            {
              id: 'step2',
              formStepId: 'step-1',
              formId: 'form-1',
              formVersion: 2,
              fieldKey: 'customer.id',
              mode: 'Prefill',
              schema: { type: 'string' },
              value: sourceNode({
                ...relatedOf(ALPHA, 'alpha_field').sources[0].reference,
                selectorId: 'rel-1',
              }),
            },
          ],
        },
      });

      async function gatherAlpha() {
        service.relatedCatalog.mockImplementation((_d: string, v: string) =>
          of(
            relatedOf(
              v === ALPHA.versionId ? ALPHA : BETA,
              v === ALPHA.versionId ? 'alpha_field' : 'beta_field',
            ),
          ),
        );
        const opened = await mount(withBinding, { choices: [ALPHA, BETA] });
        const retarget = async (versionId: string) => {
          choose(
            opened.el.querySelector<HTMLSelectElement>('[data-selector="rel-1"] select')!,
            versionId,
          );
          await opened.settle();
        };
        const box = opened.el.querySelector<HTMLInputElement>('input[type="search"]')!;
        box.value = 'field';
        box.dispatchEvent(new Event('input', { bubbles: true }));
        await opened.settle();
        opened.el
          .querySelector('[data-source="alpha_field"]')!
          .querySelector<HTMLInputElement>('input[type="checkbox"]')!
          .click();
        await opened.settle();
        const gathered = opened.ctx.tray()[0];
        expect(gathered.reference.selectorId).toBe('rel-1');
        const target = { id: 't', label: 'notes', apply: vi.fn(), occupied: () => false };
        const chooseSource = async () => {
          choose(
            opened.el.querySelector<HTMLSelectElement>(
              '[data-binding="step1"] [data-slot] select.wf-input',
            )!,
            'source',
          );
          await opened.settle();
        };
        return { ...opened, retarget, gathered, target, chooseSource };
      }

      it('is dropped from the tray on a retarget and can no longer be mapped, picked or listed', async () => {
        const { ctx, gathered, target, retarget, chooseSource, el, settle, emitted } =
          await gatherAlpha();
        expect(ctx.liveTray()).toHaveLength(1);
        await retarget(BETA.versionId);
        expect(ctx.tray()).toEqual([]);
        expect(ctx.mapTo(target, gathered)).toBe(false);
        expect(target.apply).not.toHaveBeenCalled();
        await settle();
        expect(el.textContent).toContain('nothing was mapped');

        const before = emitted.length;
        await chooseSource();
        expect(el.querySelector('[data-binding="step1"]')!.textContent).toContain(
          'The tray is empty',
        );
        const afterChoose = emitted.slice(before).map((c) => JSON.parse(c) as never) as Array<{
          typedMapping: { stepBindings: Array<{ id: string; value: { op: string } }> };
        }>;
        expect(
          afterChoose.every(
            (c) =>
              c.typedMapping.stepBindings.find((b) => b.id === 'step1')!.value.op === 'literal',
          ),
        ).toBe(true);
        expect(el.querySelectorAll('[data-testid="tray"] [role="listitem"]')).toHaveLength(0);
      });

      it('is dropped from the tray when the selector is removed', async () => {
        const { ctx, gathered, target, el, settle } = await gatherAlpha();
        Array.from(el.querySelectorAll('button'))
          .find((b) => b.textContent!.includes('Remove related workflow'))!
          .click();
        await settle();
        expect(ctx.tray()).toEqual([]);
        expect(ctx.mapTo(target, gathered)).toBe(false);
        expect(target.apply).not.toHaveBeenCalled();
      });

      it('keeps the Step value that reads it, shown as broken rather than deleted', async () => {
        const { retarget, el, emitted } = await gatherAlpha();
        await retarget(BETA.versionId);
        expect(el.querySelector('[data-binding="step2"] .mx-source-broken')).not.toBeNull();
        const last = JSON.parse(emitted.at(-1)!) as {
          typedMapping: {
            stepBindings: Array<{ id: string; value: { source: { fieldKey: string } } }>;
          };
        };
        expect(
          last.typedMapping.stepBindings.find((b) => b.id === 'step2')!.value.source.fieldKey,
        ).toBe('alpha_field');
      });
    });
  });

  describe('the mapping target of a Step binding that is gone', () => {
    const config = JSON.stringify({
      typedMapping: {
        contractVersion: 1,
        stepBindings: [
          {
            id: 'step1',
            formStepId: 'step-1',
            formId: 'form-1',
            formVersion: 2,
            fieldKey: 'notes',
            mode: 'Prefill',
            schema: { type: 'string' },
            value: { op: 'literal', value: '' },
          },
        ],
      },
    });

    it('is released when its binding is removed, with no false success afterwards', async () => {
      const { el, settle, ctx, emitted } = await mount(config);
      el.querySelector<HTMLButtonElement>(
        '[data-binding="step1"] [data-slot] .mx-slot-head button',
      )!.click();
      await settle();
      const stale = ctx.activeTarget()!;
      expect(stale).not.toBeNull();
      expect(el.textContent).toContain('Map buttons write to:');

      el.querySelector<HTMLButtonElement>('[data-binding] button.mx-danger')!.click();
      await settle();
      expect(el.querySelector('[data-binding="step1"]')).toBeNull();
      expect(ctx.activeTarget()).toBeNull();
      expect(el.textContent).not.toContain('Map buttons write to:');

      const before = emitted.length;
      expect(ctx.mapTo(stale, CATALOG.sources[1])).toBe(false);
      await settle();
      expect(el.textContent).toContain('nothing was mapped');
      expect(emitted.length).toBe(before);
    });
  });
});
