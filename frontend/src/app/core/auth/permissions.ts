import { Directive, Input, TemplateRef, ViewContainerRef, inject } from '@angular/core';
import { AuthStore } from './auth.store';

@Directive({ selector: '[hasPermission]', standalone: true })
export class HasPermissionDirective {
  private readonly store = inject(AuthStore);
  private readonly template = inject(TemplateRef<any>);
  private readonly container = inject(ViewContainerRef);
  private isVisible = false;

  @Input() set hasPermission(permission: string | string[]) {
    const perms = Array.isArray(permission) ? permission : [permission];
    const hasAccess = this.store.hasAnyPermission(...perms) ||
      this.store.roles().includes('Administrator');

    if (hasAccess && !this.isVisible) {
      this.container.createEmbeddedView(this.template);
      this.isVisible = true;
    } else if (!hasAccess && this.isVisible) {
      this.container.clear();
      this.isVisible = false;
    }
  }
}
