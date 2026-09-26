import { Component, inject, input, model, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { PasswordModule } from 'primeng/password';
import { MessageService } from 'primeng/api';
import { UserListItem, UsersService } from '../../../../core/users/users.service';

@Component({
  selector: 'app-user-reset-password-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, TranslateModule, ButtonModule, DialogModule, PasswordModule],
  templateUrl: './user-reset-password-dialog.component.html',
})
export class UserResetPasswordDialogComponent {
  readonly visible = model.required<boolean>();
  readonly user = input<UserListItem | null>(null);

  private readonly usersApi = inject(UsersService);
  private readonly messages = inject(MessageService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);

  protected readonly saving = signal(false);
  protected readonly form = this.fb.group({
    newPassword: this.fb.control('', [Validators.required, Validators.minLength(6)]),
  });

  protected save(): void {
    const user = this.user();
    if (!user || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.usersApi.resetPassword(user.id, this.form.controls.newPassword.value!).subscribe({
      next: (res) => {
        this.saving.set(false);
        if (res.isSuccess) {
          this.visible.set(false);
          this.form.reset();
          this.messages.add({ severity: 'success', summary: this.translate.instant('users.passwordReset') });
        } else {
          this.messages.add({ severity: 'error', summary: res.error?.message });
        }
      },
      error: () => this.saving.set(false),
    });
  }
}
