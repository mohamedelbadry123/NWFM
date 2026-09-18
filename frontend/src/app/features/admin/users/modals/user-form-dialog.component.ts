import { Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { MessageService } from 'primeng/api';
import { PasswordModule } from 'primeng/password';
import { AdminService } from '../../../../core/admin/admin.service';
import { UserListItem, UsersService } from '../../../../core/users/users.service';

@Component({
  selector: 'app-user-form-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, TranslateModule, ButtonModule, DialogModule, InputTextModule, MultiSelectModule, PasswordModule],
  templateUrl: './user-form-dialog.component.html',
})
export class UserFormDialogComponent {
  readonly visible = model.required<boolean>();
  readonly user = input<UserListItem | null>(null);
  readonly saved = output<void>();

  private readonly usersApi = inject(UsersService);
  private readonly adminApi = inject(AdminService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly roles = signal<{ label: string; value: string }[]>([]);
  protected readonly isEdit = computed(() => !!this.user()?.id);

  protected readonly form = this.fb.group({
    userName: this.fb.control('', [Validators.required, Validators.maxLength(256)]),
    email: this.fb.control('', [Validators.email, Validators.maxLength(256)]),
    phoneNumber: this.fb.control('', Validators.maxLength(30)),
    password: this.fb.control('', [Validators.minLength(6)]),
    roles: this.fb.control<string[]>([]),
  });

  constructor() {
    effect(() => {
      if (!this.visible()) return;
      this.adminApi.getRoles().subscribe({
        next: (res) => {
          const items = res.value ?? [];
          this.roles.set(items.map(r => ({ label: r.name, value: r.name })));
        },
      });
      const user = this.user();
      if (user) {
        this.form.reset({
          userName: user.userName,
          email: user.email ?? '',
          phoneNumber: '',
          password: '',
          roles: [...(user.roles ?? [])],
        });
        this.form.controls.userName.disable();
        this.form.controls.password.clearValidators();
      } else {
        this.form.reset({ userName: '', email: '', phoneNumber: '', password: '', roles: [] });
        this.form.controls.userName.enable();
        this.form.controls.password.setValidators([Validators.required, Validators.minLength(6)]);
      }
      this.form.controls.password.updateValueAndValidity();
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    this.saving.set(true);
    const user = this.user();
    const request = user
      ? this.usersApi.update(user.id, { email: raw.email, phoneNumber: raw.phoneNumber, roles: raw.roles ?? [] })
      : this.usersApi.create({
          userName: raw.userName!,
          email: raw.email,
          phoneNumber: raw.phoneNumber,
          password: raw.password!,
          roles: raw.roles ?? [],
        });

    request.subscribe({
      next: (res) => {
        this.saving.set(false);
        if (res.isSuccess) {
          this.visible.set(false);
          this.saved.emit();
        } else {
          this.messages.add({ severity: 'error', summary: res.error?.message ?? this.translate.instant('error.generic') });
        }
      },
      error: () => this.saving.set(false),
    });
  }
}
