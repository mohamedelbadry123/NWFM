import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  ViewChild,
  inject,
} from '@angular/core';
import { DynamicFormRendererComponent } from '@shared/components/dynamic-form/dynamic-form-renderer.component';
import { normalizeFieldKey } from '@shared/components/dynamic-form/locked-values';
import { LocaleService } from '@core/i18n/locale.service';
import type { StepMappingSnapshot } from '../mapping/mapping.models';
import {
  StepMappingMemory,
  resolveStepMapping,
  withPrefill,
  type ResolvedStepMapping,
  withoutLocked,
  type StepDraftScope,
} from '../mapping/workflow-step-mapping';

export interface WorkflowSelectedForm {
  formId: string;
  versionNo: number;
  schemaJson?: string | null;
  visitId?: string;
  stepId?: string;
}

const NO_MAPPING: ResolvedStepMapping = {
  state: 'none',
  applyKey: '',
  locked: {},
  prefill: {},
  problems: [],
};

/**
 * Stable renderer inputs preserve partially entered answers while the workspace polls.
 *
 * A workflow Step can map values into this form (`mappingSnapshot`): Read-only values lock their field and are
 * submitted exactly as the server holds them; Prefill values seed the answers ONCE per work item, visit and
 * invocation, so polling, Refresh, a tab switch or reopening the task never replaces what the user changed.
 */
@Component({
  selector: 'app-workflow-selected-form',
  standalone: true,
  imports: [DynamicFormRendererComponent],
  template: `
    @if (mapping.state === 'waiting') {
      <p
        role="status"
        class="mb-3 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-950"
      >
        {{
          locale.isRtl()
            ? 'ما زالت قيم هذه الخطوة قيد التجهيز. ستظهر تلقائياً عند اكتمالها.'
            : 'The values for this Step are still being prepared. They appear here automatically.'
        }}
      </p>
    }
    @if (mapping.state === 'invalid') {
      <div
        role="alert"
        class="mb-3 rounded-lg border border-red-300 bg-red-50 p-3 text-sm text-red-900"
      >
        <p>
          {{
            locale.isRtl()
              ? 'القيم المرتبطة لا تطابق نموذج هذه الخطوة المثبّت، لذلك لم تُطبَّق. أعد تحميل المهمة قبل المتابعة.'
              : 'The mapped values do not match the form pinned to this Step, so none were applied. Reload the task before continuing.'
          }}
        </p>
        <ul class="mt-1 list-disc ps-5">
          @for (problem of mapping.problems; track problem) {
            <li>{{ problem }}</li>
          }
        </ul>
      </div>
    }
    @if (hasLocked) {
      <p class="mb-2 text-xs opacity-80" data-testid="locked-note">
        {{
          locale.isRtl()
            ? 'الحقول المعلّمة بشريط جانبي قيمتها محددة من سير العمل ولا يمكن تعديلها.'
            : 'Fields marked with a side bar are set by the workflow and cannot be changed.'
        }}
      </p>
    }
    @if (definition) {
      <app-dynamic-form-renderer
        [definition]="definition"
        [formId]="selectedForm.formId"
        [versionNo]="selectedForm.versionNo"
        contextType="WorkItem"
        [contextId]="selectedForm.visitId ? workItemId + ':' + selectedForm.visitId : workItemId"
        [answers]="initialAnswers"
        [readOnly]="readonly || mapping.state === 'invalid' || mapping.state === 'waiting'"
        [lockedValues]="lockedValues"
        [nullKeys]="nullKeys"
        (valueChanged)="changed($event)"
      />
    } @else {
      <p role="alert">
        {{
          locale.isRtl()
            ? 'تعذر تحميل إصدار النموذج المحدد. أعد المحاولة قبل إكمال النشاط.'
            : 'The selected form version could not be loaded. Retry before completing this activity.'
        }}
      </p>
    }
  `,
})
export class WorkflowSelectedFormComponent implements OnChanges {
  @Input({ required: true }) selectedForm!: WorkflowSelectedForm;
  @Input({ required: true }) workItemId = '';
  @Input() answers: Record<string, unknown> = {};
  @Input() readonly = false;
  /** The Step's mapped values for this activation, straight from `WorkItemDto.mappingSnapshot`. */
  @Input() mappingSnapshot: StepMappingSnapshot | null | undefined = null;
  /**
   * `WorkItemDto.formDraftKey`: the opaque identity of the ownership attempt. Stable across pending -> claimed;
   * new after release, reassign or delegate-to-another. A missing key is not continuity: nothing is persisted or
   * restored. It is a draft identity only, never a receipt or a submission id.
   */
  @Input() draftKey: string | null | undefined = null;
  /**
   * The server's own answers for this work item. Shown as-is while `readonly` and used as the baseline whenever
   * the actor or attempt changes, so another attempt's local edits are never displayed or carried over.
   */
  @Input() serverAnswers: Record<string, unknown> | null | undefined;
  /** The effective actor when acting as a demo participant; drafts never cross actors. */
  @Input() actorKey = '';
  @Output() answersChange = new EventEmitter<Record<string, unknown>>();
  @ViewChild(DynamicFormRendererComponent) renderer?: DynamicFormRendererComponent;
  readonly locale = inject(LocaleService);
  private readonly memory = inject(StepMappingMemory);
  definition: Record<string, unknown> | null = null;
  initialAnswers: Record<string, unknown> = {};
  mapping: ResolvedStepMapping = NO_MAPPING;
  /** Exact typed values for locked fields; the identity only changes when the content does. */
  lockedValues: Record<string, unknown> | null = null;
  /** Prefill keys mapped to an exact `null`: the field opens empty rather than on its schema default. */
  nullKeys: string[] | null = null;
  private key = '';
  private appliedKey = '';
  private scopeSeen = false;
  private heldActor = '';
  private heldDraftKey: string | null = null;
  /** Set when the actor or attempt changed: the next apply starts from this instead of the host's old answers. */
  private reseed: Record<string, unknown> | null = null;

