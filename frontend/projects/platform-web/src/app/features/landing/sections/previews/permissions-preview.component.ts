import { Component } from '@angular/core';
import { I18nDirective } from '@platform/shared';

/**
 * Feature-tile drawing: the Role permissions screen with one role picked — each permission
 * switched on or off for it.
 */
@Component({
  selector: 'app-permissions-preview',
  imports: [I18nDirective],
  template: `
    <ng-container *appI18n="let t">
      <div class="roles">
        @for (role of roles; track role) {
          <span class="role" [class.active]="role === selectedRole">{{
            t('landing.features.permissions.roles.' + role)
          }}</span>
        }
      </div>
      <div class="perms">
        @for (perm of perms; track perm.key) {
          <div class="perm">
            <span class="label">{{ t('landing.features.permissions.perms.' + perm.key) }}</span>
            <span class="switch" [class.on]="perm.granted"><span></span></span>
          </div>
        }
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 0.8rem;
    }

    .roles {
      display: flex;
      flex-wrap: wrap;
      gap: 0.4rem;
    }

    .role {
      padding: 0.33rem 0.73rem;
      border: 1px solid var(--app-border);
      border-radius: 999px;
      background: var(--app-surface);
      font-size: 0.85rem;
      font-weight: 600;
      color: var(--p-text-muted-color);

      &.active {
        border-color: var(--p-primary-color);
        background: var(--acc-brand-bg);
        color: var(--acc-brand-fg);
      }
    }

    .perms {
      display: flex;
      flex-direction: column;
      gap: 0.6rem;
      padding: 0.87rem;
      background: var(--app-surface);
      border: 1px solid var(--app-border);
      border-radius: 0.8rem;
    }

    .perm {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.8rem;
      min-width: 0;
    }

    .label {
      min-width: 0;
      font-size: 0.93rem;
      font-weight: 500;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    // A toggle drawn in CSS; the knob sits at the inline end when on, so it mirrors in Arabic.
    .switch {
      display: flex;
      flex-shrink: 0;
      width: 2.27rem;
      height: 1.27rem;
      padding: 0.17rem;
      border-radius: 999px;
      background: var(--app-hover);

      span {
        width: 0.93rem;
        height: 0.93rem;
        border-radius: 50%;
        background: #ffffff;
        box-shadow: var(--app-shadow-sm);
      }

      &.on {
        justify-content: flex-end;
        background: var(--p-primary-color);
      }
    }
  `,
})
export class PermissionsPreviewComponent {
  protected readonly roles = ['admin', 'supervisor', 'monitor'];
  protected readonly selectedRole = 'supervisor';

  /** What the selected role (Supervisor) is granted. */
  protected readonly perms = [
    { key: 'design', granted: false },
    { key: 'review', granted: true },
    { key: 'monitor', granted: true },
    { key: 'teams', granted: true },
  ];
}
