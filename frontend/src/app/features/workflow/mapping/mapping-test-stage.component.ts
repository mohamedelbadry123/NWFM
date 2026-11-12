import { ChangeDetectionStrategy, Component, Input, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import { WorkflowMappingService } from './workflow-mapping.service';
import type {
  DesignerMappingConfiguration,
  HttpTestResult,
  MappingPreviewResult,
  MappingPreviewStage,
  MappingSample,
  SourceReference,
} from './mapping.models';
import { exampleFor, sampleSources, serializeConfiguration, sourceKey } from './mapping-config';

/**
 * Test: a dry run of the mapping on sample values you type here. Preview is evaluated by the server with no
 * network call. Send Test sends only the prepared request through the connection the server already protects; it
 * never starts follow-up actions and never writes workflow data. Sample values live only in this panel — they are
 * not part of the saved configuration.
 */
@Component({
  selector: 'app-mapping-test-stage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styleUrl: './mapping.css',
  template: `
    <p class="mx-help">
      {{
        ctx.t(
          'Sample values are for this test only and are never saved with the workflow.',
          'قيم العينة لهذا الاختبار فقط ولا تُحفظ مع سير العمل.'
        )
      }}
    </p>
    <label class="mx-field">
      {{ ctx.t('What to preview', 'ما المراد معاينته') }}
      <select class="wf-input" [ngModel]="stage()" (ngModelChange)="stage.set($event)">
        <option value="Request">{{ ctx.t('The request', 'الطلب') }}</option>
        <option value="Completion">
          {{ ctx.t('Outputs and follow-up actions', 'المخرجات والإجراءات اللاحقة') }}
        </option>
        <option value="Step">{{ ctx.t('Step field values', 'قيم حقول الخطوة') }}</option>
      </select>
    </label>

    @for (ref of sources(); track key(ref)) {
      <label class="mx-field" [attr.data-sample]="ref.fieldKey">
        {{ ctx.labelOf(ref) }}
        <span class="mx-crumbs">{{ crumbs(ref) }}</span>
        <textarea
          class="wf-input mx-code"
          rows="2"
          dir="ltr"
          spellcheck="false"
          [ngModel]="sampleText(ref)"
          [attr.aria-invalid]="!!sampleError(ref)"
          (ngModelChange)="setSample(ref, $event)"
        ></textarea>
        @if (sampleError(ref)) {
          <span class="mx-error" role="alert">{{ sampleError(ref) }}</span>
        }
      </label>
    } @empty {
      <p class="mx-help">
        {{
          ctx.t(
            'This mapping does not read any source, so no sample values are needed.',
            'لا يقرأ هذا الربط أي مصدر، لذا لا حاجة لقيم عينة.'
          )
        }}
      </p>
    }

    <div class="mx-row">
      <button
        type="button"
        class="wf-btn-secondary"
        [disabled]="busy() || !canPreview()"
        (click)="preview()"
      >
        {{
          busy()
            ? ctx.t('Working…', 'جارٍ العمل…')
            : ctx.t('Preview (no network)', 'معاينة (دون شبكة)')
        }}
      </button>
    </div>
    @if (error()) {
      <p class="mx-error" role="alert">{{ error() }}</p>
    }
    @if (result(); as r) {
      <section class="mx-slot" data-testid="preview-result" aria-live="polite">
        @for (e of r.errors; track e) {
          <p class="mx-error" role="alert">{{ e }}</p>
        }
        @if (r.request; as req) {
          <h4 class="mx-slot-label">{{ ctx.t('Prepared request', 'الطلب المُجهّز') }}</h4>
          <pre class="mx-pre" dir="ltr"
            >{{ req.method }} {{ req.path }}{{ queryText(req.query) }}</pre>
          @if (hasKeys(req.headers)) {
            <pre class="mx-pre" dir="ltr">{{ pretty(req.headers) }}</pre>
          }
          @if (req.body !== undefined && req.body !== null) {
            <pre class="mx-pre" dir="ltr">{{ pretty(req.body) }}</pre>
          }
        }
        @if (r.outputs) {
          <h4 class="mx-slot-label">{{ ctx.t('Outputs', 'المخرجات') }}</h4>
          <pre class="mx-pre" dir="ltr">{{ pretty(r.outputs) }}</pre>
        }
        @if (r.actions?.length) {
          <h4 class="mx-slot-label">
            {{
              ctx.t(
                'Follow-up actions (prepared, not dispatched)',
                'الإجراءات اللاحقة (مُجهّزة ولم تُرسل)'
              )
            }}
          </h4>
          <pre class="mx-pre" dir="ltr">{{ pretty(r.actions) }}</pre>
        }
        @if (r.stepValues) {
          <h4 class="mx-slot-label">{{ ctx.t('Step field values', 'قيم حقول الخطوة') }}</h4>
          <pre class="mx-pre" dir="ltr">{{ pretty(r.stepValues) }}</pre>
        }
      </section>
    }

    @if (httpConfiguration) {
      <section class="mx-slot">
        <h4 class="mx-slot-label">{{ ctx.t('Send Test', 'إرسال اختبار') }}</h4>
        <p class="mx-help">
          {{
            ctx.t(
              'Sends the previewed request through the selected connection and shows its answer. It does not prove the endpoint matches the declared types, and it never starts follow-up actions or saves anything.',
              'يرسل الطلب المعاين عبر الاتصال المحدد ويعرض إجابته. لا يثبت أن نقطة النهاية تطابق الأنواع المعلنة، ولا يبدأ إجراءات لاحقة ولا يحفظ شيئاً.'
            )
          }}
        </p>
        <label class="mx-field">
          <span>{{
            ctx.t('I want to send this real request now', 'أريد إرسال هذا الطلب الفعلي الآن')
          }}</span>
          <input type="checkbox" [ngModel]="confirmed()" (ngModelChange)="confirmed.set($event)" />
        </label>
        <button type="button" class="wf-btn-secondary" [disabled]="!canSend()" (click)="send()">
          {{ ctx.t('Send Test', 'إرسال اختبار') }}
        </button>
        @if (!result()?.request) {
          <p class="mx-help">{{ ctx.t('Preview the request first.', 'عاين الطلب أولاً.') }}</p>
        }
        @if (sent(); as s) {
          <div role="status" data-testid="send-result">
            <span class="mx-chip" [class.mx-chip-warn]="!s.success">{{
              s.success ? ctx.t('Succeeded', 'نجح') : ctx.t('Failed', 'فشل')
            }}</span>
            @if (s.statusCode) {
              <span class="mx-chip">HTTP {{ s.statusCode }}</span>
            }
            @if (s.error) {
              <p class="mx-error">{{ s.error }}</p>
            }
            @if (s.body) {
              <pre class="mx-pre" dir="ltr">{{ s.body }}</pre>
            }
          </div>
        }
      </section>
    }
  `,
})
export class MappingTestStageComponent {
  protected readonly ctx = inject(MappingEditContext);
  private readonly api = inject(WorkflowMappingService);
  @Input({ required: true }) configuration!: DesignerMappingConfiguration;
  /** The full HTTP configuration for Send Test (connection etc.); absent outside the integration editor. */
  @Input() httpConfiguration: (() => unknown) | null = null;

  protected readonly stage = signal<MappingPreviewStage>('Request');
  private readonly texts = signal<Record<string, string>>({});
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly result = signal<MappingPreviewResult | null>(null);
  protected readonly confirmed = signal(false);
  protected readonly sent = signal<HttpTestResult | null>(null);
  protected key = sourceKey;
  protected pretty = (value: unknown) => JSON.stringify(value, null, 2);
  protected hasKeys = (value: object | null | undefined) =>
    !!value && Object.keys(value).length > 0;
  protected crumbs = (ref: SourceReference) =>
    this.ctx.descriptorFor(ref)?.breadcrumbs.join(' › ') ?? '';

  protected sources(): SourceReference[] {
    return sampleSources(this.configuration).filter(
      (ref) => ref.kind !== 'EventOutput' || !!this.ctx.descriptorFor(ref),
    );
  }
  protected sampleText(ref: SourceReference) {
    return this.texts()[sourceKey(ref)] ?? JSON.stringify(exampleFor(this.ctx.schemaFor(ref)));
  }
  protected setSample(ref: SourceReference, text: string) {
    this.texts.update((all) => ({ ...all, [sourceKey(ref)]: text }));
    this.result.set(null);
    this.sent.set(null);
  }
  protected sampleError(ref: SourceReference) {
    try {
      JSON.parse(this.sampleText(ref));
      return '';
    } catch {
      return this.ctx.t(
        'Not valid JSON (text needs quotes).',
        'ليس JSON صالحاً (النص يحتاج علامات اقتباس).',
      );
    }
  }
  protected readonly canPreview = computed(() => !!this.ctx.root() && !!this.ctx.owner());
  protected canSend = () =>
    !!this.result()?.request &&
    !this.result()!.errors.length &&
    this.confirmed() &&
    !this.busy() &&
    this.stage() === 'Request';
  protected queryText(query: Record<string, string> | null | undefined) {
    const entries = Object.entries(query ?? {});
    return entries.length ? '?' + entries.map(([k, v]) => `${k}=${v}`).join('&') : '';
  }

  private samples(): MappingSample[] | null {
    const result: MappingSample[] = [];
    for (const ref of this.sources()) {
      if (this.sampleError(ref)) return null;
      result.push({ source: ref, value: JSON.parse(this.sampleText(ref)) as unknown });
    }
    return result;
  }
  protected preview() {
    const root = this.ctx.root();
    const owner = this.ctx.owner();
    const samples = this.samples();
    if (!root || !owner) return;
    if (!samples) {
      this.error.set(this.ctx.t('Fix the sample values first.', 'أصلح قيم العينة أولاً.'));
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.sent.set(null);
    this.confirmed.set(false);
    this.api
      .preview(root.definitionId, root.versionId, {
        configuration: serializeConfiguration(this.configuration),
        samples,
        owner,
        stage: this.stage(),
      })
      .subscribe({
        next: (result) => {
          this.busy.set(false);
          this.result.set(result);
        },
        error: (e) => {
          this.busy.set(false);
          this.result.set(null);
          this.error.set(
            e?.error?.message ??
              e?.error?.detail ??
              this.ctx.t(
                'The server could not preview this mapping.',
                'تعذر على الخادم معاينة هذا الربط.',
              ),
          );
        },
      });
  }
  protected send() {
    const prepared = this.result()?.request;
    let config: unknown;
    try {
      config = this.httpConfiguration?.();
    } catch (e) {
      // e.g. no connection selected yet — say so instead of sending nothing silently.
      this.error.set((e as Error).message);
      return;
    }
    if (!prepared || !config) return;
    this.busy.set(true);
    this.error.set('');
    this.api.sendTest(config, prepared).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.sent.set(r);
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(e?.error?.message ?? this.ctx.t('Send Test failed.', 'فشل إرسال الاختبار.'));
      },
    });
  }
}
