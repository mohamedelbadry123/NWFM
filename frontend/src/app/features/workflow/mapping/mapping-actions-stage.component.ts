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
import { MappingSchemaEditorComponent } from './mapping-schema-editor.component';
import { MappingTransformEditorComponent } from './mapping-transform-editor.component';
import type {
  CallableTarget,
  CallableTargetDescriptor,
  FollowUpAction,
  FollowUpMode,
  MappingSchema,
} from './mapping.models';
import { emptyNode, newId, newSchema, objectNode } from './mapping-config';

export const targetKey = (t: CallableTarget) =>
  `${t.workflowDefinitionId}|${t.workflowVersionId}|${t.nodeKey}`;

/** The mapping an action starts with: one slot per input the receiving event declared, so nothing is forgotten. */
export function initialActionInputs(inputs: MappingSchema | null | undefined) {
  const names = Object.keys(inputs?.properties ?? {});
  return objectNode(Object.fromEntries(names.map((n) => [n, emptyNode()])));
}

/**
 * Follow-up actions and callable inputs. An event may call other callable events of THIS workflow tree after it
 * completes: "Map only" prepares their inputs, "Run" also starts them. It can itself be made callable, which needs a
 * declared inputs schema. If a target is unavailable the action can wait or ask for attention; it never invents a value.
 */
