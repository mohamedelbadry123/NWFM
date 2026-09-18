import {
  canvasWorldSize,
  layoutWorkflowGraph,
  mapCanvasIdsToActivities,
  nodesAreCollapsed,
} from './workflow-canvas-layout';
import { isRedirectOutcome } from './workflow-outcome.util';

describe('workflow canvas layout', () => {
  it('places Start above later User Tasks', () => {
    const pos = layoutWorkflowGraph(
      [
        { id: 's', type: 'Start' },
        { id: 'a', type: 'UserTask' },
        { id: 'e', type: 'End' },
      ],
      [
        { fromNodeId: 's', toNodeId: 'a' },
        { fromNodeId: 'a', toNodeId: 'e' },
      ],
    );

    expect(pos['s'].y).toBeLessThan(pos['a'].y);
    expect(pos['a'].y).toBeLessThan(pos['e'].y);
  });

  it('spreads a decision and its branches so they do not overlap', () => {
    const pos = layoutWorkflowGraph(
      [
        { id: 's', type: 'Start' },
        { id: 't', type: 'UserTask' },
        { id: 'g', type: 'ExclusiveGateway' },
        { id: 'a', type: 'UserTask' },
        { id: 'b', type: 'UserTask' },
        { id: 'e', type: 'End' },
      ],
      [
        { fromNodeId: 's', toNodeId: 't' },
        { fromNodeId: 't', toNodeId: 'g' },
        { fromNodeId: 'g', toNodeId: 'a' },
        { fromNodeId: 'g', toNodeId: 'b' },
        { fromNodeId: 'a', toNodeId: 'e' },
        { fromNodeId: 'b', toNodeId: 'e' },
      ],
    );

    expect(Math.hypot(pos['g'].x - pos['t'].x, pos['g'].y - pos['t'].y)).toBeGreaterThan(80);
    expect(Math.abs(pos['a'].x - pos['b'].x)).toBeGreaterThan(200);
    expect(pos['g'].y).toBeLessThan(pos['a'].y);
  });

  it('grows the canvas when nodes are dragged down', () => {
    const size = canvasWorldSize([{ x: 100, y: 2800, type: 'UserTask' }]);
    expect(size.height).toBeGreaterThan(2800);
    expect(size.width).toBeGreaterThanOrEqual(3200);
  });

  it('detects a dumped pile of nodes', () => {
    expect(nodesAreCollapsed([
      { x: 100, y: 100 },
      { x: 102, y: 98 },
      { x: 100, y: 101 },
    ])).toBe(true);
    expect(nodesAreCollapsed([
      { x: 100, y: 100 },
      { x: 100, y: 360 },
      { x: 100, y: 620 },
    ])).toBe(false);
  });

  it('remaps canvas ids to new activity ids by nodeKey', () => {
    const idMap = mapCanvasIdsToActivities(
      [
        { id: 'canvas-start', nodeKey: 'start' },
        { id: 'canvas-task', nodeKey: 'review' },
      ],
      [
        { id: 'act-start', nodeKey: 'start' },
        { id: 'act-task', nodeKey: 'review' },
      ],
    );

    expect(idMap.get('canvas-start')).toBe('act-start');
    expect(idMap.get('canvas-task')).toBe('act-task');
  });
});

describe('redirect outcome', () => {
  it('detects REDIRECT keys', () => {
    expect(isRedirectOutcome('REDIRECT')).toBe(true);
    expect(isRedirectOutcome('redirect')).toBe(true);
    expect(isRedirectOutcome('APPROVE', 'REDIRECT')).toBe(true);
    expect(isRedirectOutcome('APPROVE')).toBe(false);
  });
});
