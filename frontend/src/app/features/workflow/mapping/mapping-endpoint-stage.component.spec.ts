import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import { MappingEditContext } from './mapping-edit-context';
import {
  MappingEndpointStageComponent,
  endpointProblems,
  pathPlaceholders,
} from './mapping-endpoint-stage.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import { emptyEndpoint } from './mapping-config';
import type { EndpointDefinition } from './mapping.models';

const endpoint = (over: Partial<EndpointDefinition> = {}): EndpointDefinition => ({
  ...emptyEndpoint(),
  method: 'GET',
  path: '/orders',
  ...over,
});

describe('Endpoint checks that need no server', () => {
  it('lists the placeholders of a path', () => {
    expect(pathPlaceholders('/orders/{orderId}/lines/{line}')).toEqual(['orderId', 'line']);
    expect(pathPlaceholders('/orders')).toEqual([]);
  });

  it('accepts a relative path and nothing declared', () => {
    expect(endpointProblems(endpoint())).toEqual([]);
    expect(endpointProblems(null)).toEqual([]);
  });

  it('refuses an absolute URL, a protocol-relative path and a path without a leading slash', () => {
    for (const path of ['https://evil.example/x', '//evil.example/x', 'orders'])
      expect(endpointProblems(endpoint({ path })), path).not.toEqual([]);
  });

  it('requires a declared path parameter for each placeholder and a placeholder for each path parameter', () => {
    expect(endpointProblems(endpoint({ path: '/orders/{id}' })).join(' ')).toContain(
      'no path parameter "id"',
    );
    const declared = {
      location: 'path' as const,
      name: 'id',
      schema: { type: 'string' as const, required: true },
    };
    expect(endpointProblems(endpoint({ path: '/orders/{id}', parameters: [declared] }))).toEqual(
      [],
    );
    expect(
      endpointProblems(endpoint({ path: '/orders', parameters: [declared] })).join(' '),
    ).toContain('does not appear in the path');
  });

  it('refuses a duplicate parameter (header names compare case-insensitively) and a body on GET', () => {
    const q = { location: 'query' as const, name: 'a', schema: { type: 'string' as const } };
    expect(endpointProblems(endpoint({ parameters: [q, q] })).join(' ')).toContain(
      'declared twice',
    );
    const h = (name: string) => ({
      location: 'header' as const,
      name,
      schema: { type: 'string' as const },
    });
    expect(endpointProblems(endpoint({ parameters: [h('X-Id'), h('x-id')] })).join(' ')).toContain(
      'declared twice',
    );
    expect(endpointProblems(endpoint({ body: { type: 'object' } })).join(' ')).toContain(
      'GET requests cannot send a body',
    );
  });
});