@Component({
  selector: 'app-mapping-actions-stage',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MappingSchemaEditorComponent, MappingTransformEditorComponent],
  styleUrl: './mapping.css',
  template: `
    <section class="mx-slot" data-testid="callable">
      <label class="mx-field">
        <span>{{
          ctx.t('Other events can call this event', 'يمكن لأحداث أخرى استدعاء هذا الحدث')
        }}</span>
        <input
          type="checkbox"
          [ngModel]="callable"
          [disabled]="ctx.readonly()"
          (ngModelChange)="setCallable($event)"
        />
      </label>
      @if (callable) {
        <p class="mx-help">
          {{
            ctx.t(
              'Describe the inputs a caller must provide. They become sources named “Inputs this event receives”.',
              'صف المدخلات التي يجب أن يوفرها المستدعي. تصبح مصادر باسم «المدخلات التي يستقبلها هذا الحدث».'
            )
          }}
        </p>
        <app-mapping-schema-editor
          [schema]="invocationInputs ?? emptyInputs"
          [label]="ctx.t('Inputs received', 'المدخلات المستقبلة')"
          idPath="callable-inputs"
          (schemaChange)="invocationInputsChange.emit($event)"
        />
      }
    </section>

    <h4 class="mx-slot-label">{{ ctx.t('Follow-up actions', 'الإجراءات اللاحقة') }}</h4>
    @for (action of actions; track action.id; let i = $index) {
      <section
        class="mx-slot"
        [attr.data-action]="action.id"
        [attr.aria-label]="ctx.t('Action ', 'الإجراء ') + (i + 1)"
      >
        <div class="mx-row">
          <label class="mx-field mx-grow">
            {{ ctx.t('Call this event', 'استدعاء هذا الحدث') }}
            <select
              class="wf-input"
              [ngModel]="targetKey(action.target)"
              [disabled]="ctx.readonly()"
              (ngModelChange)="setTarget(i, $event)"
            >
              @if (!known(action)) {
                <option [value]="targetKey(action.target)">
                  {{ ctx.t('Unavailable: ', 'غير متاح: ') }}{{ action.target.nodeKey }}
                </option>
              }
              @for (t of targets(); track targetKey(t.target)) {
                <option [value]="targetKey(t.target)">
                  {{ t.label }} — {{ t.breadcrumbs.join(' › ') }}
                </option>
              }
            </select>
          </label>
          <label class="mx-field">
            {{ ctx.t('What to do', 'ماذا يُفعل') }}
            <select
              class="wf-input"
              [ngModel]="action.mode"
              [disabled]="ctx.readonly()"
              (ngModelChange)="patch(i, { mode: $event })"
            >
              <option value="MapOnly">
                {{ ctx.t('Map only (prepare the inputs)', 'ربط فقط (تجهيز المدخلات)') }}
              </option>
              <option value="Run">
                {{ ctx.t('Run (prepare and start)', 'تشغيل (تجهيز وبدء)') }}
              </option>
            </select>
          </label>
          @if (!ctx.readonly()) {
            <button type="button" class="mx-link mx-danger" (click)="remove(i)">
              {{ ctx.t('Remove action', 'إزالة الإجراء') }}
            </button>
          }
        </div>
        @if (!known(action)) {
          <p class="mx-error" role="alert">
            {{
              ctx.t(
                'This target is not a callable event of the current workflow tree (it may have been removed, or a new version was created).',
                'هذا الهدف ليس حدثاً قابلاً للاستدعاء في شجرة سير العمل الحالية (ربما أُزيل أو أُنشئ إصدار جديد).'
              )
            }}
            @if (candidate(action); as c) {
              <button
                type="button"
                class="wf-btn-secondary"
                [disabled]="ctx.readonly()"
                (click)="patch(i, { target: c.target })"
              >
                {{ ctx.t('Use the current version: ', 'استخدم الإصدار الحالي: ') }}{{ c.label }}
              </button>
            }
          </p>
        }
        <app-mapping-transform-editor
          [node]="action.inputs"
          [label]="ctx.t('Inputs for the call', 'مدخلات الاستدعاء')"
          [expected]="inputsOf(action)"
          [idPath]="'action:' + action.id"
          [showSummary]="true"
          (nodeChange)="patch(i, { inputs: $event })"
        />
        <div class="mx-row">
          <label class="mx-field">
            {{ ctx.t('If the target is not available', 'إذا لم يكن الهدف متاحاً') }}
            <select
              class="wf-input"
              [ngModel]="action.unavailableTarget?.mode ?? 'Attention'"
              [disabled]="ctx.readonly()"
              (ngModelChange)="setPolicy(i, $event)"
            >
              <option value="Attention">
                {{ ctx.t('Ask for attention (default)', 'اطلب الانتباه (افتراضي)') }}
              </option>
              <option value="Wait">
                {{
                  ctx.t('Wait for it, then use the timeout outcome', 'انتظر ثم استخدم نتيجة المهلة')
                }}
              </option>
            </select>
          </label>
          @if (action.unavailableTarget?.mode === 'Wait') {
            <label class="mx-field">
              {{ ctx.t('Wait up to (seconds)', 'الانتظار حتى (ثوانٍ)') }}
              <input
                class="wf-input"
                type="number"
                min="1"
                max="31536000"
                [ngModel]="action.unavailableTarget?.deadlineSeconds"
                [disabled]="ctx.readonly()"
                (ngModelChange)="patchPolicy(i, { deadlineSeconds: $event })"
              />
            </label>
            <label class="mx-field">
              {{ ctx.t('Outcome on timeout', 'النتيجة عند انتهاء المهلة') }}
              <input
                class="wf-input"
                [ngModel]="action.unavailableTarget?.timeoutOutcome"
                [disabled]="ctx.readonly()"
                (ngModelChange)="patchPolicy(i, { timeoutOutcome: $event })"
              />
            </label>
          }
        </div>
      </section>
    } @empty {
      <p class="mx-help">{{ ctx.t('No follow-up actions.', 'لا توجد إجراءات لاحقة.') }}</p>
    }
    @if (!ctx.readonly()) {
      <button type="button" class="wf-btn-secondary" [disabled]="!targets().length" (click)="add()">
        {{ ctx.t('Add follow-up action', 'إضافة إجراء لاحق') }}
      </button>
      @if (!targets().length) {
        <p class="mx-help">
          {{
            ctx.t(
              'No other callable event exists in this workflow tree. Make an event callable (enable the option above on it) first.',
              'لا يوجد حدث آخر قابل للاستدعاء في شجرة سير العمل. اجعل حدثاً قابلاً للاستدعاء أولاً.'
            )
          }}
        </p>
      }
    }
    @for (message of serverProblems; track message) {
      <p class="mx-error" role="alert">{{ message }}</p>
    }
  `,
})
export class MappingActionsStageComponent {
  protected readonly ctx = inject(MappingEditContext);
  @Input({ required: true }) callable = false;
  @Input() invocationInputs: MappingSchema | null | undefined = null;
  @Input({ required: true }) actions: FollowUpAction[] = [];
  @Input() serverProblems: string[] = [];
  @Output() callableChange = new EventEmitter<boolean>();
  @Output() invocationInputsChange = new EventEmitter<MappingSchema | null>();
  @Output() actionsChange = new EventEmitter<FollowUpAction[]>();
  protected readonly emptyInputs: MappingSchema = newSchema('object');
  protected readonly targetKey = targetKey;

