import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { userEvent } from 'vitest/browser';
import { LocaleService } from '@core/i18n/locale.service';
import { CanvasEdge, CanvasNode } from './workflow-designer.component';
import { WorkflowRoutingEditorComponent } from './workflow-routing-editor.component';
import { edgeOrders } from './workflow-routing-order';
import {
  RoutingRuleStatus,
  RoutingTrace,
  isOrderedRouting,
  newConditionRouting,
  routingDefaults,
} from './workflow-routing.service';
import {
  isRuleLine,
  normalizeRuleTrace,
  ruleTraceFromPayload,
  simulationRows,
} from './workflow-routing-trace';

const rule = (key: string) => ({
  transitionKey: key,
  always: true,
  when: { match: 'All' as const, conditions: [], groups: [] },
});
const KEYS = ['route_a', 'route_b', 'route_c'];

function graph(routing: object) {
  const condition = {
    id: 'condition',
    nodeKey: 'condition',
    name: 'Condition',
    type: 'ExclusiveGateway',
    configurationJson: JSON.stringify({ routing }),
  } as CanvasNode;
  const targets = KEYS.map(
    (key, i) =>
      ({
        id: 'dest' + i,
        nodeKey: 'dest' + i,
        name: 'Destination ' + (i + 1),
        type: 'UserTask',
      }) as CanvasNode,
  );
  const edges = KEYS.map(
    (key, i) =>
      ({
        id: 'edge' + i,
        fromNodeId: 'condition',
        toNodeId: 'dest' + i,
        transitionKey: key,
        labelEn: 'My label ' + (i + 1),
        labelAr: '',
        descriptionEn: '',
        outcomeKey: '',
        conditionExpression: '',
        isDefault: false,
        priority: i,
      }) as CanvasEdge,
  );
  return { condition, targets, edges };
}

describe('Routing contract defaults', () => {
  it('creates a new Condition as ordered routing on contract version 2', () => {
    expect(newConditionRouting()).toMatchObject({
      contractVersion: 2,
      matchMode: 'First',
      routes: [],
    });
    expect(isOrderedRouting(newConditionRouting())).toBe(true);
  });

  it('reads a stored or missing configuration as the legacy contract, never as ordered', () => {
    expect(routingDefaults()).toMatchObject({ contractVersion: 1, matchMode: 'Unique' });
    expect(isOrderedRouting(routingDefaults())).toBe(false);
    // A legacy "First" is not the ordered contract: it still checks every route.
    expect(isOrderedRouting({ contractVersion: 1, matchMode: 'First' })).toBe(false);
  });

  it('keeps the two apart: creating never mutates the shared legacy defaults', () => {
    const created = newConditionRouting();
    created.routes.push(rule('x'));
    expect(routingDefaults().routes).toEqual([]);
    expect(routingDefaults().contractVersion).toBe(1);
  });
});

describe('Route order on canvas connections', () => {
  const { condition, targets, edges } = graph({ ...newConditionRouting(), routes: KEYS.map(rule) });

  it('numbers each connection by its route’s place and leaves its label alone', () => {
    const orders = edgeOrders([condition, ...targets], edges);
    expect([...orders.values()].map((o) => o.position)).toEqual([1, 2, 3]);
    expect(orders.get('edge1')).toEqual({ position: 2, total: 3, otherwise: false });
    expect(edges.map((e) => e.labelEn)).toEqual(['My label 1', 'My label 2', 'My label 3']);
  });

  it('follows a reorder without touching the connections or their keys', () => {
    const reordered = {
      ...condition,
      configurationJson: JSON.stringify({
        routing: { ...newConditionRouting(), routes: ['route_c', 'route_a', 'route_b'].map(rule) },
      }),
    };
    const orders = edgeOrders([reordered, ...targets], edges);
    expect(orders.get('edge2')!.position).toBe(1);
    expect(orders.get('edge0')!.position).toBe(2);
    expect(orders.get('edge1')!.position).toBe(3);
  });

  it('follows removal and marks Otherwise instead of numbering it', () => {
    const routing = {
      ...newConditionRouting(),
      routes: ['route_a', 'route_c'].map(rule),
      fallbackTransitionKey: 'route_b',
    };
    const orders = edgeOrders(
      [{ ...condition, configurationJson: JSON.stringify({ routing }) }, ...targets],
      edges,
    );
    expect(orders.get('edge0')).toEqual({ position: 1, total: 2, otherwise: false });
    expect(orders.get('edge2')).toEqual({ position: 2, total: 2, otherwise: false });
    expect(orders.get('edge1')).toEqual({ total: 2, otherwise: true });
  });

  it('numbers only ordered Conditions: not the legacy “exactly one” policy, not Fork or Join', () => {
    const legacy = {
      ...condition,
      configurationJson: JSON.stringify({
        routing: { ...routingDefaults(), routes: KEYS.map(rule) },
      }),
    };
    expect(edgeOrders([legacy, ...targets], edges).size).toBe(0);
    const fork = { ...condition, type: 'ParallelGateway' } as CanvasNode;
    expect(edgeOrders([fork, ...targets], edges).size).toBe(0);
    expect(
      edgeOrders([{ ...condition, configurationJson: '{broken' }, ...targets], edges).size,
    ).toBe(0);
  });

  it('numbers a legacy “first match” Condition, where order is the priority', () => {
    const legacyFirst = {
      ...condition,
      configurationJson: JSON.stringify({
        routing: { ...routingDefaults(), matchMode: 'First', routes: KEYS.map(rule) },
      }),
    };
    expect(edgeOrders([legacyFirst, ...targets], edges).get('edge0')!.position).toBe(1);
  });
});

