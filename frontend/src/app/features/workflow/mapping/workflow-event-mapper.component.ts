import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MappingActionsStageComponent } from './mapping-actions-stage.component';
import { MappingEditContext } from './mapping-edit-context';
import {
  MappingEndpointStageComponent,
  endpointProblems,
} from './mapping-endpoint-stage.component';
import { MappingInputsStageComponent, orphanBindings } from './mapping-inputs-stage.component';
import { MappingOutputsStageComponent } from './mapping-outputs-stage.component';
import { MappingSelectorsComponent } from './mapping-selectors.component';
import { MappingSourceBrowserComponent } from './mapping-source-browser.component';
import { MappingTestStageComponent } from './mapping-test-stage.component';
import { WORKFLOW_MAPPING_HOST } from './mapping-host';
import {
  loadMappingCatalog,
  ownerKey,
  syncMappingContext,
  syncRelatedCatalogs,
} from './mapping-loader';
import type { BrokenReference } from './mapping-config';
import {
  configExpressions,
  configFingerprint,
  emptyConfiguration,
  emptyEndpoint,
  mapConfigSources,
  normalizeConfiguration,
  repinCandidate,
  serializeConfiguration,
  sourceKey,
  unmappedRequired,
  collectSources,
} from './mapping-config';
import { WorkflowMappingService } from './workflow-mapping.service';
import type { DesignerMappingConfiguration as Configuration } from './mapping.models';

export type MapperStage = 'endpoint' | 'inputs' | 'outputs' | 'actions' | 'test';
export const MAPPER_STAGES: MapperStage[] = ['endpoint', 'inputs', 'outputs', 'actions', 'test'];

/** Puts a server validation message next to the stage it is about. The server's text is authoritative; this only routes it. */
export function stageOfError(message: string): MapperStage {
  const m = message.toLowerCase();
  if (/endpoint|path|openapi|header is not permitted|schema exceeds|method|response schema/.test(m))
    return 'endpoint';
  if (/action|callable|invocation input|target|run\b|maponly/.test(m)) return 'actions';
  if (/output|forward reference/.test(m)) return 'outputs';
  return 'inputs';
}

export type StageState = 'todo' | 'done' | 'problem' | 'optional';

