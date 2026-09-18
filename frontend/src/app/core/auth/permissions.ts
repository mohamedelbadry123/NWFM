import { Directive, Input, TemplateRef, ViewContainerRef, inject } from '@angular/core';
import { AuthStore } from './auth.store';

export const ADMINISTRATOR_ROLE = 'Administrator';

export const PERMISSIONS = {
  manageLookups: 'ManageLookups',
  manageUsers: 'ManageUsers',
  manageRolePermissions: 'CanManageRolePermissions',
} as const;

@Directive({ selector: '[hasPermission]', standalone: true })
export class HasPermissionDirective {
  private readonly store = inject(AuthStore);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private isVisible = false;

  @Input() set hasPermission(permission: string | string[]) {
    const perms = Array.isArray(permission) ? permission : [permission];
    const hasAccess = this.store.hasAnyPermission(...perms) ||
      this.store.roles().includes(ADMINISTRATOR_ROLE);

    if (hasAccess && !this.isVisible) {
      this.container.createEmbeddedView(this.template);
      this.isVisible = true;
    } else if (!hasAccess && this.isVisible) {
      this.container.clear();
      this.isVisible = false;
    }
  }
}

