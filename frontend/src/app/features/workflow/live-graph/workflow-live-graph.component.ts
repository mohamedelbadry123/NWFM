import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WorkflowRuntimeService } from '../workflow-runtime.service';
import { prettyWorkflowLabel } from '../workflow-display.util';
import type {
  WorkflowLiveGraphEdgeView,
  WorkflowLiveGraphNodeView,
  WorkflowLiveGraphRuntimeStatus,
  WorkflowLiveGraphView,
} from '@core/models/workflow-ops.models';

interface GraphLayoutNode {
  node: WorkflowLiveGraphNodeView;
  width: number;
  height: number;
  cx: number;
  cy: number;
  shape: 'circle' | 'diamond' | 'rect';
}

@Component({
  selector: 'app-workflow-live-graph',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './workflow-live-graph.component.html',
  styleUrl: './workflow-live-graph.component.css',
})
export class WorkflowLiveGraphComponent implements OnInit {
  private readonly runtime = inject(WorkflowRuntimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly canvasRef = viewChild<ElementRef<HTMLElement>>('canvas');

  readonly instanceId = input.required<string>();
  readonly admin = input(false);
  readonly showHeading = input(true);

  protected readonly graph = signal<WorkflowLiveGraphView | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly zoom = signal(1);
  protected readonly panX = signal(0);
  protected readonly panY = signal(0);
  protected readonly pretty = prettyWorkflowLabel;

  private pollHandle: ReturnType<typeof setInterval> | null = null;
  private destroyed = false;
  private inFlight = false;
  private panning: { startX: number; startY: number; origX: number; origY: number } | null = null;

  protected readonly layoutNodes = computed(() => {
    const g = this.graph();
    if (!g) return [] as GraphLayoutNode[];
    return (g.nodes ?? []).map(node => this.toLayout(node));
  });

  protected readonly layoutByKey = computed(() => {
    const map = new Map<string, GraphLayoutNode>();
    for (const n of this.layoutNodes()) map.set(n.node.nodeKey, n);
    return map;
  });

  protected readonly world = computed(() => {
    const nodes = this.layoutNodes();
    if (nodes.length === 0) return { minX: 0, minY: 0, width: 800, height: 420 };
    let minX = Number.POSITIVE_INFINITY;
    let minY = Number.POSITIVE_INFINITY;
    let maxX = 0;
    let maxY = 0;
    for (const n of nodes) {
      minX = Math.min(minX, n.node.x);
      minY = Math.min(minY, n.node.y);
      maxX = Math.max(maxX, n.node.x + n.width);
      maxY = Math.max(maxY, n.node.y + n.height);
    }
    const pad = 72;
    return {
      minX: minX - pad,
      minY: minY - pad,
      width: Math.max(maxX - minX + pad * 2, 480),
      height: Math.max(maxY - minY + pad * 2, 320),
    };
  });

  protected readonly viewBox = computed(() => {
    const w = this.world();
    return `${w.minX} ${w.minY} ${w.width} ${w.height}`;
  });

  protected readonly currentNode = computed(() => {
    const graph = this.graph();
    const nodes = this.layoutNodes();
    if (!graph || nodes.length === 0) return null;
    const key = graph.currentNodeKey;
    if (key) {
      const byKey = nodes.find(n => n.node.nodeKey === key);
      if (byKey) return byKey;
    }
    return nodes.find(n => n.node.runtimeStatus === 'Active') ?? null;
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.stopPoll();
    });
  }

  ngOnInit(): void {
    this.load(this.instanceId());
  }

  protected edgePath(edge: WorkflowLiveGraphEdgeView): string {
    const from = this.layoutByKey().get(edge.fromNodeKey);
    const to = this.layoutByKey().get(edge.toNodeKey);
    if (!from || !to) return '';
    const mx = (from.cx + to.cx) / 2;
    const my = (from.cy + to.cy) / 2;
    return `M ${from.cx} ${from.cy} Q ${mx} ${my} ${to.cx} ${to.cy}`;
  }

  protected edgeClass(edge: WorkflowLiveGraphEdgeView): string {
    const from = this.paintedStatus(this.layoutByKey().get(edge.fromNodeKey)?.node);
    const to = this.paintedStatus(this.layoutByKey().get(edge.toNodeKey)?.node);
    if (from === 'Completed' && (to === 'Completed' || to === 'Active')) return 'live-edge live-edge--done';
    if (to === 'Failed' || from === 'Failed') return 'live-edge live-edge--failed';
    if (to === 'Active') return 'live-edge live-edge--active';
    return 'live-edge';
  }

