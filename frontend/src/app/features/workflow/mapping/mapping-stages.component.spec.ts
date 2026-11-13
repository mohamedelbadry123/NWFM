import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import {
  MappingActionsStageComponent,
  initialActionInputs,
} from './mapping-actions-stage.component';
import { MappingEditContext } from './mapping-edit-context';
import { MappingOutputsStageComponent } from './mapping-outputs-stage.component';
import { MappingTestStageComponent } from './mapping-test-stage.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import {
  emptyConfiguration,
  emptyNode,
  literalNode,
  newSchema,
  sourceNode,
} from './mapping-config';
import type {
  CallableTarget,
  CallableTargetDescriptor,
  DesignerMappingConfiguration,
  FollowUpAction,
  MappingSchema,
  OutputDefinition,
  SourceDescriptor,
} from './mapping.models';

const OWNER: CallableTarget = {
  workflowDefinitionId: 'def',
  workflowVersionId: 'ver',
  nodeKey: 'Event_1',
};
const ROOT = { definitionId: 'def', versionId: 'ver' };
const field: SourceDescriptor = {
  reference: {
    workflowDefinitionId: 'def',
    workflowVersionId: 'ver',
    nodeKey: 'Task_1',
    kind: 'FormField',
    fieldKey: 'customer.id',
    jsonPointer: '',
    formStepId: 's',
    formId: 'f',
    formVersion: 1,
  },
  label: 'Customer id',
  breadcrumbs: ['Order', 'Review', 'Step', 'Form', 'Customer id'],
  schema: { type: 'string' },
  availability: 'SubmittedForm',
};
const target = (
  nodeKey: string,
  callable = true,
  inputs: MappingSchema = {
    type: 'object',
    properties: { id: { type: 'string' }, qty: { type: 'number' } },
  },
): CallableTargetDescriptor => ({
  target: { ...OWNER, nodeKey },
  label: nodeKey,
  breadcrumbs: ['Order', nodeKey],
  inputs,
  callable,
});

function configure(extra: unknown[] = []) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      MappingEditContext,
      { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
      ...(extra as never[]),
    ],
  });
  const ctx = TestBed.inject(MappingEditContext);
  ctx.owner.set(OWNER);
  ctx.root.set(ROOT);
  ctx.catalog.set({
    sources: [field],
    targets: [target('Event_1'), target('Event_2'), target('Event_3'), target('Task_9', false)],
    diagnostics: [],
  });
  return ctx;
}

