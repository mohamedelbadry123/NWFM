import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from '../auth/auth.store';
import { ADMINISTRATOR_ROLE } from '../auth/permissions';

export const authGuard: CanActivateFn = (route, state) => {
  const store = inject(AuthStore);
  const router = inject(Router);
  if (store.isAuthenticated()) return true;
  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

export const guestGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  const router = inject(Router);
  if (!store.isAuthenticated()) return true;
  router.navigate(['/']);
  return false;
};

export const permissionGuard = (...permissions: string[]): CanActivateFn => {
  return () => {
    const store = inject(AuthStore);
    const router = inject(Router);
    if (store.roles().includes(ADMINISTRATOR_ROLE)) return true;
    if (store.hasAnyPermission(...permissions)) return true;
    router.navigate(['/auth/access-denied']);
    return false;
  };
};
