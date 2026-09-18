import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from '../auth/auth.store';

export const authGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  const router = inject(Router);
  if (store.isAuthenticated()) return true;
  router.navigate(['/login']);
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
    if (store.roles().includes('Administrator')) return true;
    if (store.hasAnyPermission(...permissions)) return true;
    router.navigate(['/auth/access-denied']);
    return false;
  };
};
