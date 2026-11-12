import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowIntegrationsService } from '../integrations/workflow-integrations.service';
import { WorkflowMappingService } from './workflow-mapping.service';
import type { MappingInvocationDto, PreparedMappingInputsDto } from './mapping.models';

/** Receipts that are independent calls: they never complete a Step or move the workflow. */
export const isIndependentInvocation = (kind: string) => kind === 'Callable' || kind === 'MapOnly';

/**
 * Retry is only for an invocation that stopped for attention. A normal graph invocation whose graph step already
 * advanced cannot be replayed (the server refuses it), so it is never offered; OptionalGraph advances early on
 * purpose and stays retryable for its one background delivery.
 */
const needsRecovery = (invocation: MappingInvocationDto, canManage: boolean) =>
  canManage &&
  invocation.status === 'Attention' &&
  !(invocation.executionKind === 'Graph' && invocation.graphAdvanced);

/** Retry re-prepares an invocation that has no delivery job yet (a preparation fault). */
export const canRetryInvocation = (invocation: MappingInvocationDto, canManage: boolean) =>
  needsRecovery(invocation, canManage) && !invocation.operationId;

/**
 * Once a delivery job exists the server refuses a retry; the supported recovery is replaying that operation (a
 * delivery that already succeeded is reapplied without being sent again).
 */
export const canReplayInvocation = (invocation: MappingInvocationDto, canManage: boolean) =>
  needsRecovery(invocation, canManage) && !!invocation.operationId;

/**
 * Mapping receipts for one workflow instance tree, shown where the instance's events and integrations already are.
 * Shows status, the waiting deadline, why a receipt needs attention, which independent target a follow-up used
 * and — for "Prepare inputs only" — the prepared inputs behind an explicit, authorized read.
 */
