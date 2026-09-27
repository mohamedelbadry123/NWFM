import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthStore } from '../auth/auth.store';
import { PERMISSIONS } from '../auth/permissions';

/**
 * The pages the workflow workspace opens besides its own. The sidebar lists the same ones, so a page
 * is never in the menu but refused, or reachable but missing from the menu.
 */
export const WORKSPACE_SHARED_ROUTES: readonly string[] = [
  '/tasks',
  '/forms',
  '/lookups',
  '/admin/teams',
  '/admin/users',
  '/admin/roles',
];

const WORKSPACE_ROUTES: readonly string[] = [
  '/admin/workflow/sla-policies',
  '/admin/workflow/definitions',
  '/admin/workflow/instances',
  '/workflow/start',
  ...WORKSPACE_SHARED_ROUTES,
];

export const workflowHomeGuard: CanActivateFn = () => {
  const auth = inject(AuthStore);
  const isAdmin = auth.roles().includes('Administrator');
  const path = isAdmin || auth.hasAnyPermission(PERMISSIONS.manageDefinitions)
    ? '/admin/workflow/definitions'
    : auth.hasAnyPermission(PERMISSIONS.viewInstances)
      ? '/admin/workflow/instances'
      : auth.hasAnyPermission(PERMISSIONS.startWorkflows)
        ? '/workflow/start'
        // Field and form users have no workflow permission; their home is their own work.
        : auth.hasAnyPermission(PERMISSIONS.viewTasks)
          ? '/tasks'
          : auth.hasAnyPermission(PERMISSIONS.viewForms)
            ? '/forms'
            : '/auth/access-denied';
  return inject(Router).parseUrl(path);
};

export const workflowWorkspaceGuard: CanActivateChildFn = (_route, state) => {
  if (!environment.workflowWorkspace) return true;
  const path = state.url.split('?')[0];
  if (path === '/') return true;
  return WORKSPACE_ROUTES.some(prefix => path === prefix || path.startsWith(prefix + '/'))
    || inject(Router).parseUrl('/admin/workflow/definitions');
};
