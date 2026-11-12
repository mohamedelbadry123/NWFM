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
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import { WORKFLOW_MAPPING_HOST } from './mapping-host';
import {
  loadMappingCatalog,
  ownerKey,
  syncMappingContext,
  syncRelatedCatalogs,
} from './mapping-loader';
import { MappingSelectorsComponent } from './mapping-selectors.component';
import { MappingSourceBrowserComponent } from './mapping-source-browser.component';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import type {
  DesignerMappingConfiguration,
  SourceDescriptor,
  StepBindingMode,
  StepFieldBinding,
} from './mapping.models';
import {
  configExpressions,
  emptyConfiguration,
  emptyNode,
  newId,
  normalizeConfiguration,
  serializeConfiguration,
} from './mapping-config';
import { WorkflowMappingService } from './workflow-mapping.service';

/** The form fields of this Step that can receive a mapped value: the catalog's FormField sources of the owner Step. */
export function stepDestinations(
  sources: SourceDescriptor[],
  owner: { nodeKey: string; workflowDefinitionId: string } | null,
) {
  if (!owner) return [];
  return sources.filter(
    (s) =>
      s.reference.kind === 'FormField' &&
      s.reference.nodeKey === owner.nodeKey &&
      s.reference.workflowDefinitionId === owner.workflowDefinitionId,
  );
}

export const bindingKey = (
  b: Pick<StepFieldBinding, 'formStepId' | 'formId' | 'formVersion' | 'fieldKey'>,
) => `${b.formStepId}|${b.formId}|${b.formVersion}|${b.fieldKey}`;

const destinationKey = (d: SourceDescriptor) =>
  bindingKey({
    formStepId: d.reference.formStepId ?? '',
    formId: d.reference.formId ?? '',
    formVersion: d.reference.formVersion ?? 0,
    fieldKey: d.reference.fieldKey,
  });

/**
 * Step values: fill fields of this Step's form from mapped data. "Read-only" fixes the value (the person cannot change
 * it and the server refuses any other); "Prefill" only supplies the starting value. Bindings are saved with this Step and
 * pinned to its exact form, version and Step form, so a changed form shows up here as out of date instead of failing at
 * runtime. A field key is literal — a dot is part of the name, not a nesting path.
 */