  /** Callable events of the current tree, excluding this one (an event cannot call itself). */
  protected targets(): CallableTargetDescriptor[] {
    const owner = this.ctx.owner();
    return (this.ctx.catalog()?.targets ?? []).filter(
      (t) => t.callable && (!owner || targetKey(t.target) !== targetKey(owner)),
    );
  }
  protected known(action: FollowUpAction) {
    return this.targets().some((t) => targetKey(t.target) === targetKey(action.target));
  }
  /** The same node in the current tree when only the version pin moved. */
  protected candidate(action: FollowUpAction) {
    return this.targets().find(
      (t) =>
        t.target.nodeKey === action.target.nodeKey &&
        t.target.workflowDefinitionId === action.target.workflowDefinitionId,
    );
  }
  protected inputsOf(action: FollowUpAction) {
    return (
      this.targets().find((t) => targetKey(t.target) === targetKey(action.target))?.inputs ?? null
    );
  }
  protected setCallable(on: boolean) {
    this.callableChange.emit(on);
    this.invocationInputsChange.emit(on ? (this.invocationInputs ?? newSchema('object')) : null);
  }
  private emit(next: FollowUpAction[]) {
    this.actionsChange.emit(next);
  }
  protected patch(index: number, change: Partial<FollowUpAction>) {
    this.emit(this.actions.map((a, i) => (i === index ? { ...a, ...change } : a)));
  }
  protected setTarget(index: number, key: string) {
    const descriptor = this.targets().find((t) => targetKey(t.target) === key);
    if (!descriptor) return;
    this.patch(index, {
      target: descriptor.target,
      inputs: initialActionInputs(descriptor.inputs),
    });
  }
  protected setPolicy(index: number, mode: string) {
    const unavailableTarget =
      mode === 'Wait'
        ? { mode: 'Wait' as const, deadlineSeconds: 3600, timeoutOutcome: 'timeout' }
        : null;
    // Attention is the default and stays absent; only Wait writes a policy.
    const rest: FollowUpAction = { ...this.actions[index] };
    delete rest.unavailableTarget;
    const next: FollowUpAction = unavailableTarget ? { ...rest, unavailableTarget } : rest;
    this.emit(this.actions.map((a, i) => (i === index ? next : a)));
  }
  protected patchPolicy(
    index: number,
    change: { deadlineSeconds?: number; timeoutOutcome?: string },
  ) {
    const current = this.actions[index].unavailableTarget;
    if (current) this.patch(index, { unavailableTarget: { ...current, ...change } });
  }
  protected remove(index: number) {
    this.emit(this.actions.filter((_, i) => i !== index));
  }
  protected add() {
    const first = this.targets()[0];
    if (!first) return;
    const mode: FollowUpMode = 'MapOnly';
    this.emit([
      ...this.actions,
      {
        id: newId(
          'act',
          this.actions.map((a) => a.id),
        ),
        target: first.target,
        mode,
        inputs: initialActionInputs(first.inputs),
      },
    ]);
  }
}
