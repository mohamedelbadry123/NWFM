export type LayoutNodeType =
  | 'Start'
  | 'UserTask'
  | 'ExclusiveGateway'
  | 'InclusiveGateway'
  | 'ServiceTask'
  | 'ScriptTask'
  | 'Timer'
  | 'WaitEvent'
  | 'NotificationTask'
  | 'ParallelGateway'
  | 'JoinGateway'
  | 'CallActivity'
  | 'End';

export interface LayoutNode {
  id: string;
  type: LayoutNodeType;
}

export interface LayoutEdge {
  fromNodeId: string;
  toNodeId: string;
}

const NODE_W = 210;
const NODE_H = 84;
const GATEWAY = 34;
const MIN_WORLD_W = 3200;
const MIN_WORLD_H = 2400;
const LAYER_GAP = 260;
const COL_GAP = 140;
const COLLAPSE_DISTANCE = 56;

function isGateway(type: LayoutNodeType): boolean {
  return type === 'ExclusiveGateway'
    || type === 'InclusiveGateway'
    || type === 'ParallelGateway'
    || type === 'JoinGateway';
}

function nodeWidth(type: LayoutNodeType): number {
  if (isGateway(type) || type === 'Start' || type === 'End') return GATEWAY * 2;
  return NODE_W;
}

function nodeHeight(type: LayoutNodeType): number {
  if (isGateway(type) || type === 'Start' || type === 'End') return GATEWAY * 2;
  return NODE_H;
}

/** Top-to-bottom layered layout with crossing reduction. */
export function layoutWorkflowGraph(
  nodes: LayoutNode[],
  edges: LayoutEdge[],
): Record<string, { x: number; y: number }> {
  const ids = nodes.map(n => n.id);
  const typeById: Record<string, LayoutNodeType> = {};
  for (const n of nodes) typeById[n.id] = n.type;

  const children: Record<string, string[]> = {};
  const inDegree: Record<string, number> = {};
  for (const id of ids) {
    children[id] = [];
    inDegree[id] = 0;
  }
  for (const e of edges) {
    if (!children[e.fromNodeId] || inDegree[e.toNodeId] === undefined) continue;
    if (!children[e.fromNodeId].includes(e.toNodeId)) {
      children[e.fromNodeId].push(e.toNodeId);
      inDegree[e.toNodeId]++;
    }
  }

  const layers: string[][] = [];
  const visited = new Set<string>();
  let queue = ids.filter(id => inDegree[id] === 0);
  const start = ids.find(id => typeById[id] === 'Start');
  if (start && queue.includes(start)) {
    queue = [start, ...queue.filter(id => id !== start)];
  }

  while (queue.length > 0) {
    layers.push([...queue]);
    queue.forEach(id => visited.add(id));
    const next: string[] = [];
    const seenNext = new Set<string>();
    for (const id of queue) {
      for (const child of children[id] ?? []) {
        if (visited.has(child) || seenNext.has(child)) continue;
        inDegree[child]--;
        if (inDegree[child] <= 0) {
          seenNext.add(child);
          next.push(child);
        }
      }
    }
    queue = next;
  }

  const leftover = ids.filter(id => !visited.has(id));
  if (leftover.length > 0) layers.push(leftover);

  for (let pass = 0; pass < 3; pass++) {
    for (let li = 1; li < layers.length; li++) {
      const prevIndex = new Map(layers[li - 1].map((id, i) => [id, i]));
      layers[li].sort((a, b) => barycenter(a, prevIndex, edges) - barycenter(b, prevIndex, edges));
    }
  }

  const startY = 100;
  const pos: Record<string, { x: number; y: number }> = {};

  layers.forEach((layer, li) => {
    const widths = layer.map(id => nodeWidth(typeById[id]));
    let x = 120;
    const y = startY + li * LAYER_GAP;
    layer.forEach((id, i) => {
      const w = widths[i];
      const h = nodeHeight(typeById[id]);
      const type = typeById[id];
      if (isGateway(type) || type === 'Start' || type === 'End') {
        pos[id] = { x: x + w / 2, y: y + h / 2 };
      } else {
        pos[id] = { x, y };
      }
      x += w + COL_GAP;
    });
  });

  return pos;
}

function barycenter(
  id: string,
  prevIndex: Map<string, number>,
  edges: LayoutEdge[],
): number {
  const parents = edges.filter(e => e.toNodeId === id).map(e => prevIndex.get(e.fromNodeId)).filter((v): v is number => v !== undefined);
  if (parents.length === 0) return 0;
  return parents.reduce((s, n) => s + n, 0) / parents.length;
}

/** True when most nodes sit on top of each other (lost coordinates after save/clone). */
export function nodesAreCollapsed(
  nodes: Array<{ x: number; y: number }>,
  minDistance = COLLAPSE_DISTANCE,
): boolean {
  if (nodes.length < 2) return false;
  let clustered = 0;
  for (let i = 0; i < nodes.length; i++) {
    for (let j = i + 1; j < nodes.length; j++) {
      if (Math.hypot(nodes[i].x - nodes[j].x, nodes[i].y - nodes[j].y) < minDistance) {
        clustered++;
      }
    }
  }
  return clustered >= Math.max(1, nodes.length - 1);
}

export interface MergeActivityRef {
  id?: string | null;
  nodeKey?: string | null;
}

/** Canvas ids are local UUIDs; compile/clone mints new activity ids. Match on stable nodeKey. */
export function mapCanvasIdsToActivities(
  designerNodes: Array<{ id: string; nodeKey: string }>,
  activities: MergeActivityRef[],
): Map<string, string> {
  const byKey = new Map<string, string>();
  for (const a of activities) {
    if (a.nodeKey && a.id) byKey.set(a.nodeKey, a.id);
  }
  const idMap = new Map<string, string>();
  for (const n of designerNodes) {
    const backendId = byKey.get(n.nodeKey);
    if (backendId) idMap.set(n.id, backendId);
  }
  return idMap;
}

export function canvasWorldSize(
  nodes: Array<{ x: number; y: number; type: LayoutNodeType }>,
): { width: number; height: number } {
  let maxX = MIN_WORLD_W;
  let maxY = MIN_WORLD_H;
  for (const n of nodes) {
    maxX = Math.max(maxX, n.x + nodeWidth(n.type) + 480);
    maxY = Math.max(maxY, n.y + nodeHeight(n.type) + 480);
  }
  return { width: Math.ceil(maxX), height: Math.ceil(maxY) };
}
