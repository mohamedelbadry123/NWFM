import type { CanvasEdge, CanvasNode } from './workflow-designer.component';
import type { RoutingConfiguration } from './workflow-routing.service';

/** Where a connection stands in its Condition's route order. */
export interface EdgeOrder {
  /** 1-based place among the Condition's routes; absent for the Otherwise connection. */
  position?: number;
  total: number;
  otherwise: boolean;
}

/**
 * The route order of every connection that leaves an ordered Condition (one that tries its routes in turn: first
 * match wins). It is derived from the saved routes, so it follows adding, removing, reordering, reloading and
 * undo/redo without being stored anywhere, and it never touches a connection's own label.
 */
export function edgeOrders(nodes: CanvasNode[], edges: CanvasEdge[]): Map<string, EdgeOrder> {
  const orders = new Map<string, EdgeOrder>();
  for (const node of nodes) {
    if (node.type !== 'ExclusiveGateway' || !node.configurationJson) continue;
    let routing: Partial<RoutingConfiguration> | undefined;
    try {
      routing = JSON.parse(node.configurationJson).routing;
    } catch {
      continue;
    }
    if (routing?.matchMode !== 'First' || !Array.isArray(routing.routes)) continue;
    const outgoing = edges.filter((edge) => edge.fromNodeId === node.id);
    // Numbers are the routes' places in the list, exactly as the route editor shows them.
    routing.routes.forEach((route, index) => {
      const edge = outgoing.find((candidate) => candidate.transitionKey === route.transitionKey);
      if (edge)
        orders.set(edge.id, {
          position: index + 1,
          total: routing.routes!.length,
          otherwise: false,
        });
    });
    const otherwise = outgoing.find(
      (edge) =>
        !!routing.fallbackTransitionKey && edge.transitionKey === routing.fallbackTransitionKey,
    );
    if (otherwise) orders.set(otherwise.id, { total: routing.routes.length, otherwise: true });
  }
  return orders;
}
