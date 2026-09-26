import { Injectable, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { LocaleService } from '../../core/i18n/locale.service';
import type { FormlyFieldConfig } from '@ngx-formly/core';
import {
  DATE_GRANULARITIES,
  DATE_RULES,
  DATE_TIME_SEPARATOR,
  FORMLY_TYPES,
  FORMLY_VALIDATORS,
  FORMLY_WRAPPERS,
  MEDIA_KINDS,
  type DateGranularity,
  type FormlyChoiceOption,
  type MediaKind,
} from '../components/dynamic-form/formly-preview.types';
import {
  BARCODE_FORMAT_LABELS,
  BARCODE_MAX_LENGTH,
  DEFAULT_VALUE_MODES,
  ELEMENT_TYPES,
  NUMERIC_FORMATS,
  SECTION_DISPLAYS,
  isMediaType,
  localizedDisplay,
  normalizeExtensions,
  type ElementType,
  type FormSchema,
  type FormElement,
} from './form-schema.types';
import { SAUDI_ARABIA_ZOOM } from '../components/geo-map/geo-map.component';
import { buildRulePredicate, type RuleModel } from './form-schema-rules';
import {
  PREVIEW_VALUE_KINDS,
  parseLocalDate,
  toLocalDate,
  toLocalTime,
  type PreviewValueKind,
} from './form-schema-payload';
import { completeTime } from '../components/dynamic-form/time-picker.component';
import { normalizeDateTime } from '../components/dynamic-form/formly-date-time.component';

/** Values stored by a `yes_no` element — kept as strings so rules can match `equal 'yes'`. */
export const YES_NO_VALUES = {
  Yes: 'yes',
  No: 'no',
} as const;

const I18N_KEYS = {
  Yes: 'formBuilder.preview.yes',
  No: 'formBuilder.preview.no',
  Other: 'formBuilder.preview.other',
  OtherPlaceholder: 'formBuilder.preview.otherPlaceholder',
  SelectPlaceholder: 'formBuilder.preview.selectPlaceholder',
  BarcodePlaceholder: 'formly.barcode.placeholder',
} as const;

/**
 * How many section tints `styles.scss` defines (`--sec-1-*` … `--sec-6-*`).
 * Sections cycle through them in document order so neighbours never share a
 * tone, which is what lets a respondent see where a section ends.
 */
const SECTION_TONE_COUNT = 6;

/** PrimeNG date mask (`yy` = 4-digit year). PrimeNG defaults to US `mm/dd/yy`. */
const DATE_FORMAT = 'dd/mm/yy';

/** Fallback when a media element carries no explicit file limit. */
const DEFAULT_MAX_FILES = 1;

/** Element type -> Formly type name. */
const FORMLY_TYPE_BY_ELEMENT: Readonly<Record<Exclude<ElementType, 'section'>, string>> = {
  [ELEMENT_TYPES.Text]: FORMLY_TYPES.Input,
  [ELEMENT_TYPES.Memo]: FORMLY_TYPES.Textarea,
  [ELEMENT_TYPES.Numeric]: FORMLY_TYPES.Input,
  [ELEMENT_TYPES.YesNo]: FORMLY_TYPES.Radio,
  [ELEMENT_TYPES.Date]: FORMLY_TYPES.Date,
  [ELEMENT_TYPES.Time]: FORMLY_TYPES.Time,
  [ELEMENT_TYPES.CalendarWithHours]: FORMLY_TYPES.CalendarWithHours,
  [ELEMENT_TYPES.DateTime]: FORMLY_TYPES.DateTime,
  [ELEMENT_TYPES.SingleChoice]: FORMLY_TYPES.Choice,
  [ELEMENT_TYPES.MultipleChoice]: FORMLY_TYPES.MultiChoice,
  [ELEMENT_TYPES.Signature]: FORMLY_TYPES.Signature,
  [ELEMENT_TYPES.Photo]: FORMLY_TYPES.Attachment,
  [ELEMENT_TYPES.Video]: FORMLY_TYPES.Attachment,
  [ELEMENT_TYPES.Audio]: FORMLY_TYPES.Attachment,
  [ELEMENT_TYPES.File]: FORMLY_TYPES.Attachment,
  [ELEMENT_TYPES.Barcode]: FORMLY_TYPES.Barcode,
  [ELEMENT_TYPES.Geolocation]: FORMLY_TYPES.Geolocation,
};

/** Which capture mode the shared attachment component runs in. */
const MEDIA_KIND_BY_ELEMENT: Partial<Record<ElementType, MediaKind>> = {
  [ELEMENT_TYPES.Photo]: MEDIA_KINDS.Photo,
  [ELEMENT_TYPES.Video]: MEDIA_KINDS.Video,
  [ELEMENT_TYPES.Audio]: MEDIA_KINDS.Audio,
  [ELEMENT_TYPES.File]: MEDIA_KINDS.File,
};

/** `accept` used when an element allows every extension of its kind. */
const WILDCARD_ACCEPT_BY_ELEMENT: Partial<Record<ElementType, string>> = {
  [ELEMENT_TYPES.Photo]: 'image/*',
  [ELEMENT_TYPES.Video]: 'video/*',
  [ELEMENT_TYPES.Audio]: 'audio/*',
  // A document field with no extension list really does take anything.
  [ELEMENT_TYPES.File]: '*/*',
};

/** Element types whose control holds a `Date` and needs local formatting in the payload. */
const VALUE_KIND_BY_ELEMENT: Partial<Record<ElementType, PreviewValueKind>> = {
  [ELEMENT_TYPES.Date]: PREVIEW_VALUE_KINDS.Date,
  [ELEMENT_TYPES.Time]: PREVIEW_VALUE_KINDS.Time,
  [ELEMENT_TYPES.CalendarWithHours]: PREVIEW_VALUE_KINDS.CalendarWithHours,
  [ELEMENT_TYPES.DateTime]: PREVIEW_VALUE_KINDS.DateTime,
  [ELEMENT_TYPES.Signature]: PREVIEW_VALUE_KINDS.Signature,
  [ELEMENT_TYPES.Photo]: PREVIEW_VALUE_KINDS.Files,
  [ELEMENT_TYPES.Video]: PREVIEW_VALUE_KINDS.Files,
  [ELEMENT_TYPES.Audio]: PREVIEW_VALUE_KINDS.Files,
  [ELEMENT_TYPES.File]: PREVIEW_VALUE_KINDS.Files,
  [ELEMENT_TYPES.Geolocation]: PREVIEW_VALUE_KINDS.Geo,
  [ELEMENT_TYPES.YesNo]: PREVIEW_VALUE_KINDS.YesNo,
};

const SECTION_LAYOUT = {
  Inline: 'grid grid-cols-1 md:grid-cols-2 gap-4',
  Page: 'flex flex-col gap-4',
} as const;

/**
 * When a form has top-level geolocation fields the preview splits in two: the
 * ordinary fields on one side, the map(s) pinned on the other.
 */
const SPLIT_LAYOUT = {
  Row: 'grid grid-cols-1 lg:grid-cols-[1fr_minmax(320px,420px)] gap-6 items-start',
  Main: 'flex flex-col gap-4',
  Side: 'flex flex-col gap-4 lg:sticky lg:top-2',
} as const;

/** `"24.7136, 46.6753"` -> `{ lat, lng }`; anything else -> undefined. */
function parseCoordinate(raw: string): { lat: number; lng: number } | undefined {
  const [lat, lng] = raw.split(',').map((part) => Number(part.trim()));
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
    return undefined;
  }
  return { lat, lng };
}

