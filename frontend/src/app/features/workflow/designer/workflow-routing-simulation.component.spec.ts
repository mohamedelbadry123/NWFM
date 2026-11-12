import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { of, Subject } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import type { WorkflowHistoryEvent } from '@core/models/workflow-ops.models';
import { WorkflowHistoryTimelineComponent } from '../ui/workflow-history-timeline.component';
import { WorkflowRoutingSimulationComponent } from './workflow-routing-simulation.component';
import {
  RoutingContext,
  RoutingRuleStatus,
  RoutingSimulation,
  WorkflowRoutingService,
} from './workflow-routing.service';

const context: RoutingContext = {
  sources: [],
  nodes: [
    { key: 'start', name: 'Start', type: 'Start' },
    { key: 'gate', name: 'Decide', type: 'ExclusiveGateway' },
    { key: 'visit', name: 'Site visit', type: 'UserTask' },
    { key: 'review', name: 'Manager review', type: 'UserTask' },
    { key: 'close', name: 'Close', type: 'UserTask' },
  ],
  connections: [
    { key: 'route_a', from: 'gate', to: 'visit' },
    { key: 'route_b', from: 'gate', to: 'review' },
    { key: 'route_c', from: 'gate', to: 'close' },
  ],
  enabled: true,
  revision: 'r1',
};

const run = (trace: RoutingSimulation['trace']): RoutingSimulation => ({
  trace,
  waiting: [],
  diagnostics: [],
});
const line = (status: string, explanation: string, nodeKey = 'gate') => ({
  nodeKey,
  status,
  explanation,
  branch: 'root',
});
/** One route's line as the server sends it: `rule` and its 1-based `ruleOrder`. An errored visited route is Checked with a problem. */
const route = (
  status: RoutingRuleStatus,
  transitionKey: string,
  order: number,
  problem: string | null = null,
) => ({
  ...line(status, problem ?? transitionKey),
  rule: { transitionKey, status, problem },
  ruleOrder: order,
});

