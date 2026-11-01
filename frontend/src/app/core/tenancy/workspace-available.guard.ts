import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantContextService } from './tenant-context.service';

/**
 * Sends the user to the workspace-unavailable page when the API already refused this workspace at
 * startup (unknown address, suspended, being upgraded), rather than showing a sign-in form that
 * cannot work.
 */
export const workspaceAvailableGuard: CanActivateFn = () => {
  const refusal = inject(TenantContextService).refusal();
  return refusal
    ? inject(Router).createUrlTree(['/workspace-unavailable'], { queryParams: { reason: refusal } })
    : true;
};