type FieldProps = NonNullable<FormlyFieldConfig['props']>;

export interface FormlyPreview {
  readonly fields: FormlyFieldConfig[];
  /** Flat model seeded with the elements' default values. */
  readonly model: RuleModel;
  /** `data_name` -> value kind, for keys the payload has to format itself. */
  readonly valueKinds: Record<string, PreviewValueKind>;
}

/**
 * Translates the builder's Fulcrum-style definition into a runnable ngx-formly
 * configuration.
 *
 * The model stays **flat** — sections become keyless `fieldGroup`s — so a rule
 * on any `data_name` resolves regardless of nesting.
 */
@Injectable({ providedIn: 'root' })
export class FormSchemaFormlyMapper {
  private readonly translate = inject(TranslateService);
  private readonly locale = inject(LocaleService);

  /** @param lang Overrides the active Transloco language when supplied. */
  toPreview(definition: FormSchema, lang?: string): FormlyPreview {
    const language = lang ?? this.locale.locale();
    const preview: FormlyPreview = { fields: [], model: {}, valueKinds: {} };
    const tones = new Map<string, number>();
    this.assignSectionTones(definition.elements, tones);
    const fields = definition.elements.map((element) =>
      this.mapElement(element, language, preview, tones),
    );
    return { ...preview, fields: this.withMapLayout(definition.elements, fields) };
  }

