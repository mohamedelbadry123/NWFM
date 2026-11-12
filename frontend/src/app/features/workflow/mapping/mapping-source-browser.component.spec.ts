import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext, type MappingTarget } from './mapping-edit-context';
import { MappingSourceBrowserComponent } from './mapping-source-browser.component';
import { emptyNode } from './mapping-config';
import type { MappingCatalog, SourceDescriptor, TransformNode } from './mapping.models';

const source = (
  fieldKey: string,
  label: string,
  crumbs: string[],
  over: Partial<SourceDescriptor> = {},
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
    formVersion: 1,
  },
  label,
  breadcrumbs: [...crumbs, label],
  schema: { type: 'string' },
  availability: 'SubmittedForm',
  ...over,
});

const CUSTOMER = source('customer.id', 'Customer id', ['Order', 'Review', 'Step', 'Form']);
const NOTES = source('notes', 'Notes', ['Order', 'Review', 'Step', 'Form'], {
  schema: { type: 'string', required: true },
});
const OUTPUT = source('status', 'Status', ['Order', 'Check credit'], {
  reference: {
    ...CUSTOMER.reference,
    nodeKey: 'Event_1',
    kind: 'EventOutput',
    fieldKey: 'status',
    formStepId: null,
    formId: null,
    formVersion: null,
  },
  availability: 'CompletedOutput',
});
const UNTYPED = source('blob', 'Blob', ['Order', 'Check credit'], {
  availability: 'SchemaRequired',
});
const SECRET = source('ssn', 'National id', ['Order', 'Review', 'Step', 'Form'], {
  sensitive: true,
});
const CATALOG: MappingCatalog = {
  sources: [CUSTOMER, NOTES, OUTPUT, UNTYPED, SECRET],
  targets: [],
  diagnostics: ['Form “Intake” has no published version.'],
};