@Component({
  selector: 'app-workflow-step-mapping',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MappingEditContext],
  imports: [
    FormsModule,
    MappingSelectorsComponent,
    MappingSourceBrowserComponent,
    MappingTransformEditorComponent,
  ],
  styleUrl: './mapping.css',
  template: `
    <section
      class="mx-mapper"
      [attr.aria-label]="ctx.t('Step field values', 'قيم حقول الخطوة')"
      data-testid="step-mapping"
    >
      <h3 class="mx-stage-title">
        {{ ctx.t('Step field values from mapped data', 'قيم حقول الخطوة من بيانات مربوطة') }}
      </h3>
      <p class="mx-help">
        {{
          ctx.t(
            'Fill fields of this Step’s form from earlier events, form answers or related workflows. Read-only values cannot be changed by the person; prefilled values can.',
            'املأ حقول نموذج هذه الخطوة من أحداث سابقة أو إجابات نماذج أو سير عمل مرتبط. القيم للقراءة فقط لا يمكن للشخص تغييرها؛ أما المعبأة مسبقاً فيمكنه تعديلها.'
          )
        }}
      </p>
      @if (ctx.loading()) {
        <p class="mx-help" role="status">{{ ctx.t('Loading sources…', 'جارٍ تحميل المصادر…') }}</p>
      }
      @if (ctx.catalogError()) {
        <p class="mx-error" role="alert">
          {{ ctx.catalogError() }}
          <button type="button" class="mx-link" (click)="reload()">
            {{ ctx.t('Try again', 'حاول مجدداً') }}
          </button>
        </p>
      }
      <div class="mx-split">
        <div class="mx-panel">
          <app-mapping-selectors
            [selectors]="cfg().selectors"
            [expressions]="expressions()"
            (selectorsChange)="update({ selectors: $event })"
          />
          @for (b of cfg().stepBindings; track b.id) {
            <section
              class="mx-slot"
              [attr.data-binding]="b.id"
              [attr.aria-label]="ctx.t('Field ', 'الحقل ') + b.fieldKey"
            >
              <div class="mx-row">
                <strong class="mx-grow" dir="auto">{{ labelOf(b) }}</strong>
                <label class="mx-field">
                  {{ ctx.t('Behaviour', 'السلوك') }}
                  <select
                    class="wf-input"
                    [ngModel]="b.mode"
                    [disabled]="ctx.readonly()"
                    (ngModelChange)="patch(b.id, { mode: $event })"
                  >
                    <option value="ReadOnly">
                      {{ ctx.t('Read-only (fixed)', 'للقراءة فقط (ثابت)') }}
                    </option>
                    <option value="Prefill">
                      {{ ctx.t('Prefill (editable)', 'تعبئة مسبقة (قابل للتعديل)') }}
                    </option>
                  </select>
                </label>
                @if (!ctx.readonly()) {
                  <button type="button" class="mx-link mx-danger" (click)="remove(b.id)">
                    {{ ctx.t('Remove', 'إزالة') }}
                  </button>
                }
              </div>
              @if (outOfDate(b)) {
                <p class="mx-error" role="alert">
                  {{
                    ctx.t(
                      'This field no longer matches the current form or version.',
                      'لم يعد هذا الحقل يطابق النموذج أو الإصدار الحالي.'
                    )
                  }}
                  @if (replacement(b); as r) {
                    <button
                      type="button"
                      class="wf-btn-secondary"
                      [disabled]="ctx.readonly()"
                      (click)="repin(b, r)"
                    >
                      {{ ctx.t('Repin to the current form', 'أعد التثبيت على النموذج الحالي') }}
                    </button>
                  }
                </p>
              }
              <app-mapping-transform-editor
                [node]="b.value"
                [label]="b.fieldKey"
                [expected]="b.schema"
                [idPath]="'step:' + b.id"
                [showSummary]="true"
                (nodeChange)="patch(b.id, { value: $event })"
              />
            </section>
          } @empty {
            <p class="mx-help">
              {{
                ctx.t('No fields are mapped on this Step.', 'لا توجد حقول مربوطة في هذه الخطوة.')
              }}
            </p>
          }
          @if (!ctx.readonly()) {
            <div class="mx-row">
              <label class="mx-field mx-grow">
                {{ ctx.t('Field to fill', 'الحقل المراد ملؤه') }}
                <select
                  class="wf-input"
                  [ngModel]="chosen()"
                  (ngModelChange)="chosen.set($event)"
                  [disabled]="!available().length"
                >
                  <option value="">{{ ctx.t('Choose a field…', 'اختر حقلاً…') }}</option>
                  @for (d of available(); track key(d)) {
                    <option [value]="key(d)">
                      {{ d.label }} — {{ d.breadcrumbs.join(' › ') }}
                    </option>
                  }
                </select>
              </label>
              <button type="button" class="wf-btn-secondary" [disabled]="!chosen()" (click)="add()">
                {{ ctx.t('Map this field', 'ربط هذا الحقل') }}
              </button>
            </div>
            @if (!destinations().length && !ctx.loading()) {
              <p class="mx-help">
                {{
                  ctx.t(
                    'This Step has no form fields yet. Attach a form to the Step first.',
                    'لا تحتوي هذه الخطوة على حقول نموذج بعد. أرفق نموذجاً بالخطوة أولاً.'
                  )
                }}
              </p>
            }
          }
          @for (message of serverProblems(); track message) {
            <p class="mx-error" role="alert">{{ message }}</p>
          }
          <button type="button" class="wf-btn-secondary" [disabled]="checking()" (click)="check()">
            {{
              checking()
                ? ctx.t('Checking…', 'جارٍ الفحص…')
                : ctx.t('Check these bindings', 'افحص هذه الروابط')
            }}
          </button>
          @if (checked() === 'ok') {
            <span class="mx-chip" role="status">{{
              ctx.t('The server accepted these bindings.', 'قبل الخادم هذه الروابط.')
            }}</span>
          }
        </div>
        <app-mapping-source-browser (refresh)="reload()" />
      </div>
    </section>
  `,
})
export class WorkflowStepMappingComponent implements OnInit, OnChanges {
  protected readonly ctx = inject(MappingEditContext);
  private readonly api = inject(WorkflowMappingService);
  private readonly host = inject(WORKFLOW_MAPPING_HOST, { optional: true });

  /** The Step node's configuration JSON; only `typedMapping.stepBindings` and `.selectors` are edited here. */
  @Input() configuration = '{}';
  @Input() readonly = false;
  @Output() configurationChange = new EventEmitter<string>();

  protected readonly cfg = signal<DesignerMappingConfiguration>(emptyConfiguration());
  protected readonly chosen = signal('');
  protected readonly checking = signal(false);
  protected readonly checked = signal<'' | 'ok' | 'failed'>('');
  protected readonly serverProblems = signal<string[]>([]);
  protected readonly key = destinationKey;
  protected readonly expressions = computed(() => configExpressions(this.cfg()).map((e) => e.node));
  private original: Record<string, unknown> = {};
  private emitted = '';
  private loadedFor = '';

  protected readonly destinations = computed(() =>
    stepDestinations(this.ctx.treeSources(), this.ctx.owner()),
  );
  protected readonly available = computed(() => {
    const used = new Set(this.cfg().stepBindings.map(bindingKey));
    return this.destinations().filter((d) => !used.has(destinationKey(d)));
  });

  constructor() {
    this.ctx.hideOwnForm.set(true);
    // Related workflows' catalogs load per selector id AND its exact definition/version pins (see the event mapper).
    effect(() => {
      this.ctx.selectors();
      untracked(() => syncRelatedCatalogs(this.ctx, this.api));
    });
  }