@Component({
  selector: 'app-workflow-event-mapper',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MappingEditContext],
  imports: [
    MappingEndpointStageComponent,
    MappingInputsStageComponent,
    MappingOutputsStageComponent,
    MappingActionsStageComponent,
    MappingTestStageComponent,
    MappingSelectorsComponent,
    MappingSourceBrowserComponent,
  ],
  styleUrl: './mapping.css',
  template: `
    <section
      class="mx-mapper"
      [attr.aria-label]="ctx.t('Typed request mapping', 'ربط الطلب المصنّف')"
      data-testid="event-mapper"
    >
      @if (ctx.readonly()) {
        <p class="mx-help mx-warn" role="status">
          {{
            ctx.t(
              'This version is read-only. You can inspect every stage but not change it.',
              'هذا الإصدار للقراءة فقط. يمكنك فحص كل مرحلة دون تغييرها.'
            )
          }}
        </p>
      }
      <div
        class="mx-stage-head"
        role="tablist"
        [attr.aria-label]="ctx.t('Mapping stages', 'مراحل الربط')"
        (keydown)="stageKey($event)"
      >
        @for (s of stages; track s; let i = $index) {
          <button
            type="button"
            role="tab"
            class="mx-stage-tab"
            [id]="'mx-tab-' + s"
            [attr.aria-selected]="stage() === s"
            [attr.aria-controls]="'mx-panel-' + s"
            [attr.tabindex]="stage() === s ? 0 : -1"
            [class.mx-stage-tab-done]="state(s) === 'done'"
            [class.mx-stage-tab-problem]="state(s) === 'problem'"
            [class.mx-stage-tab-optional]="state(s) === 'optional'"
            (click)="stage.set(s)"
          >
            {{ i + 1 }}. {{ stageLabel(s) }} — <span>{{ stateLabel(state(s)) }}</span>
          </button>
        }
      </div>

      @if (broken().length) {
        <section class="mx-slot" role="alert" data-testid="repin">
          <h4 class="mx-slot-label">{{ ctx.t('Out-of-date references', 'مراجع قديمة') }}</h4>
          <p class="mx-help">
            {{
              ctx.t(
                'These values point at a version, form or field that no longer matches the current workflow. Nothing was changed; repin each one on purpose.',
                'تشير هذه القيم إلى إصدار أو نموذج أو حقل لم يعد يطابق سير العمل الحالي. لم يتغير شيء؛ أعد التثبيت لكل منها عن قصد.'
              )
            }}
          </p>
          @for (b of broken(); track b.key) {
            <div class="mx-row">
              <span>{{ b.reference.fieldKey }}</span>
              @if (b.candidate) {
                <button
                  type="button"
                  class="wf-btn-secondary"
                  [disabled]="ctx.readonly()"
                  (click)="repin(b)"
                >
                  {{ ctx.t('Repin to the current version', 'أعد التثبيت على الإصدار الحالي') }}
                </button>
              } @else {
                <span class="mx-error">{{
                  ctx.t(
                    'No current match — choose another source.',
                    'لا يوجد مطابق حالي — اختر مصدراً آخر.'
                  )
                }}</span>
              }
            </div>
          }
        </section>
      }

      <div class="mx-split">
        <div
          class="mx-panel"
          role="tabpanel"
          [id]="'mx-panel-' + stage()"
          [attr.aria-labelledby]="'mx-tab-' + stage()"
          tabindex="0"
        >
          @switch (stage()) {
            @case ('endpoint') {
              <app-mapping-endpoint-stage
                [value]="cfg().endpoint ?? emptyEndpointValue"
                [serverProblems]="problems()['endpoint']"
                (valueChange)="update({ endpoint: $event })"
              />
            }
            @case ('inputs') {
              <app-mapping-selectors
                [selectors]="cfg().selectors"
                [expressions]="expressions()"
                (selectorsChange)="update({ selectors: $event })"
              />
              <app-mapping-inputs-stage
                [endpoint]="cfg().endpoint"
                [inputs]="cfg().inputs"
                [serverProblems]="problems()['inputs']"
                (inputsChange)="update({ inputs: $event })"
              />
            }
            @case ('outputs') {
              <app-mapping-outputs-stage
                [outputs]="cfg().outputs"
                [serverProblems]="problems()['outputs']"
                (outputsChange)="update({ outputs: $event })"
              />
            }
            @case ('actions') {
              <app-mapping-actions-stage
                [callable]="cfg().callable"
                [invocationInputs]="cfg().invocationInputs"
                [actions]="cfg().actions"
                [serverProblems]="problems()['actions']"
                (callableChange)="update({ callable: $event })"
                (invocationInputsChange)="update({ invocationInputs: $event })"
                (actionsChange)="update({ actions: $event })"
              />
            }
            @case ('test') {
              <app-mapping-test-stage
                [configuration]="cfg()"
                [httpConfiguration]="httpConfiguration"
              />
            }
          }
          <div class="mx-row">
            @if (stageIndex() > 0) {
              <button type="button" class="wf-btn-secondary" (click)="go(-1)">
                {{ ctx.t('Back', 'السابق') }}
              </button>
            }
            @if (stageIndex() < stages.length - 1) {
              <button type="button" class="wf-btn-secondary" (click)="go(1)">
                {{ ctx.t('Next', 'التالي') }}
              </button>
            }
            <button
              type="button"
              class="wf-btn-secondary"
              [disabled]="checking()"
              (click)="check()"
            >
              {{
                checking()
                  ? ctx.t('Checking…', 'جارٍ الفحص…')
                  : ctx.t('Check the whole mapping', 'افحص الربط بالكامل')
              }}
            </button>
            @if (checked() === 'ok') {
              <span class="mx-chip" role="status">{{
                ctx.t('The server accepted this mapping.', 'قبل الخادم هذا الربط.')
              }}</span>
            }
          </div>
          @for (message of problems()['other']; track message) {
            <p class="mx-error" role="alert">{{ message }}</p>
          }
        </div>
        @if (showBrowser()) {
          <app-mapping-source-browser (refresh)="reload()" />
        }
      </div>
    </section>
  `,
})
export class WorkflowEventMapperComponent implements OnInit, OnChanges {
  protected readonly ctx = inject(MappingEditContext);
  private readonly api = inject(WorkflowMappingService);
  private readonly host = inject(WORKFLOW_MAPPING_HOST, { optional: true });

  @Input() configuration: Configuration | null = null;
  @Input() readonly = false;
  /** Builds the complete HTTP configuration for Send Test; null hides Send Test. */
  @Input() httpConfiguration: (() => unknown) | null = null;
  @Output() configurationChange = new EventEmitter<Configuration>();