  get hasLocked() {
    return !!this.lockedValues && Object.keys(this.lockedValues).length > 0;
  }

  ngOnChanges() {
    const key = JSON.stringify([
      this.workItemId,
      this.selectedForm.visitId,
      this.selectedForm.formId,
      this.selectedForm.versionNo,
      this.selectedForm.schemaJson,
    ]);
    if (this.scopeChanged()) {
      // A different actor or ownership attempt starts from the server's values, never from what the previous
      // actor or attempt typed; the new scope's own draft (if any) is recalled by applyMapping.
      const baseline = { ...(this.serverAnswers ?? {}) };
      this.appliedKey = '';
      this.reseed = baseline;
      this.initialAnswers = baseline;
      queueMicrotask(() => {
        // The host owns the answers it submits: hand it the clean baseline so an obsolete local draft is dropped.
        this.answersChange.emit(baseline);
        // The renderer keeps what was typed when the new seed has the same content as the old one (a value the
        // previous scope edited back to the original); rebuild so nothing carries over.
        this.renderer?.rebuild();
      });
    }
    // A viewer who cannot act sees the server's answers, never a local draft.
    if (this.readonly) {
      this.initialAnswers = this.serverAnswers ?? this.answers;
      this.reseed = null; // the host already holds the baseline emitted above
    }
    if (key !== this.key) {
      this.key = key;
      this.appliedKey = '';
      this.initialAnswers = this.readonly
        ? (this.serverAnswers ?? this.answers)
        : (this.reseed ?? this.answers);
      try {
        this.definition = this.selectedForm.schemaJson
          ? JSON.parse(this.selectedForm.schemaJson)
          : null;
      } catch {
        this.definition = null;
      }
    }
    this.applyMapping();
  }