@Component({
  selector: 'app-workflow-mapping-invocations',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="rounded-xl border bg-white dark:bg-dark-800 p-5 space-y-3"
      [attr.aria-label]="
        t('Mapped values and follow-up calls', 'القيم المرتبطة والاستدعاءات التالية')
      "
    >
      <h2 class="text-lg font-semibold">
        {{ t('Mapped values and follow-up calls', 'القيم المرتبطة والاستدعاءات التالية') }}
      </h2>
      @if (error()) {
        <p role="alert" class="text-red-700">{{ error() }}</p>
      }
      @for (item of items(); track item.id) {
        <article class="rounded-lg border p-3" [attr.data-invocation]="item.id">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <strong>{{ kindLabel(item) }}</strong>
            <span
              class="rounded-full border px-2 py-0.5 text-xs"
              [class.border-red-400]="item.status === 'Attention'"
              >{{ statusLabel(item.status) }}</span
            >
          </div>
          <p class="mt-1 text-sm">
            {{ t('Target', 'الهدف') }}: <code>{{ item.targetNodeKey }}</code>
            @if (item.actionId) {
              · {{ t('Action', 'الإجراء') }}: <code>{{ item.actionId }}</code>
            }
          </p>
          @if (independent(item)) {
            <p class="text-xs opacity-80">
              {{
                t(
                  'Independent call: it does not complete the destination Step or move the workflow.',
                  'استدعاء مستقل: لا يكمل خطوة الوجهة ولا ينقل سير العمل.'
                )
              }}
            </p>
          }
          @if (item.status === 'Waiting' && item.deadline) {
            <p class="text-sm" role="status">
              {{ t('Waiting for data until', 'بانتظار البيانات حتى') }}
              {{ item.deadline | date: 'medium' }}
            </p>
          }
          @if (item.status === 'Attention') {
            <p class="text-sm text-red-700" role="alert">
              {{ t('Needs attention', 'يحتاج انتباهاً') }}: {{ item.error || '—' }}
            </p>
          }
          @if (item.status === 'Completed' && item.completedAt) {
            <p class="text-xs opacity-70">
              {{ t('Completed', 'اكتمل') }} {{ item.completedAt | date: 'medium' }}
            </p>
          }
          <div class="mt-2 flex flex-wrap gap-2">
            @if (retryable(item)) {
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="busy()"
                (click)="retry(item)"
              >
                {{ t('Retry', 'إعادة المحاولة') }}
              </button>
            }
            @if (replayable(item)) {
              <button
                type="button"
                class="wf-btn-secondary"
                data-action="replay"
                [disabled]="busy()"
                (click)="replay(item)"
              >
                {{ t('Replay delivery', 'إعادة إرسال العملية') }}
              </button>
            }
            @if (item.executionKind === 'MapOnly' && item.status === 'Completed' && canManage) {
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="busy()"
                (click)="showInputs(item)"
              >
                {{ t('View prepared inputs', 'عرض المدخلات المجهزة') }}
              </button>
            }
          </div>
          @if (prepared()[item.id]; as inputs) {
            <details class="mt-2" open>
              <summary>{{ t('Prepared inputs', 'المدخلات المجهزة') }}</summary>
              <pre class="max-h-56 overflow-auto p-3 text-xs">{{ pretty(inputs.inputs) }}</pre>
              <p class="text-xs opacity-70">
                {{
                  t(
                    'Prepared only. Nothing was sent to the target and the workflow did not advance.',
                    'تم التجهيز فقط. لم يُرسل شيء إلى الهدف ولم يتقدم سير العمل.'
                  )
                }}
                {{ inputs.provenance.length }} {{ t('source(s) used.', 'مصدر مستخدم.') }}
              </p>
            </details>
          }
        </article>
      } @empty {
        <p class="text-sm opacity-70">
          {{ t('No mapping activity for this instance yet.', 'لا يوجد نشاط ربط لهذه النسخة بعد.') }}
        </p>
      }
    </section>
  `,
})
export class WorkflowMappingInvocationsComponent implements OnChanges {
  @Input({ required: true }) rootInstanceId = '';
  /** Change this value to reload (the host's polling tick). */
  @Input() refreshToken: unknown = 0;
  @Input() canManage = false;
  /** Emits every successful load so the host can explain a Preparing work item from the same receipts. */
  @Output() loaded = new EventEmitter<MappingInvocationDto[]>();
  private readonly api = inject(WorkflowMappingService);
  private readonly integrations = inject(WorkflowIntegrationsService);
  private readonly locale = inject(LocaleService);
  private readonly destroy = inject(DestroyRef);
  readonly items = signal<MappingInvocationDto[]>([]);
  readonly prepared = signal<Record<string, PreparedMappingInputsDto>>({});
  readonly error = signal('');
  readonly busy = signal(false);

  t(en: string, ar: string) {
    return this.locale.isRtl() ? ar : en;
  }
  independent = (item: MappingInvocationDto) => isIndependentInvocation(item.executionKind);
  retryable = (item: MappingInvocationDto) => canRetryInvocation(item, this.canManage);
  replayable = (item: MappingInvocationDto) => canReplayInvocation(item, this.canManage);
  pretty(value: unknown) {
    return JSON.stringify(value, null, 2);
  }
  kindLabel(item: MappingInvocationDto) {
    const labels: Record<string, [string, string]> = {
      Graph: ['Event in the workflow sequence', 'حدث ضمن تسلسل سير العمل'],
      OptionalGraph: ['Event delivered in the background', 'حدث يُرسل في الخلفية'],
      RequiredEvent: ['Required Step event', 'حدث مطلوب للخطوة'],
      OptionalEvent: ['Background Step event', 'حدث خلفي للخطوة'],
      Callable: ['Follow-up call (prepare and run)', 'استدعاء لاحق (تجهيز وتشغيل)'],
      MapOnly: ['Follow-up (prepare inputs only)', 'إجراء لاحق (تجهيز المدخلات فقط)'],
      Step: ['Values mapped into a Step form', 'قيم مرتبطة بنموذج الخطوة'],
    };
    const label = labels[item.executionKind];
    return label ? this.t(...label) : item.executionKind;
  }
  statusLabel(status: string) {
    const labels: Record<string, [string, string]> = {
      Preparing: ['Preparing', 'قيد التجهيز'],
      Waiting: ['Waiting for data', 'بانتظار البيانات'],
      Prepared: ['Prepared', 'مجهز'],
      Delivered: ['Delivered', 'تم الإرسال'],
      Completed: ['Completed', 'مكتمل'],
      Attention: ['Needs attention', 'يحتاج انتباهاً'],
      Cancelled: ['Cancelled', 'ملغى'],
    };
    const label = labels[status];
    return label ? this.t(...label) : status;
  }

  ngOnChanges() {
    this.load();
  }
  private load() {
    if (!this.rootInstanceId) return;
    this.api
      .invocationsForRoot(this.rootInstanceId)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (items) => {
          this.items.set(items);
          this.loaded.emit(items);
          this.error.set('');
        },
        // A viewer without receipt access simply sees no receipts; it is not an instance error.
        error: () => {
          this.items.set([]);
          this.loaded.emit([]);
        },
      });
  }
  retry(item: MappingInvocationDto) {
    this.busy.set(true);
    this.api.retryInvocation(item.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(e?.error?.message || this.t('Retry was refused.', 'تم رفض إعادة المحاولة.'));
      },
    });
  }
  replay(item: MappingInvocationDto) {
    if (!item.operationId) return;
    this.busy.set(true);
    this.integrations.replay(item.operationId).subscribe({
      next: () => {
        this.busy.set(false);
        this.error.set('');
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(e?.error?.message || this.t('Replay was refused.', 'تم رفض إعادة الإرسال.'));
      },
    });
  }
  showInputs(item: MappingInvocationDto) {
    this.busy.set(true);
    this.api.preparedInputs(item.id).subscribe({
      next: (inputs) => {
        this.busy.set(false);
        this.prepared.update((all) => ({ ...all, [item.id]: inputs }));
      },
      error: (e) => {
        this.busy.set(false);
        this.error.set(
          e?.error?.message ||
            this.t('You cannot read these inputs.', 'لا يمكنك قراءة هذه المدخلات.'),
        );
      },
    });
  }
}
