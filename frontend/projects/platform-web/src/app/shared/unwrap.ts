import { ApiResult, firstErrorMessage } from './api-result.model';

export function unwrap<T>(result: ApiResult<T> | null | undefined): T {
  if (!result?.isSuccess || result.value === null || result.value === undefined) {
    throw new Error(firstErrorMessage(result) ?? 'The request failed.');
  }
  return result.value;
}

/** Reads the API envelope out of an HttpErrorResponse, or falls back to the thrown Error. */
export function apiErrorMessage(error: unknown): string {
  const body = (error as { error?: ApiResult<unknown> })?.error;
  return (
    firstErrorMessage(body) ?? (error instanceof Error ? error.message : 'The request failed.')
  );
}
