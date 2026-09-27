import type { AbstractControl, ValidationErrors } from '@angular/forms';
import type { ConfigOption, FormlyFieldConfig } from '@ngx-formly/core';
import type { TranslateService } from '@ngx-translate/core';
import {
  FORMLY_VALIDATORS,
  CALENDAR_WITH_HOURS_TIME_PATTERN,
  DATE_GRANULARITIES,
  DATE_RULES,
  DATE_TIME_PATTERN,
  isCalendarWithHoursValue,
  type CalendarDayRange,
  type DateGranularity,
  type DateRule,
  type DateRuleError,
} from './formly-preview.types';

/** Angular/Formly error keys we surface a localized message for. */
const ERROR_KEYS = {
  Required: 'required',
  MinLength: 'minlength',
  MaxLength: 'maxlength',
  Min: 'min',
  Max: 'max',
  Pattern: 'pattern',
} as const;

const I18N_KEYS = {
  Required: 'validation.required',
  MinLength: 'validation.minLength',
  MaxLength: 'validation.maxLength',
  Min: 'validation.min',
  Max: 'validation.max',
  Pattern: 'validation.pattern',
  Integer: 'validation.integer',
  CalendarWithHoursRange: 'validation.calendarWithHoursRange',
} as const;

/**
 * One message per rule per granularity — a `date` is told about *today*, a `date_time` about *now*,
 * because a crew reading "must be later than today" on a field asking for a time has been told the
 * wrong thing about why its answer was refused.
 */
const DATE_RULE_I18N_KEYS: Readonly<
  Record<DateGranularity, Readonly<Record<Exclude<DateRule, 'none'>, string>>>
> = {
  [DATE_GRANULARITIES.Day]: {
    [DATE_RULES.After]: 'validation.dateAfterToday',
    [DATE_RULES.OnOrAfter]: 'validation.dateOnOrAfterToday',
    [DATE_RULES.Before]: 'validation.dateBeforeToday',
    [DATE_RULES.OnOrBefore]: 'validation.dateOnOrBeforeToday',
  },
  [DATE_GRANULARITIES.Minute]: {
    [DATE_RULES.After]: 'validation.dateTimeAfterNow',
    [DATE_RULES.OnOrAfter]: 'validation.dateTimeOnOrAfterNow',
    [DATE_RULES.Before]: 'validation.dateTimeBeforeNow',
    [DATE_RULES.OnOrBefore]: 'validation.dateTimeOnOrBeforeNow',
  },
};

const DATE_BOUND_I18N_KEYS = {
  Min: 'validation.dateMin',
  Max: 'validation.dateMax',
} as const;

/** Rejects non-integer numbers. Empty values are left to the `required` validator. */
export function integerValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const parsed = Number(value);
  return Number.isInteger(parsed) ? null : { [FORMLY_VALIDATORS.Integer]: true };
}

/**
 * Rejects a `calendar_with_hours` answer where a ticked day is missing one of its ends.
 *
 * The control deliberately keeps a half-filled day in its value (with an empty end)
 * so this rule can see it; leaving the field empty altogether is the `required`
 * validator's business. A shift that ends before it starts is left alone — that is
 * an overnight crew, not a mistake.
 */
export function calendarWithHoursRangeValidator(control: AbstractControl): ValidationErrors | null {
  const value: unknown = control.value;
  if (!isCalendarWithHoursValue(value)) {
    return null;
  }

  const complete = (range: CalendarDayRange | undefined): boolean =>
    !!range &&
    CALENDAR_WITH_HOURS_TIME_PATTERN.test(range.from) &&
    CALENDAR_WITH_HOURS_TIME_PATTERN.test(range.to);

  return Object.values(value).every(complete)
    ? null
    : { [FORMLY_VALIDATORS.CalendarWithHoursRange]: true };
}

/**
 * Enforces a `date` / `date_time` field's own date constraint: a rule measured against the system
 * clock, narrowed by the optional fixed bounds the author set.
 *
 * The calendar already greys out the days the rule excludes, so this exists for everything the
 * calendar cannot cover — a typed date, a value restored from an earlier fill, a form left open
 * across midnight, and the time half of a `date_time`, whose day may be legal while its minute is
 * not. Mirrored on the server by `SurveyAnswerValidator`, which is the authority; this is what
 * makes the refusal visible before the crew presses submit.
 *
 * An empty field reports nothing — that is `required`'s business, as with every validator here.
 */
