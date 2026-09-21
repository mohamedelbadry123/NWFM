/**
 * Fulcrum-style form builder schema (client-only).
 *
 * All display text is bilingual (`_en` / `_ar`) because the app runs EN + AR.
 * Choice options carry EN/AR labels and support cascading via a parent choice
 * field (`parent_field`) plus a per-option `dependency_value`.
 */

export const ELEMENT_TYPES = {
  Text: 'text',
  /** Long free text — comments, remarks, a memo. Renders as a textarea; stores like `text`. */
  Memo: 'memo',
  Numeric: 'numeric',
  YesNo: 'yes_no',
  Date: 'date',
  Time: 'time',
  /** A from/to time pair per weekday — the paper survey's `ساعات العمل` row. */
  CalendarWithHours: 'calendar_with_hours',
  /** A calendar date and a clock time, answered together. */
  DateTime: 'date_time',
  SingleChoice: 'single_choice',
  MultipleChoice: 'multiple_choice',
  Signature: 'signature',
  Photo: 'photo',
  Video: 'video',
  Audio: 'audio',
  /** Any document the crew attaches — a permit PDF, a signed work order, a spreadsheet. */
  File: 'file',
  /** A code read off an asset. The answer is the decoded text. */
  Barcode: 'barcode',
  Geolocation: 'geolocation',
  Section: 'section',
} as const;

export type ElementType = (typeof ELEMENT_TYPES)[keyof typeof ELEMENT_TYPES];

export const PALETTE_GROUPS = {
  Basic: 'basic',
  Choice: 'choice',
  Media: 'media',
  Location: 'location',
  Design: 'design',
} as const;

export type PaletteGroup = (typeof PALETTE_GROUPS)[keyof typeof PALETTE_GROUPS];

export const RULE_OPERATORS = {
  Equal: 'equal',
  NotEqual: 'not_equal',
  Contains: 'contains',
  StartsWith: 'starts_with',
  GreaterThan: 'greater_than',
  LessThan: 'less_than',
  IsEmpty: 'is_empty',
  IsNotEmpty: 'is_not_empty',
} as const;

export type RuleOperator = (typeof RULE_OPERATORS)[keyof typeof RULE_OPERATORS];

export const RULE_MATCH = {
  All: 'all',
  Any: 'any',
} as const;

export type RuleMatch = (typeof RULE_MATCH)[keyof typeof RULE_MATCH];

export const NUMERIC_FORMATS = {
  Decimal: 'decimal',
  Integer: 'integer',
} as const;

export type NumericFormat = (typeof NUMERIC_FORMATS)[keyof typeof NUMERIC_FORMATS];

export const SECTION_DISPLAYS = {
  Inline: 'inline',
  Page: 'page',
} as const;

export type SectionDisplay = (typeof SECTION_DISPLAYS)[keyof typeof SECTION_DISPLAYS];

/**
 * The date constraint a `date` / `date_time` element may carry. Defined in the shared dynamic-form
 * contract because the renderer enforces it, and re-exported here so the designer's own code keeps
 * reading the schema from one place.
 *
 * Compared at calendar-day precision for a `date` and at minute precision for a `date_time`, so
 * "after" reads as *after today* on the one and *later than now* on the other.
 */
import type { DateRule } from '../components/dynamic-form/formly-preview.types';
export { DATE_RULES, type DateRule } from '../components/dynamic-form/formly-preview.types';

/**
 * How an element's `default_value` is resolved.
 *
 * `Now` carries no stored value at all — the fill-time clock supplies it. A fixed date default
 * would otherwise contradict the field's own rule the moment that date passes.
 */
export const DEFAULT_VALUE_MODES = {
  Fixed: 'fixed',
  Now: 'now',
} as const;

export type DefaultValueMode = (typeof DEFAULT_VALUE_MODES)[keyof typeof DEFAULT_VALUE_MODES];

/** `YYYY-MM-DD` — the only shape a fixed date bound or a `date` default is stored in. */
export const DATE_ONLY_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

/**
 * Barcode symbologies a `barcode` element can accept.
 *
 * The values are the names used by the browser's `BarcodeDetector` API, which is also what the
 * Android/iOS scanning SDKs call them — so the definition JSON hands the mobile app a list it can
 * pass straight to its scanner without a translation table.
 */
export const BARCODE_FORMATS = {
  QrCode: 'qr_code',
  Code128: 'code_128',
  Code39: 'code_39',
  Code93: 'code_93',
  Ean13: 'ean_13',
  Ean8: 'ean_8',
  UpcA: 'upc_a',
  UpcE: 'upc_e',
  DataMatrix: 'data_matrix',
  Itf: 'itf',
  Pdf417: 'pdf417',
  Aztec: 'aztec',
  Codabar: 'codabar',
} as const;

export type BarcodeFormat = (typeof BARCODE_FORMATS)[keyof typeof BARCODE_FORMATS];

/**
 * How each symbology reads to a human. Industry names, deliberately untranslated — this is what
 * is printed on the asset tag and in the scanner's own documentation.
 */
