import {
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateService } from '@ngx-translate/core';
import { I18nDirective, apiErrorMessage } from '@platform/shared';
import { ButtonModule } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { Dialog } from 'primeng/dialog';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { Select } from 'primeng/select';
import { MessageService } from 'primeng/api';
import { finalize } from 'rxjs';
import {
  PLATFORM_ROLES,
  PlatformApiService,
  PlatformUser,
} from '../../../core/api/platform-api.service';
import { HelpTipComponent } from '../help-tip.component';

/** Mirrors `SavePlatformUserCommand.PasswordMinLength` on the API. */
const PASSWORD_MIN_LENGTH = 12;

/**
 * Creates or edits an NWFM staff account. E-mail is fixed once created; an empty password on edit
 * keeps the current one.
 */
@Component({
  selector: 'app-platform-user-dialog',
  imports: [
    ReactiveFormsModule,
    I18nDirective,
    ButtonModule,
    Checkbox,
    Dialog,
    InputText,
    Password,
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
        [header]="t(isNew() ? 'platform.users.add' : 'platform.users.edit')"
        [style]="{ width: '34rem' }"
        [breakpoints]="{ '640px': '95vw' }"
      >
        <form [formGroup]="form" (ngSubmit)="save()" class="flex flex-col gap-3">
          <div class="flex flex-col gap-1">
            <label for="puEmail">{{ t('platform.login.email') }}</label>
            <input
              pInputText
              id="puEmail"
              type="email"
              formControlName="email"
              autocomplete="off"
            />
          </div>
          <div class="flex flex-col gap-1">
            <label for="puName">{{ t('platform.users.displayName') }}</label>
            <input pInputText id="puName" formControlName="displayName" />
          </div>
          <div class="flex flex-col gap-1">
            <label
              >{{ t('platform.users.role') }}<app-help-tip [text]="t('platform.tips.staffRole')"
            /></label>
            <p-select
              formControlName="role"
              [options]="roles"
              optionValue="value"
              appendTo="body"
              styleClass="w-full"
            >
              <ng-template #selectedItem let-item>{{ t(item.labelKey) }}</ng-template>
              <ng-template #item let-item>{{ t(item.labelKey) }}</ng-template>
            </p-select>
          </div>
          <div class="flex flex-col gap-1">
            <label for="puPassword">{{ t('platform.login.password') }}</label>
            <p-password
              inputId="puPassword"
              formControlName="password"
              [feedback]="false"
              [toggleMask]="true"
              autocomplete="new-password"
              styleClass="w-full"
              inputStyleClass="w-full"
            />
            <small class="text-muted-color">
              {{
                t(isNew() ? 'platform.users.passwordHint' : 'platform.users.passwordKeep', {
                  min: passwordMinLength,
                })
              }}
            </small>
          </div>
          <div class="flex items-center gap-2">
            <p-checkbox inputId="puActive" formControlName="isActive" [binary]="true" />
            <label for="puActive">{{ t('platform.users.active') }}</label>
            <app-help-tip [text]="t('platform.tips.staffActive')" />
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
export class PlatformUserDialogComponent {
  private readonly api = inject(PlatformApiService);
  private readonly fb = inject(FormBuilder);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);

  readonly visible = model(false);
  /** Null creates a new user. */
  readonly user = input<PlatformUser | null>(null);
  readonly saved = output<void>();

  protected readonly saving = signal(false);
  protected readonly isNew = computed(() => this.user() === null);
  protected readonly passwordMinLength = PASSWORD_MIN_LENGTH;
  protected readonly roles = [
    { value: PLATFORM_ROLES.support, labelKey: 'platform.users.roles.PlatformSupport' },
    { value: PLATFORM_ROLES.admin, labelKey: 'platform.users.roles.PlatformAdmin' },
  ];

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    role: [PLATFORM_ROLES.support as string],
    password: [''],
    isActive: [true],
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.fill(this.user()));
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
      .saveUser({
        id: this.user()?.id ?? null,
        email: this.user()?.email ?? value.email.trim(),
        displayName: value.displayName.trim(),
        role: value.role,
        isActive: value.isActive,
        password: value.password || null,
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant('platform.users.saved'),
          });
          this.visible.set(false);
          this.saved.emit();
        },
        error: (error: unknown) =>
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: apiErrorMessage(error),
          }),
      });
  }

  private fill(user: PlatformUser | null): void {
    const password = this.form.controls.password;
    password.setValidators(
      user
        ? [Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(128)]
        : [
            Validators.required,
            Validators.minLength(PASSWORD_MIN_LENGTH),
            Validators.maxLength(128),
          ],
    );
    this.form.reset({
      email: user?.email ?? '',
      displayName: user?.displayName ?? '',
      role: user?.role ?? PLATFORM_ROLES.support,
      password: '',
      isActive: user?.isActive ?? true,
    });
    if (user) {
      this.form.controls.email.disable();
    } else {
      this.form.controls.email.enable();
    }
  }
}
