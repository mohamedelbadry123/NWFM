import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { PlatformAuthStore } from '../auth/platform-auth.store';

const PLATFORM_API = '/api/v1/platform/';
const PLATFORM_LOGIN = '/platform/auth/login';
const CONSOLE_LOGIN = '/console/login';

/** A staff token the API no longer accepts ends the console session; there is no refresh token. */
export const platformErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const store = inject(PlatformAuthStore);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (
        error.status === 401 &&
        req.url.includes(PLATFORM_API) &&
        !req.url.includes(PLATFORM_LOGIN)
      ) {
        store.clear();
        void router.navigate([CONSOLE_LOGIN]);
      }
      return throwError(() => error);
    }),
  );
};