describe('Ordered routes editor', () => {
  const locale = { isRtl: () => false, locale: () => 'en' };
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowRoutingEditorComponent],
      providers: [{ provide: LocaleService, useValue: locale }],
    }),
  );

  function setup(
    routing: object = { ...newConditionRouting(), routes: KEYS.map(rule) },
    readonly = false,
  ) {
    const { condition, targets, edges } = graph(routing);
    const fixture = TestBed.createComponent(WorkflowRoutingEditorComponent);
    fixture.componentRef.setInput('node', condition);
    fixture.componentRef.setInput('nodes', [condition, ...targets]);
    fixture.componentRef.setInput('edges', edges);
    fixture.componentRef.setInput('readonly', readonly);
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const sections = () => Array.from(el.querySelectorAll<HTMLElement>('[data-route-key]'));
    const order = () => sections().map((s) => s.dataset['routeKey']);
    const lastSaved = () => {
      const change = changed.mock.lastCall![0];
      return { change, routing: JSON.parse(change.configuration).routing };
    };
    return { fixture, editor: fixture.componentInstance, el, changed, sections, order, lastSaved };
  }

  it('numbers the routes and explains top-to-bottom, first-match evaluation', () => {
    const { el, sections } = setup();
    expect(
      Array.from(el.querySelectorAll('.route-number')).map((n) => n.textContent!.trim()),
    ).toEqual(['1', '2', '3']);
    const mode = el.querySelector('[data-routing-mode="ordered"]')!;
    expect(mode.textContent).toContain('from top to bottom');
    expect(mode.textContent).toContain('first route whose conditions match');
    expect(mode.textContent).toContain('Routes after it are not checked');
    expect(mode.textContent).toContain('never blocks an earlier match');
    expect(sections().every((s) => s.querySelector('[data-route-handle]'))).toBe(true);
    // The policy picker is a legacy setting: ordered routing is always first-match.
    expect(el.querySelector('[data-routing-field="matchMode"]')).toBeNull();
    expect(el.querySelector('[data-routing-action="use-ordered"]')).toBeNull();
  });

  it('moves a route with the up and down buttons, keeping its key, connection and label', () => {
    const { fixture, el, order, lastSaved, editor } = setup();
    const before = editor.localEdges.map((e) => ({ ...e }));
    const down = (n: number) => el.querySelectorAll<HTMLButtonElement>('[data-move="down"]')[n - 1];
    expect(el.querySelector<HTMLButtonElement>('[data-move="up"]')!.disabled).toBe(true);
    expect(el.querySelectorAll<HTMLButtonElement>('[data-move="down"]')[2].disabled).toBe(true);
    expect(down(1).getAttribute('aria-label')).toBe('Move route 1 down');

    down(1).click();
    fixture.detectChanges();

    const { change, routing } = lastSaved();
    expect(routing.routes.map((r: { transitionKey: string }) => r.transitionKey)).toEqual([
      'route_b',
      'route_a',
      'route_c',
    ]);
    expect(order()).toEqual(['route_b', 'route_a', 'route_c']);
    // Stable identity: every connection keeps its id, target and key; only its priority follows the new place.
    const after = change.edges as CanvasEdge[];
    expect(after.map((e) => [e.id, e.toNodeId, e.transitionKey]).sort()).toEqual(
      before.map((e) => [e.id, e.toNodeId, e.transitionKey]).sort(),
    );
    expect(Object.fromEntries(after.map((e) => [e.transitionKey, e.priority]))).toEqual({
      route_b: 0,
      route_a: 1,
      route_c: 2,
    });
    expect(el.querySelector('[role="status"]')!.textContent).toContain(
      'Route moved to position 2 of 3',
    );
    expect(routing.contractVersion).toBe(2);
    expect(routing.matchMode).toBe('First');
  });

  it('keeps keyboard focus on the moved route’s control, including at the end of the list', async () => {
    const { fixture, el } = setup();
    const down = el.querySelector<HTMLButtonElement>('[data-move="down"]')!;
    down.focus();
    down.click();
    fixture.detectChanges();
    await vi.waitFor(() => {
      const focused = document.activeElement as HTMLElement;
      expect(focused.getAttribute('data-move')).toBe('down');
      expect(focused.closest('[data-route-key]')!.getAttribute('data-route-position')).toBe('2');
    });
    (document.activeElement as HTMLButtonElement).click();
    fixture.detectChanges();
    await vi.waitFor(() => {
      // At the bottom the pressed button is disabled, so focus moves to its opposite.
      const focused = document.activeElement as HTMLElement;
      expect(focused.getAttribute('data-move')).toBe('up');
      expect(focused.closest('[data-route-key]')!.getAttribute('data-route-position')).toBe('3');
    });
  });

  it('can be reordered by dragging a handle in a real browser, not only by buttons', async () => {
    const { fixture, el, order, lastSaved } = setup();
    const handles = () => Array.from(el.querySelectorAll<HTMLElement>('[data-route-handle]'));
    expect(handles()).toHaveLength(3);
    el.style.display = 'block';
    el.style.width = '640px';
    // Real pointer events (press, move, release): drag route 1's handle down onto route 3's.
    await userEvent.dragAndDrop(handles()[0], handles()[2]);
    fixture.detectChanges();
    await vi.waitFor(() => expect(order()).not.toEqual(KEYS));
    // Route 1 moved down; no route was lost or duplicated, and the saved order is the one on screen.
    expect(order()[0]).not.toBe('route_a');
    expect([...order()].sort()).toEqual(KEYS);
    const saved = lastSaved();
    expect(saved.routing.routes.map((r: { transitionKey: string }) => r.transitionKey)).toEqual(
      order(),
    );
    expect((saved.change.edges as CanvasEdge[]).map((e) => e.transitionKey).sort()).toEqual(KEYS);
    expect(saved.routing).toMatchObject({ contractVersion: 2, matchMode: 'First' });
    expect(el.querySelector('[role="status"]')!.textContent).toContain('Route moved to position');
  });

  it('moving does nothing on a published, read-only version, which has no handle or upgrade button', () => {
    const { el, editor, changed } = setup(undefined, true);
    expect(el.querySelector('[data-route-handle]')).toBeNull();
    // The fieldset disables the controls of a published version.
    expect(
      Array.from(el.querySelectorAll<HTMLButtonElement>('[data-move]')).every((b) =>
        b.matches(':disabled'),
      ),
    ).toBe(true);
    editor.move(0, 1);
    editor.drop({ previousIndex: 0, currentIndex: 2 } as never);
    expect(changed).not.toHaveBeenCalled();
    expect(editor.config.routes.map((r) => r.transitionKey)).toEqual(KEYS);
  });

  describe('a Condition saved before ordered routing', () => {
    const legacy = (matchMode: 'Unique' | 'First' = 'Unique') => ({
      ...routingDefaults(),
      matchMode,
      routes: KEYS.map(rule),
    });

    it('opens as the legacy contract, says so accurately and changes nothing by being opened', () => {
      const { el, changed, editor } = setup(legacy());
      expect(changed).not.toHaveBeenCalled();
      expect(editor.config.contractVersion).toBe(1);
      const mode = el.querySelector('[data-routing-mode="legacy"]')!;
      expect(mode.textContent).toContain('Every route’s conditions are checked each time');
      expect(mode.textContent).toContain('exactly one route must match');
      expect(mode.textContent).toContain('Nothing changes until you choose ordered routing');
      expect(el.querySelector('[data-routing-mode="ordered"]')).toBeNull();
      // Unique does not depend on order: no numbers, handles or move buttons.
      expect(el.querySelector('.route-number')).toBeNull();
      expect(el.querySelector('[data-route-handle]')).toBeNull();
      expect(el.querySelector('[data-move]')).toBeNull();
      expect(el.querySelector('[data-routing-field="matchMode"]')).not.toBeNull();
    });

    it('describes a legacy “first match” as checking every route anyway', () => {
      const { el } = setup(legacy('First'));
      const mode = el.querySelector('[data-routing-mode="legacy"]')!;
      expect(mode.textContent).toContain('then the first matching route by priority is used');
      expect(mode.textContent).toContain('even when an earlier route matches');
      // Order is its priority, so it can be arranged — but it is still the legacy contract.
      expect(el.querySelectorAll('.route-number')).toHaveLength(3);
    });

    it('upgrades only on the explicit button, keeping every key, connection and reference', () => {
      const { fixture, el, changed, lastSaved, editor } = setup(legacy());
      const before = editor.localEdges.map((e) => ({
        id: e.id,
        key: e.transitionKey,
        to: e.toNodeId,
      }));
      el.querySelector<HTMLButtonElement>('[data-routing-action="use-ordered"]')!.click();
      fixture.detectChanges();
      expect(changed).toHaveBeenCalledOnce();
      const { change, routing } = lastSaved();
      expect(routing).toMatchObject({ contractVersion: 2, matchMode: 'First' });
      expect(routing.routes.map((r: { transitionKey: string }) => r.transitionKey)).toEqual(KEYS);
      expect(
        (change.edges as CanvasEdge[]).map((e) => ({
          id: e.id,
          key: e.transitionKey,
          to: e.toNodeId,
        })),
      ).toEqual(before);
      expect(el.querySelector('[data-routing-mode="ordered"]')).not.toBeNull();
      expect(el.querySelector('[data-routing-action="use-ordered"]')).toBeNull();
      expect(el.querySelector('[role="status"]')!.textContent).toContain('Ordered routing is on');
    });

    it('offers no upgrade on a published version, which keeps running as published', () => {
      const { el, editor, changed } = setup(legacy(), true);
      expect(el.querySelector('[data-routing-action="use-ordered"]')).toBeNull();
      expect(el.querySelector('[data-routing-mode="legacy"]')).not.toBeNull();
      editor.useOrderedRouting();
      expect(changed).not.toHaveBeenCalled();
      expect(editor.config.contractVersion).toBe(1);
    });
  });

  it('keeps Fork and Join editors on their own policies, with no ordering UI', () => {
    const fork = graph({ ...routingDefaults(), matchMode: 'All', routes: KEYS.map(rule) });
    const fixture = TestBed.createComponent(WorkflowRoutingEditorComponent);
    fixture.componentRef.setInput('node', { ...fork.condition, type: 'ParallelGateway' });
    fixture.componentRef.setInput('nodes', [fork.condition, ...fork.targets]);
    fixture.componentRef.setInput('edges', fork.edges);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-routing-mode]')).toBeNull();
    expect(el.querySelector('[data-move]')).toBeNull();
    expect(el.querySelector('[data-route-handle]')).toBeNull();
    expect(el.textContent).toContain('Paired Join');
    expect(fixture.componentInstance.config.matchMode).toBe('All');
  });
});