describe('Simulation of ordered routing', () => {
  const simulate = vi.fn();
  beforeEach(() => {
    simulate.mockReset();
    TestBed.configureTestingModule({
      imports: [WorkflowRoutingSimulationComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowRoutingService, useValue: { simulate } },
      ],
    });
  });

  async function simulateWith(result: RoutingSimulation) {
    simulate.mockReturnValue(of(result));
    const fixture = TestBed.createComponent(WorkflowRoutingSimulationComponent);
    fixture.componentRef.setInput('definitionId', 'd');
    fixture.componentRef.setInput('versionId', 'v');
    fixture.componentRef.setInput('context', context);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Run simulation'))!
      .click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelector('[data-trace-rules]')).not.toBeNull();
    });
    const block = el.querySelector('[data-trace-rules]') as HTMLElement;
    const rules = () =>
      Array.from(block.querySelectorAll<HTMLElement>('[data-rule-status]')).map((rule) => ({
        status: rule.dataset['ruleStatus'],
        text: rule.textContent!.replace(/\s+/g, ' ').trim(),
      }));
    return { fixture, el, block, rules };
  }

  it('shows which route matched, which were only checked and which were never tried, in order', async () => {
    const { block, rules, el } = await simulateWith(
      run([
        line('Completed', 'Started', 'start'),
        route('Checked', 'route_a', 1),
        route('Matched', 'route_b', 2),
        route('Skipped', 'route_c', 3),
        line('Matched', 'Matched: route_b'),
        line('Completed', 'This continuation reached End.', 'review'),
      ]),
    );
    expect(block.textContent).toContain('from top to bottom and the first match is used');
    expect(rules().map((r) => r.status)).toEqual(['checked', 'matched', 'skipped']);
    expect(rules()[0].text).toContain('Route 1 → Site visit');
    expect(rules()[0].text).toContain('Checked — did not match');
    expect(rules()[1].text).toContain('Route 2 → Manager review');
    expect(rules()[1].text).toContain('Matched — this route was chosen');
    expect(rules()[2].text).toContain('Route 3 → Close');
    expect(rules()[2].text).toContain('Not tried — an earlier route already decided');
    // A skipped route is not an error: nothing in the block is announced as a problem.
    expect(block.querySelector('[role="alert"]')).toBeNull();
    // The Condition's own summary and the other steps stay as ordinary numbered steps around the block.
    const titles = Array.from(el.querySelectorAll('button.trace strong, .rules-title strong')).map(
      (s) => s.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(titles).toEqual([
      '1. Start · Completed',
      '2. Decide · Routes tried',
      '3. Decide · Matched',
      '4. Manager review · Completed',
    ]);
  });

  it('reports the first visited source that is unavailable as a problem, not as “no match”', async () => {
    const { rules, block, el } = await simulateWith(
      run([
        route('Checked', 'route_a', 1, 'The Amount source has no value yet.'),
        route('Skipped', 'route_b', 2),
        route('Skipped', 'route_c', 3),
        line('Attention', 'The Amount source has no value yet.'),
      ]),
    );
    expect(rules().map((r) => r.status)).toEqual(['error', 'skipped', 'skipped']);
    expect(rules()[0].text).toContain('Could not be checked');
    expect(rules()[0].text).toContain('later routes were not tried');
    expect(block.querySelector('[role="alert"]')!.textContent).toContain(
      'The Amount source has no value yet.',
    );
    expect(el.textContent).toContain('Decide · Attention');
  });

  it('lets an earlier match win even when a later route’s source is unavailable', async () => {
    const { rules, block } = await simulateWith(
      run([
        route('Matched', 'route_a', 1),
        // Later routes are never visited, so their unavailable source cannot block the earlier match.
        route('Skipped', 'route_b', 2),
        route('Skipped', 'route_c', 3),
        line('Matched', 'Matched: route_a'),
      ]),
    );
    expect(rules().map((r) => r.status)).toEqual(['matched', 'skipped', 'skipped']);
    expect(block.querySelector('[role="alert"]')).toBeNull();
  });

  it('presents an old-contract run unchanged when it carries no route lines', async () => {
    simulate.mockReturnValue(
      of(run([line('Matched', 'Matched: route_a'), line('Completed', 'End', 'visit')])),
    );
    const fixture = TestBed.createComponent(WorkflowRoutingSimulationComponent);
    fixture.componentRef.setInput('definitionId', 'd');
    fixture.componentRef.setInput('versionId', 'v');
    fixture.componentRef.setInput('context', context);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Run simulation'))!
      .click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelectorAll('.trace').length).toBe(2);
    });
    expect(el.querySelector('[data-trace-rules]')).toBeNull();
  });
});