  protected readonly stages = MAPPER_STAGES;
  protected readonly stage = signal<MapperStage>('endpoint');
  protected readonly cfg = signal<Configuration>(emptyConfiguration());
  protected readonly emptyEndpointValue = emptyEndpoint();
  protected readonly checking = signal(false);
  protected readonly checked = signal<'' | 'ok' | 'failed'>('');
  private readonly serverErrors = signal<string[]>([]);
  /** The check itself could not run (network, permission); distinct from the server rejecting the mapping. */
  private readonly checkFailure = signal('');
  private emitted = '';
  private loadedFor = '';

  protected readonly stageIndex = computed(() => MAPPER_STAGES.indexOf(this.stage()));
  protected readonly showBrowser = computed(() =>
    ['inputs', 'outputs', 'actions'].includes(this.stage()),
  );
  protected readonly expressions = computed(() => configExpressions(this.cfg()).map((e) => e.node));

  protected readonly problems = computed<Record<string, string[]>>(() => {
    const result: Record<string, string[]> = {
      endpoint: [],
      inputs: [],
      outputs: [],
      actions: [],
      other: [],
    };
    for (const message of this.serverErrors()) result[stageOfError(message)].push(message);
    if (this.checkFailure()) result['other'].push(this.checkFailure());
    return result;
  });

  /** References the current catalog cannot find: version/form/field pins that moved, kept visible until repinned. */
  protected readonly broken = computed<(BrokenReference & { key: string })[]>(() => {
    const catalog = this.ctx.catalog();
    if (!catalog) return [];
    const seen = new Map<string, BrokenReference & { key: string }>();
    for (const { where, node } of configExpressions(this.cfg()))
      for (const ref of collectSources(node)) {
        if (
          ref.selectorId ||
          ref.kind === 'Response' ||
          ref.kind === 'InvocationInput' ||
          (ref.kind === 'EventOutput' && ref.nodeKey === this.ctx.owner()?.nodeKey)
        )
          continue;
        if (this.ctx.descriptorFor(ref)) continue;
        const key = sourceKey(ref);
        if (!seen.has(key))
          seen.set(key, {
            key,
            reference: ref,
            where,
            candidate: repinCandidate(this.ctx.treeSources(), ref),
          });
      }
    return [...seen.values()];
  });

  constructor() {
    effect(() => {
      const c = this.cfg();
      untracked(() => {
        this.ctx.endpoint.set(c.endpoint);
        this.ctx.invocationInputs.set(c.invocationInputs ?? null);
        this.ctx.selectors.set(c.selectors);
      });
    });
    // Related workflows' own catalogs, loaded per selector id AND its exact definition/version pins; a retargeted
    // selector drops its old catalog and a removed one is forgotten.
    effect(() => {
      this.ctx.selectors();
      untracked(() => syncRelatedCatalogs(this.ctx, this.api));
    });
  }

