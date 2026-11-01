import { Component, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import { PlatformApiService, PlatformTenantDetail } from '../../../core/api/platform-api.service';
import { HelpTipComponent } from '../help-tip.component';

/** Profile, integrations, SSO and extra hosts of one workspace (`PUT /platform/tenants/{id}`). */
@Component({
  selector: 'app-platform-tenant-edit-dialog',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Checkbox,
    Dialog,
    InputText,
    Select,
    TextareaModule,
    HelpTipComponent,
  ],
  template: `
    <ng-container *appI18n="let t">
      <p-dialog
        [visible]="visible()"
        (visibleChange)="visible.set($event)"
        [modal]="true"
        [closable]="!saving()"
        [header]="t('platform.tenants.edit.title')"
        [style]="{ width: '46rem' }"
        [breakpoints]="{ '768px': '95vw' }"
      >
        <form [formGroup]="form" (ngSubmit)="save()" class="flex flex-col gap-3">
          <h4 class="m-0">{{ t('platform.tenants.edit.profile') }}</h4>
          <div class="flex flex-col gap-1">
            <label for="editName">{{ t('platform.tenants.name') }}</label>
            <input pInputText id="editName" formControlName="name" />
          </div>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div class="flex flex-col gap-1">
              <label for="editContact">{{ t('platform.tenants.edit.contactName') }}</label>
              <input pInputText id="editContact" formControlName="contactName" />
            </div>
            <div class="flex flex-col gap-1">
              <label for="editEmail"
                >{{ t('platform.tenants.edit.contactEmail')
                }}<app-help-tip [text]="t('platform.tips.contactEmail')"
              /></label>
              <input pInputText id="editEmail" type="email" formControlName="contactEmail" />
            </div>
            <div class="flex flex-col gap-1">
              <label for="editPhone">{{ t('platform.tenants.edit.contactPhone') }}</label>
              <input pInputText id="editPhone" formControlName="contactPhone" />
            </div>
          </div>
          <div class="flex flex-col gap-1">
            <label
              >{{ t('platform.tenants.edit.language')
              }}<app-help-tip [text]="t('platform.tips.language')"
            /></label>
            <p-select
              formControlName="defaultLanguage"
              [options]="languages"
              optionValue="value"
              appendTo="body"
              styleClass="w-full"
            >
              <ng-template #selectedItem let-item>{{ t(item.labelKey) }}</ng-template>
              <ng-template #item let-item>{{ t(item.labelKey) }}</ng-template>
            </p-select>
          </div>

          <label class="flex items-center gap-2">
            <p-checkbox formControlName="listedPublicly" [binary]="true" />
            {{ t('platform.tenants.edit.listedPublicly') }}
            <app-help-tip [text]="t('platform.tips.listedPublicly')" />
          </label>

          <h4 class="m-0 mt-2">
            {{ t('platform.tenants.edit.sso') }}<app-help-tip [text]="t('platform.tips.sso')" />
          </h4>
          <label class="flex items-center gap-2">
            <p-checkbox formControlName="ssoEnabled" [binary]="true" />
            {{ t('platform.tenants.edit.ssoEnabled') }}
          </label>
          @if (form.controls.ssoEnabled.value) {
            <div class="flex flex-col gap-1">
              <label for="editSsoEntity">{{ t('platform.tenants.edit.ssoEntityId') }}</label>
              <input pInputText id="editSsoEntity" formControlName="ssoEntityId" />
            </div>
            <div class="flex flex-col gap-1">
              <label for="editSsoIdp">{{ t('platform.tenants.edit.ssoIdpEntityId') }}</label>
              <input pInputText id="editSsoIdp" formControlName="ssoIdentityProviderEntityId" />
            </div>
            <div class="flex flex-col gap-1">
              <label for="editSsoMeta">{{ t('platform.tenants.edit.ssoMetadataUrl') }}</label>
              <input pInputText id="editSsoMeta" formControlName="ssoMetadataUrl" />
            </div>
          }

          <h4 class="m-0 mt-2">
            {{ t('platform.tenants.edit.hosts') }}<app-help-tip [text]="t('platform.tips.hosts')" />
          </h4>
          <div class="flex flex-col gap-1">
            <textarea pTextarea id="editHosts" formControlName="hosts" rows="3"></textarea>
            <small class="text-muted-color">{{ t('platform.tenants.edit.hostsHint') }}</small>
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
            [label]="t('common.save')"
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
export class PlatformTenantEditDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  readonly tenant = input.required<PlatformTenantDetail>();
  readonly saved = output<PlatformTenantDetail>();

  protected readonly saving = signal(false);
  protected readonly languages = [
    { value: 'en', labelKey: 'common.english' },
    { value: 'ar', labelKey: 'common.arabic' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    contactName: [''],
    contactEmail: ['', [Validators.required, Validators.email]],
    contactPhone: [''],
    defaultLanguage: ['en'],
    ssoEnabled: [false],
    listedPublicly: [false],
    ssoEntityId: [''],
    ssoIdentityProviderEntityId: [''],
    ssoMetadataUrl: [''],
    hosts: [''],
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.fill(this.tenant()));
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
      .updateTenant(this.tenant().id, {
        ...value,
        contactPhone: value.contactPhone || null,
        ssoEntityId: value.ssoEntityId || null,
        ssoIdentityProviderEntityId: value.ssoIdentityProviderEntityId || null,
        ssoMetadataUrl: value.ssoMetadataUrl || null,
        listedPublicly: value.listedPublicly,
        hosts: value.hosts
          .split(/[\s,]+/)
          .map((host) => host.trim())
          .filter((host) => host.length > 0),
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (tenant) => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.tenants.edit.saved'),
          });
          this.visible.set(false);
          this.saved.emit(tenant);
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private fill(tenant: PlatformTenantDetail): void {
    this.form.reset({
      name: tenant.name,
      contactName: tenant.contactName ?? '',
      contactEmail: tenant.contactEmail,
      contactPhone: tenant.contactPhone ?? '',
      defaultLanguage: tenant.settings.defaultLanguage,
      ssoEnabled: tenant.settings.ssoEnabled,
      listedPublicly: tenant.settings.listedPublicly,
      ssoEntityId: tenant.settings.ssoEntityId ?? '',
      ssoIdentityProviderEntityId: tenant.settings.ssoIdentityProviderEntityId ?? '',
      ssoMetadataUrl: tenant.settings.ssoMetadataUrl ?? '',
      hosts: tenant.hosts.join('\n'),
    });
  }
}
