/** Map workflow API failures to i18n `error.{Code}` keys. */
export function workflowApiErrorKey(err: unknown, fallbackKey = 'error.generic'): string {
  const e = err as {
    status?: number;
    title?: string;
    detail?: string;
    error?: {
      code?: string;
      Code?: string;
      detail?: string;
      title?: string;
      message?: string;
      Message?: string;
    };
  };
  const nested = e?.error;
  const code =
    nested?.code
    ?? nested?.Code
    ?? (typeof e?.title === 'string' && e.title.startsWith('Workflow.') ? e.title : undefined);
  if (code) return `error.${code}`;
  if (e?.status === 409) return 'error.Workflow.WorkItem.ConcurrencyConflict';
  if (e?.status === 403) return 'error.forbidden';
  if (e?.title === 'VALIDATION_ERROR') return 'error.VALIDATION_ERROR';
  return fallbackKey;
}

export function workflowApiErrorStatus(err: unknown): number | undefined {
  return (err as { status?: number })?.status;
}

/** Pull the first human-readable validation / API message from common error shapes. */
export function workflowApiErrorDetail(err: unknown): string | undefined {
  const body = (err as { error?: Record<string, unknown> })?.error
    ?? (err as Record<string, unknown>);
  if (!body || typeof body !== 'object') return undefined;

  const fromList = firstMessageFromErrors(body['errors']);
  if (fromList) return fromList;

  const detail = (err as { detail?: unknown })?.detail
    ?? body['detail']
    ?? body['message']
    ?? body['Message'];
  if (typeof detail === 'string' && detail.trim()) return detail.trim();
  return undefined;
}

function firstMessageFromErrors(errors: unknown): string | undefined {
  if (Array.isArray(errors) && errors.length > 0) {
    const first = errors[0] as { message?: string; Message?: string };
    const msg = first?.message ?? first?.Message;
    if (typeof msg === 'string' && msg.trim()) return msg.trim();
  }

  // ASP.NET model-state: { "Code": ["The Code field is required."] }
  if (errors && typeof errors === 'object' && !Array.isArray(errors)) {
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(value) && value.length > 0 && typeof value[0] === 'string' && value[0].trim()) {
        return value[0].trim();
      }
      if (typeof value === 'string' && value.trim()) return value.trim();
    }
  }
  return undefined;
}

export function workflowApiErrorMessage(
  err: unknown,
  translate: { instant: (key: string) => string },
  fallbackKey = 'error.generic',
): string {
  const key = workflowApiErrorKey(err, fallbackKey);
  const translated = translate.instant(key);
  const detail = workflowApiErrorDetail(err);

  // Prefer a concrete server message over a generic "unexpected error" / bare validation title.
  const isGenericKey = key === 'error.generic' || key === fallbackKey || key === 'error.VALIDATION_ERROR';
  if (detail && isGenericKey) return detail;

  if (translated && translated !== key) return translated;
  if (detail) return detail;
  return translate.instant(fallbackKey);
}