  protected nodeClass(node: WorkflowLiveGraphNodeView): string {
    return `live-node live-node--${this.paintedStatus(node).toLowerCase()}`;
  }

  private paintedStatus(node: WorkflowLiveGraphNodeView | undefined): WorkflowLiveGraphRuntimeStatus {
    if (!node) return 'Pending';
    const graph = this.graph();
    if (graph?.instanceStatus === 'Running'
      && graph.currentNodeKey
      && node.nodeKey === graph.currentNodeKey
      && node.runtimeStatus !== 'Failed') {
      return 'Active';
    }
    return node.runtimeStatus;
  }

  protected diamondPoints(n: GraphLayoutNode): string {
    const { cx, cy, width, height } = n;
    return `${cx},${cy - height / 2} ${cx + width / 2},${cy} ${cx},${cy + height / 2} ${cx - width / 2},${cy}`;
  }

  protected zoomIn(): void { this.zoom.update(z => Math.min(2.4, +(z + 0.15).toFixed(2))); }
  protected zoomOut(): void { this.zoom.update(z => Math.max(0.55, +(z - 0.15).toFixed(2))); }

  protected fitView(): void {
    const canvas = this.canvasRef()?.nativeElement;
    const world = this.world();
    if (!canvas) {
      this.zoom.set(1);
      this.panX.set(0);
      this.panY.set(0);
      return;
    }
    const scale = Math.min(
      canvas.clientWidth / world.width,
      canvas.clientHeight / world.height,
      1.35,
    );
    this.zoom.set(Math.max(0.7, +scale.toFixed(2)));
    this.panX.set((canvas.clientWidth - world.width * this.zoom()) / 2);
    this.panY.set((canvas.clientHeight - world.height * this.zoom()) / 2);
  }

  protected onPointerDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    const target = event.target as HTMLElement;
    if (target.closest('button')) return;
    this.panning = {
      startX: event.clientX,
      startY: event.clientY,
      origX: this.panX(),
      origY: this.panY(),
    };
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  protected onPointerMove(event: PointerEvent): void {
    if (!this.panning) return;
    this.panX.set(this.panning.origX + (event.clientX - this.panning.startX));
    this.panY.set(this.panning.origY + (event.clientY - this.panning.startY));
  }

  protected onPointerUp(): void {
    this.panning = null;
  }

  protected onWheel(event: WheelEvent): void {
    event.preventDefault();
    if (event.deltaY < 0) this.zoomIn();
    else this.zoomOut();
  }

  private load(instanceId: string): void {
    if (!instanceId || this.destroyed || this.inFlight) return;
    this.inFlight = true;
    this.isLoading.set(this.graph() === null);
    this.loadError.set(null);
    const req = this.admin()
      ? this.runtime.adminGetLiveGraph(instanceId)
      : this.runtime.getLiveGraph(instanceId);

    req.subscribe({
      next: graph => {
        this.inFlight = false;
        if (this.destroyed) return;
        const first = this.graph() === null;
        this.graph.set(graph);
        this.isLoading.set(false);
        this.syncPoll(instanceId, graph.instanceStatus);
        if (first) requestAnimationFrame(() => this.fitView());
      },
      error: () => {
        this.inFlight = false;
        if (this.destroyed) return;
        this.loadError.set('workflow.runtime.progress.live_graph_load_error');
        this.isLoading.set(false);
        this.stopPoll();
      },
    });
  }

  private syncPoll(instanceId: string, status: string): void {
    this.stopPoll();
    if (status !== 'Running') return;
    this.pollHandle = setInterval(() => this.load(instanceId), 4000);
  }

  private stopPoll(): void {
    if (this.pollHandle) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
  }

  private toLayout(node: WorkflowLiveGraphNodeView): GraphLayoutNode {
    const type = node.activityType;
    const isGateway = type === 'ExclusiveGateway'
      || type === 'InclusiveGateway'
      || type === 'ParallelGateway'
      || type === 'JoinGateway';
    const isEvent = type === 'Start' || type === 'End';
    const width = isGateway || isEvent ? 80 : 220;
    const height = isGateway || isEvent ? 80 : 88;
    return {
      node,
      width,
      height,
      cx: node.x + width / 2,
      cy: node.y + height / 2,
      shape: isEvent ? 'circle' : isGateway ? 'diamond' : 'rect',
    };
  }
}