describe('Sample run across designer refreshes', () => {
  const simulate = vi.fn();
  const waiting = {
    nodeKey: 'visit',
    status: 'Waiting',
    explanation: 'Provide a mocked completion.',
    branch: 'root',
  };
  const waitingRun: RoutingSimulation = { trace: [waiting], waiting: [waiting], diagnostics: [] };
  beforeEach(() => {
    simulate.mockReset();
    TestBed.configureTestingModule({
      imports: [WorkflowRoutingSimulationComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowRoutingService, useValue: { simulate } },
      ],
    });
  });

  function open() {
    const fixture = TestBed.createComponent(WorkflowRoutingSimulationComponent);
    fixture.componentRef.setInput('definitionId', 'd');
    fixture.componentRef.setInput('versionId', 'v');
    fixture.componentRef.setInput('context', context);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (text: string) =>
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent!.includes(text));
    /** The designer re-fetches the context: always a new object, not always a new workflow. */
    const refresh = (next: RoutingContext) => {
      fixture.componentRef.setInput('context', next);
      fixture.detectChanges();
    };
    return { fixture, el, button, refresh };
  }

  it('keeps the result and the waiting options when the designer re-fetches an identical context', async () => {
    simulate.mockReturnValue(of(waitingRun));
    const { fixture, el, button, refresh } = open();
    button('Run simulation')!.click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelectorAll('select option').length).toBeGreaterThan(1);
    });
    refresh(structuredClone(context));
    expect(el.textContent).not.toContain('Run a sample to see what happens');
    expect(
      Array.from(el.querySelectorAll('select option')).map((o) => o.textContent!.trim()),
    ).toContain('Site visit · root');
    expect(button('Run simulation')).toBeUndefined();
  });

  it('starts over when the saved workflow really changed (a new revision)', async () => {
    simulate.mockReturnValue(of(waitingRun));
    const { fixture, el, button, refresh } = open();
    button('Run simulation')!.click();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelectorAll('select option').length).toBeGreaterThan(1);
    });
    refresh({ ...context, revision: 'r2' });
    expect(el.textContent).toContain('Run a sample to see what happens');
    expect(button('Run simulation')!.disabled).toBe(false);
  });

  it('does not lose a result that is still in flight when an identical context arrives', async () => {
    const pending = new Subject<RoutingSimulation>();
    simulate.mockReturnValue(pending);
    const { fixture, el, button, refresh } = open();
    button('Run simulation')!.click();
    fixture.detectChanges();
    expect(button('Run simulation')!.disabled).toBe(true);
    refresh(structuredClone(context));
    pending.next(waitingRun);
    pending.complete();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelectorAll('select option').length).toBeGreaterThan(1);
    });
  });

  it('drops the answer of a run that the workflow changed under, and leaves Run usable', async () => {
    const pending = new Subject<RoutingSimulation>();
    simulate.mockReturnValue(pending);
    const { fixture, el, button, refresh } = open();
    button('Run simulation')!.click();
    fixture.detectChanges();
    refresh({ ...context, revision: 'r2' });
    pending.next(waitingRun);
    pending.complete();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('Run a sample to see what happens');
    expect(button('Run simulation')!.disabled).toBe(false);
  });

  describe('a completion that was still in flight when the workflow changed', () => {
    const withSamples: RoutingContext = {
      ...context,
      sources: [
        { key: 'in.note', nodeKey: '$input', field: 'note', type: 'text', label: 'Note' },
        { key: 'visit.result', nodeKey: 'visit', field: 'result', type: 'text', label: 'Result' },
      ],
    };
    /** Run → pick the waiting Step → submit its mock result, left unanswered; returns the open pieces. */
    async function submitCompletion() {
      const late = new Subject<RoutingSimulation>();
      simulate.mockReturnValueOnce(of(waitingRun)).mockReturnValueOnce(late);
      const page = open();
      page.fixture.componentRef.setInput('context', withSamples);
      page.fixture.detectChanges();
      page.button('Run simulation')!.click();
      await vi.waitFor(() => {
        page.fixture.detectChanges();
        expect(page.el.querySelectorAll('select option').length).toBeGreaterThan(1);
      });
      const select = page.el.querySelector('select')!;
      select.value = 'visit|root';
      select.dispatchEvent(new Event('change'));
      await page.fixture.whenStable();
      page.fixture.detectChanges();
      page.button('Submit mock result')!.click();
      page.fixture.detectChanges();
      return { ...page, late };
    }
    /** Typing like a person: after the control's own first (asynchronous) model write has settled. */
    const typeInto = async (
      fixture: { whenStable(): Promise<unknown> },
      el: HTMLElement,
      sample: string,
      text: string,
    ) => {
      await fixture.whenStable();
      const input = el.querySelector<HTMLInputElement>(`[data-sample="${sample}"]`)!;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    /** Lets the awaiting complete() continue (a real macrotask), then renders and lets the controls write. */
    const drained = async (fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) => {
      await new Promise((resolve) => setTimeout(resolve));
      fixture.detectChanges();
      await fixture.whenStable();
    };
    const sampleValue = (el: HTMLElement, sample: string) =>
      el.querySelector<HTMLInputElement>(`[data-sample="${sample}"]`)?.value;

    it('keeps the sample typed for the new workflow when the late answer arrives', async () => {
      const { fixture, el, button, refresh, late } = await submitCompletion();
      refresh({ ...withSamples, revision: 'r2' });
      await typeInto(fixture, el, 'in.note', 'for the new revision');
      late.next(waitingRun);
      late.complete();
      await drained(fixture);
      expect(sampleValue(el, 'in.note')).toBe('for the new revision');
      expect(fixture.componentInstance.samples).toEqual({ 'in.note': 'for the new revision' });
      expect(el.textContent).toContain('Run a sample to see what happens');
      expect(button('Run simulation')!.disabled).toBe(false);
    });

    it('keeps the sample typed for the new workflow when the late request fails', async () => {
      const { fixture, el, button, refresh, late } = await submitCompletion();
      refresh({ ...withSamples, revision: 'r2' });
      await typeInto(fixture, el, 'in.note', 'for the new revision');
      late.error(new Error('network'));
      await drained(fixture);
      expect(sampleValue(el, 'in.note')).toBe('for the new revision');
      expect(fixture.componentInstance.samples).toEqual({ 'in.note': 'for the new revision' });
      expect(el.querySelector('[role="alert"]')).toBeNull();
      expect(button('Run simulation')!.disabled).toBe(false);
    });

    it.each([
      ['success', (late: Subject<RoutingSimulation>) => (late.next(waitingRun), late.complete())],
      ['failure', (late: Subject<RoutingSimulation>) => late.error(new Error('network'))],
    ])('keeps the new run’s selected element and sample after a late %s', async (_name, settle) => {
      const { fixture, el, button, refresh, late } = await submitCompletion();
      simulate.mockReturnValueOnce(of(waitingRun));
      refresh({ ...withSamples, revision: 'r2' });
      button('Run simulation')!.click();
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(el.querySelectorAll('select option').length).toBeGreaterThan(1);
      });
      const select = el.querySelector('select')!;
      select.value = 'visit|root';
      select.dispatchEvent(new Event('change'));
      await fixture.whenStable();
      fixture.detectChanges();
      await typeInto(fixture, el, 'visit.result', 'new answer');

      settle(late);
      await drained(fixture);
      expect(el.querySelector('select')!.value).toBe('visit|root');
      expect(sampleValue(el, 'visit.result')).toBe('new answer');
      expect(fixture.componentInstance.selected).toBe('visit|root');
      expect(fixture.componentInstance.samples).toEqual({ 'visit.result': 'new answer' });
    });

    it('still clears the form after the run it belongs to completes', async () => {
      const { fixture, el, late } = await submitCompletion();
      late.next(waitingRun);
      late.complete();
      await drained(fixture);
      expect(fixture.componentInstance.selected).toBe('');
      expect(fixture.componentInstance.samples).toEqual({});
      expect(el.querySelector('[data-sample="visit.result"]')).toBeNull();
    });
  });
});