export const BARCODE_FORMAT_LABELS: Readonly<Record<BarcodeFormat, string>> = {
  [BARCODE_FORMATS.QrCode]: 'QR Code',
  [BARCODE_FORMATS.Code128]: 'Code 128',
  [BARCODE_FORMATS.Code39]: 'Code 39',
  [BARCODE_FORMATS.Code93]: 'Code 93',
  [BARCODE_FORMATS.Ean13]: 'EAN-13',
  [BARCODE_FORMATS.Ean8]: 'EAN-8',
  [BARCODE_FORMATS.UpcA]: 'UPC-A',
  [BARCODE_FORMATS.UpcE]: 'UPC-E',
  [BARCODE_FORMATS.DataMatrix]: 'Data Matrix',
  [BARCODE_FORMATS.Itf]: 'ITF',
  [BARCODE_FORMATS.Pdf417]: 'PDF417',
  [BARCODE_FORMATS.Aztec]: 'Aztec',
  [BARCODE_FORMATS.Codabar]: 'Codabar',
};

/** QR plus the common 1D codes — covers asset tags, meter serials and retail codes. */
export const DEFAULT_BARCODE_FORMATS: readonly BarcodeFormat[] = [
  BARCODE_FORMATS.QrCode,
  BARCODE_FORMATS.Code128,
  BARCODE_FORMATS.Code39,
  BARCODE_FORMATS.Ean13,
  BARCODE_FORMATS.Ean8,
  BARCODE_FORMATS.UpcA,
];

/**
 * Longest decoded value a barcode answer may carry. Matches the `NVARCHAR(400)` column the
 * submissions table creates for the type, so an over-long scan is refused by the form rather
 * than truncated by SQL Server.
 */
export const BARCODE_MAX_LENGTH = 400;

/**
 * A `data_name` becomes a SQL column on the server, so it has to be a legal unquoted identifier.
 * Mirrors `SurveyDataName.IsValid` in the API — a name this rejects is refused at publish, and an
 * answer keyed on it can never be written. Kept here so the field editor blocks it at design time.
 */
export const DATA_NAME_PATTERN = /^[A-Za-z_][A-Za-z0-9_]{0,127}$/;

/** Rule mode reused by the visibility and requirement dialogs. */
export const RULE_MODES = {
  Visibility: 'visibility',
  Requirement: 'requirement',
} as const;

export type RuleMode = (typeof RULE_MODES)[keyof typeof RULE_MODES];

export interface Choice {
  value: string;
  label_en: string;
  label_ar: string;
  /** Parent option value that activates this option (cascading). Null = always. */
  dependency_value: string | null;
  /**
   * C2M `FAStatus` for this option when the field is `wfm_action_taken`: `C` or `X`.
   * Null = not set; the closure falls back to the C2M action mapping table, then the built-in rule.
   */
  c2m_fa_status?: string | null;
  /**
   * Optional reason sent with the status: the cancel reason when `X` (defaults to `value` when
   * blank), the closure reason when `C`.
   */
  c2m_reason?: string | null;
}

/**
 * The one field whose options carry a C2M close outcome. Matches `C2mFieldNames.ActionTaken` on the
 * server — a task closes its C2M field activity with whatever the chosen option says.
 */
export const ACTION_TAKEN_DATA_NAME = 'wfm_action_taken';

/** The two outcomes C2M accepts when a field activity closes. */
export const C2M_FA_STATUSES = {
  Completed: 'C',
  Cancelled: 'X',
} as const;

export interface ValidationPattern {
  regex: string;
  description: string;
}

export interface RuleCondition {
  /** `data_name` of the element this condition targets. */
  field: string;
  operator: RuleOperator;
  value: string;
}

export interface RuleGroup {
  match: RuleMatch;
  conditions: RuleCondition[];
  /** Only meaningful for visibility rules. */
  preserve_data: boolean;
}

export interface FormElement {
  /** Internal id — stripped from the exported JSON. */
  key: string;
  type: ElementType;

  label_en: string;
  label_ar: string;
  data_name: string;
  /**
   * C2M `ParameterName` this field's answer is sent under when a task closes its field activity in
   * C2M. Empty = not sent. Distinct from `data_name` (`wfm_building_units` → `CM_BUNIT`).
   */
  c2m_parameter_name?: string | null;
  description_en: string;
  description_ar: string;

  default_value: string | null;
  /** `now` ignores `default_value` and takes the fill-time clock instead. */
  default_value_mode: DefaultValueMode;
  required: boolean;
  hidden: boolean;
  /** Read-only field. */
  disabled: boolean;

  // text
  min_length: number | null;
  max_length: number | null;
  pattern: ValidationPattern | null;

  // numeric
  format: NumericFormat | null;
  min: number | null;
  max: number | null;

  // date / date_time — a rule relative to the system clock, narrowed by optional fixed bounds
  date_rule: DateRule | null;
  /** Inclusive lower bound, `YYYY-MM-DD`. For a `date_time` it means 00:00 of that day. */
  min_date: string | null;
  /** Inclusive upper bound, `YYYY-MM-DD`. For a `date_time` it means 23:59 of that day. */
  max_date: string | null;

