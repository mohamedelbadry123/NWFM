import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnInit,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { FieldType, FieldTypeConfig, FormlyModule } from '@ngx-formly/core';
import { TranslateService } from '@ngx-translate/core';
import { SelectModule } from 'primeng/select';
import { MultiSelectModule } from 'primeng/multiselect';
import { InputTextModule } from 'primeng/inputtext';
import { ChipModule } from 'primeng/chip';
import { CHOICE_OTHER_VALUE, choiceOtherKey, type FormlyChoiceOption } from './formly-preview.types';

/** How many chips stay visible before the rest collapse into a count. */
const DEFAULT_MAX_CHIPS = 4;

const I18N_KEYS = {
  MoreSelected: 'formly.moreSelected',
  TotalSelected: 'formly.totalSelected',
} as const;

/**
 * Formly types `choice` (p-select) and `multichoice` (p-multiselect).
 *
 * Covers the two things `@ngx-formly/primeng` has no answer for:
 * - **Cascade** — options are filtered by the value of `props.parentField`
 *   using each option's `dependencyValue`.
 * - **Allow other** — appends an "Other" option; picking it reveals a free-text
 *   input whose value is stored on the model under `<key>_other`.
 */
@Component({
  selector: 'formly-field-choice',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    FormlyModule,
    SelectModule,
    MultiSelectModule,
    InputTextModule,
    ChipModule,
  ],
  template: `
    @if (multiple) {
      <p-multiselect
        [options]="visibleOptions"
        optionLabel="label"
        optionValue="value"
        display="chip"
        [showClear]="!props.required"
        [placeholder]="props.placeholder ?? ''"
        [formControl]="formControl"
        [formlyAttributes]="field"
        appendTo="body"
        styleClass="w-full"
      >
        <!--
          Replaces PrimeNG's default behaviour, which drops every chip and shows
          only a count once the selection reaches maxSelectedLabels.
        -->
        <ng-template #selecteditems let-selected let-removeChip="removeChip">
          @if (selected?.length) {
            <div class="flex flex-wrap items-center gap-1">
              @for (option of chipsOf(selected); track option.value) {
                <p-chip
                  [label]="option.label"
                  [removable]="!props.disabled"
                  (onRemove)="removeChip(option.value, $event)"
                />
              }
              @if (selected.length > maxChips) {
                <span class="text-xs font-semibold rounded-full px-2 py-1 bg-[var(--p-primary-100)] text-[var(--p-primary-700)]">
                  {{ moreLabel(selected.length) }}
                </span>
              }
              <span class="text-xs text-[var(--p-text-muted-color)] ms-1">{{ totalLabel(selected.length) }}</span>
            </div>
          }
        </ng-template>
      </p-multiselect>
    } @else {
      <p-select
        [options]="visibleOptions"
        optionLabel="label"
        optionValue="value"
        [showClear]="!props.required"
        [placeholder]="props.placeholder ?? ''"
        [formControl]="formControl"
        [formlyAttributes]="field"
        appendTo="body"
        styleClass="w-full"
      />
    }

    @if (showOtherInput) {
      <input
        pInputText
        class="w-full mt-2"
        [ngModel]="otherText"
        [ngModelOptions]="{ standalone: true }"
        (ngModelChange)="setOtherText($event)"
        [disabled]="!!props.disabled"
        [placeholder]="otherPlaceholder"
      />
    }
  `,
})
export class FormlyChoiceComponent extends FieldType<FieldTypeConfig> implements OnInit {
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);

  /** Options currently offered, after cascade filtering and the "Other" entry. */
  protected visibleOptions: FormlyChoiceOption[] = [];
  protected otherText = '';

  protected get multiple(): boolean {
    return !!this.props['multiple'];
  }

  protected get maxChips(): number {
    return (this.props['maxChips'] as number | undefined) ?? DEFAULT_MAX_CHIPS;
  }

  /**
   * The chips that stay visible — the first `maxChips` selected options.
   *
   * PrimeNG hands over raw model values instead of option objects when it has
   * no options to resolve against, so entries are normalized before rendering.
   */
  protected chipsOf(selected: readonly unknown[]): FormlyChoiceOption[] {
    return selected.slice(0, this.maxChips).map((item) => this.asOption(item));
  }

  private asOption(item: unknown): FormlyChoiceOption {
    if (item !== null && typeof item === 'object' && 'value' in item) {
      return item as FormlyChoiceOption;
    }
    const value = String(item);
    return (
      this.visibleOptions.find((option) => option.value === value) ?? {
        value,
        label: value,
        dependencyValue: null,
      }
    );
  }

  protected moreLabel(total: number): string {
    return this.translate.instant(I18N_KEYS.MoreSelected, { count: total - this.maxChips });
  }

  protected totalLabel(total: number): string {
    return this.translate.instant(I18N_KEYS.TotalSelected, { count: total });
  }

  protected get otherPlaceholder(): string {
    return (this.props['otherPlaceholder'] as string | undefined) ?? '';
  }

  protected get showOtherInput(): boolean {
    if (!this.props['allowOther']) {
      return false;
    }
    const value: unknown = this.formControl.value;
    return Array.isArray(value) ? value.includes(CHOICE_OTHER_VALUE) : value === CHOICE_OTHER_VALUE;
  }

  ngOnInit(): void {
    this.otherText = String(this.modelRecord[choiceOtherKey(this.fieldKey)] ?? '');
    this.refreshOptions();

    // Watch the whole form rather than the parent control: the cascade parent
    // may not have a control yet when this field initializes (or may sit later
    // in the form), and re-filtering is cheap.
    if (this.props['parentField']) {
      this.field.form?.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
        this.refreshOptions();
        this.dropUnavailableSelection();
        this.changeDetector.markForCheck();
      });
    }

    this.formControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.dropOtherTextWhenUnused();
      this.changeDetector.markForCheck();
    });
  }

  protected setOtherText(text: string): void {
    this.otherText = text;
    this.modelRecord[choiceOtherKey(this.fieldKey)] = text;
  }

  /**
   * Drop the free text once "Other" is no longer the selection.
   *
   * `setOtherText` writes straight onto the model because the input has no Formly control of its
   * own — which also means `resetOnHide` and `form.reset()` never clear it. Left alone, text typed
   * under "Other" and then abandoned for a real option keeps riding along in every later payload,
   * and the server now has a real column to put it in.
   */
  private dropOtherTextWhenUnused(): void {
    // Guarded on the flag as well as the selection: a field that never offers "Other" owns no
    // companion key, and a real field happening to be named `<key>_other` is not ours to delete.
    if (!this.props['allowOther'] || this.showOtherInput) {
      return;
    }

    this.otherText = '';
    delete this.modelRecord[choiceOtherKey(this.fieldKey)];
  }

  private get fieldKey(): string {
    return String(this.field.key ?? '');
  }

  private get modelRecord(): Record<string, unknown> {
    return (this.model ?? {}) as Record<string, unknown>;
  }

  /** Rebuild `visibleOptions` from the cascade parent's current value. */
  private refreshOptions(): void {
    const options = (this.props.options as FormlyChoiceOption[] | undefined) ?? [];
    const parentField = this.props['parentField'] as string | null | undefined;
    const filtered = parentField
      ? options.filter((option) => this.isAvailable(option, this.modelRecord[parentField]))
      : options;

    this.visibleOptions = this.props['allowOther']
      ? [
          ...filtered,
          {
            value: CHOICE_OTHER_VALUE,
            label: (this.props['otherLabel'] as string | undefined) ?? 'Other',
            dependencyValue: null,
          },
        ]
      : [...filtered];
  }

  private isAvailable(option: FormlyChoiceOption, parentValue: unknown): boolean {
    if (option.dependencyValue === null) {
      return true;
    }
    if (Array.isArray(parentValue)) {
      return parentValue.map((item) => String(item)).includes(option.dependencyValue);
    }
    return parentValue !== null && parentValue !== undefined && String(parentValue) === option.dependencyValue;
  }

  /** Clear the selection when the cascade parent changed and it is no longer offered. */
  private dropUnavailableSelection(): void {
    const available = new Set(this.visibleOptions.map((option) => option.value));
    const value: unknown = this.formControl.value;

    if (Array.isArray(value)) {
      const kept = value.filter((item) => available.has(String(item)));
      if (kept.length !== value.length) {
        this.formControl.setValue(kept);
      }
      return;
    }

    if (value !== null && value !== undefined && value !== '' && !available.has(String(value))) {
      this.formControl.setValue(null);
    }
  }
}
