import type { FormlyFieldConfig } from '@ngx-formly/core';

/**
 * Per-field locking for a server-mandated value (a workflow Step's read-only mapped value).
 *
 * The server compares the submitted answer with the mapped value as typed JSON, key by key, after trimming and
 * ignoring case. A control can only show a *display* form of such a value (a Yes/No control holds `'yes'`, a
 * blank control cannot hold `null`), so the exact value is kept beside the controls and written into the
 * payload verbatim — never round-tripped through a control.
 */
export const LOCKED_FIELD_CLASS = 'dynamic-field--locked';

export const normalizeFieldKey = (key: string) => key.trim().toLowerCase();

/** The literal key of a field: a plain string, or the single segment of a dotted name bound as [name]. */
export function fieldKeyName(field: FormlyFieldConfig): string | undefined {
  if (typeof field.key === 'string') return field.key;
  return Array.isArray(field.key) && field.key.length === 1 && typeof field.key[0] === 'string'
    ? field.key[0]
    : undefined;
}

/** `normalized key` → the key exactly as the server gave it. */
export function lockedKeyIndex(
  locked: Record<string, unknown> | null | undefined,
): Map<string, string> {
  const index = new Map<string, string>();
  for (const key of Object.keys(locked ?? {})) index.set(normalizeFieldKey(key), key);
  return index;
}

/** Disables and visually locks every field whose key is a locked key, in place, at any depth. */
export function lockMatchingFields(
  fields: FormlyFieldConfig[],
  index: ReadonlyMap<string, string>,
): void {
  for (const field of fields) {
    const name = fieldKeyName(field);
    if (name !== undefined && index.has(normalizeFieldKey(name))) lockOne(field);
    if (field.fieldGroup) lockMatchingFields(field.fieldGroup, index);
  }
}

/** One locked field: disabled, not required (the value is mandated), no stale validation, marked for CSS and AT. */
export function lockOne(field: FormlyFieldConfig): void {
  field.props = {
    ...field.props,
    disabled: true,
    readonly: true,
    required: false,
    attributes: { ...field.props?.attributes, 'aria-readonly': 'true' },
  };
  field.validators = undefined;
  field.asyncValidators = undefined;
  field.validation = undefined;
  field.className = [field.className, LOCKED_FIELD_CLASS].filter(Boolean).join(' ');
}

/**
 * Puts each locked value into a submission payload exactly as given. An answer the controls produced under the
 * same normalised key is replaced under ITS spelling, so the payload never carries two spellings of one key
 * (the server rejects duplicates after normalisation).
 */
export function withLockedValues(
  payload: Record<string, unknown>,
  locked: Record<string, unknown> | null | undefined,
): Record<string, unknown> {
  if (!locked) return payload;
  const result = { ...payload };
  for (const [key, value] of Object.entries(locked)) {
    const normalized = normalizeFieldKey(key);
    const existing = Object.keys(result).filter((k) => normalizeFieldKey(k) === normalized);
    for (const k of existing) delete result[k];
    result[existing[0] ?? key] = value;
  }
  return result;
}
