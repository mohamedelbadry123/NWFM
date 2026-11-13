import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowIntegrationsService } from '../integrations/workflow-integrations.service';
import {
  WorkflowMappingInvocationsComponent,
  canReplayInvocation,
  canRetryInvocation,
  isIndependentInvocation,
} from './workflow-mapping-invocations.component';
import { WorkflowMappingService } from './workflow-mapping.service';
import type { MappingInvocationDto } from './mapping.models';

const invocation = (over: Partial<MappingInvocationDto> = {}): MappingInvocationDto => ({
  id: 'inv-1',
  rootInstanceId: 'root',
  originActivityId: 'act',
  targetInstanceId: 'target',
  targetVersionId: 'v',
  targetNodeKey: 'Event_1',
  executionKind: 'RequiredEvent',
  actionId: '',
  status: 'Completed',
  windowAnchor: '2026-01-01T00:00:00Z',
  prepared: true,
  delivered: true,
  applied: true,
  graphAdvanced: false,
  ...over,
});

describe('receipt recovery rules', () => {
  it('offers Retry only for an Attention receipt that has no delivery job', () => {
    expect(canRetryInvocation(invocation({ status: 'Attention' }), true)).toBe(true);
    expect(canRetryInvocation(invocation({ status: 'Attention', operationId: 'op' }), true)).toBe(
      false,
    );
    expect(canRetryInvocation(invocation({ status: 'Completed' }), true)).toBe(false);
    expect(canRetryInvocation(invocation({ status: 'Attention' }), false)).toBe(false);
  });
  it('offers Replay only when a delivery operation exists', () => {
    expect(canReplayInvocation(invocation({ status: 'Attention', operationId: 'op' }), true)).toBe(
      true,
    );
    expect(canReplayInvocation(invocation({ status: 'Attention' }), true)).toBe(false);
    expect(canReplayInvocation(invocation({ status: 'Attention', operationId: 'op' }), false)).toBe(
      false,
    );
  });
  it('never offers either once a normal graph event already advanced the graph', () => {
    const advanced = invocation({
      status: 'Attention',
      executionKind: 'Graph',
      graphAdvanced: true,
      operationId: 'op',
    });
    expect(canRetryInvocation(advanced, true)).toBe(false);
    expect(canReplayInvocation(advanced, true)).toBe(false);
    // An optional graph event advances early on purpose and stays recoverable.
    expect(canReplayInvocation({ ...advanced, executionKind: 'OptionalGraph' }, true)).toBe(true);
  });
  it('treats Callable and MapOnly receipts as independent calls', () => {
    expect(isIndependentInvocation('Callable')).toBe(true);
    expect(isIndependentInvocation('MapOnly')).toBe(true);
    expect(isIndependentInvocation('Graph')).toBe(false);
  });
});

