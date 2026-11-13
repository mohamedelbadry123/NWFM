import {
  WorkflowRoutingService,
  newConditionRouting,
  routingDefaults,
} from './workflow-routing.service';
import {
  RoutingEditorChange,
  WorkflowRoutingEditorComponent,
} from './workflow-routing-editor.component';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom, of, Subject, throwError } from 'rxjs';

import {
  buildNWFMXml,
  CanvasNode,
  CanvasEdge,
  NodeType,
  WorkflowDesignerComponent,
} from './workflow-designer.component';
import { Component, WritableSignal } from '@angular/core';
import { page as testPage } from 'vitest/browser';
import { FormGroup } from '@angular/forms';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { WorkflowActionsCatalogService } from '../workflow-actions-catalog.service';
import { WorkflowBindingsService } from '../workflow-bindings.service';
import { WorkflowSlaPoliciesService } from '../workflow-sla-policies.service';
import { ToastService } from '@core/notifications/toast.service';
import { ThemeService } from '@core/theme/theme.service';
import { WorkflowWorkspaceService } from '../workspace/workflow-workspace.service';
import { LocaleService } from '@core/i18n/locale.service';
import { AuthStore } from '@core/auth/auth.store';
import { WorkflowEmbeddedFormEditorComponent } from '../workspace/workflow-embedded-form-editor.component';
import { FormBuilderComponent } from '../../form-engine/builder/form-builder.component';
import { TaskTypesService } from '@core/tasks/task-types.service';
import type { TaskType } from '@core/tasks/tasks.models';
import type { ApiResult } from '@core/api/api-result';

/**
 * Soft acceptance for §36 designer scale: 100 and 200 nodes must add
 * under a generous budget so regressions (O(n²) canvas work) fail CI.
 */
