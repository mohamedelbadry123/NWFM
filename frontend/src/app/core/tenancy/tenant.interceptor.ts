import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { TENANT_HEADER } from './tenancy.constants';
import { TenantContextService, isTenantRefusal } from './tenant-context.service';

/**
 * Adds the development tenant hint to anonymous API calls, and turns a workspace-level refusal
 * (unknown, suspended, upgrading...) into the workspace-unavailable page instead of a broken screen.
 * A signed-in call never carries the hint: the token's tenant is the only one the API accepts.
 */
export const tenantInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) {
    return next(req);
  }

  const tenancy = inject(TenantContextService);
  const router = inject(Router);
  const key = tenancy.key();
  const outgoing =
    key &&
    tenancy.fromQuery() &&
    !req.headers.has('Authorization') &&
    !req.headers.has(TENANT_HEADER)
      ? req.clone({ setHeaders: { [TENANT_HEADER]: key } })
      : req;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      const code = refusalCode(error);
      if (isTenantRefusal(code)) {
        tenancy.refusal.set(code);
        if (router.navigated) {
          void router.navigate(['/workspace-unavailable'], { queryParams: { reason: code } });
        }
      }
      return throwError(() => error);
    }),
  );
};

/** The API answers refusals as `{ error: { code } }` (tenancy) or `{ code }` (budget and selectors). */
function refusalCode(error: unknown): unknown {
  if (!(error instanceof HttpErrorResponse)) return undefined;
  const body = error.error as { code?: unknown; error?: { code?: unknown } } | null;
  return body?.error?.code ?? body?.code;
}
