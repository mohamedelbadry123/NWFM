import { FORM_STATUSES, FormStatus } from '../../core/form-engine/form-engine.models';

/**
 * How a form's lifecycle reads on screen. The API stores and returns the uppercase status strings;
 * the helpers here compare case-insensitively so neither side can quietly break the other.
 */
export const FORM_STATUS_VALUES: readonly FormStatus[] = [
  FORM_STATUSES.draft,
  FORM_STATUSES.published,
  FORM_STATUSES.deprecated,
  FORM_STATUSES.archived,
];

/**
 * The grid's status filter: the four statuses plus two views that are not statuses themselves.
 * `Active` is the default — everything except archived — so retired forms stay out of the way until
 * someone asks for them. The sentinels are lowercase so they can never collide with a status.
 */
export const FormStatusFilter = {
  Active: 'active',
  All: 'all',
} as const;

export type FormStatusFilterValue =
  | (typeof FormStatusFilter)[keyof typeof FormStatusFilter]
  | FormStatus;

export type FormTagSeverity = 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast';

/** Normalizes whatever the row carries so casing can never decide a guard. */
function normalize(status?: string): string {
  return (status ?? '').trim().toUpperCase();
}

/** Colour of the status tag: live is green, in-design blue, retiring amber, retired grey. */
export function formStatusSeverity(status?: string): FormTagSeverity {
  switch (normalize(status)) {
    case FORM_STATUSES.published:
      return 'success';
    case FORM_STATUSES.draft:
      return 'info';
    case FORM_STATUSES.deprecated:
      return 'warn';
    default:
      return 'secondary';
  }
}

/** Translation key for the status label, so the tag is not raw uppercase in either language. */
export function formStatusLabelKey(status?: string): string {
  const value = normalize(status);

  return FORM_STATUS_VALUES.includes(value as FormStatus)
    ? `forms.statuses.${value.toLowerCase()}`
    : 'forms.statuses.unknown';
}

/** Translation key for a status-filter option, sentinels included. */
export function formStatusFilterLabelKey(value: FormStatusFilterValue): string {
  return value === FormStatusFilter.Active || value === FormStatusFilter.All
    ? `forms.statusFilter.${value}`
    : formStatusLabelKey(value);
}

/**
 * Only a draft can be published: the domain refuses a deprecated or archived form, and a published
 * one has nothing new to freeze until it is edited back into a draft.
 */
export function canPublish(status?: string): boolean {
  return normalize(status) === FORM_STATUSES.draft;
}

/** Deprecating accepts a published form and nothing else. */
export function canDeprecate(status?: string): boolean {
  return normalize(status) === FORM_STATUSES.published;
}

/**
 * Archiving is offered only after deprecation, keeping the lifecycle to a single path
 * (Published → Deprecated → Archived). The domain would allow archiving a draft outright; the UI
 * deliberately does not, so a form is never retired without first being taken out of service.
 */
export function canArchive(status?: string): boolean {
  return normalize(status) === FORM_STATUSES.deprecated;
}

/** A form can be filled while it has a published version and has not been retired. */
export function canFill(status?: string, currentVersionNo?: number | null): boolean {
  const value = normalize(status);

  return currentVersionNo != null
    && value !== FORM_STATUSES.deprecated
    && value !== FORM_STATUSES.archived;
}