  /**
   * Walks the definition in document order and gives each section the next
   * tone in the cycle. Pre-computed rather than counted during the mapping so
   * the mapper stays free of the ordering state a recursive walk would need.
   */
  private assignSectionTones(elements: readonly FormElement[], tones: Map<string, number>): void {
    for (const element of elements) {
      if (element.type !== ELEMENT_TYPES.Section) {
        continue;
      }

      tones.set(element.key, (tones.size % SECTION_TONE_COUNT) + 1);
      this.assignSectionTones(element.elements, tones);
    }
  }

  /**
   * Move top-level geolocation fields into a side column so the map sits beside
   * the rest of the form instead of interrupting it.
   *
   * Only *top-level* maps are relocated — one nested in a section stays where
   * the author put it, because a section is a deliberate grouping.
   */
  private withMapLayout(
    elements: readonly FormElement[],
    fields: FormlyFieldConfig[],
  ): FormlyFieldConfig[] {
    const isMap = (index: number) => elements[index]?.type === ELEMENT_TYPES.Geolocation;
    const side = fields.filter((_, index) => isMap(index));
    if (side.length === 0) {
      return fields;
    }

    const main = fields.filter((_, index) => !isMap(index));
    return [
      {
        fieldGroupClassName: SPLIT_LAYOUT.Row,
        fieldGroup: [
          { fieldGroupClassName: SPLIT_LAYOUT.Main, fieldGroup: main },
          { fieldGroupClassName: SPLIT_LAYOUT.Side, fieldGroup: side },
        ],
      },
    ];
  }

  private mapElement(
    element: FormElement,
    lang: string,
    preview: FormlyPreview,
    tones: ReadonlyMap<string, number>,
  ): FormlyFieldConfig {
    return element.type === ELEMENT_TYPES.Section
      ? this.mapSection(element, lang, preview, tones)
      : this.mapField(element, lang, preview);
  }

  private mapSection(
    element: FormElement,
    lang: string,
    preview: FormlyPreview,
    tones: ReadonlyMap<string, number>,
  ): FormlyFieldConfig {
    const field: FormlyFieldConfig = {
      // Keyless on purpose: keeps the model flat for cross-field conditions.
      wrappers: [FORMLY_WRAPPERS.SectionPanel],
      props: {
        label: localizedDisplay(element.label_en, element.label_ar, lang),
        description: localizedDisplay(element.description_en, element.description_ar, lang),
        tone: tones.get(element.key) ?? 1,
      },
      fieldGroupClassName:
        element.display === SECTION_DISPLAYS.Page ? SECTION_LAYOUT.Page : SECTION_LAYOUT.Inline,
      fieldGroup: element.elements.map((child) => this.mapElement(child, lang, preview, tones)),
    };

    this.applyVisibility(field, element);
    return field;
  }