describe('Rule trace from the backend', () => {
  const t = (
    nodeKey: string,
    status: string,
    explanation: string,
    branch = 'root',
  ): RoutingTrace => ({
    nodeKey,
    status,
    explanation,
    branch,
  });

  it('reads API and audit casings, and drops anything malformed', () => {
    expect(
      normalizeRuleTrace([
        { transitionKey: 'a', status: 'Matched' },
        { TransitionKey: 'b', Status: 'Checked', Problem: 'No value for Amount.' },
        { transitionKey: 'c', status: 'Skipped', problem: null },
        { transitionKey: 'd', status: 'Maybe' },
        'x',
        null,
        { status: 'Checked' },
      ]),
    ).toEqual([
      { transitionKey: 'a', status: 'Matched', problem: null },
      { transitionKey: 'b', status: 'Checked', problem: 'No value for Amount.' },
      { transitionKey: 'c', status: 'Skipped', problem: null },
    ]);
    expect(normalizeRuleTrace(undefined)).toEqual([]);
  });

  it('finds the rules in a live audit payload and ignores payloads without them', () => {
    const payload = JSON.stringify({
      routing: true,
      matched: ['route_b'],
      rules: [
        { TransitionKey: 'route_a', Status: 'Checked', Problem: null },
        { TransitionKey: 'route_b', Status: 'Matched', Problem: null },
        { TransitionKey: 'route_c', Status: 'Skipped', Problem: null },
      ],
    });
    expect(ruleTraceFromPayload(payload).map((r) => r.status)).toEqual([
      'Checked',
      'Matched',
      'Skipped',
    ]);
    expect(ruleTraceFromPayload(JSON.stringify({ routing: true }))).toEqual([]);
    expect(ruleTraceFromPayload('not json "rules"')).toEqual([]);
    expect(ruleTraceFromPayload(null)).toEqual([]);
  });

  /** A route's line exactly as the server sends it: the rule object plus its 1-based order. */
  const line = (
    nodeKey: string,
    status: RoutingRuleStatus,
    transitionKey: string,
    order: number,
    problem: string | null = null,
    branch = 'root',
  ): RoutingTrace => ({
    nodeKey,
    status,
    explanation: problem ?? transitionKey,
    branch,
    rule: { transitionKey, status, problem },
    ruleOrder: order,
  });

  it('tells a route’s line from the Condition’s own summary by its rule, never by its text', () => {
    expect(isRuleLine(line('gate', 'Checked', 'route_a', 1))).toBe(true);
    expect(isRuleLine(line('gate', 'Matched', 'route_b', 2))).toBe(true);
    expect(isRuleLine(line('gate', 'Skipped', 'route_c', 3))).toBe(true);
    // Summaries carry no rule, whatever they say.
    expect(isRuleLine(t('gate', 'Matched', 'Matched: route_b'))).toBe(false);
    expect(isRuleLine(t('gate', 'Matched', 'route_b'))).toBe(false);
    expect(isRuleLine(t('gate', 'Attention', 'Amount is unavailable.'))).toBe(false);
  });

  it('folds each Condition’s route lines into one ordered block between the other steps', () => {
    const rows = simulationRows([
      t('start', 'Completed', 'Started'),
      line('gate', 'Checked', 'route_a', 1),
      line('gate', 'Matched', 'route_b', 2),
      t('gate', 'Matched', 'Matched: route_b'),
      t('review', 'Waiting', 'Waiting for a result'),
    ]);
    expect(rows.map((r) => r.kind)).toEqual(['step', 'rules', 'step', 'step']);
    const block = rows[1];
    expect(block.kind === 'rules' && block.rules).toEqual([
      { transitionKey: 'route_a', status: 'Checked', problem: null },
      { transitionKey: 'route_b', status: 'Matched', problem: null },
    ]);
    expect(rows.map((r) => r.number)).toEqual([1, 2, 3, 4]);
  });

  it('starts a new block when the same Condition is visited again (route order restarts at 1)', () => {
    const rows = simulationRows([
      line('gate', 'Matched', 'route_a', 1),
      line('gate', 'Checked', 'route_a', 1),
      line('gate', 'Matched', 'route_b', 2),
    ]);
    expect(rows.map((r) => r.kind)).toEqual(['rules', 'rules']);
    expect(rows[1].kind === 'rules' && rows[1].rules.map((r) => r.transitionKey)).toEqual([
      'route_a',
      'route_b',
    ]);
  });

  it('shows an errored visited route as Checked with its problem, using the rule’s own key', () => {
    const rows = simulationRows([
      line('gate', 'Checked', 'route_a', 1, 'The Amount source has no value yet.'),
      line('gate', 'Skipped', 'route_b', 2),
      t('gate', 'Attention', 'The Amount source has no value yet.'),
    ]);
    const block = rows[0];
    expect(block.kind).toBe('rules');
    if (block.kind !== 'rules') return;
    expect(block.rules[0]).toEqual({
      transitionKey: 'route_a',
      status: 'Checked',
      problem: 'The Amount source has no value yet.',
    });
    expect(block.rules[1]).toMatchObject({
      transitionKey: 'route_b',
      status: 'Skipped',
      problem: null,
    });
    expect(rows[1].kind).toBe('step');
  });
});
