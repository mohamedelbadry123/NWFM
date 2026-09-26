import { Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { finalize } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { MessageService } from 'primeng/api';
import { PasswordModule } from 'primeng/password';
import { ToastModule } from 'primeng/toast';
import { ROLES } from '../../../../core/auth/permissions';
import { UserListItem, UsersService } from '../../../../core/users/users.service';
import { OrgScopeSelectorComponent } from '../../../../shared/components/org-scope/org-scope-selector.component';
import { OrgScopeAssignment } from '../../../../shared/components/org-scope/org-scope.model';

/**
 * Creates and edits a back-office login. Username is fixed after creation.
 * FieldTeam is filtered from the picker and rejected by the API.
 */
@Component({
  selector: 'app-user-form-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MultiSelectModule,
    PasswordModule,
    ToastModule,
    OrgScopeSelectorComponent,
  ],
  providers: [MessageService],
  templateUrl: './user-form-dialog.component.html',
})
export class UserFormDialogComponent {
  readonly visible = model.required<boolean>();
  readonly user = input<UserListItem | null>(null);
  readonly saved = output<void>();

  private readonly usersApi = inject(UsersService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly loading = signal(false);
  protected readonly roles = signal<{ label: string; value: string }[]>([]);
  protected readonly scopes = signal<OrgScopeAssignment[]>([]);
  protected readonly initialScopes = signal<readonly OrgScopeAssignment[]>([]);
  protected readonly isEdit = computed(() => !!this.user()?.id);

  protected readonly form = this.fb.group({
    userName: this.fb.control('', [Validators.required, Validators.maxLength(256)]),
    email: this.fb.control('', [Validators.email, Validators.maxLength(256)]),
    phoneNumber: this.fb.control('', Validators.maxLength(30)),
    password: this.fb.control(''),
    roles: this.fb.control<string[]>([]),
  });

  constructor() {
    effect(() => {
      const passwordControl = this.form.controls.password;
      if (this.isEdit()) {
        passwordControl.clearValidators();
        this.form.controls.userName.disable();
      } else {
        passwordControl.setValidators([Validators.required, Validators.minLength(6)]);
        this.form.controls.userName.enable();
      }
      passwordControl.updateValueAndValidity({ emitEvent: false });
    });
  }

  protected onShow(): void {
    this.loadRoles();

    const user = this.user();
    if (!user?.id) {
      this.form.reset({ roles: [] });
      this.scopes.set([]);
      this.initialScopes.set([]);
      return;
    }

    this.form.reset({
      userName: user.userName ?? '',
      email: user.email ?? '',
      phoneNumber: user.phoneNumber ?? '',
      password: '',
      roles: [...(user.roles ?? [])],
    });

    this.loading.set(true);
    this.usersApi.get(user.id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: res => {
          const detail = res.value;
          if (detail) {
            this.form.patchValue({
              email: detail.email ?? '',
              phoneNumber: detail.phoneNumber ?? '',
              roles: [...(detail.roles ?? [])],
            });
          }
          const loaded = detail?.scopes ?? [];
          this.scopes.set([...loaded]);
          this.initialScopes.set([...loaded]);
        },
        error: () => {
          this.scopes.set([]);
          this.initialScopes.set([]);
        },
      });
  }

  protected onScopesChange(scopes: OrgScopeAssignment[]): void {
    this.scopes.set(scopes);
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const scopes = this.scopes();
    this.saving.set(true);

    const request = this.isEdit()
      ? this.usersApi.update(this.user()!.id, {
          email: raw.email?.trim() || undefined,
          phoneNumber: raw.phoneNumber?.trim() || undefined,
          roles: raw.roles ?? [],
          scopes,
        })
      : this.usersApi.create({
          userName: raw.userName!.trim(),
          email: raw.email?.trim() || undefined,
          phoneNumber: raw.phoneNumber?.trim() || undefined,
          password: raw.password!,
          roles: raw.roles ?? [],
          scopes,
        });

    const successKey = this.isEdit() ? 'users.updatedSuccess' : 'users.createdSuccess';
    const errorKey = this.isEdit() ? 'users.updateError' : 'users.createError';

    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: res => {
        if (res.isSuccess) {
          this.messages.add({
            severity: 'success',
            summary: this.translate.instant('common.success'),
            detail: this.translate.instant(successKey),
          });
          this.visible.set(false);
          this.saved.emit();
        } else {
          this.messages.add({
            severity: 'error',
            summary: this.translate.instant('common.error'),
            detail: res.error?.message ?? this.translate.instant(errorKey),
          });
        }
      },
      error: () => {
        this.messages.add({
          severity: 'error',
          summary: this.translate.instant('common.error'),
          detail: this.translate.instant(errorKey),
        });
      },
    });
  }

  protected cancel(): void {
    this.visible.set(false);
  }

  private loadRoles(): void {
    this.usersApi.getAssignableRoles().subscribe({
      next: res =>
        this.roles.set(
          (res.value ?? [])
            .filter(name => !!name && name !== ROLES.fieldTeam)
            .map(name => ({ label: name, value: name })),
        ),
      error: () => this.roles.set([]),
    });
  }
}
