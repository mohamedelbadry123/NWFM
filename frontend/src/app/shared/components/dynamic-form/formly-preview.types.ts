/**
 * Names of the Formly types / wrappers / validators the app registers in
 * `app.config.ts`, plus the shapes the custom components expect on `props`.
 *
 * Kept in `shared` (not in the form-builder feature) so `app.config.ts` can
 * register everything without pulling a feature module into the initial bundle.
 */

/** Formly type names available to any dynamic form in the app. */
export const FORMLY_TYPES = {
  Input: 'input',
  Textarea: 'textarea',
  Radio: 'radio',
  Checkbox: 'checkbox',
  Select: 'select',
  /**
   * Custom — p-datepicker bound to `props.minDate` / `props.maxDate`.
   *
   * The `datepicker` type shipped with `@ngx-formly/primeng` cannot be used: its template binds
   * neither bound, so a constrained field would keep offering days its own validator rejects.
   */
  Date: 'date',
  /** Custom — p-datepicker in `timeOnly` mode. */
  Time: 'time',
  /** Custom — p-select with cascade + "Other" support. */
  Choice: 'choice',
  /** Custom — extends `choice` with `props.multiple = true` (p-multiselect). */
  MultiChoice: 'multichoice',
  /** Custom — draw-to-sign canvas pad. */
  Signature: 'signature',
  /** Custom — file capture for photo / video / audio / file (`props.mediaKind`). */
  Attachment: 'attachment',
  /** Custom — Google Map coordinate picker. */
  Geolocation: 'geolocation',
  /** Custom — decoded barcode text; the mobile app scans, the web types. */
  Barcode: 'barcode',
  /** Custom — a from/to time pair per weekday. */
  CalendarWithHours: 'calendar_with_hours',
  /** Custom — a calendar date and a clock time, answered together. */
  DateTime: 'date_time',
} as const;

export type FormlyTypeName = (typeof FORMLY_TYPES)[keyof typeof FORMLY_TYPES];

/** Formly wrapper names. `FormField` comes from `@ngx-formly/primeng`. */
export const FORMLY_WRAPPERS = {
  FormField: 'form-field',
  /** Custom — renders `props.description` under the control. */
  FieldHelp: 'field-help',
  /** Custom — renders a titled panel around a section's `fieldGroup`. */
  SectionPanel: 'section-panel',
} as const;

export type FormlyWrapperName = (typeof FORMLY_WRAPPERS)[keyof typeof FORMLY_WRAPPERS];

/** Custom validator names registered through `FORMLY_CONFIG`. */
export const FORMLY_VALIDATORS = {
  Integer: 'integer',
  /** Every selected weekday must carry both a start and an end time. */
  CalendarWithHoursRange: 'calendarWithHoursRange',
  /** A `date` / `date_time` answer against the field's own constraint — see `dateRuleValidator`. */
  DateRule: 'dateRule',
} as const;

/**
 * Constraint on a `date` / `date_time` answer, evaluated against the system clock at the moment the
 * survey is filled — "the inspection cannot be booked in the past" stays true next month, which a
 * fixed bound would not.
 *
 * Lives here rather than in the form-builder feature because both sides need it: the designer
 * writes it into the definition, the renderer enforces it. Re-exported from `form-builder.types`
 * for the designer's convenience, and mirrored server-side by `SurveyDateRules` — the submit
 * endpoint enforces the same rule.
 */
export const DATE_RULES = {
  None: 'none',
  After: 'after',
  OnOrAfter: 'on_or_after',
  Before: 'before',
  OnOrBefore: 'on_or_before',
} as const;

export type DateRule = (typeof DATE_RULES)[keyof typeof DATE_RULES];

/**
 * Granularity a `dateRule` comparison runs at (`props.dateGranularity`). A calendar day for a
 * `date`, so any time of today counts as today; the minute for a `date_time`, where "later than
 * now" is a real instant.
 */
export const DATE_GRANULARITIES = {
  Day: 'day',
  Minute: 'minute',
} as const;

export type DateGranularity = (typeof DATE_GRANULARITIES)[keyof typeof DATE_GRANULARITIES];

/**
 * What a failed {@link FORMLY_VALIDATORS.DateRule} check reports: the Transloco key naming the
 * rule that was broken, and the parameters its message interpolates. Carried on the error rather
 * than split across eight validators, so one rule engine serves both date types.
 */
