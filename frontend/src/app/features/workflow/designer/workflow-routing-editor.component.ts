import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  OnChanges,
  inject,
  signal,
} from '@angular/core';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { FormsModule, NgModel } from '@angular/forms';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowRoutingGroupComponent } from '../workspace/workflow-routing-group.component';
import type { FormRoutingField, RoutingGroup } from '../workspace/workflow-form-routing.types';
import type { CanvasNode, CanvasEdge, NodeType } from './workflow-designer.component';
import {
  RoutingConfiguration,
  RoutingContext,
  RoutingRule,
  isOrderedRouting,
  routingDefaults,
} from './workflow-routing.service';

export interface RoutingEditorChange {
  nodeId: string;
  configuration: string;
  edges: CanvasEdge[];
  type: NodeType;
  reconnectSource?: boolean;
}

/** Presentation-only proposal; the designer rechecks it against the current graph on edit. */
export function conditionSourceConnection(
  node: CanvasNode,
  nodes: CanvasNode[],
  edges: CanvasEdge[],
  config: RoutingConfiguration,
): {
  sourceId?: string;
  replaceEdgeId?: string;
  reason?:
    'sources' | 'invalid' | 'predecessor' | 'suppressed' | 'outgoing' | 'cycle' | 'coordination';
} {
  if (node.type !== 'ExclusiveGateway') return {};
  const incoming = edges.filter((edge) => edge.toNodeId === node.id);
  const owned = incoming.length === 1 && incoming[0].automaticConditionSource;
  const replace =
    owned &&
    owned.conditionNodeKey === node.nodeKey &&
    nodes.some(
      (candidate) =>
        candidate.id === incoming[0].fromNodeId && candidate.nodeKey === owned.sourceNodeKey,
    )
      ? incoming[0]
      : undefined;
  // A manually chosen execution predecessor takes precedence over data dependencies.
  if (incoming.length && !replace) return {};
  if (node.automaticConditionSourceSuppressed) return { reason: 'suppressed' };
  const keys = new Set<string>();
  let hasInput = false;
  const walk = (group: RoutingGroup) => {
    for (const condition of group.conditions) {
      // The Source selector emits "nodeKey." before a field is chosen.
      const schema = config.sources.find((s) => s.key === condition.field);
      const key =
        schema?.nodeKey || (condition.field.includes('.') ? condition.field.split('.')[0] : '');
      if (key === '$input') hasInput = true;
      else keys.add(key);
    }
    group.groups.forEach(walk);
  };
  config.routes.filter((rule) => !rule.always).forEach((rule) => walk(rule.when));
  if (!keys.size) return hasInput ? { reason: 'predecessor' } : {};
  if (keys.size !== 1 || keys.has('')) return { reason: 'sources' };
  const sources = nodes.filter((candidate) => keys.has(candidate.nodeKey));
  const source = sources[0];
  if (sources.length !== 1 || !['UserTask', 'ServiceTask'].includes(source.type))
    return { reason: 'invalid' };
  try {
    if (JSON.parse(source.configurationJson || '{}').triggerBinding) return { reason: 'invalid' };
  } catch {
    return { reason: 'invalid' };
  }
  if (incoming.some((edge) => edge.fromNodeId === source.id)) return {};
  const remaining = edges.filter((edge) => edge.id !== replace?.id);
  if (remaining.some((edge) => edge.fromNodeId === source.id)) return { reason: 'outgoing' };
  if (routingReachable(node.id, remaining).has(source.id)) return { reason: 'cycle' };
  const region = unfinishedForkRegion(nodes, remaining);
  if ([source.id, node.id, replace?.fromNodeId].some((id) => id && region.has(id)))
    return { reason: 'coordination' };
  return { sourceId: source.id, replaceEdgeId: replace?.id };
}