const change = (el: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) => {
  el.value = value;
  el.dispatchEvent(
    new Event(el instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
  );
};

describe('Outputs stage', () => {
  async function mount(outputs: OutputDefinition[] = [], readonly = false) {
    const ctx = configure();
    ctx.readonly.set(readonly);
    const fixture = TestBed.createComponent(MappingOutputsStageComponent);
    const emitted: OutputDefinition[][] = [];
    fixture.componentInstance.outputsChange.subscribe((o) => {
      emitted.push(o);
      fixture.componentRef.setInput('outputs', o);
      fixture.detectChanges();
    });
    fixture.componentRef.setInput('outputs', outputs);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (text: string) =>
      Array.from(el.querySelectorAll('button')).find((b) =>
        b.textContent!.trim().startsWith(text),
      )!;
    return { fixture, ctx, el, emitted, button, last: () => emitted[emitted.length - 1] };
  }
  const output = (id: string, name: string): OutputDefinition => ({
    id,
    name,
    schema: newSchema('string'),
    value: literalNode(name),
  });

  it('says so when there are no outputs and offers to add one, with a stable generated id', async () => {
    const { el, button, last } = await mount();
    expect(el.textContent).toContain('No outputs yet');
    button('Add output').click();
    expect(last()).toHaveLength(1);
    expect(last()[0]).toEqual(
      expect.objectContaining({
        id: expect.stringMatching(/^out/),
        name: 'output',
        value: expect.objectContaining({ op: 'literal' }),
      }),
    );
    expect(el.querySelector(`[data-output="${last()[0].id}"]`)).not.toBeNull();
  });

  it('keeps the stable id when an output is renamed or reordered', async () => {
    const { el, emitted, last } = await mount([
      output('out_a', 'first'),
      output('out_b', 'second'),
    ]);
    const nameInput = () =>
      el.querySelector<HTMLInputElement>('[data-output="out_a"] input[aria-invalid]')!;
    change(nameInput(), 'renamed');
    nameInput().dispatchEvent(new Event('change', { bubbles: true }));
    expect(last().map((o) => [o.id, o.name])).toEqual([
      ['out_a', 'renamed'],
      ['out_b', 'second'],
    ]);

    el.querySelector<HTMLButtonElement>(
      '[data-output="out_a"] button[aria-label^="Move"][aria-label$="down"]',
    )!.click();
    expect(last().map((o) => o.id)).toEqual(['out_b', 'out_a']);
    expect(emitted).toHaveLength(2);
  });

  it('refuses a duplicate or empty name and tells the author', async () => {
    const { el, emitted, ctx } = await mount([output('out_a', 'first'), output('out_b', 'second')]);
    const input = el.querySelector<HTMLInputElement>('[data-output="out_b"] input[aria-invalid]')!;
    change(input, 'first');
    input.dispatchEvent(new Event('change', { bubbles: true }));
    expect(emitted).toEqual([]);
    expect(ctx.announcement()).toContain('not valid or already used');
  });

  it('turns every tray source into a named, typed output reading exactly that source, then empties the tray', async () => {
    const { fixture, ctx, button, last } = await mount();
    ctx.tray.set([{ ...field, sensitive: true }]);
    fixture.detectChanges();
    button('Add the tray as outputs').click();
    expect(last()).toHaveLength(1);
    expect(last()[0].name).toBe('Customer id');
    expect(last()[0].value.source).toEqual(field.reference);
    expect(last()[0].sensitive).toBe(true);
    expect(last()[0].schema.type).toBe('string');
    expect(ctx.tray()).toEqual([]);
  });

  it('removes only the chosen output', async () => {
    const { el, last } = await mount([output('out_a', 'first'), output('out_b', 'second')]);
    el.querySelector<HTMLButtonElement>('[data-output="out_a"] button.mx-danger')!.click();
    expect(last().map((o) => o.id)).toEqual(['out_b']);
  });

  it('renders each output as a type editor and a visual expression editor, never a JSON-only box', async () => {
    const { el } = await mount([output('out_a', 'first')]);
    expect(el.querySelector('app-mapping-schema-editor')).not.toBeNull();
    expect(el.querySelector('[data-slot="output:out_a:value"]')).not.toBeNull();
    expect(el.querySelector('textarea')).toBeNull();
  });

  it('is read-only for a published version', async () => {
    const { el, button } = await mount([output('out_a', 'first')], true);
    expect(button('Add output')).toBeUndefined();
    expect(el.querySelector('button.mx-danger')).toBeNull();
    expect(el.querySelector<HTMLInputElement>('[data-output] input')!.disabled).toBe(true);
  });
});

describe('Follow-up actions stage', () => {
  async function mount(
    actions: FollowUpAction[] = [],
    opts: { callable?: boolean; readonly?: boolean; inputs?: MappingSchema | null } = {},
  ) {
    const ctx = configure();
    ctx.readonly.set(opts.readonly ?? false);
    const fixture = TestBed.createComponent(MappingActionsStageComponent);
    const emitted: FollowUpAction[][] = [];
    const callable: boolean[] = [];
    const inputs: (MappingSchema | null)[] = [];
    fixture.componentInstance.actionsChange.subscribe((a) => {
      emitted.push(a);
      fixture.componentRef.setInput('actions', a);
      fixture.detectChanges();
    });
    fixture.componentInstance.callableChange.subscribe((c) => callable.push(c));
    fixture.componentInstance.invocationInputsChange.subscribe((i) => inputs.push(i));
    fixture.componentRef.setInput('callable', opts.callable ?? false);
    fixture.componentRef.setInput('invocationInputs', opts.inputs ?? null);
    fixture.componentRef.setInput('actions', actions);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, ctx, el, emitted, callable, inputs, last: () => emitted[emitted.length - 1] };
  }
  const action = (over: Partial<FollowUpAction> = {}): FollowUpAction => ({
    id: 'act_1',
    target: { ...OWNER, nodeKey: 'Event_2' },
    mode: 'MapOnly',
    inputs: initialActionInputs(target('Event_2').inputs),
    ...over,
  });
  const addButton = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent!.includes('Add follow-up action'),
    )!;

  it('starts an action on the first callable target other than itself, as MapOnly with one empty slot per declared input', async () => {
    const { el, last } = await mount();
    addButton(el).click();
    expect(last()).toHaveLength(1);
    expect(last()[0].target.nodeKey).toBe('Event_2');
    expect(last()[0].mode).toBe('MapOnly');
    expect(Object.keys(last()[0].inputs.properties!)).toEqual(['id', 'qty']);
    expect(last()[0]).not.toHaveProperty('unavailableTarget');
  });

  it('only offers callable events of the current tree: not itself and not non-callable nodes', async () => {
    const { el } = await mount([action()]);
    const options = Array.from(
      el.querySelectorAll<HTMLSelectElement>('[data-action] select')[0].options,
    ).map((o) => o.value);
    expect(options).toEqual(['def|ver|Event_2', 'def|ver|Event_3']);
  });

  it('switches between MapOnly and Run', async () => {
    const { el, last } = await mount([action()]);
    const mode = el.querySelectorAll<HTMLSelectElement>('[data-action] select')[1];
    change(mode, 'Run');
    expect(last()[0].mode).toBe('Run');
    change(mode, 'MapOnly');
    expect(last()[0].mode).toBe('MapOnly');
  });

  it('re-targeting rebuilds the input slots for the new target instead of keeping stale ones', async () => {
    const ctx = configure();
    ctx.catalog.update((c) => ({
      ...c!,
      targets: [
        target('Event_2'),
        target('Event_3', true, { type: 'object', properties: { code: { type: 'string' } } }),
      ],
    }));
    const fixture = TestBed.createComponent(MappingActionsStageComponent);
    const emitted: FollowUpAction[][] = [];
    fixture.componentInstance.actionsChange.subscribe((a) => emitted.push(a));
    fixture.componentRef.setInput('callable', false);
    fixture.componentRef.setInput('actions', [action()]);
    fixture.detectChanges();
    change(fixture.nativeElement.querySelector('[data-action] select'), 'def|ver|Event_3');
    expect(emitted[0][0].target.nodeKey).toBe('Event_3');
    expect(Object.keys(emitted[0][0].inputs.properties!)).toEqual(['code']);
  });

  it('an unavailable-target policy is Attention by default (absent), Wait with a deadline and outcome on request', async () => {
    const { el, last } = await mount([action()]);
    const policy = () =>
      Array.from(el.querySelectorAll<HTMLSelectElement>('[data-action] select')).find((s) =>
        Array.from(s.options).some((o) => o.value === 'Wait'),
      )!;
    expect(policy().value).toBe('Attention');
    change(policy(), 'Wait');
    expect(last()[0].unavailableTarget).toEqual({
      mode: 'Wait',
      deadlineSeconds: 3600,
      timeoutOutcome: 'timeout',
    });
    change(policy(), 'Attention');
    expect(last()[0]).not.toHaveProperty('unavailableTarget');
  });

  it('keeps an action whose target left the tree, marks it, and offers the current version of the same node', async () => {
    const stale = action({ target: { ...OWNER, workflowVersionId: 'old', nodeKey: 'Event_3' } });
    const { el, last } = await mount([stale]);
    expect(el.querySelector('[role="alert"]')!.textContent).toContain(
      'not a callable event of the current workflow tree',
    );
    const fix = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent!.includes('Use the current version'),
    )!;
    fix.click();
    expect(last()[0].target).toEqual({ ...OWNER, nodeKey: 'Event_3' });
  });

  it('makes this event callable with a declared inputs schema, and clears it when switched off', async () => {
    const on = await mount();
    const toggle = on.el.querySelector<HTMLInputElement>(
      '[data-testid="callable"] input[type="checkbox"]',
    )!;
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change', { bubbles: true }));
    expect(on.callable).toEqual([true]);
    expect(on.inputs[0]).toEqual(expect.objectContaining({ type: 'object' }));

    const off = await mount([], {
      callable: true,
      inputs: { type: 'object', properties: { id: { type: 'string' } } },
    });
    expect(
      off.el.querySelector('[data-testid="callable"] app-mapping-schema-editor'),
    ).not.toBeNull();
    const toggleOff = off.el.querySelector<HTMLInputElement>(
      '[data-testid="callable"] input[type="checkbox"]',
    )!;
    toggleOff.checked = false;
    toggleOff.dispatchEvent(new Event('change', { bubbles: true }));
    expect(off.callable).toEqual([false]);
    expect(off.inputs[0]).toBeNull();
  });

  it('explains why no action can be added when the tree has no other callable event', async () => {
    const ctx = configure();
    ctx.catalog.set({ sources: [], targets: [target('Event_1')], diagnostics: [] });
    const fixture = TestBed.createComponent(MappingActionsStageComponent);
    fixture.componentRef.setInput('callable', false);
    fixture.componentRef.setInput('actions', []);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No other callable event exists');
    expect(addButton(fixture.nativeElement).disabled).toBe(true);
  });

  it('is read-only for a published version', async () => {
    const { el } = await mount([action()], { readonly: true });
    expect(addButton(el)).toBeUndefined();
    expect(el.querySelector('button.mx-danger')).toBeNull();
    expect(el.querySelector<HTMLSelectElement>('[data-action] select')!.disabled).toBe(true);
  });
});

