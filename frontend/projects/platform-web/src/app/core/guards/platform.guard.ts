import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PlatformAuthStore } from '../auth/platform-auth.store';

export const platformGuard: CanActivateFn = (_route, state) => {
  const store = inject(PlatformAuthStore);
  const router = inject(Router);

  if (store.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/console/login'], { queryParams: { returnUrl: state.url } });
};

/** Admin-only platform pages. Support staff are sent back to the tenant list rather than a refused API call. */
export const platformAdminGuard: CanActivateFn = () => {
  const store = inject(PlatformAuthStore);
  const router = inject(Router);

  return store.isAdmin() ? true : router.createUrlTree(['/console/tenants']);
};

export const platformGuestGuard: CanActivateFn = () => {
  const store = inject(PlatformAuthStore);
  const router = inject(Router);

  if (!store.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/console/tenants']);
};
