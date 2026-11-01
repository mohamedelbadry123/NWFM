import { Component, effect, inject, model, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  CreatePlatformTenantResult,
  PlatformApiService,
  PlatformPlan,
} from '../../../core/api/platform-api.service';
import { HelpTipComponent } from '../help-tip.component';

/** Same shape the API's `TenantKey.IsValid` accepts: lowercase, starts with a letter, single hyphens. */
const TENANT_KEY_PATTERN = /^[a-z](?:[a-z0-9]|-(?=[a-z0-9]))*$/;

/**
 * Staff create a workspace by hand. The API runs it through the customer signup pipeline, so the
 * result appears on the signups page while it provisions and the administrator gets the usual
 * set-password e-mail. Managed database only.
 */
@Component({
  selector: 'app-platform-tenant-create-dialog',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Dialog,
    InputText,
    Message,
    Select,
    HelpTipComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t('platform.tenants.create.title')"
        [style]="{ width: '40rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <p-message severity="info" styleClass="mb-3">{{
          t('platform.tenants.create.hint')
        }}</p-message>
        <form [formGroup]="form" (ngSubmit)="save()" class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label for="createOrg">{{ t('signup.organizationName') }}</label>
            <input pInputText id="createOrg" formControlName="organizationName" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="createKey"
              >{{ t('signup.tenantKey') }}<app-help-tip [text]="t('platform.tips.tenantKey')"
            /></label>
            <input pInputText id="createKey" formControlName="tenantKey" autocomplete="off" />
            @if (form.controls.tenantKey.touched && form.controls.tenantKey.invalid) {
              <small class="text-red-500">{{ t('platform.tenants.create.keyRule') }}</small>
            }
          </div>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div class="flex flex-col gap-1">
              <label>{{ t('signup.plan') }}<app-help-tip [text]="t('platform.tips.plan')" /></label>
              <p-select
                formControlName="planCode"
                [options]="plans()"
                optionLabel="code"
                optionValue="code"
                [loading]="loadingPlans()"
                [placeholder]="t('platform.tenants.create.defaultPlan')"
                [showClear]="true"
                appendTo="body"
                styleClass="w-full"
              />
            </div>
            <div class="flex flex-col gap-1">
              <label
                >{{ t('signup.language') }}<app-help-tip [text]="t('platform.tips.language')"
              /></label>
              <p-select
                formControlName="language"
                [options]="languages"
                optionValue="value"
                appendTo="body"
                styleClass="w-full"
              >
                <ng-template #selectedItem let-item>{{ t(item.labelKey) }}</ng-template>
                <ng-template #item let-item>{{ t(item.labelKey) }}</ng-template>
              </p-select>
            </div>
          </div>
          <div class="flex flex-col gap-1">
            <label for="createAdmin">{{ t('signup.adminFullName') }}</label>
            <input pInputText id="createAdmin" formControlName="adminFullName" />
          </div>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div class="flex flex-col gap-1">
              <label for="createEmail">{{ t('signup.adminEmail') }}</label>
              <input pInputText id="createEmail" type="email" formControlName="adminEmail" />
            </div>
            <div class="flex flex-col gap-1">
              <label for="createPhone">{{ t('signup.adminPhone') }}</label>
              <input pInputText id="createPhone" formControlName="adminPhone" />
            </div>
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
            [label]="t('platform.tenants.create.submit')"
            icon="pi pi-check"
            [loading]="saving()"
            [disabled]="saving()"
            (onClick)="save()"
          />
        </ng-template>
      </p-dialog>
    </ng-container>
  `,
})
export class PlatformTenantCreateDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly created = output<CreatePlatformTenantResult>();

  protected readonly plans = signal<PlatformPlan[]>([]);
  protected readonly loadingPlans = signal(false);
  protected readonly saving = signal(false);
  protected readonly languages = [
    { value: 'en', labelKey: 'common.english' },
    { value: 'ar', labelKey: 'common.arabic' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    organizationName: ['', [Validators.required, Validators.maxLength(200)]],
    tenantKey: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(32),
        Validators.pattern(TENANT_KEY_PATTERN),
      ],
    ],
    planCode: [null as string | null],
    language: ['en'],
    adminFullName: ['', [Validators.required, Validators.maxLength(200)]],
    adminEmail: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    adminPhone: ['', Validators.maxLength(30)],
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.open());
      }
    });
  }

  protected save(): void {
    if (this.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .createTenant({
        ...value,
        tenantKey: value.tenantKey.trim().toLowerCase(),
        adminPhone: value.adminPhone || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (result) => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.tenants.create.queued', {
              key: result.tenantKey,
            }),
          });
          this.visible.set(false);
          this.created.emit(result);
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private open(): void {
    this.form.reset({
      organizationName: '',
      tenantKey: '',
      planCode: null,
      language: 'en',
      adminFullName: '',
      adminEmail: '',
      adminPhone: '',
    });
    this.loadingPlans.set(true);
    this.api
      .getPlans()
      .pipe(finalize(() => this.loadingPlans.set(false)))
      .subscribe({ next: (plans) => this.plans.set(plans.filter((plan) => plan.isActive)) });
  }
}
