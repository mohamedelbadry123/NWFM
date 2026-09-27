import { WorkflowActivitySlaComponent } from '../workspace/workflow-activity-sla.component';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  HostListener,
  inject,
  OnDestroy,
  OnInit,
  signal,
  ViewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { highlightXml } from './workflow-xml-highlight';
import { toLocalDateTime, toUtcDateTime } from './workflow-date.util';
import { WorkflowIntegrationEditorComponent } from '../integrations/workflow-integration-editor.component';
import { WorkflowBusinessActivityComponent } from '../workspace/workflow-business-activity.component';
import { WorkflowGeographyComponent } from '../workspace/workflow-geography.component';
import { WorkspaceSettings } from '../workspace/workflow-workspace.service';
import { WorkflowVariableEditorComponent } from './workflow-variable-editor.component';
import { catchError, debounceTime, Observable, of, Subject, switchMap, throwError } from 'rxjs';
import { tap } from 'rxjs/operators';
import { canvasWorldSize, layoutWorkflowGraph, mapCanvasIdsToActivities, nodesAreCollapsed } from './workflow-canvas-layout';
import { isRedirectOutcome, REDIRECT_OUTCOME_KEY } from './workflow-outcome.util';
import { workflowApiErrorMessage } from '../workflow-api-error';
import { WorkflowVersionsService } from '../workflow-versions.service';
import { WorkflowDefinitionsService } from '../workflow-definitions.service';
import { WorkflowActionsCatalogService } from '../workflow-actions-catalog.service';
import { WorkflowBindingsService } from '../workflow-bindings.service';
import { WorkflowSlaPoliciesService } from '../workflow-sla-policies.service';
import type { ActivityDefinitionDto } from '@shared/models/models/Workflow/Application/DTOs/activity-definition-dto';
import type { WorkflowVersionDetailDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-detail-dto';
import type { WorkflowVersionDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-version-dto';
import type { WorkflowValidationResultDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-validation-result-dto';
import type { WorkflowActionCatalogEntryDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-action-catalog-entry-dto';
import type { WorkflowAssignmentGroupDto } from '@shared/models/models/Workflow/Application/DTOs/workflow-assignment-group-dto';
import type { SlaPolicyDto } from '@shared/models/models/Workflow/Application/DTOs/sla-policy-dto';
import { ToastService } from '@core/notifications/toast.service';
import { ThemeService } from '@core/theme/theme.service';
import { LocaleService } from '@core/i18n/locale.service';

// ── Canvas data types ────────────────────────────────────────────────────────

export type NodeType =
  | 'Start'
  | 'UserTask'
  | 'MainActivity'
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

export type FormFieldType = 'text' | 'textarea' | 'number' | 'date' | 'select';

export interface FormFieldRow {
  id: string;
  key: string;
  labelEn: string;
  labelAr: string;
  type: FormFieldType;
  required: boolean;
  options?: string;
}

export type TimerTypeOption = 'DueDate' | 'Duration' | 'ExternalSignal';
export type NotificationFailurePolicyOption = 'Continue' | 'Retry' | 'FailWorkflow';
export type InspectorTab = 'general' | 'assignment' | 'outcomes' | 'actions' | 'sla' | 'data' | 'form' | 'advanced';
export type BottomTab = 'variables' | 'validation';

export interface CanvasOutcome {
  id: string;
  key: string;
  label: string;
  labelAr: string;
  description: string;
  descriptionAr: string;
  sortOrder: number;
  isDefault: boolean;
  requiresComment: boolean;
  requiresAttachment: boolean;
  isActive: boolean;
  resultValue: string;
}

export interface CanvasAction {
  id: string;
  actionKey: string;
  executionTrigger: string;
  outcomeKey: string;
  conditionExpression: string;
  sequence: number;
  inputMappingJson: string;
  outputMappingJson: string;
  failurePolicy: string;
  retryCount: number;
  retryDelaySeconds: number;
  timeoutSeconds: number;
  isActive: boolean;
}

export interface CanvasVariable {
  id: string;
  variableKey: string;
  name: string;
  nameAr: string;
  dataType: 'String' | 'Number' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime' | 'Guid' | 'Json';
  defaultValue: string;
  isRequired: boolean;
  isSensitive: boolean;
  description: string;
  descriptionAr: string;
}

export interface CanvasNode {
  id: string;
  nodeKey: string;
  type: NodeType;
  name: string;
  nameAr: string;
  actionKey: string;
  assignmentKey: string;
  assignmentGroupId: string;
  assignmentPurpose: string;
  configurationJson: string;
  x: number;
  y: number;
  outcomes: CanvasOutcome[];
  actions: CanvasAction[];
}

export interface CanvasEdge {
  id: string;
  fromNodeId: string;
  toNodeId: string;
  transitionKey: string;
  labelEn: string;
  labelAr: string;
  descriptionEn: string;
  outcomeKey: string;
  conditionExpression: string;
  isDefault: boolean;
  priority: number;
}

export interface CanvasState {
  workspace?: WorkspaceSettings;
  nodes: CanvasNode[];
  edges: CanvasEdge[];
  variables: CanvasVariable[];
}

// ── Node dimensions ──────────────────────────────────────────────────────────
const NODE_W    = 210;
const NODE_H    = 84;
const GATEWAY_R = 34;

// ── XML builder ──────────────────────────────────────────────────────────────
export function buildNWFMXml(state: CanvasState): string {
  const nodes = state.nodes.map(n => {
    const attrs = [
      `nodeKey="${n.nodeKey}"`,
      `type="${n.type}"`,
      `name="${escXml(n.name)}"`,
      n.nameAr ? `nameAr="${escXml(n.nameAr)}"` : '',
      n.actionKey ? `actionKey="${escXml(n.actionKey)}"` : '',
      n.assignmentKey ? `assignmentKey="${escXml(n.assignmentKey)}"` : '',
      n.assignmentGroupId ? `assignmentGroupId="${escXml(n.assignmentGroupId)}"` : '',
      n.assignmentPurpose ? `assignmentPurpose="${escXml(n.assignmentPurpose)}"` : '',
      n.configurationJson ? `configurationJson="${escXml(n.configurationJson)}"` : '',
      `positionX="${Math.round(n.x)}"`,
      `positionY="${Math.round(n.y)}"`,
    ].filter(Boolean).join(' ');

    const outcomeLines = (n.outcomes ?? []).map(o => {
      const oAttrs = [
        `key="${escXml(o.key)}"`,
        `name="${escXml(o.label)}"`,
        o.labelAr ? `nameAr="${escXml(o.labelAr)}"` : '',
        `order="${o.sortOrder}"`,
        o.isDefault ? 'isDefault="true"' : '',
        o.requiresComment ? 'requiresComment="true"' : '',
        o.requiresAttachment ? 'requiresAttachment="true"' : '',
        o.resultValue ? `resultValue="${escXml(o.resultValue)}"` : '',
      ].filter(Boolean).join(' ');
      return `        <Outcome ${oAttrs} />`;
    });

    const actionLines = (n.actions ?? []).filter(a => a.isActive !== false).map(a => {
      const aAttrs = [
        `key="${escXml(a.actionKey)}"`,
        `trigger="${escXml(a.executionTrigger)}"`,
        `sequence="${a.sequence}"`,
        a.outcomeKey ? `outcomeKey="${escXml(a.outcomeKey)}"` : '',
        a.conditionExpression ? `condition="${escXml(a.conditionExpression)}"` : '',
        a.failurePolicy ? `failurePolicy="${escXml(a.failurePolicy)}"` : '',
        a.retryCount ? `retryCount="${a.retryCount}"` : '',
        a.retryDelaySeconds ? `retryDelaySeconds="${a.retryDelaySeconds}"` : '',
        a.timeoutSeconds ? `timeoutSeconds="${a.timeoutSeconds}"` : '',
        a.inputMappingJson ? `inputMapping="${escXml(a.inputMappingJson)}"` : '',
        a.outputMappingJson ? `outputMapping="${escXml(a.outputMappingJson)}"` : '',
      ].filter(Boolean).join(' ');
      return `        <Action ${aAttrs} />`;
    });

    const assignmentLines = (n.assignmentGroupId || n.assignmentKey)
      ? [`        <AssignmentRule assigneeType="AssignmentGroup"${n.assignmentGroupId ? ` assignmentGroupId="${escXml(n.assignmentGroupId)}"` : ''}${n.assignmentKey ? ` assignmentKey="${escXml(n.assignmentKey)}"` : ''}${n.assignmentPurpose ? ` assignmentPurpose="${escXml(n.assignmentPurpose)}"` : ''} />`]
      : [];

    const hasChildren = outcomeLines.length > 0 || actionLines.length > 0 || assignmentLines.length > 0;
    if (!hasChildren) return `    <Activity ${attrs} />`;

    const children: string[] = [];
    if (assignmentLines.length > 0) {
      children.push(assignmentLines.join('\n'));
    }
    if (outcomeLines.length > 0) {
      children.push(`      <Outcomes>\n${outcomeLines.join('\n')}\n      </Outcomes>`);
    }
    if (actionLines.length > 0) {
      children.push(`      <Actions>\n${actionLines.join('\n')}\n      </Actions>`);
    }
    return `    <Activity ${attrs}>\n${children.join('\n')}\n    </Activity>`;
  }).join('\n');

  const transitions = state.edges.map(e => {
    const from = state.nodes.find(n => n.id === e.fromNodeId);
    const to   = state.nodes.find(n => n.id === e.toNodeId);
    if (!from || !to) return '';
    const attrs = [
      `key="${e.transitionKey}"`,
      `from="${from.nodeKey}"`,
      `to="${to.nodeKey}"`,
      e.isDefault ? 'isDefault="true"' : '',
      e.outcomeKey && e.outcomeKey !== e.transitionKey ? `outcomeKey="${escXml(e.outcomeKey)}"` : '',
      e.conditionExpression ? `condition="${escXml(e.conditionExpression)}"` : '',
      e.labelEn ? `label="${escXml(e.labelEn)}"` : '',
      e.labelAr ? `labelAr="${escXml(e.labelAr)}"` : '',
      `priority="${e.priority}"`,
    ].filter(Boolean).join(' ');
    return `    <Transition ${attrs} />`;
  }).filter(Boolean).join('\n');

  const variableLines = (state.variables ?? []).map(v => {
    const attrs = [
      `key="${escXml(v.variableKey)}"`,
      v.name ? `name="${escXml(v.name)}"` : '',
      v.nameAr ? `nameAr="${escXml(v.nameAr)}"` : '',
      `dataType="${v.dataType}"`,
      v.defaultValue ? `defaultValue="${escXml(v.defaultValue)}"` : '',
      v.isRequired  ? 'isRequired="true"'  : '',
      v.isSensitive ? 'isSensitive="true"' : '',
      v.description ? `description="${escXml(v.description)}"` : '',
      v.descriptionAr ? `descriptionAr="${escXml(v.descriptionAr)}"` : '',
    ].filter(Boolean).join(' ');
    return `    <Variable ${attrs} />`;
  }).join('\n');

  const variablesBlock = variableLines
    ? `  <Variables>\n${variableLines}\n  </Variables>\n`
    : '';

  return `<?xml version="1.0" encoding="utf-8"?>
<WorkflowDefinition xmlns="https://privora.io/workflow/v1"${state.workspace ? ` workspaceJson="${escXml(JSON.stringify(state.workspace))}"` : ''}>
  <Activities>
${nodes}
  </Activities>
  <Transitions>
${transitions}
  </Transitions>
${variablesBlock}</WorkflowDefinition>`;
}

function escXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// ── Component ────────────────────────────────────────────────────────────────

@Component({
  selector: 'app-workflow-designer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [WorkflowActivitySlaComponent, FormsModule, ReactiveFormsModule, TranslateModule, RouterLink, WorkflowIntegrationEditorComponent, WorkflowVariableEditorComponent, WorkflowBusinessActivityComponent, WorkflowGeographyComponent],
  templateUrl: './workflow-designer.component.html',
  styleUrls: ['./workflow-designer.component.css'],
})
export class WorkflowDesignerComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('canvasSvg') canvasSvgRef!: ElementRef<SVGSVGElement>;
  @ViewChild('canvasScroll') canvasScrollRef?: ElementRef<HTMLDivElement>;

  private readonly route              = inject(ActivatedRoute);
  private readonly router             = inject(Router);
  private readonly versionsService    = inject(WorkflowVersionsService);
  private readonly definitionsService = inject(WorkflowDefinitionsService);
  private readonly catalogService     = inject(WorkflowActionsCatalogService);
  private readonly bindingsService    = inject(WorkflowBindingsService);
  private readonly slaPoliciesService = inject(WorkflowSlaPoliciesService);
  private readonly toast              = inject(ToastService);
  private readonly translate          = inject(TranslateService);
  private readonly fb                 = inject(FormBuilder);
  private readonly destroyRef         = inject(DestroyRef);
  protected readonly theme            = inject(ThemeService);
  protected readonly locale           = inject(LocaleService);

  // ── Route params
  protected readonly definitionId = signal('');
  protected readonly versionId    = signal('');

  // ── Version state
  protected readonly version        = signal<WorkflowVersionDetailDto | null>(null);
  protected readonly definitionName = signal('');
  protected readonly isLoading      = signal(true);
  protected readonly loadError      = signal<string | null>(null);
  protected readonly isReadonly     = computed(() => this.version()?.status !== 'Draft');

  // ── Lifecycle actions
  protected readonly publishing = signal(false);
  protected readonly cloning    = signal(false);
  protected readonly retiring   = signal(false);

  // ── Save state
  protected readonly saveStatus = signal<'saved' | 'unsaved' | 'saving'>('saved');
  private readonly autosave$    = new Subject<void>();

  // ── Canvas state
  protected readonly workspace = signal<WorkspaceSettings | undefined>(undefined);
  protected updateWorkspace(settings: WorkspaceSettings): void { if (!this.isReadonly()) { this.workspace.set(settings); this.markUnsaved(); } }
  protected readonly nodes     = signal<CanvasNode[]>([]);
  protected readonly edges     = signal<CanvasEdge[]>([]);
  protected readonly variables = signal<CanvasVariable[]>([]);
  protected readonly canvasWorld = computed(() => canvasWorldSize(this.nodes()));

  // ── Selection
  protected readonly selectedNodeId = signal<string | null>(null);
  protected readonly selectedEdgeId = signal<string | null>(null);
  protected readonly selectedNode   = computed(() =>
    this.nodes().find(n => n.id === this.selectedNodeId()) ?? null);
  protected readonly selectedEdge   = computed(() =>
    this.edges().find(e => e.id === this.selectedEdgeId()) ?? null);

  // ── Inspector state
  protected readonly inspectorTab = signal<InspectorTab>('general');

  // ── Bottom drawer
  protected readonly bottomTab        = signal<BottomTab>('validation');
  protected readonly showBottomDrawer = signal(false);

  // ── Drag state (not signal — updated at 60fps)
  private dragging: { nodeId: string; startX: number; startY: number; origPositions: Record<string, { x: number; y: number }> } | null = null;

  // ── Connection drawing
  protected readonly drawingEdge = signal<{
    fromNodeId: string;
    x1: number;
    y1: number;
    x2: number;
    y2: number;
    outcomeKey?: string;
  } | null>(null);
  /** Node under cursor while dragging a connection (drop target highlight). */
  protected readonly connectHoverNodeId = signal<string | null>(null);

  // ── Pan/Zoom
  protected readonly panX = signal(0);
  protected readonly panY = signal(0);
  protected readonly zoom = signal(1);
  private panning: { startX: number; startY: number; origPanX: number; origPanY: number } | null = null;

  // ── Node properties form
  protected readonly propsForm = this.fb.group({
    name:                   ['', Validators.required],
    nameAr:                 [''],
    description:            [''],
    descriptionAr:          [''],
    instructionsEn:         [''],
    instructionsAr:         [''],
    nodePriority:           [0],
    actionKey:              [''],
    assignmentKey:          [''],
    assignmentGroupId:      [''],
    assignmentPurpose:      [''],
    fallbackAssignmentKey:  [''],
    requiresClaim:          [false],
    allowSelfClaim:         [false],
    configJson:             [''],
    timerType:              ['Duration' as TimerTypeOption],
    dueAt:                  [''],
    duration:               [''],
    signalKey:              [''],
    templateKey:            [''],
    failurePolicy:          ['Continue' as NotificationFailurePolicyOption],
    slaPolicyId:            [''],
    slaDurationHours:       [0],
    slaEscalationKey:       [''],
    inputMappingJson:       [''],
    outputMappingJson:      [''],
    formKey:                [''],
    formSchemaJson:         [''],
    setVariablesJson:       [''],
    eventKey:               [''],
    correlationVariable:    [''],
    definitionKey:          [''],
    waitForCompletion:      [true],
    callVersionId:          [''],
    callInputMappingsJson:  ['{}'],
    callOutputMappingsJson: ['{}'],
  });

  protected readonly formFieldTypes: FormFieldType[] = ['text', 'textarea', 'number', 'date', 'select'];
  protected readonly formFields = signal<FormFieldRow[]>([]);

  // ── Transition (edge) form
  protected readonly edgeForm = this.fb.group({
    transitionKey:       [''],
    labelEn:             [''],
    labelAr:             [''],
    descriptionEn:       [''],
    outcomeKey:          [''],
    conditionExpression: [''],
    isDefault:           [false],
    priority:            [0],
  });

  // ── Enums / options
  protected readonly timerTypeOptions: TimerTypeOption[] = ['DueDate', 'Duration'];
  protected readonly notifFailurePolicyOptions: NotificationFailurePolicyOption[] = ['Continue', 'Retry', 'FailWorkflow'];
  protected readonly executionTriggerOptions = ['OnEnter', 'OnComplete', 'OnOutcome', 'OnFailure'];
  protected readonly actionFailurePolicyOptions = ['Continue', 'Retry', 'FailActivity', 'FailWorkflow', 'CreateIncident'];
  protected readonly variableTypes: CanvasVariable['dataType'][] = ['String', 'Integer', 'Decimal', 'Boolean', 'Date', 'DateTime', 'Guid', 'Json'];

  // ── Inspector tab definitions
  protected readonly userTaskTabs: { id: InspectorTab; labelKey: string; helpKey: string }[] = [
    { id: 'general',    labelKey: 'workflow.designer.tab_general',    helpKey: 'workflow.designer.tab_help_general'    },
    { id: 'assignment', labelKey: 'workflow.designer.tab_assignment', helpKey: 'workflow.designer.tab_help_assignment' },
    { id: 'actions',    labelKey: 'workflow.designer.tab_actions',    helpKey: 'workflow.designer.tab_help_actions'    },
    { id: 'sla',        labelKey: 'workflow.designer.tab_sla',        helpKey: 'workflow.designer.tab_help_sla'        },
  ];
  protected readonly activeTabHelpKey = computed(() =>
    this.userTaskTabs.find(t => t.id === this.inspectorTab())?.helpKey
    ?? 'workflow.designer.tab_help_general',
  );

  // ── Palette
  protected readonly paletteNodes: { type: NodeType; protocol?: string; labelKey: string; label: string; category: string; descKey: string }[] = [
    { type: 'Start',            labelKey: 'workflow.designer.node_start',             label: 'Start',          category: 'flow',       descKey: 'workflow.designer.node_desc_start'            },
    { type: 'End',              labelKey: 'workflow.designer.node_end',               label: 'End',            category: 'flow',       descKey: 'workflow.designer.node_desc_end'              },
    { type: 'ExclusiveGateway', labelKey: 'workflow.designer.node_exclusive_gateway', label: 'Decision',           category: 'flow',       descKey: 'workflow.designer.node_desc_exclusive_gateway'},
    { type: 'InclusiveGateway', labelKey: 'workflow.designer.node_inclusive_gateway', label: 'Inclusive Gateway',  category: 'flow',       descKey: 'workflow.designer.node_desc_inclusive_gateway'},
    { type: 'ParallelGateway',  labelKey: 'workflow.designer.node_parallel_gateway',  label: 'Parallel Fork',      category: 'flow',       descKey: 'workflow.designer.node_desc_parallel_gateway' },
    { type: 'JoinGateway',      labelKey: 'workflow.designer.node_join_gateway',      label: 'Join',               category: 'flow',       descKey: 'workflow.designer.node_desc_join_gateway'     },
    { type: 'MainActivity', labelKey: 'workflow.designer.node_main_activity', label: 'Main Activity', category: 'tasks', descKey: 'workflow.designer.node_desc_main_activity' },
    { type: 'UserTask',         labelKey: 'workflow.designer.node_user_task',         label: 'User Task',          category: 'tasks',      descKey: 'workflow.designer.node_desc_user_task'        },
    { type: 'ServiceTask', protocol: 'Sms', labelKey: 'workflow.designer.node_sms', label: 'SMS', category: 'events', descKey: 'workflow.designer.node_desc_sms' },
    { type: 'ServiceTask',      labelKey: 'workflow.designer.node_service_task',      label: 'API Request',       category: 'automation', descKey: 'workflow.designer.node_desc_service_task'     },
    { type: 'NotificationTask', labelKey: 'workflow.designer.node_notification_task', label: 'Email',       category: 'automation', descKey: 'workflow.designer.node_desc_notification_task'},
    { type: 'Timer',            labelKey: 'workflow.designer.node_timer',             label: 'Timer',              category: 'events',     descKey: 'workflow.designer.node_desc_timer'            },
    { type: 'WaitEvent',        labelKey: 'workflow.designer.node_wait_event',        label: 'Wait for Event',     category: 'events',     descKey: 'workflow.designer.node_desc_wait_event'       },
  ];

  protected readonly paletteCategories = [
    { id: 'flow',       labelKey: 'workflow.designer.palette_cat_flow'       },
    { id: 'tasks',      labelKey: 'workflow.designer.palette_cat_tasks'      },
    { id: 'automation', labelKey: 'workflow.designer.palette_cat_automation' },
    { id: 'events',     labelKey: 'workflow.designer.palette_cat_events'     },
  ];

  protected readonly paletteSearch = signal('');
  protected readonly filteredPalette = computed(() => {
    const q = this.paletteSearch().toLowerCase().trim();
    if (!q) return this.paletteNodes;
    return this.paletteNodes.filter(p =>
      p.label.toLowerCase().includes(q) || p.type.toLowerCase().includes(q)
    );
  });
  protected readonly filteredPaletteByCategory = computed(() => {
    const nodes = this.filteredPalette();
    return this.paletteCategories.map(cat => ({
      ...cat,
      nodes: nodes.filter(n => n.category === cat.id),
    })).filter(c => c.nodes.length > 0);
  });

  // ── Validation
  protected readonly validationResult = signal<WorkflowValidationResultDto | null>(null);
  protected readonly validating       = signal(false);

  // ── Active main tab (designer | xml)
  protected readonly activeTab  = signal<'designer' | 'xml'>('designer');
  protected readonly xmlContent = signal('');
  protected readonly xmlWrap    = signal(true);
  protected readonly xmlCopied  = signal(false);
  protected readonly xmlLines   = computed(() => highlightXml(this.xmlContent()));
  protected readonly xmlStats   = computed(() => ({
    activities:  this.nodes().length,
    transitions: this.edges().length,
    variables:   this.variables().length,
  }));
  protected readonly connectGuideDismissed = signal(false);
  protected readonly showConnectGuide = computed(() =>
    !this.isReadonly()
    && this.activeTab() === 'designer'
    && this.nodes().length >= 2
    && this.edges().length === 0
    && !this.connectGuideDismissed()
    && !this.drawingEdge()
    && !this.showOnboarding()
    && !this.showTour()
  );

  // ── Delete confirmation
  protected readonly deleteTarget = signal<CanvasNode | null>(null);

  // ── Action catalog
  protected readonly actionCatalog    = signal<WorkflowActionCatalogEntryDto[]>([]);
  protected readonly catalogLoading   = signal(false);

  protected readonly assignmentGroups        = signal<WorkflowAssignmentGroupDto[]>([]);
  protected readonly assignmentGroupsLoading = signal(false);
  protected readonly organizationId          = signal('');

  // ── SLA policies
  protected readonly slaPolicies        = signal<SlaPolicyDto[]>([]);
  protected readonly slaPoliciesLoading = signal(false);

  // ── Undo/redo stacks
  private undoStack: CanvasState[] = [];
  private redoStack: CanvasState[] = [];

  // ── Clipboard
  protected readonly clipboard = signal<CanvasNode | null>(null);

  // ── Multi-select
  protected readonly selectedNodeIds = signal<string[]>([]);
  protected readonly selectedNodes   = computed(() =>
    this.nodes().filter(n => this.selectedNodeIds().includes(n.id)));

  // ── Marquee
  protected readonly marquee = signal<{ x: number; y: number; w: number; h: number } | null>(null);
  private marqueeStartPos: { clientX: number; clientY: number; canvasX: number; canvasY: number } | null = null;

  // ── Context menu
  protected readonly contextMenu = signal<{
    x: number;
    y: number;
    canvasX?: number;
    canvasY?: number;
    nodeId?: string;
    edgeId?: string;
  } | null>(null);

  /** Properties panel only when a node or edge is selected. */
  protected readonly inspectorVisible = computed(
    () => !!this.selectedNode() || !!this.selectedEdge(),
  );

  // ── Tool mode
  protected readonly activeTool = signal<'select' | 'pan'>('select');

  // ── Onboarding / tour
  protected readonly onboardingDismissed = signal(false);
  protected readonly showTour = signal(false);
  protected readonly tourStep = signal(0);
  protected readonly tourTotal = 6;
  protected readonly tourDots = [0, 1, 2, 3, 4, 5] as const;
  protected readonly showOnboarding = computed(() =>
    this.nodes().length === 0 && !this.isLoading() && !this.onboardingDismissed()
  );
  protected readonly tourProgressPct = computed(() =>
    Math.round(((this.tourStep() + 1) / this.tourTotal) * 100)
  );

  // ── Inspector
  protected readonly inspectorWidth = signal(460);

  // ── Palette collapse
  protected readonly paletteCollapsed = signal(false);

  // ── Fullscreen (covers admin shell with fixed overlay)
  protected readonly isFullscreen = signal(false);

  // ── Quick-add node picker
  protected readonly quickAddSourceId = signal<string | null>(null);

  // ── Bottom drawer height (resizable)
  protected readonly drawerHeight = signal(260);
  protected readonly MIN_DRAWER_H    = 120;
  protected readonly MAX_DRAWER_H    = 500;
  protected readonly MIN_INSPECTOR_W = 400;
  protected readonly MAX_INSPECTOR_W = 640;

  // ── Resize state (mutable – updated at 60fps, not signals)
  private inspectorResizing     = false;
  private inspectorResizeStartX = 0;
  private inspectorResizeStartW = 0;
  private drawerResizing        = false;
  private drawerResizeStartY    = 0;
  private drawerResizeStartH    = 0;

  // ── Seed IDs (for GUID panel)

  // ── Expose constants to template
  protected readonly NODE_W    = NODE_W;
  protected readonly NODE_H    = NODE_H;
  protected readonly GATEWAY_R = GATEWAY_R;

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.definitionId.set(params.get('definitionId') ?? '');
      this.versionId.set(params.get('versionId') ?? '');
      this.selectedNodeId.set(null);
      this.selectedEdgeId.set(null);
      this.selectedNodeIds.set([]);
      this.validationResult.set(null);
      this.saveStatus.set('saved');
      this.undoStack = [];
      this.redoStack = [];
      this.loadVersion();
      this.loadDefinitionName();
    });
    this.loadActionCatalog();
    this.loadSlaPolicies();

    this.autosave$.pipe(
      debounceTime(2000),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(() => {
      this.saveToBackend().subscribe();
    });
  }

  ngAfterViewInit(): void {}
  ngOnDestroy(): void {
    this.setFullscreen(false);
  }

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.saveStatus() === 'unsaved') event.preventDefault();
  }

  protected toggleFullscreen(): void {
    this.setFullscreen(!this.isFullscreen());
  }

  private setFullscreen(on: boolean): void {
    this.isFullscreen.set(on);
    document.body.style.overflow = on ? 'hidden' : '';
  }

  // ── Load ─────────────────────────────────────────────────────────────────

  private loadVersion(): void {
    this.isLoading.set(true);
    const requestedVersion = this.versionId();
    this.versionsService.getById(this.definitionId(), requestedVersion)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: v => {
          if (this.versionId() !== requestedVersion) return;
          this.version.set(v);
          try { this.workspace.set(v.workspaceJson ? JSON.parse(v.workspaceJson) : undefined); } catch { this.workspace.set(undefined); }
          this.isLoading.set(false);
          this.initCanvasFromVersion(v);
          if (v.status === 'Draft' && this.workspace()) { this.workspace.update(w => ({...w!, designerVersion: 2})); this.ensureSimpleActions(); this.convertEmbeddedEvents(); }
          this.relayoutIfCollapsed();
          if (v.status !== 'Draft') {
            this.xmlContent.set(buildNWFMXml({ nodes: this.nodes(), edges: this.edges(), variables: this.variables(), workspace: this.workspace() }));
          }
        },
        error: (err: { error?: { message?: string } }) => {
          this.loadError.set(err?.error?.message ?? 'Failed to load version.');
          this.isLoading.set(false);
        },
      });
  }

  private initCanvasFromVersion(v: WorkflowVersionDetailDto): void {
    if (v.designerJson) {
      try {
        const state = JSON.parse(v.designerJson) as CanvasState;
        const merged = this.mergeWithBackend(state, v);
        this.nodes.set(merged.nodes.map(n => ({
          ...n,
          configurationJson: n.configurationJson ?? '',
          actionKey:         n.actionKey ?? '',
          assignmentKey:     n.assignmentKey ?? '',
          assignmentGroupId: n.assignmentGroupId ?? '',
          assignmentPurpose: n.assignmentPurpose ?? '',
          outcomes:          n.outcomes ?? [],
          actions:           n.actions ?? [],
        })));
        this.edges.set(merged.edges.map(e => ({
          ...e,
          labelEn:      e.labelEn      ?? '',
          labelAr:      e.labelAr      ?? '',
          descriptionEn: e.descriptionEn ?? '',
        })));
        this.variables.set(state.variables?.length ? state.variables : this.projectVariables(v));
        return;
      } catch { /* fall through */ }
    }

    this.nodes.set((v.activities ?? []).map((a, i) => this.activityToCanvasNode(a, i)));
    this.edges.set(this.transitionsToEdges(v));
    this.variables.set(this.projectVariables(v));
  }

  private projectVariables(v: WorkflowVersionDetailDto): CanvasVariable[] {
    return (v.variables ?? []).map(variable => ({ id: variable.id || crypto.randomUUID(), variableKey: variable.variableKey || '',
      name: variable.name || '', nameAr: variable.nameAr || '', dataType: (variable.dataType || 'String') as CanvasVariable['dataType'],
      defaultValue: variable.defaultValue || '', isRequired: !!variable.isRequired, isSensitive: !!variable.isSensitive,
      description: variable.description || '', descriptionAr: variable.descriptionAr || '' }));
  }

  private activityToCanvasNode(a: ActivityDefinitionDto, i: number): CanvasNode {
    const fallbackX = 120 + (i % 3) * 280;
    const fallbackY = 100 + Math.floor(i / 3) * 260;
    return {
      id:                a.id ?? `activity-${i}`,
      nodeKey:           a.nodeKey ?? `node-${i}`,
      type:              (a.activityType as NodeType) ?? 'UserTask',
      name:              a.name ?? '',
      nameAr:            a.nameAr ?? '',
      actionKey:         a.actionKey ?? '',
      assignmentKey:     a.assignmentRules?.[0]?.assignmentKey ?? '',
      assignmentGroupId: a.assignmentRules?.[0]?.referenceId ?? '',
      assignmentPurpose: (a.assignmentRules?.[0] as { assignmentPurpose?: string } | undefined)?.assignmentPurpose ?? '',
      configurationJson: a.configurationJson ?? '',
      x:                 a.positionX ?? fallbackX,
      y:                 a.positionY ?? fallbackY,
      outcomes: (a.outcomes ?? []).map(o => ({
        id:                 o.id ?? '',
        key:                o.outcomeKey ?? '',
        label:              o.name ?? '',
        labelAr:            o.nameAr ?? '',
        description:        o.description ?? '',
        descriptionAr:      o.descriptionAr ?? '',
        sortOrder:          o.sortOrder ?? 0,
        isDefault:          o.isDefault ?? false,
        requiresComment:    o.requiresComment ?? false,
        requiresAttachment: o.requiresAttachment ?? false,
        isActive:           o.isActive ?? true,
        resultValue:        o.resultValue ?? '',
      })),
      actions: (a.actions ?? []).map(ac => ({
        id:                  ac.id ?? '',
        actionKey:           ac.actionKey ?? '',
        executionTrigger:    String(ac.executionTrigger ?? 'OnComplete'),
        outcomeKey:          ac.outcomeKey ?? '',
        conditionExpression: ac.conditionExpression ?? '',
        sequence:            ac.sequence ?? 0,
        inputMappingJson:    ac.inputMappingJson ?? '',
        outputMappingJson:   ac.outputMappingJson ?? '',
        failurePolicy:       String(ac.failurePolicy ?? 'Continue'),
        retryCount:          ac.retryCount ?? 0,
        retryDelaySeconds:   ac.retryDelaySeconds ?? 0,
        timeoutSeconds:      ac.timeoutSeconds ?? 0,
        isActive:            ac.isActive ?? true,
      })),
    };
  }

  private transitionsToEdges(v: WorkflowVersionDetailDto): CanvasEdge[] {
    return (v.transitions ?? []).map((t, i) => ({
      id:                  t.id ?? `transition-${i}`,
      fromNodeId:          t.fromActivityDefinitionId ?? '',
      toNodeId:            t.toActivityDefinitionId ?? '',
      transitionKey:       t.transitionKey ?? `t-${i}`,
      labelEn:             '',
      labelAr:             '',
      descriptionEn:       '',
      outcomeKey:          t.transitionKey ?? `t-${i}`,
      conditionExpression: t.conditionExpression ?? '',
      isDefault:           t.isDefault ?? false,
      priority:            t.priority ?? 0,
    }));
  }

  private mergeWithBackend(state: CanvasState, v: WorkflowVersionDetailDto): CanvasState {
    const activities = v.activities ?? [];
    const idMap = mapCanvasIdsToActivities(state.nodes, activities);
    const keptKeys = new Set(
      state.nodes.filter(n => idMap.has(n.id)).map(n => n.nodeKey),
    );

    const nodes: CanvasNode[] = [];
    for (const n of state.nodes) {
      const backendId = idMap.get(n.id);
      if (!backendId) continue;
      nodes.push({ ...n, id: backendId });
    }

    activities.forEach((a, i) => {
      if (!a.id || !a.nodeKey || keptKeys.has(a.nodeKey)) return;
      nodes.push(this.activityToCanvasNode(a, i));
    });

    const nodeIds = new Set(nodes.map(n => n.id));
    const remapped = state.edges.map(e => ({
      ...e,
      fromNodeId: idMap.get(e.fromNodeId) ?? e.fromNodeId,
      toNodeId:   idMap.get(e.toNodeId) ?? e.toNodeId,
    })).filter(e => nodeIds.has(e.fromNodeId) && nodeIds.has(e.toNodeId));

    const edges = remapped.length > 0 ? remapped : this.transitionsToEdges(v);
    return { nodes, edges, variables: state.variables ?? [] };
  }

  private relayoutIfCollapsed(): void {
    if (!nodesAreCollapsed(this.nodes())) return;
    const posMap = layoutWorkflowGraph(this.nodes(), this.edges());
    this.nodes.update(all => all.map(n => posMap[n.id] ? { ...n, ...posMap[n.id] } : n));
    this.resetView();
    if (!this.isReadonly()) this.markUnsaved();
  }

  // ── Action catalog ────────────────────────────────────────────────────────

  private loadActionCatalog(): void {
    this.catalogLoading.set(true);
    this.catalogService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: entries => { this.actionCatalog.set(entries); this.catalogLoading.set(false); },
        error: () => this.catalogLoading.set(false),
      });
  }

  private loadDefinitionName(): void {
    const defId = this.definitionId();
    if (!defId) return;
    this.definitionsService.getById(defId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: d => {
          this.definitionName.set(d.name ?? '');
          const orgId = d.organizationId ?? '';
          this.organizationId.set(orgId);
          if (orgId) this.loadAssignmentGroups(orgId);
        },
        error: () => {},
      });
  }

  private loadAssignmentGroups(orgId: string): void {
    this.assignmentGroupsLoading.set(true);
    this.bindingsService.listOrgGroups(orgId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: groups => {
          this.assignmentGroups.set(groups.filter(g => g.isActive !== false));
          this.assignmentGroupsLoading.set(false);
        },
        error: () => this.assignmentGroupsLoading.set(false),
      });
  }

  protected assignmentGroupLabel(group: WorkflowAssignmentGroupDto): string {
    return group.name || group.code || group.id || '';
  }

  protected selectedAssignmentGroupName(groupId: string | null | undefined): string {
    if (!groupId) return '';
    const g = this.assignmentGroups().find(x => x.id === groupId);
    return g ? this.assignmentGroupLabel(g) : '';
  }

  protected onAssignmentGroupChange(groupId: string): void {
    const g = this.assignmentGroups().find(x => x.id === groupId);
    this.propsForm.patchValue({
      assignmentGroupId: groupId,
      assignmentKey: g?.code ?? '',
    });
  }

  private loadSlaPolicies(): void {
    this.slaPoliciesLoading.set(true);
    this.slaPoliciesService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: policies => { this.slaPolicies.set(policies.filter(p => p.isActive)); this.slaPoliciesLoading.set(false); },
        error: () => this.slaPoliciesLoading.set(false),
      });
  }

  // ── Copy to clipboard helper ──────────────────────────────────────────────

  protected copyIdToClipboard(id: string): void {
    navigator.clipboard.writeText(id).then(
      () => this.toast.success('Copied to clipboard'),
      () => this.toast.error('Copy failed'),
    );
  }

  protected startGuidedTour(): void {
    this.showTour.set(true);
    this.tourStep.set(0);
  }

  protected finishTour(): void {
    this.showTour.set(false);
  }

  protected dismissOnboarding(): void {
    this.onboardingDismissed.set(true);
    this.showTour.set(false);
  }

  protected nextTourStep(): void {
    if (this.tourStep() < this.tourTotal - 1) this.tourStep.update(s => s + 1);
  }

  protected prevTourStep(): void {
    if (this.tourStep() > 0) this.tourStep.update(s => s - 1);
  }

  protected goToTourStep(step: number): void {
    if (step >= 0 && step < this.tourTotal) this.tourStep.set(step);
  }

  protected addStartNode(): void {
    if (this.isReadonly()) return;
    const id = crypto.randomUUID();
    const node: CanvasNode = {
      id, nodeKey: 'start',
      type: 'Start', name: 'Start', nameAr: 'البداية',
      actionKey: '', assignmentKey: '', assignmentGroupId: '', assignmentPurpose: '', configurationJson: '',
      x: 200, y: 200, outcomes: [], actions: [],
    };
    this.pushUndo();
    this.nodes.update(ns => [...ns, node]);
    this.ensureSimpleActions();
    this.markUnsaved();
  }

  // ── Publish / Clone / Retire ──────────────────────────────────────────────

  protected publishVersion(): void {
    if (this.publishing() || this.isReadonly()) return;
    const v = this.version();
    if (!v) return;
    this.publishing.set(true);
    this.saveToBackend().pipe(
      switchMap(() => this.versionsService.validate(this.definitionId(), this.versionId())),
      switchMap(result => {
        this.validationResult.set(result);
        this.bottomTab.set('validation');
        this.showBottomDrawer.set(true);
        if (!result.isValid) {
          this.toast.error(
            this.translate.instant('workflow.designer.publish_blocked', {
              count: result.errors?.length ?? 0,
            }),
          );
          return throwError(() => result);
        }
        return this.versionsService.publish(this.definitionId(), this.versionId());
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: published => {
        this.version.set({ ...v, status: published.status });
        this.publishing.set(false);
        this.toast.success(this.translate.instant('workflow.designer.publish_ok'));
      },
      error: (err: unknown) => {
        this.publishing.set(false);
        if (err && typeof err === 'object' && 'isValid' in err) return;
        this.toast.error(workflowApiErrorMessage(err, this.translate, 'workflow.designer.publish_failed'));
      },
    });
  }

  protected cloneVersion(): void {
    if (this.cloning()) return;
    this.cloning.set(true);
    this.versionsService.clone(this.definitionId(), this.versionId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: cloned => {
          this.cloning.set(false);
          this.toast.success('Draft clone created. Opening…');
          this.router.navigate([
            '/admin/workflow/definitions',
            this.definitionId(),
            'versions',
            cloned.id,
            'designer',
          ]);
        },
        error: (err: { error?: { message?: string } }) => {
          this.cloning.set(false);
          this.toast.error(err?.error?.message ?? 'Clone failed.');
        },
      });
  }

  protected retireVersion(): void {
    if (this.retiring()) return;
    if (!confirm('Retire this version? It will no longer be usable for new bindings.')) return;
    this.retiring.set(true);
    this.versionsService.retire(this.definitionId(), this.versionId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: retired => {
          this.version.update(v => v ? { ...v, status: retired.status } : v);
          this.retiring.set(false);
          this.toast.success('Version retired.');
        },
        error: (err: { error?: { message?: string } }) => {
          this.retiring.set(false);
          this.toast.error(err?.error?.message ?? 'Retire failed.');
        },
      });
  }

  // ── Toolbar actions ──────────────────────────────────────────────────────

  protected save(): void { this.saveToBackend().subscribe(); }

  private saveToBackend(): Observable<WorkflowVersionDto | null> {
    if (this.isReadonly()) return of(null);
    this.ensureSimpleActions();
    this.saveStatus.set('saving');
    const state: CanvasState = { nodes: this.nodes(), edges: this.edges(), variables: this.variables(), workspace: this.workspace() };
    const xml = buildNWFMXml(state);
    const designerJson = JSON.stringify(state);
    return this.versionsService.saveXml(this.definitionId(), this.versionId(), xml, designerJson).pipe(
      tap(() => this.saveStatus.set('saved')),
      catchError(() => {
        this.saveStatus.set('unsaved');
        this.toast.error(this.translate.instant('workflow.designer.save_failed'));
        return throwError(() => new Error('save-failed'));
      }),
    );
  }

  protected validate(): void {
    if (this.validating()) return;
    this.validating.set(true);
    this.saveToBackend().pipe(
      switchMap(() => this.versionsService.validate(this.definitionId(), this.versionId())),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: result => {
        this.validating.set(false);
        this.validationResult.set(result);
        this.bottomTab.set('validation');
        this.showBottomDrawer.set(true);
        if (result.isValid) {
          this.toast.success(this.translate.instant('workflow.designer.validate_ok'));
        } else {
          this.toast.error(
            this.translate.instant('workflow.designer.validate_failed', {
              count: result.errors?.length ?? 0,
            }),
          );
        }
      },
      error: () => {
        this.validating.set(false);
        this.toast.error(this.translate.instant('workflow.designer.validate_failed_generic'));
      },
    });
  }

  protected openVariablesDrawer(): void {
    this.bottomTab.set('variables');
    this.showBottomDrawer.set(true);
  }

  protected switchTab(tab: 'designer' | 'xml'): void {
    if (tab === 'xml') {
      this.xmlContent.set(buildNWFMXml({ nodes: this.nodes(), edges: this.edges(), variables: this.variables(), workspace: this.workspace() }));
    }
    this.activeTab.set(tab);
  }

  protected copyXml(): void {
    const xml = this.xmlContent();
    if (!xml) return;
    navigator.clipboard.writeText(xml).then(
      () => {
        this.xmlCopied.set(true);
        this.toast.success(this.translate.instant('workflow.designer.xml_copied'));
        window.setTimeout(() => this.xmlCopied.set(false), 2000);
      },
      () => this.toast.error(this.translate.instant('workflow.designer.xml_copy_failed')),
    );
  }

  protected dismissConnectGuide(): void {
    this.connectGuideDismissed.set(true);
  }

  // ── Canvas interactions ──────────────────────────────────────────────────

  private convertEmbeddedEvents():void {
    const added:CanvasNode[]=[];
    this.nodes.update(nodes=>nodes.map(n=>{const c=this.parseConfig(n.configurationJson) as any;if(!Array.isArray(c.events)||!c.events.length)return n;const remaining=[];
      for(const ev of c.events){if(!['Http','Soap','Sms','Email'].includes(ev.kind)){remaining.push(ev);continue;}
        const key='event_'+n.id.replaceAll('-','')+'_'+String(ev.id).replaceAll('-','');
        added.push({id:key,nodeKey:key,type:ev.kind==='Email'?'NotificationTask':'ServiceTask',name:ev.name||ev.kind,nameAr:'',actionKey:ev.kind==='Email'?'':'http.request',assignmentKey:'',assignmentGroupId:'',assignmentPurpose:'',outcomes:[],actions:[],x:n.x+300,y:n.y+140+added.length*120,configurationJson:JSON.stringify({...ev.configuration,protocol:ev.kind==='Http'?'Rest':ev.kind,required:ev.required??['Http','Soap'].includes(ev.kind),...(ev.kind==='Email'?{channels:'Email',failurePolicy:'Retry'}:{}),triggerBinding:{sourceNodeKey:n.nodeKey,trigger:ev.trigger}})});
      }return {...n,configurationJson:JSON.stringify({...c,events:remaining})};}));
    if(added.length){this.nodes.update(nodes=>[...nodes,...added]);this.markUnsaved();}
  }
  private ensureSimpleActions(): void {
    if (!this.workspace() || this.isReadonly()) return;
    this.nodes.update(nodes => nodes.map(n => {
      if(n.type!=='UserTask'&&n.type!=='MainActivity') return n;
      if(n.outcomes.some(o=>!['APPROVE','REJECT'].includes(o.key))) return n;
      const outcomes: CanvasOutcome[] = ['APPROVE','REJECT'].map((key,i)=>({...n.outcomes.find(o=>o.key===key),id:n.outcomes.find(o=>o.key===key)?.id||crypto.randomUUID(),key,label:i?'Reject':'Accept',labelAr:i?'رفض':'قبول',description:'',descriptionAr:'',sortOrder:i,isDefault:i===0,requiresComment:i===1,requiresAttachment:false,isActive:true,resultValue:key}));
      return {...n,outcomes};
    }));
  }
  protected rejectTarget(value:string):void {const n=this.selectedNode();if(!n)return;this.applyIntegrationConfiguration(JSON.stringify({...this.parseConfig(n.configurationJson),rejectTargetNodeKey:value}));}
  protected businessNodes(){return this.nodes().filter(n=>n.type==='UserTask'||n.type==='MainActivity');}
  protected activityTabKey(event:KeyboardEvent,index:number){if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();const step=(event.key==='ArrowRight'?1:-1)*(this.locale.isRtl()?-1:1);const next=event.key==='Home'?0:event.key==='End'?this.userTaskTabs.length-1:(index+step+this.userTaskTabs.length)%this.userTaskTabs.length;this.inspectorTab.set(this.userTaskTabs[next].id);(event.target as HTMLElement).parentElement?.querySelectorAll<HTMLButtonElement>('button')[next]?.focus();}
  protected rejectionDestinations(){
    const current=this.selectedNode();if(!current)return [];
    const reaches=(from:string,to:string)=>{const pending=[from],seen=new Set<string>();while(pending.length){const id=pending.pop()!;if(seen.has(id))continue;seen.add(id);for(const edge of this.edges().filter(e=>e.fromNodeId===id)){if(edge.toNodeId===to)return true;pending.push(edge.toNodeId);}}return false;};
    const business=this.businessNodes();return business.filter(n=>n.id===current.id?!business.some(b=>b.id!==n.id&&reaches(b.id,n.id)):reaches(n.id,current.id)&&!reaches(current.id,n.id));
  }
  protected selectedConfig(): any {return this.parseConfig(this.selectedNode()?.configurationJson||'{}');}
  protected resetSimpleActions(){const n=this.selectedNode();if(!n||this.isReadonly())return;this.nodes.update(nodes=>nodes.map(x=>x.id===n.id?{...x,outcomes:[],actions:[]}:x));this.ensureSimpleActions();this.markUnsaved();}
  protected eventTriggerLabel(trigger:string){const labels:Record<string,[string,string]>={OnEnter:['Entry','الدخول'],OnApprove:['Accept','القبول'],OnReject:['Reject','الرفض'],OnComment:['Comment','تعليق'],OnComplete:['Completion','الاكتمال'],OnFailure:['Failure','الفشل'],OnSlaReminder:['SLA reminder','تذكير الخدمة'],OnSlaBreach:['Overdue','تجاوز المدة']};return labels[trigger]?.[this.locale.locale()==='ar'?1:0]||trigger;}
  protected visualLinks(){const links:{id:string;path:string;label:string;x:number;y:number;target:string}[]=[];for(const n of this.nodes()){const c=this.parseConfig(n.configurationJson) as any;const b=c.triggerBinding;const source=b?this.nodes().find(x=>x.nodeKey===b.sourceNodeKey):n;const target=b?n:this.nodes().find(x=>x.nodeKey===c.rejectTargetNodeKey);if(!source||!target)continue;const x=source.x+105,y=source.y+84,tx=target.x+105,ty=target.y;links.push({id:n.id,path:source.id===target.id?`M ${x} ${y} C ${x+220} ${y+100}, ${tx+220} ${ty-100}, ${tx} ${ty}`:`M ${x} ${y} C ${x} ${y+60}, ${tx} ${ty-60}, ${tx} ${ty}`,label:b?this.eventTriggerLabel(b.trigger):(this.locale.locale()==='ar'?'رفض':'Reject'),x:(x+tx)/2,y:(y+ty)/2,target:b?n.id:source.id});}return links;}

  protected addNode(type: NodeType, x?: number, y?: number, protocol?: string): void {
    if (this.isReadonly()) return;
    const count = this.nodes().length;
    const px = x ?? 160 + (count % 4) * 48;
    const py = y ?? 140 + (count % 4) * 48;
    const key = `${type.toLowerCase()}_${Date.now()}`;
    const node: CanvasNode = {
      id: crypto.randomUUID(), nodeKey: key, type,
      name: this.defaultNodeName(type), nameAr: '',
      actionKey: '', assignmentKey: '', assignmentGroupId: '', assignmentPurpose: '', configurationJson: '',
      x: px, y: py, outcomes: [], actions: [],
    };
    this.pushUndo();
    if(type==='ServiceTask') { node.actionKey='http.request'; node.configurationJson=JSON.stringify({protocol:protocol||'Rest',required:protocol!=='Sms',method:'GET',path:'/',timeoutSeconds:30,maxAttempts:3,retryDelaySeconds:10}); }
    if(protocol==='Sms') node.name='SMS';
    if(type==='NotificationTask') node.configurationJson=JSON.stringify({channels:'Email',required:false,failurePolicy:'Retry',maxAttempts:3});
    this.nodes.update(ns => [...ns, node]);
    this.ensureSimpleActions();
    this.selectedNodeId.set(node.id);
    this.selectedEdgeId.set(null);
    this.inspectorTab.set('general');
    this.patchPropsFromNode(node);
    this.markUnsaved();
  }

  protected onPaletteDragStart(event: DragEvent, type: NodeType, protocol?:string): void {
    event.dataTransfer?.setData('nodeType', type);
    event.dataTransfer?.setData('nodeProtocol', protocol||'');
  }

  protected onCanvasDrop(event: DragEvent): void {
    event.preventDefault();
    const type = event.dataTransfer?.getData('nodeType') as NodeType;
    if (!type) return;
    const svg = this.canvasSvgRef?.nativeElement;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const x = (event.clientX - rect.left - this.panX()) / this.zoom();
    const y = (event.clientY - rect.top  - this.panY()) / this.zoom();
    this.addNode(type, x, y, event.dataTransfer?.getData('nodeProtocol'));
  }

  protected onCanvasDragOver(event: DragEvent): void { event.preventDefault(); }

  protected onNodeClick(event: MouseEvent, nodeId: string): void {
    event.stopPropagation();
    this.contextMenu.set(null);
    if (this.drawingEdge()) { this.finishEdge(nodeId); return; }

    const node = this.nodes().find(n => n.id === nodeId);

    if (event.shiftKey) {
      // Shift+click: toggle in multi-select
      const current = this.selectedNodeIds();
      if (current.includes(nodeId)) {
        const next = current.filter(id => id !== nodeId);
        this.selectedNodeIds.set(next);
        if (next.length > 0) {
          this.selectedNodeId.set(next[next.length - 1]);
          const lastNode = this.nodes().find(n => n.id === next[next.length - 1]);
          if (lastNode) this.patchPropsFromNode(lastNode);
        } else {
          this.selectedNodeId.set(null);
        }
      } else {
        this.selectedNodeIds.set([...current, nodeId]);
        this.selectedNodeId.set(nodeId);
        if (node) this.patchPropsFromNode(node);
      }
      this.selectedEdgeId.set(null);
      return;
    }

    // Normal click: single-select (clear multi-select)
    if (this.selectedNodeId() !== nodeId) {
      const userTaskOnlyTabs: InspectorTab[] = ['assignment', 'outcomes', 'actions', 'sla', 'data', 'form', 'advanced'];
      if ((node?.type !== 'UserTask' && node?.type !== 'MainActivity') && userTaskOnlyTabs.includes(this.inspectorTab())) {
        this.inspectorTab.set('general');
      }
    }
    this.selectedNodeIds.set([nodeId]);
    this.selectedNodeId.set(nodeId);
    this.selectedEdgeId.set(null);
    if (node) this.patchPropsFromNode(node);
    // Blur property fields so Delete/Backspace shortcuts work immediately
    this.blurActiveField();
  }

  private blurActiveField(): void {
    const ae = document.activeElement as HTMLElement | null;
    if (!ae) return;
    const tag = ae.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || ae.isContentEditable) {
      ae.blur();
    }
  }

  protected onCanvasClick(): void {
    this.contextMenu.set(null);
    if (this.drawingEdge()) {
      this.drawingEdge.set(null);
      this.connectHoverNodeId.set(null);
      return;
    }
    this.selectedNodeId.set(null);
    this.selectedEdgeId.set(null);
    this.selectedNodeIds.set([]);
  }

  protected onEdgeClick(event: MouseEvent, edgeId: string): void {
    event.stopPropagation();
    this.contextMenu.set(null);
    this.selectedEdgeId.set(edgeId);
    this.selectedNodeId.set(null);
    this.selectedNodeIds.set([]);
    const edge = this.edges().find(e => e.id === edgeId);
    if (edge) this.patchEdgeForm(edge);
  }

  protected onNodeContextMenu(event: MouseEvent, nodeId: string): void {
    event.preventDefault();
    event.stopPropagation();
    // Cancel connection drag so the menu (incl. Delete) is usable
    this.drawingEdge.set(null);
    this.connectHoverNodeId.set(null);
    this.blurActiveField();
    if (!this.selectedNodeIds().includes(nodeId)) {
      this.selectedNodeIds.set([nodeId]);
      this.selectedNodeId.set(nodeId);
      const node = this.nodes().find(n => n.id === nodeId);
      if (node) this.patchPropsFromNode(node);
    } else {
      this.selectedNodeId.set(nodeId);
    }
    this.selectedEdgeId.set(null);
    this.contextMenu.set({ x: event.clientX, y: event.clientY, nodeId });
  }

  protected onEdgeContextMenu(event: MouseEvent, edgeId: string): void {
    event.preventDefault();
    event.stopPropagation();
    this.contextMenu.set({ x: event.clientX, y: event.clientY, edgeId });
  }

  protected onCanvasContextMenu(event: MouseEvent): void {
    event.preventDefault();
    const svg = this.canvasSvgRef?.nativeElement;
    let canvasX = 0;
    let canvasY = 0;
    if (svg) {
      const rect = svg.getBoundingClientRect();
      canvasX = (event.clientX - rect.left - this.panX()) / this.zoom();
      canvasY = (event.clientY - rect.top  - this.panY()) / this.zoom();
    }
    this.contextMenu.set({ x: event.clientX, y: event.clientY, canvasX, canvasY });
  }

  protected closeContextMenu(): void { this.contextMenu.set(null); }

  protected contextNodeCanConnect(): boolean {
    const id = this.contextMenu()?.nodeId;
    if (!id) return false;
    const n = this.nodes().find(x => x.id === id);
    return !!n && this.canHaveEdge(n.type);
  }

  // ── Node dragging ─────────────────────────────────────────────────────────

  protected onNodeMouseDown(event: MouseEvent, nodeId: string): void {
    if (this.isReadonly()) return;
    if (event.button !== 0) return;
    // Port / connect-handle owns the event — do not start a node drag
    const t = event.target as Element | null;
    if (t?.classList?.contains('port-hit') || t?.closest?.('.port-hit')) return;
    if (this.drawingEdge()) return;
    event.stopPropagation();
    const ns = this.nodes();
    const node = ns.find(n => n.id === nodeId);
    if (!node) return;

    // Determine which nodes to drag: if this node is in the multi-select set, drag all; else drag only this one
    const dragIds = this.selectedNodeIds().includes(nodeId)
      ? this.selectedNodeIds()
      : [nodeId];

    const origPositions: Record<string, { x: number; y: number }> = {};
    for (const id of dragIds) {
      const n = ns.find(x => x.id === id);
      if (n) origPositions[id] = { x: n.x, y: n.y };
    }
    this.dragging = { nodeId, startX: event.clientX, startY: event.clientY, origPositions };
  }

  @HostListener('document:mousemove', ['$event'])
  onMouseMove(event: MouseEvent): void {
    if (this.inspectorResizing) {
      const dx = event.clientX - this.inspectorResizeStartX;
      const newW = Math.min(this.MAX_INSPECTOR_W, Math.max(this.MIN_INSPECTOR_W, this.inspectorResizeStartW - dx));
      this.inspectorWidth.set(newW);
      return;
    }
    if (this.drawerResizing) {
      const dy = event.clientY - this.drawerResizeStartY;
      const newH = Math.min(this.MAX_DRAWER_H, Math.max(this.MIN_DRAWER_H, this.drawerResizeStartH - dy));
      this.drawerHeight.set(newH);
      return;
    }
    // Live connection wire (must track outside SVG so the line never stalls)
    if (this.drawingEdge()) {
      this.updateDrawingEdge(event);
      return;
    }
    if (this.dragging) {
      const dx = (event.clientX - this.dragging.startX) / this.zoom();
      const dy = (event.clientY - this.dragging.startY) / this.zoom();
      const orig = this.dragging.origPositions;
      this.nodes.update(ns => ns.map(n => {
        const o = orig[n.id];
        return o ? { ...n, x: o.x + dx, y: o.y + dy } : n;
      }));
      this.autoScrollWhileDragging(event);
      return;
    }
    if (this.marqueeStartPos) {
      const svg = this.canvasSvgRef?.nativeElement;
      if (!svg) return;
      const rect = svg.getBoundingClientRect();
      const cx = (event.clientX - rect.left - this.panX()) / this.zoom();
      const cy = (event.clientY - rect.top  - this.panY()) / this.zoom();
      const sx = this.marqueeStartPos.canvasX;
      const sy = this.marqueeStartPos.canvasY;
      this.marquee.set({
        x: Math.min(sx, cx), y: Math.min(sy, cy),
        w: Math.abs(cx - sx), h: Math.abs(cy - sy),
      });
      return;
    }
    if (this.panning) {
      const dx = event.clientX - this.panning.startX;
      const dy = event.clientY - this.panning.startY;
      this.panX.set(this.panning.origPanX + dx);
      this.panY.set(this.panning.origPanY + dy);
    }
  }

  @HostListener('document:mouseup', ['$event'])
  onMouseUp(event: MouseEvent): void {
    if (this.inspectorResizing) { this.inspectorResizing = false; return; }
    if (this.drawerResizing)    { this.drawerResizing    = false; return; }
    if (this.drawingEdge()) {
      const targetId = this.hitTestNodeId(event.clientX, event.clientY);
      if (targetId && targetId !== this.drawingEdge()!.fromNodeId) {
        this.finishEdge(targetId);
      } else {
        this.drawingEdge.set(null);
        this.connectHoverNodeId.set(null);
      }
      return;
    }
    if (this.dragging) { this.dragging = null; this.markUnsaved(); }
    if (this.panning)  { this.panning  = null; }
    if (this.marqueeStartPos) {
      const m = this.marquee();
      if (m && (m.w > 4 || m.h > 4)) {
        const ids = this.nodes()
          .filter(n => this.nodeInMarquee(n, m))
          .map(n => n.id);
        this.selectedNodeIds.set(ids);
        if (ids.length === 1) {
          this.selectedNodeId.set(ids[0]);
          const nd = this.nodes().find(n => n.id === ids[0]);
          if (nd) this.patchPropsFromNode(nd);
        } else if (ids.length > 1) {
          this.selectedNodeId.set(ids[0]);
        } else {
          this.selectedNodeId.set(null);
        }
        this.selectedEdgeId.set(null);
      }
      this.marqueeStartPos = null;
      this.marquee.set(null);
    }
  }

  private nodeInMarquee(n: CanvasNode, m: { x: number; y: number; w: number; h: number }): boolean {
    const nx = this.isGateway(n.type) ? n.x - GATEWAY_R : n.x;
    const ny = this.isGateway(n.type) ? n.y - GATEWAY_R : n.y;
    const nw = this.isGateway(n.type) ? GATEWAY_R * 2 : NODE_W;
    const nh = this.isGateway(n.type) ? GATEWAY_R * 2 : NODE_H;
    return nx < m.x + m.w && nx + nw > m.x && ny < m.y + m.h && ny + nh > m.y;
  }

  // ── Canvas pan ────────────────────────────────────────────────────────────

  protected onCanvasMouseDown(event: MouseEvent): void {
    const onSvgRoot = (event.target as SVGElement).tagName === 'svg' ||
      ((event.target as SVGElement).closest('.node') === null &&
       (event.target as SVGElement).tagName !== 'circle' &&
       (event.target as SVGElement).tagName !== 'rect' &&
       (event.target as SVGElement).tagName !== 'polygon' &&
       (event.target as SVGElement).tagName !== 'path' &&
       !(event.target as SVGElement).classList?.contains('edge-group'));

    if (event.button === 1) {
      // Middle mouse always pans
      this.panning = { startX: event.clientX, startY: event.clientY, origPanX: this.panX(), origPanY: this.panY() };
      return;
    }
    if (event.button === 0 && !this.drawingEdge()) {
      if (this.activeTool() === 'pan' || event.altKey) {
        this.panning = { startX: event.clientX, startY: event.clientY, origPanX: this.panX(), origPanY: this.panY() };
      } else {
        // Select mode: start marquee on empty canvas area
        if (onSvgRoot) {
          const svg = this.canvasSvgRef?.nativeElement;
          if (!svg) return;
          const rect = svg.getBoundingClientRect();
          const cx = (event.clientX - rect.left - this.panX()) / this.zoom();
          const cy = (event.clientY - rect.top  - this.panY()) / this.zoom();
          this.marqueeStartPos = { clientX: event.clientX, clientY: event.clientY, canvasX: cx, canvasY: cy };
        }
      }
    }
  }

  // ── Zoom ──────────────────────────────────────────────────────────────────

  protected onWheel(event: WheelEvent): void {
    if (!(event.ctrlKey || event.metaKey)) return;
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.1 : 0.9;
    const svg = this.canvasSvgRef?.nativeElement;
    if (svg) {
      const rect = svg.getBoundingClientRect();
      const mx = event.clientX - rect.left;
      const my = event.clientY - rect.top;
      const oldZoom = this.zoom();
      const newZoom = Math.min(2, Math.max(0.25, oldZoom * factor));
      this.zoom.set(newZoom);
      this.panX.update(px => mx - (mx - px) * (newZoom / oldZoom));
      this.panY.update(py => my - (my - py) * (newZoom / oldZoom));
    } else {
      this.zoom.update(z => Math.min(2, Math.max(0.25, z * factor)));
    }
  }

  protected zoomIn():    void { this.zoom.update(z => Math.min(2, z * 1.2)); }
  protected zoomOut():   void { this.zoom.update(z => Math.max(0.25, z * 0.8)); }
  protected resetView(): void { this.panX.set(0); this.panY.set(0); this.zoom.set(1); }

  protected fitScreen(): void {
    const ns = this.nodes();
    if (ns.length === 0) { this.resetView(); return; }
    const scroller = this.canvasScrollRef?.nativeElement;
    const viewW = scroller?.clientWidth || this.canvasSvgRef?.nativeElement.clientWidth || 800;
    const viewH = scroller?.clientHeight || this.canvasSvgRef?.nativeElement.clientHeight || 600;
    const padding = 60;
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const n of ns) {
      if (this.isGateway(n.type)) {
        minX = Math.min(minX, n.x - GATEWAY_R); minY = Math.min(minY, n.y - GATEWAY_R);
        maxX = Math.max(maxX, n.x + GATEWAY_R); maxY = Math.max(maxY, n.y + GATEWAY_R);
      } else if (n.type === 'Start' || n.type === 'End') {
        minX = Math.min(minX, n.x - 24); minY = Math.min(minY, n.y - 24);
        maxX = Math.max(maxX, n.x + 24); maxY = Math.max(maxY, n.y + 24);
      } else {
        minX = Math.min(minX, n.x); minY = Math.min(minY, n.y);
        maxX = Math.max(maxX, n.x + NODE_W); maxY = Math.max(maxY, n.y + NODE_H);
      }
    }
    const contentW = maxX - minX + padding * 2;
    const contentH = maxY - minY + padding * 2;
    const newZoom = Math.max(0.4, Math.min(1, Math.min(viewW / contentW, viewH / contentH)));
    this.zoom.set(newZoom);
    this.panX.set(0);
    this.panY.set(0);
    queueMicrotask(() => {
      if (!scroller) return;
      scroller.scrollLeft = Math.max(0, minX * newZoom - padding);
      scroller.scrollTop = Math.max(0, minY * newZoom - padding);
    });
  }

  // ── Keyboard / clipboard ──────────────────────────────────────────────────

  protected duplicateSelected(): void {
    if (this.isReadonly()) return;
    const nodeId = this.selectedNodeId();
    if (!nodeId) return;
    const node = this.nodes().find(n => n.id === nodeId);
    if (!node) return;
    this.pushUndo();
    const newNode: CanvasNode = {
      ...structuredClone(node),
      id: crypto.randomUUID(),
      nodeKey: `${node.type.toLowerCase()}_${Date.now()}`,
      x: node.x + 40, y: node.y + 40,
    };
    this.nodes.update(ns => [...ns, newNode]);
    this.selectedNodeId.set(newNode.id);
    this.patchPropsFromNode(newNode);
    this.markUnsaved();
  }

  protected copySelected(): void {
    const nodeId = this.selectedNodeId();
    if (!nodeId) return;
    const node = this.nodes().find(n => n.id === nodeId);
    if (node) this.clipboard.set(structuredClone(node));
  }

  protected pasteClipboard(): void {
    const cb = this.clipboard();
    if (this.isReadonly() || !cb) return;
    this.pushUndo();
    const newNode: CanvasNode = {
      ...structuredClone(cb),
      id: crypto.randomUUID(),
      nodeKey: `${cb.type.toLowerCase()}_${Date.now()}`,
      x: cb.x + 60, y: cb.y + 60,
    };
    this.nodes.update(ns => [...ns, newNode]);
    this.selectedNodeId.set(newNode.id);
    this.patchPropsFromNode(newNode);
    this.markUnsaved();
  }

  // ── Edge drawing ─────────────────────────────────────────────────────────

  protected portOutPos(n: CanvasNode): { x: number; y: number } {
    if (this.isGateway(n.type)) return { x: n.x + GATEWAY_R + 10, y: n.y };
    if (n.type === 'Start' || n.type === 'End') return { x: n.x + 28, y: n.y };
    return { x: n.x + NODE_W + 10, y: n.y + NODE_H / 2 };
  }

  protected portInPos(n: CanvasNode): { x: number; y: number } {
    if (this.isGateway(n.type)) return { x: n.x - GATEWAY_R - 10, y: n.y };
    if (n.type === 'Start' || n.type === 'End') return { x: n.x - 28, y: n.y };
    return { x: n.x - 10, y: n.y + NODE_H / 2 };
  }

  protected startEdge(event: MouseEvent, fromNodeId: string, outcomeKey?: string): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.isReadonly() || event.button !== 0) return;
    const node = this.nodes().find(n => n.id === fromNodeId);
    if (!node || !this.canHaveEdge(node.type)) return;
    this.dragging = null;
    this.panning = null;
    this.marqueeStartPos = null;
    this.marquee.set(null);
    const port = outcomeKey
      ? this.decisionOutcomePorts(node).find(p => p.key === outcomeKey)
      : undefined;
    const p = port ?? this.portOutPos(node);
    this.connectHoverNodeId.set(null);
    this.drawingEdge.set({ fromNodeId, x1: p.x, y1: p.y, x2: p.x, y2: p.y, outcomeKey });
  }

  protected decisionOutcomePorts(n: CanvasNode): { key: string; label: string; x: number; y: number }[] {
    if (n.type !== 'ExclusiveGateway') return [];
    const outcomes = this.incomingUserTask(n.id)?.outcomes.filter(o => !!o.key) ?? [];
    if (outcomes.length === 0) return [];
    const spread = 22;
    return outcomes.map((o, i) => ({
      key: o.key,
      label: (o.label || o.key).slice(0, 14),
      x: n.x + GATEWAY_R + 14,
      y: n.y + (i - (outcomes.length - 1) / 2) * spread,
    }));
  }

  protected beginOutcomeConnect(gatewayId: string, outcomeKey: string): void {
    if (this.isReadonly()) return;
    const node = this.nodes().find(n => n.id === gatewayId);
    if (!node) return;
    const port = this.decisionOutcomePorts(node).find(p => p.key === outcomeKey);
    const p = port ?? this.portOutPos(node);
    this.contextMenu.set(null);
    this.quickAddSourceId.set(null);
    this.drawingEdge.set({
      fromNodeId: gatewayId,
      x1: p.x, y1: p.y, x2: p.x + 48, y2: p.y,
      outcomeKey,
    });
  }

  protected onCanvasMouseMove(event: MouseEvent): void {
    // Kept for hover affordance when not using document listener path
    if (this.drawingEdge()) this.updateDrawingEdge(event);
  }

  private updateDrawingEdge(event: MouseEvent): void {
    const de = this.drawingEdge();
    if (!de) return;
    const svg = this.canvasSvgRef?.nativeElement;
    if (!svg) return;
    const rect = svg.getBoundingClientRect();
    const x = (event.clientX - rect.left - this.panX()) / this.zoom();
    const y = (event.clientY - rect.top  - this.panY()) / this.zoom();
    this.drawingEdge.set({ ...de, x2: x, y2: y });
    const hit = this.hitTestNodeId(event.clientX, event.clientY);
    this.connectHoverNodeId.set(hit && hit !== de.fromNodeId ? hit : null);
  }

  /** Screen-space hit test against node bounds (includes padding for easier drops). */
  private hitTestNodeId(clientX: number, clientY: number): string | null {
    const svg = this.canvasSvgRef?.nativeElement;
    if (!svg) return null;
    const rect = svg.getBoundingClientRect();
    const x = (clientX - rect.left - this.panX()) / this.zoom();
    const y = (clientY - rect.top  - this.panY()) / this.zoom();
    const pad = 12;
    // Prefer topmost / last-drawn node
    const ns = this.nodes();
    for (let i = ns.length - 1; i >= 0; i--) {
      const n = ns[i];
      if (this.isGateway(n.type)) {
        const dx = Math.abs(x - n.x);
        const dy = Math.abs(y - n.y);
        if (dx + dy <= GATEWAY_R + pad) return n.id;
      } else if (n.type === 'Start' || n.type === 'End') {
        const dx = x - n.x;
        const dy = y - n.y;
        if (dx * dx + dy * dy <= (26 + pad) * (26 + pad)) return n.id;
      } else if (
        x >= n.x - pad && x <= n.x + NODE_W + pad &&
        y >= n.y - pad && y <= n.y + NODE_H + pad
      ) {
        return n.id;
      }
    }
    return null;
  }

  private finishEdge(toNodeId: string): void {
    const de = this.drawingEdge();
    if (!de || de.fromNodeId === toNodeId) {
      this.drawingEdge.set(null);
      this.connectHoverNodeId.set(null);
      return;
    }
    const exists = this.edges().some(e => e.fromNodeId === de.fromNodeId && e.toNodeId === toNodeId);
    if (exists) {
      this.drawingEdge.set(null);
      this.connectHoverNodeId.set(null);
      return;
    }
    this.pushUndo();
    const key = `transition_${Date.now()}`;
    const outcomeKey = de.outcomeKey ?? '';
    const outcome = outcomeKey
      ? this.incomingUserTask(de.fromNodeId)?.outcomes.find(o => o.key === outcomeKey)
      : undefined;
    const fromNode = this.nodes().find(n => n.id === de.fromNodeId);
    const existingFrom = this.edges().filter(e => e.fromNodeId === de.fromNodeId);
    const shouldDefault = !!fromNode && this.isGateway(fromNode.type) && existingFrom.every(e => !e.isDefault)
      && (outcome?.isDefault || existingFrom.length === 0);
    const edge: CanvasEdge = {
      id: crypto.randomUUID(), fromNodeId: de.fromNodeId, toNodeId,
      transitionKey: key,
      labelEn: outcome?.label ?? '',
      labelAr: outcome?.labelAr ?? '',
      descriptionEn: '',
      outcomeKey,
      conditionExpression: outcomeKey ? `OutcomeKey == '${outcomeKey}'` : '',
      isDefault: shouldDefault || (outcome?.isDefault ?? false),
      priority: existingFrom.length,
    };
    this.edges.update(es => [...es, edge]);
    this.drawingEdge.set(null);
    this.connectHoverNodeId.set(null);
    this.markUnsaved();
  }

  // ── Node deletion ─────────────────────────────────────────────────────────

  protected requestDeleteNode(event: MouseEvent, node: CanvasNode): void {
    event.stopPropagation();
    event.preventDefault();
    if (this.isReadonly()) return;
    // Cancel any in-progress connection so the confirm dialog can be used
    this.drawingEdge.set(null);
    this.connectHoverNodeId.set(null);
    this.quickAddSourceId.set(null);
    this.contextMenu.set(null);
    this.selectedNodeId.set(node.id);
    this.selectedNodeIds.set([node.id]);
    this.selectedEdgeId.set(null);
    this.deleteTarget.set(node);
  }

  protected cancelDelete(): void { this.deleteTarget.set(null); }

  protected confirmDeleteNode(): void {
    const node = this.deleteTarget();
    if (!node) return;
    this.pushUndo();
    this.nodes.update(ns => ns.filter(n => n.id !== node.id));
    this.edges.update(es => es.filter(e => e.fromNodeId !== node.id && e.toNodeId !== node.id));
    if (this.selectedNodeId() === node.id) this.selectedNodeId.set(null);
    this.selectedNodeIds.update(ids => ids.filter(id => id !== node.id));
    this.deleteTarget.set(null);
    this.markUnsaved();
  }

  /** Immediate delete (keyboard shortcut) — no confirm dialog. */
  protected deleteSelectedNow(): void {
    if (this.isReadonly()) return;
    this.drawingEdge.set(null);
    this.connectHoverNodeId.set(null);
    this.contextMenu.set(null);

    const multiIds = this.selectedNodeIds();
    if (multiIds.length > 1) {
      this.deleteSelectedMulti();
      return;
    }

    const edgeId = this.selectedEdgeId();
    if (edgeId && !this.selectedNodeId()) {
      this.deleteEdge(edgeId);
      return;
    }

    const sel = this.selectedNodeId() ?? (multiIds.length === 1 ? multiIds[0] : null);
    if (!sel) return;
    const node = this.nodes().find(n => n.id === sel);
    if (!node) return;
    this.pushUndo();
    this.nodes.update(ns => ns.filter(n => n.id !== node.id));
    this.edges.update(es => es.filter(e => e.fromNodeId !== node.id && e.toNodeId !== node.id));
    this.selectedNodeId.set(null);
    this.selectedNodeIds.set([]);
    this.selectedEdgeId.set(null);
    this.deleteTarget.set(null);
    this.markUnsaved();
  }

  // ── Edge deletion ─────────────────────────────────────────────────────────

  protected deleteEdge(edgeId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    this.edges.update(es => es.filter(e => e.id !== edgeId));
    if (this.selectedEdgeId() === edgeId) this.selectedEdgeId.set(null);
    this.markUnsaved();
  }

  // ── Multi-select operations ───────────────────────────────────────────────

  protected selectAll(): void {
    const ids = this.nodes().map(n => n.id);
    this.selectedNodeIds.set(ids);
    if (ids.length > 0) this.selectedNodeId.set(ids[0]);
    this.selectedEdgeId.set(null);
  }

  protected cutSelected(): void {
    this.copySelected();
    if (!this.selectedNodeId() && this.selectedNodeIds().length === 0) return;
    this.deleteSelectedNow();
  }

  protected deleteSelectedMulti(): void {
    if (this.isReadonly()) return;
    const ids = this.selectedNodeIds();
    if (ids.length === 0) return;
    this.pushUndo();
    this.nodes.update(ns => ns.filter(n => !ids.includes(n.id)));
    this.edges.update(es => es.filter(e => !ids.includes(e.fromNodeId) && !ids.includes(e.toNodeId)));
    this.selectedNodeIds.set([]);
    this.selectedNodeId.set(null);
    this.markUnsaved();
  }

  // ── Alignment ─────────────────────────────────────────────────────────────

  protected alignNodes(axis: 'left' | 'right' | 'top' | 'bottom' | 'centerH' | 'centerV'): void {
    if (this.isReadonly()) return;
    const ids = this.selectedNodeIds();
    if (ids.length < 2) return;
    const ns = this.nodes().filter(n => ids.includes(n.id));
    this.pushUndo();
    let ref = 0;
    switch (axis) {
      case 'left':    ref = Math.min(...ns.map(n => n.x)); break;
      case 'right':   ref = Math.max(...ns.map(n => n.x + (this.isGateway(n.type) ? 0 : NODE_W))); break;
      case 'top':     ref = Math.min(...ns.map(n => n.y)); break;
      case 'bottom':  ref = Math.max(...ns.map(n => n.y + (this.isGateway(n.type) ? 0 : NODE_H))); break;
      case 'centerH': ref = ns.reduce((s, n) => s + n.y + (this.isGateway(n.type) ? 0 : NODE_H / 2), 0) / ns.length; break;
      case 'centerV': ref = ns.reduce((s, n) => s + n.x + (this.isGateway(n.type) ? 0 : NODE_W / 2), 0) / ns.length; break;
    }
    this.nodes.update(all => all.map(n => {
      if (!ids.includes(n.id)) return n;
      const gw = this.isGateway(n.type);
      switch (axis) {
        case 'left':    return { ...n, x: ref };
        case 'right':   return { ...n, x: ref - (gw ? 0 : NODE_W) };
        case 'top':     return { ...n, y: ref };
        case 'bottom':  return { ...n, y: ref - (gw ? 0 : NODE_H) };
        case 'centerH': return { ...n, y: ref - (gw ? 0 : NODE_H / 2) };
        case 'centerV': return { ...n, x: ref - (gw ? 0 : NODE_W / 2) };
        default: return n;
      }
    }));
    this.markUnsaved();
  }

  protected distributeNodes(dir: 'horizontal' | 'vertical'): void {
    if (this.isReadonly()) return;
    const ids = this.selectedNodeIds();
    if (ids.length < 3) return;
    const ns = this.nodes().filter(n => ids.includes(n.id));
    this.pushUndo();
    if (dir === 'horizontal') {
      const sorted = [...ns].sort((a, b) => a.x - b.x);
      const minX = sorted[0].x;
      const lastN = sorted[sorted.length - 1];
      const maxX = lastN.x + (this.isGateway(lastN.type) ? 0 : NODE_W);
      const gap = (maxX - minX) / (sorted.length - 1);
      const posMap: Record<string, number> = {};
      sorted.forEach((n, i) => { posMap[n.id] = minX + i * gap; });
      this.nodes.update(all => all.map(n => posMap[n.id] !== undefined ? { ...n, x: posMap[n.id] } : n));
    } else {
      const sorted = [...ns].sort((a, b) => a.y - b.y);
      const minY = sorted[0].y;
      const lastN = sorted[sorted.length - 1];
      const maxY = lastN.y + (this.isGateway(lastN.type) ? 0 : NODE_H);
      const gap = (maxY - minY) / (sorted.length - 1);
      const posMap: Record<string, number> = {};
      sorted.forEach((n, i) => { posMap[n.id] = minY + i * gap; });
      this.nodes.update(all => all.map(n => posMap[n.id] !== undefined ? { ...n, y: posMap[n.id] } : n));
    }
    this.markUnsaved();
  }

  // ── Auto-layout ───────────────────────────────────────────────────────────

  protected autoLayout(): void {
    const ns = this.nodes();
    if (ns.length === 0) return;
    if (!this.isReadonly()) this.pushUndo();
    const posMap = layoutWorkflowGraph(ns, this.edges());
    this.nodes.update(all => all.map(n => posMap[n.id] ? { ...n, ...posMap[n.id] } : n));
    this.resetView();
    const scroller = this.canvasScrollRef?.nativeElement;
    if (scroller) {
      scroller.scrollLeft = 0;
      scroller.scrollTop = 0;
    }
    if (!this.isReadonly()) this.markUnsaved();
  }

  private autoScrollWhileDragging(event: MouseEvent): void {
    const scroller = this.canvasScrollRef?.nativeElement;
    if (!scroller) return;
    const rect = scroller.getBoundingClientRect();
    const edge = 48;
    const step = 28;
    if (event.clientY > rect.bottom - edge) scroller.scrollTop += step;
    else if (event.clientY < rect.top + edge) scroller.scrollTop -= step;
    if (event.clientX > rect.right - edge) scroller.scrollLeft += step;
    else if (event.clientX < rect.left + edge) scroller.scrollLeft -= step;
  }

  // ── Center on node ────────────────────────────────────────────────────────

  protected centerOnNode(nodeId: string): void {
    const node = this.nodes().find(n => n.id === nodeId);
    if (!node) return;
    const svg = this.canvasSvgRef?.nativeElement;
    if (!svg) return;
    const viewW = svg.clientWidth || 800;
    const viewH = svg.clientHeight || 600;
    const z = this.zoom();
    const cx = this.isGateway(node.type) ? node.x : node.x + NODE_W / 2;
    const cy = this.isGateway(node.type) ? node.y : node.y + NODE_H / 2;
    this.panX.set(viewW / 2 - cx * z);
    this.panY.set(viewH / 2 - cy * z);
  }

  // ── Context menu actions ──────────────────────────────────────────────────

  protected executeContextAction(action: string, nodeId?: string, edgeId?: string): void {
    const menu = this.contextMenu();
    this.contextMenu.set(null);
    switch (action) {
      case 'copy':          this.copySelected(); break;
      case 'cut':           this.cutSelected(); break;
      case 'paste':         this.pasteClipboardAt(menu?.canvasX, menu?.canvasY); break;
      case 'duplicate':     this.duplicateSelected(); break;
      case 'delete-multi':  this.deleteSelectedMulti(); break;
      case 'delete':
        if (nodeId) { const nd = this.nodes().find(n => n.id === nodeId); if (nd) this.requestDeleteNode(new MouseEvent('click'), nd); }
        else if (edgeId) this.deleteEdge(edgeId);
        break;
      case 'auto-layout':   this.autoLayout(); break;
      case 'fit':           this.fitScreen(); break;
      case 'select-all':    this.selectAll(); break;
      case 'align-left':    this.alignNodes('left'); break;
      case 'align-right':   this.alignNodes('right'); break;
      case 'align-top':     this.alignNodes('top'); break;
      case 'align-bottom':  this.alignNodes('bottom'); break;
      case 'align-centerH': this.alignNodes('centerH'); break;
      case 'align-centerV': this.alignNodes('centerV'); break;
      case 'distribute-h':  this.distributeNodes('horizontal'); break;
      case 'distribute-v':  this.distributeNodes('vertical'); break;
      case 'connect-from':
        if (nodeId) this.beginConnectFromNode(nodeId);
        break;
      case 'add-next':
        if (nodeId) this.quickAddSourceId.set(nodeId);
        break;
      case 'properties':
        if (nodeId) {
          const node = this.nodes().find(n => n.id === nodeId);
          if (node) {
            this.selectedNodeIds.set([nodeId]);
            this.selectedNodeId.set(nodeId);
            this.selectedEdgeId.set(null);
            this.patchPropsFromNode(node);
          }
        } else if (edgeId) {
          this.selectedEdgeId.set(edgeId);
          this.selectedNodeId.set(null);
          this.selectedNodeIds.set([]);
          const edge = this.edges().find(e => e.id === edgeId);
          if (edge) this.patchEdgeForm(edge);
        }
        break;
      case 'center-node':
        if (nodeId) this.centerOnNode(nodeId);
        break;
      case 'set-default-edge':
        if (edgeId) this.setEdgeDefault(edgeId);
        break;
      case 'add-start':
        if (!this.isReadonly()) this.addNode('Start', menu?.canvasX ?? 120, menu?.canvasY ?? 120);
        break;
      case 'add-user-task':
        if (!this.isReadonly()) this.addNode('UserTask', menu?.canvasX ?? 200, menu?.canvasY ?? 120);
        break;
      case 'add-decision':
        if (!this.isReadonly()) this.addNode('ExclusiveGateway', menu?.canvasX ?? 200, menu?.canvasY ?? 120);
        break;
      case 'add-end':
        if (!this.isReadonly()) this.addNode('End', menu?.canvasX ?? 280, menu?.canvasY ?? 120);
        break;
      case 'undo': this.undo(); break;
      case 'redo': this.redo(); break;
    }
  }

  /** Start a connection drag from a node (used by right-click). */
  protected beginConnectFromNode(nodeId: string): void {
    if (this.isReadonly()) return;
    const node = this.nodes().find(n => n.id === nodeId);
    if (!node || !this.canHaveEdge(node.type)) return;
    this.dragging = null;
    this.selectedNodeIds.set([nodeId]);
    this.selectedNodeId.set(nodeId);
    this.selectedEdgeId.set(null);
    const p = this.portOutPos(node);
    this.connectHoverNodeId.set(null);
    this.drawingEdge.set({ fromNodeId: nodeId, x1: p.x, y1: p.y, x2: p.x + 40, y2: p.y });
  }

  protected setEdgeDefault(edgeId: string): void {
    if (this.isReadonly()) return;
    const edge = this.edges().find(e => e.id === edgeId);
    if (!edge) return;
    this.pushUndo();
    this.edges.update(es => es.map(e =>
      e.fromNodeId === edge.fromNodeId
        ? { ...e, isDefault: e.id === edgeId }
        : e
    ));
    this.markUnsaved();
  }

  protected pasteClipboardAt(canvasX?: number, canvasY?: number): void {
    const cb = this.clipboard();
    if (this.isReadonly() || !cb) return;
    this.pushUndo();
    const x = canvasX != null ? canvasX : cb.x + 60;
    const y = canvasY != null ? canvasY : cb.y + 60;
    const newNode: CanvasNode = {
      ...structuredClone(cb),
      id: crypto.randomUUID(),
      nodeKey: `${cb.type.toLowerCase()}_${Date.now()}`,
      x, y,
    };
    this.nodes.update(ns => [...ns, newNode]);
    this.selectedNodeId.set(newNode.id);
    this.selectedNodeIds.set([newNode.id]);
    this.patchPropsFromNode(newNode);
    this.markUnsaved();
  }

  // ── Keyboard shortcuts ────────────────────────────────────────────────────

  @HostListener('document:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName ?? '';
    const inInput = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || !!target?.isContentEditable;
    const ctrl = event.ctrlKey || event.metaKey;

    if (ctrl && event.key === '0') { event.preventDefault(); this.fitScreen(); return; }

    // Escape: cancel draw / menus first
    if (event.key === 'Escape') {
      if (this.drawingEdge() || this.contextMenu() || this.quickAddSourceId() || this.deleteTarget()) {
        event.preventDefault();
        this.drawingEdge.set(null);
        this.connectHoverNodeId.set(null);
        this.contextMenu.set(null);
        this.quickAddSourceId.set(null);
        this.deleteTarget.set(null);
        return;
      }
      if (this.isFullscreen()) {
        this.setFullscreen(false);
        return;
      }
      this.selectedNodeId.set(null); this.selectedEdgeId.set(null);
      this.selectedNodeIds.set([]);
      return;
    }

    // Delete key: remove selected control (works while connecting; still allow typing in inputs)
    if (!this.isReadonly() && event.key === 'Delete') {
      if (inInput) return; // keep text editing
      event.preventDefault();
      if (this.drawingEdge()) {
        this.drawingEdge.set(null);
        this.connectHoverNodeId.set(null);
      }
      this.deleteSelectedNow();
      return;
    }

    // Backspace: same as Delete only when not typing in a field
    if (!this.isReadonly() && event.key === 'Backspace' && !inInput) {
      event.preventDefault();
      if (this.drawingEdge()) {
        this.drawingEdge.set(null);
        this.connectHoverNodeId.set(null);
      }
      this.deleteSelectedNow();
      return;
    }

    if (inInput) return;
    if (ctrl && event.key === 'z') { event.preventDefault(); if (!this.isReadonly()) this.undo(); return; }
    if (ctrl && event.key === 'y') { event.preventDefault(); if (!this.isReadonly()) this.redo(); return; }
    if (ctrl && event.shiftKey && event.key === 'Z') { event.preventDefault(); if (!this.isReadonly()) this.redo(); return; }
    if (ctrl && event.key === 's') { event.preventDefault(); this.save(); return; }
    if (ctrl && event.key === 'c') { event.preventDefault(); this.copySelected(); return; }
    if (ctrl && event.key === 'v') { event.preventDefault(); this.pasteClipboard(); return; }
    if (ctrl && event.key === 'd') { event.preventDefault(); this.duplicateSelected(); return; }
    if (ctrl && event.key === 'a') { event.preventDefault(); this.selectAll(); return; }
    if (ctrl && event.key === 'x') { event.preventDefault(); if (!this.isReadonly()) this.cutSelected(); return; }
    if (event.key === 'F11') {
      event.preventDefault();
      this.toggleFullscreen();
      return;
    }
  }

  // ── Undo/redo ─────────────────────────────────────────────────────────────

  private pushUndo(): void {
    this.undoStack.push({
      nodes: structuredClone(this.nodes()),
      edges: structuredClone(this.edges()),
      variables: structuredClone(this.variables()),
    });
    this.redoStack = [];
  }

  protected undo(): void {
    const state = this.undoStack.pop();
    if (!state) return;
    this.redoStack.push({
      nodes: structuredClone(this.nodes()),
      edges: structuredClone(this.edges()),
      variables: structuredClone(this.variables()),
    });
    this.nodes.set(state.nodes);
    this.edges.set(state.edges);
    this.variables.set(state.variables ?? []);
    this.markUnsaved();
  }

  protected redo(): void {
    const state = this.redoStack.pop();
    if (!state) return;
    this.undoStack.push({
      nodes: structuredClone(this.nodes()),
      edges: structuredClone(this.edges()),
      variables: structuredClone(this.variables()),
    });
    this.nodes.set(state.nodes);
    this.edges.set(state.edges);
    this.variables.set(state.variables ?? []);
    this.markUnsaved();
  }

  // ── Node props form ───────────────────────────────────────────────────────

  protected applyProps(): void {
    const nodeId = this.selectedNodeId();
    if (!nodeId) return;
    const node = this.nodes().find(n => n.id === nodeId);
    if (!node) return;
    const v = this.propsForm.value;
    const configurationJson = this.buildConfigurationJson(node.type, v);
    this.pushUndo();
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId
        ? { ...n,
            name:          v.name ?? n.name,
            nameAr:        v.nameAr ?? '',
            actionKey:     v.actionKey ?? '',
            assignmentKey: v.assignmentKey ?? '',
            assignmentGroupId: v.assignmentGroupId ?? '',
            assignmentPurpose: v.assignmentPurpose ?? '',
            configurationJson }
        : n
    ));
    this.markUnsaved();
  }

  protected applyIntegrationConfiguration(configurationJson: string): void {
    if (this.isReadonly()) return;
    const nodeId = this.selectedNodeId();
    this.pushUndo();
    this.nodes.update(nodes => nodes.map(n => n.id === nodeId
      ? { ...n, configurationJson, actionKey: n.type === 'ServiceTask' ? 'http.request' : n.actionKey } : n));
    const node = this.nodes().find(n => n.id === nodeId);
    if (node) this.patchPropsFromNode(node);
    this.markUnsaved();
  }

  protected applyTypedVariables(values: string): void {
    this.applyIntegrationConfiguration(JSON.stringify({ ...this.parseConfig(this.selectedNode()?.configurationJson || '{}'),
      assignmentFormat: 'typed', setVariables: JSON.parse(values) }));
  }

  // ── Edge props form ───────────────────────────────────────────────────────

  protected applyEdgeProps(): void {
    const edgeId = this.selectedEdgeId();
    if (!edgeId) return;
    const v = this.edgeForm.value;
    this.pushUndo();
    this.edges.update(es => es.map(e =>
      e.id === edgeId
        ? { ...e,
            transitionKey:       v.transitionKey       ?? e.transitionKey,
            labelEn:             v.labelEn             ?? e.labelEn,
            labelAr:             v.labelAr             ?? e.labelAr,
            descriptionEn:       v.descriptionEn       ?? e.descriptionEn,
            outcomeKey:          v.outcomeKey          ?? e.outcomeKey,
            conditionExpression: v.conditionExpression ?? e.conditionExpression,
            isDefault:           v.isDefault           ?? e.isDefault,
            priority:            v.priority            ?? e.priority }
        : e
    ));
    this.markUnsaved();
  }

  protected incomingUserTask(nodeId: string): CanvasNode | null {
    const incoming = this.edges().find(e => e.toNodeId === nodeId);
    if (!incoming) return null;
    const src = this.nodes().find(n => n.id === incoming.fromNodeId) ?? null;
    return (src?.type === 'UserTask' || src?.type === 'MainActivity') ? src : null;
  }

  protected outgoingEdgesOf(nodeId: string): CanvasEdge[] {
    return this.edges().filter(e => e.fromNodeId === nodeId);
  }

  protected decisionSourceOutcomes(): CanvasOutcome[] {
    const edge = this.selectedEdge();
    if (edge) {
      const from = this.nodes().find(n => n.id === edge.fromNodeId);
      if ((from?.type === 'UserTask' || from?.type === 'MainActivity')) return from.outcomes.filter(o => !!o.key);
      if (from && this.isGateway(from.type)) {
        return this.incomingUserTask(from.id)?.outcomes.filter(o => !!o.key) ?? [];
      }
      return [];
    }
    const node = this.selectedNode();
    if (node?.type === 'ExclusiveGateway') {
      return this.incomingUserTask(node.id)?.outcomes.filter(o => !!o.key) ?? [];
    }
    return [];
  }

  protected edgeForOutcome(gatewayId: string, outcomeKey: string): CanvasEdge | undefined {
    const key = outcomeKey.toUpperCase();
    return this.outgoingEdgesOf(gatewayId).find(e =>
      e.outcomeKey.toUpperCase() === key
      || e.conditionExpression.toUpperCase().includes(`'${key}'`),
    );
  }

  protected pickEdgeOutcome(key: string): void {
    const outcome = this.decisionSourceOutcomes().find(o => o.key === key);
    this.edgeForm.patchValue({
      outcomeKey: key,
      labelEn: outcome?.label || key,
      labelAr: outcome?.labelAr || '',
      conditionExpression: key ? `OutcomeKey == '${key}'` : '',
      isDefault: outcome?.isDefault ?? false,
    });
  }

  protected selectOutgoingEdge(edgeId: string): void {
    this.selectedNodeId.set(null);
    this.selectedNodeIds.set([]);
    this.selectedEdgeId.set(edgeId);
    const edge = this.edges().find(e => e.id === edgeId);
    if (edge) this.patchEdgeForm(edge);
  }

  // ── Outcomes CRUD ─────────────────────────────────────────────────────────

  protected addOutcome(nodeId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    const node = this.nodes().find(n => n.id === nodeId);
    const outcome: CanvasOutcome = {
      id: crypto.randomUUID(), key: '', label: '', labelAr: '',
      description: '', descriptionAr: '',
      sortOrder: node?.outcomes.length ?? 0,
      isDefault: false, requiresComment: false,
      requiresAttachment: false, isActive: true, resultValue: '',
    };
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId ? { ...n, outcomes: [...n.outcomes, outcome] } : n
    ));
    this.markUnsaved();
  }

  protected addStandardOutcomes(nodeId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    const presets: Array<Pick<CanvasOutcome, 'key' | 'label' | 'labelAr' | 'resultValue' | 'isDefault'>> = [
      { key: 'APPROVE', label: 'Approve', labelAr: 'اعتماد', resultValue: 'APPROVED', isDefault: true },
      { key: 'REJECT', label: 'Reject', labelAr: 'رفض', resultValue: 'REJECTED', isDefault: false },
      { key: REDIRECT_OUTCOME_KEY, label: 'Redirect', labelAr: 'إعادة توجيه', resultValue: REDIRECT_OUTCOME_KEY, isDefault: false },
    ];
    this.nodes.update(ns => ns.map(n => {
      if (n.id !== nodeId) return n;
      const existing = new Set(n.outcomes.map(o => o.key.toUpperCase()));
      const extras: CanvasOutcome[] = [];
      presets.forEach((p, i) => {
        if (existing.has(p.key)) return;
        extras.push({
          id: crypto.randomUUID(), key: p.key, label: p.label, labelAr: p.labelAr,
          description: '', descriptionAr: '',
          sortOrder: n.outcomes.length + extras.length + i,
          isDefault: p.isDefault && !n.outcomes.some(o => o.isDefault),
          requiresComment: p.key !== 'APPROVE',
          requiresAttachment: false, isActive: true, resultValue: p.resultValue,
        });
      });
      return extras.length ? { ...n, outcomes: [...n.outcomes, ...extras] } : n;
    }));
    this.markUnsaved();
  }

  protected addRedirectOutcome(nodeId: string): void {
    if (this.isReadonly()) return;
    const node = this.nodes().find(n => n.id === nodeId);
    if (node?.outcomes.some(o => isRedirectOutcome(o.key, o.resultValue))) return;
    this.pushUndo();
    const outcome: CanvasOutcome = {
      id: crypto.randomUUID(),
      key: REDIRECT_OUTCOME_KEY,
      label: 'Redirect',
      labelAr: 'إعادة توجيه',
      description: 'Send this task to another department or group',
      descriptionAr: '',
      sortOrder: node?.outcomes.length ?? 0,
      isDefault: false, requiresComment: true,
      requiresAttachment: false, isActive: true, resultValue: REDIRECT_OUTCOME_KEY,
    };
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId ? { ...n, outcomes: [...n.outcomes, outcome] } : n
    ));
    this.markUnsaved();
  }

  protected removeOutcome(nodeId: string, outcomeId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId ? { ...n, outcomes: n.outcomes.filter(o => o.id !== outcomeId) } : n
    ));
    this.markUnsaved();
  }

  protected updateOutcomeField(nodeId: string, outcomeId: string, field: keyof CanvasOutcome, value: unknown): void {
    if (this.isReadonly()) return;
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId
        ? { ...n, outcomes: n.outcomes.map(o => o.id === outcomeId ? { ...o, [field]: value } : o) }
        : n
    ));
    this.markUnsaved();
  }

  protected moveOutcome(nodeId: string, outcomeId: string, direction: 'up' | 'down'): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    this.nodes.update(ns => ns.map(n => {
      if (n.id !== nodeId) return n;
      const arr = [...n.outcomes];
      const idx = arr.findIndex(o => o.id === outcomeId);
      const swap = direction === 'up' ? idx - 1 : idx + 1;
      if (idx < 0 || swap < 0 || swap >= arr.length) return n;
      [arr[idx], arr[swap]] = [arr[swap], arr[idx]];
      return { ...n, outcomes: arr };
    }));
    this.markUnsaved();
  }

  // ── Actions CRUD ──────────────────────────────────────────────────────────

  protected addAction(nodeId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    const node = this.nodes().find(n => n.id === nodeId);
    const action: CanvasAction = {
      id: crypto.randomUUID(), actionKey: '', executionTrigger: 'OnComplete',
      outcomeKey: '', conditionExpression: '',
      sequence: node?.actions.length ?? 0,
      inputMappingJson: '', outputMappingJson: '',
      failurePolicy: 'Continue', retryCount: 0, retryDelaySeconds: 0, timeoutSeconds: 0,
      isActive: true,
    };
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId ? { ...n, actions: [...n.actions, action] } : n
    ));
    this.markUnsaved();
  }

  protected removeAction(nodeId: string, actionId: string): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId ? { ...n, actions: n.actions.filter(a => a.id !== actionId) } : n
    ));
    this.markUnsaved();
  }

  protected updateActionField(nodeId: string, actionId: string, field: keyof CanvasAction, value: unknown): void {
    if (this.isReadonly()) return;
    this.nodes.update(ns => ns.map(n =>
      n.id === nodeId
        ? { ...n, actions: n.actions.map(a => a.id === actionId ? { ...a, [field]: value } : a) }
        : n
    ));
    this.markUnsaved();
  }

  protected moveAction(nodeId: string, actionId: string, direction: 'up' | 'down'): void {
    if (this.isReadonly()) return;
    this.pushUndo();
    this.nodes.update(ns => ns.map(n => {
      if (n.id !== nodeId) return n;
      const arr = [...n.actions];
      const idx = arr.findIndex(a => a.id === actionId);
      const swap = direction === 'up' ? idx - 1 : idx + 1;
      if (idx < 0 || swap < 0 || swap >= arr.length) return n;
      [arr[idx], arr[swap]] = [arr[swap], arr[idx]];
      return { ...n, actions: arr };
    }));
    this.markUnsaved();
  }

  // ── Variables CRUD ────────────────────────────────────────────────────────

  protected addVariable(): void {
    if (this.isReadonly()) return;
    const v: CanvasVariable = {
      id: crypto.randomUUID(), variableKey: '', name: '', nameAr: '',
      dataType: 'String', defaultValue: '',
      isRequired: false, isSensitive: false, description: '', descriptionAr: '',
    };
    this.variables.update(vs => [...vs, v]);
    this.markUnsaved();
  }

  protected removeVariable(variableId: string): void {
    if (this.isReadonly()) return;
    this.variables.update(vs => vs.filter(v => v.id !== variableId));
    this.markUnsaved();
  }

  protected updateVariableField(variableId: string, field: keyof CanvasVariable, value: unknown): void {
    if (this.isReadonly()) return;
    this.variables.update(vs =>
      vs.map(v => v.id === variableId ? { ...v, [field]: value } : v)
    );
    this.markUnsaved();
  }

  // ── Validation click-to-navigate ──────────────────────────────────────────

  protected navigateToError(nodeKey: string | undefined | null, errorCode?: string | null): void {
    if (!nodeKey) return;
    const node = this.nodes().find(n => n.nodeKey === nodeKey);
    if (!node) return;
    this.selectedNodeId.set(node.id);
    this.selectedNodeIds.set([node.id]);
    this.selectedEdgeId.set(null);
    this.patchPropsFromNode(node);
    this.centerOnNode(node.id);

    const tab = this.inferTabFromCode(errorCode);
    if ((node.type === 'UserTask' || node.type === 'MainActivity') || !['assignment', 'outcomes', 'actions', 'sla', 'data', 'form', 'advanced'].includes(tab)) {
      this.inspectorTab.set(tab as InspectorTab);
    } else {
      this.inspectorTab.set('general');
    }
  }

  private inferTabFromCode(code?: string | null): string {
    if (!code) return 'general';
    const lower = code.toLowerCase();
    if (lower.includes('assignment') || lower.includes('assignmentkey')) return 'assignment';
    if (lower.includes('outcome') || lower.includes('reject') || lower.includes('accept')) return 'actions';
    if (lower.includes('action')) return 'actions';
    if (lower.includes('sla') || lower.includes('timer')) return 'sla';
    return 'general';
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  private markUnsaved(): void {
    this.saveStatus.set('unsaved');
    this.autosave$.next();
  }

  protected isGateway(type: NodeType): boolean {
    return type === 'ExclusiveGateway'
      || type === 'InclusiveGateway'
      || type === 'ParallelGateway'
      || type === 'JoinGateway';
  }

  protected getNodeName(nodeId: string | undefined | null): string {
    if (!nodeId) return '—';
    return this.nodes().find(n => n.id === nodeId)?.name || nodeId;
  }

  private defaultNodeName(type: NodeType): string {
    switch (type) {
      case 'Start':             return 'Start';
      case 'End':               return 'End';
      case 'ExclusiveGateway':  return 'Decision';
      case 'InclusiveGateway':  return 'Inclusive Gateway';
      case 'ParallelGateway':   return 'Parallel';
      case 'JoinGateway':       return 'Join';
      case 'Timer':             return 'Timer';
      case 'WaitEvent':         return 'Wait for Event';
      case 'ServiceTask':       return 'Service Task';
      case 'CallActivity':      return 'Call Activity';
      case 'ScriptTask':        return 'Set Variables';
      case 'NotificationTask':  return 'Notification';
      default:                  return 'New Task';
    }
  }

  private patchPropsFromNode(node: CanvasNode): void {
    const cfg = this.parseConfig(node.configurationJson);
    this.propsForm.patchValue({
      name:                  node.name,
      nameAr:                node.nameAr,
      description:           typeof cfg['description'] === 'string' ? cfg['description'] as string : '',
      descriptionAr:         typeof cfg['descriptionAr'] === 'string' ? cfg['descriptionAr'] as string : '',
      instructionsEn:        typeof cfg['instructionsEn'] === 'string' ? cfg['instructionsEn'] as string : '',
      instructionsAr:        typeof cfg['instructionsAr'] === 'string' ? cfg['instructionsAr'] as string : '',
      nodePriority:          typeof cfg['nodePriority'] === 'number' ? cfg['nodePriority'] as number : 0,
      actionKey:             node.actionKey,
      assignmentKey:         node.assignmentKey,
      assignmentGroupId:     node.assignmentGroupId ?? '',
      assignmentPurpose:     node.assignmentPurpose ?? '',
      fallbackAssignmentKey: typeof cfg['fallbackAssignmentKey'] === 'string' ? cfg['fallbackAssignmentKey'] as string : '',
      requiresClaim:         typeof cfg['requiresClaim'] === 'boolean' ? cfg['requiresClaim'] as boolean : false,
      allowSelfClaim:        typeof cfg['allowSelfClaim'] === 'boolean' ? cfg['allowSelfClaim'] as boolean : false,
      configJson:            node.configurationJson || '',
      timerType:             (cfg['timerType'] as TimerTypeOption) || 'Duration',
      dueAt:                 typeof cfg['dueAt'] === 'string' ? toLocalDateTime(cfg['dueAt']) : '',
      duration:              typeof cfg['duration'] === 'string' ? cfg['duration'] as string : '',
      signalKey:             typeof cfg['signalKey'] === 'string' ? cfg['signalKey'] as string : '',
      templateKey:           typeof cfg['templateKey'] === 'string' ? cfg['templateKey'] as string : '',
      failurePolicy:         (cfg['failurePolicy'] as NotificationFailurePolicyOption) || 'Continue',
      slaPolicyId:           typeof cfg['slaPolicyId'] === 'string' ? cfg['slaPolicyId'] as string : '',
      slaDurationHours:      typeof cfg['slaDurationHours'] === 'number' ? cfg['slaDurationHours'] as number : 0,
      slaEscalationKey:      typeof cfg['slaEscalationKey'] === 'string' ? cfg['slaEscalationKey'] as string : '',
      inputMappingJson:      typeof cfg['inputMappingJson'] === 'string' ? cfg['inputMappingJson'] as string : '',
      outputMappingJson:     typeof cfg['outputMappingJson'] === 'string' ? cfg['outputMappingJson'] as string : '',
      formKey:               typeof cfg['formKey'] === 'string' ? cfg['formKey'] as string : '',
      formSchemaJson:        typeof cfg['formSchemaJson'] === 'string'
        ? cfg['formSchemaJson'] as string
        : (cfg['formSchemaJson'] && typeof cfg['formSchemaJson'] === 'object'
          ? JSON.stringify(cfg['formSchemaJson'], null, 2)
          : ''),
      setVariablesJson:      this.formatSetVariables(cfg['setVariables'] ?? cfg['assignments']),
      eventKey:              typeof cfg['eventKey'] === 'string' ? cfg['eventKey'] : typeof cfg['signalKey'] === 'string' ? cfg['signalKey'] : '',
      correlationVariable:   typeof cfg['correlationVariable'] === 'string' ? cfg['correlationVariable'] as string : '',
      definitionKey:         typeof cfg['definitionKey'] === 'string' ? cfg['definitionKey'] as string : '',
      waitForCompletion:     typeof cfg['waitForCompletion'] === 'boolean' ? cfg['waitForCompletion'] as boolean : true,
      callVersionId:         typeof cfg['versionId'] === 'string' ? cfg['versionId'] as string : '',
      callInputMappingsJson: JSON.stringify(cfg['inputMappings'] || {}, null, 2),
      callOutputMappingsJson: JSON.stringify(cfg['outputMappings'] || {}, null, 2),
    });
    this.formFields.set(this.parseFormFields(cfg['formFields']));
  }

  private parseFormFields(value: unknown): FormFieldRow[] {
    if (!Array.isArray(value)) return [];
    return value.map((raw, index) => {
      const row = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
      const type = typeof row['type'] === 'string' && this.formFieldTypes.includes(row['type'] as FormFieldType)
        ? row['type'] as FormFieldType
        : 'text';
      return {
        id: typeof row['id'] === 'string' ? row['id'] : crypto.randomUUID(),
        key: typeof row['key'] === 'string' ? row['key'] : `field_${index + 1}`,
        labelEn: typeof row['labelEn'] === 'string' ? row['labelEn'] : '',
        labelAr: typeof row['labelAr'] === 'string' ? row['labelAr'] : '',
        type,
        required: !!row['required'],
        options: Array.isArray(row['options']) ? (row['options'] as string[]).join(', ') : '',
      };
    });
  }

  protected addFormField(): void {
    if (this.isReadonly()) return;
    this.formFields.update(fields => [
      ...fields,
      {
        id: crypto.randomUUID(),
        key: `field_${fields.length + 1}`,
        labelEn: '',
        labelAr: '',
        type: 'text',
        required: false,
      },
    ]);
  }

  protected removeFormField(id: string): void {
    if (this.isReadonly()) return;
    this.formFields.update(fields => fields.filter(f => f.id !== id));
  }

  protected updateFormField(
    id: string,
    field: keyof Omit<FormFieldRow, 'id'>,
    value: string | boolean
  ): void {
    if (this.isReadonly()) return;
    this.formFields.update(fields =>
      fields.map(f => (f.id === id ? { ...f, [field]: value } : f))
    );
  }

  private formatSetVariables(value: unknown): string {
    if (value == null) return '';
    if (typeof value === 'string') return value;
    try { return JSON.stringify(value, null, 2); } catch { return ''; }
  }

  private patchEdgeForm(edge: CanvasEdge): void {
    this.edgeForm.patchValue({
      transitionKey:       edge.transitionKey,
      labelEn:             edge.labelEn,
      labelAr:             edge.labelAr,
      descriptionEn:       edge.descriptionEn,
      outcomeKey:          edge.outcomeKey,
      conditionExpression: edge.conditionExpression,
      isDefault:           edge.isDefault,
      priority:            edge.priority,
    });
  }

  private parseConfig(json: string): Record<string, unknown> {
    if (!json?.trim()) return {};
    try {
      const parsed = JSON.parse(json) as unknown;
      return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
        ? parsed as Record<string, unknown>
        : {};
    } catch { return {}; }
  }

  private buildConfigurationJson(type: NodeType, v: typeof this.propsForm.value): string {
    const existing = this.parseConfig(
      this.nodes().find(n => n.id === this.selectedNodeId())?.configurationJson ?? ''
    );
    if (type === 'Timer') {
      const cfg: Record<string, unknown> = { ...existing, timerType: (v.timerType as string) || 'Duration' };
      delete cfg['dueAt'];
      delete cfg['duration'];
      delete cfg['signalKey'];
      if (v.timerType === 'DueDate' && v.dueAt)     cfg['dueAt'] = toUtcDateTime(v.dueAt);
      if (v.timerType === 'Duration' && v.duration)  cfg['duration'] = v.duration;
      if (v.timerType === 'ExternalSignal' && v.signalKey) cfg['signalKey'] = v.signalKey;
      return JSON.stringify(cfg);
    }
    if (type === 'NotificationTask') {
      return JSON.stringify({ ...existing, templateKey: v.templateKey ?? '', failurePolicy: v.failurePolicy ?? 'Continue' });
    }
    if (type === 'UserTask' || type === 'MainActivity') {
      const existing = this.parseConfig(
        this.nodes().find(n => n.id === this.selectedNodeId())?.configurationJson ?? ''
      );
      const cfg: Record<string, unknown> = { ...existing };
      const setOrDelete = (key: string, value: unknown, present: boolean): void => {
        if (present) cfg[key] = value;
        else delete cfg[key];
      };
      setOrDelete('description', v.description, !!v.description?.trim());
      setOrDelete('descriptionAr', v.descriptionAr, !!v.descriptionAr?.trim());
      setOrDelete('instructionsEn', v.instructionsEn, !!v.instructionsEn?.trim());
      setOrDelete('instructionsAr', v.instructionsAr, !!v.instructionsAr?.trim());
      setOrDelete('nodePriority', v.nodePriority, (v.nodePriority ?? 0) > 0);
      setOrDelete('fallbackAssignmentKey', v.fallbackAssignmentKey, !!v.fallbackAssignmentKey?.trim());
      setOrDelete('requiresClaim', true, !!v.requiresClaim);
      setOrDelete('allowSelfClaim', true, !!v.allowSelfClaim);
      if (!this.workspace()) {
      setOrDelete('slaPolicyId', v.slaPolicyId, !!v.slaPolicyId?.trim());
      setOrDelete('slaDurationHours', v.slaDurationHours, (v.slaDurationHours ?? 0) > 0);
      setOrDelete('slaEscalationKey', v.slaEscalationKey, !!v.slaEscalationKey?.trim());
      setOrDelete('inputMappingJson', v.inputMappingJson, !!v.inputMappingJson?.trim());
      setOrDelete('outputMappingJson', v.outputMappingJson, !!v.outputMappingJson?.trim());
      setOrDelete('formKey', v.formKey, !!v.formKey?.trim());
      }
      if (!this.workspace()) {
      const fields = this.formFields()
        .map(({ key, labelEn, labelAr, type: fieldType, required, options }) => ({
          key: key.trim(),
          labelEn: labelEn.trim(),
          labelAr: labelAr.trim(),
          type: fieldType,
          required: !!required,
          ...(fieldType === 'select' ? { options: (options || '').split(',').map(s => s.trim()).filter(Boolean) } : {}),
        }))
        .filter(f => f.key.length > 0);
      if (fields.length > 0) cfg['formFields'] = fields;
      else delete cfg['formFields'];
      if (v.formSchemaJson?.trim()) {
        try { cfg['formSchemaJson'] = JSON.parse(v.formSchemaJson) as unknown; }
        catch { cfg['formSchemaJson'] = v.formSchemaJson; }
      } else {
        delete cfg['formSchemaJson'];
      }
      }
      return Object.keys(cfg).length ? JSON.stringify(cfg) : '';
    }
    if (type === 'CallActivity') {
      const parseMapping = (text: string | null | undefined) => { try { return JSON.parse(text || '{}') as unknown; } catch { return text; } };
      return JSON.stringify({
        ...existing,
        definitionKey: v.definitionKey ?? '',
        waitForCompletion: v.waitForCompletion ?? true,
        versionId: v.callVersionId?.trim() || null,
        inputMappings: parseMapping(v.callInputMappingsJson),
        outputMappings: parseMapping(v.callOutputMappingsJson),
      });
    }
    if (type === 'ScriptTask') {
      const raw = (v.setVariablesJson ?? '').trim();
      // Legacy assignments are replaced when the user edits the canonical field.
      delete existing['assignments'];
      if (!raw) return JSON.stringify({ ...existing, setVariables: {} });
      try {
        return JSON.stringify({ ...existing, setVariables: JSON.parse(raw) as unknown });
      } catch {
        return JSON.stringify({ ...existing, setVariables: raw });
      }
    }
    if (type === 'WaitEvent') {
      delete existing['signalKey'];
      return JSON.stringify({
        ...existing,
        eventKey: v.eventKey ?? '',
        correlationVariable: v.correlationVariable ?? '',
      });
    }
    if (type === 'ServiceTask') {
      const raw = (v.configJson ?? '').trim();
      if (!raw) return '';
      try { return JSON.stringify(JSON.parse(raw) as unknown); } catch { return raw; }
    }
    return (v.configJson ?? '').trim();
  }

  // ── SVG helpers ───────────────────────────────────────────────────────────

  protected nodeCenterX(n: CanvasNode): number {
    return this.isGateway(n.type) ? n.x : n.x + NODE_W / 2;
  }

  protected nodeCenterY(n: CanvasNode): number {
    return this.isGateway(n.type) ? n.y : n.y + NODE_H / 2;
  }

  private nodeCenter(n: CanvasNode): { x: number; y: number } {
    if (n.type === 'Start' || n.type === 'End') return { x: n.x, y: n.y };
    if (this.isGateway(n.type)) return { x: n.x, y: n.y };
    return { x: n.x + NODE_W / 2, y: n.y + NODE_H / 2 };
  }

  private nodeBounds(n: CanvasNode): { x: number; y: number; w: number; h: number } {
    if (n.type === 'Start' || n.type === 'End') {
      const r = 26;
      return { x: n.x - r, y: n.y - r, w: r * 2, h: r * 2 };
    }
    if (this.isGateway(n.type)) {
      const r = GATEWAY_R;
      return { x: n.x - r, y: n.y - r, w: r * 2, h: r * 2 };
    }
    return { x: n.x, y: n.y, w: NODE_W, h: NODE_H };
  }

  private anchorOnSide(n: CanvasNode, side: 'left' | 'right' | 'top' | 'bottom'): { x: number; y: number } {
    const b = this.nodeBounds(n);
    const cx = b.x + b.w / 2;
    const cy = b.y + b.h / 2;
    switch (side) {
      case 'left':   return { x: b.x, y: cy };
      case 'right':  return { x: b.x + b.w, y: cy };
      case 'top':    return { x: cx, y: b.y };
      case 'bottom': return { x: cx, y: b.y + b.h };
    }
  }

  /**
   * Choose exit/entry sides from relative node placement so arrows stay flexible after drag.
   */
  private pickEdgeSides(
    from: CanvasNode,
    to: CanvasNode,
  ): { fromSide: 'left' | 'right' | 'top' | 'bottom'; toSide: 'left' | 'right' | 'top' | 'bottom' } {
    const a = this.nodeCenter(from);
    const b = this.nodeCenter(to);
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const absX = Math.abs(dx);
    const absY = Math.abs(dy);

    // Mostly horizontal → left/right
    if (absX >= absY * 0.85) {
      if (dx >= 0) return { fromSide: 'right', toSide: 'left' };
      return { fromSide: 'left', toSide: 'right' };
    }

    // Mostly vertical → top/bottom
    if (dy >= 0) return { fromSide: 'bottom', toSide: 'top' };
    return { fromSide: 'top', toSide: 'bottom' };
  }

  private sideNormal(side: 'left' | 'right' | 'top' | 'bottom'): { x: number; y: number } {
    switch (side) {
      case 'left':   return { x: -1, y: 0 };
      case 'right':  return { x: 1, y: 0 };
      case 'top':    return { x: 0, y: -1 };
      case 'bottom': return { x: 0, y: 1 };
    }
  }

  /** Fan parallel edges so overlapping routes separate slightly. */
  private edgeLaneOffset(e: CanvasEdge): number {
    const siblings = this.edges().filter(
      x => x.fromNodeId === e.fromNodeId && x.toNodeId === e.toNodeId,
    );
    if (siblings.length <= 1) return 0;
    const idx = siblings.findIndex(x => x.id === e.id);
    return (idx - (siblings.length - 1) / 2) * 14;
  }

  /**
   * Flexible orthogonal route with rounded elbows (adapts after drag/drop).
   */
  private buildOrthogonalPath(
    x1: number, y1: number, fromSide: 'left' | 'right' | 'top' | 'bottom',
    x2: number, y2: number, toSide: 'left' | 'right' | 'top' | 'bottom',
    lane = 0,
  ): string {
    const stub = 28;
    const n1 = this.sideNormal(fromSide);
    const n2 = this.sideNormal(toSide);
    const sx = x1 + n1.x * stub;
    const sy = y1 + n1.y * stub;
    const ex = x2 + n2.x * stub;
    const ey = y2 + n2.y * stub;

    // Mid routing: prefer H-V-H or V-H-V based on start direction
    const points: { x: number; y: number }[] = [{ x: x1, y: y1 }, { x: sx, y: sy }];

    if (fromSide === 'left' || fromSide === 'right') {
      // Horizontal first
      let midX = (sx + ex) / 2 + lane;
      // If stubs overlap in x (stacked nodes), push mid past both
      if ((fromSide === 'right' && midX < sx) || (fromSide === 'left' && midX > sx)) {
        midX = sx + n1.x * Math.max(40, Math.abs(ex - sx) * 0.5);
      }
      if (Math.abs(sy - ey) < 1) {
        points.push({ x: ex, y: ey });
      } else {
        points.push({ x: midX, y: sy });
        points.push({ x: midX, y: ey });
        points.push({ x: ex, y: ey });
      }
    } else {
      // Vertical first
      let midY = (sy + ey) / 2 + lane;
      if ((fromSide === 'bottom' && midY < sy) || (fromSide === 'top' && midY > sy)) {
        midY = sy + n1.y * Math.max(40, Math.abs(ey - sy) * 0.5);
      }
      if (Math.abs(sx - ex) < 1) {
        points.push({ x: ex, y: ey });
      } else {
        points.push({ x: sx, y: midY });
        points.push({ x: ex, y: midY });
        points.push({ x: ex, y: ey });
      }
    }

    points.push({ x: x2, y: y2 });
    return this.roundedPolyline(points, 10);
  }

  /** Convert polyline points to SVG path with rounded corners. */
  private roundedPolyline(pts: { x: number; y: number }[], radius: number): string {
    if (pts.length < 2) return '';
    if (pts.length === 2) {
      return `M ${pts[0].x} ${pts[0].y} L ${pts[1].x} ${pts[1].y}`;
    }

    let d = `M ${pts[0].x} ${pts[0].y}`;
    for (let i = 1; i < pts.length - 1; i++) {
      const prev = pts[i - 1];
      const curr = pts[i];
      const next = pts[i + 1];
      const v1x = curr.x - prev.x;
      const v1y = curr.y - prev.y;
      const v2x = next.x - curr.x;
      const v2y = next.y - curr.y;
      const len1 = Math.hypot(v1x, v1y) || 1;
      const len2 = Math.hypot(v2x, v2y) || 1;
      const r = Math.min(radius, len1 / 2, len2 / 2);
      const p1x = curr.x - (v1x / len1) * r;
      const p1y = curr.y - (v1y / len1) * r;
      const p2x = curr.x + (v2x / len2) * r;
      const p2y = curr.y + (v2y / len2) * r;
      d += ` L ${p1x} ${p1y} Q ${curr.x} ${curr.y} ${p2x} ${p2y}`;
    }
    const last = pts[pts.length - 1];
    d += ` L ${last.x} ${last.y}`;
    return d;
  }

  protected edgeAnchors(e: CanvasEdge): {
    x1: number; y1: number; x2: number; y2: number;
    fromSide: 'left' | 'right' | 'top' | 'bottom';
    toSide: 'left' | 'right' | 'top' | 'bottom';
  } | null {
    const from = this.nodes().find(n => n.id === e.fromNodeId);
    const to   = this.nodes().find(n => n.id === e.toNodeId);
    if (!from || !to) return null;
    const { fromSide, toSide } = this.pickEdgeSides(from, to);
    const a = this.anchorOnSide(from, fromSide);
    const b = this.anchorOnSide(to, toSide);
    return { x1: a.x, y1: a.y, x2: b.x, y2: b.y, fromSide, toSide };
  }

  protected edgePath(e: CanvasEdge): string {
    const a = this.edgeAnchors(e);
    if (!a) return '';
    const lane = this.edgeLaneOffset(e);
    return this.buildOrthogonalPath(a.x1, a.y1, a.fromSide, a.x2, a.y2, a.toSide, lane);
  }

  protected edgeLabelPos(e: CanvasEdge): { x: number; y: number } {
    const a = this.edgeAnchors(e);
    if (!a) return { x: 0, y: 0 };
    return {
      x: (a.x1 + a.x2) / 2,
      y: (a.y1 + a.y2) / 2 - 10,
    };
  }

  /** Live preview while dragging a new connection — same smart routing. */
  protected drawingEdgePath(): string {
    const de = this.drawingEdge();
    if (!de) return '';
    const from = this.nodes().find(n => n.id === de.fromNodeId);
    if (!from) {
      return `M ${de.x1} ${de.y1} L ${de.x2} ${de.y2}`;
    }
    const hoverId = this.connectHoverNodeId();
    const to = hoverId ? this.nodes().find(n => n.id === hoverId) : null;
    if (to) {
      const { fromSide, toSide } = this.pickEdgeSides(from, to);
      const a = this.anchorOnSide(from, fromSide);
      const b = this.anchorOnSide(to, toSide);
      return this.buildOrthogonalPath(a.x, a.y, fromSide, b.x, b.y, toSide);
    }
    // Free cursor: exit from best side toward cursor
    const c = this.nodeCenter(from);
    const dx = de.x2 - c.x;
    const dy = de.y2 - c.y;
    const fromSide: 'left' | 'right' | 'top' | 'bottom' =
      Math.abs(dx) >= Math.abs(dy) ? (dx >= 0 ? 'right' : 'left') : (dy >= 0 ? 'bottom' : 'top');
    const a = this.anchorOnSide(from, fromSide);
    const toSide: 'left' | 'right' | 'top' | 'bottom' =
      fromSide === 'right' ? 'left' : fromSide === 'left' ? 'right' : fromSide === 'bottom' ? 'top' : 'bottom';
    return this.buildOrthogonalPath(a.x, a.y, fromSide, de.x2, de.y2, toSide);
  }

  protected nodeColor(type: NodeType): string {
    switch (type) {
      case 'Start':             return '#16a34a';
      case 'End':               return '#dc2626';
      case 'ExclusiveGateway':  return '#d97706';
      case 'InclusiveGateway':  return '#db2777';
      case 'ParallelGateway':   return '#0891b2';
      case 'JoinGateway':       return '#0e7490';
      case 'Timer':             return '#ca8a04';
      case 'WaitEvent':         return '#ea580c';
      case 'ServiceTask':       return '#2563eb';
      case 'CallActivity':      return '#4f46e5';
      case 'ScriptTask':        return '#0d9488';
      case 'NotificationTask':  return '#7c3aed';
      default:                  return '#5040D6';
    }
  }

  /** Theme-aware task node body fill/stroke for SVG (reads CSS vars from shell). */
  protected taskFill(selected: boolean): string {
    return selected ? 'var(--ds-node-task-bg-sel)' : 'var(--ds-node-task-bg)';
  }

  protected taskStroke(selected: boolean): string {
    return selected ? 'var(--ds-node-task-stroke-sel)' : 'var(--ds-node-task-stroke)';
  }

  protected nodeLabelFill(): string { return 'var(--ds-node-label)'; }
  protected nodeSubLabelFill(): string { return 'var(--ds-node-sublabel)'; }
  protected edgeStroke(): string { return 'var(--ds-edge)'; }
  protected edgeSelectedStroke(): string { return 'var(--ds-edge-selected)'; }
  protected edgeDefaultStroke(): string { return 'var(--ds-edge-default)'; }
  protected gridDotFill(): string { return 'var(--ds-grid-dot)'; }

  protected startFill(selected: boolean): string {
    return this.theme.isDark()
      ? (selected ? '#15803d' : '#166534')
      : (selected ? '#16a34a' : '#22c55e');
  }

  protected startStroke(selected: boolean): string {
    return this.theme.isDark()
      ? (selected ? '#4ade80' : '#16a34a')
      : (selected ? '#14532d' : '#15803d');
  }

  protected endFill(selected: boolean): string {
    return this.theme.isDark()
      ? (selected ? '#7f1d1d' : '#450a0a')
      : (selected ? '#dc2626' : '#ef4444');
  }

  protected endStroke(selected: boolean): string {
    return this.theme.isDark()
      ? (selected ? '#f87171' : '#dc2626')
      : (selected ? '#7f1d1d' : '#b91c1c');
  }

  protected gatewayFill(selected: boolean): string {
    return this.theme.isDark()
      ? (selected ? '#422006' : '#1c0f02')
      : (selected ? '#fffbeb' : '#ffffff');
  }

  protected trackById(_i: number, item: { id: string }): string { return item.id; }

  protected goBack(): void {
    this.router.navigate(['/admin/workflow/definitions', this.definitionId(), 'versions']);
  }

  protected readonly canHaveEdge = (type: NodeType): boolean => type !== 'End';

  protected truncateText(text: string, maxChars: number): string {
    if (!text) return '';
    return text.length > maxChars ? text.slice(0, maxChars - 1) + '…' : text;
  }

  protected nodeSubLabel(n: CanvasNode): string {
    if ((n.type === 'UserTask' || n.type === 'MainActivity')) {
      return this.selectedAssignmentGroupName(n.assignmentGroupId) || n.assignmentKey || '';
    }
    if (n.type === 'ServiceTask' && n.actionKey) return n.actionKey;
    if (n.type === 'CallActivity') {
      const cfg = this.parseConfig(n.configurationJson);
      return typeof cfg['definitionKey'] === 'string' ? cfg['definitionKey'] as string : '';
    }
    if (n.type === 'ScriptTask') {
      const cfg = this.parseConfig(n.configurationJson);
      return cfg['setVariables'] ? 'setVariables' : '';
    }
    if (n.type === 'WaitEvent') {
      const cfg = this.parseConfig(n.configurationJson);
      return typeof cfg['eventKey'] === 'string' ? cfg['eventKey'] as string : '';
    }
    if (n.type === 'Timer') {
      const cfg = this.parseConfig(n.configurationJson);
      return typeof cfg['timerType'] === 'string' ? cfg['timerType'] as string : '';
    }
    return '';
  }

  protected paletteNodesInCategory(categoryId: string): typeof this.paletteNodes {
    const q = this.paletteSearch().toLowerCase().trim();
    return this.paletteNodes.filter(p =>
      p.category === categoryId &&
      (!q || p.label.toLowerCase().includes(q) || p.type.toLowerCase().includes(q))
    );
  }

  // ── Panel resize handles ──────────────────────────────────────────────────

  protected startInspectorResize(event: MouseEvent): void {
    event.preventDefault();
    this.inspectorResizing     = true;
    this.inspectorResizeStartX = event.clientX;
    this.inspectorResizeStartW = this.inspectorWidth();
  }

  protected startDrawerResize(event: MouseEvent): void {
    event.preventDefault();
    this.drawerResizing     = true;
    this.drawerResizeStartY = event.clientY;
    this.drawerResizeStartH = this.drawerHeight();
  }

  // ── Quick-add node ────────────────────────────────────────────────────────

  protected quickAddBtnPos(n: CanvasNode): { x: number; y: number } {
    // Keep quick-add below the outbound port so it never steals connect drags
    if (this.isGateway(n.type)) return { x: n.x + GATEWAY_R + 10, y: n.y + GATEWAY_R + 18 };
    if (n.type === 'Start') return { x: n.x + 28, y: n.y + 40 };
    return { x: n.x + NODE_W + 10, y: n.y + NODE_H / 2 + 26 };
  }

  protected quickAddNode(type: NodeType, protocol?:string): void {
    if (this.isReadonly()) return;
    const sourceId = this.quickAddSourceId();
    if (!sourceId) return;
    const source = this.nodes().find(n => n.id === sourceId);
    if (!source) return;
    const gap = this.isGateway(source.type) ? GATEWAY_R * 2 + 100 : NODE_W + 100;
    const x = source.x + gap;
    const y = source.y;
    const id  = crypto.randomUUID();
    const key = `${type.toLowerCase()}_${Date.now()}`;
    const newNode: CanvasNode = {
      id, nodeKey: key, type,
      name: this.defaultNodeName(type), nameAr: '',
      actionKey: '', assignmentKey: '', assignmentGroupId: '', assignmentPurpose: '', configurationJson: '',
      x, y, outcomes: [], actions: [],
    };
    const edge: CanvasEdge = {
      id: crypto.randomUUID(), fromNodeId: sourceId, toNodeId: id,
      transitionKey: `transition_${Date.now()}`,
      labelEn: '', labelAr: '', descriptionEn: '', outcomeKey: '',
      conditionExpression: '', isDefault: false,
      priority: this.edges().filter(e => e.fromNodeId === sourceId).length,
    };
    this.pushUndo();
    if(type==='ServiceTask') { newNode.actionKey='http.request'; newNode.configurationJson=JSON.stringify({protocol:protocol||'Rest',required:protocol!=='Sms',method:'GET',path:'/',timeoutSeconds:30,maxAttempts:3,retryDelaySeconds:10}); }
    if(protocol==='Sms') newNode.name='SMS';
    if(type==='NotificationTask') newNode.configurationJson=JSON.stringify({channels:'Email',required:false,failurePolicy:'Retry',maxAttempts:3});
    this.nodes.update(ns => [...ns, newNode]);
    this.ensureSimpleActions();
    this.edges.update(es => [...es, edge]);
    this.quickAddSourceId.set(null);
    this.selectedNodeId.set(id);
    this.selectedNodeIds.set([id]);
    this.selectedEdgeId.set(null);
    this.inspectorTab.set('general');
    this.patchPropsFromNode(newNode);
    this.markUnsaved();
  }
}
