import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { ButtonModule } from 'primeng/button';
import { PasswordModule } from 'primeng/password';
import { AuthService } from '../../../core/auth/auth.service';

/** What a second query parameter becomes when an HTML-encoded e-mail link (`&amp;`) is pasted as-is. */
const HTML_ENCODED_PREFIX = 'amp;';

const passwordsMatch = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirmPassword')?.value
    ? null
    : { mismatch: true };

/**
 * Where a new workspace's administrator lands from the activation e-mail: the link carries a
 * single-use set-password token, the workspace comes from the host. The account has no password
 * until this page sets one.
 */
@Component({
  selector: 'app-activate',
  standalone: true,
  imports: [ReactiveFormsModule, TranslateModule, ButtonModule, PasswordModule],
  templateUrl: './activate.component.html',
  styleUrl: '../access-denied/access-denied.component.scss',
})
export class ActivateComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  private readonly userId = this.param('uid');
  private readonly token = this.param('token');

  protected readonly linkValid = !!this.userId && !!this.token;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  protected submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.auth.activate(this.userId, this.token, this.form.getRawValue().newPassword).subscribe({
      next: () => void this.router.navigate(['/login'], { queryParams: { activated: 1 } }),
      error: (failure: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(failure.error?.error?.message ?? null);
      },
    });
  }

  private param(name: string): string {
    const params = this.route.snapshot.queryParamMap;
    return params.get(name) ?? params.get(HTML_ENCODED_PREFIX + name) ?? '';
  }
}
