import { HttpErrorResponse } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { ApiResult } from '../api/api-result';

/**
 * The message to show when a FormEngine call fails.
 *
 * A translated message wins when one exists for the error code, so a publish refused by a data-name
 * conflict can read properly in Arabic. Otherwise the server's own message is used: it names the
 * field or the form, which is more useful than a generic failure.
 */
export function formEngineErrorMessage(
  error: unknown,
  translate: TranslateService,
  fallbackKey = 'common.error',
): string {
  const body = error instanceof HttpErrorResponse ? error.error : error;
  const result = body as ApiResult<unknown> | { code?: string; message?: string } | null | undefined;

  const code = (result as ApiResult<unknown>)?.error?.code ?? (result as { code?: string })?.code;
  const message = (result as ApiResult<unknown>)?.error?.message ?? (result as { message?: string })?.message;

  if (code) {
    const key = `error.${code}`;
    const translated = translate.instant(key);

    if (translated !== key) {
      return translated;
    }
  }

  return message || translate.instant(fallbackKey);
}