  private mapField(element: FormElement, lang: string, preview: FormlyPreview): FormlyFieldConfig {
    const props: FieldProps = {
      label: localizedDisplay(element.label_en, element.label_ar, lang),
      description: localizedDisplay(element.description_en, element.description_ar, lang),
      required: element.required,
      disabled: element.disabled,
    };

    const field: FormlyFieldConfig = {
      key: element.data_name,
      type: FORMLY_TYPE_BY_ELEMENT[element.type as Exclude<ElementType, 'section'>],
      wrappers: [FORMLY_WRAPPERS.FormField, FORMLY_WRAPPERS.FieldHelp],
      props,
    };

    this.applyTypeSpecifics(field, props, element, lang);

    const valueKind = VALUE_KIND_BY_ELEMENT[element.type];
    if (valueKind) {
      preview.valueKinds[element.data_name] = valueKind;
    }

    const defaultValue = this.defaultValue(element);
    if (defaultValue !== undefined) {
      field.defaultValue = defaultValue;
      preview.model[element.data_name] = defaultValue;
    }

    this.applyVisibility(field, element);
    this.applyRequirement(field, element);
    return field;
  }

  private applyTypeSpecifics(
    field: FormlyFieldConfig,
    props: FieldProps,
    element: FormElement,
    lang: string,
  ): void {
    switch (element.type) {
      case ELEMENT_TYPES.Text:
      case ELEMENT_TYPES.Memo: {
        if (element.type === ELEMENT_TYPES.Memo) {
          props.rows = 4;
        }
        if (element.min_length !== null) {
          props.minLength = element.min_length;
        }
        if (element.max_length !== null) {
          props.maxLength = element.max_length;
        }
        const pattern = element.pattern;
        if (pattern?.regex) {
          props.pattern = pattern.regex;
          if (pattern.description) {
            // The author already described the rule — use it verbatim.
            field.validation = { messages: { pattern: () => pattern.description } };
          }
        }
        break;
      }

      case ELEMENT_TYPES.Numeric: {
        props.type = 'number';
        if (element.min !== null) {
          props.min = element.min;
        }
        if (element.max !== null) {
          props.max = element.max;
        }
        if (element.format === NUMERIC_FORMATS.Integer) {
          field.validators = { validation: [FORMLY_VALIDATORS.Integer] };
        }
        break;
      }

      case ELEMENT_TYPES.Date: {
        props['dateFormat'] = DATE_FORMAT;
        props['showIcon'] = true;
        props['showClear'] = !element.required;
        this.applyDateRule(field, props, element, DATE_GRANULARITIES.Day);
        break;
      }

      case ELEMENT_TYPES.DateTime: {
        this.applyDateRule(field, props, element, DATE_GRANULARITIES.Minute);
        break;
      }

      case ELEMENT_TYPES.CalendarWithHours: {
        // Blocks a submit where a ticked day carries only one of its two ends.
        field.validators = { validation: [FORMLY_VALIDATORS.CalendarWithHoursRange] };
        break;
      }

      case ELEMENT_TYPES.Photo:
      case ELEMENT_TYPES.Video:
      case ELEMENT_TYPES.Audio:
      case ELEMENT_TYPES.File: {
        const extensions = normalizeExtensions(element.allowed_extensions ?? []);
        props['mediaKind'] = MEDIA_KIND_BY_ELEMENT[element.type];
        props['maxFiles'] = element.max_files ?? DEFAULT_MAX_FILES;
        props['maxFileSizeMb'] = element.max_file_size_mb;
        props['allowedExtensions'] = extensions;
        // An empty list means "anything of this kind" — fall back to the wildcard.
        props['accept'] =
          extensions.length > 0
            ? extensions.map((extension) => `.${extension}`).join(',')
            : WILDCARD_ACCEPT_BY_ELEMENT[element.type];
        break;
      }

      case ELEMENT_TYPES.Barcode: {
        props.placeholder = this.translate.instant(I18N_KEYS.BarcodePlaceholder);
        // Caps the answer at what the NVARCHAR(400) column holds, as a formly validator.
        props.maxLength = BARCODE_MAX_LENGTH;
        // The scanner itself is a mobile concern (it reads `barcode_formats` from the definition);
        // the web control only names the accepted symbologies under the input.
        props['formatsLabel'] = (element.barcode_formats ?? [])
          .map((format) => BARCODE_FORMAT_LABELS[format] ?? format)
          .join(', ');
        break;
      }

      case ELEMENT_TYPES.Geolocation: {
        props['mapZoom'] = element.map_zoom ?? SAUDI_ARABIA_ZOOM;
        break;
      }

      case ELEMENT_TYPES.YesNo: {
        props.options = [
          { value: YES_NO_VALUES.Yes, label: this.translate.instant(I18N_KEYS.Yes) },
          { value: YES_NO_VALUES.No, label: this.translate.instant(I18N_KEYS.No) },
        ];
        break;
      }

      case ELEMENT_TYPES.SingleChoice:
      case ELEMENT_TYPES.MultipleChoice: {
        props.placeholder = this.translate.instant(I18N_KEYS.SelectPlaceholder);
        props['multiple'] = element.type === ELEMENT_TYPES.MultipleChoice;
        props['allowOther'] = element.allow_other;
        props['parentField'] = element.parent_field;
        props['otherLabel'] = this.translate.instant(I18N_KEYS.Other);
        props['otherPlaceholder'] = this.translate.instant(I18N_KEYS.OtherPlaceholder);
        props.options = element.choices.map<FormlyChoiceOption>((choice) => ({
          value: choice.value,
          label: localizedDisplay(choice.label_en, choice.label_ar, lang),
          dependencyValue: choice.dependency_value,
        }));
        break;
      }

      default:
        break;
    }
  }

