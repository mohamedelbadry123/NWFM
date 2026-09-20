import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthStore } from '../auth/auth.store';
import { AuthService } from '../auth/auth.service';
import { Router } from '@angular/router';

let isRefreshing = false;

export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const store = inject(AuthStore);
  const authService = inject(AuthService);
  const router = inject(Router);

  const token = store.accessToken();
  const authReq = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && token && !req.url.includes('/auth/refresh') && !req.url.includes('/auth/login')) {
        if (!isRefreshing) {
          isRefreshing = true;
          const refreshToken = store.refreshToken();
          if (refreshToken) {
            return authService.refresh(refreshToken).pipe(
              switchMap(response => {
                isRefreshing = false;
                if (response.isSuccess) {
                  store.setSession(response);
                  const retryReq = req.clone({ setHeaders: { Authorization: `Bearer ${response.value.accessToken}` } });
                  return next(retryReq);
                }
                store.clearSession();
                router.navigate(['/login']);
                return throwError(() => error);
              }),
              catchError(err => {
                isRefreshing = false;
                store.clearSession();
                router.navigate(['/login']);
                return throwError(() => err);
              })
            );
          }
        }
        store.clearSession();
        router.navigate(['/login']);
      }
      return throwError(() => error);
    })
  );
};
