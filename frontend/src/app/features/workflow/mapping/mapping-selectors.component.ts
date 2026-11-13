import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MappingEditContext } from './mapping-edit-context';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import type {
  MappingDefinitionChoice,
  MissingDataPolicy,
  RelatedInstanceSelector,
  SourceReference,
  TransformNode,
} from './mapping.models';
import { collectSources, emptyNode, newId } from './mapping-config';

export const SELECTOR_STATUSES = [
  'Pending',
  'Running',
  'Completed',
  'Suspended',
  'Failed',
  'Cancelled',
];
const UNITS: { id: string; seconds: number; label: [string, string] }[] = [
  { id: 'minutes', seconds: 60, label: ['minutes', 'دقائق'] },
  { id: 'hours', seconds: 3600, label: ['hours', 'ساعات'] },
  { id: 'days', seconds: 86400, label: ['days', 'أيام'] },
];

/** Splits a number of seconds into the largest whole unit (for display); the stored value is always seconds. */
export function splitDuration(seconds: number | null | undefined): {
  amount: number;
  unit: string;
} {
  const value = seconds ?? 0;
  for (const unit of [...UNITS].reverse())
    if (value >= unit.seconds && value % unit.seconds === 0)
      return { amount: value / unit.seconds, unit: unit.id };
  return { amount: Math.max(1, Math.round(value / 60)) || 1, unit: 'minutes' };
}
export const durationSeconds = (amount: number, unit: string) =>
  Math.round(amount * (UNITS.find((u) => u.id === unit)?.seconds ?? 60));

/** A new selector with the settled defaults: business key equality, ambiguity and no-match both stop for attention. */
export function newSelector(
  choice: MappingDefinitionChoice,
  taken: string[],
): RelatedInstanceSelector {
  return {
    id: newId('related', taken),
    workflowDefinitionId: choice.definitionId,
    workflowVersionId: choice.versionId,
    keyKind: 'BusinessKey',
    keyValue: emptyNode(),
    multipleMatches: 'Attention',
    missing: { mode: 'Attention' },
  };
}

/**
 * Criteria for choosing ONE instance of another workflow to read from. Defaults are the safe settled ones:
 * equality on the business key, stop for attention when nothing or several match. Once an instance is chosen it
 * is frozen for the whole invocation, and the time window is anchored when the event starts — the server
 * enforces both; this panel only states them.
 */