describe('WorkflowDesignerComponent scale', () => {
  let fixture: ComponentFixture<WorkflowDesignerComponent>;
  let component: WorkflowDesignerComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkflowDesignerComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (k: string) =>
                  k === 'definitionId'
                    ? '00000000-0000-0000-0000-000000000001'
                    : '00000000-0000-0000-0000-000000000002',
              },
            },
            paramMap: of({
              get: (k: string) =>
                k === 'definitionId'
                  ? '00000000-0000-0000-0000-000000000001'
                  : '00000000-0000-0000-0000-000000000002',
            }),
          },
        },
        {
          provide: WorkflowVersionsService,
          useValue: {
            getById: () =>
              of({
                id: 'v1',
                status: 'Draft',
                versionNumber: 1,
                xmlContent:
                  '<Workflow xmlns="https://privora.io/workflow/v1"><Activities/><Transitions/></Workflow>',
              }),
            saveXml: () => of({}),
            validate: () => of({ isValid: true, errors: [], warnings: [] }),
            publish: () => of({}),
            clone: () => of({}),
            retire: () => of({}),
            getPublishPreview: () => of({}),
          },
        },
        {
          provide: WorkflowDefinitionsService,
          useValue: { getById: () => of({ id: 'd1', name: 'Perf' }) },
        },
        { provide: WorkflowActionsCatalogService, useValue: { getAll: () => of([]) } },
        {
          provide: WorkflowBindingsService,
          useValue: { listByDefinition: () => of([]), listOrgGroups: () => of([]) },
        },
        {
          provide: WorkflowSlaPoliciesService,
          useValue: { getAll: () => of([]), getPaged: () => of({ items: [] }) },
        },
        { provide: ToastService, useValue: { success: () => undefined, error: () => undefined } },
        { provide: ThemeService, useValue: { isDark: () => false } },
        {
          provide: LocaleService,
          useValue: { current: () => 'en', locale: () => 'en', isRtl: () => false },
        },
        {
          provide: WorkflowWorkspaceService,
          useValue: {
            references: () => of([]),
            children: () => of([]),
            openChild: vi.fn(() =>
              of({
                id: 'child',
                versionId: 'child-v1',
                name: 'Meter steps',
                definitionKey: 'meter-child',
              }),
            ),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(WorkflowDesignerComponent);
    component = fixture.componentInstance;
    const c = component as unknown as {
      isLoading: { set: (v: boolean) => void };
      version: { set: (v: { id: string; status: string; versionNumber: number }) => void };
    };
    c.isLoading.set(false);
    c.version.set({ id: 'v1', status: 'Draft', versionNumber: 1 });
  });

  function fillNodes(count: number): void {
    const add = (
      component as unknown as { addNode: (t: string, x: number, y: number) => void }
    ).addNode.bind(component);
    for (let i = 0; i < count; i++) {
      add('UserTask', 40 + (i % 20) * 80, 40 + Math.floor(i / 20) * 70);
    }
  }

  function draftEditor() {
    fixture.detectChanges();
    return component as unknown as {
      nodes: WritableSignal<CanvasNode[]>;
      edges: WritableSignal<CanvasEdge[]>;
      propsForm: FormGroup;
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      version: WritableSignal<{ id: string; status: string; versionNumber: number }>;
      selectedNodeId: WritableSignal<string | null>;
      selectedEdgeId: WritableSignal<string | null>;
      saveStatus(): string;
      addNode(type: NodeType, x?: number, y?: number): void;
      onNodeClick(event: Event, id: string): void;
      onEdgeClick(event: Event, id: string): void;
      saveToBackend(): import('rxjs').Observable<unknown>;
      undo(): void;
    };
  }

  function conditionEditor(sourceType: NodeType = 'UserTask') {
    const c = draftEditor() as ReturnType<typeof draftEditor> & {
      applyRoutingChange(change: RoutingEditorChange): void;
      deleteEdge(id: string): void;
      edgeForm: FormGroup;
      redo(): void;
      mergeWithBackend(
        state: unknown,
        version: unknown,
      ): { nodes: CanvasNode[]; edges: CanvasEdge[] };
    };
    c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
    c.addNode(sourceType, 100, 100);
    const source = c.nodes().find((node) => node.id === c.selectedNodeId())!;
    c.addNode('UserTask', 700, 100);
    const destination = c.nodes().find((node) => node.id === c.selectedNodeId())!;
    c.addNode('ExclusiveGateway', 400, 100);
    const condition = c.nodes().find((node) => node.id === c.selectedNodeId())!;
    c.nodes.update((nodes) =>
      nodes.map((node) =>
        node.id === condition.id
          ? {
              ...node,
              configurationJson: JSON.stringify({
                routing: {
                  ...routingDefaults(),
                  routes: [
                    {
                      transitionKey: 'route_saved',
                      always: false,
                      when: {
                        match: 'All',
                        conditions: [{ field: `${source.nodeKey}.name`, operator: 'is_not_empty' }],
                        groups: [],
                      },
                    },
                  ],
                },
              }),
            }
          : node,
      ),
    );
    const editorFixture = TestBed.createComponent(WorkflowRoutingEditorComponent);
    const editor = editorFixture.componentInstance;
    const sync = () => {
      editorFixture.componentRef.setInput(
        'node',
        c.nodes().find((node) => node.id === condition.id),
      );
      editorFixture.componentRef.setInput('nodes', c.nodes());
      editorFixture.componentRef.setInput('edges', c.edges());
      editorFixture.detectChanges();
    };
    editor.changed.subscribe((change) => c.applyRoutingChange(change));
    sync();
    return { c, source, destination, condition, editor, editorFixture, sync };
  }

  function connector(from: string, to: string): CanvasEdge {
    return {
      id: crypto.randomUUID(),
      transitionKey: `route_${crypto.randomUUID()}`,
      fromNodeId: from,
      toNodeId: to,
      labelEn: '',
      labelAr: '',
      descriptionEn: '',
      outcomeKey: '',
      conditionExpression: '',
      isDefault: false,
      priority: 0,
    };
  }

  it('offers a one-click link for a saved Condition configured before automatic source linking', () => {
    const { c, source, condition, editorFixture } = conditionEditor();
    expect(c.edges()).toEqual([]);
    const button = editorFixture.nativeElement.querySelector(
      '[data-routing-action="reconnect-source"]',
    ) as HTMLButtonElement;
    expect(button.textContent).toContain('Connect source');
    button.click();
    expect(c.edges()).toEqual([
      expect.objectContaining({ fromNodeId: source.id, toNodeId: condition.id }),
    ]);
  });

  it.each(['UserTask', 'ServiceTask'] as NodeType[])(
    'connects a saved disconnected Condition on an edit for a %s source, with one undo and redo',
    (type) => {
      const { c, source, destination, condition, editor, sync } = conditionEditor(type);
      const before = structuredClone(c.nodes());
      expect(c.edges()).toEqual([]); // Opening a saved rule does not mutate execution.
      editor.routeTarget(editor.config.routes[0], destination.id);
      expect(c.edges().map((edge) => [edge.fromNodeId, edge.toNodeId])).toEqual([
        [condition.id, destination.id],
        [source.id, condition.id],
      ]);
      const saved = structuredClone(c.edges());
      expect(c.nodes()).toEqual(before);
      expect(c.selectedNodeId()).toBe(condition.id);
      expect(c.saveStatus()).toBe('unsaved');
      c.undo();
      expect(c.edges()).toEqual([]);
      c.redo();
      expect(c.edges()).toEqual(saved);
      sync();
      editor.save();
      expect(c.edges()).toHaveLength(saved.length);
      expect(c.edges()).toEqual(expect.arrayContaining(saved)); // No second continuation.
    },
  );

  describe('ordered routing in the designer', () => {
    const configOf = (c: { nodes(): CanvasNode[] }, id: string) =>
      JSON.parse(c.nodes().find((node) => node.id === id)!.configurationJson).routing;
    const badges = () =>
      Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<SVGGElement>('[data-edge-order]'),
      ).map((badge) => ({
        text: badge.getAttribute('data-edge-order'),
        edge: badge.closest('[data-edge-id]')!.getAttribute('data-edge-id'),
      }));

    it('creates a new Condition on the ordered contract and keeps Fork and Join on theirs', () => {
      const c = draftEditor();
      c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
      c.addNode('ExclusiveGateway', 100, 100);
      const condition = c.selectedNodeId()!;
      expect(configOf(c, condition)).toMatchObject({
        contractVersion: 2,
        matchMode: 'First',
        routes: [],
      });
      c.addNode('ParallelGateway', 300, 100);
      expect(configOf(c, c.selectedNodeId()!)).toMatchObject({
        contractVersion: 1,
        matchMode: 'All',
      });
      const join = c.nodes().find((node) => node.type === 'JoinGateway')!;
      expect(configOf(c, join.id)).toMatchObject({ contractVersion: 1, matchMode: 'Unique' });
    });

    it('does not upgrade a saved Condition by opening it, selecting it or editing its destination', () => {
      const { c, condition, destination, editor, sync } = conditionEditor();
      // A configuration serialized before ordered routing: no contract version at all.
      c.nodes.update((nodes) =>
        nodes.map((node) =>
          node.id === condition.id
            ? {
                ...node,
                configurationJson: JSON.stringify({
                  routing: {
                    matchMode: 'Unique',
                    routes: [
                      {
                        transitionKey: 'route_saved',
                        always: true,
                        when: { match: 'All', conditions: [], groups: [] },
                      },
                    ],
                    sources: [],
                    waitFor: 'All',
                    remainingBranches: 'Continue',
                  },
                }),
              }
            : node,
        ),
      );
      const before = c.nodes().find((node) => node.id === condition.id)!.configurationJson;
      sync();
      expect(editor.config.contractVersion).toBe(1);
      expect(c.nodes().find((node) => node.id === condition.id)!.configurationJson).toBe(before);
      editor.routeTarget(editor.config.routes[0], destination.id);
      expect(configOf(c, condition.id)).toMatchObject({ contractVersion: 1, matchMode: 'Unique' });
    });

    it('numbers the canvas connections of an ordered Condition and follows add, reorder, undo and redo without touching labels', () => {
      const { c, destination, condition, editor, sync } = conditionEditor();
      c.nodes.update((nodes) =>
        nodes.map((node) =>
          node.id === condition.id
            ? {
                ...node,
                configurationJson: JSON.stringify({
                  routing: {
                    ...newConditionRouting(),
                    routes: [
                      {
                        transitionKey: 'route_one',
                        always: true,
                        when: { match: 'All', conditions: [], groups: [] },
                      },
                      {
                        transitionKey: 'route_two',
                        always: true,
                        when: { match: 'All', conditions: [], groups: [] },
                      },
                    ],
                  },
                }),
              }
            : node,
        ),
      );
      const other = {
        ...destination,
        id: 'other',
        nodeKey: 'other',
        name: 'Other Step',
        x: 700,
        y: 300,
      };
      c.nodes.update((nodes) => [...nodes, other]);
      sync();
      editor.routeTarget(editor.config.routes[0], destination.id);
      sync();
      fixture.detectChanges();
      const edgeOf = (key: string) => c.edges().find((edge) => edge.transitionKey === key)!;
      // Only the first route has an arrow yet, and it carries its own number.
      expect(badges()).toEqual([{ text: '1', edge: edgeOf('route_one').id }]);

      editor.routeTarget(editor.config.routes[1], other.id);
      sync();
      fixture.detectChanges();
      expect(badges()).toEqual(
        expect.arrayContaining([
          { text: '1', edge: edgeOf('route_one').id },
          { text: '2', edge: edgeOf('route_two').id },
        ]),
      );
      const labels = c.edges().map((edge) => [edge.id, edge.labelEn]);

      editor.move(0, 1);
      sync();
      fixture.detectChanges();
      expect(badges()).toEqual(
        expect.arrayContaining([
          { text: '2', edge: edgeOf('route_one').id },
          { text: '1', edge: edgeOf('route_two').id },
        ]),
      );
      // The connections keep their identity and their own labels: a badge is never part of a label.
      expect(c.edges().map((edge) => [edge.id, edge.labelEn])).toEqual(labels);
      expect(c.edges().every((edge) => !/^\d/.test(edge.labelEn))).toBe(true);

      c.undo();
      fixture.detectChanges();
      expect(badges()).toEqual(
        expect.arrayContaining([
          { text: '1', edge: edgeOf('route_one').id },
          { text: '2', edge: edgeOf('route_two').id },
        ]),
      );
      c.redo();
      fixture.detectChanges();
      expect(badges()).toEqual(
        expect.arrayContaining([
          { text: '2', edge: edgeOf('route_one').id },
          { text: '1', edge: edgeOf('route_two').id },
        ]),
      );

      // The route editor reloads the node after an undo or redo, then removing the first route renumbers the rest.
      sync();
      editor.removeRule(0);
      sync();
      fixture.detectChanges();
      expect(badges()).toEqual([{ text: '1', edge: edgeOf('route_one').id }]);
    });

    it('shows no route numbers for a Condition that keeps the legacy exactly-one policy', () => {
      const { c, destination, editor, sync } = conditionEditor();
      editor.routeTarget(editor.config.routes[0], destination.id);
      sync();
      fixture.detectChanges();
      expect(editor.config.matchMode).toBe('Unique');
      expect(badges()).toEqual([]);
      expect(c.edges().length).toBeGreaterThan(0);
    });
  });

  it('updates destinations and Otherwise targets, and removes only the fallback when stopping', () => {
    const { c, source, destination, condition, editor, sync } = conditionEditor();
    editor.routeTarget(editor.config.routes[0], destination.id);
    sync();
    const incoming = c.edges().find((edge) => edge.toNodeId === condition.id)!;
    const other = { ...destination, id: 'other', nodeKey: 'other', name: 'Other Step' };
    c.nodes.update((nodes) => [...nodes, other]);
    sync();
    editor.routeTarget(editor.config.routes[0], other.id);
    sync();
    expect(
      c
        .edges()
        .filter((edge) => edge.fromNodeId === condition.id)
        .map((edge) => edge.toNodeId),
    ).toEqual([other.id]);
    editor.fallback(destination.id);
    sync();
    const fallbackKey = editor.config.fallbackTransitionKey;
    editor.fallback(other.id);
    sync();
    expect(c.edges().filter((edge) => edge.transitionKey === fallbackKey)).toHaveLength(1);
    editor.fallback('');
    sync();
    expect(editor.config.fallbackTransitionKey).toBeNull();
    expect(c.edges()).toEqual([
      incoming,
      expect.objectContaining({ fromNodeId: condition.id, toNodeId: other.id }),
    ]);
    editor.routeTarget(editor.config.routes[0], '');
    expect(c.edges()).toEqual([incoming]);
    expect(incoming.fromNodeId).toBe(source.id);
  });

  it('replaces only an owned source connection and preserves ownership through save and ID remapping', async () => {
    const { c, source, destination, condition, editor, sync } = conditionEditor();
    const save = vi.spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml');
    editor.routeTarget(editor.config.routes[0], destination.id);
    sync();
    const first = structuredClone(c.edges());
    await firstValueFrom(c.saveToBackend());
    const persisted = JSON.parse(save.mock.lastCall![3]!);
    const owned = persisted.edges.find((edge: CanvasEdge) => edge.toNodeId === condition.id);
    expect(owned.automaticConditionSource).toEqual({
      conditionNodeKey: condition.nodeKey,
      sourceNodeKey: source.nodeKey,
    });
    expect(save.mock.lastCall![2]).not.toContain('automaticConditionSource');
    const remapped = c.mergeWithBackend(persisted, {
      status: 'Draft',
      activities: persisted.nodes.map((node: CanvasNode) => ({
        id: `saved-${node.id}`,
        nodeKey: node.nodeKey,
      })),
    });
    expect(remapped.edges.find((edge) => edge.id === owned.id)).toMatchObject({
      fromNodeId: `saved-${source.id}`,
      toNodeId: `saved-${condition.id}`,
      automaticConditionSource: owned.automaticConditionSource,
    });
    const next = { ...source, id: 'new-source', nodeKey: 'new-source' };
    c.nodes.update((nodes) => [...nodes, next]);
    sync();
    editor.config.routes[0].when.conditions[0].field = 'new-source.';
    editor.save();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toEqual([
      expect.objectContaining({ fromNodeId: next.id }),
    ]);
    expect(c.edges().find((edge) => edge.fromNodeId === condition.id)).toEqual({
      ...first.find((edge) => edge.fromNodeId === condition.id),
      labelEn: expect.any(String),
    });
    c.undo();
    expect(c.edges()).toEqual(first);
  });

  it('preserves manual source connections, including when the data source changes', () => {
    const { c, source, destination, condition, editor, sync } = conditionEditor();
    const manual = connector(source.id, condition.id);
    c.edges.set([manual]);
    sync();
    editor.save();
    expect(c.edges()).toEqual([manual]);
    sync();
    editor.config.routes[0].when.conditions[0].field = `${destination.nodeKey}.name`;
    editor.save();
    sync();
    expect(c.edges()).toEqual([manual]);
    expect(editor.sourceConnectionMessage()).toBe('');
  });

  it('keeps explicit connector deletion on reload and restores it with undo', () => {
    const { c, condition, editor, sync } = conditionEditor();
    editor.save();
    sync();
    const edge = c.edges()[0];
    c.deleteEdge(edge.id);
    sync();
    expect(c.edges()).toEqual([]);
    expect(editor.node.automaticConditionSourceSuppressed).toBe(true);
    editor.ngOnChanges();
    expect(c.edges()).toEqual([]);
    c.undo();
    sync();
    expect(c.edges()).toEqual([edge]);
    expect(editor.node.automaticConditionSourceSuppressed).toBeUndefined();
    expect(c.selectedNodeId()).toBe(condition.id);
    c.redo();
    sync();
    expect(c.edges()).toEqual([]);
    expect(editor.node.automaticConditionSourceSuppressed).toBe(true);
  });

  it('treats multiple fields and nested rules from the same Step as one source', () => {
    const { c, source, condition, editor } = conditionEditor();
    editor.config.routes[0].when.groups.push({
      match: 'Any',
      conditions: [{ field: `${source.nodeKey}.address`, operator: 'is_not_empty', value: null }],
      groups: [],
    });
    editor.save();
    expect(c.edges()).toHaveLength(1);
    expect(c.edges()[0]).toMatchObject({ fromNodeId: source.id, toNodeId: condition.id });
  });

  it('uses the Step as predecessor when a rule also reads workflow inputs', () => {
    const { c, source, condition, editor } = conditionEditor();
    editor.config.routes[0].when.groups.push({
      match: 'All',
      conditions: [{ field: '$input.priority', operator: 'equal', value: 'high' }],
      groups: [],
    });
    editor.save();
    expect(c.edges()).toHaveLength(1);
    expect(c.edges()[0]).toMatchObject({ fromNodeId: source.id, toNodeId: condition.id });
    expect(
      JSON.parse(c.nodes().find((node) => node.id === condition.id)!.configurationJson).routing
        .routes[0].when.groups[0].conditions[0].field,
    ).toBe('$input.priority');
  });

  it('explains the need for a manual execution predecessor for input-only rules', () => {
    const { c, editor, editorFixture, sync } = conditionEditor();
    editor.config.routes[0].when.conditions[0].field = '$input.priority';
    editor.save();
    sync();
    expect(c.edges()).toEqual([]);
    expect(editorFixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'Connect the preceding Step/Event manually',
    );
    expect(editor.sourceConnectionMessage()).not.toContain('not an available Step/Event');
  });

  it.each(['older Step', 'Join', '$input'])(
    'keeps a manual predecessor without source warnings when reading %s data',
    (dataSource) => {
      const { c, source, destination, condition, editor, sync } = conditionEditor();
      const manual = connector(destination.id, condition.id);
      c.edges.set([manual]);
      if (dataSource === 'Join')
        c.nodes.update((nodes) =>
          nodes.map((node) => (node.id === source.id ? { ...node, type: 'JoinGateway' } : node)),
        );
      sync();
      editor.config.routes[0].when.conditions[0].field =
        dataSource === '$input' ? '$input.priority' : `${source.nodeKey}.name`;
      editor.save();
      sync();
      expect(c.edges()).toEqual([manual]);
      expect(editor.sourceConnectionMessage()).toBe('');
    },
  );

  function pairedFork(seed: CanvasNode, key: string, type: NodeType = 'ParallelGateway') {
    const join = {
      ...seed,
      id: `${key}-join`,
      nodeKey: `${key}-join`,
      type: 'JoinGateway' as NodeType,
      configurationJson: '{}',
    };
    const fork = {
      ...seed,
      id: key,
      nodeKey: key,
      type,
      configurationJson: JSON.stringify({
        routing: { ...routingDefaults(), joinNodeKey: join.nodeKey },
      }),
    };
    return { fork, join };
  }

  it.each(['ParallelGateway', 'InclusiveGateway'] as NodeType[])(
    'allows auto-link after a completed %s group and before a downstream group',
    (type) => {
      const { c, source, destination, condition, editor, sync } = conditionEditor();
      const upstream = pairedFork(source, 'upstream', type);
      const downstream = pairedFork(source, 'downstream', type);
      const start = { ...source, id: 'start', nodeKey: 'start', type: 'Start' as NodeType };
      c.nodes.update((nodes) => [
        ...nodes,
        start,
        upstream.fork,
        upstream.join,
        downstream.fork,
        downstream.join,
      ]);
      const existing = [
        connector(start.id, upstream.fork.id),
        connector(upstream.fork.id, upstream.join.id),
        connector(upstream.join.id, source.id),
        connector(condition.id, downstream.fork.id),
        connector(downstream.fork.id, downstream.join.id),
        connector(downstream.join.id, destination.id),
      ];
      c.edges.set(existing);
      sync();
      editor.save();
      expect(c.edges()).toHaveLength(existing.length + 1);
      expect(c.edges()).toEqual(
        expect.arrayContaining([
          ...existing,
          expect.objectContaining({ fromNodeId: source.id, toNodeId: condition.id }),
        ]),
      );
    },
  );

  it.each(['inside inner', 'after inner', 'after outer'])(
    'respects nested Fork boundaries for a source %s Join',
    (position) => {
      const { c, source, condition, editor, sync } = conditionEditor();
      const outer = pairedFork(source, 'outer');
      const inner = pairedFork(source, 'inner', 'InclusiveGateway');
      c.nodes.update((nodes) => [...nodes, outer.fork, outer.join, inner.fork, inner.join]);
      const existing = [
        connector(outer.fork.id, inner.fork.id),
        connector(inner.fork.id, inner.join.id),
        connector(inner.join.id, outer.join.id),
        connector(
          position === 'inside inner'
            ? inner.fork.id
            : position === 'after inner'
              ? inner.join.id
              : outer.join.id,
          source.id,
        ),
      ];
      c.edges.set(existing);
      sync();
      editor.save();
      sync();
      if (position === 'after outer') {
        expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toEqual([
          expect.objectContaining({ fromNodeId: source.id }),
        ]);
        expect(editor.sourceConnectionMessage()).toBe('');
      } else {
        expect(c.edges()).toEqual(existing);
        expect(editor.sourceConnectionMessage()).toContain('Fork/Join');
      }
    },
  );

  it('leaves a disconnected Condition branch tail leading into a Join for manual wiring', () => {
    const { c, source, condition, editor, sync } = conditionEditor();
    const { fork, join } = pairedFork(source, 'parallel');
    c.nodes.update((nodes) => [...nodes, fork, join]);
    const existing = [connector(fork.id, join.id), connector(condition.id, join.id)];
    c.edges.set(existing);
    sync();
    editor.save();
    sync();
    expect(c.edges()).toHaveLength(existing.length);
    expect(c.edges()).toEqual(expect.arrayContaining(existing));
    expect(editor.sourceConnectionMessage()).toContain('Fork/Join');
  });

  it('persists deletion suppression across destination edits, Otherwise edits and reload, until explicit reconnect', async () => {
    const { c, destination, condition, editor, editorFixture, sync } = conditionEditor();
    const save = vi.spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml');
    editor.save();
    c.deleteEdge(c.edges()[0].id);
    sync();
    editor.routeTarget(editor.config.routes[0], destination.id);
    sync();
    editor.fallback(destination.id);
    sync();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toEqual([]);
    expect(editor.node.automaticConditionSourceSuppressed).toBe(true);
    await firstValueFrom(c.saveToBackend());
    const persisted = JSON.parse(save.mock.lastCall![3]!);
    expect(
      persisted.nodes.find((node: CanvasNode) => node.id === condition.id)
        .automaticConditionSourceSuppressed,
    ).toBe(true);
    expect(save.mock.lastCall![2]).not.toContain('automaticConditionSourceSuppressed');
    const reloaded = c.mergeWithBackend(persisted, {
      status: 'Draft',
      activities: persisted.nodes.map((node: CanvasNode) => ({
        id: node.id,
        nodeKey: node.nodeKey,
      })),
    });
    c.nodes.set(reloaded.nodes);
    c.edges.set(reloaded.edges);
    sync();
    editor.fallback('');
    sync();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toEqual([]);
    (
      editorFixture.nativeElement.querySelector(
        '[data-routing-action="reconnect-source"]',
      ) as HTMLButtonElement
    ).click();
    sync();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toHaveLength(1);
    expect(editor.node.automaticConditionSourceSuppressed).toBeUndefined();
    c.undo();
    sync();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toEqual([]);
    expect(editor.node.automaticConditionSourceSuppressed).toBe(true);
    c.redo();
    sync();
    expect(c.edges().filter((edge) => edge.toNodeId === condition.id)).toHaveLength(1);
    expect(editor.node.automaticConditionSourceSuppressed).toBeUndefined();
  });

  it('lifts deletion suppression only when the user selects a Source, not when editing its field or comparison', async () => {
    const { c, source, destination, condition, editor, editorFixture, sync } = conditionEditor();
    editor.save();
    c.deleteEdge(c.edges()[0].id);
    sync();
    editor.config.routes[0].when.conditions[0].field = `${source.nodeKey}.address`;
    editor.ruleChanged();
    sync();
    expect(c.edges()).toEqual([]);
    expect(editor.node.automaticConditionSourceSuppressed).toBe(true);
    editorFixture.componentRef.setInput('context', {
      sources: [source, destination].map((node) => ({
        key: `${node.nodeKey}.name`,
        nodeKey: node.nodeKey,
        field: 'name',
        type: 'text',
        label: `${node.name} → Name`,
      })),
      nodes: [],
      connections: [],
      enabled: true,
      revision: 'r1',
    });
    editorFixture.detectChanges();
    await editorFixture.whenStable();
    const sourceSelect = editorFixture.nativeElement.querySelector(
      'app-workflow-routing-group label select',
    ) as HTMLSelectElement;
    sourceSelect.value = destination.nodeKey;
    sourceSelect.dispatchEvent(new Event('change'));
    sync();
    expect(c.edges()).toEqual([
      expect.objectContaining({ fromNodeId: destination.id, toNodeId: condition.id }),
    ]);
    expect(editor.node.automaticConditionSourceSuppressed).toBeUndefined();
  });

  it('relinquishes automatic ownership when the user customizes the connector', () => {
    const { c, destination, condition, editor, sync } = conditionEditor();
    editor.save();
    const edge = c.edges()[0];
    c.onEdgeClick(new Event('click'), edge.id);
    c.edgeForm.patchValue({ labelEn: 'Manual route' });
    expect(c.edges()[0].automaticConditionSource).toBeUndefined();
    c.onNodeClick(new Event('click'), condition.id);
    sync();
    editor.config.routes[0].when.conditions[0].field = `${destination.nodeKey}.name`;
    editor.save();
    expect(c.edges()).toEqual([
      { ...edge, labelEn: 'Manual route', automaticConditionSource: undefined },
    ]);
  });

  it.each([
    'multiple sources',
    'source already routed',
    'cycle',
    'Fork',
    'missing source',
    'bound Event',
  ])('leaves connectors intact and explains the next action for %s', (scenario) => {
    const { c, source, destination, condition, editor, editorFixture, sync } = conditionEditor();
    if (scenario === 'source already routed') c.edges.set([connector(source.id, destination.id)]);
    if (scenario === 'cycle')
      c.edges.set([connector(condition.id, destination.id), connector(destination.id, source.id)]);
    if (scenario === 'Fork') {
      const gate = {
        ...destination,
        id: 'coordination',
        nodeKey: 'coordination',
        type: 'ParallelGateway' as NodeType,
      };
      c.nodes.update((nodes) => [...nodes, gate]);
      c.edges.set([connector(gate.id, source.id)]);
    }
    if (scenario === 'bound Event')
      c.nodes.update((nodes) =>
        nodes.map((node) =>
          node.id === source.id
            ? {
                ...node,
                type: 'ServiceTask',
                configurationJson: '{"triggerBinding":{"parentNodeKey":"parent"}}',
              }
            : node,
        ),
      );
    sync();
    if (scenario === 'multiple sources')
      editor.config.routes[0].when.groups.push({
        match: 'Any',
        conditions: [
          { field: `${destination.nodeKey}.name`, operator: 'is_not_empty', value: null },
        ],
        groups: [],
      });
    if (scenario === 'missing source')
      editor.config.routes[0].when.conditions[0].field = 'other-graph.name';
    const before = structuredClone(c.edges());
    editor.save();
    sync();
    expect(c.edges()).toHaveLength(before.length);
    expect(c.edges()).toEqual(expect.arrayContaining(before));
    expect(
      editorFixture.nativeElement.querySelector('[role="status"]')?.textContent?.trim(),
    ).toBeTruthy();
  });

  it('does not replace an owned edge when the new source is unsafe', () => {
    const { c, destination, editor, sync } = conditionEditor();
    editor.routeTarget(editor.config.routes[0], destination.id);
    sync();
    const before = structuredClone(c.edges());
    editor.config.routes[0].when.conditions[0].field = `${destination.nodeKey}.name`;
    editor.save();
    sync();
    expect(c.edges()).toHaveLength(before.length);
    expect(c.edges()).toEqual(
      expect.arrayContaining(before.map((edge) => ({ ...edge, labelEn: expect.any(String) }))),
    );
    expect(editor.sourceConnectionMessage()).toContain('cycle');
  });

  it('keeps typed step properties when switching steps without Apply or Save, and restores them on undo', () => {
    const c = draftEditor();
    c.addNode('UserTask');
    const firstId = c.selectedNodeId()!;
    c.propsForm.patchValue({
      name: 'Registration',
      nameAr: 'تسجيل الطلب',
      description: 'Capture request',
    });
    c.addNode('UserTask');
    const secondId = c.selectedNodeId()!;
    c.propsForm.patchValue({ name: 'Go to Location' });
    c.onNodeClick(new Event('click'), firstId);
    expect(c.propsForm.value.name).toBe('Registration');
    expect(JSON.parse(c.nodes().find((n) => n.id === firstId)!.configurationJson).description).toBe(
      'Capture request',
    );
    expect(c.nodes().find((n) => n.id === secondId)!.name).toBe('Go to Location');
    c.propsForm.patchValue({ name: 'Changed' });
    c.undo();
    expect(c.propsForm.value.name).toBe('Registration');
  });

  it('remembers guided sections per node without losing typed names or changing executable settings', () => {
    const c = draftEditor();
    c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
    const panel = component as unknown as {
      openPropertySection(section: string): void;
      propertySection(): string;
    };
    c.addNode('UserTask');
    const first = c.selectedNodeId()!;
    panel.openPropertySection('general');
    c.propsForm.patchValue({ name: 'Registration' });
    panel.openPropertySection('assignment');
    c.addNode('UserTask');
    const second = c.selectedNodeId()!;
    panel.openPropertySection('general');
    c.propsForm.patchValue({ name: 'Go to Location' });
    c.onNodeClick(new Event('click'), first);
    expect(panel.propertySection()).toBe('assignment');
    expect(c.propsForm.value.name).toBe('Registration');
    c.onNodeClick(new Event('click'), second);
    expect(panel.propertySection()).toBe('general');
    expect(c.propsForm.value.name).toBe('Go to Location');
    expect(c.nodes().every((n) => !n.configurationJson.includes('propertySection'))).toBe(true);
  });

  it('renders guided Properties within desktop and narrow viewports using the existing theme', async () => {
    const c = draftEditor();
    c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
    c.addNode('UserTask', 200, 160);
    c.propsForm.patchValue({ name: 'Registration' });
    (component as unknown as { openPropertySection(s: string): void }).openPropertySection(
      'general',
    );
    try {
      for (const width of [1440, 390]) {
        await testPage.viewport(width, 900);
        fixture.detectChanges();
        await fixture.whenStable();
        const panel = fixture.nativeElement.querySelector('.inspector-panel') as HTMLElement;
        const box = panel.getBoundingClientRect();
        expect(box.width).toBeLessThanOrEqual(width);
        expect(box.height).toBeLessThanOrEqual(900);
        // Comfortable on desktop, never wider than the screen on a phone.
        if (width >= 1280) {
          expect(box.width).toBeGreaterThanOrEqual(1000);
          expect(box.width).toBeLessThanOrEqual(1120);
        }
        const header = panel.querySelector('.properties-modal-header')!;
        expect(header.textContent).toContain('Registration');
        expect(header.textContent).not.toContain('usertask_');
        expect(panel.querySelector('.properties-heading')).not.toBeNull();
        // One primary scroll area: the body scrolls, the guided sections inside it do not.
        expect(getComputedStyle(panel.querySelector('.properties-modal-body')!).overflowY).toBe(
          'auto',
        );
        expect(getComputedStyle(panel.querySelector('.properties-scroll')!).overflowY).toBe(
          'visible',
        );
        await testPage.screenshot({ path: `__screenshots__/builder-properties-${width}.png` });
      }
    } finally {
      await testPage.viewport(1280, 900);
    }
  });

  describe('Properties modal', () => {
    type PropertiesEditor = ReturnType<typeof draftEditor> & {
      closeProperties(): void;
      inspectorVisible(): boolean;
      navigateToError(nodeKey: string, code?: string | null): void;
      zoom: WritableSignal<number>;
      panX: WritableSignal<number>;
      edgeForm: FormGroup;
      activeChild: WritableSignal<string | null>;
      childWindows: WritableSignal<{ nodeId: string; minimized: boolean; maximized?: boolean }[]>;
      openChildEditor(): void;
      minimizeChild(id: string): void;
      restoreChild(id: string): void;
      maximizeChild(id: string): void;
    };
    const editor = () => draftEditor() as unknown as PropertiesEditor;
    const el = () => fixture.nativeElement as HTMLElement;
    const dialog = (root: ParentNode = el()) =>
      root.querySelector<HTMLElement>(
        '.properties-overlay:not([hidden]) .properties-modal[role="dialog"]',
      );
    const key = (target: Element, name: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', {
        key: name,
        bubbles: true,
        cancelable: true,
        ...init,
      });
      target.dispatchEvent(event);
      return event;
    };
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const focusedNode = () =>
      (document.activeElement as HTMLElement | null)
        ?.closest('[data-node-id]')
        ?.getAttribute('data-node-id');

    it('opens as an accessible modal and closing keeps the selection, viewport and typed edits', async () => {
      const c = editor();
      c.addNode('UserTask', 200, 160);
      const id = c.selectedNodeId()!;
      c.propsForm.patchValue({ name: 'Registration' });
      c.zoom.set(1.3);
      c.panX.set(77);
      await settle();
      const open = dialog()!;
      expect(open).not.toBeNull();
      expect(open.getAttribute('aria-modal')).toBe('true');
      expect(el().querySelector(`#${open.getAttribute('aria-labelledby')}`)!.textContent).toContain(
        'Registration',
      );
      expect(el().querySelector('.designer-main')!.hasAttribute('inert')).toBe(true);
      await vi.waitFor(() =>
        expect(document.activeElement?.id).toBe(open.getAttribute('aria-labelledby')),
      );

      el().querySelector<HTMLButtonElement>('.properties-modal-header button')!.click();
      await settle();
      expect(dialog()).toBeNull();
      // Keep embedded editors mounted so their unapplied local drafts survive closing.
      expect(el().querySelector('.properties-modal')).toBe(open);
      expect(el().querySelector('.designer-main')!.hasAttribute('inert')).toBe(false);
      expect(c.selectedNodeId()).toBe(id);
      expect(c.nodes().find((n) => n.id === id)!.name).toBe('Registration');
      expect(c.propsForm.value.name).toBe('Registration');
      expect(c.zoom()).toBe(1.3);
      expect(c.panX()).toBe(77);
      // Focus goes back to the element being edited, not to the page.
      await vi.waitFor(() => expect(focusedNode()).toBe(id));

      c.onNodeClick(new Event('click'), id);
      await settle();
      expect(dialog()).toBe(open);
      expect(dialog()!.querySelector('.properties-modal-heading')!.textContent).toContain(
        'Registration',
      );
    });

    it('closes the topmost Properties on Escape without reaching the canvas behind it', async () => {
      const c = editor();
      c.addNode('UserTask', 200, 160);
      const id = c.selectedNodeId()!;
      await settle();
      const reachedDocument = vi.fn();
      document.addEventListener('keydown', reachedDocument);
      try {
        const escape = key(el().querySelector('.properties-modal-heading h2')!, 'Escape');
        expect(escape.defaultPrevented).toBe(true);
        expect(reachedDocument).not.toHaveBeenCalled();
      } finally {
        document.removeEventListener('keydown', reachedDocument);
      }
      await settle();
      expect(dialog()).toBeNull();
      // Escape closed Properties only; the element is still selected and present.
      expect(c.selectedNodeId()).toBe(id);
      expect(c.nodes().some((n) => n.id === id)).toBe(true);

      // Focus dropped to the page (a control vanished): Escape still closes a visible Properties.
      c.onNodeClick(new Event('click'), id);
      await settle();
      (document.activeElement as HTMLElement).blur();
      key(document.body, 'Escape');
      await settle();
      expect(dialog()).toBeNull();
      expect(c.selectedNodeId()).toBe(id);
    });

    it('keeps Delete, undo and copy shortcuts away from the canvas while Properties is open', async () => {
      const c = editor();
      c.addNode('UserTask', 200, 160);
      const id = c.selectedNodeId()!;
      c.propsForm.patchValue({ name: 'Registration' });
      await settle();
      const close = el().querySelector<HTMLButtonElement>('.properties-modal-header button')!;
      close.focus();
      key(close, 'Delete');
      key(close, 'Backspace');
      key(close, 'z', { ctrlKey: true });
      key(el().querySelector('.properties-modal-heading h2')!, 'a', { ctrlKey: true });
      expect(c.nodes().some((n) => n.id === id)).toBe(true);
      expect(c.nodes().find((n) => n.id === id)!.name).toBe('Registration');
      expect(c.inspectorVisible()).toBe(true);
      // Back on the canvas the same keys work again.
      c.closeProperties();
      await settle();
      key(document.querySelector<HTMLElement>(`[data-node-id="${id}"]`)!, 'Delete');
      expect(c.nodes().some((n) => n.id === id)).toBe(false);
    });

    it('closes from the backdrop only after a press that started there, and never drops edits', async () => {
      const c = editor();
      c.addNode('UserTask', 200, 160);
      c.propsForm.patchValue({ name: 'Registration' });
      await settle();
      const overlay = el().querySelector<HTMLElement>('.properties-overlay')!;
      const press = (target: Element) => {
        target.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      };
      // A text selection dragged out of a field ends on the backdrop but began inside the dialog.
      press(dialog()!);
      await settle();
      expect(dialog()).not.toBeNull();
      press(overlay);
      await settle();
      expect(dialog()).toBeNull();
      expect(c.nodes()[0].name).toBe('Registration');
      expect(c.propsForm.value.name).toBe('Registration');
    });

    it('reopens for a diagnostic, and retargets and refocuses when another element is chosen', async () => {
      const c = editor();
      c.addNode('UserTask', 100, 100);
      const first = c.nodes()[0];
      c.propsForm.patchValue({ name: 'Registration' });
      c.addNode('UserTask', 400, 100);
      c.propsForm.patchValue({ name: 'Inspection' });
      const second = c.nodes()[1];
      await settle();
      c.closeProperties();
      await settle();
      expect(dialog()).toBeNull();

      c.navigateToError(first.nodeKey, 'ACTIVITY_NAME_REQUIRED');
      await settle();
      expect(c.selectedNodeId()).toBe(first.id);
      expect(dialog()!.querySelector('.properties-modal-heading')!.textContent).toContain(
        'Registration',
      );
      expect(el().querySelector('.designer-main')!.hasAttribute('inert')).toBe(true);

      // Chosen while already open (for example from the review list inside the modal).
      c.navigateToError(second.nodeKey);
      await settle();
      expect(c.selectedNodeId()).toBe(second.id);
      expect(dialog()!.querySelector('.properties-modal-heading')!.textContent).toContain(
        'Inspection',
      );
      expect(el().querySelectorAll('.properties-modal')).toHaveLength(1);
    });

    it('edits a legacy connection in the modal and keeps the edit after closing', async () => {
      const c = editor();
      c.addNode('UserTask', 150, 200);
      const from = c.selectedNodeId()!;
      c.addNode('End', 500, 100);
      const to = c.selectedNodeId()!;
      c.edges.set([
        {
          id: 'edge',
          transitionKey: 'finish',
          fromNodeId: from,
          toNodeId: to,
          labelEn: '',
          labelAr: '',
          descriptionEn: '',
          outcomeKey: '',
          conditionExpression: '',
          isDefault: false,
          priority: 0,
        },
      ]);
      c.onEdgeClick(new Event('click'), 'edge');
      await settle();
      expect(c.selectedNodeId()).toBeNull();
      expect(dialog()!.querySelector('.properties-modal-heading')!.textContent).toContain(' → ');
      c.edgeForm.patchValue({ labelEn: 'Approved' });
      c.closeProperties();
      await settle();
      expect(dialog()).toBeNull();
      expect(c.selectedEdgeId()).toBe('edge');
      expect(c.edges()[0].labelEn).toBe('Approved');
      await vi.waitFor(() =>
        expect((document.activeElement as HTMLElement | null)?.getAttribute('data-edge-id')).toBe(
          'edge',
        ),
      );
    });

    it('keeps its state when the child workflow is maximized, minimized and restored, and Escape closes only the child Properties', async () => {
      const c = editor();
      c.workspace.set({ kind: 'Main', designerVersion: 3 });
      c.addNode('MainActivity');
      await settle();
      c.openChildEditor();
      await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
      await settle();
      const parentOverlay = el().querySelector<HTMLElement>(
        ':scope > .designer-shell > .properties-overlay',
      )!;
      expect(parentOverlay).not.toBeNull();
      // The parent's Properties stays open but is inert behind the child window.
      expect(parentOverlay.hasAttribute('inert')).toBe(true);

      const childDesigner = fixture.debugElement.query(By.directive(WorkflowDesignerComponent))
        .componentInstance as unknown as PropertiesEditor;
      childDesigner.addNode('UserTask', 200, 160);
      await settle();
      const childDialog = dialog(el().querySelector('.child-editor-window')!)!;
      expect(childDialog).not.toBeNull();

      const escape = key(childDialog.querySelector('h2')!, 'Escape');
      expect(escape.defaultPrevented).toBe(true);
      await settle();
      expect(dialog(el().querySelector('.child-editor-window')!)).toBeNull();
      expect(c.childWindows()).toHaveLength(1);
      expect(c.activeChild()).toBe(c.childWindows()[0].nodeId);
      expect(c.inspectorVisible()).toBe(true);
      expect(childDesigner.selectedNodeId()).not.toBeNull();

      const childId = c.childWindows()[0].nodeId;
      c.maximizeChild(childId);
      await settle();
      expect(c.inspectorVisible()).toBe(true);
      c.minimizeChild(childId);
      await settle();
      expect(c.childWindows()[0].minimized).toBe(true);
      expect(c.inspectorVisible()).toBe(true);
      expect(parentOverlay.hasAttribute('inert')).toBe(false);
      // The dock is behind the modal, so focus lands in the parent's Properties.
      await vi.waitFor(() => expect(parentOverlay.contains(document.activeElement)).toBe(true));
      c.restoreChild(childId);
      await settle();
      expect(c.inspectorVisible()).toBe(true);
      expect(c.childWindows()[0].minimized).toBe(false);
      // Escape in the parent's Properties closes it while the child workflow stays open.
      c.minimizeChild(childId);
      await settle();
      key(parentOverlay.querySelector('h2')!, 'Escape');
      await settle();
      expect(c.inspectorVisible()).toBe(false);
      expect(c.childWindows()).toHaveLength(1);
    });

    it('offers the Step Values editor as a guided section of a hierarchical Step and opens the real panel', async () => {
      const c = editor();
      c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
      c.addNode('UserTask', 200, 160);
      c.propsForm.patchValue({ name: 'Registration' });
      await settle();
      const id = c.selectedNodeId()!;
      const sections = () =>
        Array.from(
          el().querySelectorAll<HTMLElement>('app-workflow-property-section button[aria-controls]'),
        );
      const values = sections().find(
        (b) => b.getAttribute('aria-controls') === `property-${id}-values`,
      );
      expect(values).toBeDefined();
      expect(values!.textContent).toContain('Values');
      // Not rendered until the author opens it.
      expect(el().querySelector('app-workflow-step-mapping')).toBeNull();

      values!.click();
      await settle();
      expect(values!.getAttribute('aria-expanded')).toBe('true');
      const panel = el().querySelector<HTMLElement>('app-workflow-step-mapping');
      expect(panel).not.toBeNull();
      expect(panel!.querySelector('[aria-label="Step field values"]')).not.toBeNull();
      // Switching to another section and back keeps the node and its typed name.
      sections()
        .find((b) => b.getAttribute('aria-controls') === `property-${id}-general`)!
        .click();
      await settle();
      expect(el().querySelector('app-workflow-step-mapping')).toBeNull();
      expect(c.propsForm.value.name).toBe('Registration');
    });

    it('does not offer Step Values for a legacy (non-hierarchical) workflow or for a gateway', async () => {
      const c = editor();
      c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 2 });
      c.addNode('UserTask', 200, 160);
      await settle();
      const hasValues = () =>
        Array.from(
          el().querySelectorAll<HTMLElement>('app-workflow-property-section button[aria-controls]'),
        ).some((b) => b.getAttribute('aria-controls')!.endsWith('-values'));
      expect(hasValues()).toBe(false);
      c.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 4 });
      c.addNode('ServiceTask', 400, 160);
      await settle();
      expect(hasValues()).toBe(false);
    });

    it('keeps the child window controls and the dock reachable by a real pointer while an inspector is open', async () => {
      const c = editor();
      c.workspace.set({ kind: 'Main', designerVersion: 3 });
      c.addNode('MainActivity');
      await settle();
      c.openChildEditor();
      await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
      await settle();
      const childDesigner = fixture.debugElement.query(By.directive(WorkflowDesignerComponent))
        .componentInstance as unknown as PropertiesEditor;
      childDesigner.addNode('UserTask', 200, 160);
      await settle();
      // The child's own inspector is open over its canvas.
      expect(childDesigner.selectedNodeId()).not.toBeNull();
      const childWindow = el().querySelector<HTMLElement>('.child-editor-window')!;
      expect(dialog(childWindow)).not.toBeNull();

      // What a pointer lands on at the centre of each titlebar control is the control itself.
      const hit = (target: HTMLElement) => {
        const box = target.getBoundingClientRect();
        const top = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
        return top === target || target.contains(top);
      };
      const full = childWindow.querySelector<HTMLElement>('button[aria-label^="Full screen"]')!;
      const minimize = childWindow.querySelector<HTMLElement>('button[aria-label^="Minimize"]')!;
      const close = childWindow.querySelector<HTMLElement>('button[aria-label^="Close "]')!;
      expect(hit(full)).toBe(true);
      expect(hit(minimize)).toBe(true);
      expect(hit(close)).toBe(true);

      // A real click on Full screen maximizes it without losing the inspector selection.
      const selected = childDesigner.selectedNodeId();
      full.click();
      await settle();
      expect(c.childWindows()[0].maximized).toBe(true);
      expect(childDesigner.selectedNodeId()).toBe(selected);
      expect(hit(full)).toBe(true);

      // Minimized with the parent's inspector open, the dock sits above the parent's overlay.
      const childId = c.childWindows()[0].nodeId;
      c.minimizeChild(childId);
      await settle();
      expect(c.inspectorVisible()).toBe(true);
      const restore = el().querySelector<HTMLElement>(
        `.child-editor-dock-item[data-child-id="${childId}"]`,
      )!;
      expect(restore).not.toBeNull();
      expect(hit(restore)).toBe(true);
    });
  });

  it('automatically serializes saves and only reports Saved after edits made during a slow request persist', async () => {
    vi.useFakeTimers();
    try {
      const c = draftEditor();
      const first = new Subject<
        import('@shared/models/models/Workflow/Application/DTOs/workflow-version-dto').WorkflowVersionDto
      >();
      const second = new Subject<
        import('@shared/models/models/Workflow/Application/DTOs/workflow-version-dto').WorkflowVersionDto
      >();
      const save = vi
        .spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml')
        .mockReturnValueOnce(first)
        .mockReturnValueOnce(second);
      c.addNode('UserTask');
      c.propsForm.patchValue({ name: 'Registration' });
      await vi.advanceTimersByTimeAsync(801);
      expect(save).toHaveBeenCalledTimes(1);
      c.propsForm.patchValue({ name: 'Registration updated' });
      await vi.advanceTimersByTimeAsync(801);
      expect(save).toHaveBeenCalledTimes(1);
      first.next({});
      first.complete();
      await Promise.resolve();
      await Promise.resolve();
      expect(save).toHaveBeenCalledTimes(2);
      expect(save.mock.calls[1][2]).toContain('Registration updated');
      expect(c.saveStatus()).toBe('saving');
      second.next({});
      second.complete();
      await vi.advanceTimersByTimeAsync(0);
      expect(c.saveStatus()).toBe('saved');
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows autosave failure and recovers on the next edit without losing the draft', async () => {
    vi.useFakeTimers();
    try {
      const c = draftEditor();
      const save = vi
        .spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml')
        .mockReturnValueOnce(throwError(() => new Error('offline')))
        .mockReturnValue(of({}));
      c.addNode('UserTask');
      c.propsForm.patchValue({ name: 'Registration' });
      await vi.advanceTimersByTimeAsync(801);
      expect(c.saveStatus()).toBe('error');
      expect(c.nodes()[0].name).toBe('Registration');
      c.propsForm.patchValue({ description: 'Recovered' });
      await vi.advanceTimersByTimeAsync(801);
      expect(c.saveStatus()).toBe('saved');
      expect(save.mock.calls[1][2]).toContain('Registration');
      expect(save.mock.calls[1][2]).toContain('Recovered');
    } finally {
      vi.useRealTimers();
    }
  });

  it('saves the current typed name immediately when Save is requested before the debounce', async () => {
    const c = draftEditor();
    const save = vi.spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml');
    c.addNode('UserTask');
    c.propsForm.patchValue({ name: 'Registration' });
    await firstValueFrom(c.saveToBackend());
    expect(save.mock.calls[0][2]).toContain('name="Registration"');
    expect(c.saveStatus()).toBe('saved');
  });

  it('deletes a connection from the full SVG delete button and preserves the deletion through saving', async () => {
    const c = draftEditor();
    c.addNode('UserTask', 150, 200);
    const from = c.selectedNodeId()!;
    c.addNode('End', 500, 100);
    const to = c.selectedNodeId()!;
    c.edges.set([
      {
        id: 'edge',
        transitionKey: 'finish',
        fromNodeId: from,
        toNodeId: to,
        labelEn: '',
        labelAr: '',
        descriptionEn: '',
        outcomeKey: '',
        conditionExpression: '',
        isDefault: false,
        priority: 0,
      },
    ]);
    c.onEdgeClick(new Event('click'), 'edge');
    fixture.detectChanges();
    const circle = (fixture.nativeElement as HTMLElement).querySelector(
      'g[aria-label^="Delete connection"] circle',
    )!;
    circle.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(c.edges()).toHaveLength(0);
    expect(c.selectedEdgeId()).toBeNull();
    const save = vi.spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml');
    await firstValueFrom(c.saveToBackend());
    expect(save.mock.calls[0][2]).not.toContain('<Transition ');
  });

  it('keeps published versions read only when form controls emit changes', () => {
    const c = draftEditor();
    c.addNode('UserTask');
    const before = c.nodes();
    c.version.set({ id: 'v1', status: 'Published', versionNumber: 1 });
    c.propsForm.patchValue({ name: 'Unauthorized change' });
    expect(c.nodes()).toBe(before);
  });

  it('settles the rendered role picker after selecting multiple roles', async () => {
    vi.spyOn(TestBed.inject(WorkflowWorkspaceService), 'references').mockReturnValue(
      of([
        { id: 'reviewer', code: 'REVIEWER', nameEn: 'Reviewer', nameAr: 'Reviewer' },
        { id: 'operator', code: 'OPERATOR', nameEn: 'Operator', nameAr: 'Operator' },
      ]),
    );
    fixture.detectChanges();
    const c = component as unknown as {
      addNode(type: NodeType): void;
      inspectorTab: WritableSignal<string>;
      selectedAssignmentRoleIds(): string[];
      onAssignmentRolesChange(ids: string[]): void;
    };
    c.addNode('UserTask');
    c.onAssignmentRolesChange(['reviewer', 'operator']);
    c.inspectorTab.set('assignment');
    fixture.detectChanges();
    await fixture.whenStable();
    const selection = c.selectedAssignmentRoleIds();
    fixture.detectChanges();
    expect(c.selectedAssignmentRoleIds()).toBe(selection);
    expect(
      fixture.nativeElement.querySelector('app-workflow-role-assignment').textContent,
    ).toContain('Reviewer');
    expect(
      fixture.nativeElement.querySelector('app-workflow-role-assignment').textContent,
    ).toContain('Operator');
  });

  it('keeps the same child editing context through minimize, restore and repeated open', async () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      addNode(type: NodeType): void;
      openChildEditor(): void;
      minimizeChild(id: string): void;
      restoreChild(id: string): void;
      childWindows: WritableSignal<{ nodeId: string; minimized: boolean; inputs: object }[]>;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 3 });
    c.addNode('MainActivity');
    c.openChildEditor();
    await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
    const original = c.childWindows()[0];
    c.minimizeChild(original.nodeId);
    expect(c.childWindows()[0].minimized).toBe(true);
    c.restoreChild(original.nodeId);
    c.openChildEditor();
    expect(c.childWindows()).toHaveLength(1);
    expect(c.childWindows()[0].inputs).toBe(original.inputs);
    expect(c.childWindows()[0].minimized).toBe(false);
    expect(TestBed.inject(WorkflowWorkspaceService).openChild).toHaveBeenCalledTimes(1);
  });

  it('reports the parent save reason once and retains the canvas when child opening is blocked', async () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      nodes: WritableSignal<CanvasNode[]>;
      childBusy: WritableSignal<boolean>;
      saveStatus: WritableSignal<string>;
      addNode(type: NodeType): void;
      openChildEditor(): void;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 4 });
    c.addNode('MainActivity');
    const before = c.nodes();
    const message = 'Routing version 4 authoring is not enabled.';
    vi.spyOn(TestBed.inject(WorkflowVersionsService), 'saveXml').mockReturnValue(
      throwError(() => ({ status: 400, error: { code: 'Routing.Disabled', message } })),
    );
    const notify = vi.spyOn(TestBed.inject(ToastService), 'error');
    c.openChildEditor();
    await vi.waitFor(() => expect(c.childBusy()).toBe(false));
    c.openChildEditor();
    await vi.waitFor(() => expect(c.childBusy()).toBe(false));
    expect(TestBed.inject(WorkflowWorkspaceService).openChild).not.toHaveBeenCalled();
    expect(c.nodes()).toStrictEqual(before);
    expect(c.saveStatus()).toBe('error');
    expect(notify).toHaveBeenCalledExactlyOnceWith(message);
  });

  describe('form editor windows', () => {
    type FormWindow = {
      nodeId: string;
      minimized: boolean;
      inputs: { editorContext?: { classification?: unknown } };
      formContext?: {
        requestClose?: () => Promise<boolean>;
        use: (form: unknown) => void;
        classification?: unknown;
      };
    };
    type FormHost = {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      saveStatus: WritableSignal<string>;
      childWindows: WritableSignal<FormWindow[]>;
      canAuthorForms(): boolean;
      addNode(type: NodeType): void;
      openFormEditor(request: {
        mode: 'create' | 'edit' | 'duplicate';
        form?: unknown;
      }): Promise<void>;
      useFormInStep(nodeId: string, form: unknown): void;
      formRevision(nodeId: string): number;
      minimizeChild(id: string): void;
      restoreChild(id: string): void;
      closeChild(id: string): Promise<void>;
      closeProperties(): void;
    };
    const grant = (permissions: string[]) =>
      TestBed.inject(AuthStore).updateProfile({
        userId: 'u1',
        userName: 'designer',
        email: null,
        roles: [],
        permissions,
        teamId: null,
        isUnrestrictedScope: false,
      });
    const pinned = (formId: string, versionNo: number) =>
      JSON.stringify({
        taskTypeId: 'meter',
        formSteps: [
          {
            id: 's-' + formId,
            formId,
            versionNo,
            name: formId,
            rules: [],
            otherwise: { kind: 'Next' },
          },
        ],
      });
    function childStep() {
      fixture.detectChanges();
      const c = component as unknown as FormHost;
      c.workspace.set({ kind: 'Child', designerVersion: 3, taskTypeId: 'meter' });
      c.addNode('UserTask');
      return c;
    }

    it('needs both form and Task Type management; reading either is not enough', async () => {
      try {
        const c = childStep();
        for (const permissions of [
          [],
          ['ManageForms'],
          ['ManageTaskTypes'],
          ['ViewForms', 'ViewTasks'],
        ]) {
          grant(permissions);
          expect(c.canAuthorForms()).toBe(false);
          await c.openFormEditor({ mode: 'create' });
          expect(c.childWindows()).toHaveLength(0);
        }
        grant(['ManageForms', 'ManageTaskTypes']);
        expect(c.canAuthorForms()).toBe(true);
      } finally {
        TestBed.inject(AuthStore).clearSession();
      }
    });

    it('opens in the same window shell as a child workflow and keeps its state through minimize and restore', async () => {
      try {
        grant(['ManageForms', 'ManageTaskTypes']);
        const c = childStep();
        const steps = c.nodes();
        await c.openFormEditor({ mode: 'create' });
        await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
        fixture.detectChanges();
        const key = c.childWindows()[0].nodeId;
        expect(key).toBe('form:' + c.selectedNodeId());
        const window = fixture.nativeElement.querySelector('.child-editor-window') as HTMLElement;
        expect(window.getAttribute('aria-modal')).toBe('true');
        expect(window.getAttribute('aria-label')).toContain('form editor');
        expect(window.querySelector('[aria-label="Minimize form editor"]')).not.toBeNull();
        expect(window.querySelector('[aria-label="Close form editor"]')).not.toBeNull();
        const editor = fixture.debugElement.query(By.directive(WorkflowEmbeddedFormEditorComponent))
          .componentInstance as WorkflowEmbeddedFormEditorComponent;
        editor.nameEn.set('Draft name');

        c.minimizeChild(key);
        c.closeProperties();
        fixture.detectChanges();
        expect(window.style.display).toBe('none');
        const dock = fixture.nativeElement.querySelector('.child-editor-dock');
        expect(dock.getAttribute('aria-label')).toBe('Minimized editors');
        expect(dock.querySelector('[data-child-id]').getAttribute('data-child-id')).toBe(key);

        c.restoreChild(key);
        fixture.detectChanges();
        expect(window.style.display).toBe('flex');
        expect(
          fixture.debugElement.query(By.directive(WorkflowEmbeddedFormEditorComponent))
            .componentInstance,
        ).toBe(editor);
        expect(editor.nameEn()).toBe('Draft name');
        expect(c.nodes()).toBe(steps);
        // A second request for the same Step restores the open window rather than adding another.
        await c.openFormEditor({ mode: 'create' });
        expect(c.childWindows()).toHaveLength(1);
      } finally {
        TestBed.inject(AuthStore).clearSession();
      }
    });

    it('closes only when the editor agrees, so a kept or failed draft leaves the window open', async () => {
      try {
        grant(['ManageForms', 'ManageTaskTypes']);
        const c = childStep();
        await c.openFormEditor({ mode: 'create' });
        await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
        fixture.detectChanges();
        const context = c.childWindows()[0].formContext!;
        context.requestClose = vi.fn().mockResolvedValue(false);
        await c.closeChild(c.childWindows()[0].nodeId);
        expect(c.childWindows()).toHaveLength(1);
        context.requestClose = vi.fn().mockResolvedValue(true);
        await c.closeChild(c.childWindows()[0].nodeId);
        expect(c.childWindows()).toHaveLength(0);
      } finally {
        TestBed.inject(AuthStore).clearSession();
      }
    });

    it('pins the new version in the originating Step only and refreshes only that Step', () => {
      const c = childStep();
      c.addNode('UserTask');
      const [first, second] = c.nodes();
      c.nodes.update((nodes) =>
        nodes.map((node) => ({ ...node, configurationJson: pinned('shared', 1) })),
      );
      const untouched = c.nodes()[1].configurationJson;
      c.saveStatus.set('saved');

      c.useFormInStep(first.id, { formId: 'shared', versionNo: 2, name: 'Shared' });

      const config = JSON.parse(c.nodes()[0].configurationJson);
      expect(config.formSteps).toHaveLength(1);
      expect(config.formSteps[0]).toMatchObject({
        formId: 'shared',
        versionNo: 2,
        reviewRequired: true,
      });
      expect(c.nodes()[1].configurationJson).toBe(untouched);
      expect(c.formRevision(first.id)).toBe(1);
      expect(c.formRevision(second.id)).toBe(0);
      expect(c.saveStatus()).toBe('unsaved');
    });

    it('selects and refreshes the exact Step the window was opened from, even if another was selected meanwhile', () => {
      const c = childStep();
      c.addNode('UserTask');
      const [first, second] = c.nodes();
      expect(c.selectedNodeId()).toBe(second.id);
      c.useFormInStep(first.id, { formId: 'created', versionNo: 1, name: 'Created' });
      expect(c.selectedNodeId()).toBe(first.id);
      expect(JSON.parse(c.nodes()[1].configurationJson).formSteps).toBeUndefined();
      expect(c.formRevision(first.id)).toBe(1);
      expect(c.formRevision(second.id)).toBe(0);
    });
    it('replaces the Step’s form with a new one, resetting its rules, and keeps its Task Type', () => {
      const c = childStep();
      const [step] = c.nodes();
      c.useFormInStep(step.id, { formId: 'created', versionNo: 1, name: 'Created' });
      const config = JSON.parse(c.nodes()[0].configurationJson);
      expect(config.taskTypeId).toBe('meter');
      expect(config.formSteps.map((s: { formId: string }) => s.formId)).toEqual(['created']);
      expect(config.formSteps[0].rules).toEqual([]);
    });

    describe('leaving with unsaved form editors', () => {
      type Leave = FormHost & {
        editorContext?: Record<string, unknown>;
        activeChild: WritableSignal<string | null>;
        resolveLeave(): Promise<boolean>;
        canLeaveRoute(): Promise<boolean>;
        hasDirtyForms(): boolean;
        goBack(): Promise<void>;
        navigateContext(parent?: boolean): Promise<void>;
        save(): Promise<void>;
        onBeforeUnload(event: BeforeUnloadEvent): void;
        saveToBackend(): unknown;
        inheritedClassification(): unknown;
        openChildEditor(): void;
      };
      @Component({ selector: 'app-window-stub', standalone: true, template: '' })
      class WindowStub {}
      const router = () => TestBed.inject(Router);
      let saveSpy: { mock: { calls: unknown[] } };
      const wire = () => {
        const c = childStep() as unknown as Leave;
        saveSpy = vi
          .spyOn(c as unknown as { saveToBackend(): unknown }, 'saveToBackend')
          .mockReturnValue(of(null));
        return c;
      };
      const formWindow = (id: string, over: Record<string, unknown> = {}, minimized = true) => {
        const context = {
          mode: 'edit',
          taskTypeId: 'meter',
          stepName: 'Step',
          use: vi.fn(),
          close: vi.fn(),
          isDirty: vi.fn(() => true),
          requestClose: vi.fn().mockResolvedValue(true),
          saveDraft: vi.fn().mockResolvedValue(true),
          ...over,
        };
        return {
          nodeId: 'form:' + id,
          taskTypeId: 'meter',
          name: 'Step',
          minimized,
          maximized: false,
          component: WindowStub,
          formContext: context,
          inputs: { context },
        } as unknown as FormWindow;
      };
      const childWindow = (id: string, editorContext: Record<string, unknown>, minimized = true) =>
        ({
          nodeId: id,
          taskTypeId: 'meter',
          name: 'Child',
          minimized,
          maximized: false,
          component: WindowStub,
          inputs: { editorContext },
        }) as unknown as FormWindow;
      type Ctx = { requestClose: ReturnType<typeof vi.fn>; saveDraft: ReturnType<typeof vi.fn> };

      it('asks about a dirty minimized form before navigating, and Keep editing stays on the canvas', async () => {
        const c = wire();
        const navigate = vi.spyOn(router(), 'navigate').mockResolvedValue(true);
        const window = formWindow('a');
        c.childWindows.set([window]);
        const nodes = c.nodes();
        const selected = c.selectedNodeId();
        (window.formContext as unknown as Ctx).requestClose.mockResolvedValue(false);

        await c.goBack();
        await c.navigateContext(false);

        expect((window.formContext as unknown as Ctx).requestClose).toHaveBeenCalledTimes(2);
        expect(navigate).not.toHaveBeenCalled();
        // The window was brought forward for the question; the workflow canvas and selection are untouched.
        expect(c.childWindows()[0].minimized).toBe(false);
        expect(c.activeChild()).toBe('form:a');
        expect(c.nodes()).toBe(nodes);
        expect(c.selectedNodeId()).toBe(selected);
      });

      it('goes on after Save draft or Discard, saves the workflow and returns the window to the dock', async () => {
        const c = wire();
        const navigate = vi.spyOn(router(), 'navigate').mockResolvedValue(true);
        c.childWindows.set([formWindow('a')]);
        await c.goBack();
        expect(navigate).toHaveBeenCalledOnce();
        expect(saveSpy.mock.calls).toHaveLength(1);
        expect(c.childWindows()[0].minimized).toBe(true);
        expect(c.activeChild()).toBeNull();
      });

      it('does not navigate when the workflow itself cannot be saved', async () => {
        const c = childStep() as unknown as Leave;
        vi.spyOn(c as unknown as { saveToBackend(): unknown }, 'saveToBackend').mockReturnValue(
          throwError(() => new Error('Offline')),
        );
        const navigate = vi.spyOn(router(), 'navigate').mockResolvedValue(true);
        c.childWindows.set([formWindow('a', { isDirty: () => false })]);
        await c.goBack();
        expect(navigate).not.toHaveBeenCalled();
        await expect(c.resolveLeave()).resolves.toBe(false);
      });

      it('asks once per leave: the route guard trusts a toolbar action that already settled the editors', async () => {
        const c = wire();
        const window = formWindow('a');
        c.childWindows.set([window]);
        vi.spyOn(router(), 'navigate').mockImplementation(async () => c.canLeaveRoute());
        await c.goBack();
        expect((window.formContext as unknown as Ctx).requestClose).toHaveBeenCalledOnce();
      });

      it('guards a route change that did not come from the toolbar, such as the browser Back button', async () => {
        const c = wire();
        const window = formWindow('a');
        c.childWindows.set([window]);
        await expect(c.canLeaveRoute()).resolves.toBe(true);
        expect((window.formContext as unknown as Ctx).requestClose).toHaveBeenCalledOnce();
        (window.formContext as unknown as Ctx).requestClose.mockResolvedValue(false);
        await expect(c.canLeaveRoute()).resolves.toBe(false);
      });

      it('has the designer route guard registered, so leaving by any link is covered', async () => {
        const { DESIGNER_ROUTES } = await import('./designer.routes');
        expect(DESIGNER_ROUTES[0].canDeactivate).toHaveLength(1);
      });

      it('warns before the page unloads for a dirty form editor, open or minimized, at any depth', () => {
        const c = wire();
        const event = () => ({ preventDefault: vi.fn() }) as unknown as BeforeUnloadEvent;
        c.saveStatus.set('saved');
        let e = event();
        c.onBeforeUnload(e);
        expect(e.preventDefault).not.toHaveBeenCalled();

        c.childWindows.set([formWindow('a', { isDirty: () => false })]);
        e = event();
        c.onBeforeUnload(e);
        expect(e.preventDefault).not.toHaveBeenCalled();

        c.childWindows.set([formWindow('a')]);
        e = event();
        c.onBeforeUnload(e);
        expect(e.preventDefault).toHaveBeenCalledOnce();

        c.childWindows.set([childWindow('child', { hasDirtyForms: () => true })]);
        expect(c.hasDirtyForms()).toBe(true);
        e = event();
        c.onBeforeUnload(e);
        expect(e.preventDefault).toHaveBeenCalledOnce();
      });

      it('settles a form nested inside a child workflow before that child, and then the parent, can close or navigate', async () => {
        const c = wire();
        const navigate = vi.spyOn(router(), 'navigate').mockResolvedValue(true);
        const resolveLeave = vi.fn().mockResolvedValue(false);
        const save = vi.fn().mockResolvedValue(undefined);
        const child = childWindow('child', { resolveLeave, hasDirtyForms: () => true, save });
        c.childWindows.set([child]);

        // Closing the child asks its nested editors first; Keep editing leaves the child (and its form) open.
        await c.closeChild('child');
        expect(resolveLeave).toHaveBeenCalledOnce();
        expect(save).not.toHaveBeenCalled();
        expect(c.childWindows()).toHaveLength(1);

        // Leaving the page is refused for the same reason, and the child is brought forward to show the question.
        await c.goBack();
        expect(navigate).not.toHaveBeenCalled();
        expect(c.childWindows()[0].minimized).toBe(false);

        resolveLeave.mockResolvedValue(true);
        await c.closeChild('child');
        expect(c.childWindows()).toHaveLength(0);
      });

      it('lets a child designer’s own Back close it only through the window host that guards its forms', async () => {
        const c = wire();
        const close = vi.fn();
        c.editorContext = { close, definitionId: 'd', versionId: 'v' };
        const navigate = vi.spyOn(router(), 'navigate').mockResolvedValue(true);
        c.childWindows.set([formWindow('a')]);
        await c.goBack();
        await c.navigateContext(true);
        expect(close).toHaveBeenCalledTimes(2);
        expect(navigate).not.toHaveBeenCalled();
        // The host's own close runs the guard; the route guard never double-asks inside a window.
        await expect(c.canLeaveRoute()).resolves.toBe(true);
      });

      it('saves a dirty form draft with the toolbar Save, and does not save the workflow when that fails', async () => {
        const c = wire();
        const window = formWindow('a');
        c.childWindows.set([window]);
        await c.save();
        expect((window.formContext as unknown as Ctx).saveDraft).toHaveBeenCalledOnce();
        expect(saveSpy.mock.calls).toHaveLength(1);
        (window.formContext as unknown as Ctx).saveDraft.mockResolvedValue(false);
        await c.save();
        expect(saveSpy.mock.calls).toHaveLength(1);
      });

      it('refuses to leave a form window that is busy even though it has no unsaved changes', async () => {
        const c = wire();
        const requestClose = vi.fn().mockResolvedValue(false);
        c.childWindows.set([
          formWindow('a', { isDirty: () => false, isBusy: () => true, requestClose }),
        ]);
        expect(c.hasDirtyForms()).toBe(true);
        await expect(c.resolveLeave()).resolves.toBe(false);
        expect(requestClose).toHaveBeenCalledOnce();
        expect(saveSpy.mock.calls).toHaveLength(0);
        // It is brought forward so the person sees why it will not close.
        expect(c.childWindows()[0].minimized).toBe(false);
      });

      it('lets a window that is neither dirty nor busy go without being brought forward', async () => {
        const c = wire();
        c.childWindows.set([formWindow('a', { isDirty: () => false, isBusy: () => false })]);
        expect(c.hasDirtyForms()).toBe(false);
        await expect(c.resolveLeave()).resolves.toBe(true);
        expect(c.childWindows()[0].minimized).toBe(true);
      });

      it('toolbar Save also saves a nested child workflow’s own editors, and stops when that save fails', async () => {
        const c = wire();
        const save = vi.fn().mockResolvedValue(undefined);
        c.childWindows.set([childWindow('child', { save })]);
        await c.save();
        expect(save).toHaveBeenCalledOnce();
        expect(saveSpy.mock.calls).toHaveLength(1);

        save.mockRejectedValue(new Error('Offline'));
        await c.save();
        expect(save).toHaveBeenCalledTimes(2);
        expect(saveSpy.mock.calls).toHaveLength(1);
      });

      describe('as the host of the mapping editor', () => {
        type Hosted = Leave & {
          mappingHost: {
            owner(): unknown;
            root(): unknown;
            readonly(): boolean;
            isChild(): boolean;
            ensureSaved(): Promise<void>;
          };
          definitionId: WritableSignal<string>;
          versionId: WritableSignal<string>;
          version: WritableSignal<{ id: string; status: string; versionNumber: number } | null>;
        };
        const hosted = () => {
          const c = wire() as Hosted;
          c.definitionId.set('def-1');
          c.versionId.set('ver-1');
          c.version.set({ id: 'ver-1', status: 'Draft', versionNumber: 1 });
          return c;
        };

        it('has no owner until an event is selected, then names it by this designer’s own pins', () => {
          const c = hosted();
          c.selectedNodeId.set(null);
          expect(c.mappingHost.owner()).toBeNull();
          const [step] = c.nodes();
          c.selectedNodeId.set(step.id);
          expect(c.mappingHost.owner()).toEqual({
            workflowDefinitionId: 'def-1',
            workflowVersionId: 'ver-1',
            nodeKey: step.nodeKey,
          });
        });

        it('addresses a top-level designer’s own version as the root, and is not a child', () => {
          const c = hosted();
          expect(c.mappingHost.root()).toEqual({ definitionId: 'def-1', versionId: 'ver-1' });
          expect(c.mappingHost.isChild()).toBe(false);
          expect(c.mappingHost.readonly()).toBe(false);
        });

        it('addresses the pinned MAIN root from a child window while the owner stays the child’s own event', () => {
          const c = hosted();
          c.editorContext = {
            treeRoot: () => ({ definitionId: 'main-def', versionId: 'main-ver' }),
          };
          const [step] = c.nodes();
          c.selectedNodeId.set(step.id);
          expect(c.mappingHost.root()).toEqual({ definitionId: 'main-def', versionId: 'main-ver' });
          expect(c.mappingHost.isChild()).toBe(true);
          expect(c.mappingHost.owner()).toEqual(
            expect.objectContaining({ workflowDefinitionId: 'def-1', workflowVersionId: 'ver-1' }),
          );
        });

        it('has no root before the draft exists', () => {
          const c = hosted();
          c.definitionId.set('');
          expect(c.mappingHost.root()).toBeNull();
        });

        it('is read-only for a published version, so the mapper can inspect but not change', () => {
          const c = hosted();
          c.version.set({ id: 'ver-1', status: 'Published', versionNumber: 1 });
          expect(c.mappingHost.readonly()).toBe(true);
        });

        it('saves an unsaved draft and then every ancestor before the catalog is read', async () => {
          const c = hosted();
          const order: string[] = [];
          saveSpy.mock.calls.length = 0;
          (c as unknown as { saveToBackend(): unknown }).saveToBackend = () => {
            order.push('self');
            return of(null);
          };
          c.editorContext = { ensureSaved: async () => void order.push('parent') };
          c.saveStatus.set('unsaved');
          await c.mappingHost.ensureSaved();
          expect(order).toEqual(['self', 'parent']);
        });

        it('does not save a draft that is already saved, nor a published version, but still settles the ancestors', async () => {
          const c = hosted();
          const parent = vi.fn().mockResolvedValue(undefined);
          c.editorContext = { ensureSaved: parent };
          saveSpy.mock.calls.length = 0;
          c.saveStatus.set('saved');
          await c.mappingHost.ensureSaved();
          expect(saveSpy.mock.calls).toHaveLength(0);
          c.version.set({ id: 'ver-1', status: 'Published', versionNumber: 1 });
          c.saveStatus.set('unsaved');
          await c.mappingHost.ensureSaved();
          expect(saveSpy.mock.calls).toHaveLength(0);
          expect(parent).toHaveBeenCalledTimes(2);
        });
      });

      it('closing through the host after Publish and use works: the window is removed once the editor is idle', async () => {
        const c = childStep() as unknown as Leave;
        grant(['ManageForms', 'ManageTaskTypes']);
        try {
          const attach = vi
            .spyOn(TestBed.inject(TaskTypesService), 'attachForm')
            .mockReturnValue(of({ isSuccess: true } as unknown as ApiResult<TaskType>));
          const form = {
            id: 'F1',
            code: 'F1',
            nameEn: 'Leak',
            nameAr: 'تسرب',
            category: 'GENERAL',
            status: 'PUBLISHED',
            departmentCode: '10',
            fieldActivityCode: 'LEAK',
            currentVersionNo: 2,
            versionNos: [2],
            isUsable: true,
            updatedAt: '',
          };
          const step = c.nodes()[0];
          await c.openFormEditor({ mode: 'edit', form });
          await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
          fixture.detectChanges();
          const editor = fixture.debugElement.query(
            By.directive(WorkflowEmbeddedFormEditorComponent),
          ).componentInstance as WorkflowEmbeddedFormEditorComponent;
          const builder = (editor as unknown as { builder: FormBuilderComponent }).builder;
          vi.spyOn(builder, 'publishForm').mockResolvedValue({
            ...form,
            currentVersionNo: 3,
            createdBy: null,
            updatedBy: null,
            schemaJson: '',
            createdAt: '',
          } as never);

          await editor.publishAndUse();

          // The host's own close handshake runs after the editor is idle, so the window really goes away.
          await vi.waitFor(() => expect(c.childWindows()).toHaveLength(0));
          expect(attach).toHaveBeenCalledWith('meter', 'F1');
          const config = JSON.parse(c.nodes().find((n) => n.id === step.id)!.configurationJson);
          expect(config.formSteps[0]).toMatchObject({ formId: 'F1', versionNo: 3 });
          expect(c.formRevision(step.id)).toBe(1);
          expect(c.selectedNodeId()).toBe(step.id);
        } finally {
          TestBed.inject(AuthStore).clearSession();
        }
      });
    });

    describe('Step classification for forms made inside a child workflow', () => {
      type Classified = FormHost & {
        editorContext?: Record<string, unknown>;
        openChildEditor(): void;
      };

      it('gives a new Step of a child workflow the classification of the Task Type Step it came from', () => {
        const c = childStep() as unknown as Classified;
        c.editorContext = { classification: { departmentCode: '10', fieldActivityCode: 'LEAK' } };
        c.addNode('UserTask');
        const config = JSON.parse(c.nodes()[c.nodes().length - 1].configurationJson);
        expect(config).toMatchObject({
          taskTypeId: 'meter',
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
      });

      it('leaves a Step unclassified, to be chosen in the form window, when nothing can supply one', () => {
        const c = childStep() as unknown as Classified;
        const config = JSON.parse(c.nodes()[0].configurationJson);
        expect(config.departmentCode).toBeUndefined();
        expect(config.fieldActivityCode).toBeUndefined();
      });

      it('opens the form window with the Step’s own classification, else the inherited one', async () => {
        try {
          grant(['ManageForms', 'ManageTaskTypes']);
          const c = childStep() as unknown as Classified;
          c.editorContext = { classification: { departmentCode: '10', fieldActivityCode: 'LEAK' } };
          await c.openFormEditor({ mode: 'create' });
          await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
          expect(c.childWindows()[0].formContext!.classification).toEqual({
            departmentCode: '10',
            fieldActivityCode: 'LEAK',
          });
          await c.closeChild(c.childWindows()[0].nodeId);

          const [step] = c.nodes();
          c.nodes.update((nodes) =>
            nodes.map((node) =>
              node.id === step.id
                ? {
                    ...node,
                    configurationJson: JSON.stringify({
                      taskTypeId: 'meter',
                      departmentCode: '20',
                      fieldActivityCode: 'OWN',
                    }),
                  }
                : node,
            ),
          );
          await c.openFormEditor({ mode: 'create' });
          await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
          expect(c.childWindows()[0].formContext!.classification).toEqual({
            departmentCode: '20',
            fieldActivityCode: 'OWN',
          });
        } finally {
          TestBed.inject(AuthStore).clearSession();
        }
      });

      it('hands a child workflow window the classification of the Task Type Step that opens it', async () => {
        const c = draftEditor() as unknown as Classified & {
          workspace: WritableSignal<
            import('../workspace/workflow-workspace.service').WorkspaceSettings
          >;
          addNode(type: NodeType): void;
        };
        c.workspace.set({
          kind: 'Main',
          designerVersion: 3,
          fieldActivityTypeId: 'T1',
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
        c.addNode('MainActivity');
        c.openChildEditor();
        await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
        expect(c.childWindows()[0].inputs.editorContext!.classification).toEqual({
          departmentCode: '10',
          fieldActivityCode: 'LEAK',
        });
      });
    });
  });

  it('restricts new steps to children and inherits the owning Task Type', () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      nodes: WritableSignal<CanvasNode[]>;
      addNode(type: NodeType): void;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 3 });
    c.addNode('UserTask');
    expect(c.nodes()).toHaveLength(0);
    c.workspace.set({ kind: 'Child', designerVersion: 3, taskTypeId: 'meter' });
    c.addNode('MainActivity');
    expect(c.nodes()).toHaveLength(0);
    c.addNode('UserTask');
    expect(JSON.parse(c.nodes()[0].configurationJson).taskTypeId).toBe('meter');
  });

  it('keeps the rendered child editor instance and its canvas when minimized', async () => {
    fixture.detectChanges();
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      addNode(type: NodeType): void;
      openChildEditor(): void;
      minimizeChild(id: string): void;
      restoreChild(id: string): void;
      maximizeChild(id: string): void;
      closeProperties(): void;
      childWindows: WritableSignal<{ nodeId: string }[]>;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 3 });
    c.addNode('MainActivity');
    c.openChildEditor();
    await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
    fixture.detectChanges();
    const childElement = fixture.debugElement.query(By.directive(WorkflowDesignerComponent));
    const child = childElement.componentInstance as WorkflowDesignerComponent;
    const childState = child as unknown as {
      addNode(type: NodeType): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      zoom: WritableSignal<number>;
      panX: WritableSignal<number>;
    };
    childState.addNode('UserTask');
    // Older drafts may have a form step without routing rules or an otherwise route.
    // Rendering that step must not interrupt the canvas or the minimized dock.
    childState.nodes.update((nodes) =>
      nodes.map((node) => ({
        ...node,
        configurationJson: JSON.stringify({
          formSteps: [{ id: 'registration', formId: 'form-1', versionNo: 1, name: 'Registration' }],
        }),
      })),
    );
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('.child-editor-window [aria-label="Select New Task"]'),
    ).not.toBeNull();
    const steps = childState.nodes();
    const selection = childState.selectedNodeId();
    childState.zoom.set(1.4);
    childState.panX.set(123);
    const window = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '.child-editor-window',
    )!;
    const main = fixture.nativeElement.querySelector(
      '.designer-shell.has-focused-child > .designer-main',
    ) as HTMLElement;
    expect(main.hasAttribute('inert')).toBe(true);
    expect(window.getAttribute('aria-modal')).toBe('true');
    expect(window.querySelector('[aria-label="Minimize child workflow"]')).not.toBeNull();
    c.maximizeChild(c.childWindows()[0].nodeId);
    fixture.detectChanges();
    expect(window.parentElement!.classList.contains('child-editor-fullscreen')).toBe(true);
    expect(fixture.nativeElement.querySelector('.child-editor-dock')).toBeNull();
    c.minimizeChild(c.childWindows()[0].nodeId);
    fixture.detectChanges();
    // The activity's Properties modal (opened when it was added) keeps the canvas inert until it is closed.
    expect(main.hasAttribute('inert')).toBe(true);
    c.closeProperties();
    fixture.detectChanges();
    expect(main.hasAttribute('inert')).toBe(false);
    expect(window.style.display).toBe('none');
    expect(
      fixture.nativeElement.querySelector('.child-editor-dock [data-child-id]'),
    ).not.toBeNull();
    c.restoreChild(c.childWindows()[0].nodeId);
    fixture.detectChanges();
    expect(window.style.display).toBe('flex');
    expect(
      fixture.debugElement.query(By.directive(WorkflowDesignerComponent)).componentInstance,
    ).toBe(child);
    expect(childState.nodes()).toBe(steps);
    expect(childState.selectedNodeId()).toBe(selection);
    expect(childState.zoom()).toBe(1.4);
    expect(childState.panX()).toBe(123);
    expect(window.parentElement!.classList.contains('child-editor-fullscreen')).toBe(true);
    c.maximizeChild(c.childWindows()[0].nodeId);
    fixture.detectChanges();
    expect(window.parentElement!.classList.contains('child-editor-fullscreen')).toBe(false);
    expect(childState.nodes()).toBe(steps);
    expect(fixture.nativeElement.querySelector('.child-editor-dock')).toBeNull();
  });

  it('keeps the owned child editor controls available in routing version four', () => {
    fixture.detectChanges();
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      addNode(type: NodeType): void;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 4 });
    c.addNode('MainActivity');
    fixture.detectChanges();
    const business = fixture.nativeElement.querySelector(
      'app-workflow-business-activity',
    ) as HTMLElement;
    expect(business.textContent).toContain('Create child workflow');
    expect(business.querySelector('#wf-activity-child')).toBeNull();
    fixture.destroy();
  });

  it('adds a Fork and its Join in one undo operation with two incomplete branch slots', () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      nodes: WritableSignal<CanvasNode[]>;
      addNode(type: NodeType): void;
      undo(): void;
    };
    c.workspace.set({ kind: 'Child', designerVersion: 4 });
    c.addNode('ParallelGateway');
    const fork = c.nodes().find((n) => n.type === 'ParallelGateway')!;
    const join = c.nodes().find((n) => n.type === 'JoinGateway')!;
    const routing = JSON.parse(fork.configurationJson).routing;
    expect(routing.joinNodeKey).toBe(join.nodeKey);
    expect(routing.routes).toHaveLength(2);
    expect(routing.routes.every((r: { always: boolean }) => r.always)).toBe(true);
    c.undo();
    expect(c.nodes()).toHaveLength(0);
  });

  it('keeps the save failure visible without repeating notifications on every retry', async () => {
    fixture.detectChanges();
    const versions = TestBed.inject(WorkflowVersionsService);
    vi.spyOn(versions, 'saveXml').mockReturnValue(throwError(() => new Error('offline')));
    const notify = vi.spyOn(TestBed.inject(ToastService), 'error');
    const c = component as unknown as {
      saveToBackend(): import('rxjs').Observable<unknown>;
      saveStatus: WritableSignal<string>;
    };
    await expect(firstValueFrom(c.saveToBackend())).rejects.toThrow('save-failed');
    await expect(firstValueFrom(c.saveToBackend())).rejects.toThrow('save-failed');
    expect(c.saveStatus()).toBe('error');
    expect(notify).toHaveBeenCalledTimes(1);
    fixture.destroy();
  });

  it('discards live validation for older edits and exposes an offline retry state', async () => {
    fixture.detectChanges();
    const responses: Subject<{
      editId: number;
      revision: string;
      result: { isValid: boolean; errors: []; warnings: [] };
    }>[] = [];
    const edits: number[] = [];
    const api = TestBed.inject(WorkflowRoutingService);
    vi.spyOn(api, 'validateDesign').mockImplementation((_d, _v, _x, _j, editId) => {
      const response = new Subject<{
        editId: number;
        revision: string;
        result: { isValid: boolean; errors: []; warnings: [] };
      }>();
      responses.push(response);
      edits.push(editId);
      return response.asObservable();
    });
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      checkState: WritableSignal<string>;
      validationResult: WritableSignal<unknown>;
      addNode(type: NodeType): void;
    };
    c.workspace.set({ kind: 'Child', designerVersion: 4 });
    c.addNode('ExclusiveGateway');
    await vi.waitFor(() => expect(responses).toHaveLength(1), { timeout: 2000 });
    c.addNode('JoinGateway');
    responses[0].next({
      editId: edits[0],
      revision: '',
      result: { isValid: true, errors: [], warnings: [] },
    });
    expect(c.checkState()).toBe('checking');
    expect(c.validationResult()).toBeNull();
    await vi.waitFor(() => expect(responses).toHaveLength(2), { timeout: 2000 });
    responses[1].error(new Error('Offline'));
    expect(c.checkState()).toBe('error');
    fixture.destroy();
  });

  it('saves before closing the child modal and retains it when saving fails', async () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      addNode(type: NodeType): void;
      openChildEditor(): void;
      closeChild(id: string): Promise<void>;
      activeChild: WritableSignal<string | null>;
      childWindows: WritableSignal<
        { nodeId: string; inputs: { editorContext: { save?: () => Promise<void> } } }[]
      >;
    };
    c.workspace.set({ kind: 'Main', designerVersion: 3 });
    c.addNode('MainActivity');
    c.openChildEditor();
    await vi.waitFor(() => expect(c.childWindows()).toHaveLength(1));
    const child = c.childWindows()[0];
    child.inputs.editorContext.save = vi.fn().mockRejectedValue(new Error('Offline'));
    await c.closeChild(child.nodeId);
    expect(c.childWindows()).toHaveLength(1);
    expect(c.activeChild()).toBe(child.nodeId);
    child.inputs.editorContext.save = vi.fn().mockResolvedValue(undefined);
    await c.closeChild(child.nodeId);
    expect(child.inputs.editorContext.save).toHaveBeenCalledOnce();
    expect(c.childWindows()).toHaveLength(0);
    expect(c.activeChild()).toBeNull();
  });

  it('creates a task activity and preserves its identity through undo, duplication and XML save', () => {
    const c = component as unknown as {
      workspace: WritableSignal<
        import('../workspace/workflow-workspace.service').WorkspaceSettings
      >;
      addTaskActivity(task: { id: string; code: string; nameEn: string; nameAr: string }): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      undo(): void;
      redo(): void;
      duplicateSelected(): void;
    };
    c.workspace.set({
      kind: 'Main',
      schemaVersion: 2,
      organizationScopes: [{ level: 'Cluster', code: 'CC', clusterCode: 'CC' }],
    });
    c.addTaskActivity({ id: 'task-id', code: 'SURVEY', nameEn: 'Survey', nameAr: 'مسح' });
    let node = c.nodes().find((n: CanvasNode) => n.type === 'MainActivity')!;
    expect(node.name).toBe('Survey');
    expect(JSON.parse(node.configurationJson).taskTypeId).toBe('task-id');
    c.undo();
    expect(c.nodes().some((n: CanvasNode) => n.type === 'MainActivity')).toBe(false);
    c.redo();
    node = c.nodes().find((n: CanvasNode) => n.type === 'MainActivity')!;
    c.selectedNodeId.set(node.id);
    c.duplicateSelected();
    const activities = c.nodes().filter((n: CanvasNode) => n.type === 'MainActivity');
    expect(activities.length).toBe(2);
    for (const activity of activities)
      expect(JSON.parse(activity.configurationJson).taskTypeId).toBe('task-id');
    const doc = new DOMParser().parseFromString(
      buildNWFMXml({ nodes: c.nodes(), edges: [], variables: [], workspace: c.workspace() }),
      'application/xml',
    );
    const saved = Array.from(doc.querySelectorAll('Activity')).filter(
      (n) => n.getAttribute('type') === 'MainActivity',
    );
    expect(saved.length).toBe(2);
    for (const activity of saved)
      expect(JSON.parse(activity.getAttribute('configurationJson')!).taskTypeId).toBe('task-id');
  });

  function nodeCount(): number {
    return (component as unknown as { nodes: () => unknown[] }).nodes().length;
  }

  it('adds 100 nodes under 2s', () => {
    const started = performance.now();
    fillNodes(100);
    const elapsed = performance.now() - started;
    expect(nodeCount()).toBeGreaterThanOrEqual(100);
    expect(elapsed).toBeLessThan(2000);
  });

  it('adds 200 nodes under 4s', () => {
    const started = performance.now();
    fillNodes(200);
    const elapsed = performance.now() - started;
    expect(nodeCount()).toBeGreaterThanOrEqual(200);
    expect(elapsed).toBeLessThan(4000);
  });

  function access() {
    return component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      propsForm: FormGroup;
      patchPropsFromNode(node: CanvasNode): void;
      buildConfigurationJson(type: NodeType, value: Record<string, unknown>): string;
    };
  }

  it('loads a cloned draft when route parameters change on the existing designer', () => {
    const params = new Subject<{ get(key: string): string }>();
    (TestBed.inject(ActivatedRoute) as unknown as { paramMap: unknown }).paramMap = params;
    const versions = TestBed.inject(WorkflowVersionsService);
    const load = vi.spyOn(versions, 'getById').mockImplementation((_definition, version) =>
      of({
        id: version,
        status: version === 'published' ? 'Published' : 'Draft',
        activities: [],
        transitions: [],
        variables: [],
      }),
    );
    component.ngOnInit();
    params.next({ get: (key) => (key === 'definitionId' ? 'definition' : 'published') });
    const editor = component as unknown as {
      version: () => { status: string };
      isReadonly: () => boolean;
    };
    expect(editor.isReadonly()).toBe(true);
    params.next({ get: (key) => (key === 'definitionId' ? 'definition' : 'cloned') });
    expect(editor.version().status).toBe('Draft');
    expect(editor.isReadonly()).toBe(false);
    expect(load).toHaveBeenCalledWith('definition', 'cloned');
    fixture.destroy();
  });

  function configure(type: NodeType, configuration: Record<string, unknown>) {
    const editor = access();
    editor.addNode(type, 100, 100);
    const node = {
      ...editor.nodes()[editor.nodes().length - 1],
      configurationJson: JSON.stringify(configuration),
    };
    editor.nodes.update((nodes) => nodes.map((n) => (n.id === node.id ? node : n)));
    editor.selectedNodeId.set(node.id);
    editor.patchPropsFromNode(node);
    return editor;
  }

  it('keeps notification channels and recipients when editing its template', () => {
    const editor = configure('NotificationTask', {
      templateKey: 'before',
      channels: 'Email',
      recipientUserIds: ['recipient-id'],
    });
    editor.propsForm.patchValue({ templateKey: 'after' });
    const saved = JSON.parse(
      editor.buildConfigurationJson('NotificationTask', editor.propsForm.value),
    );
    expect(saved.templateKey).toBe('after');
    expect(saved.channels).toBe('Email');
    expect(saved.recipientUserIds).toEqual(['recipient-id']);
  });

  it('loads legacy signal keys and saves the canonical event key without losing extension fields', () => {
    const editor = configure('WaitEvent', {
      signalKey: 'order.ready',
      extension: { source: 'orders' },
    });
    expect(editor.propsForm.value.eventKey).toBe('order.ready');
    const saved = JSON.parse(editor.buildConfigurationJson('WaitEvent', editor.propsForm.value));
    expect(saved.eventKey).toBe('order.ready');
    expect(saved.signalKey).toBeUndefined();
    expect(saved.extension).toEqual({ source: 'orders' });
  });

  it('round trips a due date without shifting its timezone', () => {
    const editor = configure('Timer', {
      timerType: 'DueDate',
      dueAt: '2026-09-19T12:30:45.000Z',
      extension: true,
    });
    const saved = JSON.parse(editor.buildConfigurationJson('Timer', editor.propsForm.value));
    expect(saved.dueAt).toBe('2026-09-19T12:30:45.000Z');
    expect(saved.extension).toBe(true);
  });

  it('removes obsolete timer settings when changing timer type', () => {
    const editor = configure('Timer', { timerType: 'DueDate', dueAt: '2026-09-19T12:30:00Z' });
    editor.propsForm.patchValue({ timerType: 'Duration', duration: '00:30:00' });
    const saved = JSON.parse(editor.buildConfigurationJson('Timer', editor.propsForm.value));
    expect(saved.dueAt).toBeUndefined();
    expect(saved.duration).toBe('00:30:00');
  });

  it('suspends canvas shortcuts while the SLA editor is open', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      slaEditor: WritableSignal<unknown>;
      closeProperties(): void;
      onKeyDown(event: KeyboardEvent): void;
    };
    designer.addNode('UserTask', 100, 100);
    const node = designer.nodes().find((n) => n.type === 'UserTask')!;
    designer.selectedNodeId.set(node.id);
    designer.slaEditor.set({ locked: true, departmentCode: 'D1', fieldActivityCode: 'FA1' });
    for (const key of ['Delete', 'Backspace', 'Escape'])
      designer.onKeyDown(new KeyboardEvent('keydown', { key }));
    expect(designer.nodes().some((n) => n.id === node.id)).toBe(true);
    expect(designer.selectedNodeId()).toBe(node.id);
    designer.slaEditor.set(null);
    // The SLA editor is opened from Properties, which is still modal once the editor closes.
    designer.onKeyDown(new KeyboardEvent('keydown', { key: 'Delete' }));
    expect(designer.nodes().some((n) => n.id === node.id)).toBe(true);
    designer.closeProperties();
    designer.onKeyDown(new KeyboardEvent('keydown', { key: 'Delete' }));
    expect(designer.nodes().some((n) => n.id === node.id)).toBe(false);
  });

  it('shows Form on both business activity types and no tabs on other nodes', () => {
    const ids = (type: NodeType) => WorkflowDesignerComponent.tabsFor(type).map((t) => t.id);
    expect(ids('UserTask')).toEqual(['general', 'assignment', 'form', 'actions', 'sla', 'values']);
    expect(ids('MainActivity')).toEqual([
      'general',
      'assignment',
      'form',
      'actions',
      'sla',
      'values',
    ]);
    for (const type of [
      'Start',
      'End',
      'ExclusiveGateway',
      'ParallelGateway',
      'Timer',
      'ServiceTask',
      'NotificationTask',
      'WaitEvent',
    ] as NodeType[]) {
      expect(ids(type), type).toEqual([]);
    }
    expect(WorkflowDesignerComponent.tabsFor('UserTask').map((t) => t.labelKey)).toContain(
      'workflow.designer.tab_actions',
    );
  });

  it('offers Step values only in the hierarchical designer, where the mapping catalog exists', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      workspace: WritableSignal<unknown>;
      userTaskTabs(): { id: string }[];
    };
    designer.addNode('UserTask', 100, 100);
    const ids = () => designer.userTaskTabs().map((t) => t.id);
    designer.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion: 2 });
    expect(ids()).not.toContain('values');
    for (const designerVersion of [3, 4]) {
      designer.workspace.set({ kind: 'Child', schemaVersion: 2, designerVersion });
      expect(ids(), `designerVersion ${designerVersion}`).toContain('values');
    }
    // A Main workflow's activity has its Step form chosen from its Task Type, so only Step values remain.
    designer.workspace.set({ kind: 'Main', schemaVersion: 2, designerVersion: 3 });
    designer.addNode('MainActivity', 300, 100);
    expect(ids()).toEqual(['general', 'assignment', 'actions', 'sla', 'values']);
  });

  it('keeps Form selected when moving between business activities', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      inspectorTab: WritableSignal<string>;
      activeInspectorTab: () => string;
      onNodeClick(event: MouseEvent, id: string): void;
    };
    designer.addNode('UserTask', 100, 100);
    designer.addNode('MainActivity', 400, 100);
    const [task, main] = [
      designer.nodes().find((n) => n.type === 'UserTask')!,
      designer.nodes().find((n) => n.type === 'MainActivity')!,
    ];
    designer.onNodeClick(new MouseEvent('click'), task.id);
    designer.inspectorTab.set('form');
    expect(designer.activeInspectorTab()).toBe('form');
    // Both activity types expose the same form selection panel.
    designer.selectedNodeId.set(main.id);
    expect(designer.activeInspectorTab()).toBe('form');
    designer.selectedNodeId.set(task.id);
    designer.onNodeClick(new MouseEvent('click'), main.id);
    expect(designer.inspectorTab()).toBe('form');
    designer.inspectorTab.set('sla');
    designer.onNodeClick(new MouseEvent('click'), task.id);
    expect(designer.inspectorTab(), 'a tab both have is kept').toBe('sla');
  });

  it('keeps the rejection destination and legacy task questions through General edits', () => {
    const editor = configure('UserTask', {
      departmentCode: '10',
      fieldActivityCode: 'LEAK_REPAIR',
      taskTypeId: 'type',
      formDefinitionId: 'form',
      formVersionNo: 2,
      rejectTargetNodeKey: 'earlier_review',
      formFields: [
        { key: 'note', labelEn: 'Note', labelAr: 'ملاحظة', type: 'text', required: false },
      ],
    });
    const node = editor.nodes().find((n) => n.id === editor.selectedNodeId())!;
    editor.patchPropsFromNode(node);
    editor.propsForm.patchValue({ name: 'Renamed' });
    const saved = JSON.parse(editor.buildConfigurationJson('UserTask', editor.propsForm.value));
    expect(saved.rejectTargetNodeKey).toBe('earlier_review');
    expect(saved.departmentCode).toBe('10');
    expect(saved.fieldActivityCode).toBe('LEAK_REPAIR');
    expect(saved.formDefinitionId).toBe('form');
    expect(saved.formVersionNo).toBe(2);
    expect(saved.taskTypeId).toBe('type');
    expect(saved.formFields.map((f: { key: string }) => f.key)).toEqual(['note']);
  });

  it('does not render an Accept and Reject section on the General tab', () => {
    fixture.detectChanges();
    const state = component as unknown as {
      isLoading: { set: (v: boolean) => void };
      loadError: { set: (v: string | null) => void };
      version: { set: (v: { id: string; status: string; versionNumber: number }) => void };
    };
    state.isLoading.set(false);
    state.loadError.set(null);
    state.version.set({ id: 'v1', status: 'Draft', versionNumber: 1 });
    for (const type of ['UserTask', 'MainActivity'] as NodeType[]) {
      configure(type, {
        departmentCode: '10',
        fieldActivityCode: 'LEAK_REPAIR',
        rejectTargetNodeKey: 'earlier_review',
      });
      fixture.detectChanges();
      const panel = (fixture.nativeElement as HTMLElement).querySelector('#wf-activity-panel');
      expect(panel, type).not.toBeNull();
      expect(panel!.querySelector('#wf-activity-task-type'), type).not.toBeNull();
      expect(panel!.querySelector('#wf-activity-department'), type).toBeNull();
      expect(panel!.querySelector('#wf-activity-field-type'), type).toBeNull();
      expect(panel!.querySelector('#wf-activity-outcomes'), type).toBeNull();
      expect(panel!.querySelector('#wf-activity-reject-target'), type).toBeNull();
      expect(panel!.textContent, type).not.toMatch(/Accept and Reject|Return to/);
    }
  });

  it('suspends canvas shortcuts while a form preview is open', () => {
    const designer = component as unknown as {
      addNode(type: NodeType, x: number, y: number): void;
      nodes: WritableSignal<CanvasNode[]>;
      selectedNodeId: WritableSignal<string | null>;
      formPreview: WritableSignal<unknown>;
      onKeyDown(event: KeyboardEvent): void;
    };
    designer.addNode('UserTask', 100, 100);
    const node = designer.nodes().find((n) => n.type === 'UserTask')!;
    designer.selectedNodeId.set(node.id);
    designer.formPreview.set({
      form: { id: 'f' },
      departmentCode: '10',
      fieldActivityCode: 'LEAK_REPAIR',
      versionNo: 1,
    });
    for (const key of ['Delete', 'Backspace', 'Escape'])
      designer.onKeyDown(new KeyboardEvent('keydown', { key }));
    designer.onKeyDown(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true }));
    expect(designer.nodes().some((n) => n.id === node.id)).toBe(true);
    expect(designer.selectedNodeId()).toBe(node.id);
  });

  it('hydrates prepared canvas nodes from executable data while preserving their positions', () => {
    const c = component as unknown as {
      mergeWithBackend(state: unknown, version: unknown): { nodes: CanvasNode[] };
    };
    const merged = c.mergeWithBackend(
      {
        preparedFromLegacy: true,
        nodes: [
          {
            id: 'canvas-event',
            nodeKey: 'event',
            type: 'ServiceTask',
            x: 333,
            y: 444,
            configurationJson: '{}',
          },
        ],
        edges: [],
      },
      {
        status: 'Draft',
        activities: [
          {
            id: 'server-event',
            nodeKey: 'event',
            activityType: 'ServiceTask',
            actionKey: 'http.request',
            name: 'Event',
            configurationJson: '{"method":"POST"}',
            positionX: 100,
            positionY: 100,
          },
        ],
        transitions: [],
      },
    );
    expect(merged.nodes[0].actionKey).toBe('http.request');
    expect(merged.nodes[0].configurationJson).toContain('POST');
    expect(merged.nodes[0].x).toBe(333);
    expect(merged.nodes[0].y).toBe(444);
  });

  it('shows the published configuration, with its SLA snapshot, on read-only versions', () => {
    const merge = (status: string) =>
      (
        component as unknown as {
          mergeWithBackend(state: unknown, v: unknown): { nodes: CanvasNode[] };
        }
      ).mergeWithBackend(
        {
          nodes: [
            {
              id: 'canvas-1',
              nodeKey: 'review',
              type: 'UserTask',
              configurationJson: '{"departmentCode":"D1"}',
            },
          ],
          edges: [],
          variables: [],
        },
        {
          status,
          activities: [
            {
              id: 'a1',
              nodeKey: 'review',
              activityType: 'UserTask',
              configurationJson: '{"departmentCode":"D1","publishedSla":{"name":"Captured"}}',
            },
          ],
          transitions: [],
        },
      );
    expect(JSON.parse(merge('Published').nodes[0].configurationJson).publishedSla.name).toBe(
      'Captured',
    );
    expect(JSON.parse(merge('Draft').nodes[0].configurationJson).publishedSla).toBeUndefined();
  });

  it('loads legacy variable assignments and replaces them with edited assignments', () => {
    const editor = configure('ScriptTask', { assignments: { amount: 10 }, extension: true });
    expect(JSON.parse(editor.propsForm.value.setVariablesJson)).toEqual({ amount: 10 });
    editor.propsForm.patchValue({ setVariablesJson: '{"amount":20}' });
    const saved = JSON.parse(editor.buildConfigurationJson('ScriptTask', editor.propsForm.value));
    expect(saved.setVariables).toEqual({ amount: 20 });
    expect(saved.assignments).toBeUndefined();
    expect(saved.extension).toBe(true);
  });
});