describe('Typed sample inputs of the simulation', () => {
  const simulate = vi.fn();
  const typed: RoutingContext = {
    ...context,
    sources: [
      { key: 'in.amount', nodeKey: '$input', field: 'amount', type: 'numeric', label: 'Amount' },
      { key: 'in.urgent', nodeKey: '$input', field: 'urgent', type: 'yes_no', label: 'Urgent' },
      {
        key: 'in.tags',
        nodeKey: '$input',
        field: 'tags',
        type: 'multiple_choice',
        label: 'Tags',
        choices: [
          { value: 'water', labelEn: 'Water', labelAr: 'مياه' },
          { value: 'power', labelEn: 'Power', labelAr: 'كهرباء' },
        ],
      },
      { key: 'in.note', nodeKey: '$input', field: 'note', type: 'text', label: 'Note' },
    ],
  };
  beforeEach(() => {
    simulate.mockReset().mockReturnValue(of(run([])));
    TestBed.configureTestingModule({
      imports: [WorkflowRoutingSimulationComponent],
      providers: [
        { provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } },
        { provide: WorkflowRoutingService, useValue: { simulate } },
      ],
    });
  });

  it('sends a real number, a real boolean and a real array from the controls the author uses', async () => {
    const fixture = TestBed.createComponent(WorkflowRoutingSimulationComponent);
    fixture.componentRef.setInput('definitionId', 'd');
    fixture.componentRef.setInput('versionId', 'v');
    fixture.componentRef.setInput('context', typed);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const amount = el.querySelector<HTMLInputElement>('[data-sample="in.amount"]')!;
    expect(amount.type).toBe('number');
    amount.value = '42';
    amount.dispatchEvent(new Event('input'));
    const urgent = el.querySelector<HTMLSelectElement>('[data-sample="in.urgent"]')!;
    expect(Array.from(urgent.options).map((o) => o.textContent!.trim())).toEqual([
      'Empty',
      'Yes',
      'No',
    ]);
    urgent.selectedIndex = 1;
    urgent.dispatchEvent(new Event('change'));
    const boxes = el.querySelectorAll<HTMLInputElement>(
      '[data-sample="in.tags"] input[type="checkbox"]',
    );
    expect(boxes.length).toBe(2);
    boxes[0].click();
    boxes[1].click();
    el.querySelector<HTMLInputElement>('[data-sample="in.note"]')!.value = 'hello';
    el.querySelector<HTMLInputElement>('[data-sample="in.note"]')!.dispatchEvent(
      new Event('input'),
    );
    fixture.detectChanges();
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Run simulation'))!
      .click();
    await vi.waitFor(() => expect(simulate).toHaveBeenCalled());
    const inputs = simulate.mock.calls[0][3];
    expect(inputs).toEqual({ amount: 42, urgent: true, tags: ['water', 'power'], note: 'hello' });
    expect(typeof inputs.amount).toBe('number');
  });

  it('sends nothing for an emptied number or a fully unchecked list', async () => {
    const fixture = TestBed.createComponent(WorkflowRoutingSimulationComponent);
    fixture.componentRef.setInput('definitionId', 'd');
    fixture.componentRef.setInput('versionId', 'v');
    fixture.componentRef.setInput('context', typed);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const amount = el.querySelector<HTMLInputElement>('[data-sample="in.amount"]')!;
    amount.value = '7';
    amount.dispatchEvent(new Event('input'));
    amount.value = '';
    amount.dispatchEvent(new Event('input'));
    const box = el.querySelector<HTMLInputElement>(
      '[data-sample="in.tags"] input[type="checkbox"]',
    )!;
    box.click();
    box.click();
    Array.from(el.querySelectorAll('button'))
      .find((b) => b.textContent!.includes('Run simulation'))!
      .click();
    await vi.waitFor(() => expect(simulate).toHaveBeenCalled());
    expect(simulate.mock.calls[0][3]).toEqual({});
  });
});

