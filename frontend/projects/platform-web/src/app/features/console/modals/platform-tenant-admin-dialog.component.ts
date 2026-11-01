import { Component, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PlatformApiService,
  PlatformTenantDetail,
  TenantAdminCredentialsResult,
} from '../../../core/api/platform-api.service';

/** Adds another Administrator to the workspace; the API e-mails a temporary password and a set-password link. */
@Component({
  selector: 'app-platform-tenant-admin-dialog',
  imports: [ReactiveFormsModule, I18nDirective, ButtonModule, Dialog, InputText],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t('platform.admins.addTitle')"
        [style]="{ width: '32rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <form [formGroup]="form" class="flex flex-col gap-3" (ngSubmit)="save()">
          <p class="muted m-0">{{ t('platform.admins.addHint') }}</p>
          <div class="flex flex-col gap-1">
            <label for="adminEmail">{{ t('platform.admins.email') }}</label>
            <input pInputText id="adminEmail" type="email" formControlName="email" dir="ltr" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="adminPhone">{{ t('platform.admins.phone') }}</label>
            <input pInputText id="adminPhone" formControlName="phoneNumber" dir="ltr" />
          </div>
        </form>
        <ng-template #footer>
          <p-button
            [label]="t('common.cancel')"
            [outlined]="true"
            [disabled]="saving()"
            (onClick)="visible.set(false)"
          />
          <p-button
            [label]="t('platform.admins.add')"
            icon="pi pi-user-plus"
            [loading]="saving()"
            [disabled]="saving() || form.invalid"
            (onClick)="save()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformTenantAdminDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();
  readonly saved = output<TenantAdminCredentialsResult>();

  protected readonly saving = signal(false);
  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.maxLength(30)]],
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.form.reset());
      }
    });
  }

  protected save(): void {
    if (this.saving() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, phoneNumber } = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .createTenantAdmin(this.tenant().id, {
        email: email.trim(),
        phoneNumber: phoneNumber.trim() || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (result) => {
          this.visible.set(false);
          this.saved.emit(result);
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