export interface DateRuleError {
  readonly key: string;
  readonly params?: Record<string, unknown>;
}

/** Option shape consumed by `FormlyChoiceComponent` (`props.options`). */
export interface FormlyChoiceOption {
  readonly value: string;
  readonly label: string;
  /**
   * Value of the parent field (`props.parentField`) that activates this option.
   * `null` means the option is always available.
   */
  readonly dependencyValue: string | null;
}

/** Which capture mode `FormlyAttachmentComponent` runs in (`props.mediaKind`). */
export const MEDIA_KINDS = {
  Photo: 'photo',
  Video: 'video',
  Audio: 'audio',
  /** A document picked from storage — no camera, no recorder. */
  File: 'file',
} as const;

export type MediaKind = (typeof MEDIA_KINDS)[keyof typeof MEDIA_KINDS];

/**
 * One captured file (photo / video / audio / file / signature). `url` is a blob
 * object URL owned by the component when available, so previews can render
 * without a round trip. Seeded values from a previous fill may only have `fileId`
 * until the component downloads a preview URL.
 *
 * `fileId` / `path` appear once the file has been uploaded — they are the
 * durable reference the submission row stores. While `uploading` is true the
 * entry is still in flight and must not be submitted.
 */
export interface FormlyAttachment {
  readonly name: string;
  readonly size: number;
  readonly type: string;
  /** Local blob object URL; absent until downloaded or freshly captured. */
  readonly url?: string;
  /** Server handle; absent in the designer preview, where nothing is uploaded. */
  readonly fileId?: string;
  /** Storage-relative path returned by the upload endpoint. */
  readonly path?: string;
  readonly uploading?: boolean;
  /** 0–100 while `uploading`. */
  readonly progress?: number;
}

/**
 * `formState` keys describing the fill a field belongs to. The attachment and signature fields
 * upload on pick only when `formId` is present — the designer preview has no form to upload
 * against, so it stays purely local and leaves no pending files behind.
 */
export const FORM_STATE_FORM_ID = 'formId';
export const FORM_STATE_VERSION_NO = 'versionNo';
export const FORM_STATE_CONTEXT_TYPE = 'contextType';
export const FORM_STATE_CONTEXT_ID = 'contextId';

/**
 * Days offered by a `calendar_with_hours` field, in the order they are shown and stored.
 * Saturday first — the Saudi working week, and the order the paper survey uses.
 */
export const WEEKDAY_CODES = ['sat', 'sun', 'mon', 'tue', 'wed', 'thu', 'fri'] as const;

export type WeekdayCode = (typeof WEEKDAY_CODES)[number];

/** Translation key prefix for the weekday names and the from/to captions. */
export const CALENDAR_WITH_HOURS_I18N_PREFIX = 'formly.calendarWithHours';

/** One day's shift. Both ends are local `HH:mm`, matching what the `time` type stores. */
export interface CalendarDayRange {
  readonly from: string;
  readonly to: string;
}

/**
 * A `calendar_with_hours` answer. Only the days the crew actually works appear, so an
 * absent key means "closed" — there is no need for an explicit flag.
 *
 * A shift may end before it starts (`22:00` → `06:00`); that is an overnight
 * shift, not an error.
 */
export type CalendarWithHoursValue = Partial<Record<WeekdayCode, CalendarDayRange>>;

/** `HH:mm`, the only shape a calendar-with-hours end is stored in. */
export const CALENDAR_WITH_HOURS_TIME_PATTERN = /^\d{2}:\d{2}$/;

/**
 * A `date_time` answer: `YYYY-MM-DD HH:mm`, local. A space rather than an ISO `T` because the
 * value is read straight out of the stored answer by the submission viewer,
 * and `2026-08-13 14:30` needs no explaining to whoever reads it.
 */
export const DATE_TIME_PATTERN = /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/;

/** Between the date and the time of a `date_time` value. */
export const DATE_TIME_SEPARATOR = ' ';

/** Narrow an unknown control/stored value to a calendar-with-hours map. */
export function isCalendarWithHoursValue(value: unknown): value is CalendarWithHoursValue {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** Sentinel option value used when a choice field has `allow_other` enabled. */
export const CHOICE_OTHER_VALUE = '__other__';

/** Model key that stores the free-text value typed for the "Other" option. */
export function choiceOtherKey(key: string): string {
  return `${key}_other`;
}