describe('Source browser', () => {
  async function mount(opts: { readonly?: boolean; catalog?: MappingCatalog | null } = {}) {
    TestBed.configureTestingModule({
      providers: [
        MappingEditContext,
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
      ],
    });
    const ctx = TestBed.inject(MappingEditContext);
    ctx.readonly.set(opts.readonly ?? false);
    ctx.catalog.set(opts.catalog === undefined ? CATALOG : opts.catalog);
    const fixture = TestBed.createComponent(MappingSourceBrowserComponent);
    const refreshed = vi.fn();
    fixture.componentInstance.refresh.subscribe(refreshed);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const leaf = (fieldKey: string) => el.querySelector<HTMLElement>(`[data-source="${fieldKey}"]`);
    const groupButton = (text: string) =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('button.mx-link')).find((b) =>
        b.textContent!.includes(text),
      );
    return { fixture, ctx, el, settle, leaf, groupButton, refreshed };
  }

  it('shows the hierarchy workflow › Task Type › Step › Form, opening the first level and keeping deeper levels closed', async () => {
    const { el, groupButton, leaf, settle } = await mount();
    expect(groupButton('Order')!.getAttribute('aria-expanded')).toBe('true');
    expect(groupButton('Review')!.getAttribute('aria-expanded')).toBe('false');
    expect(leaf('customer.id')).toBeNull();

    groupButton('Review')!.click();
    await settle();
    groupButton('Step')!.click();
    await settle();
    groupButton('Form')!.click();
    await settle();
    expect(leaf('customer.id')).not.toBeNull();
    expect(leaf('notes')!.textContent).toContain('Required');
    expect(el.querySelector('[data-testid="source-tree"]')!.textContent).toContain('Order');
  });

  it('shows availability and wait information, sensitive flags and the server’s diagnostics', async () => {
    const { el, groupButton, leaf, settle } = await mount();
    groupButton('Check credit')!.click();
    await settle();
    expect(leaf('status')!.textContent).toContain(
      'Available once that event completes (may need to wait)',
    );
    expect(leaf('blob')!.textContent).toContain('Needs a declared type before it can be mapped');
    expect(el.querySelector('[role="status"].mx-warn')!.textContent).toContain(
      'has no published version',
    );
    for (const name of ['Review', 'Step', 'Form']) {
      groupButton(name)!.click();
      await settle();
    }
    expect(leaf('ssn')!.textContent).toContain('Sensitive');
    expect(leaf('customer.id')!.textContent).toContain('Available once the form is submitted');
  });

  it('cannot gather or map a source that has no declared type', async () => {
    const { ctx, groupButton, leaf, settle } = await mount();
    groupButton('Check credit')!.click();
    await settle();
    expect(leaf('blob')!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(
      true,
    );
    expect(
      leaf('status')!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled,
    ).toBe(false);

    ctx.setActive(target());
    await settle();
    expect(leaf('blob')!.querySelector<HTMLButtonElement>('button')!.disabled).toBe(true);
  });

  it('searches by label and breadcrumb and opens the matching groups by itself', async () => {
    const { el, fixture, leaf, settle } = await mount();
    const search = el.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'customer';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(leaf('customer.id')).not.toBeNull();
    expect(leaf('notes')).toBeNull();

    search.value = 'no such source';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(el.querySelector('[data-testid="source-tree"]')!.textContent).toContain(
      'No sources match',
    );
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('gathers several sources in the tray with the keyboard-reachable checkboxes, and removes or clears them', async () => {
    const { ctx, el, groupButton, leaf, settle } = await mount();
    for (const name of ['Review', 'Step', 'Form']) {
      groupButton(name)!.click();
      await settle();
    }
    const tick = (fieldKey: string) => {
      const box = leaf(fieldKey)!.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
      box.click();
    };
    tick('customer.id');
    tick('notes');
    await settle();
    expect(ctx.tray().map((d) => d.reference.fieldKey)).toEqual(['customer.id', 'notes']);
    expect(el.querySelector('[data-testid="tray"]')!.textContent).toContain('Customer id');
    expect(leaf('notes')!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(
      true,
    );
    expect(el.querySelector('[aria-live="polite"]')!.textContent).toContain('added to the tray');

    el.querySelector<HTMLButtonElement>('[data-testid="tray"] [role="listitem"] button')!.click();
    await settle();
    expect(ctx.tray().map((d) => d.reference.fieldKey)).toEqual(['notes']);

    const clear = Array.from(
      el.querySelectorAll<HTMLButtonElement>('[data-testid="tray"] button'),
    ).find((b) => b.textContent!.includes('Clear tray'))!;
    clear.click();
    await settle();
    expect(ctx.tray()).toEqual([]);
    expect(el.querySelector('[data-testid="tray"]')!.textContent).toContain('The tray is empty.');
  });

  const applied: TransformNode[] = [];
  const target = (occupied = false): MappingTarget => ({
    id: 't1',
    label: 'Query “customer”',
    apply: (n) => applied.push(n),
    occupied: () => occupied,
  });

  it('maps a source to the chosen target with a click, writing exact pins, and says so', async () => {
    applied.length = 0;
    const { ctx, el, groupButton, leaf, settle } = await mount();
    ctx.setActive(target());
    for (const name of ['Review', 'Step', 'Form']) {
      groupButton(name)!.click();
      await settle();
    }
    expect(el.textContent).toContain('Map buttons write to:');
    leaf('customer.id')!.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(applied).toHaveLength(1);
    expect(applied[0]).toEqual(
      expect.objectContaining({ op: 'source', source: CUSTOMER.reference }),
    );
    expect(el.querySelector('[aria-live="polite"]')!.textContent).toContain(
      'Customer id mapped to Query “customer”',
    );

    groupButton('Clear target')!.click();
    await settle();
    expect(leaf('customer.id')!.querySelector('button')).toBeNull();
  });

  it('refuses to replace a target that already holds an expression and says why', async () => {
    applied.length = 0;
    const { ctx, el, groupButton, leaf, settle } = await mount();
    ctx.setActive(target(true));
    for (const name of ['Review', 'Step', 'Form']) {
      groupButton(name)!.click();
      await settle();
    }
    leaf('notes')!.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(applied).toEqual([]);
    expect(el.querySelector('[aria-live="polite"]')!.textContent).toContain(
      'already has an expression',
    );
    expect(emptyNode().op).toBe('literal');
  });

  it('is read-only for a published version: nothing can be gathered or mapped', async () => {
    const { ctx, groupButton, leaf, settle } = await mount({ readonly: true });
    ctx.setActive(target());
    for (const name of ['Review', 'Step', 'Form']) {
      groupButton(name)!.click();
      await settle();
    }
    expect(
      leaf('customer.id')!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled,
    ).toBe(true);
    expect(leaf('customer.id')!.querySelector<HTMLButtonElement>('button')!.disabled).toBe(true);
    expect(ctx.mapToActive(CUSTOMER)).toBe(false);
  });

  it('shows the catalog failure, asks the owner to refresh, and never invents sources', async () => {
    const { ctx, el, settle, refreshed } = await mount({ catalog: null });
    ctx.catalogError.set('The catalog could not be read.');
    await settle();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('could not be read');
    expect(el.querySelector('[data-testid="source-tree"]')!.textContent).toContain(
      'No sources match',
    );
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Refresh sources'))!
      .click();
    expect(refreshed).toHaveBeenCalledTimes(1);
  });

  it('lists the event’s own response as a source once an endpoint declares one', async () => {
    const { ctx, el, groupButton, settle } = await mount();
    ctx.owner.set({ workflowDefinitionId: 'def', workflowVersionId: 'ver', nodeKey: 'Event_1' });
    ctx.endpoint.set({
      method: 'GET',
      path: '/orders',
      parameters: [],
      body: null,
      response: { type: 'object', properties: { id: { type: 'string' } } },
    });
    await settle();
    expect(groupButton('This event')).toBeDefined();
    expect(el.textContent).toContain('This event');
  });
});