describe('Routes tried in the request history', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowHistoryTimelineComponent, TranslateModule.forRoot()],
      providers: [{ provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } }],
    }),
  );
  const event = (payloadJson: string | null): WorkflowHistoryEvent => ({
    id: 'e1',
    eventType: 'InstanceStarted',
    occurredAt: '2026-11-11T10:00:00Z',
    payloadJson,
  });
  function render(events: WorkflowHistoryEvent[]) {
    const fixture = TestBed.createComponent(WorkflowHistoryTimelineComponent);
    fixture.componentRef.setInput('events', events);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the recorded rule trace of an event that has one, including a problem', () => {
    const el = render([
      event(
        JSON.stringify({
          routing: true,
          rules: [
            {
              TransitionKey: 'route_a',
              Status: 'Checked',
              Problem: 'The Amount source has no value.',
            },
            { TransitionKey: 'route_b', Status: 'Skipped', Problem: null },
          ],
        }),
      ),
    ]);
    const block = el.querySelector('[data-routing-rules]')!;
    expect(block).not.toBeNull();
    expect(
      Array.from(block.querySelectorAll<HTMLElement>('[data-rule-status]')).map(
        (r) => r.dataset['ruleStatus'],
      ),
    ).toEqual(['error', 'skipped']);
    expect(block.querySelector('[role="alert"]')!.textContent).toContain(
      'The Amount source has no value.',
    );
  });

  it('keeps the ActivityCompleted event that carries a Condition’s routes even when people’s events exist', () => {
    const el = render([
      { ...event(null), id: 'h1', eventType: 'WorkItemCompleted' },
      {
        ...event(
          JSON.stringify({
            routing: true,
            rules: [{ TransitionKey: 'route_a', Status: 'Matched', Problem: null }],
          }),
        ),
        id: 'r1',
        eventType: 'ActivityCompleted',
      },
      // System noise without a trace stays hidden next to human events.
      { ...event('{"routing":true}'), id: 'n1', eventType: 'ActivityCompleted' },
    ]);
    expect(el.querySelectorAll('ol.relative > li').length).toBe(2);
    expect(el.querySelector('[data-routing-rules] [data-rule-status="matched"]')).not.toBeNull();
  });

  it('shows nothing extra for events without a rule trace', () => {
    expect(render([event(null)]).querySelector('[data-routing-rules]')).toBeNull();
    expect(render([event('{"routing":true}')]).querySelector('[data-routing-rules]')).toBeNull();
  });
});
