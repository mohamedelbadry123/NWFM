import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, input, model, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { CheckboxModule } from 'primeng/checkbox';
import { SelectModule } from 'primeng/select';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { MultiSelectModule } from 'primeng/multiselect';
import { DatePickerModule } from 'primeng/datepicker';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LocaleService } from '../../../../core/i18n/locale.service';
import { FieldCatalogService } from '../../../../core/form-engine/field-catalog.service';
import { type FieldCatalogItem } from '../../../../core/form-engine/form-engine.models';
import { FormBuilderStore } from '../store/form-builder.store';
import { ValidationPatternDialogComponent } from './validation-pattern-dialog.component';
import { RulesDialogComponent, type RuleFieldOption } from './rules-dialog.component';
import { DefaultValueEditorComponent } from './default-value-editor.component';
import { parseLocalDate, toLocalDate } from '../../../../shared/form-schema/form-schema-payload';
import {
  ACTION_TAKEN_DATA_NAME,
  BARCODE_FORMATS,
  C2M_FA_STATUSES,
  BARCODE_FORMAT_LABELS,
  DATA_NAME_PATTERN,
  DATE_RULES,
  DEFAULT_ALLOWED_EXTENSIONS,
  DEFAULT_BARCODE_FORMATS,
  DEFAULT_VALUE_MODES,
  ELEMENT_TYPES,
  NUMERIC_FORMATS,
  RULE_MODES,
  SECTION_DISPLAYS,
  isAttachmentType,
  isChoiceType,
  isDateRuleType,
  isMediaType,
  isTextLikeType,
  localizedDisplay,
  normalizeExtensions,
  type BarcodeFormat,
  type Choice,
  type DateRule,
  type DefaultValueMode,
  type FormElement,
  type RuleGroup,
  type ValidationPattern,
} from '../../../../shared/form-schema/form-schema.types';

