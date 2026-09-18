import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NgClass } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { Checkbox } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinner } from 'primeng/progressspinner';
import { MessageModule } from 'primeng/message';
import { AuthService } from '../../../core/auth/auth.service';
import { AuthStore } from '../../../core/auth/auth.store';
import { AppContextService } from '../../../core/context/app-context.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { SsoStatus } from '../../../core/auth/auth.model';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    NgClass,
    TranslateModule,
    InputText,
    Password,
    Checkbox,
    ButtonModule,
    ProgressSpinner,
    MessageModule,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly store = inject(AuthStore);
  private readonly appContext = inject(AppContextService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly locale = inject(LocaleService);

  protected readonly submitting = signal(false);
  protected readonly redirecting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  private readonly ssoStatus = signal<SsoStatus>({ enabled: false, allowLocalLoginForAdministrators: true });

  private readonly returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
  private readonly localRequested = this.route.snapshot.queryParamMap.get('local') === '1';

  protected readonly ssoEnabled = computed(() => this.ssoStatus().enabled);
  protected readonly credentialsFormVisible = signal(this.localRequested);
  protected readonly showCredentialsForm = computed(
    () => !this.ssoEnabled() || this.credentialsFormVisible(),
  );
  protected readonly localLoginOffered = computed(
    () => !this.ssoEnabled() || this.ssoStatus().allowLocalLoginForAdministrators,
  );
  protected readonly handingOver = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required]],
    password: ['', [Validators.required]],
    rememberMe: [true],
  });

  protected get f() {
    return this.form.controls;
  }

  ngOnInit(): void {
    this.auth.getSsoStatus().subscribe({
      next: (res) => {
        if (res.isSuccess && res.value) {
          this.ssoStatus.set(res.value);
        }
      },
    });
  }

  protected toggleLanguage(): void {
    this.locale.toggle();
  }

  protected revealCredentialsForm(): void {
    this.credentialsFormVisible.set(true);
  }

  protected signInWithSso(): void {
    if (this.redirecting()) return;
    this.redirecting.set(true);
    if (this.returnUrl) sessionStorage.setItem('nwfm.sso.returnUrl', this.returnUrl);
    window.location.href = '/api/v1/auth/sso/redirect';
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { userName, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.errorMessage.set(null);

    this.auth.login(userName, password).subscribe({
      next: (response) => {
        this.submitting.set(false);
        if (response.isSuccess) {
          this.store.setSession(response);
          this.loadProfileAndNavigate();
        } else {
          this.errorMessage.set(response.error?.message || 'auth.login.error_invalid');
        }
      },
      error: () => {
        this.submitting.set(false);
        this.errorMessage.set('auth.login.error_generic');
      },
    });
  }

  private loadProfileAndNavigate(): void {
    this.auth.getProfile().subscribe({
      next: (res) => {
        if (res.isSuccess) this.store.updateProfile(res.value);
        void this.finishLogin();
      },
      error: () => void this.finishLogin(),
    });
  }

  private async finishLogin(): Promise<void> {
    await this.appContext.load();
    await this.router.navigateByUrl(this.returnUrl && this.returnUrl !== '/login' ? this.returnUrl : '/');
  }
}