/** Each nested Fork closes only at its own paired Join, not at an inner Join. */
function unfinishedForkRegion(nodes: CanvasNode[], edges: CanvasEdge[]): Set<string> {
  const region = new Set<string>();
  for (const fork of nodes.filter((node) =>
    ['ParallelGateway', 'InclusiveGateway'].includes(node.type),
  )) {
    let joinKey: string | undefined;
    try {
      joinKey = JSON.parse(fork.configurationJson || '{}').routing?.joinNodeKey;
    } catch {
      /* An unpaired Fork cannot establish a safe boundary. */
    }
    const join = nodes.find((node) => node.nodeKey === joinKey && node.type === 'JoinGateway');
    const branches = edges.filter((edge) => edge.fromNodeId !== join?.id);
    for (const id of routingReachable(fork.id, branches)) {
      if (id !== fork.id && id !== join?.id) region.add(id);
    }
    // Include dangling branch tails that lead into this Join but are not yet reachable
    // from the Fork. Stop at the paired Fork so earlier/later groups stay outside.
    if (join) {
      const backwards = edges
        .filter((edge) => edge.toNodeId !== fork.id)
        .map((edge) => ({ ...edge, fromNodeId: edge.toNodeId, toNodeId: edge.fromNodeId }));
      for (const id of routingReachable(join.id, backwards)) {
        if (id !== fork.id && id !== join.id) region.add(id);
      }
    }
  }
  return region;
}

function routingReachable(start: string, edges: CanvasEdge[]): Set<string> {
  const adjacent = new Map<string, string[]>();
  for (const edge of edges) {
    adjacent.set(edge.fromNodeId, [...(adjacent.get(edge.fromNodeId) || []), edge.toNodeId]);
  }
  const visited = new Set<string>();
  const pending = [start];
  while (pending.length) {
    const id = pending.pop()!;
    if (visited.has(id)) continue;
    visited.add(id);
    pending.push(...(adjacent.get(id) || []));
  }
  return visited;
}

