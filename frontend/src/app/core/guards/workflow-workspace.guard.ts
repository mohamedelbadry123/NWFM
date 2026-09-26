import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthStore } from '../auth/auth.store';
import { PERMISSIONS } from '../auth/permissions';

export const workflowHomeGuard: CanActivateFn = () => {
  const auth = inject(AuthStore);
  const path = auth.roles().includes('Administrator') || auth.hasAnyPermission(PERMISSIONS.manageDefinitions)
    ? '/admin/workflow/definitions' : auth.hasAnyPermission(PERMISSIONS.viewInstances)
      ? '/admin/workflow/instances' : auth.hasAnyPermission(PERMISSIONS.startWorkflows) ? '/workflow/start' : '/auth/access-denied';
  return inject(Router).parseUrl(path);
};

export const workflowWorkspaceGuard: CanActivateChildFn = (_route, state) => {
  if (!environment.workflowWorkspace) return true;
  const path = state.url.split('?')[0];
  if (path === '/') return true;
  const visible = ['/admin/workflow/sla-policies','/admin/workflow/definitions', '/admin/workflow/instances', '/workflow/start', '/lookups', '/admin/users', '/admin/roles'];
  return visible.some(prefix => path === prefix || path.startsWith(prefix + '/')) || inject(Router).parseUrl('/admin/workflow/definitions');
};