  ngOnInit() {
    syncMappingContext(this.ctx, this.host, this.readonly);
    this.ctx.selectors.set(this.cfg().selectors);
    void this.reload();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['configuration']) {
      try {
        this.original = JSON.parse(this.configuration || '{}') as Record<string, unknown>;
      } catch {
        this.original = {};
      }
      const incoming = normalizeConfiguration(this.original['typedMapping']);
      if (JSON.stringify(serializeConfiguration(incoming)) !== this.emitted) {
        this.cfg.set(incoming);
        this.ctx.selectors.set(incoming.selectors);
      }
    }
    syncMappingContext(this.ctx, this.host, this.readonly);
    if (this.loadedFor && this.loadedFor !== ownerKey(this.host)) void this.reload();
  }

  async reload() {
    syncMappingContext(this.ctx, this.host, this.readonly);
    this.loadedFor = ownerKey(this.host);
    await loadMappingCatalog(this.ctx, this.api, this.host);
  }

  protected labelOf(b: StepFieldBinding) {
    const d = this.destinations().find((x) => destinationKey(x) === bindingKey(b));
    return d ? `${d.label} (${b.fieldKey})` : b.fieldKey;
  }
  protected outOfDate(b: StepFieldBinding) {
    return (
      !!this.ctx.catalog() && !this.destinations().some((d) => destinationKey(d) === bindingKey(b))
    );
  }
  /** The same field in the current form version, when only the pins moved. */
  protected replacement(b: StepFieldBinding) {
    return this.destinations().find(
      (d) => d.reference.fieldKey === b.fieldKey && d.reference.formStepId === b.formStepId,
    );
  }

  protected update(change: Partial<DesignerMappingConfiguration>) {
    if (this.ctx.readonly()) return;
    const next = { ...this.cfg(), ...change };
    const root = this.ctx.root();
    if (this.host?.isChild() && root)
      next.treeRoot = {
        workflowDefinitionId: root.definitionId,
        workflowVersionId: root.versionId,
      };
    else delete next.treeRoot;
    this.cfg.set(next);
    this.ctx.selectors.set(next.selectors);
    this.checked.set('');
    this.serverProblems.set([]);
    const empty = !next.stepBindings.length && !next.selectors.length;
    const config: Record<string, unknown> = { ...this.original };
    if (empty) delete config['typedMapping'];
    else config['typedMapping'] = serializeConfiguration(next);
    this.emitted = JSON.stringify(serializeConfiguration(next));
    this.original = config;
    this.configurationChange.emit(JSON.stringify(config));
  }

  protected patch(id: string, change: Partial<StepFieldBinding>) {
    this.update({
      stepBindings: this.cfg().stepBindings.map((b) => (b.id === id ? { ...b, ...change } : b)),
    });
  }
  protected remove(id: string) {
    this.update({ stepBindings: this.cfg().stepBindings.filter((b) => b.id !== id) });
  }
  protected add() {
    const d = this.available().find((x) => destinationKey(x) === this.chosen());
    if (!d) return;
    const mode: StepBindingMode = 'ReadOnly';
    this.update({
      stepBindings: [
        ...this.cfg().stepBindings,
        {
          id: newId(
            'step',
            this.cfg().stepBindings.map((b) => b.id),
          ),
          formStepId: d.reference.formStepId ?? '',
          formId: d.reference.formId ?? '',
          formVersion: d.reference.formVersion ?? 0,
          fieldKey: d.reference.fieldKey,
          mode,
          schema: { ...d.schema, required: undefined, inferred: undefined },
          value: emptyNode(),
        },
      ],
    });
    this.chosen.set('');
  }
  protected repin(b: StepFieldBinding, d: SourceDescriptor) {
    this.patch(b.id, {
      formId: d.reference.formId ?? b.formId,
      formVersion: d.reference.formVersion ?? b.formVersion,
      schema: { ...d.schema, required: undefined, inferred: undefined },
    });
    this.ctx.announce(
      this.ctx.t(
        'Field repinned to the current form.',
        'تمت إعادة تثبيت الحقل على النموذج الحالي.',
      ),
    );
  }

  protected async check() {
    const root = this.ctx.root();
    const owner = this.ctx.owner();
    if (!root || !owner) return;
    this.checking.set(true);
    try {
      const result = await this.api
        .validate(root.definitionId, root.versionId, {
          owner,
          configuration: serializeConfiguration(this.cfg()),
        })
        .toPromise();
      this.serverProblems.set(result?.errors ?? []);
      this.checked.set(result?.valid ? 'ok' : 'failed');
    } catch (e) {
      this.serverProblems.set([
        (e as { error?: { message?: string } })?.error?.message ??
          this.ctx.t(
            'The server could not check these bindings.',
            'تعذر على الخادم فحص هذه الروابط.',
          ),
      ]);
      this.checked.set('failed');
    } finally {
      this.checking.set(false);
    }
  }
}
