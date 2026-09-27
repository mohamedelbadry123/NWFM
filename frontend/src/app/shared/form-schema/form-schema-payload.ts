import type { RuleModel } from './form-schema-rules';
import { completeTime } from '../components/dynamic-form/time-picker.component';
import { normalizeDateTime } from '../components/dynamic-form/formly-date-time.component';
import {
  WEEKDAY_CODES,
  CALENDAR_WITH_HOURS_TIME_PATTERN,
  isCalendarWithHoursValue,
  type CalendarDayRange,
  type CalendarWithHoursValue,
} from '../components/dynamic-form/formly-preview.types';

/**
 * How a model value is rendered in the submitted payload.
 *
 * Date/time controls hold a `Date`, which `JSON.stringify` would emit as a UTC
 * instant (`2026-12-30T21:00:00.000Z` for a date picked as 31/12/2026 in
 * UTC+3). A survey answer is a calendar value, not an instant, so those keys
 * are formatted in **local** time instead.
 */
export const PREVIEW_VALUE_KINDS = {
  Date: 'date',
  Time: 'time',
  /**
   * Drawn signature — stored as a single-item file-ref array (same shape as {@link Files}).
   * Legacy full `data:image/...;base64,...` strings are still accepted on restore/submit.
   */
  Signature: 'signature',
  /** Captured files — the blob object URL is dropped, see `formatAttachments`. */
  Files: 'files',
  /** A { lat, lng, address? } point — rounded for display, see `formatGeoPoint`. */
  Geo: 'geo',
  /** YesNo fields store strings 'yes' or 'no', but DB columns are booleans. */
  YesNo: 'yes_no',
  /** A `{ sat: { from, to }, … }` weekly shift grid, see `formatCalendarWithHours`. */
  CalendarWithHours: 'calendar_with_hours',
  /** A local `YYYY-MM-DD HH:mm`, see `formly-date-time.component.ts`. */
  DateTime: 'date_time',
} as const;

export type PreviewValueKind = (typeof PREVIEW_VALUE_KINDS)[keyof typeof PREVIEW_VALUE_KINDS];

/** `data_name` -> value kind, for the keys that need special formatting. */
export type PreviewValueKinds = Readonly<Record<string, PreviewValueKind>>;

/** Decimal places kept for latitude/longitude (~11 cm). */
const COORDINATE_PRECISION = 6;
/** Mirrors `SurveyGeolocation.MaxAddressLength` on the server, which truncates past this. */
const MAX_ADDRESS_LENGTH = 250;
const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Build the JSON-ready payload for the live preview and the submit result. */
export function toPreviewPayload(model: RuleModel, kinds: PreviewValueKinds): Record<string, unknown> {
  const payload: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(model)) {
    payload[key] = formatValue(value, kinds[key]);
  }
  return payload;
}

/**
 * The inverse of {@link toPreviewPayload}: turns stored answers back into control values so a
 * repeat fill opens on what was submitted last time.
 *
 * The round trip is not symmetric, because the store is not a mirror of the model:
 *
 * - Date/time columns come back as ADO timestamps (`2026-01-05T00:00:00`, `08:30:00`), so they are
 *   re-parsed as local calendar values rather than fed to `new Date()` as instants.
 * - Arrays and objects live in `NVARCHAR` columns as raw JSON text, so they are parsed back.
 * - Signatures restore as file-ref arrays (or a full legacy data URL). Truncated labels from older
 *   clients (`data:image/png;base64,… (11.3 KB)`) are skipped — they are not images.
 *
 * Keys absent from `answers` keep whatever default the definition gave them.
 */
export function fromStoredAnswers(
  answers: Record<string, unknown>,
  kinds: PreviewValueKinds,
): Record<string, unknown> {
  const model: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(answers)) {
    if (value === null || value === undefined) {
      continue;
    }

    if (kinds[key] === PREVIEW_VALUE_KINDS.Signature && isTruncatedSignatureLabel(value)) {
      continue;
    }

    const restored = restoreValue(value, kinds[key]);
    if (restored !== undefined) {
      model[key] = restored;
    }
  }

  return model;
}

function restoreValue(value: unknown, kind: PreviewValueKind | undefined): unknown {
  if (kind === PREVIEW_VALUE_KINDS.Date) {
    return typeof value === 'string' ? parseLocalDate(value) : value;
  }
  if (kind === PREVIEW_VALUE_KINDS.Time) {
    // The control holds `HH:mm` text, so an ADO `08:30:00` is trimmed rather than made a Date.
    return typeof value === 'string' ? completeTime(value) : value;
  }
  if (kind === PREVIEW_VALUE_KINDS.YesNo) {
    if (typeof value === 'boolean') {
      return value ? 'yes' : 'no';
    }
    return value;
  }
  if (kind === PREVIEW_VALUE_KINDS.CalendarWithHours) {
    // Stored as raw JSON text in an NVARCHAR column, so it comes back as a string.
    return parseJson(value);
  }
  if (kind === PREVIEW_VALUE_KINDS.DateTime) {
    // A DATETIME2 reads back as `2026-08-13T14:30:00`; the control holds `2026-08-13 14:30`.
    return typeof value === 'string' ? normalizeDateTime(value) : value;
  }
  if (
    kind === PREVIEW_VALUE_KINDS.Geo ||
    kind === PREVIEW_VALUE_KINDS.Files ||
    kind === PREVIEW_VALUE_KINDS.Signature
  ) {
    // A legacy signature may still be a full data URL string rather than a file-ref array.
    if (
      kind === PREVIEW_VALUE_KINDS.Signature &&
      typeof value === 'string' &&
      value.startsWith('data:image/') &&
      !isTruncatedSignatureLabel(value)
    ) {
      return value;
    }
    return parseJson(value);
  }

  // An untyped key can still hold a JSON array — a multi-choice answer is stored that way.
  return typeof value === 'string' && looksLikeJsonArray(value) ? parseJson(value) : value;
}