describe('Endpoint stage', () => {
  const service = { importOpenApi: vi.fn(), inferResponse: vi.fn(), validateSchema: vi.fn() };

  async function mount(
    value: EndpointDefinition = endpoint(),
    opts: { readonly?: boolean; serverProblems?: string[] } = {},
  ) {
    TestBed.configureTestingModule({
      providers: [
        MappingEditContext,
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowMappingService, useValue: service },
      ],
    });
    const ctx = TestBed.inject(MappingEditContext);
    ctx.root.set({ definitionId: 'def', versionId: 'ver' });
    ctx.readonly.set(opts.readonly ?? false);
    const fixture = TestBed.createComponent(MappingEndpointStageComponent);
    const emitted: EndpointDefinition[] = [];
    fixture.componentInstance.valueChange.subscribe((v) => {
      emitted.push(v);
      fixture.componentRef.setInput('value', v);
      fixture.detectChanges();
    });
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('serverProblems', opts.serverProblems ?? []);
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
    return { fixture, el, emitted, button, settle, last: () => emitted[emitted.length - 1] };
  }
  const type = (el: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement, value: string) => {
    el.value = value;
    el.dispatchEvent(
      new Event(el instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }),
    );
  };
  beforeEach(() => Object.values(service).forEach((fn) => fn.mockReset()));

  it('edits the method and path, and drops a body the new method cannot send', async () => {
    const { el, last } = await mount(endpoint({ method: 'POST', body: { type: 'object' } }));
    type(el.querySelector<HTMLSelectElement>('select.wf-input')!, 'GET');
    expect(last().method).toBe('GET');
    expect(last().body).toBeNull();
    type(el.querySelector<HTMLInputElement>('input[aria-invalid]')!, '/orders/{id}');
    expect(last().path).toBe('/orders/{id}');
  });

  it('shows what is wrong with the path immediately and marks the field invalid', async () => {
    const { el } = await mount(endpoint({ path: 'https://x.example' }));
    expect(el.querySelector('[data-testid="endpoint-problems"]')!.textContent).toContain(
      'must be relative to the connection',
    );
    expect(el.querySelector('input[aria-invalid="true"]')).not.toBeNull();
  });

  it('adds path parameters for the placeholders in the path, required, without duplicating declared ones', async () => {
    const { button, last, settle } = await mount(
      endpoint({
        path: '/orders/{orderId}/lines/{line}',
        parameters: [
          { location: 'path', name: 'line', schema: { type: 'integer', required: true } },
        ],
      }),
    );
    button('Add path parameters from the path').click();
    await settle();
    expect(last().parameters.map((p) => `${p.location}:${p.name}:${p.schema.required}`)).toEqual([
      'path:line:true',
      'path:orderId:true',
    ]);
  });

  it('adds query and header parameters with fresh names and typed schemas', async () => {
    const { button, last, settle } = await mount();
    button('Add query parameter').click();
    await settle();
    button('Add query parameter').click();
    await settle();
    button('Add header').click();
    expect(last().parameters.map((p) => `${p.location}:${p.name}`)).toEqual([
      'query:filter',
      'query:filter2',
      'header:X-Custom',
    ]);
    expect(last().parameters[0].schema.type).toBe('string');
  });

  it('turns a request body and an expected response on and off with typed object schemas', async () => {
    const { el, last, settle } = await mount(endpoint({ method: 'POST' }));
    const toggle = (text: string) => {
      const box = Array.from(el.querySelectorAll('label'))
        .find((l) => l.textContent!.includes(text))!
        .querySelector<HTMLInputElement>('input[type="checkbox"]')!;
      box.checked = !box.checked;
      box.dispatchEvent(new Event('change', { bubbles: true }));
    };
    toggle('sends a JSON body');
    await settle();
    expect(last().body).toEqual(expect.objectContaining({ type: 'object' }));
    expect(el.querySelector('[data-schema="body"]')).not.toBeNull();
    toggle('Describe the response');
    await settle();
    expect(last().response).toEqual(expect.objectContaining({ type: 'object' }));
    expect(el.querySelector('[data-schema="response"]')).not.toBeNull();
    toggle('sends a JSON body');
    expect(last().body).toBeNull();
  });

  it('imports an operation through the real API call and replaces the structure, announcing it', async () => {
    const imported = endpoint({
      method: 'GET',
      path: '/orders/{id}',
      parameters: [{ location: 'path', name: 'id', schema: { type: 'string', required: true } }],
      response: { type: 'object', properties: { id: { type: 'string' } } },
    });
    service.importOpenApi.mockReturnValue(of(imported));
    const { el, button, last, settle, fixture } = await mount();
    type(el.querySelector<HTMLTextAreaElement>('details textarea')!, '{"openapi":"3.0.0"}');
    const inputs = Array.from(el.querySelectorAll<HTMLInputElement>('details input.wf-input'));
    type(inputs[0], '/orders/{id}');
    await settle();
    button('Import operation').click();
    await settle();
    expect(service.importOpenApi).toHaveBeenCalledWith('def', 'ver', {
      document: '{"openapi":"3.0.0"}',
      path: '/orders/{id}',
      method: 'GET',
    });
    expect(last()).toEqual(imported);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('cannot import without a document and an operation path', async () => {
    const { button } = await mount();
    expect((button('Import operation') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the server’s reason when an import fails and keeps the current structure', async () => {
    service.importOpenApi.mockReturnValue(
      throwError(() => ({ error: { message: 'Only OpenAPI 3.0 JSON is supported.' } })),
    );
    const { el, button, emitted, settle } = await mount();
    type(el.querySelector<HTMLTextAreaElement>('details textarea')!, '{}');
    type(Array.from(el.querySelectorAll<HTMLInputElement>('details input.wf-input'))[0], '/x');
    await settle();
    button('Import operation').click();
    await settle();
    expect(el.querySelector('[data-testid="endpoint-problems"]')!.textContent).toContain(
      'Only OpenAPI 3.0 JSON is supported.',
    );
    expect(emitted).toEqual([]);
  });

  it('labels a response suggested from a sample as inferred and never claims it is verified', async () => {
    service.inferResponse.mockReturnValue(
      of({
        type: 'object',
        inferred: true,
        properties: { id: { type: 'string', inferred: true } },
      }),
    );
    const { el, button, settle } = await mount();
    const sample = Array.from(el.querySelectorAll<HTMLTextAreaElement>('details textarea')).at(-1)!;
    type(sample, '{"id":"A1"}');
    await settle();
    button('Suggest response structure').click();
    await settle();
    expect(service.inferResponse).toHaveBeenCalledWith('def', 'ver', { id: 'A1' });
    expect(el.textContent).toContain('Inferred from a sample — review it');
    expect(el.textContent).toContain('cannot tell which fields are required');
  });

  it('refuses a sample that is not JSON without calling the server', async () => {
    const { el, button, settle } = await mount();
    type(
      Array.from(el.querySelectorAll<HTMLTextAreaElement>('details textarea')).at(-1)!,
      'not json',
    );
    await settle();
    button('Suggest response structure').click();
    await settle();
    expect(service.inferResponse).not.toHaveBeenCalled();
    expect(el.querySelector('[data-testid="endpoint-problems"]')!.textContent).toContain(
      'not valid JSON',
    );
  });

  it('asks the server to check the endpoint, and forgets the acceptance as soon as it is edited', async () => {
    service.validateSchema.mockReturnValue(of({}));
    const { el, button, settle } = await mount();
    button('Check endpoint with the server').click();
    await settle();
    expect(service.validateSchema).toHaveBeenCalledWith(
      'def',
      'ver',
      expect.objectContaining({ path: '/orders' }),
    );
    expect(el.querySelector('[role="status"]')!.textContent).toContain(
      'The server accepted this endpoint.',
    );
    type(el.querySelector<HTMLInputElement>('input[aria-invalid]')!, '/orders2');
    await settle();
    expect(el.querySelector('[role="status"]')).toBeNull();
  });

  it('shows the problems the server reported for this stage', async () => {
    const { el } = await mount(endpoint(), {
      serverProblems: ['Header "Authorization" is not permitted.'],
    });
    expect(el.querySelector('[data-testid="endpoint-problems"]')!.textContent).toContain(
      'Authorization',
    );
  });

  it('is read-only for a published version, yet still inspectable', async () => {
    const { el } = await mount(
      endpoint({
        path: '/orders/{id}',
        parameters: [{ location: 'path', name: 'id', schema: { type: 'string', required: true } }],
      }),
      { readonly: true },
    );
    expect(el.querySelector('fieldset')!.disabled).toBe(true);
    expect(el.querySelector('[data-parameter="path:id"]')).not.toBeNull();
  });
});
