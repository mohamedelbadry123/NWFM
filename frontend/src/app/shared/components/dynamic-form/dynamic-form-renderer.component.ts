import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormGroup, ReactiveFormsModule, type AbstractControl } from '@angular/forms';
import { FormlyModule, type FormlyFieldConfig, type FormlyFormOptions } from '@ngx-formly/core';
import { MessageModule } from 'primeng/message';

import { deserializeSchema } from '../../form-schema/form-schema-import';
import { FormSchemaFormlyMapper } from '../../form-schema/form-schema-formly.mapper';
import {
  fromStoredAnswers,
  toPreviewPayload,
  type PreviewValueKinds,
} from '../../form-schema/form-schema-payload';
import type { RuleModel } from '../../form-schema/form-schema-rules';
import {
  FORM_STATE_CONTEXT_ID,
  FORM_STATE_CONTEXT_TYPE,
  FORM_STATE_FORM_ID,
  FORM_STATE_VERSION_NO,
} from './formly-preview.types';

/**
 * Renders a stored form schema as a live ngx-formly form.
 *
 * The single place a schema becomes a fillable form: the builder's preview, the form preview page,
 * the fill page and the submission viewer all mount this, so a field looks and behaves the same
 * wherever it appears and the control styling lives in one stylesheet instead of four.
 *
 * The component owns the `FormGroup`, the model and the value kinds; the host reads them back
 * through a template reference or `viewChild` and calls {@link payload} when it is ready to submit.
 */
@Component({
  selector: 'app-dynamic-form-renderer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, FormlyModule, MessageModule],
  template: `
    @if (fields().length === 0) {
      <p-message severity="info" [text]="emptyMessage()" styleClass="w-full" />
    } @else {
      <form
        [formGroup]="form()"
        (ngSubmit)="submitted.emit()"
        class="dynamic-form rounded-xl border border-[var(--app-border)] bg-[var(--app-surface)] p-4 md:p-5"
        [class.dynamic-form--readonly]="readOnly()"
        [attr.aria-readonly]="readOnly() ? 'true' : null"
      >
        <formly-form
          [form]="form()"
          [fields]="fields()"
          [model]="model()"
          [options]="formOptions()"
        />
      </form>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }

      :host ::ng-deep .dynamic-form formly-field {
        display: block;
      }

      /* ── Labels ─────────────────────────────────────────────────────── */
      :host ::ng-deep .dynamic-form .p-field label {
        display: block;
        margin-bottom: 0.4rem;
        font-weight: 600;
        font-size: 0.8125rem;
        letter-spacing: 0.01em;
        color: var(--p-text-color);
      }
      /* The required marker the PrimeNG form-field wrapper appends. */
      :host ::ng-deep .dynamic-form .p-field label span {
        color: var(--p-red-500, #ef4444);
        margin-inline-start: 0.15rem;
      }

      /* ── Controls ───────────────────────────────────────────────────── */
      :host ::ng-deep .dynamic-form .p-inputtext,
      :host ::ng-deep .dynamic-form .p-select,
      :host ::ng-deep .dynamic-form .p-multiselect,
      :host ::ng-deep .dynamic-form .p-datepicker {
        width: 100%;
      }
      /* The PrimeNG radio type renders one block per option — lay them out in a row. */
      :host ::ng-deep .dynamic-form formly-field-primeng-radio {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem 0.75rem;
      }
      :host ::ng-deep .dynamic-form .p-field-radiobutton {
        display: flex;
        align-items: center;
        gap: 0.15rem;
        padding: 0.35rem 0.7rem 0.35rem 0.5rem;
        border: 1px solid var(--app-border);
        border-radius: 9999px;
        transition:
          border-color 0.15s ease,
          background-color 0.15s ease;
      }
      :host ::ng-deep .dynamic-form .p-field-radiobutton:hover {
        border-color: var(--p-primary-color);
        background: color-mix(in srgb, var(--p-primary-color) 6%, transparent);
      }
      :host ::ng-deep .dynamic-form .p-field-radiobutton label {
        margin-bottom: 0;
        font-weight: 500;
        cursor: pointer;
      }

      /* ── Read-only ──────────────────────────────────────────────────── */
      /* Belt and braces beside the disabled controls below. Every field type sets
         props.disabled, but the custom ones — signature, attachment, geolocation —
         each decide for themselves what that means, and a preview that let one of
         them through would be worse than useless. Blocking pointer events on the
         container is one rule that cannot be missed by a type added later. */
      :host ::ng-deep .dynamic-form--readonly {
        pointer-events: none;
      }
      /* Anything scrollable or clickable-to-view stays usable: a long answer must
         still be readable, and a photo must still open. */
      :host ::ng-deep .dynamic-form--readonly textarea,
      :host ::ng-deep .dynamic-form--readonly img,
      :host ::ng-deep .dynamic-form--readonly a,
      :host ::ng-deep .dynamic-form--readonly .app-media-thumb {
        pointer-events: auto;
      }
      /* Disabled greys the text to the point of unreadability, which defeats a
         screen whose only job is reading the answers. */
      :host ::ng-deep .dynamic-form--readonly .p-disabled,
      :host ::ng-deep .dynamic-form--readonly .p-inputtext:disabled {
        opacity: 1;
        color: var(--p-text-color);
        background: var(--app-surface-alt, transparent);
      }

      /* ── Validation ─────────────────────────────────────────────────── */
      :host ::ng-deep .dynamic-form .p-error {
        display: block;
        margin-top: 0.3rem;
        color: var(--p-red-500, #ef4444);
        font-size: 0.75rem;
        font-weight: 500;
      }
      :host ::ng-deep .dynamic-form .p-error::before {
        content: '\\e989'; /* pi-exclamation-circle */
        font-family: 'primeicons';
        margin-inline-end: 0.3rem;
        font-size: 0.7rem;
      }
      /* Tint the control itself, not just the message. */
      :host ::ng-deep .dynamic-form .ng-invalid.ng-touched .p-inputtext,
      :host ::ng-deep .dynamic-form .p-inputtext.ng-invalid.ng-touched {
        border-color: var(--p-red-400, #f87171);
      }
    `,
  ],
})
export class DynamicFormRendererComponent {
  /**
   * The form-builder schema document (`name_en` / `name_ar` / `elements`), already parsed
   * from JSON. A new object rebuilds the form from scratch.
   */
  readonly definition = input<Record<string, unknown> | null>(null);