  /**
   * `date_rule` + `min_date` / `max_date` -> the calendar's bounds and the `dateRule` validator.
   *
   * The bounds are resolved **here**, per render, so a rule stated against "today" moves with the
   * day instead of freezing at design time. They narrow the calendar to the days the rule allows;
   * the validator still runs, because the calendar cannot speak for a typed date, a value restored
   * from an earlier fill, or the time half of a `date_time` on an otherwise legal day.
   */
  private applyDateRule(
    field: FormlyFieldConfig,
    props: FieldProps,
    element: FormElement,
    granularity: DateGranularity,
  ): void {
    const rule = element.date_rule ?? DATE_RULES.None;
    const fixedMin = element.min_date ? parseLocalDate(element.min_date) : undefined;
    const fixedMax = element.max_date ? parseLocalDate(element.max_date) : undefined;

    if (rule === DATE_RULES.None && !fixedMin && !fixedMax) {
      return;
    }

    const today = startOfDay(new Date());

    // "After today" bottoms out at tomorrow on a calendar of whole days; at minute granularity the
    // rest of today is still open, so the day itself has to stay selectable and the validator
    // decides. The same asymmetry, mirrored, applies to "before today".
    const strictDayShift = granularity === DATE_GRANULARITIES.Day ? 1 : 0;

    const ruleMin =
      rule === DATE_RULES.After
        ? addDays(today, strictDayShift)
        : rule === DATE_RULES.OnOrAfter
          ? today
          : undefined;

    const ruleMax =
      rule === DATE_RULES.Before
        ? addDays(today, -strictDayShift)
        : rule === DATE_RULES.OnOrBefore
          ? today
          : undefined;

    // The calendar gets the merged bounds — it can only offer or withhold whole days, and a day
    // either survives both the rule and the fixed bound or it does not.
    const minDate = latest(ruleMin, fixedMin);
    const maxDate = earliest(ruleMax, fixedMax);

    if (minDate) {
      props['minDate'] = minDate;
    }
    if (maxDate) {
      props['maxDate'] = maxDate;
    }

    // The validator gets the author's own bounds, unmerged, so its message names the date the
    // author actually set. Anything the rule half of the merge would have caught the rule catches
    // first, and says so in the rule's own words.
    if (fixedMin) {
      props['dateMinBound'] = fixedMin;
    }
    if (fixedMax) {
      props['dateMaxBound'] = fixedMax;
    }

    props['dateRule'] = rule;
    props['dateGranularity'] = granularity;

    field.validators = {
      ...field.validators,
      validation: [
        ...((field.validators?.['validation'] as string[] | undefined) ?? []),
        FORMLY_VALIDATORS.DateRule,
      ],
    };
  }