  /**
   * True when the effective actor or the ownership attempt (`draftKey`) differs from the one this component last
   * held. The attempt identity is stable across pending -> claimed, so a normal claim is not a boundary; it
   * changes on release, reassign and delegate-to-another. A missing key on either side is a boundary too:
   * continuity is never assumed from null.
   */
  private scopeChanged(): boolean {
    const actor = this.actorKey ?? '';
    const draftKey = this.draftKey || null;
    const changed = this.scopeSeen && (actor !== this.heldActor || draftKey !== this.heldDraftKey);
    this.scopeSeen = true;
    this.heldActor = actor;
    this.heldDraftKey = draftKey;
    return changed;
  }

  private applyMapping() {
    const next = resolveStepMapping(
      this.workItemId,
      {
        formId: this.selectedForm.formId,
        versionNo: this.selectedForm.versionNo,
        visitId: this.selectedForm.visitId,
        stepId: this.selectedForm.stepId,
      },
      this.mappingSnapshot,
    );
    this.mapping = next;
    const locked = next.state === 'ready' && Object.keys(next.locked).length ? next.locked : null;
    if (JSON.stringify(locked) !== JSON.stringify(this.lockedValues)) this.lockedValues = locked;
    if (next.state !== 'ready' || next.applyKey === this.appliedKey || this.readonly) {
      this.syncNullKeys(next);
      return;
    }
    // A viewer who cannot act (readonly) does not consume the one-time prefill; it applies when they can.
    this.appliedKey = next.applyKey;
    const scope = this.draftScope(next);
    const remembered = this.memory.recall(next.applyKey, scope);
    // A draft restored after a reload or reopen holds only what the person changed; the mandated ReadOnly
    // values are always the server's, never the cache's.
    const base = remembered
      ? { ...withoutLocked(remembered, next.locked), ...next.locked }
      : withPrefill(this.reseed ?? this.answers, next.prefill);
    this.reseed = null;
    this.memory.remember(next.applyKey, base, scope);
    if (base === this.initialAnswers) {
      this.syncNullKeys(next);
      return;
    }
    this.initialAnswers = base;
    this.syncNullKeys(next);
    // After the current change-detection pass: the host owns the answers it passes back in.
    queueMicrotask(() => this.answersChange.emit(base));
  }

  /** Only a Prefill `null` the answers still hold is exact; once the person typed a value the key is no longer listed. */
  private syncNullKeys(next: ResolvedStepMapping) {
    const held = new Set(
      Object.keys(this.initialAnswers)
        .filter((k) => this.initialAnswers[k] === null)
        .map(normalizeFieldKey),
    );
    const nulls =
      next.state === 'ready'
        ? Object.keys(next.prefill).filter(
            (k) => next.prefill[k] === null && held.has(normalizeFieldKey(k)),
          )
        : [];
    if (JSON.stringify(nulls) !== JSON.stringify(this.nullKeys ?? []))
      this.nullKeys = nulls.length ? nulls : null;
  }

  private draftScope(mapping: ResolvedStepMapping): StepDraftScope {
    return {
      formId: this.selectedForm.formId,
      versionNo: this.selectedForm.versionNo,
      stepId: this.selectedForm.stepId,
      draftKey: this.draftKey,
      actorKey: this.actorKey,
      locked: mapping.locked,
    };
  }

  changed(values: Record<string, unknown>) {
    // Edits are drafts only: they are remembered here, and submitted solely by the host's explicit action.
    if (this.mapping.state === 'ready')
      this.memory.remember(this.mapping.applyKey, values, this.draftScope(this.mapping), true);
    this.answersChange.emit(values);
  }

  validate() {
    return (
      !!this.definition &&
      this.mapping.state !== 'invalid' &&
      this.mapping.state !== 'waiting' &&
      !!this.renderer?.validate()
    );
  }
  payload() {
    return this.renderer?.payload() ?? {};
  }
}
