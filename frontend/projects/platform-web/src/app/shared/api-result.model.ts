/**
 * Mirrors the backend NWFM.Shared.Results.Result<T> envelope: a success carries `value`, a
 * failure carries `error` with a stable code and a human-readable message.
 */
export interface ApiError {
  readonly code: string | null;
  readonly message: string | null;
}

export interface ApiResult<T> {
  readonly isSuccess: boolean;
  readonly isFailure?: boolean;
  readonly value: T | null;
  readonly error: ApiError | null;
}

export function firstErrorMessage(result: ApiResult<unknown> | null | undefined): string | null {
  return result?.error?.message || null;
}