  ngOnInit() {
    this.syncOwner();
    void this.reload();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['readonly']) this.syncOwner();
    if (changes['configuration']) {
      const incoming = normalizeConfiguration(this.configuration ?? emptyConfiguration());
      // The parent echoes our own emission back; only a genuinely different document replaces local state.
      if (configFingerprint(serializeConfiguration(incoming)) !== this.emitted) {
        this.cfg.set(incoming);
        this.emitted = configFingerprint(serializeConfiguration(incoming));
        this.serverErrors.set([]);
        this.checkFailure.set('');
        this.checked.set('');
      }
      this.syncOwner();
      if (this.loadedFor && this.loadedFor !== ownerKey(this.host)) void this.reload();
    }
  }

  private syncOwner() {
    syncMappingContext(this.ctx, this.host, this.readonly);
  }

  /** Saves the draft so the server catalog matches what the author sees, then reads the catalog and related-workflow choices. */
  async reload() {
    this.syncOwner();
    this.loadedFor = ownerKey(this.host);
    await loadMappingCatalog(this.ctx, this.api, this.host);
  }

  // ── editing ──────────────────────────────────────────────────────────────
  protected update(change: Partial<Configuration>) {
    if (this.ctx.readonly()) return;
    const next: Configuration = { ...this.cfg(), ...change };
    if (this.host?.isChild()) {
      const root = this.ctx.root();
      if (root)
        next.treeRoot = {
          workflowDefinitionId: root.definitionId,
          workflowVersionId: root.versionId,
        };
    } else delete next.treeRoot;
    if (!next.callable && !next.invocationInputs) delete next.invocationInputs;
    this.cfg.set(next);
    this.checked.set('');
    this.serverErrors.set([]);
    this.checkFailure.set('');
    const serialized = serializeConfiguration(next);
    this.emitted = configFingerprint(serialized);
    this.configurationChange.emit(serialized);
  }

  protected repin(broken: BrokenReference) {
    const candidate = broken.candidate;
    if (!candidate) return;
    const from = sourceKey(broken.reference);
    this.update(
      mapConfigSources(this.cfg(), (ref) =>
        sourceKey(ref) === from && !ref.selectorId
          ? { ...ref, ...candidate.reference, jsonPointer: ref.jsonPointer }
          : ref,
      ),
    );
    this.ctx.announce(
      this.ctx.t(
        'Reference repinned to the current version.',
        'تمت إعادة تثبيت المرجع على الإصدار الحالي.',
      ),
    );
  }

  // ── validation and completion ─────────────────────────────────────────────
  protected async check() {
    const root = this.ctx.root();
    const owner = this.ctx.owner();
    if (!root || !owner) return;
    this.checking.set(true);
    this.checked.set('');
    this.checkFailure.set('');
    try {
      const result = await firstValueFrom(
        this.api.validate(root.definitionId, root.versionId, {
          owner,
          configuration: serializeConfiguration(this.cfg()),
        }),
      );
      this.serverErrors.set(result.errors ?? []);
      this.checked.set(result.valid ? 'ok' : 'failed');
      const first = (result.errors ?? [])[0];
      if (first) this.stage.set(stageOfError(first));
    } catch (e) {
      const message = (e as { error?: { message?: string } })?.error?.message;
      // Not about any one stage: shown under whichever stage is open, never routed away from the author.
      this.checkFailure.set(
        message ??
          this.ctx.t('The server could not check this mapping.', 'تعذر على الخادم فحص هذا الربط.'),
      );
      this.checked.set('failed');
    } finally {
      this.checking.set(false);
    }
  }

  protected state(stage: MapperStage): StageState {
    const c = this.cfg();
    const hasServer = (this.problems()[stage] ?? []).length > 0;
    if (hasServer) return 'problem';
    switch (stage) {
      case 'endpoint':
        return !c.endpoint ? 'todo' : endpointProblems(c.endpoint).length ? 'problem' : 'done';
      case 'inputs':
        if (orphanBindings(c.endpoint, c.inputs).length) return 'problem';
        return !c.endpoint
          ? 'todo'
          : unmappedRequired(c).length
            ? 'problem'
            : c.inputs.length || c.selectors.length
              ? 'done'
              : 'optional';
      case 'outputs':
        return c.outputs.length ? 'done' : 'optional';
      case 'actions':
        return c.actions.length || c.callable ? 'done' : 'optional';
      case 'test':
        return 'optional';
    }
  }
  protected stateLabel(state: StageState) {
    return state === 'done'
      ? this.ctx.t('Done', 'مكتمل')
      : state === 'problem'
        ? this.ctx.t('Needs attention', 'يحتاج انتباهاً')
        : state === 'optional'
          ? this.ctx.t('Optional', 'اختياري')
          : this.ctx.t('Not started', 'لم يبدأ');
  }
  protected stageLabel(stage: MapperStage) {
    return {
      endpoint: this.ctx.t('Endpoint', 'نقطة النهاية'),
      inputs: this.ctx.t('Inputs', 'المدخلات'),
      outputs: this.ctx.t('Outputs', 'المخرجات'),
      actions: this.ctx.t('Follow-up actions', 'الإجراءات اللاحقة'),
      test: this.ctx.t('Test', 'اختبار'),
    }[stage];
  }
  protected go(by: number) {
    this.stage.set(
      MAPPER_STAGES[Math.min(MAPPER_STAGES.length - 1, Math.max(0, this.stageIndex() + by))],
    );
  }
  protected stageKey(event: KeyboardEvent) {
    const rtl = document.documentElement.dir === 'rtl';
    const forward = event.key === (rtl ? 'ArrowLeft' : 'ArrowRight');
    const back = event.key === (rtl ? 'ArrowRight' : 'ArrowLeft');
    if (!forward && !back && event.key !== 'Home' && event.key !== 'End') return;
    event.preventDefault();
    const index =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? MAPPER_STAGES.length - 1
          : this.stageIndex() + (forward ? 1 : -1);
    const next = MAPPER_STAGES[(index + MAPPER_STAGES.length) % MAPPER_STAGES.length];
    this.stage.set(next);
    queueMicrotask(() => document.getElementById('mx-tab-' + next)?.focus());
  }
}