@Component({
  selector: 'app-mapping-selectors',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MappingTransformEditorComponent],
  styleUrl: './mapping.css',
  template: `
    <section class="mx-stage" [attr.aria-label]="ctx.t('Related workflows', 'سير عمل مرتبط')">
      <h3 class="mx-stage-title">{{ ctx.t('Related workflows', 'سير عمل مرتبط') }}</h3>
      <p class="mx-help">
        {{
          ctx.t(
            'Read data from another workflow’s instance. Choose the workflow, then say exactly which instance by matching a key. The server checks your access again every time.',
            'اقرأ بيانات من نسخة سير عمل آخر. اختر سير العمل ثم حدد النسخة بمطابقة مفتاح. يتحقق الخادم من صلاحياتك في كل مرة.'
          )
        }}
      </p>
      @for (selector of selectors; track selector.id; let i = $index) {
        <fieldset class="mx-slot" [attr.data-selector]="selector.id" [disabled]="ctx.readonly()">
          <legend class="mx-slot-label">{{ choiceName(selector) }}</legend>
          <label class="mx-field">
            {{ ctx.t('Related workflow', 'سير العمل المرتبط') }}
            <select
              class="wf-input"
              [ngModel]="selector.workflowVersionId"
              (ngModelChange)="chooseWorkflow(i, $event)"
            >
              @if (!hasChoice(selector)) {
                <option [value]="selector.workflowVersionId">
                  {{
                    ctx.t(
                      'Unavailable workflow (kept until corrected)',
                      'سير عمل غير متاح (يبقى حتى التصحيح)'
                    )
                  }}
                </option>
              }
              @for (c of ctx.choices(); track c.versionId) {
                <option [value]="c.versionId">{{ c.name }} · v{{ c.versionNumber }}</option>
              }
            </select>
          </label>
          @if (!hasChoice(selector)) {
            <p class="mx-error" role="alert">
              {{
                ctx.t(
                  'This workflow version is no longer offered to you. Pick another to continue.',
                  'لم يعد إصدار سير العمل هذا متاحاً لك. اختر آخر للمتابعة.'
                )
              }}
            </p>
          }

          <label class="mx-field">
            {{ ctx.t('Match instances by', 'مطابقة النسخ حسب') }}
            <select
              class="wf-input"
              [ngModel]="selector.keyKind"
              (ngModelChange)="patch(i, { keyKind: $event })"
            >
              <option value="BusinessKey">
                {{
                  ctx.t(
                    'Business key (the business entity, e.g. the customer or request ID)',
                    'المفتاح التجاري (الكيان التجاري مثل رقم العميل أو الطلب)'
                  )
                }}
              </option>
              <option value="CorrelationKey">
                {{ ctx.t('Correlation key', 'مفتاح المطابقة') }}
              </option>
            </select>
          </label>
          <app-mapping-transform-editor
            [node]="selector.keyValue"
            [label]="
              ctx.t(
                'Key to match (must equal the related instance’s key)',
                'المفتاح المراد مطابقته (يجب أن يساوي مفتاح النسخة المرتبطة)'
              )
            "
            [idPath]="'selector.' + selector.id + '.key'"
            [expected]="{ type: 'string' }"
            (nodeChange)="patch(i, { keyValue: $event })"
          />

          <details>
            <summary>{{ ctx.t('More criteria (optional)', 'معايير إضافية (اختياري)') }}</summary>
            <label class="mx-field">
              <span>{{
                ctx.t(
                  'Only instances started by a specific user',
                  'فقط النسخ التي بدأها مستخدم محدد'
                )
              }}</span>
              <input
                type="checkbox"
                [ngModel]="!!selector.startedByUserId"
                (ngModelChange)="toggleStarter(i, $event)"
              />
            </label>
            @if (selector.startedByUserId) {
              <app-mapping-transform-editor
                [node]="selector.startedByUserId"
                [label]="
                  ctx.t(
                    'User who started the instance (a user ID, not the customer)',
                    'المستخدم الذي بدأ النسخة (معرّف مستخدم وليس العميل)'
                  )
                "
                [idPath]="'selector.' + selector.id + '.starter'"
                [expected]="{ type: 'string' }"
                (nodeChange)="patch(i, { startedByUserId: $event })"
              />
            }
            <label class="mx-field">
              {{ ctx.t('Only instances with status', 'فقط النسخ بالحالة') }}
              <select
                class="wf-input"
                [ngModel]="selector.status ?? ''"
                (ngModelChange)="patch(i, { status: $event || null })"
              >
                <option value="">{{ ctx.t('Any status', 'أي حالة') }}</option>
                @for (s of statuses; track s) {
                  <option [value]="s">{{ s }}</option>
                }
              </select>
            </label>
            <div class="mx-row">
              <label class="mx-field">
                <span>{{
                  ctx.t(
                    'Only instances started within a time window',
                    'فقط النسخ التي بدأت ضمن نافذة زمنية'
                  )
                }}</span>
                <input
                  type="checkbox"
                  [ngModel]="!!selector.windowSeconds"
                  (ngModelChange)="patch(i, { windowSeconds: $event ? 86400 : null })"
                />
              </label>
              @if (selector.windowSeconds) {
                <label class="mx-field">
                  {{ ctx.t('Within the last', 'خلال آخر') }}
                  <input
                    class="wf-input"
                    type="number"
                    min="1"
                    [ngModel]="duration(selector.windowSeconds).amount"
                    (ngModelChange)="setWindow(i, $event, duration(selector.windowSeconds).unit)"
                  />
                </label>
                <label class="mx-field">
                  {{ ctx.t('Unit', 'الوحدة') }}
                  <select
                    class="wf-input"
                    [ngModel]="duration(selector.windowSeconds).unit"
                    (ngModelChange)="setWindow(i, duration(selector.windowSeconds).amount, $event)"
                  >
                    @for (u of units; track u.id) {
                      <option [value]="u.id">{{ ctx.t(u.label[0], u.label[1]) }}</option>
                    }
                  </select>
                </label>
              }
            </div>
            @if (selector.windowSeconds) {
              <p class="mx-help">
                {{
                  ctx.t(
                    'The window is anchored when this event starts and never slides while waiting.',
                    'تُثبَّت النافذة عند بدء هذا الحدث ولا تتحرك أثناء الانتظار.'
                  )
                }}
              </p>
            }
          </details>

          <label class="mx-field">
            {{ ctx.t('If several instances match', 'إذا طابقت عدة نسخ') }}
            <select
              class="wf-input"
              [ngModel]="selector.multipleMatches"
              (ngModelChange)="patch(i, { multipleMatches: $event })"
            >
              <option value="Attention">
                {{ ctx.t('Stop and ask for attention (default)', 'توقف واطلب الانتباه (افتراضي)') }}
              </option>
              <option value="Latest">
                {{
                  ctx.t(
                    'Use the latest (newest start; ties by instance ID)',
                    'استخدم الأحدث (أحدث بدء؛ وعند التساوي حسب المعرّف)'
                  )
                }}
              </option>
            </select>
          </label>

          <label class="mx-field">
            {{ ctx.t('If no instance matches', 'إذا لم تطابق أي نسخة') }}
            <select
              class="wf-input"
              [ngModel]="selector.missing?.mode ?? 'Attention'"
              (ngModelChange)="setMissingMode(i, $event)"
            >
              <option value="Attention">
                {{ ctx.t('Stop and ask for attention (default)', 'توقف واطلب الانتباه (افتراضي)') }}
              </option>
              <option value="Wait">
                {{
                  ctx.t(
                    'Wait until a deadline, then use a timeout outcome',
                    'انتظر حتى موعد نهائي ثم استخدم نتيجة المهلة'
                  )
                }}
              </option>
              <option value="Fallback">
                {{ ctx.t('Continue with explicit fallback values', 'تابع بقيم بديلة صريحة') }}
              </option>
            </select>
          </label>
          @if (selector.missing?.mode === 'Wait') {
            <div class="mx-row">
              <label class="mx-field">
                {{ ctx.t('Wait up to', 'الانتظار حتى') }}
                <input
                  class="wf-input"
                  type="number"
                  min="1"
                  [ngModel]="duration(selector.missing?.deadlineSeconds).amount"
                  (ngModelChange)="
                    setWait(i, $event, duration(selector.missing?.deadlineSeconds).unit)
                  "
                />
              </label>
              <label class="mx-field">
                {{ ctx.t('Unit', 'الوحدة') }}
                <select
                  class="wf-input"
                  [ngModel]="duration(selector.missing?.deadlineSeconds).unit"
                  (ngModelChange)="
                    setWait(i, duration(selector.missing?.deadlineSeconds).amount, $event)
                  "
                >
                  @for (u of units; track u.id) {
                    <option [value]="u.id">{{ ctx.t(u.label[0], u.label[1]) }}</option>
                  }
                </select>
              </label>
              <label class="mx-field">
                {{ ctx.t('Outcome when the wait ends', 'النتيجة عند انتهاء الانتظار') }}
                <input
                  class="wf-input"
                  [ngModel]="selector.missing?.timeoutOutcome"
                  (ngModelChange)="patchMissing(i, { timeoutOutcome: $event })"
                />
              </label>
            </div>
          }
          @if (selector.missing?.mode === 'Fallback') {
            <p class="mx-help mx-warn">
              {{
                ctx.t(
                  'No instance is invented. Give each value you read from this workflow its own fallback (or use “First available value”), otherwise that value still stops for attention.',
                  'لا يتم اختلاق نسخة. حدد لكل قيمة تقرأها من سير العمل هذا قيمة بديلة (أو استخدم "أول قيمة متوفرة") وإلا ستتوقف للانتباه.'
                )
              }}
            </p>
          }
          <p class="mx-help">
            {{
              ctx.t(
                'Once an instance is chosen it is frozen for this invocation and never switches while waiting.',
                'عند اختيار نسخة تُثبَّت لهذا الاستدعاء ولا تتغير أثناء الانتظار.'
              )
            }}
          </p>
          <div class="mx-row">
            <details>
              <summary>{{ ctx.t('Technical details', 'تفاصيل تقنية') }}</summary>
              <p class="mx-help">
                {{ ctx.t('Stable selector ID', 'معرّف المحدد الثابت') }}:
                <code>{{ selector.id }}</code>
              </p>
            </details>
            @if (!ctx.readonly()) {
              <button type="button" class="mx-link mx-danger" (click)="remove(i)">
                {{ ctx.t('Remove related workflow', 'إزالة سير العمل المرتبط') }}
              </button>
            }
          </div>
          @if (usage(selector.id) > 0) {
            <p class="mx-help mx-warn">
              {{
                ctx.t(
                  usage(selector.id) +
                    ' value(s) read from this workflow; they stay in your mapping and are flagged until corrected.',
                  'تُقرأ ' +
                    usage(selector.id) +
                    ' قيمة من سير العمل هذا؛ تبقى في الربط وتُعلَّم حتى تُصحَّح.'
                )
              }}
            </p>
          }
        </fieldset>
      }
      @if (!ctx.readonly()) {
        <div class="mx-row">
          <label class="mx-field">
            {{ ctx.t('Add a related workflow', 'إضافة سير عمل مرتبط') }}
            <select
              class="wf-input"
              [ngModel]="''"
              (ngModelChange)="add($event)"
              [disabled]="selectors.length >= 20 || !ctx.choices().length"
            >
              <option value="">
                {{
                  ctx.choices().length
                    ? ctx.t('Choose…', 'اختر…')
                    : ctx.t(
                        'No related workflows available to you',
                        'لا توجد سير عمل مرتبطة متاحة لك'
                      )
                }}
              </option>
              @for (c of ctx.choices(); track c.versionId) {
                <option [value]="c.versionId">{{ c.name }} · v{{ c.versionNumber }}</option>
              }
            </select>
          </label>
        </div>
      }
    </section>
  `,
})
export class MappingSelectorsComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) selectors: RelatedInstanceSelector[] = [];
  /** Every expression of the configuration, to count how many values read through each selector. */
  @Input() expressions: TransformNode[] = [];
  @Output() selectorsChange = new EventEmitter<RelatedInstanceSelector[]>();
  protected readonly statuses = SELECTOR_STATUSES;
  protected readonly units = UNITS;

  protected duration = splitDuration;
  protected hasChoice(selector: RelatedInstanceSelector) {
    return this.ctx.choices().some((c) => c.versionId === selector.workflowVersionId);
  }
  protected choiceName(selector: RelatedInstanceSelector) {
    return (
      this.ctx.choices().find((c) => c.versionId === selector.workflowVersionId)?.name ??
      this.ctx.t('Related workflow', 'سير عمل مرتبط')
    );
  }
  protected usage(id: string) {
    return this.expressions
      .flatMap((e) => collectSources(e))
      .filter((s: SourceReference) => s.selectorId === id).length;
  }
  private emit(next: RelatedInstanceSelector[]) {
    this.selectorsChange.emit(next);
  }
  protected patch(i: number, change: Partial<RelatedInstanceSelector>) {
    const next = this.selectors.map((s, n) => (n === i ? { ...s, ...change } : s));
    // Absent optional members must be absent, not null: the contract treats them differently from "explicit null".
    for (const key of ['startedByUserId', 'status', 'windowSeconds'] as const)
      if (next[i][key] === null) delete next[i][key];
    this.emit(next);
  }
  protected chooseWorkflow(i: number, versionId: string) {
    const choice = this.ctx.choices().find((c) => c.versionId === versionId);
    if (choice)
      this.patch(i, {
        workflowDefinitionId: choice.definitionId,
        workflowVersionId: choice.versionId,
      });
  }
  protected add(versionId: string) {
    const choice = this.ctx.choices().find((c) => c.versionId === versionId);
    if (choice)
      this.emit([
        ...this.selectors,
        newSelector(
          choice,
          this.selectors.map((s) => s.id),
        ),
      ]);
  }
  protected remove(i: number) {
    this.emit(this.selectors.filter((_, n) => n !== i));
  }
  protected toggleStarter(i: number, on: boolean) {
    this.patch(i, { startedByUserId: on ? emptyNode() : null });
  }
  protected setWindow(i: number, amount: number, unit: string) {
    this.patch(i, {
      windowSeconds: Math.min(31536000, Math.max(1, durationSeconds(amount || 1, unit))),
    });
  }
  protected setMissingMode(i: number, mode: string) {
    // A selector Fallback is ALWAYS `fallback: null`: it permits per-value fallbacks and never invents an instance.
    const missing: MissingDataPolicy =
      mode === 'Wait'
        ? { mode: 'Wait', deadlineSeconds: 86400, timeoutOutcome: 'timeout' }
        : mode === 'Fallback'
          ? { mode: 'Fallback', fallback: null }
          : { mode: 'Attention' };
    this.patch(i, { missing });
  }
  protected patchMissing(i: number, change: Partial<MissingDataPolicy>) {
    this.patch(i, { missing: { ...(this.selectors[i].missing as MissingDataPolicy), ...change } });
  }
  protected setWait(i: number, amount: number, unit: string) {
    this.patchMissing(i, {
      deadlineSeconds: Math.min(31536000, Math.max(1, durationSeconds(amount || 1, unit))),
    });
  }
}