describe('Mapping receipts panel', () => {
  const invocationsForRoot = vi.fn();
  const retryInvocation = vi.fn();
  const preparedInputs = vi.fn();
  const replay = vi.fn();

  beforeEach(() => {
    for (const fn of [invocationsForRoot, retryInvocation, preparedInputs, replay]) fn.mockReset();
    TestBed.configureTestingModule({
      imports: [WorkflowMappingInvocationsComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        {
          provide: WorkflowMappingService,
          useValue: { invocationsForRoot, retryInvocation, preparedInputs },
        },
        { provide: WorkflowIntegrationsService, useValue: { replay } },
      ],
    });
  });

  function show(items: MappingInvocationDto[], canManage = true) {
    invocationsForRoot.mockReturnValue(of(items));
    const fixture = TestBed.createComponent(WorkflowMappingInvocationsComponent);
    fixture.componentRef.setInput('rootInstanceId', 'root');
    fixture.componentRef.setInput('canManage', canManage);
    const loaded = vi.fn();
    fixture.componentInstance.loaded.subscribe(loaded);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, loaded };
  }
  const buttons = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).map((b) => b.textContent!.trim());

  it('shows the waiting deadline and the attention reason, and tells the host what it loaded', () => {
    const { el, loaded } = show([
      invocation({ id: 'w', status: 'Waiting', deadline: '2026-03-01T10:00:00Z' }),
      invocation({ id: 'a', status: 'Attention', error: 'No related instance matched.' }),
    ]);
    expect(el.querySelector('[data-invocation="w"] [role="status"]')!.textContent).toContain(
      'Waiting for data until',
    );
    expect(el.querySelector('[data-invocation="a"] [role="alert"]')!.textContent).toContain(
      'No related instance matched.',
    );
    expect(loaded).toHaveBeenCalledWith(
      expect.arrayContaining([expect.objectContaining({ id: 'w' })]),
    );
  });

  it('marks independent follow-ups as not moving the workflow', () => {
    const { el } = show([invocation({ executionKind: 'MapOnly', actionId: 'act_1' })]);
    expect(el.textContent).toContain('Independent call');
    expect(el.textContent).toContain('act_1');
  });

  it('offers Retry for a preparation fault and Replay once a delivery job exists', () => {
    const { el } = show([
      invocation({ id: 'prep', status: 'Attention' }),
      invocation({ id: 'sent', status: 'Attention', operationId: 'op-9' }),
    ]);
    expect(buttons(el.querySelector<HTMLElement>('[data-invocation="prep"]')!)).toEqual(['Retry']);
    expect(buttons(el.querySelector<HTMLElement>('[data-invocation="sent"]')!)).toEqual([
      'Replay delivery',
    ]);
  });

  it('offers no recovery to a viewer who cannot manage', () => {
    const { el } = show([invocation({ status: 'Attention', operationId: 'op' })], false);
    expect(buttons(el)).toEqual([]);
  });

  it('replays the recorded operation, then reloads the receipts', () => {
    replay.mockReturnValue(of({}));
    const { fixture, el } = show([
      invocation({ status: 'Attention', operationId: 'op-9', error: 'boom' }),
    ]);
    el.querySelector<HTMLButtonElement>('[data-action="replay"]')!.click();
    fixture.detectChanges();
    expect(replay).toHaveBeenCalledWith('op-9');
    expect(invocationsForRoot).toHaveBeenCalledTimes(2);
  });

  it('shows the server’s refusal when a replay is refused', () => {
    replay.mockReturnValue(
      throwError(() => ({ error: { message: 'The delivery job is not Failed.' } })),
    );
    const { fixture, el } = show([invocation({ status: 'Attention', operationId: 'op-9' })]);
    el.querySelector<HTMLButtonElement>('[data-action="replay"]')!.click();
    fixture.detectChanges();
    expect(el.querySelector('p[role="alert"].text-red-700')!.textContent).toContain(
      'The delivery job is not Failed.',
    );
  });

  it('retries a preparation fault through the receipt endpoint', () => {
    retryInvocation.mockReturnValue(of(undefined));
    const { el } = show([invocation({ id: 'inv-7', status: 'Attention' })]);
    el.querySelector<HTMLButtonElement>('[data-invocation="inv-7"] button')!.click();
    expect(retryInvocation).toHaveBeenCalledWith('inv-7');
  });

  it('reads prepared inputs only on explicit request, and says nothing was sent', () => {
    preparedInputs.mockReturnValue(
      of({
        invocationId: 'm',
        rootInstanceId: 'root',
        targetInstanceId: 't',
        target: {},
        actionId: 'a',
        inputs: { id: 7 },
        provenance: [{}, {}],
      }),
    );
    const { fixture, el } = show([invocation({ id: 'm', executionKind: 'MapOnly' })]);
    expect(preparedInputs).not.toHaveBeenCalled();
    el.querySelector<HTMLButtonElement>('[data-invocation="m"] button')!.click();
    fixture.detectChanges();
    expect(preparedInputs).toHaveBeenCalledWith('m');
    expect(el.querySelector('pre')!.textContent).toContain('"id": 7');
    expect(el.textContent).toContain('Nothing was sent to the target');
  });

  it('shows no receipts, without an error, to a viewer who may not read them', () => {
    invocationsForRoot.mockReturnValue(throwError(() => ({ status: 403 })));
    const fixture = TestBed.createComponent(WorkflowMappingInvocationsComponent);
    fixture.componentRef.setInput('rootInstanceId', 'root');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('p[role="alert"].text-red-700')).toBeNull();
    expect(el.textContent).toContain('No mapping activity');
  });
});
