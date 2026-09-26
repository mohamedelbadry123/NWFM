import {
  DATE_RULES,
  DEFAULT_ALLOWED_EXTENSIONS,
  DEFAULT_BARCODE_FORMATS,
  DEFAULT_VALUE_MODES,
  ELEMENT_TYPES,
  NUMERIC_FORMATS,
  RULE_MATCH,
  SECTION_DISPLAYS,
  type ElementType,
  type FormElement,
  type RuleGroup,
  isAttachmentType,
  isChoiceType,
  isDateRuleType,
} from './form-schema.types';

/**
 * Builds a fresh element with sensible defaults for its type. Lives beside the schema model rather
 * than with the builder's palette because importing a schema needs the same defaults: a document
 * written by an older builder omits whatever it did not know about, and those gaps are filled here.
 *
 * `data_name` is left empty — the builder's store assigns it, so it can guarantee uniqueness.
 */
export function createElement(type: ElementType): FormElement {
  const base: FormElement = {
    key: generateKey(),
    type,
    label_en: '',
    label_ar: '',
    data_name: '',
    c2m_parameter_name: null,
    description_en: '',
    description_ar: '',
    default_value: null,
    default_value_mode: DEFAULT_VALUE_MODES.Fixed,
    required: false,
    hidden: false,
    disabled: false,
    min_length: null,
    max_length: null,
    pattern: null,
    format: type === ELEMENT_TYPES.Numeric ? NUMERIC_FORMATS.Decimal : null,
    min: null,
    max: null,
    date_rule: isDateRuleType(type) ? DATE_RULES.None : null,
    min_date: null,
    max_date: null,
    allow_other: false,
    multiple: type === ELEMENT_TYPES.MultipleChoice,
    parent_field: null,
    choices: [],
    max_files: isAttachmentType(type) ? DEFAULT_MAX_FILES : null,
    max_file_size_mb: isAttachmentType(type) ? DEFAULT_MAX_FILE_SIZE_MB : null,
    allowed_extensions: [...(DEFAULT_ALLOWED_EXTENSIONS[type] ?? [])],
    barcode_formats: type === ELEMENT_TYPES.Barcode ? [...DEFAULT_BARCODE_FORMATS] : [],
    map_zoom: type === ELEMENT_TYPES.Geolocation ? DEFAULT_MAP_ZOOM : null,
    display: type === ELEMENT_TYPES.Section ? SECTION_DISPLAYS.Inline : null,
    elements: [],
    visible_conditions: emptyRuleGroup(),
    required_conditions: emptyRuleGroup(),
  };

  if (isChoiceType(type)) {
    base.choices = [
      { value: 'option_1', label_en: 'Option 1', label_ar: 'الخيار 1', dependency_value: null, c2m_fa_status: null, c2m_reason: null },
      { value: 'option_2', label_en: 'Option 2', label_ar: 'الخيار 2', dependency_value: null, c2m_fa_status: null, c2m_reason: null },
    ];
  }

  return base;
}

export function emptyRuleGroup(): RuleGroup {
  return { match: RULE_MATCH.All, conditions: [], preserve_data: false };
}

/** Defaults for a new file-backed media element. */
const DEFAULT_MAX_FILES = 3;
const DEFAULT_MAX_FILE_SIZE_MB = 10;

/** Country-level view. */
const DEFAULT_MAP_ZOOM = 5;

/**
 * The element's identity while it is being designed. It never leaves the builder — `serialize()`
 * strips it — so it only has to be unique within one editing session.
 */
function generateKey(): string {
  return `el_${Math.random().toString(36).slice(2, 8)}`;
}
