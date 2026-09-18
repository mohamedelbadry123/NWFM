import { Component, inject, signal, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { finalize } from 'rxjs';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, TranslateModule, InputTextModule, PasswordModule, ButtonModule, MessageModule],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-ink-50 px-4">
      <div class="w-full max-w-md rounded-2xl border border-ink-200 bg-white p-8 shadow-lg">
        <div class="mb-6 text-center">
          <h1 class="text-2xl font-bold text-primary">NWFM</h1>
          <h2 class="mt-2 text-xl font-semibold text-ink-900">{{ 'auth.login.title' | translate }}</h2>
          <p class="mt-1 text-sm text-ink-500">{{ 'auth.login.subtitle' | translate }}</p>
        </div>

        @if (errorMessage()) {
          <p-message severity="error" [text]="errorMessage()!" styleClass="mb-4 w-full" />
        }

        <form (ngSubmit)="onLogin()" class="space-y-5">
          <div>
            <label for="username" class="prv-label">{{ 'auth.login.username' | translate }}</label>
            <input pInputText id="username" [(ngModel)]="userName" name="userName"
              [placeholder]="'auth.login.username_placeholder' | translate"
              class="w-full" autocomplete="username" />
          </div>

          <div>
            <label for="password" class="prv-label">{{ 'auth.login.password' | translate }}</label>
            <p-password id="password" [(ngModel)]="password" name="password"
              [placeholder]="'auth.login.password_placeholder' | translate"
              [feedback]="false" [toggleMask]="true"
              styleClass="w-full" inputStyleClass="w-full" />
          </div>

          <p-button type="submit" [label]="loading() ? ('auth.login.signing_in' | translate) : ('auth.login.submit' | translate)"
            [loading]="loading()" [disabled]="loading() || !userName || !password"
            styleClass="w-full" />
        </form>

        @if (ssoEnabled()) {
          <div class="mt-4">
            <div class="my-4 flex items-center gap-3">
              <div class="h-px flex-1 bg-ink-200"></div>
              <span class="text-xs text-ink-400">OR</span>
              <div class="h-px flex-1 bg-ink-200"></div>
            </div>
            <p-button [label]="'auth.login.sso_btn' | translate" severity="secondary"
              styleClass="w-full" (onClick)="onSsoLogin()" [disabled]="loading()" />
          </div>
        }
      </div>
    </div>
  `
})
export class LoginComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);

  userName = '';
  password = '';
  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly ssoEnabled = signal(false);

  ngOnInit(): void {
    this.authService.getSsoStatus().subscribe({
      next: (res) => {
        if (res.isSuccess && res.value.enabled) {
          this.ssoEnabled.set(true);
        }
      }
    });
  }

  onLogin(): void {
    if (!this.userName || !this.password) return;
    this.loading.set(true);
    this.errorMessage.set(null);

    this.authService.login(this.userName, this.password)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (response) => {
          if (response.isSuccess) {
            this.authStore.setSession(response);
            this.loadProfileAndNavigate();
          } else {
            this.errorMessage.set(response.error?.message || 'auth.login.error_invalid');
          }
        },
        error: () => {
          this.errorMessage.set('auth.login.error_generic');
        }
      });
  }

  onSsoLogin(): void {
    window.location.href = '/api/v1/auth/sso/redirect';
  }

  private loadProfileAndNavigate(): void {
    this.authService.getProfile().subscribe({
      next: (res) => {
        if (res.isSuccess) {
          this.authStore.updateProfile(res.value);
        }
        this.router.navigate(['/']);
      },
      error: () => {
        this.router.navigate(['/']);
      }
    });
  }
}
