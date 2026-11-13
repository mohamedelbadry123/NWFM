import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowRoutingEditorComponent } from './workflow-routing-editor.component';
import { CanvasNode, CanvasEdge } from './workflow-designer.component';
import { routingDefaults } from './workflow-routing.service';
import { WorkflowRoutingGroupComponent } from '../workspace/workflow-routing-group.component';
import { By } from '@angular/platform-browser';

describe('Guided routing editor', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [WorkflowRoutingEditorComponent],
      providers: [{ provide: LocaleService, useValue: { isRtl: () => false, locale: () => 'en' } }],
    }),
  );
  function setup(type: CanvasNode['type'] = 'ExclusiveGateway') {
    const fixture = TestBed.createComponent(WorkflowRoutingEditorComponent);
    const node = {
      id: 'condition',
      nodeKey: 'condition',
      name: 'Condition',
      type,
      configurationJson: JSON.stringify({ routing: routingDefaults() }),
    } as CanvasNode;
    fixture.componentRef.setInput('node', node);
    fixture.componentRef.setInput('nodes', [
      node,
      {
        id: 'destination',
        nodeKey: 'destination',
        name: 'Next Step',
        type: 'UserTask',
      } as CanvasNode,
    ]);
    fixture.componentRef.setInput('edges', []);
    fixture.detectChanges();
    return { fixture, editor: fixture.componentInstance };
  }
  it('preserves an unfinished rule as a draft without inventing an Always route', () => {
    const { editor } = setup();
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    editor.addRule();
    const draft = changed.mock.lastCall![0];
    expect(JSON.parse(draft.configuration).routing.routes[0].always).toBe(false);
    expect(draft.edges).toEqual([]);
    editor.config.routes[0].always = true;
    editor.routeTarget(editor.config.routes[0], 'destination');
    const saved = changed.mock.lastCall![0];
    expect(saved.edges).toHaveLength(1);
    expect(saved.edges[0].conditionExpression).toBe('');
    expect(saved.edges[0].labelEn).toBe('Always');
  });
  it('keeps rule destinations and selected field schema together across reopening', () => {
    const { fixture, editor } = setup();
    fixture.componentRef.setInput('context', {
      sources: [
        {
          key: 'registration.amount',
          nodeKey: 'registration',
          field: 'amount',
          type: 'numeric',
          label: 'Amount',
          formId: 'form',
          formVersion: 2,
        },
      ],
      nodes: [],
      connections: [],
      enabled: true,
      revision: 'r1',
    });
    fixture.detectChanges();
    editor.addRule();
    editor.config.routes[0].when.conditions.push({
      field: 'registration.amount',
      operator: 'greater_than',
      value: 100,
    });
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    editor.routeTarget(editor.config.routes[0], 'destination');
    const saved = changed.mock.lastCall![0];
    expect(JSON.parse(saved.configuration).routing.sources[0].formVersion).toBe(2);
    fixture.componentRef.setInput('node', {
      ...editor.node,
      configurationJson: saved.configuration,
    });
    fixture.componentRef.setInput('edges', saved.edges as CanvasEdge[]);
    fixture.detectChanges();
    expect(editor.target(editor.config.routes[0].transitionKey)).toBe('destination');
    expect(editor.describe(editor.config.routes[0].when)).toContain('Amount > 100');
    editor.removeRule(0);
    expect(changed.mock.lastCall![0].edges).toEqual([]);
  });
  it('separates join ALL/ANY from rule AND/OR and never mutates a published graph', () => {
    const { fixture, editor } = setup('JoinGateway');
    expect(fixture.nativeElement.textContent).toContain('ALL / ANY');
    expect(fixture.nativeElement.textContent).not.toContain('Add route');
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    fixture.componentRef.setInput('readonly', true);
    fixture.detectChanges();
    editor.setContinuation('destination');
    expect(changed).not.toHaveBeenCalled();
  });

  it('emits a source-only selection as an edit without requiring a field or a manual arrow', async () => {
    const { fixture, editor } = setup();
    fixture.componentRef.setInput('context', {
      sources: [
        {
          key: 'registration.name',
          nodeKey: 'registration',
          field: 'name',
          type: 'text',
          label: 'Registration → Customer name',
        },
      ],
      nodes: [],
      connections: [],
      enabled: true,
      revision: 'r1',
    });
    fixture.detectChanges();
    editor.addRule();
    editor.config.routes[0].when.conditions.push({
      field: '',
      operator: 'is_not_empty',
      value: null,
    });
    fixture.detectChanges();
    await fixture.whenStable();
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    const group = fixture.debugElement.query(By.directive(WorkflowRoutingGroupComponent));
    const source = group.nativeElement.querySelector('label select') as HTMLSelectElement;
    source.value = 'registration';
    source.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(changed).toHaveBeenCalledTimes(1);
    expect(changed.mock.lastCall![0].nodeId).toBe('condition');
    expect(
      JSON.parse(changed.mock.lastCall![0].configuration).routing.routes[0].when.conditions[0]
        .field,
    ).toBe('registration.');
  });

  it('rejects a destination that would loop back through the incoming source', () => {
    const { fixture, editor } = setup();
    fixture.componentRef.setInput('edges', [
      { id: 'incoming', fromNodeId: 'destination', toNodeId: 'condition' } as CanvasEdge,
    ]);
    fixture.detectChanges();
    editor.addRule();
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    editor.routeTarget(editor.config.routes[0], 'destination');
    fixture.detectChanges();
    expect(changed).not.toHaveBeenCalled();
    expect(editor.localEdges).toEqual([]);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('cycle');
  });

  it('keeps exactly one Join continuation when changing its destination', () => {
    const { editor } = setup('JoinGateway');
    const changed = vi.fn();
    editor.changed.subscribe(changed);
    editor.setContinuation('destination');
    const first = structuredClone(changed.mock.lastCall![0].edges);
    editor.setContinuation('destination');
    expect(changed.mock.lastCall![0].edges).toEqual(first);
    editor.setContinuation('');
    expect(changed.mock.lastCall![0].edges).toEqual([]);
  });

  it.each(['destination', 'otherwise', 'otherwise stop'])(
    'restores the visible %s selection when a user chooses a cyclic route',
    async (selection) => {
      const { fixture, editor } = setup();
      fixture.componentRef.setInput('nodes', [
        ...editor.nodes,
        {
          id: 'previous',
          nodeKey: 'previous',
          name: 'Previous Step',
          type: 'UserTask',
        } as CanvasNode,
      ]);
      fixture.componentRef.setInput('edges', [
        { id: 'incoming', fromNodeId: 'previous', toNodeId: 'condition' } as CanvasEdge,
      ]);
      fixture.detectChanges();
      editor.addRule();
      const rule = editor.config.routes[0];
      if (selection === 'destination') editor.routeTarget(rule, 'destination');
      if (selection === 'otherwise') editor.fallback('destination');
      fixture.detectChanges();
      await fixture.whenStable();
      const key = selection === 'destination' ? rule.transitionKey : 'otherwise';
      const select = fixture.nativeElement.querySelector(
        `[data-routing-field="${key}"]`,
      ) as HTMLSelectElement;
      const stored = selection === 'otherwise stop' ? '' : 'destination';
      expect(select.value).toBe(stored);
      const before = structuredClone(editor.localEdges);
      const changed = vi.fn();
      editor.changed.subscribe(changed);
      select.value = 'previous';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      await fixture.whenStable();
      expect(select.value).toBe(stored);
      expect(editor.localEdges).toEqual(before);
      expect(changed).not.toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('cycle');
      // A subsequent valid selection still saves normally after the rejected edit.
      select.value = selection === 'otherwise stop' ? 'destination' : '';
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      await fixture.whenStable();
      expect(changed).toHaveBeenCalledTimes(1);
      expect(editor.destinationProblem).toBe('');
    },
  );

  it('clears a rejected destination message when the editor switches nodes', () => {
    const { fixture, editor } = setup();
    editor.addRule();
    editor.routeTarget(editor.config.routes[0], 'condition');
    expect(editor.destinationProblem).toContain('cycle');
    fixture.componentRef.setInput('node', {
      ...editor.node,
      id: 'another-condition',
      nodeKey: 'another-condition',
    });
    fixture.detectChanges();
    expect(editor.destinationProblem).toBe('');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it.each([false, true])(
    'does not invite source edits in a read-only editor (suppressed: %s)',
    (suppressed) => {
      const { fixture, editor } = setup();
      editor.addRule();
      editor.config.routes[0].when.conditions.push({
        field: 'destination.name',
        operator: 'is_not_empty',
        value: null,
      });
      fixture.componentRef.setInput('node', {
        ...editor.node,
        automaticConditionSourceSuppressed: suppressed,
        configurationJson: JSON.stringify({ routing: editor.config }),
      });
      fixture.componentRef.setInput('readonly', true);
      fixture.detectChanges();
      expect(editor.sourceConnectionMessage()).toBe('');
      expect(
        fixture.nativeElement.querySelector('[data-routing-action="reconnect-source"]'),
      ).toBeNull();
    },
  );
});
