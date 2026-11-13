import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext } from './mapping-edit-context';
import { MappingInputsStageComponent, inputDestinations } from './mapping-inputs-stage.component';
import { emptyEndpoint, literalNode } from './mapping-config';
import type { EndpointDefinition, InputBinding } from './mapping.models';

const endpoint = (over: Partial<EndpointDefinition> = {}): EndpointDefinition => ({
  ...emptyEndpoint(),
  method: 'POST',
  path: '/orders/{id}',
  parameters: [
    { location: 'path', name: 'id', schema: { type: 'string', required: true } },
    { location: 'query', name: 'dryRun', schema: { type: 'boolean' } },
    { location: 'header', name: 'X-Trace', schema: { type: 'string' } },
  ],
  body: { type: 'object', properties: { total: { type: 'number', required: true } } },
  ...over,
});

describe('Input destinations', () => {
  it('lists every declared parameter and then the body', () => {
    expect(inputDestinations(endpoint()).map((d) => d.key)).toEqual([
      'path:id',
      'query:dryRun',
      'header:X-Trace',
      'body:$body',
    ]);
    expect(inputDestinations(null)).toEqual([]);
    expect(inputDestinations(endpoint({ parameters: [], body: null }))).toEqual([]);
  });
});

describe('Inputs stage', () => {
  async function mount(
    ep: EndpointDefinition | null,
    inputs: InputBinding[] = [],
    opts: { readonly?: boolean; problems?: string[] } = {},
  ) {
    TestBed.configureTestingModule({
      providers: [
        MappingEditContext,
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
      ],
    });
    TestBed.inject(MappingEditContext).readonly.set(opts.readonly ?? false);
    const fixture = TestBed.createComponent(MappingInputsStageComponent);
    const emitted: InputBinding[][] = [];
    fixture.componentInstance.inputsChange.subscribe((v) => {
      emitted.push(v);
      fixture.componentRef.setInput('inputs', v);
      fixture.detectChanges();
    });
    fixture.componentRef.setInput('endpoint', ep);
    fixture.componentRef.setInput('inputs', inputs);
    fixture.componentRef.setInput('serverProblems', opts.problems ?? []);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const section = (key: string) => el.querySelector<HTMLElement>(`[data-input="${key}"]`)!;
    const press = (key: string, text: string) =>
      Array.from(section(key).querySelectorAll('button'))
        .find((b) => b.textContent!.includes(text))!
        .click();
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return {
      fixture,
      el,
      emitted,
      section,
      press,
      settle,
      last: () => emitted[emitted.length - 1],
    };
  }

  it('asks for the endpoint stage first when nothing is declared', async () => {
    const { el } = await mount(null);
    expect(el.textContent).toContain('Declare parameters or a body in the Endpoint stage first');
    expect(el.querySelector('[data-input]')).toBeNull();
  });

  it('shows each destination with its location, type and whether it is required', async () => {
    const { section } = await mount(endpoint());
    expect(section('path:id').textContent).toContain('Path · id');
    expect(section('path:id').textContent).toContain('Required');
    expect(section('path:id').textContent).toContain('Required and not mapped yet.');
    expect(section('query:dryRun').textContent).toContain('Not sent.');
    expect(section('body:$body').textContent).toContain('request body');
  });

  it('maps a destination with an empty constant, an object body with a skeleton, and sends nothing else', async () => {
    const { press, last, settle, section } = await mount(endpoint());
    press('query:dryRun', 'Map this input');
    await settle();
    expect(last()).toEqual([
      { location: 'query', name: 'dryRun', value: expect.objectContaining({ op: 'literal' }) },
    ]);
    press('body:$body', 'Map this input');
    await settle();
    expect(last().map((b) => `${b.location}:${b.name}`)).toEqual(['query:dryRun', 'body:$body']);
    expect(last()[1].value.op).toBe('object');
    expect(section('query:dryRun').querySelector('app-mapping-transform-editor')).not.toBeNull();
    expect(section('path:id').querySelector('app-mapping-transform-editor')).toBeNull();
  });

  it('edits a mapped value in place without touching the other bindings', async () => {
    const existing: InputBinding[] = [
      { location: 'path', name: 'id', value: literalNode('A1') },
      { location: 'query', name: 'dryRun', value: literalNode(true) },
    ];
    const { section, last, settle } = await mount(endpoint(), existing);
    const select = section('path:id').querySelector<HTMLSelectElement>('select.wf-input')!;
    select.value = 'text';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
    expect(last()[0].value.op).toBe('text');
    expect(last()[1]).toEqual(existing[1]);
  });

  it('lets an optional input stop being sent, but never a required one', async () => {
    const { section, press, last, settle } = await mount(endpoint(), [
      { location: 'path', name: 'id', value: literalNode('A1') },
      { location: 'query', name: 'dryRun', value: literalNode(true) },
    ]);
    expect(
      Array.from(section('path:id').querySelectorAll('button')).some((b) =>
        b.textContent!.includes('Stop sending'),
      ),
    ).toBe(false);
    press('query:dryRun', 'Stop sending this input');
    await settle();
    expect(last().map((b) => b.name)).toEqual(['id']);
  });

  it('shows what the server reported for this stage', async () => {
    const { el } = await mount(endpoint(), [], { problems: ['Input "id" is required.'] });
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('Input "id" is required.');
  });

  it('is read-only for a published version: no mapping or removal controls, values stay visible', async () => {
    const { el, section, emitted } = await mount(
      endpoint(),
      [{ location: 'query', name: 'dryRun', value: literalNode(true) }],
      { readonly: true },
    );
    expect(
      Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim()),
    ).not.toContain('Map this input');
    expect(
      Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim()),
    ).not.toContain('Stop sending this input');
    expect(section('query:dryRun').querySelector('app-mapping-transform-editor')).not.toBeNull();
    expect(
      Array.from(
        section('query:dryRun').querySelectorAll<HTMLSelectElement | HTMLInputElement>(
          'select, input',
        ),
      ).every((c) => c.disabled),
    ).toBe(true);
    expect(emitted).toEqual([]);
  });
});
