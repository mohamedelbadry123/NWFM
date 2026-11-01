import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { PlatformAuthStore } from '../auth/platform-auth.store';

const PLATFORM_API = '/api/v1/platform/';
const PLATFORM_LOGIN = '/platform/auth/login';

/**
 * Attaches the staff token to console calls. Public signup calls go out anonymous, and so does the
 * login itself.
 */
export const platformAuthInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.includes(PLATFORM_API) || req.url.includes(PLATFORM_LOGIN)) {
    return next(req);
  }

  const token = inject(PlatformAuthStore).currentAccessToken;
  return token ? next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })) : next(req);
};