export function dateRuleValidator(
  control: AbstractControl,
  field: FormlyFieldConfig,
): ValidationErrors | null {
  const props = field.props ?? {};
  const rule = (props['dateRule'] as DateRule | undefined) ?? DATE_RULES.None;
  // The author's own fixed bounds, not the merged ones the calendar is narrowed to — so a message
  // naming a date names the date the author set.
  const minDate = asDate(props['dateMinBound']);
  const maxDate = asDate(props['dateMaxBound']);

  if (rule === DATE_RULES.None && !minDate && !maxDate) {
    return null;
  }

  const granularity =
    (props['dateGranularity'] as DateGranularity | undefined) ?? DATE_GRANULARITIES.Day;

  const answer = toDate(control.value);
  if (!answer) {
    return null;
  }

  const value = truncate(answer, granularity).getTime();
  const now = truncate(new Date(), granularity).getTime();

  const broken =
    (rule === DATE_RULES.After && value <= now) ||
    (rule === DATE_RULES.OnOrAfter && value < now) ||
    (rule === DATE_RULES.Before && value >= now) ||
    (rule === DATE_RULES.OnOrBefore && value > now);

  if (broken) {
    return fail({ key: DATE_RULE_I18N_KEYS[granularity][rule as Exclude<DateRule, 'none'>] });
  }

  // The fixed bounds are whole days at both granularities — a `max_date` means the end of that day,
  // so a `date_time` is compared on its calendar half alone.
  const day = startOfDay(answer).getTime();

  if (minDate && day < startOfDay(minDate).getTime()) {
    return fail({ key: DATE_BOUND_I18N_KEYS.Min, params: { date: formatDate(minDate) } });
  }

  if (maxDate && day > startOfDay(maxDate).getTime()) {
    return fail({ key: DATE_BOUND_I18N_KEYS.Max, params: { date: formatDate(maxDate) } });
  }

  return null;
}

function fail(error: DateRuleError): ValidationErrors {
  return { [FORMLY_VALIDATORS.DateRule]: error };
}

/**
 * The two shapes a constrained field holds: a `date` keeps a `Date`, a `date_time` keeps the local
 * `YYYY-MM-DD HH:mm` text it stores. Anything else is unanswered as far as this rule is concerned.
 */
function toDate(value: unknown): Date | null {
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value;
  }

  if (typeof value !== 'string' || !DATE_TIME_PATTERN.test(value)) {
    return null;
  }

  const [datePart, timePart] = value.split(' ');
  const [year, month, day] = datePart.split('-').map(Number);
  const [hour, minute] = timePart.split(':').map(Number);

  // Built from the parts rather than `new Date(string)`, which reads the value as UTC and lands on
  // the wrong side of midnight anywhere east of Greenwich.
  return new Date(year, month - 1, day, hour, minute);
}

function truncate(value: Date, granularity: DateGranularity): Date {
  return granularity === DATE_GRANULARITIES.Minute
    ? new Date(
        value.getFullYear(),
        value.getMonth(),
        value.getDate(),
        value.getHours(),
        value.getMinutes(),
      )
    : startOfDay(value);
}

function startOfDay(value: Date): Date {
  return new Date(value.getFullYear(), value.getMonth(), value.getDate());
}

function asDate(value: unknown): Date | null {
  return value instanceof Date && !Number.isNaN(value.getTime()) ? value : null;
}

/** `dd/mm/yyyy` — the mask the pickers show, so the message names the date the way the field does. */
function formatDate(value: Date): string {
  const pad = (part: number) => String(part).padStart(2, '0');
  return `${pad(value.getDate())}/${pad(value.getMonth() + 1)}/${value.getFullYear()}`;
}

/**
 * Localized Formly validators + messages.
 *
 * Registered through the `FORMLY_CONFIG` multi-provider (rather than
 * `FormlyModule.forRoot`) so the messages can resolve through Transloco at
 * render time and follow the active language.
 */
export function formlyValidationConfig(translate: TranslateService): ConfigOption {
  return {
    validators: [
      { name: FORMLY_VALIDATORS.Integer, validation: integerValidator },
      { name: FORMLY_VALIDATORS.CalendarWithHoursRange, validation: calendarWithHoursRangeValidator },
      { name: FORMLY_VALIDATORS.DateRule, validation: dateRuleValidator },
    ],
    validationMessages: [
      {
        name: ERROR_KEYS.Required,
        message: () => translate.instant(I18N_KEYS.Required),
      },
      {
        name: ERROR_KEYS.MinLength,
        message: (error: { requiredLength?: number }, field: FormlyFieldConfig) =>
          translate.instant(I18N_KEYS.MinLength, {
            min: error?.requiredLength ?? field.props?.minLength,
          }),
      },
      {
        name: ERROR_KEYS.MaxLength,
        message: (error: { requiredLength?: number }, field: FormlyFieldConfig) =>
          translate.instant(I18N_KEYS.MaxLength, {
            max: error?.requiredLength ?? field.props?.maxLength,
          }),
      },
      {
        name: ERROR_KEYS.Min,
        message: (error: { min?: number }, field: FormlyFieldConfig) =>
          translate.instant(I18N_KEYS.Min, { min: error?.min ?? field.props?.min }),
      },
      {
        name: ERROR_KEYS.Max,
        message: (error: { max?: number }, field: FormlyFieldConfig) =>
          translate.instant(I18N_KEYS.Max, { max: error?.max ?? field.props?.max }),
      },
      {
        name: ERROR_KEYS.Pattern,
        message: () => translate.instant(I18N_KEYS.Pattern),
      },
      {
        name: FORMLY_VALIDATORS.Integer,
        message: () => translate.instant(I18N_KEYS.Integer),
      },
      {
        name: FORMLY_VALIDATORS.CalendarWithHoursRange,
        message: () => translate.instant(I18N_KEYS.CalendarWithHoursRange),
      },
      {
        // The rule that was broken travels on the error itself, so one validator covers all
        // eight wordings without eight registrations.
        name: FORMLY_VALIDATORS.DateRule,
        message: (error: DateRuleError) => translate.instant(error.key, error.params),
      },
    ],
  };
}