/** Values already parsed by the HTTP layer pass straight through. */
function parseJson(value: unknown): unknown {
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}

function looksLikeJsonArray(value: string): boolean {
  const trimmed = value.trimStart();
  return trimmed.startsWith('[');
}

function formatValue(value: unknown, kind: PreviewValueKind | undefined): unknown {
  if (kind === PREVIEW_VALUE_KINDS.Signature || kind === PREVIEW_VALUE_KINDS.Files) {
    return formatAttachments(value);
  }
  if (kind === PREVIEW_VALUE_KINDS.Geo) {
    return formatGeoPoint(value);
  }
  if (kind === PREVIEW_VALUE_KINDS.CalendarWithHours) {
    return formatCalendarWithHours(value);
  }
  if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
    return value;
  }
  return kind === PREVIEW_VALUE_KINDS.Time ? toLocalTime(value) : toLocalDate(value);
}

/**
 * Keep the server-side reference (`fileId` + `path`) and drop the blob object
 * URL — that is a browser-session handle, meaningless once submitted. The file
 * itself was uploaded when it was picked; this array is all the submission row
 * stores. In the designer preview nothing is uploaded, so `fileId` / `path` are
 * simply absent.
 */
function formatAttachments(value: unknown): unknown {
  if (!Array.isArray(value)) {
    return value;
  }
  return value.map((item) => {
    if (item === null || typeof item !== 'object') {
      return item;
    }
    const { fileId, path, name, size, type } = item as {
      fileId?: string;
      path?: string;
      name?: string;
      size?: number;
      type?: string;
    };
    return fileId ? { fileId, path, name, size, type } : { name, size, type };
  });
}

/**
 * Emit the weekly shift grid in a fixed shape: days in {@link WEEKDAY_CODES} order, and
 * only the ones carrying both a start and an end.
 *
 * The control keeps a half-filled day in its value so the range validator can report on
 * it, and the mobile app posts whatever its own grid held — dropping incomplete days here
 * is what keeps one shape in the column whichever client submitted. `null` when nothing
 * survives, matching an unanswered field.
 */
function formatCalendarWithHours(value: unknown): unknown {
  if (!isCalendarWithHoursValue(value)) {
    return value;
  }

  const complete = (range: CalendarDayRange | undefined): range is CalendarDayRange =>
    !!range &&
    CALENDAR_WITH_HOURS_TIME_PATTERN.test(range.from) &&
    CALENDAR_WITH_HOURS_TIME_PATTERN.test(range.to);

  const result: Record<string, CalendarDayRange> = {};
  for (const day of WEEKDAY_CODES) {
    const range = (value as CalendarWithHoursValue)[day];
    if (complete(range)) {
      result[day] = { from: range.from, to: range.to };
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

/** Older web clients stored a truncated data-URL label — not a recoverable image. */
function isTruncatedSignatureLabel(value: unknown): boolean {
  return typeof value === 'string' && value.startsWith('data:') && value.includes('…');
}

/** `YYYY-MM-DD` in the user's own timezone. */
export function toLocalDate(value: Date): string {
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

/** `HH:mm` in the user's own timezone. */
export function toLocalTime(value: Date): string {
  return `${pad(value.getHours())}:${pad(value.getMinutes())}`;
}

/** Parse `YYYY-MM-DD` as a *local* midnight (`new Date(string)` would treat it as UTC). */
export function parseLocalDate(raw: string): Date | undefined {
  const parts = DATE_ONLY_PATTERN.exec(raw);
  if (parts) {
    return new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]));
  }
  return asValidDate(new Date(raw));
}

function asValidDate(value: Date): Date | undefined {
  return Number.isNaN(value.getTime()) ? undefined : value;
}

/**
 * Trim map precision to ~11 cm so the payload does not carry float noise, and carry the address the
 * picker resolved. The address key is omitted rather than sent as `null` when there is none — a
 * form default and a mobile fill both post a point without one, and the server treats it as
 * optional either way.
 */
function formatGeoPoint(value: unknown): unknown {
  if (value === null || typeof value !== 'object') {
    return value;
  }
  const { lat, lng, address } = value as { lat?: unknown; lng?: unknown; address?: unknown };
  if (typeof lat !== 'number' || typeof lng !== 'number') {
    return value;
  }

  const point = { lat: round(lat, COORDINATE_PRECISION), lng: round(lng, COORDINATE_PRECISION) };
  const trimmed = typeof address === 'string' ? address.trim() : '';

  return trimmed ? { ...point, address: trimmed.slice(0, MAX_ADDRESS_LENGTH) } : point;
}

function round(value: number, precision: number): number {
  return Number(value.toFixed(precision));
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