describe('Test stage', () => {
  const service = { preview: vi.fn(), sendTest: vi.fn() };
  const config = (): DesignerMappingConfiguration => ({
    ...emptyConfiguration(),
    outputs: [
      {
        id: 'out_1',
        name: 'customer',
        schema: newSchema('string'),
        value: sourceNode(field.reference),
      },
    ],
  });

  async function mount(httpConfiguration: (() => unknown) | null = null) {
    const ctx = configure([{ provide: WorkflowMappingService, useValue: service }]);
    const fixture = TestBed.createComponent(MappingTestStageComponent);
    const configuration = config();
    fixture.componentRef.setInput('configuration', configuration);
    fixture.componentRef.setInput('httpConfiguration', httpConfiguration);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const click = async (text: string) => {
      Array.from(el.querySelectorAll('button'))
        .find((b) => b.textContent!.includes(text))!
        .click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, ctx, el, configuration, click };
  }
  beforeEach(() => {
    service.preview.mockReset();
    service.sendTest.mockReset();
  });

  it('previews through the backend with typed samples, outside the saved configuration', async () => {
    service.preview.mockReturnValue(of({ errors: [], outputs: { customer: 'C-9' } }));
    const { el, click, configuration } = await mount();
    expect(el.querySelector('[data-sample="customer.id"]')).not.toBeNull();
    change(el.querySelector<HTMLTextAreaElement>('[data-sample="customer.id"] textarea')!, '"C-9"');
    await click('Preview (no network)');
    expect(service.preview).toHaveBeenCalledTimes(1);
    const [definitionId, versionId, body] = service.preview.mock.calls[0];
    expect([definitionId, versionId]).toEqual([ROOT.definitionId, ROOT.versionId]);
    expect(body.owner).toEqual(OWNER);
    expect(body.stage).toBe('Request');
    expect(body.samples).toEqual([
      { source: expect.objectContaining({ fieldKey: 'customer.id' }), value: 'C-9' },
    ]);
    expect(JSON.stringify(body.configuration)).not.toContain('C-9');
    expect(JSON.stringify(configuration)).not.toContain('C-9');
    expect(el.querySelector('[data-testid="preview-result"]')!.textContent).toContain(
      '"customer": "C-9"',
    );
  });

  it('keeps a number, boolean, null and list sample exact rather than turning it into text', async () => {
    service.preview.mockReturnValue(of({ errors: [] }));
    const { el, click } = await mount();
    for (const [text, value] of [
      ['12.5', 12.5],
      ['false', false],
      ['null', null],
      ['[1,2]', [1, 2]],
    ] as const) {
      change(el.querySelector<HTMLTextAreaElement>('[data-sample="customer.id"] textarea')!, text);
      await click('Preview (no network)');
      expect(service.preview.mock.calls.at(-1)![2].samples[0].value).toEqual(value);
    }
  });

  it('does not call the server while a sample is not valid JSON and says what to fix', async () => {
    const { el, click } = await mount();
    change(el.querySelector<HTMLTextAreaElement>('[data-sample="customer.id"] textarea')!, 'C-9');
    await click('Preview (no network)');
    expect(service.preview).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Not valid JSON');
    expect(el.textContent).toContain('Fix the sample values first.');
  });

  it('shows the server’s refusal of a preview', async () => {
    service.preview.mockReturnValue(
      throwError(() => ({ error: { message: 'Output "customer" is not typed.' } })),
    );
    const { el, click } = await mount();
    await click('Preview (no network)');
    expect(el.querySelector('p[role="alert"].mx-error')!.textContent).toContain(
      'Output "customer" is not typed.',
    );
  });

  it('offers no Send Test outside an integration editor that has a connection configuration', async () => {
    expect((await mount()).el.textContent).not.toContain('Send Test');
  });

  it('sends a real test only after a preview AND an explicit confirmation, with the prepared request alone', async () => {
    const prepared = { method: 'GET', path: '/orders/7', query: {}, headers: {}, body: null };
    service.preview.mockReturnValue(of({ errors: [], request: prepared }));
    service.sendTest.mockReturnValue(of({ success: true, statusCode: 200, body: '{"ok":true}' }));
    const http = { connectionId: 'conn-1' };
    const { el, click, fixture } = await mount(() => http);
    const send = () =>
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Send Test')!;
    expect(send().disabled).toBe(true);
    expect(el.textContent).toContain('Preview the request first.');

    await click('Preview (no network)');
    expect(send().disabled).toBe(true);

    const confirm = el.querySelector<HTMLInputElement>('section input[type="checkbox"]')!;
    confirm.checked = true;
    confirm.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(send().disabled).toBe(false);

    await click('Send Test');
    expect(service.sendTest).toHaveBeenCalledWith(http, prepared);
    expect(el.querySelector('[data-testid="send-result"]')!.textContent).toContain('HTTP 200');
    expect(service.preview).toHaveBeenCalledTimes(1);
  });

  it('forgets the confirmation when the request is previewed again, so a stale tick cannot send', async () => {
    service.preview.mockReturnValue(
      of({ errors: [], request: { method: 'GET', path: '/', query: {}, headers: {}, body: null } }),
    );
    const { el, click, fixture } = await mount(() => ({}));
    await click('Preview (no network)');
    const confirm = el.querySelector<HTMLInputElement>('section input[type="checkbox"]')!;
    confirm.checked = true;
    confirm.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    await click('Preview (no network)');
    const send = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent!.trim() === 'Send Test',
    )!;
    expect(send.disabled).toBe(true);
  });

  it('never starts follow-up actions: preview of completion labels them prepared, not dispatched', async () => {
    service.preview.mockReturnValue(
      of({ errors: [], outputs: {}, actions: [{ actionId: 'a', inputs: { id: 1 } }] }),
    );
    const { el, click } = await mount();
    change(el.querySelector<HTMLSelectElement>('select.wf-input')!, 'Completion');
    await click('Preview (no network)');
    expect(service.preview.mock.calls[0][2].stage).toBe('Completion');
    expect(el.querySelector('[data-testid="preview-result"]')!.textContent).toContain(
      'prepared, not dispatched',
    );
    expect(service.sendTest).not.toHaveBeenCalled();
  });

  it('needs no samples when the mapping reads no source', async () => {
    configure([{ provide: WorkflowMappingService, useValue: service }]);
    const fixture = TestBed.createComponent(MappingTestStageComponent);
    fixture.componentRef.setInput('configuration', {
      ...emptyConfiguration(),
      outputs: [{ id: 'o', name: 'n', schema: newSchema('string'), value: emptyNode() }],
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('does not read any source');
  });
});