  /**
   * The form the answers belong to. Media fields upload each picked file straight away when this
   * is set, so the payload only carries references; leaving it null keeps the form local, which is
   * what the builder's preview wants.
   */
  readonly formId = input<string | null>(null);

  /** The published version being filled, so an upload is checked against the schema in force. */
  readonly versionNo = input<number | null>(null);

  /** What the fill belongs to, e.g. a workflow work item. Both or neither. */
  readonly contextType = input<string | null>(null);
  readonly contextId = input<string | null>(null);

  /**
   * Answers to open the form on, keyed by `data_name`, as the submission store holds them. Used to
   * re-fill a form that has been submitted before, so the respondent edits what is already there
   * instead of retyping it. Keys the definition does not know are ignored.
   */
  readonly answers = input<Record<string, unknown> | null>(null);

  /** Shown in place of the form when the definition has no fields. */
  readonly emptyMessage = input('');

  /**
   * Renders the form for reading only — every control disabled and no validation shown.
   *
   * The point is to show a completed submission exactly as it was filled, including one whose
   * form has since been deprecated. Reconstructing that as a list of label/value pairs would lose the thing
   * being asked for: the grouping, the ordering and the conditional sections are the form, and an
   * answer read outside them can be read wrongly.
   */
  readonly readOnly = input(false);

  /** The respondent pressed Enter / submitted the inner form; the host decides what that means. */
  readonly submitted = output<void>();

  readonly form = signal(new FormGroup({}));
  readonly fields = signal<FormlyFieldConfig[]>([]);
  readonly model = signal<RuleModel>({});
  readonly valueKinds = signal<PreviewValueKinds>({});

  private readonly mapper = inject(FormSchemaFormlyMapper);

  protected readonly formOptions = computed<FormlyFormOptions>(() => ({
    formState: {
      [FORM_STATE_FORM_ID]: this.formId(),
      [FORM_STATE_VERSION_NO]: this.versionNo(),
      [FORM_STATE_CONTEXT_TYPE]: this.contextType(),
      [FORM_STATE_CONTEXT_ID]: this.contextId(),
    },
  }));

  constructor() {
    // All three inputs feed the same build: seeding answers into an already-built model would leave
    // Formly's controls holding the old values, since it binds to the object it was given, and
    // read-only is baked into the field configs rather than applied to the form afterwards.
    effect(() => this.build(this.definition(), this.answers(), this.readOnly()));
  }

  /** JSON-ready answers, keyed by `data_name`. */
  payload(): Record<string, unknown> {
    return toPreviewPayload(this.model(), this.valueKinds());
  }

  /** Marks every control touched so errors show, and reports whether the form may be submitted. */
  validate(): boolean {
    const form = this.form();
    form.markAllAsTouched();
    return form.valid;
  }

  /** How many controls are currently invalid. */
  invalidCount(): number {
    const controls: AbstractControl[] = Object.values(this.form().controls);
    return controls.filter((control) => control.invalid).length;
  }

  /** How many keys the respondent has actually answered. */
  answeredCount(): number {
    return Object.values(this.model()).filter((value) => !isBlank(value)).length;
  }

  /** Discards the current edits and rebuilds from the definition and seed answers. */
  rebuild(): void {
    this.build(this.definition(), this.answers(), this.readOnly());
  }

  /**
   * A fresh `FormGroup` every time — reusing one keeps the previous fill's controls, values and
   * touched state, which would leak one respondent's answers into the next form.
   *
   * Seed answers are layered over the definition's defaults rather than replacing the model, so a
   * field added to the form since the last fill still opens on its default instead of blank.
   */
  private build(
    definition: Record<string, unknown> | null,
    answers: Record<string, unknown> | null,
    readOnly: boolean,
  ): void {
    this.form.set(new FormGroup({}));

    if (!definition) {
      this.fields.set([]);
      this.model.set({});
      this.valueKinds.set({});
      return;
    }

    const preview = this.mapper.toPreview(deserializeSchema(definition));
    this.valueKinds.set(preview.valueKinds);
    this.model.set(
      answers
        ? { ...preview.model, ...fromStoredAnswers(answers, preview.valueKinds) }
        : preview.model,
    );

    if (readOnly) {
      lockFields(preview.fields);
    }

    this.fields.set(preview.fields);
  }
}

/**
 * Disables every field in the tree, in place, and strips the validation that would otherwise paint
 * a finished submission red for rules its form only gained afterwards. Mutating the configs is safe:
 * `toPreview` builds a fresh tree per call, so nothing else holds these objects.
 */
function lockFields(fields: FormlyFieldConfig[]): void {
  for (const field of fields) {
    field.props = { ...field.props, disabled: true, required: false };
    field.validators = undefined;
    field.asyncValidators = undefined;
    field.validation = undefined;

    if (field.fieldGroup) {
      lockFields(field.fieldGroup);
    }
  }
}

function isBlank(value: unknown): boolean {
  if (value === null || value === undefined || value === '') {
    return true;
  }
  return Array.isArray(value) && value.length === 0;
}
