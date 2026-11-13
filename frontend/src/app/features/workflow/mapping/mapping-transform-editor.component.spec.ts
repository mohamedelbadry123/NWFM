import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext } from './mapping-edit-context';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import { emptyNode, itemNode, literalNode, sourceNode } from './mapping-config';
import type { MappingSchema, SourceDescriptor, TransformNode, TransformOp } from './mapping.models';

const LIST_ITEM: MappingSchema = {
  type: 'object',
  properties: { name: { type: 'string' }, qty: { type: 'number' } },
};

const descriptor: SourceDescriptor = {
  reference: {
    workflowDefinitionId: 'd',
    workflowVersionId: 'v',
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

@Component({
  standalone: true,
  imports: [MappingTransformEditorComponent],
  providers: [MappingEditContext],
  template: `<app-mapping-transform-editor
    [node]="node()"
    label="Value"
    [itemSchema]="itemSchema()"
    (nodeChange)="node.set($event); changes.push($event)"
  />`,
})
class HostComponent {
  readonly node = signal<TransformNode>(literalNode(''));
  readonly itemSchema = signal<MappingSchema | null>(null);
  readonly changes: TransformNode[] = [];
}

const ALL_OPS: TransformOp[] = [
  'source',
  'literal',
  'object',
  'concat',
  'add',
  'subtract',
  'multiply',
  'divide',
  'text',
  'number',
  'integer',
  'boolean',
  'coalesce',
  'map',
  'filter',
  'count',
  'sum',
  'equal',
  'greater',
  'item',
];

describe('Transform editor', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } }],
    }),
  );

  function mount(
    node: TransformNode = literalNode(''),
    opts: { readonly?: boolean; list?: boolean } = {},
  ) {
    const fixture = TestBed.createComponent(HostComponent);
    const ctx = fixture.debugElement.injector.get(MappingEditContext);
    ctx.readonly.set(opts.readonly ?? false);
    fixture.componentInstance.node.set(node);
    if (opts.list) fixture.componentInstance.itemSchema.set(LIST_ITEM);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const slot = (path: string) => el.querySelector<HTMLElement>(`[data-slot="${path}"]`)!;
    const opSelect = (path = 'slot') =>
      slot(path).querySelector<HTMLSelectElement>('select.wf-input')!;
    const choose = (path: string, op: string) => {
      const select = opSelect(path);
      select.value = op;
      select.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
    };
    return { fixture, ctx, el, host: fixture.componentInstance, slot, opSelect, choose };
  }

  it('offers all 20 operations to pick from, "Each item" only inside a list', () => {
    const outside = mount().opSelect();
    expect(Array.from(outside.options).map((o) => o.value)).toEqual(
      ALL_OPS.filter((o) => o !== 'item'),
    );
    const inside = mount(literalNode(''), { list: true }).opSelect();
    expect(
      Array.from(inside.options)
        .map((o) => o.value)
        .sort(),
    ).toEqual([...ALL_OPS].sort());
  });

  it('switches to every operation, shows its slots as nested editors and never falls back to a JSON box', () => {
    const withArgs = [
      'concat',
      'add',
      'subtract',
      'multiply',
      'divide',
      'text',
      'number',
      'integer',
      'boolean',
      'coalesce',
      'map',
      'filter',
      'count',
      'sum',
      'equal',
      'greater',
    ];
    for (const op of withArgs) {
      const { el, choose, host } = mount(literalNode(''), { list: true });
      choose('slot', op);
      expect(host.node().op, op).toBe(op);
      expect(el.querySelectorAll('[data-slot]').length, op).toBeGreaterThan(1);
      expect(el.querySelector('textarea'), op).toBeNull();
    }
    const object = mount(literalNode(''));
    object.choose('slot', 'object');
    expect(object.host.node().op).toBe('object');
    expect(object.el.querySelector('textarea')).toBeNull();
    const item = mount(literalNode(''), { list: true });
    item.choose('slot', 'item');
    expect(item.host.node().op).toBe('item');
    expect(item.el.textContent).toContain('Field of the current item');
  });

  it('keeps the current value inside the new step instead of discarding it', () => {
    const { host, choose } = mount(literalNode('abc'));
    choose('slot', 'text');
    expect(host.node().op).toBe('text');
    expect(host.node().args![0]).toEqual(expect.objectContaining({ op: 'literal', value: 'abc' }));
  });

  it('nests deeper: change a slot inside a step and the parent document carries the whole tree', () => {
    const { host, choose, slot } = mount(literalNode(''));
    choose('slot', 'add');
    choose('slot.0', 'multiply');
    expect(slot('slot.0.0')).not.toBeNull();
    expect(host.node().op).toBe('add');
    expect(host.node().args![0].op).toBe('multiply');
    expect(host.node().args![0].args).toHaveLength(2);
  });

  it('picks a source from the tray, replacing the empty step with exact pins', () => {
    const { ctx, fixture, host, choose } = mount(literalNode(''));
    ctx.catalog.set({ sources: [descriptor], targets: [], diagnostics: [] });
    ctx.tray.set([descriptor]);
    choose('slot', 'source');
    expect(host.node().op).toBe('source');
    expect(host.node().source).toEqual(descriptor.reference);
    expect(fixture.nativeElement.textContent).toContain('Customer id');
  });

  it('asks for a source when the tray is empty, without changing the node', () => {
    const { el, host, choose } = mount(literalNode('keep'));
    choose('slot', 'source');
    expect(host.node().op).toBe('literal');
    expect(el.textContent).toContain('The tray is empty');
  });

  it('flags a source that the catalog no longer offers but keeps its pins', () => {
    const { el, host } = mount(sourceNode(descriptor.reference));
    expect(el.querySelector('.mx-source-broken')).not.toBeNull();
    expect(host.node().source).toEqual(descriptor.reference);
  });

  it('builds an object: add, rename and remove a field without losing the others', () => {
    const { fixture, el, host, choose } = mount(literalNode(''));
    choose('slot', 'object');
    const add = () =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('button'))
        .find((b) => b.textContent!.trim() === 'Add field')!
        .click();
    add();
    fixture.detectChanges();
    add();
    fixture.detectChanges();
    expect(Object.keys(host.node().properties!)).toEqual(['field', 'field_2']);

    const nameInput = el.querySelector<HTMLInputElement>('input[aria-invalid]')!;
    nameInput.value = 'total';
    nameInput.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(Object.keys(host.node().properties!)).toEqual(['total', 'field_2']);

    // A duplicate name is refused rather than silently merging two fields.
    const second = el.querySelectorAll<HTMLInputElement>('input[aria-invalid]')[1];
    second.value = 'total';
    second.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    expect(Object.keys(host.node().properties!)).toEqual(['total', 'field_2']);

    el.querySelector<HTMLButtonElement>('button.mx-danger')!.click();
    fixture.detectChanges();
    expect(Object.keys(host.node().properties!)).toEqual(['field_2']);
  });

  it('shows the JSON box only for an advanced list/object constant, and says when it is not valid JSON', () => {
    const { fixture, el } = mount(literalNode('x'));
    expect(el.querySelector('textarea')).toBeNull();
    const kind = el.querySelectorAll<HTMLSelectElement>('select.wf-input')[1];
    kind.value = 'json';
    kind.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const box = el.querySelector<HTMLTextAreaElement>('textarea')!;
    expect(box).not.toBeNull();
    box.value = '{ nope';
    box.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(el.textContent).toContain('That is not valid JSON.');
  });

  it('keeps a literal 0, false and null as exact typed constants', () => {
    const { fixture, el, host } = mount(literalNode(''));
    const kind = () => el.querySelectorAll<HTMLSelectElement>('select.wf-input')[1];
    for (const [k, expected] of [
      ['number', 0],
      ['boolean', false],
      ['null', null],
    ] as const) {
      kind().value = k;
      kind().dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      expect(host.node().value, k).toBe(expected);
    }
  });

  it('gives a missing source Attention by default, and Wait / Fallback a complete policy', async () => {
    const { ctx, fixture, el, host } = mount(sourceNode(descriptor.reference));
    ctx.catalog.set({ sources: [descriptor], targets: [], diagnostics: [] });
    fixture.detectChanges();
    await fixture.whenStable();
    const missing = () =>
      Array.from(el.querySelectorAll<HTMLSelectElement>('select.wf-input')).find((s) =>
        Array.from(s.options).some((o) => o.value === 'Fallback'),
      )!;
    expect(missing().value).toBe('Attention');
    for (const [mode, expected] of [
      ['Wait', { mode: 'Wait', deadlineSeconds: 3600, timeoutOutcome: 'timeout' }],
      ['Fallback', { mode: 'Fallback', fallback: '' }],
    ] as const) {
      missing().value = mode;
      missing().dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      expect(host.node().missing).toEqual(expected);
    }
    missing().value = 'Attention';
    missing().dispatchEvent(new Event('change', { bubbles: true }));
    expect(host.node().missing).toBeUndefined();
  });

  it('explains a step with the wrong number of values', () => {
    const { el } = mount({ op: 'add', args: [emptyNode()] });
    expect(el.querySelector('ul[role="alert"]')!.textContent).toContain('needs 2');
  });

  it('is read-only for a published version: every control is disabled and nothing is emitted', async () => {
    const { fixture, el, host } = mount(
      { op: 'object', properties: { a: literalNode('x') } },
      { readonly: true },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(
      Array.from(el.querySelectorAll<HTMLSelectElement | HTMLInputElement>('select, input')).every(
        (c) => c.disabled,
      ),
    ).toBe(true);
    expect(
      Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim()),
    ).not.toContain('Add field');
    expect(host.changes).toEqual([]);
  });

  describe('a field of the current item', () => {
    /** Declared `id` and dotted `a.b`, a dictionary of numbers beside them, and a nested list. */
    const ITEM: MappingSchema = {
      type: 'object',
      properties: {
        id: { type: 'string' },
        'a.b': { type: 'string' },
        scores: { type: 'array', items: { type: 'number' } },
      },
      additionalPropertySchema: { type: 'number' },
    };

    async function mountItem(node: TransformNode, itemSchema: MappingSchema | null = ITEM) {
      const opened = mount(node);
      opened.host.itemSchema.set(itemSchema);
      opened.fixture.detectChanges();
      await opened.fixture.whenStable();
      opened.fixture.detectChanges();
      const pointerBox = () =>
        opened.el.querySelector<HTMLInputElement>('input[data-testid="item-pointer"]')!;
      const picker = () =>
        Array.from(opened.el.querySelectorAll<HTMLSelectElement>('select.wf-input')).find((s) =>
          Array.from(s.options).some((o) => o.value === '/id'),
        );
      const type = async (text: string) => {
        const box = pointerBox();
        box.value = text;
        box.dispatchEvent(new Event('change', { bubbles: true }));
        opened.fixture.detectChanges();
        await opened.fixture.whenStable();
        opened.fixture.detectChanges();
      };
      const pointer = () => opened.host.node().jsonPointer;
      return { ...opened, pointerBox, picker, type, pointer };
    }

    it('keeps the exact-pointer entry beside the picker of known properties', async () => {
      const { pointerBox, picker } = await mountItem(itemNode('/id'));
      expect(picker()).toBeDefined();
      expect(Array.from(picker()!.options).map((o) => o.value)).toEqual(
        expect.arrayContaining(['', '/id', '/a.b', '/scores']),
      );
      expect(pointerBox()).not.toBeNull();
      expect(pointerBox().value).toBe('/id');
    });

    it.each([
      ['a dictionary key the schema does not list', '/score'],
      ['an element of a nested list', '/scores/0'],
      ['a key whose name contains a slash, escaped', '/a~1b'],
      ['a key whose name contains a tilde, escaped', '/x~0y'],
      ['a literal dotted name', '/a.b'],
      ['a deeper path below a dictionary key', '/score/0/inner'],
    ])('accepts %s (%s) and emits it verbatim', async (_what, text) => {
      const { type, pointer, host } = await mountItem(itemNode(''));
      await type(text);
      expect(pointer()).toBe(text);
      expect(host.changes.at(-1)).toEqual({ op: 'item', jsonPointer: text });
    });

    it('keeps a trailing space in a dictionary key: `/score ` is a different key from `/score`', async () => {
      const { type, pointer, pointerBox, host } = await mountItem(itemNode(''));
      await type('/score ');
      expect(pointer()).toBe('/score ');
      expect(host.changes.at(-1)).toEqual({ op: 'item', jsonPointer: '/score ' });
      expect(pointerBox().value).toBe('/score ');
      expect(pointerBox().getAttribute('aria-invalid')).toBe('false');
      // …and it is distinct from the trimmed key, which is still a separate valid pointer.
      await type('/score');
      expect(pointer()).toBe('/score');
    });

    it('does not trim into a different meaning: surrounding spaces that make it invalid stay visible', async () => {
      const { type, pointer, pointerBox } = await mountItem(itemNode('/id'));
      for (const text of [' /score', ' ']) {
        await type(text);
        expect(pointer()).toBe('/id');
        expect(pointerBox().value).toBe(text);
        expect(pointerBox().getAttribute('aria-invalid')).toBe('true');
      }
    });

    it('shows a pointer the picker cannot list as a selectable custom path, next to the known ones', async () => {
      const { picker, type, el } = await mountItem(itemNode(''));
      await type('/score');
      const options = Array.from(picker()!.options);
      expect(options.map((o) => o.value)).toEqual(
        expect.arrayContaining(['/score', '/id', '/scores']),
      );
      expect(options.find((o) => o.value === '/score')!.textContent).toContain('Custom path');
      expect(el.querySelector('[role="alert"]')).toBeNull();
    });

    it('does not offer a custom path for a pointer the picker already lists', async () => {
      const { picker } = await mountItem(itemNode('/id'));
      expect(
        Array.from(picker()!.options).some((o) => (o.textContent ?? '').includes('Custom path')),
      ).toBe(false);
    });

    it('lets the author go back to a known property, and to the whole item', async () => {
      const { fixture, picker, type, pointer, pointerBox } = await mountItem(itemNode(''));
      await type('/score');
      picker()!.value = '/id';
      picker()!.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(pointer()).toBe('/id');
      expect(pointerBox().value).toBe('/id');
      await type('');
      expect(pointer()).toBe('');
    });

    it('says so and keeps what was typed when it is not a pointer, without changing the mapping', async () => {
      const { type, pointer, pointerBox, el, host, fixture } = await mountItem(itemNode('/id'));
      for (const bad of ['score', '/a~2b', '/x~']) {
        await type(bad);
        expect(pointer()).toBe('/id');
        expect(host.changes).toEqual([]);
        expect(pointerBox().value).toBe(bad);
        expect(pointerBox().getAttribute('aria-invalid')).toBe('true');
        expect(el.querySelector('[role="alert"]')!.textContent).toContain('starts with');
      }
      await type('/score');
      fixture.detectChanges();
      expect(pointer()).toBe('/score');
      expect(el.querySelector('[role="alert"]')).toBeNull();
      expect(pointerBox().getAttribute('aria-invalid')).toBe('false');
    });

    it('is the only entry when the item has no declared properties', async () => {
      const { picker, pointerBox, type, pointer, el } = await mountItem(itemNode(''), {
        type: 'string',
      });
      expect(picker()).toBeUndefined();
      expect(el.textContent).toContain('Field of the current item');
      await type('/anything');
      expect(pointer()).toBe('/anything');
      expect(pointerBox().disabled).toBe(false);
    });

    it('is read-only for a published version: both entries are disabled', async () => {
      const opened = mount(itemNode('/score'), { readonly: true });
      opened.host.itemSchema.set(ITEM);
      opened.fixture.detectChanges();
      await opened.fixture.whenStable();
      opened.fixture.detectChanges();
      const controls = Array.from(
        opened.el.querySelectorAll<HTMLSelectElement | HTMLInputElement>(
          'input[data-testid="item-pointer"], select.wf-input',
        ),
      );
      expect(controls.length).toBeGreaterThan(1);
      expect(controls.every((c) => c.disabled)).toBe(true);
      expect(opened.host.changes).toEqual([]);
    });
  });
});