  // choice (single_choice / multiple_choice)
  allow_other: boolean;
  multiple: boolean;
  /** `data_name` of another choice field this field cascades from. Null = independent. */
  parent_field: string | null;
  choices: Choice[];

  // media (photo / video / audio / file / signature) — signature is a single uploaded PNG
  max_files: number | null;
  max_file_size_mb: number | null;
  /** Lower-case, dot-less file extensions. Empty = accept anything of the kind. */
  allowed_extensions: string[];

  // barcode — symbologies the scanner should accept. Empty = accept anything it can read.
  barcode_formats: BarcodeFormat[];

  // geolocation — initial map zoom; `default_value` holds "lat,lng"
  map_zoom: number | null;

  // section
  display: SectionDisplay | null;
  elements: FormElement[];

  // rules
  visible_conditions: RuleGroup;
  required_conditions: RuleGroup;
}

export interface FormSchema {
  name_en: string;
  name_ar: string;
  elements: FormElement[];
}

/** Stable CDK drop-list ids shared between the palette and the canvas. */
export const DROP_IDS = {
  Palette: 'fb-palette',
  CanvasRoot: 'fb-canvas-root',
} as const;

export const CHOICE_TYPES: readonly ElementType[] = [
  ELEMENT_TYPES.SingleChoice,
  ELEMENT_TYPES.MultipleChoice,
];

export function isChoiceType(type: ElementType): boolean {
  return CHOICE_TYPES.includes(type);
}

/** Short `text` and long `memo` share min/max length and pattern rules. */
export function isTextLikeType(type: ElementType): boolean {
  return type === ELEMENT_TYPES.Text || type === ELEMENT_TYPES.Memo;
}

const ARABIC_LANG = 'ar';

/** Pick EN/AR display text for the active UI language, falling back to the other then `fallback`. */
export function localizedDisplay(
  english: string | null | undefined,
  arabic: string | null | undefined,
  lang: string,
  fallback = '',
): string {
  const en = english?.trim() ?? '';
  const ar = arabic?.trim() ?? '';
  const preferred = lang === ARABIC_LANG ? ar : en;
  return preferred || en || ar || fallback;
}

/** Types carrying a date constraint. A `time` has no system date to be measured against. */
export const DATE_RULE_TYPES: readonly ElementType[] = [ELEMENT_TYPES.Date, ELEMENT_TYPES.DateTime];

export function isDateRuleType(type: ElementType): boolean {
  return DATE_RULE_TYPES.includes(type);
}

/** Types whose default may be the fill-time clock rather than a value chosen at design time. */
export const CLOCK_DEFAULT_TYPES: readonly ElementType[] = [
  ELEMENT_TYPES.Date,
  ELEMENT_TYPES.DateTime,
  ELEMENT_TYPES.Time,
];

export function isClockDefaultType(type: ElementType): boolean {
  return CLOCK_DEFAULT_TYPES.includes(type);
}

/** Media types that capture one or more files from the device. */
export const ATTACHMENT_TYPES: readonly ElementType[] = [
  ELEMENT_TYPES.Photo,
  ELEMENT_TYPES.Video,
  ELEMENT_TYPES.Audio,
  ELEMENT_TYPES.File,
];

/** Every media type, including the drawn signature. */
export const MEDIA_TYPES: readonly ElementType[] = [ELEMENT_TYPES.Signature, ...ATTACHMENT_TYPES];

/** File-backed media — the only types carrying `max_files` / `max_file_size_mb`. */
export function isAttachmentType(type: ElementType): boolean {
  return ATTACHMENT_TYPES.includes(type);
}

export function isMediaType(type: ElementType): boolean {
  return MEDIA_TYPES.includes(type);
}

/** Extensions offered by default for each file-backed media type. */
export const DEFAULT_ALLOWED_EXTENSIONS: Readonly<Partial<Record<ElementType, readonly string[]>>> =
  {
    [ELEMENT_TYPES.Photo]: ['jpg', 'jpeg', 'png', 'heic', 'webp'],
    [ELEMENT_TYPES.Video]: ['mp4', 'mov', 'webm'],
    [ELEMENT_TYPES.Audio]: ['mp3', 'wav', 'm4a', 'ogg'],
    [ELEMENT_TYPES.File]: ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'csv', 'txt'],
  };

/** `.JPG` / ` jpg ` / `. png` -> `jpg`. Returns '' for entries that are only dots/space. */
export function normalizeExtension(value: string): string {
  return value
    .toLowerCase()
    .replace(/^[.\s]+/, '')
    .trim();
}

/** Normalize, drop blanks, de-duplicate — order is preserved. */
export function normalizeExtensions(values: readonly string[]): string[] {
  const result = new Set<string>();
  for (const value of values) {
    const extension = normalizeExtension(value);
    if (extension) {
      result.add(extension);
    }
  }
  return [...result];
}