@Component({
  selector: 'app-workflow-routing-editor',
  standalone: true,
  imports: [FormsModule, WorkflowRoutingGroupComponent, CdkDropList, CdkDrag, CdkDragHandle],
  templateUrl: './workflow-routing-editor.component.html',
  styles: [
    `
      :host {
        display: block;
        padding: 20px;
      }
      label {
        display: block;
        font-size: 13px;
        margin: 14px 0;
      }
      label > span {
        display: block;
        margin-bottom: 7px;
        font-weight: 600;
      }
      select,
      input {
        width: 100%;
        min-height: 38px;
        padding: 8px;
        border: 1px solid var(--ds-border-strong);
        border-radius: 6px;
        background: var(--ds-input-bg, var(--ds-surface));
        color: var(--ds-text);
      }
      fieldset {
        border: 0;
        padding: 0;
        margin: 0;
        min-width: 0;
      }
      .guided-section {
        margin: 12px 0;
        border-top: 1px solid var(--ds-border);
        padding-top: 12px;
      }
      summary {
        cursor: pointer;
        font-size: 13px;
        font-weight: 600;
        padding: 6px 0;
      }
      .setup-review {
        margin: 8px 0;
      }
      .route {
        border: 1px solid var(--ds-border);
        border-radius: 8px;
        padding: 14px;
        margin: 16px 0;
      }
      .summary {
        border-inline-start: 3px solid var(--ds-primary);
        padding: 12px;
        background: var(--ds-surface-2);
        line-height: 1.6;
        font-size: 13px;
      }
      .hint {
        color: var(--ds-text-3);
        font-size: 12px;
        line-height: 1.6;
      }
      .setup-missing {
        color: var(--ds-danger, #b45309);
      }
      select[aria-invalid='true'] {
        border-color: var(--ds-danger, #b45309);
      }
      .heading {
        font-size: 17px;
        font-weight: 650;
        margin-bottom: 10px;
      }
      .buttons {
        display: flex;
        gap: 8px;
        flex-wrap: wrap;
      }
      button {
        padding: 8px 12px;
        border: 1px solid var(--ds-border-strong);
        border-radius: 6px;
        background: var(--ds-surface);
        color: var(--ds-text);
        cursor: pointer;
      }
      button:focus-visible,
      select:focus-visible,
      input:focus-visible {
        outline: 3px solid var(--ds-primary);
        outline-offset: 2px;
      }
      li {
        padding: 10px 0;
        list-style: none;
        border-bottom: 1px solid var(--ds-border);
      }
      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        margin: -1px;
        padding: 0;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
      .route-head {
        display: flex;
        align-items: center;
        gap: 10px;
      }
      .route-head h3 {
        flex: 1;
        margin: 0;
      }
      .route-number {
        display: inline-block;
        min-width: 24px;
        height: 24px;
        border-radius: 12px;
        text-align: center;
        line-height: 24px;
        font-size: 12px;
        font-weight: 650;
        color: #fff;
        background: var(--ds-primary, #5040d6);
      }
      .drag-handle {
        cursor: grab;
        user-select: none;
        touch-action: none;
        padding: 4px 8px;
        border: 1px dashed var(--ds-border-strong);
        border-radius: 6px;
        color: var(--ds-text-3);
      }
      .drag-handle:active {
        cursor: grabbing;
      }
      .cdk-drag-preview {
        box-sizing: border-box;
        border: 1px solid var(--ds-primary);
        border-radius: 8px;
        background: var(--ds-surface);
        box-shadow: 0 6px 18px rgb(0 0 0 / 25%);
      }
      .cdk-drag-placeholder {
        opacity: 0.35;
      }
      .cdk-drag-animating,
      .routes-list.cdk-drop-list-dragging .route:not(.cdk-drag-placeholder) {
        transition: transform 180ms cubic-bezier(0, 0, 0.2, 1);
      }
      .mode {
        border: 1px solid var(--ds-border);
        border-radius: 8px;
        padding: 12px;
        margin: 12px 0;
        font-size: 13px;
        line-height: 1.6;
      }
      .mode[data-routing-mode='ordered'] {
        border-inline-start: 3px solid var(--ds-primary, #5040d6);
      }
      .mode[data-routing-mode='legacy'] {
        border-inline-start: 3px solid var(--ds-warning, #b45309);
      }
    `,
  ],
})
export class WorkflowRoutingEditorComponent implements OnChanges {
  @Input({ required: true }) node!: CanvasNode;
  @Input() nodes: CanvasNode[] = [];
  @Input() edges: CanvasEdge[] = [];
  @Input() context: RoutingContext | null = null;
  @Input() readonly = false;
  @Output() changed = new EventEmitter<RoutingEditorChange>();
  @Output() sourceRequested = new EventEmitter<string>();
  @Output() refreshRequested = new EventEmitter<void>();
  private readonly locale = inject(LocaleService);
  @Input() reviewIssues = false;
  touched = false;
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  /** Spoken after a route changes place, so a keyboard or screen-reader user knows where it went. */
  readonly announcement = signal('');
  /** Whether this Condition tries its routes in order: order is then part of what it does. */
  ordered(): boolean {
    return this.type === 'ExclusiveGateway' && this.config.matchMode === 'First';
  }
  /** Ordered routing on the current contract: later routes are not looked at once one matches. */
  firstMatchWins(): boolean {
    return this.type === 'ExclusiveGateway' && isOrderedRouting(this.config);
  }
  /** A Condition saved before ordered routing: it keeps checking every route until it is explicitly upgraded. */
  legacy(): boolean {
    return this.type === 'ExclusiveGateway' && this.config.contractVersion !== 2;
  }
  /** The explicit, draft-only opt-in to ordered routing. Opening a Condition never does this. */
  useOrderedRouting() {
    if (this.readonly || !this.legacy()) return;
    this.config.contractVersion = 2;
    this.config.matchMode = 'First';
    this.announcement.set(
      this.t(
        'Ordered routing is on. Routes are tried from top to bottom; the first match is used.',
        'تم تفعيل التوجيه المرتب. تُجرَّب المسارات من الأعلى إلى الأسفل ويُستخدم أول مطابق.',
      ),
    );
    this.save();
  }
  checks() {
    if (this.type === 'JoinGateway')
      return [
        {
          label: this.t('Connect the branches from the paired Fork', 'صل فروع التفرع المطابق'),
          done: this.incoming().length > 0,
        },
        {
          label: this.t('Choose where to continue', 'اختر الوجهة التالية'),
          done: !!this.target(this.continuation()),
        },
      ];
    const checks = [
      { label: this.t('Add a route', 'أضف مساراً'), done: this.config.routes.length > 0 },
      {
        label: this.t('Choose every destination', 'اختر وجهة كل مسار'),
        done:
          this.config.routes.length > 0 &&
          this.config.routes.every((r) => !!this.target(r.transitionKey)),
      },
      {
        label: this.t('Set conditions or explicitly choose Always', 'حدد الشروط أو اختر دائماً'),
        done:
          this.config.routes.length > 0 &&
          this.config.routes.every((r) => r.always || this.completeGroup(r.when)),
      },
    ];
    if (this.type !== 'ExclusiveGateway')
      checks.unshift({
        label: this.t('Pair with a Join / Wait', 'اربط بعنصر انتظار'),
        done: !!this.config.joinNodeKey,
      });
    return checks;
  }
  nextSetting(): string {
    return this.checks().find((check) => !check.done)?.label || '';
  }
  private completeGroup(group: RoutingGroup): boolean {
    return (
      group.conditions.length + group.groups.length > 0 &&
      group.conditions.every(
        (c) =>
          !!c.field &&
          !!c.operator &&
          (['is_empty', 'is_not_empty'].includes(c.operator) ||
            (c.value !== null && c.value !== undefined && c.value !== '')),
      ) &&
      group.groups.every((g) => this.completeGroup(g))
    );
  }
  repairs(): { transitionKey: string; message: string; originalJson: string }[] {
    try {
      return JSON.parse(this.node.configurationJson || '{}').authoring?.repairs || [];
    } catch {
      return [];
    }
  }
  canResolve(key: string): boolean {
    const rule = this.config.routes.find((r) => r.transitionKey === key);
    return !!rule && !!this.target(key) && (rule.always || this.completeGroup(rule.when));
  }
  resolveRepair(key: string): void {
    if (this.readonly || !this.canResolve(key)) return;
    const data = JSON.parse(this.node.configurationJson || '{}');
    data.authoring.repairs = this.repairs().filter((r) => r.transitionKey !== key);
    data.routing = this.config;
    this.changed.emit({
      nodeId: this.node.id,
      configuration: JSON.stringify(data),
      edges: this.localEdges,
      type: this.type,
    });
  }
  config = routingDefaults();
  fields: FormRoutingField[] = [];
  localEdges: CanvasEdge[] = [];
  type: NodeType = 'ExclusiveGateway';
  destinationProblem = '';
  private editingNodeId?: string;
  canConnectSource(): boolean {
    if (this.readonly || this.type !== 'ExclusiveGateway' || this.incoming().length) return false;
    return !!conditionSourceConnection(
      { ...this.node, automaticConditionSourceSuppressed: undefined },
      this.nodes,
      this.edges,
      this.config,
    ).sourceId;
  }
  sourceConnectionMessage(): string {
    if (this.readonly) return '';
    const proposal = conditionSourceConnection(this.node, this.nodes, this.edges, this.config);
    const messages = {
      sources: this.t(
        'Rules use multiple or incomplete sources. Choose one Step/Event source, or connect the execution predecessor manually.',
        'تستخدم القواعد مصادر متعددة أو غير مكتملة. اختر مصدراً واحداً أو صل العنصر السابق يدوياً.',
      ),
      invalid: this.t(
        'This data source is not an available Step/Event in this workflow. Choose a local source or connect the execution predecessor manually.',
        'مصدر البيانات ليس خطوة أو حدثاً متاحاً في سير العمل. اختر مصدراً محلياً أو صل العنصر السابق يدوياً.',
      ),
      predecessor: this.t(
        'Workflow inputs provide data, not an execution predecessor. Connect the preceding Step/Event manually.',
        'توفر مدخلات سير العمل بيانات فقط. صل الخطوة أو الحدث السابق يدوياً.',
      ),
      suppressed: this.t(
        'The source connector was deleted. Choose Connect source or select a Source to connect it again.',
        'تم حذف اتصال المصدر. اختر توصيل المصدر أو حدد مصدراً لتوصيله مجدداً.',
      ),
      outgoing: this.t(
        'Source already continues elsewhere. Review its existing route before connecting this Condition.',
        'يتابع المصدر إلى وجهة أخرى. راجع مساره الحالي قبل توصيل هذا الشرط.',
      ),
      cycle: this.t(
        'Connecting this source would create a cycle. Choose another source or revise the destinations.',
        'سيؤدي توصيل هذا المصدر إلى دورة. اختر مصدراً آخر أو راجع الوجهات.',
      ),
      coordination: this.t(
        'This route touches Fork/Join coordination. Connect the execution predecessor manually after reviewing the branches.',
        'يتصل هذا المسار بتفرع أو التقاء. راجع الفروع ثم صل العنصر السابق يدوياً.',
      ),
    };
    return proposal.reason
      ? messages[proposal.reason]
      : proposal.sourceId
        ? this.t(
            'This saved source is ready. Connect it to add the missing arrow; new Source selections connect automatically.',
            'هذا المصدر المحفوظ جاهز. صله لإضافة السهم الناقص؛ تتصل اختيارات المصدر الجديدة تلقائياً.',
          )
        : '';
  }
  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  ngOnChanges() {
    if (this.editingNodeId !== this.node.id) this.destinationProblem = '';
    this.editingNodeId = this.node.id;
    let data: Record<string, unknown> = {};
    try {
      data = JSON.parse(this.node.configurationJson || '{}');
    } catch {
      /* Draft validation shows malformed configuration. */
    }
    this.config = {
      ...routingDefaults(),
      ...(structuredClone(data['routing'] || {}) as RoutingConfiguration),
    };
    this.type = this.node.type;
    this.localEdges = this.edges
      .filter((e) => e.fromNodeId === this.node.id)
      .map((e) => ({ ...e }));
    const sources = [...(this.context?.sources || [])];
    for (const source of this.config.sources)
      if (!sources.some((s) => s.key === source.key)) sources.push(source);
    this.fields = sources.map((s) => ({
      dataName: s.key,
      type: s.type,
      labelEn: s.label,
      labelAr: s.label,
      choices: s.choices || [],
    }));
  }
  destinations() {
    return this.nodes.filter((n) => n.type !== 'Start' && n.id !== this.node.id && !this.bound(n));
  }
  private bound(node: CanvasNode) {
    try {
      return !!JSON.parse(node.configurationJson || '{}').triggerBinding;
    } catch {
      return false;
    }
  }
  pairedForks() {
    return this.nodes.filter((n) => {
      if (!['ParallelGateway', 'InclusiveGateway'].includes(n.type)) return false;
      try {
        return JSON.parse(n.configurationJson || '{}').routing?.joinNodeKey === this.node.nodeKey;
      } catch {
        return false;
      }
    });
  }
  joins() {
    return this.nodes.filter((n) => n.type === 'JoinGateway');
  }
  incoming() {
    return this.edges
      .filter((e) => e.toNodeId === this.node.id)
      .map((e) => this.nodes.find((n) => n.id === e.fromNodeId))
      .filter((n): n is CanvasNode => !!n);
  }
  prerequisite(node: CanvasNode): string {
    try {
      const routing = JSON.parse(node.configurationJson || '{}').routing as
        RoutingConfiguration | undefined;
      if (!routing?.routes?.length) return this.t('Configuration incomplete', 'الإعداد غير مكتمل');
      return routing.routes
        .map((rule) => (rule.always ? this.t('Always', 'دائماً') : this.describe(rule.when)))
        .join(' · ');
    } catch {
      return this.t('Configuration incomplete', 'الإعداد غير مكتمل');
    }
  }
  target(key?: string | null) {
    return this.localEdges.find((e) => e.transitionKey === key)?.toNodeId || '';
  }
  continuation() {
    return this.localEdges.find((e) => e.transitionKey !== this.config.failureTransitionKey)
      ?.transitionKey;
  }
  destination(key?: string | null) {
    return (
      this.nodes.find((n) => n.id === this.target(key))?.name ||
      this.t('Choose a destination', 'اختر الوجهة')
    );
  }
  setTarget(key: string, target: string, model?: NgModel) {
    if (this.readonly) return false;
    this.destinationProblem = '';
    const remaining = [
      ...this.edges.filter((edge) => edge.fromNodeId !== this.node.id),
      ...this.localEdges.filter((edge) => edge.transitionKey !== key),
    ];
    if (
      target &&
      (!this.destinations().some((node) => node.id === target) ||
        routingReachable(target, remaining).has(this.node.id))
    ) {
      this.destinationProblem = this.t(
        'This destination would create an invalid route or cycle. Choose another destination.',
        'ستؤدي هذه الوجهة إلى مسار غير صالح أو دورة. اختر وجهة أخرى.',
      );
      model?.control.setValue(this.target(key), { emitEvent: false, emitViewToModelChange: false });
      return false;
    }
    const existing = this.localEdges.find((edge) => edge.transitionKey === key);
    this.localEdges = this.localEdges.filter((e) => e.transitionKey !== key);
    if (target)
      this.localEdges.push({
        id: existing?.id || crypto.randomUUID(),
        fromNodeId: this.node.id,
        toNodeId: target,
        transitionKey: key,
        labelEn: '',
        labelAr: '',
        descriptionEn: '',
        outcomeKey: '',
        conditionExpression: '',
        isDefault: false,
        priority: this.localEdges.length,
      });
    return true;
  }
  routeTarget(rule: RoutingRule, target: string, model?: NgModel) {
    if (this.setTarget(rule.transitionKey, target, model)) this.save();
  }
  fallback(target: string, failure = false, model?: NgModel) {
    const prop = failure ? 'failureTransitionKey' : 'fallbackTransitionKey';
    const key = this.config[prop] || `route_${crypto.randomUUID()}`;
    if (!this.setTarget(key, target, model)) return;
    this.config[prop] = target ? key : null;
    this.save();
  }
  setContinuation(target: string, model?: NgModel) {
    if (this.setTarget(this.continuation() || `route_${crypto.randomUUID()}`, target, model))
      this.save();
  }
  addRule() {
    this.config.routes.push({
      transitionKey: `route_${crypto.randomUUID()}`,
      always: this.type === 'ParallelGateway',
      when: { match: 'All', conditions: [], groups: [] },
    });
    this.save();
  }
  removeRule(index: number) {
    const rule = this.config.routes.splice(index, 1)[0];
    this.localEdges = this.localEdges.filter((e) => e.transitionKey !== rule.transitionKey);
    this.save();
  }
  /** Moves a route by one place. Its transition key, connection and references stay; only its position changes. */
  move(index: number, by: number) {
    if (this.readonly) return;
    const target = index + by;
    if (target < 0 || target >= this.config.routes.length) return;
    const key = this.config.routes[index].transitionKey;
    this.reorder(index, target);
    // The moved element is re-created in its new place, so keep the keyboard on the same control.
    // At either end the button that was pressed is now disabled, so the opposite one takes the focus.
    const atEnd = by < 0 ? target === 0 : target === this.config.routes.length - 1;
    const wanted = by < 0 !== atEnd ? 'up' : 'down';
    setTimeout(() =>
      Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-route-key]'))
        .find((element) => element.dataset['routeKey'] === key)
        ?.querySelector<HTMLElement>('[data-move="' + wanted + '"]')
        ?.focus(),
    );
  }
  /** Drag and drop: the same reorder as the buttons. */
  drop(event: CdkDragDrop<RoutingRule[]>) {
    if (this.readonly || !this.ordered()) return;
    this.reorder(event.previousIndex, event.currentIndex);
  }
  private reorder(from: number, to: number) {
    if (from === to) return;
    const [rule] = this.config.routes.splice(from, 1);
    this.config.routes.splice(to, 0, rule);
    this.announcement.set(
      this.t(
        'Route moved to position ' + (to + 1) + ' of ' + this.config.routes.length + '.',
        'نُقل المسار إلى الموضع ' + (to + 1) + ' من ' + this.config.routes.length + '.',
      ),
    );
    this.save();
  }
  describe(group: RoutingGroup): string {
    const operators: Record<string, string> = {
      equal: '=',
      not_equal: '≠',
      greater_than: '>',
      greater_or_equal: '≥',
      less_than: '<',
      less_or_equal: '≤',
      is_empty: this.t('is empty', 'فارغ'),
      is_not_empty: this.t('is present', 'موجود'),
    };
    const parts = group.conditions.map((c) => {
      const source = this.context?.sources.find((s) => s.key === c.field);
      const value =
        c.operator === 'is_empty' || c.operator === 'is_not_empty'
          ? ''
          : source?.sensitive
            ? '•••'
            : String(c.value ?? '');
      return `${source?.label || c.field || this.t('Choose field', 'اختر الحقل')} ${operators[c.operator] || c.operator.replaceAll('_', ' ')} ${value}`.trim();
    });
    parts.push(...group.groups.map((g) => `(${this.describe(g)})`));
    return (
      parts.join(group.match === 'All' ? this.t(' AND ', ' و ') : this.t(' OR ', ' أو ')) ||
      this.t('Incomplete condition', 'شرط غير مكتمل')
    );
  }
  forkMode(type: NodeType) {
    this.type = type;
    this.config.matchMode = 'All';
    if (type === 'ParallelGateway') this.config.routes.forEach((r) => (r.always = true));
    this.save();
  }
  ruleChanged() {
    // Source selection emits "nodeKey."; comparison/value changes and removals do not.
    // Count placeholders so moving/removing groups cannot lift a deletion suppression.
    const selections = (config: RoutingConfiguration) => {
      const counts = new Map<string, number>();
      const walk = (group: RoutingGroup) => {
        for (const { field } of group.conditions)
          if (field.endsWith('.') && field !== '$input.' && field !== '.')
            counts.set(field, (counts.get(field) || 0) + 1);
        group.groups.forEach(walk);
      };
      config.routes.filter((rule) => !rule.always).forEach((rule) => walk(rule.when));
      return counts;
    };
    const previous = selections(
      JSON.parse(this.node.configurationJson || '{}').routing || routingDefaults(),
    );
    this.save(
      [...selections(this.config)].some(([key, count]) => count > (previous.get(key) || 0)),
    );
  }
  save(reconnectSource = false) {
    if (this.readonly) return;
    const keys = new Set<string>();
    const walk = (g: RoutingGroup) => {
      g.conditions.forEach((c) => keys.add(c.field));
      g.groups.forEach(walk);
    };
    this.config.routes.filter((r) => !r.always).forEach((r) => walk(r.when));
    this.config.sources = [...keys]
      .map(
        (key) =>
          this.context?.sources.find((s) => s.key === key) ||
          this.config.sources.find((s) => s.key === key),
      )
      .filter((s): s is NonNullable<typeof s> => !!s);
    this.config.routes.forEach((r, i) => {
      const edge = this.localEdges.find((e) => e.transitionKey === r.transitionKey);
      if (edge) {
        edge.priority = i;
        edge.labelEn = r.always ? 'Always' : this.describe(r.when);
      }
    });
    const data = JSON.parse(this.node.configurationJson || '{}');
    data.routing = this.config;
    this.changed.emit({
      nodeId: this.node.id,
      reconnectSource,
      configuration: JSON.stringify(data),
      edges: this.localEdges,
      type: this.type,
    });
  }
}
