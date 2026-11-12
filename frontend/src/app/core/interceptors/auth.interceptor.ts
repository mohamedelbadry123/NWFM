import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpErrorResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, catchError, finalize, of, switchMap, tap, throwError } from 'rxjs';
import { AuthStore } from '../auth/auth.store';
import { AuthService } from '../auth/auth.service';
import { SKIP_REFRESH } from '../auth/auth-request-context';
import { Router } from '@angular/router';

let isRefreshing = false;

/**
 * After a refresh that could not keep the verified identity, load the profile of the refreshed session and apply it
 * for `epoch`. The caller waits for this before replaying the request, so nothing that completes afterwards
 * (including a startup that is itself waiting on a request) can see the session unverified because of a race.
 * A failed load leaves the identity unverified (drafts memory-only); a result for an earlier session is ignored.
 */
function verifyProfile(
  authService: AuthService,
  store: AuthStore,
  epoch: number,
): Observable<unknown> {
  return authService.getProfile({ refresh: false }).pipe(
    tap((res) => {
      if (res.isSuccess && res.value?.userId?.trim()) store.updateProfile(res.value, epoch);
    }),
    catchError(() => of(null)),
  );
}

export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
) => {
  const store = inject(AuthStore);
  const authService = inject(AuthService);
  const router = inject(Router);

  const token = store.accessToken();
  const authReq = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (
        error.status !== 401 ||
        !token ||
        req.url.includes('/auth/refresh') ||
        req.url.includes('/auth/login') ||
        req.context.get(SKIP_REFRESH)
      ) {
        return throwError(() => error);
      }
      // Another request is already renewing the session: this one fails without ending it.
      if (isRefreshing) return throwError(() => error);
      const refreshToken = store.refreshToken();
      if (!refreshToken) {
        store.clearSession();
        router.navigate(['/login']);
        return throwError(() => error);
      }

      isRefreshing = true;
      // The session this refresh belongs to; a logout or new login while it is in flight makes it stale. An
      // unverified refresh advances the epoch, and the refreshed session is the one that must stay current.
      let epoch = store.sessionEpoch();
      let stale = false;
      const current = () => !stale && store.sessionEpoch() === epoch;
      const endCurrentSession = () => {
        if (current()) {
          store.clearSession();
          router.navigate(['/login']);
        }
      };
      return authService.refresh(refreshToken).pipe(
        // The latch belongs to the refresh request itself, so it is released when that request completes, fails
        // or is cancelled (for example by a startup timeout), never left set.
        finalize(() => {
          isRefreshing = false;
        }),
        switchMap((response) => {
          if (!response.isSuccess) return throwError(() => error);
          const outcome = store.refreshSession(response, { epoch, refreshToken });
          // A stale refresh must neither install its tokens nor replay the request for another session.
          if (outcome === 'stale') {
            stale = true;
            return throwError(() => error);
          }
          const replay = () =>
            next(
              req.clone({
                setHeaders: { Authorization: `Bearer ${response.value.accessToken}` },
              }),
            );
          if (outcome === 'kept') return replay();
          epoch = store.sessionEpoch();
          return verifyProfile(authService, store, epoch).pipe(
            switchMap(() => {
              if (store.sessionEpoch() === epoch) return replay();
              stale = true;
              return throwError(() => error);
            }),
          );
        }),
        catchError((err) => {
          endCurrentSession();
          return throwError(() => err);
        }),
      );
    }),
  );
};
