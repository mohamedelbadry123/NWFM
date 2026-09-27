import { createElement } from './form-schema-element';
import {
  BARCODE_FORMATS,
  DATE_ONLY_PATTERN,
  DATE_RULES,
  DEFAULT_VALUE_MODES,
  ELEMENT_TYPES,
  RULE_MATCH,
  isAttachmentType,
  isChoiceType,
  isDateRuleType,
  isTextLikeType,
  normalizeExtensions,
  type BarcodeFormat,
  type Choice,
  type DateRule,
  type DefaultValueMode,
  type ElementType,
  type FormSchema,
  type FormElement,
  type RuleGroup,
  type ValidationPattern,
} from './form-schema.types';

function emptyRuleGroup(): RuleGroup {
  return { match: RULE_MATCH.All, conditions: [], preserve_data: false };
}

function asString(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

function asNullableString(value: unknown): string | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  return typeof value === 'string' ? value : null;
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  return typeof value === 'number' ? value : null;
}

function asRuleGroup(value: unknown): RuleGroup {
  if (!value || typeof value !== 'object') {
    return emptyRuleGroup();
  }
  const raw = value as Record<string, unknown>;
  return {
    match: raw['match'] === RULE_MATCH.Any ? RULE_MATCH.Any : RULE_MATCH.All,
    conditions: Array.isArray(raw['conditions'])
      ? raw['conditions'].map((condition) => {
          const c = condition as Record<string, unknown>;
          return {
            // `field` names a data_name, which is trimmed on import — so this is too, or the rule
            // would point at a field that no longer answers to that name.
            field: asString(c['field']).trim(),
            operator: c['operator'] as RuleGroup['conditions'][number]['operator'],
            value: asString(c['value']),
          };
        })
      : [],
    preserve_data: !!raw['preserve_data'],
  };
}

function asPattern(value: unknown): ValidationPattern | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const raw = value as Record<string, unknown>;
  const regex = asString(raw['regex']);
  const description = asString(raw['description']);
  if (!regex) {
    return null;
  }
  return { regex, description };
}

/** An unknown rule name reads as "no constraint" rather than silently enforcing something else. */
function asDateRule(value: unknown): DateRule {
  const known = Object.values(DATE_RULES) as string[];
  return typeof value === 'string' && known.includes(value) ? (value as DateRule) : DATE_RULES.None;
}

/** A fixed bound is only honoured in the one shape the builder writes; anything else is dropped. */
function asDateOnly(value: unknown): string | null {
  return typeof value === 'string' && DATE_ONLY_PATTERN.test(value) ? value : null;
}

/**
 * A definition written before default modes existed carries no `default_value_mode`, and its
 * `default_value` is a value chosen at design time — which is exactly {@link DEFAULT_VALUE_MODES.Fixed}.
 */
function asDefaultValueMode(value: unknown): DefaultValueMode {
  return value === DEFAULT_VALUE_MODES.Now ? DEFAULT_VALUE_MODES.Now : DEFAULT_VALUE_MODES.Fixed;
}

function asChoices(value: unknown): Choice[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value.map((item) => {
    const raw = item as Record<string, unknown>;
    return {
      // The server trims a choice's value when it parses the definition, so a stored answer of
      // 'leak_type_1 ' would never resolve back to its label. Trimmed here to keep the two the same
      // string; `dependency_value` points at one of these values, so it follows.
      value: asString(raw['value']).trim(),
      label_en: asString(raw['label_en']),
      label_ar: asString(raw['label_ar']),
      dependency_value: asNullableString(raw['dependency_value'])?.trim() ?? null,
      c2m_fa_status: asNullableString(raw['c2m_fa_status'])?.trim().toUpperCase() || null,
      c2m_reason: asNullableString(raw['c2m_reason'])?.trim() || null,
    };
  });
}

/**
 * Accepts either an array or a comma-separated string, so hand-written JSON
 * (`"jpg, png"`) imports as cleanly as exported JSON (`["jpg", "png"]`).
 * A missing key falls back to the type's defaults; an explicit `[]` means
 * "accept anything of this kind" and is preserved.
 */
function asExtensions(value: unknown, fallback: string[]): string[] {
  if (Array.isArray(value)) {
    return normalizeExtensions(value.map((item) => asString(item)));
  }
  if (typeof value === 'string') {
    return normalizeExtensions(value.split(','));
  }
  return [...fallback];
}

/**
 * Accepts an array or a comma-separated string, mirroring `asExtensions`. Unrecognised symbologies
 * are dropped rather than kept: the list is handed straight to a scanner SDK, and a name it does
 * not know is a runtime error there instead of a visible mistake here.
 */
