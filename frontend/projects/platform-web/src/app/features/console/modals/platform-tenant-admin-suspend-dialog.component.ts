import { Component, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PlatformApiService,
  PlatformTenantDetail,
  TenantAdministrator,
} from '../../../core/api/platform-api.service';

/** Suspends one workspace administrator, with a reason for the audit log. */
@Component({
  selector: 'app-platform-tenant-admin-suspend-dialog',
  imports: [FormsModule, I18nDirective, ButtonModule, Dialog, TextareaModule],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t('platform.admins.suspendTitle')"
        [style]="{ width: '32rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <div class="flex flex-col gap-3">
          <p class="m-0">
            <strong dir="ltr">{{ admin()?.userName }}</strong>
          </p>
          <p class="muted m-0">{{ t('platform.admins.suspendHint') }}</p>
          <div class="flex flex-col gap-1">
            <label for="suspendReason">{{ t('platform.admins.reason') }}</label>
            <textarea
              pTextarea
              id="suspendReason"
              [(ngModel)]="reason"
              rows="2"
              class="w-full"
            ></textarea>
          </div>
        </div>
        <ng-template #footer>
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="saving()"
            (onClick)="visible.set(false)"
          />
          <p-button
            [label]="t('platform.admins.suspend')"
            icon="pi pi-ban"
            severity="danger"
            [loading]="saving()"
            [disabled]="saving() || !reason.trim()"
            (onClick)="suspend()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformTenantAdminSuspendDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();
  readonly admin = input<TenantAdministrator | null>(null);
  readonly saved = output<TenantAdministrator>();

  protected readonly saving = signal(false);
  protected reason = '';

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => (this.reason = ''));
      }
    });
  }

  protected suspend(): void {
    const admin = this.admin();
    if (!admin || this.saving() || !this.reason.trim()) {
      return;
    }
    this.saving.set(true);
    this.api
      .changeTenantAdminStatus(this.tenant().id, admin.userId, false, this.reason.trim())
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (updated) => {
          this.visible.set(false);
          this.saved.emit(updated);
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }
}