@Component({
  selector: 'app-field-editor-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    InputNumberModule,
    TextareaModule,
    CheckboxModule,
    SelectModule,
    AutoCompleteModule,
    MultiSelectModule,
    DatePickerModule,
    TranslateModule,
    ValidationPatternDialogComponent,
    RulesDialogComponent,
    DefaultValueEditorComponent,
  ],
  templateUrl: './field-editor-dialog.component.html',
})
export class FieldEditorDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(FormBuilderStore);
  private readonly catalog = inject(FieldCatalogService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);
  private readonly activeLang = this.locale.locale;

  readonly visible = model(false);
  readonly elementKey = input<string | null>(null);

  /** Field-catalog autocomplete state for the Data Name input. */
  protected readonly catalogSuggestions = signal<FieldCatalogItem[]>([]);
  private readonly knownCatalog = signal<ReadonlyMap<string, FieldCatalogItem>>(new Map());
  private readonly dataNameValue = signal('');

  /** The catalog entry (if any) matching the currently typed data name. */
  private readonly dataNameMatch = computed(() =>
    this.knownCatalog().get(this.dataNameValue().trim().toLowerCase()),
  );

  /** A non-empty data name that is not yet in the catalog — a column is created on publish. */
  protected readonly dataNameIsNew = computed(
    () => this.dataNameValue().trim().length > 0 && !this.dataNameMatch(),
  );

  /** The typed data name exists in the catalog under a different field type. */
  protected readonly dataNameTypeMismatch = computed(() => {
    const match = this.dataNameMatch();
    const element = this.element();
    return !!match && !!element && match.fieldType !== element.type;
  });

  /**
   * The typed data name could never become a SQL column — a space or dash inside it, a leading
   * digit. Blocked here because the server drops such an answer silently: the fill succeeds and the
   * column stays NULL. A trailing space is not flagged, since {@link save} trims it away.
   */
  protected readonly dataNameInvalid = computed(() => {
    const value = this.dataNameValue().trim();
    return value.length > 0 && !DATA_NAME_PATTERN.test(value);
  });

  /** The date constraint as it is being edited, mirrored out of the form so computeds can see it. */
  private readonly dateConstraint = signal<{
    rule: DateRule;
    min: Date | null;
    max: Date | null;
  }>({ rule: DATE_RULES.None, min: null, max: null });

  /** A window that can never contain a date. Blocks save, like an unusable data name does. */
  protected readonly dateBoundsInvalid = computed(() => {
    const { min, max } = this.dateConstraint();
    return !!min && !!max && min.getTime() > max.getTime();
  });

  /**
   * A fixed default that its own field would reject. Only a warning: the author may be mid-edit,
   * and the fill form reports it anyway — but saying so here is the whole point of the feature.
   */
  protected readonly defaultOutOfRange = computed(() => {
    if (!this.isDateRuleField() || this.defaultValueMode() !== DEFAULT_VALUE_MODES.Fixed) {
      return false;
    }

    const raw = this.defaultValue();
    if (!raw) {
      return false;
    }

    // A `date_time` default is `YYYY-MM-DD HH:mm`; only the calendar half is compared, because
    // that is the granularity the bounds and the today-relative rules are expressed in.
    const candidate = parseLocalDate(raw.split(' ')[0]);
    if (!candidate) {
      return false;
    }

    const { rule, min, max } = this.dateConstraint();
    const day = startOfDay(candidate).getTime();
    const today = startOfDay(new Date()).getTime();

    if (rule === DATE_RULES.After && day <= today) return true;
    if (rule === DATE_RULES.OnOrAfter && day < today) return true;
    if (rule === DATE_RULES.Before && day >= today) return true;
    if (rule === DATE_RULES.OnOrBefore && day > today) return true;

    return (!!min && day < startOfDay(min).getTime()) || (!!max && day > startOfDay(max).getTime());
  });

  protected readonly visibilityMode = RULE_MODES.Visibility;
  protected readonly requirementMode = RULE_MODES.Requirement;

  protected readonly element = signal<FormElement | null>(null);
  protected readonly form = signal<FormGroup | null>(null);

  // sub-dialog state
  protected readonly patternDialogVisible = model(false);
  protected readonly visibilityDialogVisible = model(false);
  protected readonly requirementDialogVisible = model(false);
  protected readonly pattern = signal<ValidationPattern | null>(null);
  protected readonly visibleConditions = signal<RuleGroup | null>(null);
  protected readonly requiredConditions = signal<RuleGroup | null>(null);

  /**
   * The default lives outside the form group, alongside {@link pattern} and the two rule groups —
   * it is edited by its own component and read back in {@link save}.
   */
  protected readonly defaultValue = signal<string | null>(null);
  protected readonly defaultValueMode = signal<DefaultValueMode>(DEFAULT_VALUE_MODES.Fixed);

  /**
   * The choices as they are being edited, not as they were loaded — a default picked from the list
   * has to offer the option the author just added without closing the dialog first.
   */
  protected readonly liveChoices = signal<readonly Choice[]>([]);

  protected readonly formatOptions = [
    { label: 'Decimal', value: NUMERIC_FORMATS.Decimal },
    { label: 'Integer', value: NUMERIC_FORMATS.Integer },
  ];
  protected readonly displayOptions = [
    { label: 'Inline', value: SECTION_DISPLAYS.Inline },
    { label: 'Page', value: SECTION_DISPLAYS.Page },
  ];

  /** Every symbology the barcode picker offers, in declaration order. */
  protected readonly barcodeFormatOptions = Object.values(BARCODE_FORMATS).map((value) => ({
    label: BARCODE_FORMAT_LABELS[value],
    value,
  }));

  /**
   * The rule options. The stored values are shared by both types, but the wording is not: a `date`
   * is measured against *today* and a `date_time` against *now*, so each reads in its own terms
   * rather than in one compromise phrasing that is wrong for both.
   */
  protected readonly dateRuleOptions = computed(() => {
    const prefix =
      this.element()?.type === ELEMENT_TYPES.DateTime
        ? 'formBuilder.editor.dateTimeRules'
        : 'formBuilder.editor.dateRules';

    return [
      { value: DATE_RULES.None, key: 'none' },
      { value: DATE_RULES.After, key: 'after' },
      { value: DATE_RULES.OnOrAfter, key: 'onOrAfter' },
      { value: DATE_RULES.Before, key: 'before' },
      { value: DATE_RULES.OnOrBefore, key: 'onOrBefore' },
    ].map((option) => ({
      value: option.value,
      label: this.translate.instant(`${prefix}.${option.key}`),
    }));
  });

  protected readonly isText = computed(() => {
    const type = this.element()?.type;
    return type ? isTextLikeType(type) : false;
  });
  protected readonly isNumeric = computed(() => this.element()?.type === ELEMENT_TYPES.Numeric);
  protected readonly isSection = computed(() => this.element()?.type === ELEMENT_TYPES.Section);
  protected readonly isGeolocation = computed(() => this.element()?.type === ELEMENT_TYPES.Geolocation);
  protected readonly isBarcode = computed(() => this.element()?.type === ELEMENT_TYPES.Barcode);
  protected readonly isCalendarWithHours = computed(
    () => this.element()?.type === ELEMENT_TYPES.CalendarWithHours,
  );
  /** `date` / `date_time` — the types that can be constrained against the system clock. */
  protected readonly isDateRuleField = computed(() => {
    const el = this.element();
    return el ? isDateRuleType(el.type) : false;
  });
  protected readonly isChoice = computed(() => {
    const el = this.element();
    return el ? isChoiceType(el.type) : false;
  });
  protected readonly isMedia = computed(() => {
    const el = this.element();
    return el ? isMediaType(el.type) : false;
  });
  /** Photo / video / audio — the media types that carry file limits. */
  protected readonly isAttachment = computed(() => {
    const el = this.element();
    return el ? isAttachmentType(el.type) : false;
  });

  /** Extra C2M outcome columns — only the Action Taken field's options close a field activity. */
  protected readonly isActionTakenField = computed(
    () => this.dataNameValue().trim() === ACTION_TAKEN_DATA_NAME,
  );

  protected readonly c2mFaStatusOptions = computed(() => {
    this.activeLang();
    return [
      { value: C2M_FA_STATUSES.Completed, label: this.translate.instant('formBuilder.editor.c2mFaStatusCompleted') },
      { value: C2M_FA_STATUSES.Cancelled, label: this.translate.instant('formBuilder.editor.c2mFaStatusCancelled') },
    ];
  });

  /** Other choice fields available as a cascading parent. */
  protected readonly parentFieldOptions = computed<RuleFieldOption[]>(() =>
    this.store
      .choiceFields()
      .filter((el) => el.key !== this.elementKey())
      .map((el) => ({
        data_name: el.data_name,
        label: localizedDisplay(el.label_en, el.label_ar, this.activeLang(), el.data_name),
      })),
  );

  /** Fields available to reference in visibility/requirement rules. */
  protected readonly ruleFieldOptions = computed<RuleFieldOption[]>(() =>
    this.store
      .allElements()
      .filter((el) => el.key !== this.elementKey() && el.type !== ELEMENT_TYPES.Section)
      .map((el) => ({
        data_name: el.data_name,
        label: localizedDisplay(el.label_en, el.label_ar, this.activeLang(), el.data_name),
      })),
  );

  constructor() {
    effect(() => {
      const key = this.elementKey();
      if (this.visible() && key) {
        this.load(key);
      }
    });
  }

  protected get choices(): FormArray {
    return this.form()?.get('choices') as FormArray;
  }

  /** Options for a choice row's dependency_value select, based on the chosen parent field. */
  protected parentChoiceOptions(): { label: string; value: string }[] {
    const parent = this.form()?.get('parent_field')?.value as string | null;
    if (!parent) {
      return [];
    }
    const parentField = this.store.choiceFields().find((el) => el.data_name === parent);
    return (parentField?.choices ?? []).map((choice) => ({
      label: localizedDisplay(choice.label_en, choice.label_ar, this.activeLang(), choice.value),
      value: choice.value,
    }));
  }

  /** Restore the default extension list for this element's media kind. */
  protected resetExtensions(): void {
    const type = this.element()?.type;
    this.form()
      ?.get('allowed_extensions')
      ?.setValue([...(type ? (DEFAULT_ALLOWED_EXTENSIONS[type] ?? []) : [])]);
  }

  /** An empty list means "accept any file of this media kind". */
  protected clearExtensions(): void {
    this.form()?.get('allowed_extensions')?.setValue([]);
  }

  /** Restore QR plus the common 1D codes. */
  protected resetBarcodeFormats(): void {
    this.form()?.get('barcode_formats')?.setValue([...DEFAULT_BARCODE_FORMATS]);
  }

  /** Debounced-ish lookup against the global field catalog for autocomplete suggestions. */
  protected searchCatalog(event: { query: string }): void {
    this.catalog.search(event.query, 20).subscribe({
      next: (result) => {
        const items = result.value ?? [];
        this.catalogSuggestions.set(items);
        this.knownCatalog.update((current) => {
          const next = new Map(current);
          for (const item of items) {
            if (item.dataName) {
              next.set(item.dataName.toLowerCase(), item);
            }
          }
          return next;
        });
      },
      error: () => this.catalogSuggestions.set([]),
    });
  }

  /** Selecting an existing catalog entry sets the data name and prefills empty labels. */
  protected onCatalogSelect(event: { value: FieldCatalogItem }): void {
    const item = event.value;
    const form = this.form();
    if (!form || !item?.dataName) {
      return;
    }
    form.get('data_name')?.setValue(item.dataName);
    const labelEn = form.get('label_en');
    if (item.labelEn && !labelEn?.value) {
      labelEn?.setValue(item.labelEn);
    }
    const labelAr = form.get('label_ar');
    if (item.labelAr && !labelAr?.value) {
      labelAr?.setValue(item.labelAr);
    }
  }

  protected addChoice(): void {
    this.choices.push(
      this.fb.group({
        value: [`option_${this.choices.length + 1}`],
        label_en: [''],
        label_ar: [''],
        dependency_value: [null as string | null],
        c2m_fa_status: [null as string | null],
        c2m_reason: [null as string | null],
      }),
    );
  }

  protected removeChoice(index: number): void {
    this.choices.removeAt(index);
  }

  protected onPatternChange(pattern: ValidationPattern | null): void {
    this.pattern.set(pattern);
  }

  protected clearPattern(): void {
    this.pattern.set(null);
  }

  protected onVisibilityChange(group: RuleGroup): void {
    this.visibleConditions.set(group);
  }

  protected onRequirementChange(group: RuleGroup): void {
    this.requiredConditions.set(group);
  }

  protected save(): void {
    const form = this.form();
    const key = this.elementKey();
    if (!form || !key || this.dataNameInvalid() || this.dateBoundsInvalid()) {
      return;
    }
    const raw = form.getRawValue();
    const patch: Partial<FormElement> = {
      label_en: raw.label_en,
      label_ar: raw.label_ar,
      // Trimmed because the server trims it too: a stray space here would leave the payload keyed
      // on 'leak_type ' while the column it must land in is 'leak_type'.
      data_name: typeof raw.data_name === 'string' ? raw.data_name.trim() : raw.data_name,
      c2m_parameter_name:
        typeof raw.c2m_parameter_name === 'string' ? raw.c2m_parameter_name.trim() || null : null,
      description_en: raw.description_en,
      description_ar: raw.description_ar,
      default_value: this.defaultValue(),
      default_value_mode: this.defaultValueMode(),
      required: !!raw.required,
      hidden: !!raw.hidden,
      disabled: !!raw.disabled,
      min_length: raw.min_length ?? null,
      max_length: raw.max_length ?? null,
      pattern: this.pattern(),
      format: raw.format ?? null,
      min: raw.min ?? null,
      max: raw.max ?? null,
      date_rule: raw.date_rule ?? null,
      // Held as a `Date` by the picker, stored as the `YYYY-MM-DD` the definition carries.
      min_date: asStoredDate(raw.min_date),
      max_date: asStoredDate(raw.max_date),
      allow_other: !!raw.allow_other,
      parent_field: raw.parent_field || null,
      choices: ((raw.choices as Choice[] | null) ?? []).map((choice) => ({
        ...choice,
        c2m_fa_status: typeof choice.c2m_fa_status === 'string' ? choice.c2m_fa_status.trim() || null : null,
        c2m_reason: typeof choice.c2m_reason === 'string' ? choice.c2m_reason.trim() || null : null,
      })),
      max_files: raw.max_files ?? null,
      max_file_size_mb: raw.max_file_size_mb ?? null,
      // Typed free-hand, so normalize `.JPG` / ` jpg ` before storing.
      allowed_extensions: normalizeExtensions(raw.allowed_extensions ?? []),
      barcode_formats: (raw.barcode_formats ?? []) as BarcodeFormat[],
      map_zoom: raw.map_zoom ?? null,
      display: raw.display ?? null,
      visible_conditions: this.visibleConditions() ?? this.element()!.visible_conditions,
      required_conditions: this.requiredConditions() ?? this.element()!.required_conditions,
    };
    this.store.update(key, patch);
    this.visible.set(false);
  }

  protected close(): void {
    this.visible.set(false);
  }

  private load(key: string): void {
    const element = this.store.find(key);
    if (!element) {
      return;
    }
    this.element.set(element);
    this.pattern.set(element.pattern ? { ...element.pattern } : null);
    this.visibleConditions.set(structuredClone(element.visible_conditions));
    this.requiredConditions.set(structuredClone(element.required_conditions));
    this.defaultValue.set(element.default_value);
    this.defaultValueMode.set(element.default_value_mode);
    this.liveChoices.set(element.choices.map((choice) => ({ ...choice })));
    this.dateConstraint.set({
      rule: element.date_rule ?? DATE_RULES.None,
      min: element.min_date ? (parseLocalDate(element.min_date) ?? null) : null,
      max: element.max_date ? (parseLocalDate(element.max_date) ?? null) : null,
    });

    this.form.set(
      this.fb.group({
        label_en: [element.label_en],
        label_ar: [element.label_ar],
        data_name: [element.data_name],
        c2m_parameter_name: [element.c2m_parameter_name ?? null],
        description_en: [element.description_en],
        description_ar: [element.description_ar],
        default_value: [element.default_value],
        required: [element.required],
        hidden: [element.hidden],
        disabled: [element.disabled],
        min_length: [element.min_length],
        max_length: [element.max_length],
        format: [element.format],
        min: [element.min],
        max: [element.max],
        allow_other: [element.allow_other],
        parent_field: [element.parent_field],
        max_files: [element.max_files],
        max_file_size_mb: [element.max_file_size_mb],
        allowed_extensions: [[...element.allowed_extensions]],
        barcode_formats: [[...element.barcode_formats]],
        map_zoom: [element.map_zoom],
        display: [element.display],
        date_rule: [element.date_rule ?? DATE_RULES.None],
        min_date: [element.min_date ? (parseLocalDate(element.min_date) ?? null) : null],
        max_date: [element.max_date ? (parseLocalDate(element.max_date) ?? null) : null],
        choices: this.fb.array(
          element.choices.map((choice) =>
            this.fb.group({
              value: [choice.value],
              label_en: [choice.label_en],
              label_ar: [choice.label_ar],
              dependency_value: [choice.dependency_value],
              c2m_fa_status: [choice.c2m_fa_status ?? null],
              c2m_reason: [choice.c2m_reason ?? null],
            }),
          ),
        ),
      }),
    );

    const form = this.form()!;

    const dataNameControl = form.get('data_name');
    this.dataNameValue.set(element.data_name ?? '');
    dataNameControl?.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      const asString =
        typeof value === 'string' ? value : ((value as FieldCatalogItem | null)?.dataName ?? '');
      this.dataNameValue.set(asString);
    });

    // The date controls and the choice rows feed computeds (the bounds check, the warning on a
    // default its own field would reject, the default's option list), so their values are mirrored
    // out of the form as they are edited.
    form
      .valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const raw = form.getRawValue();
        this.dateConstraint.set({
          rule: (raw.date_rule as DateRule | null) ?? DATE_RULES.None,
          min: (raw.min_date as Date | null) ?? null,
          max: (raw.max_date as Date | null) ?? null,
        });
        this.liveChoices.set((raw.choices as Choice[] | null) ?? []);
      });

    this.searchCatalog({ query: element.data_name ?? '' });
  }
}

/** A picker's `Date` as the `YYYY-MM-DD` the definition stores, or null when nothing is picked. */
function asStoredDate(value: unknown): string | null {
  return value instanceof Date && !Number.isNaN(value.getTime()) ? toLocalDate(value) : null;
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}