function asBarcodeFormats(value: unknown, fallback: BarcodeFormat[]): BarcodeFormat[] {
  const known = Object.values(BARCODE_FORMATS) as string[];
  const raw = Array.isArray(value)
    ? value.map((item) => asString(item))
    : typeof value === 'string'
      ? value.split(',')
      : null;

  if (raw === null) {
    return [...fallback];
  }

  const result = new Set<BarcodeFormat>();
  for (const item of raw) {
    const normalized = item.trim().toLowerCase();
    if (known.includes(normalized)) {
      result.add(normalized as BarcodeFormat);
    }
  }
  return [...result];
}

function isElementType(value: unknown): value is ElementType {
  return (
    typeof value === 'string' &&
    (Object.values(ELEMENT_TYPES) as string[]).includes(value)
  );
}

/** Convert exported Fulcrum-style JSON back into editable `FormElement` trees. */
export function deserializeSchemaElement(raw: Record<string, unknown>): FormElement {
  const type = isElementType(raw['type']) ? raw['type'] : ELEMENT_TYPES.Text;
  const defaults = createElement(type);

  const element: FormElement = {
    ...defaults,
    key: `el_${Math.random().toString(36).slice(2, 8)}`,
    type,
    label_en: asString(raw['label_en'], defaults.label_en),
    label_ar: asString(raw['label_ar'], defaults.label_ar),
    // Trimmed on the way in so an existing definition carrying 'leak_type ' is cleaned by simply
    // opening it: the server trims the name to derive the column, and an untrimmed key posted
    // against that column matches nothing and is dropped.
    data_name: asString(raw['data_name'], defaults.data_name).trim(),
    description_en: asString(raw['description_en'], defaults.description_en),
    description_ar: asString(raw['description_ar'], defaults.description_ar),
    hidden: raw['hidden'] !== undefined ? !!raw['hidden'] : defaults.hidden,
    visible_conditions: asRuleGroup(raw['visible_conditions']),
  };

  if (type === ELEMENT_TYPES.Section) {
    element.display = (raw['display'] as FormElement['display']) ?? defaults.display;
    element.elements = Array.isArray(raw['elements'])
      ? raw['elements'].map((child) => deserializeSchemaElement(child as Record<string, unknown>))
      : [];
    return element;
  }

  element.c2m_parameter_name = asNullableString(raw['c2m_parameter_name'])?.trim() || null;
  element.default_value = asNullableString(raw['default_value']);
  element.default_value_mode = asDefaultValueMode(raw['default_value_mode']);
  element.required = raw['required'] !== undefined ? !!raw['required'] : defaults.required;
  element.disabled = raw['disabled'] !== undefined ? !!raw['disabled'] : defaults.disabled;
  element.required_conditions = asRuleGroup(raw['required_conditions']);

  if (isDateRuleType(type)) {
    element.date_rule = asDateRule(raw['date_rule']);
    element.min_date = asDateOnly(raw['min_date']);
    element.max_date = asDateOnly(raw['max_date']);
  }

  if (isTextLikeType(type)) {
    element.min_length = asNullableNumber(raw['min_length']);
    element.max_length = asNullableNumber(raw['max_length']);
    element.pattern = asPattern(raw['pattern']);
  }

  if (type === ELEMENT_TYPES.Numeric) {
    element.format = (raw['format'] as FormElement['format']) ?? defaults.format;
    element.min = asNullableNumber(raw['min']);
    element.max = asNullableNumber(raw['max']);
  }

  if (isAttachmentType(type)) {
    element.max_files = asNullableNumber(raw['max_files']) ?? defaults.max_files;
    element.max_file_size_mb = asNullableNumber(raw['max_file_size_mb']) ?? defaults.max_file_size_mb;
    element.allowed_extensions = asExtensions(raw['allowed_extensions'], defaults.allowed_extensions);
  }

  if (type === ELEMENT_TYPES.Barcode) {
    element.barcode_formats = asBarcodeFormats(raw['barcode_formats'], defaults.barcode_formats);
  }

  if (type === ELEMENT_TYPES.Geolocation) {
    element.map_zoom = asNullableNumber(raw['map_zoom']) ?? defaults.map_zoom;
  }

  if (isChoiceType(type)) {
    element.allow_other = raw['allow_other'] !== undefined ? !!raw['allow_other'] : defaults.allow_other;
    element.multiple = type === ELEMENT_TYPES.MultipleChoice;
    // Trimmed alongside `data_name`, which it points at — trimming one and not the other would
    // leave the cascade referencing a parent that no longer answers to that name.
    element.parent_field = asNullableString(raw['parent_field'])?.trim() ?? null;
    element.choices = asChoices(raw['choices']);
    if (element.choices.length === 0) {
      element.choices = defaults.choices;
    }
  }

  return element;
}

export function deserializeSchema(raw: Record<string, unknown>): FormSchema {
  return {
    name_en: asString(raw['name_en'], 'Untitled Form'),
    name_ar: asString(raw['name_ar'], 'نموذج بدون عنوان'),
    elements: Array.isArray(raw['elements'])
      ? raw['elements'].map((el) => deserializeSchemaElement(el as Record<string, unknown>))
      : [],
  };
}