  /** `visible_conditions` -> `expressions.hide`; `preserve_data` -> `resetOnHide: false`. */
  private applyVisibility(field: FormlyFieldConfig, element: FormElement): void {
    if (element.hidden) {
      field.hide = true;
      return;
    }

    const predicate = buildRulePredicate(element.visible_conditions);
    if (!predicate) {
      return;
    }

    field.expressions = {
      ...field.expressions,
      hide: (current: FormlyFieldConfig) => !predicate(this.modelOf(current)),
    };

    if (element.visible_conditions.preserve_data) {
      field.resetOnHide = false;
    }
  }

  /** `required_conditions` -> `expressions['props.required']`. */
  private applyRequirement(field: FormlyFieldConfig, element: FormElement): void {
    const predicate = buildRulePredicate(element.required_conditions);
    if (!predicate) {
      return;
    }

    field.expressions = {
      ...field.expressions,
      'props.required': (current: FormlyFieldConfig) => predicate(this.modelOf(current)),
    };
  }

  /** Sections are keyless, so a field's own model *is* the flat root model. */
  private modelOf(field: FormlyFieldConfig): RuleModel {
    return (field.model ?? {}) as RuleModel;
  }

  private defaultValue(element: FormElement): unknown {
    const raw = element.default_value;
    // Media is captured, never pre-filled from a text default. A weekly shift grid
    // has no meaningful single-line default either, so it starts empty.
    if (
      isMediaType(element.type) ||
      element.type === ELEMENT_TYPES.CalendarWithHours
    ) {
      return undefined;
    }

    // Resolved from the clock, not from the definition — which is the point: a fixed date default
    // would start contradicting the field's own "must be in the future" rule the day it passed.
    if (element.default_value_mode === DEFAULT_VALUE_MODES.Now) {
      return this.clockDefault(element.type);
    }

    if (raw === null || raw === '') {
      return undefined;
    }

    switch (element.type) {
      case ELEMENT_TYPES.Numeric: {
        const parsed = Number(raw);
        return Number.isNaN(parsed) ? undefined : parsed;
      }
      // Parsed as local values so they round-trip with what the payload emits.
      case ELEMENT_TYPES.Date:
        return parseLocalDate(raw);
      // A time is `HH:mm` text end to end — the picker holds no Date to parse into.
      case ELEMENT_TYPES.Time:
        return completeTime(raw);
      // Normalized rather than passed through: the control splits on the separator and would
      // swallow a malformed default into an empty pair while the model still held the bad string.
      case ELEMENT_TYPES.DateTime:
        return normalizeDateTime(raw) || undefined;
      case ELEMENT_TYPES.MultipleChoice:
        return raw.split(',').map((item) => item.trim()).filter((item) => item.length > 0);
      case ELEMENT_TYPES.Geolocation:
        return parseCoordinate(raw);
      default:
        return raw;
    }
  }

  /** The fill-time clock in each type's own stored shape. */
  private clockDefault(type: ElementType): unknown {
    const now = new Date();

    switch (type) {
      case ELEMENT_TYPES.Date:
        return startOfDay(now);
      case ELEMENT_TYPES.DateTime:
        return `${toLocalDate(now)}${DATE_TIME_SEPARATOR}${toLocalTime(now)}`;
      case ELEMENT_TYPES.Time:
        return toLocalTime(now);
      default:
        // Only the clock-backed types offer the mode; anything else simply has no default.
        return undefined;
    }
  }
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}

function addDays(value: Date, days: number): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate() + days);
}

/** The later of two lower bounds — the effective floor when a rule and a fixed bound both apply. */
function latest(first: Date | undefined, second: Date | undefined): Date | undefined {
  if (!first) return second;
  if (!second) return first;
  return first.getTime() >= second.getTime() ? first : second;
}

/** The earlier of two upper bounds. */
function earliest(first: Date | undefined, second: Date | undefined): Date | undefined {
  if (!first) return second;
  if (!second) return first;
  return first.getTime() <= second.getTime() ? first : second;
}
